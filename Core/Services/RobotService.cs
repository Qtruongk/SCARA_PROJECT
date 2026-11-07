using System;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using Test_1.Models;
using Test_1.Utilities;
using TsRemoteLib;

namespace Test_1.Services
{
    public class RobotService
    {
        private TsRemoteS _robot;
        private readonly LoggingService _logger;
        // Protect the two feedback reads from a concurrent disconnect/reconnect.
        // Move* and STOP use the driver's own transport lock and remain independent.
        private readonly object _feedbackLifecycle = new object();
        
        public RobotState State { get; private set; }

        public event Action StateChanged;

        // Tool Offset (TCP) - Giai phap 1: Quan ly toa do cong cu tren Robot Controller
        public bool IsToolActive { get; private set; } = true;
        public double ToolOffsetX { get; private set; } = 119.0;
        public double ToolOffsetY { get; private set; } = 0.0;
        public double ToolOffsetZ { get; private set; } = -20.0;
        public double ToolOffsetC { get; private set; } = 0.0;
        public string ToolName { get; private set; } = "TOOL1";

        public event Action ToolStateChanged;

        public RobotService(LoggingService logger)
        {
            _logger = logger;
            State = new RobotState();
        }

        public bool Connect(string ip = Constants.DefaultRobotIP, int port = Constants.DefaultRobotPort, int srcPort = Constants.DefaultSourcePort)
        {
            lock (_feedbackLifecycle)
            {
                return ConnectCore(ip, port, srcPort);
            }
        }

        private bool ConnectCore(string ip, int port, int srcPort)
        {
            try
            {
                if (_robot == null)
                {
                    _robot = new TsRemoteS();
                    _logger.Log("TsRemoteS instance created.");
                }

                _logger.Log($"Setting IP: {ip}, Port: {port}, SrcPort: {srcPort} (openmode=1:Client) ...");
                bool isIpSet = _robot.SetIPaddr(1, ip, port, srcPort);

                if (!isIpSet)
                {
                    _logger.Log("\u2718 SetIPaddr failed. Cannot proceed with connection.");
                    return false;
                }

                _logger.Log("Connecting via TCP/IP (openmode=Client, robot must be E00=1 server) ...");
                bool isConnected = _robot.Connect(1);

                if (isConnected)
                {
                    State.IsConnected = true;
                    _logger.Log("\u2714 Connected successfully to robot controller.");
                    ReapplyToolSettingsIfConnected();
                    NotifyStateChanged();
                    return true;
                }
                else
                {
                    State.IsConnected = false;
                    _logger.Log("\u2718 Connection failed. Check IP address and controller status.");
                    NotifyStateChanged();
                    return false;
                }
            }
            catch (TsRemoteSException ex)
            {
                _logger.Log($"Connection error (TsRemote): {ex.errorCode} - {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Log($"Connection error (General): {ex.GetType().Name} - {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            lock (_feedbackLifecycle)
            {
                DisconnectCore();
            }
        }

        private void DisconnectCore()
        {
            if (!State.IsConnected) return;

            try
            {
                _robot?.Disconnect();
                State.IsConnected = false;
                State.IsServoOn = false;
                State.IsWatchdogRunning = false;
                _logger.Log("Disconnected from robot controller.");
                NotifyStateChanged();
            }
            catch (TsRemoteSException ex)
            {
                _logger.Log($"Disconnect error: {ex.errorCode}");
            }
        }

        public void TurnServoOn()
        {
            if (!State.IsConnected || _robot == null)
            {
                _logger.Log("Cannot turn Servo ON: Not connected.");
                return;
            }

            try
            {
                _logger.Log("--- Servo ON Sequence ---");
                _logger.Log("[1] Reading controller status...");
                
                TsStatusAllS status = _robot.GetStatusAll();
                LogControllerStatus(status);

                if (HasActiveAlarms(status))
                {
                    _logger.Log("[2] Resetting alarms...");
                    try
                    {
                        _robot.ResetALARM();
                        _logger.Log("    Alarms reset OK.");
                        Thread.Sleep(500);
                    }
                    catch (TsRemoteSException ex)
                    {
                        _logger.Log($"    Alarm reset error: {ex.errorCode}");
                    }
                }
                else
                {
                    _logger.Log("[2] No active alarms.");
                }

                if (status.EmergencySwitch == 1)
                {
                    _logger.Log("\u2718 EMERGENCY STOP is active! Release E-Stop on controller/teach pendant first.");
                    State.IsEmergencyStopActive = true;
                    NotifyStateChanged();
                    return;
                }
                State.IsEmergencyStopActive = false;

                _logger.Log("[4] Re-reading status after preparation...");
                status = _robot.GetStatusAll();
                LogControllerStatus(status);

                _logger.Log("[5] Sending ServoOn command...");
                _robot.ServoOn();
                Thread.Sleep(500);

                status = _robot.GetStatusAll();
                if (status.ServoStatus == 1)
                {
                    State.IsServoOn = true;
                    _logger.Log("\u2714 \u25B6 Servo is ON! Controller LED should be lit.");
                }
                else
                {
                    _logger.Log("\u26A0 ServoOn command sent but ServoStatus still OFF. Check controller.");
                    State.IsServoOn = false;
                }
                
                NotifyStateChanged();
            }
            catch (TsRemoteSException ex)
            {
                _logger.Log($"Servo ON error: {ex.errorCode} - {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Log($"Servo ON error (General): {ex.GetType().Name} - {ex.Message}");
            }
        }

        public void TurnServoOff()
        {
            if (!State.IsConnected || _robot == null)
            {
                _logger.Log("Cannot turn Servo OFF: Not connected.");
                return;
            }

            try
            {
                _robot.ServoOff();
                State.IsServoOn = false;
                _logger.Log("\u25A0 Servo turned OFF.");
                NotifyStateChanged();
            }
            catch (TsRemoteSException ex)
            {
                _logger.Log($"Servo OFF error: {ex.errorCode}");
            }
        }

        public void StartWatchdog()
        {
            lock (_feedbackLifecycle)
            {
                StartWatchdogCore();
            }
        }

        private void StartWatchdogCore()
        {
            if (!State.IsConnected || _robot == null)
            {
                _logger.Log("Cannot start Watchdog: Not connected.");
                return;
            }

            if (State.IsWatchdogRunning)
            {
                _logger.Log("Watchdog is already running.");
                return;
            }

            try
            {
                _robot.WatchDogStart(Constants.WatchdogIntervalMs, Constants.WatchdogTimeoutMs, 0, new TsRemoteS.TSStatusEvent(OnWatchdogStatusEvent));
                State.IsWatchdogRunning = true;
                _logger.Log($"\uD83D\uDC41 Watchdog monitoring started (interval: {Constants.WatchdogIntervalMs}ms, timeout: {Constants.WatchdogTimeoutMs}ms).");
                NotifyStateChanged();
            }
            catch (TsRemoteSException ex)
            {
                _logger.Log($"Watchdog error: {ex.errorCode}");
            }
        }

        private void OnWatchdogStatusEvent(TsStatusMonitor para)
        {
            bool stateModified = false;

            bool servoFromWatchdog = (para.ServoStatus == 1);
            if (servoFromWatchdog != State.IsServoOn)
            {
                State.IsServoOn = servoFromWatchdog;
                _logger.Log(State.IsServoOn ? "\u25B6 Servo status changed: ON" : "\u25A0 Servo status changed: OFF");
                stateModified = true;
            }

            if (para.EmergencyStop == 1 && !State.IsEmergencyStopActive)
            {
                _logger.Log("\u26A0 EMERGENCY STOP detected!");
                State.IsEmergencyStopActive = true;
                stateModified = true;
            }
            else if (para.EmergencyStop == 0 && State.IsEmergencyStopActive)
            {
                State.IsEmergencyStopActive = false;
                stateModified = true;
            }

            if (para.SafetyStop == 1)
            {
                _logger.Log("\u26A0 Safety Stop detected!");
            }

            if (para.WatchDogErr == 1)
            {
                _logger.Log("\u26A0 Watchdog communication error!");
                State.IsWatchdogRunning = false;
                stateModified = true;
            }

            if (stateModified)
            {
                NotifyStateChanged();
            }
        }

        public RobotPositionData GetCurrentPosition()
        {
            lock (_feedbackLifecycle)
            {
                return ReadCurrentPosition();
            }
        }

        private RobotPositionData ReadCurrentPosition()
        {
            if (!State.IsConnected || _robot == null) return null;

            try
            {
                return new RobotPositionData
                {
                    JointPosition = _robot.GetPsnFbkJoint(),
                    WorldPosition = _robot.GetPsnFbkWorld()
                };
            }
            catch
            {
                return null;
            }
        }

        public void EnsureWatchdogRunning()
        {
            if (State.IsConnected && !State.IsWatchdogRunning)
            {
                StartWatchdog();
            }
        }

        private static bool IsFinite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        private TsPointS BuildConfiguredWorldPoint(double x, double y, double z, double c, int? preferredConfig = null)
        {
            TsPointS cur = null;
            lock (_feedbackLifecycle)
            {
                if (State.IsConnected && _robot != null)
                {
                    try { cur = _robot.GetPsnFbkWorld(); } catch { }
                }
            }

            ConfigS config = ConfigS.LEFTY;
            double tVal = 0.0;

            if (cur != null)
            {
                if (cur.Config == ConfigS.LEFTY || cur.Config == ConfigS.RIGHTY)
                {
                    config = cur.Config;
                }
                tVal = cur.T;
            }
            else if (preferredConfig.HasValue)
            {
                ConfigS pref = (ConfigS)preferredConfig.Value;
                if (pref == ConfigS.LEFTY || pref == ConfigS.RIGHTY)
                {
                    config = pref;
                }
            }

            return new TsPointS
            {
                X = x,
                Y = y,
                Z = z,
                C = c,
                T = tVal,
                Config = config
            };
        }

        /// <summary>
        /// Kiểm tra tọa độ (X, Y) có nằm trong vùng bán kính an toàn của SCARA THL300 hay không
        /// (tự động tính bù chiều dài tay gắp nếu Tool TCP đang bật).
        /// </summary>
        public bool CheckWorkspaceRadius(double x, double y, out double radiusMm, out double minAllowedMm, out double maxAllowedMm)
        {
            radiusMm = Math.Sqrt(x * x + y * y);
            if (!IsFinite(x) || !IsFinite(y))
            {
                minAllowedMm = 0;
                maxAllowedMm = 0;
                return false;
            }

            if (IsToolActive)
            {
                double toolLen = Math.Sqrt(ToolOffsetX * ToolOffsetX + ToolOffsetY * ToolOffsetY);
                minAllowedMm = 0.0;
                maxAllowedMm = 300.0 + toolLen;
            }
            else
            {
                minAllowedMm = 102.0;
                maxAllowedMm = 298.0;
            }

            return radiusMm >= minAllowedMm && radiusMm <= maxAllowedMm;
        }

        public void SetSpeed(int speedPercent)
        {
            if (State.IsConnected && _robot != null)
            {
                _robot.MvSpeed = Math.Max(1, Math.Min(100, speedPercent));
            }
        }

        public void MoveJointAxis(int axisNo, double targetValue)
        {
            if (!State.IsConnected || _robot == null || !IsFinite(targetValue)) return;
            try
            {
                _robot.Movea(axisNo, targetValue);
            }
            catch (Exception ex)
            {
                _logger.Log($"Movea Axis {axisNo} Error: {ex.Message}");
            }
        }

        public void MoveCartesian(double x, double y, double z, double c)
        {
            if (!State.IsConnected || _robot == null) return;
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) || !IsFinite(c))
            {
                _logger.Log("[Robot] Lỗi MoveCartesian: Tọa độ không hợp lệ (NaN/Infinity).");
                return;
            }

            try
            {
                TsPointS pt = BuildConfiguredWorldPoint(x, y, z, c);
                _robot.MvConfig = ConfigS.FREE;
                _robot.Move(pt);
            }
            catch (TsRemoteSException ex)
            {
                _logger.Log($"Move Cartesian Error (TsRemote {ex.errorCode}): {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Log($"Move Cartesian Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Di chuyển PTP theo tọa độ Descartes (Move) có giữ cấu hình cánh tay ConfigS (LEFTY/RIGHTY) và kiểm tra điều kiện an toàn.
        /// </summary>
        public bool MoveCartesian(double x, double y, double z, double c, int speedPercent, int? preferredConfig = null)
        {
            if (!State.IsConnected || _robot == null)
            {
                _logger.Log("[Robot] Chưa kết nối tới Controller.");
                return false;
            }
            if (!State.IsServoOn)
            {
                _logger.Log("[Robot] Servo đang TẮT — không thể di chuyển.");
                return false;
            }
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) || !IsFinite(c))
            {
                _logger.Log("[Robot] Tọa độ đích không hợp lệ (NaN/Infinity).");
                return false;
            }

            int clampedSpeed = Math.Max(1, Math.Min(100, speedPercent));
            EnsureWatchdogRunning();

            try
            {
                TsPointS pt = BuildConfiguredWorldPoint(x, y, z, c, preferredConfig);
                _robot.MvSpeed = clampedSpeed;
                _robot.MvConfig = ConfigS.FREE;
                _robot.Move(pt);
                return true;
            }
            catch (TsRemoteSException ex)
            {
                _logger.Log($"[Robot] Lỗi MoveCartesian (TsRemote {ex.errorCode}): {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Log($"[Robot] Lỗi MoveCartesian: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Di chuyển nội suy đường thẳng (Linear / Moves) có giữ cấu hình cánh tay ConfigS (LEFTY/RIGHTY).
        /// Rất quan trọng cho chu trình gắp & rút Wafer thẳng hàng trong khay FOUP.
        /// </summary>
        public bool MoveLinear(double x, double y, double z, double c, int speedPercent, int? preferredConfig = null)
        {
            if (!State.IsConnected || _robot == null)
            {
                _logger.Log("[Robot] Chưa kết nối tới Controller.");
                return false;
            }
            if (!State.IsServoOn)
            {
                _logger.Log("[Robot] Servo đang TẮT — không thể di chuyển.");
                return false;
            }
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) || !IsFinite(c))
            {
                _logger.Log("[Robot] Tọa độ đích không hợp lệ (NaN/Infinity).");
                return false;
            }

            int clampedSpeed = Math.Max(1, Math.Min(100, speedPercent));
            EnsureWatchdogRunning();

            try
            {
                TsPointS pt = BuildConfiguredWorldPoint(x, y, z, c, preferredConfig);
                _robot.MvSpeed = clampedSpeed;
                _robot.MvConfig = ConfigS.FREE;
                _robot.Moves(pt);
                return true;
            }
            catch (TsRemoteSException ex)
            {
                _logger.Log($"[Robot] Lỗi MoveLinear (TsRemote {ex.errorCode}): {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Log($"[Robot] Lỗi MoveLinear: {ex.Message}");
                return false;
            }
        }

        #region Tool Offset Management (Solution 1)

        public bool SetToolOffset(double x, double y, double z, double c, string toolName = "TOOL1")
        {
            ToolOffsetX = x;
            ToolOffsetY = y;
            ToolOffsetZ = z;
            ToolOffsetC = c;
            ToolName = string.IsNullOrEmpty(toolName) ? "TOOL1" : toolName;
            IsToolActive = true;

            if (State.IsConnected && _robot != null)
            {
                try
                {
                    TsTransS trans = new TsTransS { X = x, Y = y, Z = z, C = c };
                    _robot.MvTool = trans;
                    try { _robot.SetTransTool(ToolName, x, y, z, c); } catch { }
                    _logger.Log($"[Tool] Đã kích hoạt Tool Offset ({ToolName}) trên Controller: X={x:F2}mm, Y={y:F2}mm, Z={z:F2}mm, C={c:F2}°");
                }
                catch (Exception ex)
                {
                    _logger.Log($"[Tool] Cảnh báo kích hoạt Tool trên Controller: {ex.Message}");
                }
            }
            else
            {
                _logger.Log($"[Tool] Đã lưu thông số Tool Offset ({ToolName}): X={x:F2}mm, Y={y:F2}mm, Z={z:F2}mm, C={c:F2}° (Sẽ đồng bộ khi kết nối)");
            }

            ToolStateChanged?.Invoke();
            return true;
        }

        public bool DisableTool()
        {
            IsToolActive = false;
            if (State.IsConnected && _robot != null)
            {
                try
                {
                    TsTransS trans = new TsTransS { X = 0, Y = 0, Z = 0, C = 0 };
                    _robot.MvTool = trans;
                    _logger.Log("[Tool] Đã tắt Tool Offset. Trở về điều khiển mặt bích Flange (Tool 0).");
                }
                catch (Exception ex)
                {
                    _logger.Log($"[Tool] Cảnh báo tắt Tool trên Controller: {ex.Message}");
                }
            }
            else
            {
                _logger.Log("[Tool] Đã tắt Tool Offset (Trở về mặt bích Flange / Tool 0).");
            }

            ToolStateChanged?.Invoke();
            return true;
        }

        public void ReapplyToolSettingsIfConnected()
        {
            if (State.IsConnected && _robot != null && IsToolActive)
            {
                try
                {
                    TsTransS trans = new TsTransS { X = ToolOffsetX, Y = ToolOffsetY, Z = ToolOffsetZ, C = ToolOffsetC };
                    _robot.MvTool = trans;
                    try { _robot.SetTransTool(ToolName, ToolOffsetX, ToolOffsetY, ToolOffsetZ, ToolOffsetC); } catch { }
                    _logger.Log($"[Tool] Đã nạp Tool Offset ({ToolName}) sang Controller: X={ToolOffsetX:F2}, Y={ToolOffsetY:F2}, Z={ToolOffsetZ:F2}, C={ToolOffsetC:F2}");
                }
                catch (Exception ex)
                {
                    _logger.Log($"[Tool] Lỗi nạp lại Tool Offset khi kết nối: {ex.Message}");
                }
            }
        }

        #endregion

        public async Task DrawSquareAsync(double sideLength)
        {
            if (!State.IsConnected || _robot == null) return;
            try
            {
                await MoveToSafeDrawPositionAsync();

                var startPos = GetCurrentPosition()?.WorldPosition;
                if (startPos == null) return;

                double x0 = startPos.X;
                double y0 = startPos.Y;
                double z = startPos.Z;
                double c = startPos.C;

                TsPointS[] points = new TsPointS[]
                {
                    new TsPointS { X = x0 + sideLength, Y = y0, Z = z, C = c, T = 0 },
                    new TsPointS { X = x0 + sideLength, Y = y0 + sideLength, Z = z, C = c, T = 0 },
                    new TsPointS { X = x0, Y = y0 + sideLength, Z = z, C = c, T = 0 },
                    new TsPointS { X = x0, Y = y0, Z = z, C = c, T = 0 }
                };

                _logger.Log($"[Draw] Starting Square (side: {sideLength}mm) from ({x0:F1}, {y0:F1}) at optimized speed...");

                await Task.Run(async () =>
                {
                    // Save current speeds
                    double oldSpeed = _robot.MvSpeed;
                    double oldAcc = _robot.MvAccel;
                    double oldDec = _robot.MvDecel;

                    // Set speed to a higher value but not max
                    _robot.MvSpeed = 40;
                    _robot.MvAccel = 60;
                    _robot.MvDecel = 60;

                    foreach (var pt in points)
                    {
                        try { _robot.Moves(pt); } catch { }
                        await WaitUntilReachAsync(pt.X, pt.Y, 5.0); // Allow blending around corners
                    }

                    // Restore speeds
                    _robot.MvSpeed = oldSpeed;
                    _robot.MvAccel = oldAcc;
                    _robot.MvDecel = oldDec;
                });
                
                _logger.Log("[Draw] Square completed.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Draw Square Error: {ex.Message}");
            }
        }

        public async Task DrawCircleAsync(double radius)
        {
            if (!State.IsConnected || _robot == null) return;
            try
            {
                await MoveToSafeDrawPositionAsync();

                var startPos = GetCurrentPosition()?.WorldPosition;
                if (startPos == null) return;

                double x0 = startPos.X;
                double y0 = startPos.Y;
                double z = startPos.Z;
                double c = startPos.C;

                double cx = x0 + radius;
                double cy = y0;

                _logger.Log($"[Draw] Starting Circle (radius: {radius}mm) from ({x0:F1}, {y0:F1}) at optimized speed...");

                await Task.Run(async () =>
                {
                    double oldSpeed = _robot.MvSpeed;
                    double oldAcc = _robot.MvAccel;
                    double oldDec = _robot.MvDecel;

                    _robot.MvSpeed = 40;  // Increased speed
                    _robot.MvAccel = 60;
                    _robot.MvDecel = 60;

                    int segments = 72; // More segments for a smoother circle
                    for (int i = 1; i <= segments; i++)
                    {
                        double angle = Math.PI - (i * 2 * Math.PI / segments);
                        double px = cx + radius * Math.Cos(angle);
                        double py = cy + radius * Math.Sin(angle);

                        TsPointS pt = new TsPointS { X = px, Y = py, Z = z, C = c, T = 0 };
                        
                        try 
                        {
                            _robot.Moves(pt);
                        } 
                        catch { }
                        
                        // Increase tolerance significantly so the next point is sent well before stopping
                        // This allows the robot controller to blend the motion smoothly.
                        double tolerance = Math.Max(5.0, radius * 0.4); 
                        await WaitUntilReachAsync(pt.X, pt.Y, tolerance);
                    }

                    _robot.MvSpeed = oldSpeed;
                    _robot.MvAccel = oldAcc;
                    _robot.MvDecel = oldDec;
                });
                
                _logger.Log("[Draw] Circle completed.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Draw Circle Error: {ex.Message}");
            }
        }

        private async Task MoveToSafeDrawPositionAsync()
        {
            var pos = GetCurrentPosition()?.WorldPosition;
            if (pos == null) return;
            
            // Safe coordinates for drawing (middle of typical SCARA workspace)
            double safeX = 150.0;
            double safeY = 0.0;
            
            double dx = pos.X - safeX;
            double dy = pos.Y - safeY;
            
            if (Math.Sqrt(dx*dx + dy*dy) > 5.0)
            {
                _logger.Log($"[Draw] Auto-moving to safe start position ({safeX}, {safeY})...");
                TsPointS safePt = new TsPointS { X = safeX, Y = safeY, Z = pos.Z, C = pos.C, T = 0 };
                _robot.Move(safePt); 
                await WaitUntilReachAsync(safeX, safeY, 5.0);
            }
        }

        private async Task WaitUntilReachAsync(double targetX, double targetY, double tolerance = 2.0)
        {
            int timeoutMs = 10000;
            int elapsedMs = 0;
            while (elapsedMs < timeoutMs)
            {
                var pos = GetCurrentPosition()?.WorldPosition;
                if (pos != null)
                {
                    double dx = pos.X - targetX;
                    double dy = pos.Y - targetY;
                    if (Math.Sqrt(dx * dx + dy * dy) <= tolerance)
                    {
                        break;
                    }
                }
                await Task.Delay(50);
                elapsedMs += 50;
            }
        }

        private async Task WaitUntilReach3DAsync(double targetX, double targetY, double targetZ, double tolerance = 3.0)
        {
            int timeoutMs = 10000;
            int elapsedMs = 0;
            while (elapsedMs < timeoutMs)
            {
                var pos = GetCurrentPosition()?.WorldPosition;
                if (pos != null)
                {
                    double dx = pos.X - targetX;
                    double dy = pos.Y - targetY;
                    double dz = pos.Z - targetZ;
                    if (Math.Sqrt(dx * dx + dy * dy + dz * dz) <= tolerance)
                    {
                        break;
                    }
                }
                await Task.Delay(50);
                elapsedMs += 50;
            }
        }

        /// <summary>
        /// Điều khiển Robot THL300 cầm bút vẽ chữ X thật sự vào ô vuông trên giấy theo 4 đỉnh góc ranh giới.
        /// </summary>
        public async Task<bool> DrawXInCellAsync(PointF[] cornersRobot, double workZ = 0.0, double speedPercent = 20.0, double liftHeightMm = 20.0, double marginRatio = 0.20)
        {
            if (!State.IsConnected || _robot == null)
            {
                _logger.Log("[Draw X] Thất bại: Robot chưa kết nối.");
                return false;
            }

            if (!State.IsServoOn)
            {
                _logger.Log("[Draw X] Thất bại: Servo đang TẮT.");
                return false;
            }

            if (cornersRobot == null || cornersRobot.Length < 4)
            {
                _logger.Log("[Draw X] Thất bại: Thiếu dữ liệu 4 góc ranh giới.");
                return false;
            }

            // Kiểm tra an toàn bán kính làm việc R <= 300mm cho cả 4 góc
            for (int i = 0; i < 4; i++)
            {
                double r = Math.Sqrt(cornersRobot[i].X * cornersRobot[i].X + cornersRobot[i].Y * cornersRobot[i].Y);
                if (r > 300.0)
                {
                    _logger.Log($"[Draw X] Hủy lệnh: Góc #{i + 1} có R={r:F1}mm vượt quá bán kính an toàn 300mm!");
                    return false;
                }
            }

            try
            {
                var curPos = GetCurrentPosition()?.WorldPosition;
                double cAngle = curPos?.C ?? 0.0;
                double zSafe = workZ + Math.Abs(liftHeightMm);
                double zWork = workZ;

                PointF tl = cornersRobot[0];
                PointF tr = cornersRobot[1];
                PointF br = cornersRobot[2];
                PointF bl = cornersRobot[3];

                float m = (float)Math.Max(0.05, Math.Min(0.40, marginRatio));

                // Nét 1: Chéo từ TL sang BR
                PointF p1Start = new PointF(tl.X + m * (br.X - tl.X), tl.Y + m * (br.Y - tl.Y));
                PointF p1End = new PointF(br.X - m * (br.X - tl.X), br.Y - m * (br.Y - tl.Y));

                // Nét 2: Chéo từ TR sang BL
                PointF p2Start = new PointF(tr.X + m * (bl.X - tr.X), tr.Y + m * (bl.Y - tr.Y));
                PointF p2End = new PointF(bl.X - m * (bl.X - tr.X), bl.Y - m * (bl.Y - tr.Y));

                _logger.Log($"[Draw X] Bắt đầu vẽ chữ X (Axis 3: Vẽ={zWork:F1}mm / Nhấc={zSafe:F1}mm, Tốc độ: {speedPercent}%)...");

                await Task.Run(async () =>
                {
                    double oldSpeed = _robot.MvSpeed;
                    double oldAcc = _robot.MvAccel;
                    double oldDec = _robot.MvDecel;

                    _robot.MvSpeed = Math.Max(5, Math.Min(50, speedPercent));
                    _robot.MvAccel = 50;
                    _robot.MvDecel = 50;

                    try
                    {
                        // 1. Nhấc bút lên độ cao an toàn
                        if (curPos != null)
                        {
                            _robot.Move(new TsPointS { X = curPos.X, Y = curPos.Y, Z = zSafe, C = cAngle, T = 0 });
                            await WaitUntilReach3DAsync(curPos.X, curPos.Y, zSafe, 3.0);
                        }

                        // --- NÉT 1 (TL -> BR) ---
                        // 2. Bay ngang đến điểm bắt đầu nét 1
                        _robot.Move(new TsPointS { X = p1Start.X, Y = p1Start.Y, Z = zSafe, C = cAngle, T = 0 });
                        await WaitUntilReachAsync(p1Start.X, p1Start.Y, 2.0);

                        // 3. Hạ bút xuống mặt giấy
                        _robot.Moves(new TsPointS { X = p1Start.X, Y = p1Start.Y, Z = zWork, C = cAngle, T = 0 });
                        await WaitUntilReach3DAsync(p1Start.X, p1Start.Y, zWork, 2.0);

                        // 4. Kẻ nét 1 đến p1End
                        _robot.Moves(new TsPointS { X = p1End.X, Y = p1End.Y, Z = zWork, C = cAngle, T = 0 });
                        await WaitUntilReachAsync(p1End.X, p1End.Y, 2.0);

                        // 5. Nhấc bút lên Z_safe
                        _robot.Moves(new TsPointS { X = p1End.X, Y = p1End.Y, Z = zSafe, C = cAngle, T = 0 });
                        await WaitUntilReach3DAsync(p1End.X, p1End.Y, zSafe, 2.0);

                        // --- NÉT 2 (TR -> BL) ---
                        // 6. Bay ngang đến điểm bắt đầu nét 2
                        _robot.Move(new TsPointS { X = p2Start.X, Y = p2Start.Y, Z = zSafe, C = cAngle, T = 0 });
                        await WaitUntilReachAsync(p2Start.X, p2Start.Y, 2.0);

                        // 7. Hạ bút xuống mặt giấy
                        _robot.Moves(new TsPointS { X = p2Start.X, Y = p2Start.Y, Z = zWork, C = cAngle, T = 0 });
                        await WaitUntilReach3DAsync(p2Start.X, p2Start.Y, zWork, 2.0);

                        // 8. Kẻ nét 2 đến p2End
                        _robot.Moves(new TsPointS { X = p2End.X, Y = p2End.Y, Z = zWork, C = cAngle, T = 0 });
                        await WaitUntilReachAsync(p2End.X, p2End.Y, 2.0);

                        // 9. Nhấc bút lên Z_safe kết thúc
                        _robot.Moves(new TsPointS { X = p2End.X, Y = p2End.Y, Z = zSafe, C = cAngle, T = 0 });
                        await WaitUntilReach3DAsync(p2End.X, p2End.Y, zSafe, 2.0);
                    }
                    finally
                    {
                        _robot.MvSpeed = oldSpeed;
                        _robot.MvAccel = oldAcc;
                        _robot.MvDecel = oldDec;
                    }
                });

                _logger.Log($"[Draw X] ✔ Đã vẽ xong chữ X thành công! Trục 3 đã nhấc lên {zSafe:F1}mm.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Log($"[Draw X] Lỗi thực thi: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Điều khiển Robot THL300 cầm bút vẽ chữ O chuẩn xác vào tâm ô vuông trên giấy với đường kính 2.75 cm (27.5 mm).
        /// </summary>
        /// <param name="cornersRobot">4 đỉnh ranh giới của ô vuông trong hệ Robot Base (mm)</param>
        /// <param name="workZ">Cao độ Z tiếp xúc mặt giấy để vẽ nét (mm)</param>
        /// <param name="speedPercent">Tốc độ di chuyển robot (%)</param>
        /// <param name="liftHeightMm">Độ cao nhấc bút an toàn Z_safe = workZ + liftHeightMm (mm)</param>
        /// <param name="diameterMm">Đường kính hình tròn chữ O (mm, mặc định 27.5 mm = 2.75 cm)</param>
        /// <param name="centerRobot">Tọa độ tâm ô mục tiêu (mm, nếu truyền vào sẽ ưu tiên sử dụng)</param>
        public async Task<bool> DrawOInCellAsync(PointF[] cornersRobot, double workZ = 0.0, double speedPercent = 20.0, double liftHeightMm = 20.0, double diameterMm = 27.5, PointF? centerRobot = null)
        {
            if (!State.IsConnected || _robot == null)
            {
                _logger.Log("[Draw O] Thất bại: Robot chưa kết nối.");
                return false;
            }

            if (!State.IsServoOn)
            {
                _logger.Log("[Draw O] Thất bại: Servo đang TẮT.");
                return false;
            }

            if (cornersRobot == null || cornersRobot.Length < 4)
            {
                _logger.Log("[Draw O] Thất bại: Thiếu dữ liệu 4 góc ranh giới.");
                return false;
            }

            // Kiểm tra an toàn bán kính làm việc R <= 300mm
            for (int i = 0; i < 4; i++)
            {
                double r = Math.Sqrt(cornersRobot[i].X * cornersRobot[i].X + cornersRobot[i].Y * cornersRobot[i].Y);
                if (r > 300.0)
                {
                    _logger.Log($"[Draw O] Hủy lệnh: Góc #{i + 1} có R={r:F1}mm vượt quá bán kính an toàn 300mm!");
                    return false;
                }
            }

            try
            {
                var curPos = GetCurrentPosition()?.WorldPosition;
                double cAngle = curPos?.C ?? 0.0;
                double zSafe = workZ + Math.Abs(liftHeightMm);
                double zWork = workZ;

                PointF tl = cornersRobot[0];
                PointF tr = cornersRobot[1];
                PointF br = cornersRobot[2];
                PointF bl = cornersRobot[3];

                // Xác định tọa độ tâm ô (ưu tiên centerRobot truyền vào, fallback trung bình 4 đỉnh)
                double cx = centerRobot.HasValue ? centerRobot.Value.X : ((tl.X + tr.X + br.X + bl.X) / 4.0);
                double cy = centerRobot.HasValue ? centerRobot.Value.Y : ((tl.Y + tr.Y + br.Y + bl.Y) / 4.0);

                // Kích thước cạnh ô để kiểm tra bao an toàn
                double w = Math.Sqrt(Math.Pow(tr.X - tl.X, 2) + Math.Pow(tr.Y - tl.Y, 2));
                double h = Math.Sqrt(Math.Pow(bl.X - tl.X, 2) + Math.Pow(bl.Y - tl.Y, 2));

                // Bán kính hình tròn chữ O (chuẩn: đường kính 2.75 cm = 27.5 mm => Bán kính R = 13.75 mm)
                double radius = (diameterMm > 0) ? (diameterMm / 2.0) : 13.75;
                double maxSafeRadius = Math.Min(w, h) * 0.48;
                if (w > 10.0 && h > 10.0 && radius > maxSafeRadius)
                {
                    radius = maxSafeRadius;
                }

                // Kiểm tra an toàn: Tâm và toàn bộ đường tròn phải nằm trọn trong bán kính làm việc 300mm của THL300
                double centerDist = Math.Sqrt(cx * cx + cy * cy);
                if (centerDist + radius > 300.0)
                {
                    _logger.Log($"[Draw O] Hủy lệnh: Điểm xa nhất của đường tròn ({centerDist + radius:F1}mm) vượt quá bán kính làm việc 300.0mm!");
                    return false;
                }

                // Sinh quỹ đạo 36 cung tròn hoàn hảo trong hệ Robot Base
                int segments = 36;
                var circlePoints = new PointF[segments + 1];
                for (int i = 0; i <= segments; i++)
                {
                    double angle = i * 2.0 * Math.PI / segments;
                    double px = cx + radius * Math.Cos(angle);
                    double py = cy + radius * Math.Sin(angle);
                    circlePoints[i] = new PointF((float)px, (float)py);
                }

                _logger.Log($"[Draw O] Bắt đầu vẽ chữ O tại tâm ({cx:F1}, {cy:F1}), Đường kính {radius * 2.0:F2}mm (2.75cm, R={radius:F2}mm), Axis 3: Vẽ={zWork:F1}mm / Nhấc={zSafe:F1}mm, Tốc độ: {speedPercent}%)...");

                await Task.Run(async () =>
                {
                    double oldSpeed = _robot.MvSpeed;
                    double oldAcc = _robot.MvAccel;
                    double oldDec = _robot.MvDecel;

                    _robot.MvSpeed = Math.Max(5, Math.Min(40, speedPercent));
                    _robot.MvAccel = 50;
                    _robot.MvDecel = 50;

                    try
                    {
                        // 1. Nhấc bút lên độ cao an toàn
                        if (curPos != null)
                        {
                            _robot.Move(new TsPointS { X = curPos.X, Y = curPos.Y, Z = zSafe, C = cAngle, T = 0 });
                            await WaitUntilReach3DAsync(curPos.X, curPos.Y, zSafe, 3.0);
                        }

                        // 2. Bay ngang đến điểm bắt đầu đường tròn
                        PointF startPt = circlePoints[0];
                        _robot.Move(new TsPointS { X = startPt.X, Y = startPt.Y, Z = zSafe, C = cAngle, T = 0 });
                        await WaitUntilReachAsync(startPt.X, startPt.Y, 2.0);

                        // 3. Hạ bút xuống mặt giấy
                        _robot.Moves(new TsPointS { X = startPt.X, Y = startPt.Y, Z = zWork, C = cAngle, T = 0 });
                        await WaitUntilReach3DAsync(startPt.X, startPt.Y, zWork, 2.0);

                        // 4. Nội suy vẽ liên tục 360 độ
                        for (int i = 1; i <= segments; i++)
                        {
                            PointF pt = circlePoints[i];
                            _robot.Moves(new TsPointS { X = pt.X, Y = pt.Y, Z = zWork, C = cAngle, T = 0 });
                            double tol = (i == segments) ? 1.0 : 1.2;
                            await WaitUntilReachAsync(pt.X, pt.Y, tol);
                        }

                        // 5. Nhấc bút lên Z_safe kết thúc (20.0 mm)
                        PointF endPt = circlePoints[segments];
                        _robot.Moves(new TsPointS { X = endPt.X, Y = endPt.Y, Z = zSafe, C = cAngle, T = 0 });
                        await WaitUntilReach3DAsync(endPt.X, endPt.Y, zSafe, 2.0);

                        // 6. Đưa Robot lui về vị trí quan sát Camera (X=158.16, Y=-130.73, Z=20.00, C=-302.25)
                        // để cánh tay không che khuất bàn cờ, giúp camera nhận diện rõ toàn bộ các ô cờ.
                        _robot.Move(new TsPointS { X = GomokuVisionService.CameraHomeX, Y = GomokuVisionService.CameraHomeY, Z = zSafe, C = GomokuVisionService.CameraHomeC, T = 0 });
                        await WaitUntilReach3DAsync(GomokuVisionService.CameraHomeX, GomokuVisionService.CameraHomeY, zSafe, 3.0);
                    }
                    finally
                    {
                        _robot.MvSpeed = oldSpeed;
                        _robot.MvAccel = oldAcc;
                        _robot.MvDecel = oldDec;
                    }
                });

                _logger.Log($"[Draw O] ✔ Đã vẽ xong chữ O (Đường kính {radius * 2.0:F2}mm) và lùi về vị trí quan sát Camera (X={GomokuVisionService.CameraHomeX:F1}, Y={GomokuVisionService.CameraHomeY:F1}, Z={zSafe:F1}mm) an toàn!");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Log($"[Draw O] Lỗi thực thi: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Di chuyển Robot THL300 về vị trí quan sát Camera (X=158.16, Y=-130.73, Z=20.00, C=-302.25).
        /// Giúp cánh tay lùi ra khỏi bàn cờ để Camera có tầm nhìn trọn vẹn 100% 9 ô cờ.
        /// </summary>
        public async Task<bool> MoveToObservationPositionAsync(double speed = 25.0)
        {
            if (!State.IsConnected || _robot == null) return false;
            try
            {
                var curPos = GetCurrentPosition()?.WorldPosition;
                double zSafe = GomokuVisionService.CameraHomeZ;
                await Task.Run(async () =>
                {
                    double oldSpeed = _robot.MvSpeed;
                    _robot.MvSpeed = Math.Max(5, Math.Min(40, speed));
                    try
                    {
                        // 1. Nhấc trục Z lên cao độ an toàn 20.0mm trước nếu đang ở thấp
                        if (curPos != null && curPos.Z < zSafe - 1.0)
                        {
                            _robot.Move(new TsPointS { X = curPos.X, Y = curPos.Y, Z = zSafe, C = curPos.C, T = 0 });
                            await WaitUntilReach3DAsync(curPos.X, curPos.Y, zSafe, 2.0);
                        }

                        // 2. Di chuyển tới tọa độ quan sát
                        _robot.Move(new TsPointS { X = GomokuVisionService.CameraHomeX, Y = GomokuVisionService.CameraHomeY, Z = zSafe, C = GomokuVisionService.CameraHomeC, T = 0 });
                        await WaitUntilReach3DAsync(GomokuVisionService.CameraHomeX, GomokuVisionService.CameraHomeY, zSafe, 3.0);
                    }
                    finally
                    {
                        _robot.MvSpeed = oldSpeed;
                    }
                });

                _logger.Log($"[Robot] ✔ Đã đưa Robot về vị trí quan sát Camera (X={GomokuVisionService.CameraHomeX:F2}, Y={GomokuVisionService.CameraHomeY:F2}, Z={zSafe:F2} mm).");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Log($"[Robot] Lỗi di chuyển về vị trí quan sát: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Mở khóa cờ dừng lệnh di chuyển (MoveStop) bên trong TsRemoteS sau khi gọi ProgramStop.
        /// </summary>
        public void ResetMove()
        {
            if (_robot == null || !State.IsConnected) return;
            try
            {
                _robot.ResetMove();
                _logger.Log("[Robot] Đã gọi ResetMove (mở khóa trạng thái dừng lệnh di chuyển).");
            }
            catch (Exception ex)
            {
                _logger.Log($"[Robot] Cảnh báo ResetMove: {ex.Message}");
            }
        }

        /// <summary>
        /// Xóa cờ cảnh báo lỗi (Alarm) trên bộ điều khiển TSL3000.
        /// </summary>
        public void ResetAlarm()
        {
            if (_robot == null || !State.IsConnected) return;
            try
            {
                _robot.ResetALARM();
                _logger.Log("[Robot] Đã gọi ResetALARM (xóa cảnh báo lỗi Controller).");
            }
            catch (Exception ex)
            {
                _logger.Log($"[Robot] Cảnh báo ResetALARM: {ex.Message}");
            }
        }

        public void StopMotion()
        {
            if (!State.IsConnected || _robot == null) return;
            try
            {
                _robot.ProgramStop();
                try { _robot.ProgramBreak(); } catch { }
                try { _robot.ResetMove(); } catch { }
                _logger.Log("STOP requested. Robot motion halted & move lock reset.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Stop Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Đợi Robot di chuyển thực tế tới tọa độ mục tiêu (kiểm tra sai số XY và Z, hỗ trợ hủy khẩn cấp qua CancellationToken).
        /// Ngăn chặn hiện tượng gửi dồn lệnh vào socket làm nghẽn hoặc kẹt bộ điều khiển TSL3000.
        /// </summary>
        public bool WaitUntilReached(
            double targetX,
            double targetY,
            double? targetZ = null,
            double toleranceXY = 1.5,
            double toleranceZ = 2.0,
            int timeoutMs = 15000,
            CancellationToken token = default(CancellationToken))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (token.IsCancellationRequested || !State.IsConnected)
                {
                    StopMotion();
                    return false;
                }

                var pos = GetCurrentPosition()?.WorldPosition;
                if (pos != null)
                {
                    double dx = pos.X - targetX;
                    double dy = pos.Y - targetY;
                    bool xyReached = Math.Sqrt(dx * dx + dy * dy) <= toleranceXY;
                    bool zReached = true;
                    if (targetZ.HasValue)
                    {
                        zReached = Math.Abs(pos.Z - targetZ.Value) <= toleranceZ;
                    }

                    if (xyReached && zReached)
                    {
                        return true;
                    }
                }

                Thread.Sleep(30);
            }

            _logger.Log($"[Robot] Cảnh báo: Quá thời gian chờ ({timeoutMs}ms) khi đợi Robot tới (X={targetX:F2}, Y={targetY:F2}{(targetZ.HasValue ? $", Z={targetZ.Value:F2}" : "")}).");
            return false;
        }

        private bool HasActiveAlarms(TsStatusAllS status)
        {
            if (status.AlarmNo == null || status.AlarmNo.Length == 0) return false;
            
            bool hasAlarm = false;
            foreach (string alarm in status.AlarmNo)
            {
                if (!string.IsNullOrEmpty(alarm) && alarm != "0" && alarm.Trim() != "")
                {
                    _logger.Log($"    \u26A0 Active Alarm: {alarm}");
                    hasAlarm = true;
                }
            }
            return hasAlarm;
        }

        private void LogControllerStatus(TsStatusAllS status)
        {
            string[] modeNames = { "Teaching", "Internal", "Ext_Sig", "Ext", "Ext232C", "ExtEther" };
            string modeName = (status.MasterMode >= 0 && status.MasterMode < modeNames.Length)
                ? modeNames[status.MasterMode]
                : $"Unknown({status.MasterMode})";

            State.MasterModeName = modeName;

            _logger.Log($"    MasterMode: {modeName} ({status.MasterMode})");
            _logger.Log($"    ServoStatus: {(status.ServoStatus == 1 ? "ON" : "OFF")}");
            _logger.Log($"    EmergencySwitch: {(status.EmergencySwitch == 1 ? "ACTIVE!" : "Normal")}");
            _logger.Log($"    RunStatus: {status.RunStatus}");
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
