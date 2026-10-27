using System;
using System.Collections.Generic;

namespace LiquidEffectPreview
{
    internal struct TubeVertex
    {
        public V3 Position, Normal;
        public float U, V, Alpha;
    }

    internal sealed class TubeGeometry
    {
        public const int Sides = 8;
        public const float MaxSpacing = .005f;
        public readonly List<TubeVertex> Vertices = new List<TubeVertex>(8192);
        public readonly List<int> Indices = new List<int>(49152);
        internal readonly List<PathPoint> Resampled = new List<PathPoint>(1024);
        public void Clear() { Vertices.Clear(); Indices.Clear(); }

        public void AddPath(List<PathPoint> path, bool smooth)
        {
            if (path.Count < 2) return;
            Resampled.Clear();
            Resampled.Add(path[0]);
            for (int i = 0; i + 1 < path.Count; i++)
            {
                PathPoint a = path[i], b = path[i + 1];
                V3 before = path[Math.Max(0, i - 1)].Position, after = path[Math.Min(path.Count - 1, i + 2)].Position;
                V3 m0 = smooth ? (b.Position - before) * .5f : b.Position - a.Position;
                V3 m1 = smooth ? (after - a.Position) * .5f : b.Position - a.Position;
                Subdivide(a, b, m0, m1, 0, 1, a, b, 0);
            }
            int firstVertex = Vertices.Count;
            V3 normal = default, previousTangent = V3.Forward;
            float arc = 0;
            for (int i = 0; i < Resampled.Count; i++)
            {
                PathPoint p = Resampled[i];
                V3 tangent = Resampled[Math.Min(Resampled.Count - 1, i + 1)].Position - Resampled[Math.Max(0, i - 1)].Position;
                tangent = tangent.Length < 1e-7f ? previousTangent : tangent.Unit;
                if (i == 0) normal = Perpendicular(tangent);
                else
                {
                    V3 projected = normal - tangent * V3.Dot(normal, tangent);
                    if (projected.Length < 1e-6f) projected = V3.Cross(previousTangent, normal);
                    projected = projected - tangent * V3.Dot(projected, tangent);
                    projected = projected.Length < 1e-6f ? Perpendicular(tangent) : projected.Unit;
                    if (V3.Dot(projected, normal) < 0) projected = projected * -1;
                    normal = projected;
                    arc += (p.Position - Resampled[i - 1].Position).Length;
                }
                V3 binormal = V3.Cross(tangent, normal).Unit;
                for (int side = 0; side < Sides; side++)
                {
                    float angle = (float)(Math.PI * 2 * side / Sides);
                    V3 radial = normal * (float)Math.Cos(angle) + binormal * (float)Math.Sin(angle);
                    Vertices.Add(new TubeVertex { Position = p.Position + radial * Math.Max(0, p.Radius), Normal = radial,
                        U = (float)side / Sides, V = arc, Alpha = p.Alpha });
                }
                if (i > 0)
                {
                    int previous = firstVertex + (i - 1) * Sides, current = firstVertex + i * Sides;
                    for (int side = 0; side < Sides; side++)
                    {
                        int next = (side + 1) % Sides;
                        Indices.Add(previous + side); Indices.Add(previous + next); Indices.Add(current + side);
                        Indices.Add(current + side); Indices.Add(previous + next); Indices.Add(current + next);
                    }
                }
                previousTangent = tangent;
            }
        }

        private static V3 Perpendicular(V3 tangent)
            => V3.Cross(tangent, Math.Abs(V3.Dot(tangent, V3.Up)) < .9f ? V3.Up : new V3(1, 0, 0)).Unit;

        private static PathPoint Evaluate(PathPoint a, PathPoint b, V3 m0, V3 m1, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            V3 p = a.Position * (2 * t3 - 3 * t2 + 1) + m0 * (t3 - 2 * t2 + t)
                + b.Position * (-2 * t3 + 3 * t2) + m1 * (t3 - t2);
            return new PathPoint(p, a.Radius + (b.Radius - a.Radius) * t, a.Alpha + (b.Alpha - a.Alpha) * t);
        }

        private void Subdivide(PathPoint a, PathPoint b, V3 m0, V3 m1, float lo, float hi, PathPoint p, PathPoint q, int depth)
        {
            float middle = (lo + hi) * .5f;
            PathPoint mid = Evaluate(a, b, m0, m1, middle);
            float error = (mid.Position - V3.Lerp(p.Position, q.Position, .5f)).Length;
            // Test quarter points too: an S curve can have zero midpoint error.
            error = Math.Max(error, (Evaluate(a, b, m0, m1, (lo + middle) * .5f).Position - V3.Lerp(p.Position, q.Position, .25f)).Length);
            error = Math.Max(error, (Evaluate(a, b, m0, m1, (middle + hi) * .5f).Position - V3.Lerp(p.Position, q.Position, .75f)).Length);
            if (depth < 16 && ((q.Position - p.Position).Length > MaxSpacing || error > .00025f))
            {
                Subdivide(a, b, m0, m1, lo, middle, p, mid, depth + 1);
                Subdivide(a, b, m0, m1, middle, hi, mid, q, depth + 1);
            }
            else if ((q.Position - Resampled[Resampled.Count - 1].Position).Length > 1e-8f)
                Resampled.Add(q);
        }
    }
}
