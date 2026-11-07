using System;

namespace Test_1.Models
{
    /// <summary>
    /// Điểm dạy (Teaching Point) lưu trữ tọa độ Robot (Cartesian & Joint), cấu hình cánh tay,
    /// thông số chuyển động dây chuyền (Speed, DelayMs, MotionType) và trạng thái Tool TCP
    /// phục vụ pipeline gắp & đặt Wafer tự động.
    /// </summary>
    public class TeachingPoint
    {
        public string Id { get; set; } = "P1";
        public string Name { get; set; } = "New Point";

        // Tọa độ Descartes (Cartesian - độ chính xác F4 từ TSL3000)
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double C { get; set; }

        // Tọa độ Khớp (Joints)
        public double J1 { get; set; }
        public double J2 { get; set; }
        public double J3 { get; set; }
        public double J4 { get; set; }

        // Cấu hình tư thế cánh tay SCARA (ConfigS: LEFTY / RIGHTY, 0 nếu chưa xác định)
        public int ArmConfig { get; set; }

        // Thông số chuyển động cho Dây chuyền / Pipeline Wafer (kế thừa từ TeachPoint)
        /// <summary>Tốc độ di chuyển tới điểm (1..100 %).</summary>
        public int Speed { get; set; } = 20;

        /// <summary>Thời gian dừng chờ tại điểm (ms) — phục vụ bật/tắt hút chân không Wafer.</summary>
        public int DelayMs { get; set; } = 0;

        /// <summary>
        /// Kiểu nội suy chuyển động:
        /// - "Linear": Nội suy đường thẳng (Moves) — có kết hợp Z an toàn nếu bật Z-Lift.
        /// - "PTP": Di chuyển khớp nhanh (Move) — có kết hợp Z an toàn nếu bật Z-Lift.
        /// - "Direct": Đi thẳng 3D trực tiếp (Moves) bỏ qua Z-Lift (dùng khi đút/rút lưỡi gắp ngang trong khay FOUP).
        /// </summary>
        public string MotionType { get; set; } = "Linear";

        // Trạng thái Tool TCP khi lưu điểm
        public bool IsTcpMode { get; set; }
        public string ToolName { get; set; } = "TOOL1";
        public double ToolOffsetX { get; set; }
        public double ToolOffsetY { get; set; }
        public double ToolOffsetZ { get; set; }
        public double ToolOffsetC { get; set; }

        public DateTime SavedAt { get; set; } = DateTime.Now;

        public static string NormalizeMotionType(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Linear";
            string trimmed = raw.Trim();
            if (string.Equals(trimmed, "PTP", StringComparison.OrdinalIgnoreCase)) return "PTP";
            if (string.Equals(trimmed, "Direct", StringComparison.OrdinalIgnoreCase)) return "Direct";
            return "Linear";
        }

        public TeachingPoint Clone()
        {
            return new TeachingPoint
            {
                Id = Id,
                Name = Name,
                X = X,
                Y = Y,
                Z = Z,
                C = C,
                J1 = J1,
                J2 = J2,
                J3 = J3,
                J4 = J4,
                ArmConfig = ArmConfig,
                Speed = Speed,
                DelayMs = DelayMs,
                MotionType = MotionType,
                IsTcpMode = IsTcpMode,
                ToolName = ToolName,
                ToolOffsetX = ToolOffsetX,
                ToolOffsetY = ToolOffsetY,
                ToolOffsetZ = ToolOffsetZ,
                ToolOffsetC = ToolOffsetC,
                SavedAt = SavedAt
            };
        }

        public override string ToString()
        {
            string mode = IsTcpMode ? "TCP" : "Flange";
            return $"{Name} [{MotionType} {Speed}%] ({mode}: X={X:F2}, Y={Y:F2}, Z={Z:F2}, C={C:F2})";
        }
    }
}
