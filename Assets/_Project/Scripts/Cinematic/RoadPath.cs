using System;
using UnityEngine;

namespace MedievalZen.Cinematic
{
    /// <summary>
    /// A sampled stretch of road around the traveller (behind and ahead), as plain points with an arc-length table, for the
    /// shot solver. Refilled a few times a second from the auto-traveller's route (or the nearest road); queries beyond the
    /// ends continue straight along the end tangent. Fixed capacity, no allocation after construction.
    /// </summary>
    public sealed class RoadPath
    {
        private readonly Vector3[] points;
        private readonly float[] cumulative;

        /// <summary>A path holding up to <paramref name="capacity"/> points.</summary>
        public RoadPath(int capacity = 160)
        {
            points = new Vector3[Math.Max(2, capacity)];
            cumulative = new float[points.Length];
        }

        /// <summary>Number of points.</summary>
        public int Count { get; private set; }

        /// <summary>Arc length from the first to the last point.</summary>
        public float Length => Count > 0 ? cumulative[Count - 1] : 0f;

        /// <summary>True when the path has at least two points.</summary>
        public bool IsUsable => Count >= 2 && Length > 0.1f;

        /// <summary>Point <paramref name="i"/>.</summary>
        public Vector3 this[int i] => points[i];

        /// <summary>Empties the path.</summary>
        public void Clear() => Count = 0;

        /// <summary>Appends a point (ignored when full or closer than 1 cm to the previous one).</summary>
        public void Add(Vector3 p)
        {
            if (Count >= points.Length) return;
            if (Count > 0)
            {
                float d = Vector3.Distance(points[Count - 1], p);
                if (d < 0.01f) return;
                cumulative[Count] = cumulative[Count - 1] + d;
            }
            else
            {
                cumulative[0] = 0f;
            }

            points[Count++] = p;
        }

        /// <summary>Moves every point by <paramref name="delta"/> (floating-origin shift).</summary>
        public void Shift(Vector3 delta)
        {
            for (int i = 0; i < Count; i++) points[i] += delta;
        }

        /// <summary>Position at arc length <paramref name="s"/> (extrapolated straight beyond either end).</summary>
        public Vector3 At(float s)
        {
            if (Count == 0) return Vector3.zero;
            if (Count == 1) return points[0];
            if (s <= 0f) return points[0] + (points[1] - points[0]).normalized * s;
            float length = Length;
            if (s >= length) return points[Count - 1] + (points[Count - 1] - points[Count - 2]).normalized * (s - length);
            int i = Segment(s);
            float span = cumulative[i + 1] - cumulative[i];
            float t = span > 1e-6f ? (s - cumulative[i]) / span : 0f;
            return points[i] + (points[i + 1] - points[i]) * t;
        }

        /// <summary>Unit horizontal direction of travel at <paramref name="s"/> (averaged over ±<paramref name="window"/> m).</summary>
        public Vector3 TangentAt(float s, float window = 3f)
        {
            if (Count < 2) return Vector3.forward;
            Vector3 d = At(s + window) - At(s - window);
            return CinematicMath.Planar(d, Vector3.forward);
        }

        /// <summary>Arc length of the point of the path nearest to <paramref name="p"/> (horizontal distance).</summary>
        public float Project(Vector3 p)
        {
            if (Count < 2) return 0f;
            float bestSqr = float.PositiveInfinity;
            float best = 0f;
            for (int i = 0; i < Count - 1; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                float abx = b.x - a.x, abz = b.z - a.z;
                float len2 = abx * abx + abz * abz;
                float t = len2 > 1e-8f ? ((p.x - a.x) * abx + (p.z - a.z) * abz) / len2 : 0f;
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                float qx = a.x + abx * t - p.x, qz = a.z + abz * t - p.z;
                float sqr = qx * qx + qz * qz;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = cumulative[i] + (cumulative[i + 1] - cumulative[i]) * t;
                }
            }

            return best;
        }

        private int Segment(float s)
        {
            int lo = 0, hi = Count - 2;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (cumulative[mid] <= s) lo = mid;
                else hi = mid - 1;
            }

            return lo;
        }
    }
}
