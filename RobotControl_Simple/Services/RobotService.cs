using System;
using System.Collections.Generic;
using System.Threading;
using TsRemoteLib;

namespace RobotControl.Services
{
    public class RobotService
    {
        private TsRemoteS _robot;
        private readonly object _feedbackLifecycle = new object();
        private volatile bool _cancelDraw = false;

        public bool IsConnected  { get; private set; }
        public bool IsServoOn    { get; private set; }
        public bool IsWatchdog   { get; private set; }

        public event Action<string> OnLog;
        public event Action OnStateChanged;

        private void Log(string msg) => OnLog?.Invoke(msg);
        private void Notify()        => OnStateChanged?.Invoke();
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        // ═══════════════════════════════════════════════════════════════════
        //  CONNECTION (Exact logic from Robot_Controller / SCARA_UI)
        // ═══════════════════════════════════════════════════════════════════

        public bool Connect(string ip = "192.168.0.124", int port = 1000, int srcPort = 10000)
        {
            lock (_feedbackLifecycle)
            {
                return ConnectCore(ip, port, srcPort);
            }
        }

        private bool ConnectCore(string ip, int port, int srcPort)
        {
            if (IsConnected)
            {
                Log("[INFO] Already connected. Disconnect first.");
                return false;
            }

            try
            {
                if (_robot == null)
                {
                    _robot = new TsRemoteS();
                    Log("[INFO] TsRemoteS instance created.");
                }

                Log($"Setting IP: {ip}, Port: {port}, SrcPort: {srcPort} (openmode=1:Client) ...");
                bool isIpSet = _robot.SetIPaddr(1, ip, port, srcPort);

                if (!isIpSet)
                {
                    Log("[ERROR] ✖ SetIPaddr failed. Cannot proceed with connection.");
                    return false;
                }

                Log("Connecting via TCP/IP (openmode=Client, robot must be E00=1 server) ...");
                bool isConnected = _robot.Connect(1);

                if (isConnected)
                {
                    IsConnected = true;
                    Log("[OK] ✔ Connected successfully to robot controller.");
                    Notify();
                    return true;
                }
                else
                {
                    IsConnected = false;
                    Log("[ERROR] ✖ Connection failed.");
                    DiagnoseConnectionFailure(ip);
                    Notify();
                    return false;
                }
            }
            catch (TsRemoteSException ex)
            {
                Log($"[ERROR] Connection error (TsRemote): {ex.errorCode} - {ex.Message}");
                DiagnoseConnectionFailure(ip);
                return false;
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Connection error (General): {ex.GetType().Name} - {ex.Message}");
                DiagnoseConnectionFailure(ip);
                return false;
            }
        }

        private void DiagnoseConnectionFailure(string ip)
        {
            try
            {
                using (var ping = new System.Net.NetworkInformation.Ping())
                {
                    var reply = ping.Send(ip, 800);
                    if (reply != null && reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    {
                        Log("[DIAG] Ping OK, but Port 1000 is locked or busy.");
                        Log("[ACTION] Stale session detected. Please power-cycle the controller.");
                    }
                    else
                    {
                        Log("[ERROR] Host unreachable (Ping failed). Check LAN cable and Static IP.");
                    }
                }
            }
            catch
            {
                Log("[ERROR] Host unreachable. Check LAN cable and Static IP settings.");
            }
        }

        public void Disconnect()
        {
            lock (_feedbackLifecycle)
            {
                if (!IsConnected) return;

                try
                {
                    if (IsWatchdog) { try { _robot.WatchDogStop(); } catch { } }
                    if (IsServoOn)  { try { _robot.ServoOff(); } catch { } }
                    _robot?.Disconnect();
                }
                catch (TsRemoteSException ex)
                {
                    Log($"[ERROR] Disconnect error: {ex.errorCode}");
                }
                finally
                {
                    _robot = null;
                    IsConnected = false;
                    IsServoOn = false;
                    IsWatchdog = false;
                    Log("[OK] Disconnected from robot controller.");
                    Notify();
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  WATCHDOG
        // ═══════════════════════════════════════════════════════════════════

        public bool StartWatchdog(int intervalMs = 200, int timeoutMs = 3000)
        {
            if (!IsConnected || _robot == null)
            {
                Log("[ERROR] Connect first before starting watchdog.");
                return false;
            }
            if (IsWatchdog)
            {
                Log("[INFO] Watchdog already running.");
                return true;
            }
            try
            {
                _robot.WatchDogStart(intervalMs, timeoutMs, 0, new TsRemoteS.TSStatusEvent(OnWatchdogStatus));
                IsWatchdog = true;
                Log($"[OK] Watchdog monitoring started (interval: {intervalMs}ms, timeout: {timeoutMs}ms).");
                Notify();
                return true;
            }
            catch (Exception ex)
            {
                Log($"[ERROR] WatchdogStart error: {ex.Message}");
                return false;
            }
        }

        public void StopWatchdog()
        {
            if (_robot == null || !IsWatchdog) return;
            try { _robot.WatchDogStop(); } catch { }
            IsWatchdog = false;
            Log("[OK] Watchdog stopped.");
            Notify();
        }

        private void OnWatchdogStatus(TsStatusMonitor para)
        {
            if (para == null) return;
            if (para.EmergencyStop == 1)
                Log("[SAFETY] Emergency Stop detected via watchdog!");

            bool liveServo = (para.ServoStatus == 1);
            if (liveServo != IsServoOn)
            {
                IsServoOn = liveServo;
                Notify();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  SERVO (With Alarm Reset sequence like SCARA_UI)
        // ═══════════════════════════════════════════════════════════════════

        public bool ServoOn()
        {
            if (!IsConnected || _robot == null)
            {
                Log("[ERROR] Cannot turn Servo ON: Not connected.");
                return false;
            }

            try
            {
                Log("--- Servo ON Sequence ---");
                TsStatusAllS status = _robot.GetStatusAll();

                // Check and reset alarms if present
                if (status != null && status.AlarmNo != null && status.AlarmNo[0] != null)
                {
                    Log($"[INFO] Active alarm detected: {status.AlarmNo[0]}. Resetting alarms...");
                    try
                    {
                        _robot.ResetALARM();
                        Thread.Sleep(500);
                        Log("[OK] Alarms reset completed.");
                    }
                    catch (Exception ex)
                    {
                        Log($"[WARN] Alarm reset error: {ex.Message}");
                    }
                }

                // Check E-Stop
                if (status != null && status.EmergencySwitch == 1)
                {
                    Log("[SAFETY ERROR] EMERGENCY STOP is active! Release E-Stop on controller/teach pendant first.");
                    return false;
                }

                _robot.ServoOn();
                Thread.Sleep(500);

                status = _robot.GetStatusAll();
                if (status != null && status.ServoStatus == 1)
                {
                    IsServoOn = true;
                    Log("[OK] ✔ Servo is ON! Motors energized.");
                    Notify();
                    return true;
                }
                else
                {
                    IsServoOn = false;
                    Log("[WARN] ServoOn command sent, but controller reports Servo is still OFF.");
                    Notify();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] ServoOn exception: {ex.Message}");
                return false;
            }
        }

        public bool ServoOff()
        {
            if (_robot == null) return false;
            try
            {
                _robot.ServoOff();
                IsServoOn = false;
                Log("[OK] Servo OFF.");
                Notify();
                return true;
            }
            catch (Exception ex)
            {
                Log($"[ERROR] ServoOff: {ex.Message}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  STOP
        // ═══════════════════════════════════════════════════════════════════

        public bool StopMotion()
        {
            if (_robot == null) { Log("[ERROR] Not connected."); return false; }
            try
            {
                _robot.ProgramStop();
                Log("[OK] Motion STOPPED.");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Stop error: {ex.Message}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  MOTION — CARTESIAN (X, Y, Z, C)
        // ═══════════════════════════════════════════════════════════════════

        public bool MoveCartesian(double x, double y, double z, double c, int speedPercent)
        {
            if (!CheckMotionReady()) return false;
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(c))
            {
                Log("[ERROR] Coordinates must be finite numbers.");
                return false;
            }
            if (speedPercent < 1 || speedPercent > 100)
            {
                Log("[ERROR] Speed must be 1 to 100 %.");
                return false;
            }

            try
            {
                // Read current world feedback to retain arm configuration and tool/time parameters
                TsPointS cur = null;
                try { cur = _robot.GetPsnFbkWorld(); } catch { }

                ConfigS config = ConfigS.LEFTY; // default fallback
                double tVal = 0;
                if (cur != null)
                {
                    if (cur.Config == ConfigS.LEFTY || cur.Config == ConfigS.RIGHTY)
                        config = cur.Config;
                    tVal = cur.T;
                }

                _robot.MvSpeed = speedPercent;
                _robot.MvConfig = ConfigS.FREE;

                TsPointS pt = new TsPointS
                {
                    X = x,
                    Y = y,
                    Z = z,
                    C = c,
                    T = tVal,
                    Config = config
                };

                Log($"[INFO] Moving Cartesian: X={x:F2}, Y={y:F2}, Z={z:F2}, C={c:F2}, Config={config} @ {speedPercent}% ...");
                _robot.Move(pt);
                Log($"[OK] Move Cartesian command sent successfully.");
                return true;
            }
            catch (TsRemoteSException ex)
            {
                Log($"[ERROR] MoveCartesian (TsRemote error): {ex.errorCode} - {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"[ERROR] MoveCartesian exception: {ex.Message}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  MOTION — JOINT (J1, J2, J3, J4)
        // ═══════════════════════════════════════════════════════════════════

        public bool MoveJoint(double j1, double j2, double j3, double j4, int speedPercent)
        {
            if (!CheckMotionReady()) return false;
            double[] values = { j1, j2, j3, j4 };
            foreach (double v in values)
                if (!Finite(v)) { Log("[ERROR] Joint values must be finite numbers."); return false; }
            if (speedPercent < 1 || speedPercent > 100)
            {
                Log("[ERROR] Speed must be 1 to 100 %.");
                return false;
            }

            try
            {
                _robot.MvSpeed = speedPercent;
                Log($"[INFO] Moving Joints: J1={j1:F2}°, J2={j2:F2}°, J3={j3:F2}mm, J4={j4:F2}° @ {speedPercent}% ...");
                for (int axis = 1; axis <= 4; axis++)
                {
                    _robot.Movea(axis, values[axis - 1]);
                }
                Log($"[OK] Move Joint commands sent successfully.");
                return true;
            }
            catch (TsRemoteSException ex)
            {
                Log($"[ERROR] MoveJoint (TsRemote error): {ex.errorCode} - {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"[ERROR] MoveJoint exception: {ex.Message}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  FEEDBACK
        // ═══════════════════════════════════════════════════════════════════

        public PositionFeedback GetFeedback()
        {
            if (_robot == null || !IsConnected) return null;
            try
            {
                TsPointS pt = _robot.GetPsnFbkWorld();
                TsJointS jt = _robot.GetPsnFbkJoint();
                return new PositionFeedback
                {
                    X  = pt.X,  Y  = pt.Y,  Z  = pt.Z,  C  = pt.C,
                    J1 = jt.J1, J2 = jt.J2, J3 = jt.J3, J4 = jt.J4,
                    Config = (int)pt.Config
                };
            }
            catch
            {
                return null;
            }
        }

        private bool CheckMotionReady()
        {
            if (!IsConnected) { Log("[ERROR] Not connected. Connect first."); return false; }
            if (!IsWatchdog)  { Log("[ERROR] Watchdog is not running. Start watchdog first."); return false; }
            if (!IsServoOn)   { Log("[ERROR] Servo is OFF. Turn servo ON first."); return false; }
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  MOTION — LINEAR (Moves: straight-line interpolation for plotting)
        // ═══════════════════════════════════════════════════════════════════

        public bool MoveLinear(double x, double y, double z, double c, int speedPercent)
        {
            if (!CheckMotionReady()) return false;
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(c))
            {
                Log("[ERROR] Coordinates must be finite numbers.");
                return false;
            }
            if (speedPercent < 1 || speedPercent > 100)
            {
                Log("[ERROR] Speed must be 1 to 100 %.");
                return false;
            }

            try
            {
                TsPointS cur = null;
                try { cur = _robot.GetPsnFbkWorld(); } catch { }

                ConfigS config = ConfigS.LEFTY;
                double tVal = 0;
                if (cur != null)
                {
                    if (cur.Config == ConfigS.LEFTY || cur.Config == ConfigS.RIGHTY)
                        config = cur.Config;
                    tVal = cur.T;
                }

                _robot.MvSpeed  = speedPercent;
                _robot.MvConfig = ConfigS.FREE;

                TsPointS pt = new TsPointS
                {
                    X = x, Y = y, Z = z, C = c,
                    T = tVal, Config = config
                };

                _robot.Moves(pt);   // Straight-line (linear interpolation) motion
                return true;
            }
            catch (TsRemoteSException ex)
            {
                Log($"[ERROR] MoveLinear (TsRemote): {ex.errorCode} - {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"[ERROR] MoveLinear: {ex.Message}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  DRAW — Per-stroke executor used by the SCARA Plotter feature
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Signals the current draw stroke to stop after the next waypoint.</summary>
        public void CancelDraw()      => _cancelDraw = true;

        /// <summary>Resets the cancel flag; call once before starting a new draw job.</summary>
        public void ResetCancelFlag() => _cancelDraw = false;

        /// <summary>Resets the MoveStop flag inside TsRemoteS controller.</summary>
        public void ResetMove()
        {
            if (_robot == null) return;
            try
            {
                _robot.ResetMove();
                Log("[ROBOT] Đã gọi ResetMove (xóa trạng thái dừng/khóa lệnh di chuyển).");
            }
            catch (Exception ex)
            {
                Log($"[WARN] ResetMove: {ex.Message}");
            }
        }

        /// <summary>Resets alarms on the robot controller.</summary>
        public void ResetAlarm()
        {
            if (_robot == null) return;
            try
            {
                _robot.ResetALARM();
                Log("[ROBOT] Đã gọi ResetALARM (xóa cờ cảnh báo lỗi controller).");
            }
            catch (Exception ex)
            {
                Log($"[WARN] ResetALARM: {ex.Message}");
            }
        }

        /// <summary>Dừng khẩn cấp mọi chuyển động của robot ngay lập tức.</summary>
        public void ProgramStop()
        {
            if (_robot == null || !IsConnected) return;
            try
            {
                _robot.ProgramStop();
                Log("[ROBOT] Đã gửi lệnh ProgramStop (dừng khẩn cấp mọi chuyển động).");
            }
            catch (Exception ex)
            {
                Log($"[WARN] ProgramStop: {ex.Message}");
            }
        }

        /// <summary>
        /// Đợi robot di chuyển tới tọa độ mục tiêu (đồng bộ với chuyển động thực tế).
        /// Tránh tình trạng bắn dồn dập nhiều tọa độ vào socket làm controller bị nghẽn/kẹt cờ.
        /// </summary>
        public bool WaitUntilReached(
            double targetX, double targetY, double? targetZ = null,
            double toleranceXY = 1.5, double toleranceZ = 2.0,
            int timeoutMs = 15000, CancellationToken token = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (_cancelDraw || token.IsCancellationRequested)
                {
                    ProgramStop();
                    return false;
                }

                var fb = GetFeedback();
                if (fb != null)
                {
                    double dx = fb.X - targetX;
                    double dy = fb.Y - targetY;
                    bool xyReached = Math.Sqrt(dx * dx + dy * dy) <= toleranceXY;
                    bool zReached = true;
                    if (targetZ.HasValue)
                    {
                        zReached = Math.Abs(fb.Z - targetZ.Value) <= toleranceZ;
                    }

                    if (xyReached && zReached)
                    {
                        return true;
                    }
                }
                Thread.Sleep(30);
            }
            Log($"[WARN] Đợi robot tới tọa độ ({targetX:F1}, {targetY:F1}) quá {timeoutMs}ms.");
            return false;
        }

        /// <summary>
        /// Draws one continuous pen stroke through a list of XY waypoints at a given Z.
        /// Lifts pen to zLift before travelling to the start and again after the last point.
        /// </summary>
        public bool DrawStroke(
            List<Tuple<double, double>> points,
            double zDraw, double zLift, double c, int speed,
            bool pauseOnFirstPenDown,
            ManualResetEventSlim continueSignal,
            bool dryRun,
            CancellationToken token = default)
        {
            if (points == null || points.Count < 2)
            {
                Log("[ERROR] DrawStroke: need at least 2 waypoints.");
                return false;
            }

            try
            {
                double drawZ = dryRun ? (zLift + 10.0) : zDraw;
                var fb = GetFeedback();

                // 1 — Nhấc bút tại vị trí hiện tại lên zLift trước khi bay (nếu đang ở thấp hơn zLift)
                if (fb != null && fb.Z < zLift)
                {
                    if (_cancelDraw || token.IsCancellationRequested) return false;
                    if (!MoveLinear(fb.X, fb.Y, zLift, c, speed)) return false;
                    if (!WaitUntilReached(fb.X, fb.Y, zLift, 2.0, 2.0, 10000, token)) return false;
                }

                // 2 — Bay tới điểm đầu tiên ở độ cao nhấc bút an toàn
                if (_cancelDraw || token.IsCancellationRequested) return false;
                Log($"[DRAW] Bay tới điểm đầu ({points[0].Item1:F1}, {points[0].Item2:F1})");
                if (!MoveLinear(points[0].Item1, points[0].Item2, zLift, c, speed)) return false;
                if (!WaitUntilReached(points[0].Item1, points[0].Item2, zLift, 1.5, 2.0, 15000, token)) return false;

                // 3 — Hạ ngòi bút chạm giấy (hoặc lơ lửng nếu dryRun)
                if (_cancelDraw || token.IsCancellationRequested) return false;
                Log(dryRun ? "[DRAW] Dry-run: Hạ ngòi ở độ cao an toàn." : "[DRAW] Hạ ngòi bút chạm giấy.");
                if (!MoveLinear(points[0].Item1, points[0].Item2, drawZ, c, Math.Min(speed, 10))) return false;
                if (!WaitUntilReached(points[0].Item1, points[0].Item2, drawZ, 1.5, 2.0, 10000, token)) return false;

                // 4 — Tạm dừng tùy chọn để kiểm tra ngòi
                if (pauseOnFirstPenDown && continueSignal != null)
                {
                    Log(dryRun 
                        ? "[DRAW] TẠM DỪNG (Dry-run) — Kiểm tra độ cao ngòi bút, sau đó bấm [▶ TIẾP TỤC VẼ]."
                        : "[DRAW] TẠM DỪNG — Kiểm tra ngòi bút chạm giấy, sau đó bấm [▶ TIẾP TỤC VẼ].");
                    continueSignal.Reset();
                    while (!continueSignal.Wait(100))
                    {
                        if (_cancelDraw || token.IsCancellationRequested)
                        {
                            var fbC = GetFeedback();
                            if (fbC != null)
                            {
                                MoveLinear(fbC.X, fbC.Y, zLift, c, speed);
                                WaitUntilReached(fbC.X, fbC.Y, zLift, 2.0, 2.0, 5000, CancellationToken.None);
                            }
                            return false;
                        }
                    }
                    if (_cancelDraw || token.IsCancellationRequested)
                    {
                        var fbC = GetFeedback();
                        if (fbC != null)
                        {
                            MoveLinear(fbC.X, fbC.Y, zLift, c, speed);
                            WaitUntilReached(fbC.X, fbC.Y, zLift, 2.0, 2.0, 5000, CancellationToken.None);
                        }
                        return false;
                    }
                }

                // 5 — Vẽ qua lần lượt các điểm
                for (int i = 1; i < points.Count; i++)
                {
                    if (_cancelDraw || token.IsCancellationRequested)
                    {
                        Log("[DRAW] Đã ngắt nét vẽ (Break mid-stroke).");
                        break;
                    }
                    if (!MoveLinear(points[i].Item1, points[i].Item2, drawZ, c, speed)) return false;
                    if (!WaitUntilReached(points[i].Item1, points[i].Item2, drawZ, 1.5, 2.0, 15000, token))
                    {
                        Log($"[WARN] Dừng nét vẽ do không đến kịp điểm {i}.");
                        break;
                    }
                }

                // 6 — Nhấc bút lên độ cao an toàn zLift tại điểm kết thúc
                var fbEnd = GetFeedback();
                double lx = fbEnd != null ? fbEnd.X : points[points.Count - 1].Item1;
                double ly = fbEnd != null ? fbEnd.Y : points[points.Count - 1].Item2;
                MoveLinear(lx, ly, zLift, c, speed);
                WaitUntilReached(lx, ly, zLift, 2.0, 2.0, 10000, CancellationToken.None);

                if (!_cancelDraw && !token.IsCancellationRequested)
                {
                    Log("[DRAW] Hoàn thành nét vẽ. Bút đã nhấc lên an toàn.");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Log($"[ERROR] DrawStroke exception: {ex.Message}");
                return false;
            }
        }
    }

    public class PositionFeedback
    {
        public double X, Y, Z, C;
        public double J1, J2, J3, J4;
        public int Config;
    }
}
