using System;

namespace RobotControl.Models
{
    /// <summary>Một điểm dạy (Teach Point) trong bảng dây chuyền.</summary>
    public class TeachPoint
    {
        public string Name      { get; set; }
        public double X         { get; set; }
        public double Y         { get; set; }
        public double Z         { get; set; }
        public double C         { get; set; }
        public int    Speed     { get; set; } = 20;
        public int    DelayMs   { get; set; } = 0;
        /// <summary>"Linear" = nội suy đường thẳng (Moves), "PTP" = nội suy khớp (Move).</summary>
        public string MotionType { get; set; } = "Linear";

        public TeachPoint() { Name = "P_New"; }

        public TeachPoint(string name, double x, double y, double z, double c,
                          int speed = 20, int delayMs = 0, string motionType = "Linear")
        {
            Name = name; X = x; Y = y; Z = z; C = c;
            Speed = speed; DelayMs = delayMs; MotionType = motionType;
        }

        public TeachPoint Clone() => new TeachPoint(Name, X, Y, Z, C, Speed, DelayMs, MotionType);
    }
}
