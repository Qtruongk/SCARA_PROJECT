using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Test_1.Models;

namespace Test_1.Services
{
    public class PointTeachingService
    {
        private readonly LoggingService _logger;
        private readonly string _primaryFilePath;
        private readonly List<TeachingPoint> _points = new List<TeachingPoint>();
        private readonly object _lock = new object();

        public event Action PointsChanged;

        public PointTeachingService(LoggingService logger, string filePath = null)
        {
            _logger = logger;
            _primaryFilePath = filePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "saved_teaching_points.json");
            LoadFromFile();
        }

        public IReadOnlyList<TeachingPoint> GetAllPoints()
        {
            lock (_lock)
            {
                return _points.ToArray();
            }
        }

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _points.Count;
                }
            }
        }

        public TeachingPoint GetPointById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (_lock)
            {
                return _points.Find(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            }
        }

        public string GenerateNextId()
        {
            lock (_lock)
            {
                return GenerateNextIdUnsafe();
            }
        }

        private string GenerateNextIdUnsafe()
        {
            int maxNum = 0;
            foreach (var p in _points)
            {
                if (!string.IsNullOrEmpty(p.Id) && p.Id.StartsWith("P", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(p.Id.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int num) && num > maxNum)
                    {
                        maxNum = num;
                    }
                }
            }
            return "P" + (maxNum + 1);
        }

        public bool AddOrUpdatePoint(TeachingPoint point, bool notify = true)
        {
            if (point == null) return false;
            point.Speed = Math.Max(1, Math.Min(100, point.Speed));
            point.DelayMs = Math.Max(0, point.DelayMs);
            point.MotionType = TeachingPoint.NormalizeMotionType(point.MotionType);

            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(point.Id))
                {
                    point.Id = GenerateNextIdUnsafe();
                }

                int idx = _points.FindIndex(p => string.Equals(p.Id, point.Id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    _points[idx] = point;
                    _logger?.Log($"[Point] Đã cập nhật điểm '{point.Name}' ({point.Id}): X={point.X:F3}, Y={point.Y:F3}, Z={point.Z:F3}, C={point.C:F2} [{point.MotionType}, {point.Speed}%, Delay={point.DelayMs}ms]");
                }
                else
                {
                    _points.Add(point);
                    _logger?.Log($"[Point] Đã thêm mới điểm '{point.Name}' ({point.Id}): X={point.X:F3}, Y={point.Y:F3}, Z={point.Z:F3}, C={point.C:F2} [{point.MotionType}, {point.Speed}%, Delay={point.DelayMs}ms]");
                }
            }

            SaveToFile();
            if (notify) PointsChanged?.Invoke();
            return true;
        }

        public bool MovePointOrder(int index, int direction, out int newIndex)
        {
            newIndex = index;
            bool moved = false;
            string movedName = null;

            lock (_lock)
            {
                int target = index + direction;
                if (index >= 0 && index < _points.Count && target >= 0 && target < _points.Count)
                {
                    TeachingPoint tmp = _points[index];
                    _points[index] = _points[target];
                    _points[target] = tmp;
                    newIndex = target;
                    movedName = tmp.Name;
                    moved = true;
                }
            }

            if (moved)
            {
                _logger?.Log($"[Point] Đã đổi thứ tự bước '{movedName}' sang vị trí #{newIndex + 1}.");
                SaveToFile();
                PointsChanged?.Invoke();
            }
            return moved;
        }

        public bool RemovePoint(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            bool removed = false;
            lock (_lock)
            {
                int count = _points.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                removed = (count > 0);
            }

            if (removed)
            {
                _logger?.Log($"[Point] Đã xóa điểm có ID '{id}'.");
                SaveToFile();
                PointsChanged?.Invoke();
            }
            return removed;
        }

        /// <summary>
        /// Xuất danh sách điểm dạy ra định dạng CSV chuẩn tương thích 100% với RobotControl_Simple:
        /// Name,X,Y,Z,C,Speed,DelayMs,MotionType (tọa độ chính xác F4).
        /// </summary>
        public int ExportToCsv(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Đường dẫn file CSV không hợp lệ.");

            List<string> lines = new List<string> { "Name,X,Y,Z,C,Speed,DelayMs,MotionType" };
            lock (_lock)
            {
                foreach (var p in _points)
                {
                    string safeName = (p.Name ?? p.Id ?? "Point").Replace(",", ";").Replace("\r", "").Replace("\n", " ").Trim();
                    if (string.IsNullOrEmpty(safeName)) safeName = p.Id ?? "Point";
                    string motion = TeachingPoint.NormalizeMotionType(p.MotionType);
                    int spd = Math.Max(1, Math.Min(100, p.Speed));
                    int delay = Math.Max(0, p.DelayMs);

                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1:F4},{2:F4},{3:F4},{4:F4},{5},{6},{7}",
                        safeName, p.X, p.Y, p.Z, p.C, spd, delay, motion));
                }
            }

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllLines(filePath, lines, Encoding.UTF8);
            int exported = lines.Count - 1;
            _logger?.Log($"[Point] Đã xuất {exported} điểm dạy ra CSV: {filePath}");
            return exported;
        }

        /// <summary>
        /// Nhập danh sách điểm dạy từ file CSV (tương thích định dạng teach_points.csv của RobotControl_Simple).
        /// </summary>
        public int ImportFromCsv(
            string filePath,
            bool defaultIsTcpMode,
            string defaultToolName = "TOOL1",
            double defaultToolX = 119.0,
            double defaultToolY = 0.0,
            double defaultToolZ = -20.0,
            double defaultToolC = 0.0)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("Không tìm thấy file CSV.", filePath);
            }

            string[] rawLines = File.ReadAllLines(filePath, Encoding.UTF8);
            var loaded = new List<TeachingPoint>();

            for (int i = 0; i < rawLines.Length; i++)
            {
                string line = rawLines[i]?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                string[] f = line.Split(',');
                if (f.Length < 5) continue;

                // Bỏ qua dòng tiêu đề (Name,X,Y,Z,C,...)
                if (i == 0 && string.Equals(f[0].Trim(), "Name", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!double.TryParse(f[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lx)) continue;
                if (!double.TryParse(f[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double ly)) continue;
                if (!double.TryParse(f[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lz)) continue;
                if (!double.TryParse(f[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lc)) continue;

                int spd = 20;
                if (f.Length > 5 && int.TryParse(f[5].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSpd))
                {
                    spd = Math.Max(1, Math.Min(100, parsedSpd));
                }

                int del = 0;
                if (f.Length > 6 && int.TryParse(f[6].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDel))
                {
                    del = Math.Max(0, parsedDel);
                }

                string mt = (f.Length > 7) ? TeachingPoint.NormalizeMotionType(f[7]) : "Linear";
                string ptId = "P" + (loaded.Count + 1);
                string ptName = f[0].Trim();
                if (string.IsNullOrEmpty(ptName)) ptName = ptId;

                loaded.Add(new TeachingPoint
                {
                    Id = ptId,
                    Name = ptName,
                    X = lx,
                    Y = ly,
                    Z = lz,
                    C = lc,
                    Speed = spd,
                    DelayMs = del,
                    MotionType = mt,
                    IsTcpMode = defaultIsTcpMode,
                    ToolName = string.IsNullOrEmpty(defaultToolName) ? "TOOL1" : defaultToolName,
                    ToolOffsetX = defaultToolX,
                    ToolOffsetY = defaultToolY,
                    ToolOffsetZ = defaultToolZ,
                    ToolOffsetC = defaultToolC,
                    SavedAt = DateTime.Now
                });
            }

            if (loaded.Count == 0)
            {
                return 0;
            }

            lock (_lock)
            {
                _points.Clear();
                _points.AddRange(loaded);
            }

            SaveToFile();
            PointsChanged?.Invoke();
            _logger?.Log($"[Point] Đã nạp thành công {loaded.Count} điểm dạy từ CSV: {filePath}");
            return loaded.Count;
        }

        public void SaveToFile()
        {
            lock (_lock)
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("{");
                    sb.AppendLine("  \"points\": [");
                    for (int i = 0; i < _points.Count; i++)
                    {
                        var p = _points[i];
                        string comma = (i < _points.Count - 1) ? "," : "";
                        sb.AppendLine("    {");
                        sb.AppendLine($"      \"id\": \"{EscapeJson(p.Id)}\",");
                        sb.AppendLine($"      \"name\": \"{EscapeJson(p.Name)}\",");
                        sb.AppendLine($"      \"x\": {p.X.ToString("F4", CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"y\": {p.Y.ToString("F4", CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"z\": {p.Z.ToString("F4", CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"c\": {p.C.ToString("F4", CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"j1\": {p.J1.ToString("F4", CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"j2\": {p.J2.ToString("F4", CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"j3\": {p.J3.ToString("F4", CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"j4\": {p.J4.ToString("F4", CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"arm_config\": {p.ArmConfig.ToString(CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"speed\": {Math.Max(1, Math.Min(100, p.Speed)).ToString(CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"delay_ms\": {Math.Max(0, p.DelayMs).ToString(CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"motion_type\": \"{EscapeJson(TeachingPoint.NormalizeMotionType(p.MotionType))}\",");
                        sb.AppendLine($"      \"is_tcp\": {(p.IsTcpMode ? "true" : "false")},");
                        sb.AppendLine($"      \"tool_name\": \"{EscapeJson(p.ToolName ?? "TOOL1")}\",");
                        sb.AppendLine($"      \"tool_offset_x\": {p.ToolOffsetX.ToString("F4", CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"tool_offset_y\": {p.ToolOffsetY.ToString("F4", CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"tool_offset_z\": {p.ToolOffsetZ.ToString("F4", CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"tool_offset_c\": {p.ToolOffsetC.ToString("F4", CultureInfo.InvariantCulture)},");
                        sb.AppendLine($"      \"saved_at\": \"{p.SavedAt:yyyy-MM-dd HH:mm:ss}\"");
                        sb.AppendLine($"    }}{comma}");
                    }
                    sb.AppendLine("  ]");
                    sb.AppendLine("}");

                    string content = sb.ToString();
                    WriteToPaths(_primaryFilePath, content);
                }
                catch (Exception ex)
                {
                    _logger?.Log($"[Point] Lỗi lưu danh sách điểm: {ex.Message}");
                }
            }
        }

        public void LoadFromFile()
        {
            lock (_lock)
            {
                _points.Clear();
                string resolvedPath = ResolveReadPath(_primaryFilePath);

                if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
                {
                    try
                    {
                        string json = File.ReadAllText(resolvedPath, Encoding.UTF8);
                        var parsed = ParsePointsJson(json);
                        if (parsed.Count > 0)
                        {
                            _points.AddRange(parsed);
                            _logger?.Log($"[Point] Đã nạp {parsed.Count} điểm dạy từ: {resolvedPath}");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Log($"[Point] Cảnh báo đọc file điểm ({resolvedPath}): {ex.Message}. Khởi tạo điểm mặc định.");
                    }
                }

                InitializeDefaultPoints();
                SaveToFile();
            }
        }

        private void InitializeDefaultPoints()
        {
            _points.Add(new TeachingPoint
            {
                Id = "P1",
                Name = "Safe Standby (Điểm chờ an toàn)",
                X = 150.0,
                Y = 0.0,
                Z = 30.0,
                C = 0.0,
                Speed = 20,
                DelayMs = 0,
                MotionType = "Linear",
                IsTcpMode = true,
                ToolName = "TOOL1",
                ToolOffsetX = 119.0,
                ToolOffsetY = 0.0,
                ToolOffsetZ = -20.0,
                ToolOffsetC = 0.0,
                SavedAt = DateTime.Now
            });

            _points.Add(new TeachingPoint
            {
                Id = "P2",
                Name = "FOUP Approach (Trước cửa khay)",
                X = 180.0,
                Y = 80.0,
                Z = 15.0,
                C = 0.0,
                Speed = 20,
                DelayMs = 100,
                MotionType = "Linear",
                IsTcpMode = true,
                ToolName = "TOOL1",
                ToolOffsetX = 119.0,
                ToolOffsetY = 0.0,
                ToolOffsetZ = -20.0,
                ToolOffsetC = 0.0,
                SavedAt = DateTime.Now
            });

            _points.Add(new TeachingPoint
            {
                Id = "P3",
                Name = "Wafer Pick (Hút phôi Wafer)",
                X = 230.0,
                Y = 80.0,
                Z = 15.0,
                C = 0.0,
                Speed = 12,
                DelayMs = 500,
                MotionType = "Direct",
                IsTcpMode = true,
                ToolName = "TOOL1",
                ToolOffsetX = 119.0,
                ToolOffsetY = 0.0,
                ToolOffsetZ = -20.0,
                ToolOffsetC = 0.0,
                SavedAt = DateTime.Now
            });

            _points.Add(new TeachingPoint
            {
                Id = "P4",
                Name = "FOUP Retract (Rút Wafer khỏi khay)",
                X = 180.0,
                Y = 80.0,
                Z = 18.0,
                C = 0.0,
                Speed = 12,
                DelayMs = 100,
                MotionType = "Direct",
                IsTcpMode = true,
                ToolName = "TOOL1",
                ToolOffsetX = 119.0,
                ToolOffsetY = 0.0,
                ToolOffsetZ = -20.0,
                ToolOffsetC = 0.0,
                SavedAt = DateTime.Now
            });

            _points.Add(new TeachingPoint
            {
                Id = "P5",
                Name = "Wafer Place Stage (Đặt phôi ra bàn)",
                X = 180.0,
                Y = -80.0,
                Z = 10.0,
                C = 0.0,
                Speed = 20,
                DelayMs = 500,
                MotionType = "Linear",
                IsTcpMode = true,
                ToolName = "TOOL1",
                ToolOffsetX = 119.0,
                ToolOffsetY = 0.0,
                ToolOffsetZ = -20.0,
                ToolOffsetC = 0.0,
                SavedAt = DateTime.Now
            });
        }

        private List<TeachingPoint> ParsePointsJson(string json)
        {
            var list = new List<TeachingPoint>();
            if (string.IsNullOrWhiteSpace(json)) return list;

            var matchBlocks = Regex.Matches(json, @"\{[^{}]*""id""[^{}]*\}", RegexOptions.Singleline);
            foreach (Match m in matchBlocks)
            {
                string block = m.Value;
                try
                {
                    var pt = new TeachingPoint
                    {
                        Id = ExtractString(block, "id", "P" + (list.Count + 1)),
                        Name = ExtractString(block, "name", "Point"),
                        X = ExtractDouble(block, "x", 0.0),
                        Y = ExtractDouble(block, "y", 0.0),
                        Z = ExtractDouble(block, "z", 0.0),
                        C = ExtractDouble(block, "c", 0.0),
                        J1 = ExtractDouble(block, "j1", 0.0),
                        J2 = ExtractDouble(block, "j2", 0.0),
                        J3 = ExtractDouble(block, "j3", 0.0),
                        J4 = ExtractDouble(block, "j4", 0.0),
                        ArmConfig = ExtractInt(block, "arm_config", 0),
                        Speed = Math.Max(1, Math.Min(100, ExtractInt(block, "speed", 20))),
                        DelayMs = Math.Max(0, ExtractInt(block, "delay_ms", 0)),
                        MotionType = TeachingPoint.NormalizeMotionType(ExtractString(block, "motion_type", "Linear")),
                        IsTcpMode = ExtractBool(block, "is_tcp", false),
                        ToolName = ExtractString(block, "tool_name", "TOOL1"),
                        ToolOffsetX = ExtractDouble(block, "tool_offset_x", 0.0),
                        ToolOffsetY = ExtractDouble(block, "tool_offset_y", 0.0),
                        ToolOffsetZ = ExtractDouble(block, "tool_offset_z", 0.0),
                        ToolOffsetC = ExtractDouble(block, "tool_offset_c", 0.0)
                    };

                    string dateStr = ExtractString(block, "saved_at", "");
                    if (DateTime.TryParse(dateStr, out DateTime dt))
                    {
                        pt.SavedAt = dt;
                    }

                    list.Add(pt);
                }
                catch { }
            }
            return list;
        }

        private static string ExtractString(string block, string key, string fallback)
        {
            var m = Regex.Match(block, $"\"{Regex.Escape(key)}\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : fallback;
        }

        private static double ExtractDouble(string block, string key, double fallback)
        {
            var m = Regex.Match(block, $"\"{Regex.Escape(key)}\"\\s*:\\s*([+-]?[0-9]*\\.?[0-9]+(?:[eE][+-]?[0-9]+)?)");
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
            {
                return val;
            }
            return fallback;
        }

        private static int ExtractInt(string block, string key, int fallback)
        {
            var m = Regex.Match(block, $"\"{Regex.Escape(key)}\"\\s*:\\s*([+-]?[0-9]+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
            {
                return val;
            }
            return fallback;
        }

        private static bool ExtractBool(string block, string key, bool fallback)
        {
            var m = Regex.Match(block, $"\"{Regex.Escape(key)}\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                return bool.Parse(m.Groups[1].Value);
            }
            return fallback;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }

        private static string FindProjectRootDir()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] rels = { @"..\..\", @"..\..\..\" };
                foreach (string rel in rels)
                {
                    string candidateDir = Path.GetFullPath(Path.Combine(baseDir, rel));
                    if (Directory.Exists(Path.Combine(candidateDir, "Core")))
                    {
                        return candidateDir;
                    }
                }
            }
            catch { }
            return null;
        }

        private void WriteToPaths(string primaryPath, string content)
        {
            string dir = Path.GetDirectoryName(primaryPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(primaryPath, content, Encoding.UTF8);

            // Đồng bộ sang thư mục Project root nếu có để lưu trữ lâu dài
            try
            {
                string rootDir = FindProjectRootDir();
                if (!string.IsNullOrEmpty(rootDir))
                {
                    string target = Path.Combine(rootDir, Path.GetFileName(primaryPath));
                    if (!string.Equals(Path.GetFullPath(primaryPath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    {
                        File.WriteAllText(target, content, Encoding.UTF8);
                    }
                }
            }
            catch { }
        }

        private static string ResolveReadPath(string primaryPath)
        {
            if (File.Exists(primaryPath)) return primaryPath;

            try
            {
                string rootDir = FindProjectRootDir();
                if (!string.IsNullOrEmpty(rootDir))
                {
                    string candidateFile = Path.Combine(rootDir, Path.GetFileName(primaryPath));
                    if (File.Exists(candidateFile)) return candidateFile;
                }
            }
            catch { }

            return primaryPath;
        }
    }
}
