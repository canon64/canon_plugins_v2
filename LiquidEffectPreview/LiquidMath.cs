using System;

namespace LiquidEffectPreview
{
    // Engine-independent math: also used by the offline regression harness.
    internal struct V3
    {
        public float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static readonly V3 Up = new V3(0, 1, 0);
        public static readonly V3 Forward = new V3(0, 0, 1);
        public float Length => (float)Math.Sqrt(Dot(this, this));
        public V3 Unit => Length > 1e-8f ? this / Length : Forward;
        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, float s) => new V3(a.X * s, a.Y * s, a.Z * s);
        public static V3 operator /(V3 a, float s) => a * (1f / s);
        public static float Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static V3 Cross(V3 a, V3 b) => new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public static V3 Lerp(V3 a, V3 b, float t) => a + (b - a) * t;
    }

    internal struct EmitterPose
    {
        public V3 Position, Forward, Right;
        public V3 Up => V3.Cross(Forward, Right).Unit;
        public EmitterPose(V3 position, V3 forward, V3 right)
        {
            Position = position; Forward = forward.Unit;
            V3 projected = right - Forward * V3.Dot(right, Forward);
            if (projected.Length < 1e-6f)
                projected = V3.Cross(Math.Abs(V3.Dot(Forward, V3.Up)) < .9f ? V3.Up : V3.Forward, Forward);
            Right = projected.Unit;
        }
        public static EmitterPose Lerp(EmitterPose a, EmitterPose b, float t)
            => new EmitterPose(V3.Lerp(a.Position, b.Position, t), V3.Lerp(a.Forward, b.Forward, t), V3.Lerp(a.Right, b.Right, t));
    }

    internal struct LiquidSpec
    {
        public int Jets, Drops;
        public float Duration, Breakup, Diameter, Speed, SpreadRadians, DropSize, DropLife, Gravity;
        public static LiquidSpec Default => new LiquidSpec { Jets = 3, Drops = 12, Duration = .1f, Breakup = .1f,
            Diameter = .002f, Speed = 1.5f, SpreadRadians = .15708f, DropSize = .008f, DropLife = .7f, Gravity = 6.867f };
    }

    internal struct LaunchPoint
    {
        public V3 Origin, Velocity;
        public double Born;
        public V3 Position(double now, float gravity)
        {
            float age = (float)(now - Born);
            return Origin + Velocity * age - V3.Up * (.5f * gravity * age * age);
        }
        public V3 SpeedAt(double now, float gravity) => Velocity - V3.Up * (gravity * (float)(now - Born));
        public static LaunchPoint Lerp(LaunchPoint a, LaunchPoint b, float t) => new LaunchPoint
        { Origin = V3.Lerp(a.Origin, b.Origin, t), Velocity = V3.Lerp(a.Velocity, b.Velocity, t), Born = a.Born + (b.Born - a.Born) * t };
    }

    internal struct PathPoint
    {
        public V3 Position;
        public float Radius, Alpha;
        public PathPoint(V3 p, float r, float a = 1f) { Position = p; Radius = r; Alpha = a; }
    }
}
