using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Roads
{
    /// <summary>
    /// Immutable 3D polyline with a cumulative arc-length table. Queries are parameterised by
    /// distance along the line from its first point and never allocate.
    /// </summary>
    public sealed class Polyline
    {
        private const float CoincidentEpsilon = 1e-4f;

        private readonly Vector3[] points;
        private readonly float[] cumulative;     // cumulative[i] = arc length from points[0] to points[i]
        private readonly Vector3[] directions;   // directions[i] = unit direction of span i -> i+1

        /// <summary>
        /// Creates a polyline through <paramref name="source"/>. Consecutive coincident points are
        /// merged. At least one point is required; a single point yields a zero-length polyline.
        /// </summary>
        public Polyline(IReadOnlyList<Vector3> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Count == 0) throw new ArgumentException("A polyline needs at least one point.", nameof(source));

            var merged = new List<Vector3>(source.Count) { source[0] };
            for (int i = 1; i < source.Count; i++)
            {
                if ((source[i] - merged[merged.Count - 1]).sqrMagnitude > CoincidentEpsilon * CoincidentEpsilon)
                {
                    merged.Add(source[i]);
                }
            }

            // Keep the exact final point even if it was merged into its predecessor.
            merged[merged.Count - 1] = source[source.Count - 1];
            if (merged.Count == 1)
            {
                merged.Add(merged[0]);
            }

            points = merged.ToArray();
            cumulative = new float[points.Length];
            directions = new Vector3[points.Length - 1];
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 delta = points[i] - points[i - 1];
                float length = delta.magnitude;
                cumulative[i] = cumulative[i - 1] + length;
                directions[i - 1] = length > 0f ? delta / length : Vector3.forward;
            }
        }

        /// <summary>The polyline vertices (at least two).</summary>
        public IReadOnlyList<Vector3> Points => points;

        /// <summary>Number of vertices.</summary>
        public int Count => points.Length;

        /// <summary>Total arc length.</summary>
        public float Length => cumulative[cumulative.Length - 1];

        /// <summary>First vertex.</summary>
        public Vector3 Start => points[0];

        /// <summary>Last vertex.</summary>
        public Vector3 End => points[points.Length - 1];

        /// <summary>Arc length from the first vertex to vertex <paramref name="index"/>.</summary>
        public float DistanceAt(int index) => cumulative[index];

        /// <summary>Position at <paramref name="distance"/> along the line (clamped to [0, Length]).</summary>
        public Vector3 Evaluate(float distance)
        {
            int span = FindSpan(distance, out float t);
            return Vector3.LerpUnclamped(points[span], points[span + 1], t);
        }

        /// <summary>Unit direction of travel at <paramref name="distance"/> (clamped to [0, Length]).</summary>
        public Vector3 Tangent(float distance)
        {
            return directions[FindSpan(distance, out _)];
        }

        /// <summary>
        /// Closest point on the polyline to <paramref name="position"/> (segment-wise projection, O(n)).
        /// </summary>
        /// <param name="position">Query point.</param>
        /// <param name="distance">Arc length of the returned point along the polyline.</param>
        public Vector3 ClosestPoint(Vector3 position, out float distance)
        {
            float bestSqr = float.PositiveInfinity;
            Vector3 best = points[0];
            distance = 0f;

            for (int i = 0; i < directions.Length; i++)
            {
                Vector3 a = points[i];
                float spanLength = cumulative[i + 1] - cumulative[i];
                float along = Mathf.Clamp(Vector3.Dot(position - a, directions[i]), 0f, spanLength);
                Vector3 candidate = a + directions[i] * along;
                float sqr = (position - candidate).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                    distance = cumulative[i] + along;
                }
            }

            return best;
        }

        /// <summary>
        /// Returns a new polyline with evenly spaced vertices, as close to <paramref name="spacing"/>
        /// apart (in arc length) as divides the length evenly. First and last points are preserved.
        /// </summary>
        public Polyline Resample(float spacing)
        {
            if (spacing <= 0f) throw new ArgumentOutOfRangeException(nameof(spacing), "Spacing must be positive.");

            int spans = Mathf.Max(1, Mathf.RoundToInt(Length / spacing));
            var result = new Vector3[spans + 1];
            float step = Length / spans;
            for (int i = 0; i < spans; i++)
            {
                result[i] = Evaluate(i * step);
            }

            result[spans] = End;
            return new Polyline(result);
        }

        /// <summary>
        /// Returns a new polyline that keeps every existing vertex and inserts extra vertices so
        /// no span is longer than <paramref name="maxSpacing"/>. Lossless, unlike <see cref="Resample"/>.
        /// </summary>
        public Polyline Densify(float maxSpacing)
        {
            if (maxSpacing <= 0f) throw new ArgumentOutOfRangeException(nameof(maxSpacing), "Spacing must be positive.");

            var result = new List<Vector3>(Mathf.CeilToInt(Length / maxSpacing) + points.Length) { points[0] };
            for (int i = 0; i < directions.Length; i++)
            {
                float spanLength = cumulative[i + 1] - cumulative[i];
                int pieces = Mathf.Max(1, Mathf.CeilToInt(spanLength / maxSpacing - 1e-4f));
                for (int k = 1; k < pieces; k++)
                {
                    result.Add(Vector3.LerpUnclamped(points[i], points[i + 1], (float)k / pieces));
                }

                result.Add(points[i + 1]);
            }

            return new Polyline(result);
        }

        /// <summary>
        /// Builds a smooth curve through <paramref name="controlPoints"/> (centripetal Catmull-Rom,
        /// which avoids cusps and overshoot on unevenly spaced points) and resamples it so vertices
        /// are about <paramref name="spacing"/> apart. The curve passes through every control point.
        /// </summary>
        public static Polyline CatmullRom(IReadOnlyList<Vector3> controlPoints, float spacing)
        {
            if (controlPoints == null) throw new ArgumentNullException(nameof(controlPoints));
            if (controlPoints.Count == 0) throw new ArgumentException("At least one control point is required.", nameof(controlPoints));
            if (spacing <= 0f) throw new ArgumentOutOfRangeException(nameof(spacing), "Spacing must be positive.");

            // Merging coincident points first keeps the centripetal knot intervals non-zero.
            Vector3[] control = new Polyline(controlPoints).points;
            if (control.Length == 2)
            {
                return new Polyline(control).Resample(spacing);
            }

            int n = control.Length;
            var dense = new List<Vector3>(n * 8) { control[0] };
            float sampleStep = spacing * 0.25f;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = i > 0 ? control[i - 1] : 2f * control[0] - control[1];
                Vector3 p1 = control[i];
                Vector3 p2 = control[i + 1];
                Vector3 p3 = i + 2 < n ? control[i + 2] : 2f * control[n - 1] - control[n - 2];

                int samples = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(p1, p2) / sampleStep));
                for (int s = 1; s < samples; s++)
                {
                    dense.Add(CentripetalCatmullRom(p0, p1, p2, p3, (float)s / samples));
                }

                dense.Add(p2);
            }

            return new Polyline(dense).Resample(spacing);
        }

        /// <summary>
        /// Douglas-Peucker simplification: removes vertices that deviate less than
        /// <paramref name="tolerance"/> metres from the simplified line. Endpoints are always kept.
        /// </summary>
        public static List<Vector3> Simplify(IReadOnlyList<Vector3> points, float tolerance)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            var result = new List<Vector3>(points.Count);
            if (points.Count <= 2)
            {
                for (int i = 0; i < points.Count; i++) result.Add(points[i]);
                return result;
            }

            var keep = new bool[points.Count];
            keep[0] = true;
            keep[points.Count - 1] = true;
            float toleranceSqr = tolerance * tolerance;

            var stack = new Stack<(int first, int last)>();
            stack.Push((0, points.Count - 1));
            while (stack.Count > 0)
            {
                var (first, last) = stack.Pop();
                float maxSqr = -1f;
                int index = -1;
                for (int i = first + 1; i < last; i++)
                {
                    float sqr = SqrDistanceToSegment(points[i], points[first], points[last]);
                    if (sqr > maxSqr)
                    {
                        maxSqr = sqr;
                        index = i;
                    }
                }

                if (index >= 0 && maxSqr > toleranceSqr)
                {
                    keep[index] = true;
                    stack.Push((first, index));
                    stack.Push((index, last));
                }
            }

            for (int i = 0; i < points.Count; i++)
            {
                if (keep[i]) result.Add(points[i]);
            }

            return result;
        }

        private int FindSpan(float distance, out float t)
        {
            int last = cumulative.Length - 1;
            if (distance <= 0f)
            {
                t = 0f;
                return 0;
            }

            if (distance >= cumulative[last])
            {
                t = 1f;
                return last - 1;
            }

            // Largest i with cumulative[i] <= distance.
            int lo = 0;
            int hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (cumulative[mid] <= distance) lo = mid;
                else hi = mid;
            }

            float spanLength = cumulative[lo + 1] - cumulative[lo];
            t = spanLength > 0f ? (distance - cumulative[lo]) / spanLength : 0f;
            return lo;
        }

        private static float SqrDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            if (lengthSqr <= 0f) return (p - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lengthSqr);
            return (p - (a + ab * t)).sqrMagnitude;
        }

        // Barry-Goldman pyramidal evaluation of a centripetal (alpha = 0.5) Catmull-Rom span p1 -> p2.
        private static Vector3 CentripetalCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            const float Epsilon = 1e-4f;
            float t0 = 0f;
            float t1 = t0 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p0, p1)), Epsilon);
            float t2 = t1 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p1, p2)), Epsilon);
            float t3 = t2 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p2, p3)), Epsilon);
            float t = Mathf.LerpUnclamped(t1, t2, u);

            Vector3 a1 = ((t1 - t) * p0 + (t - t0) * p1) / (t1 - t0);
            Vector3 a2 = ((t2 - t) * p1 + (t - t1) * p2) / (t2 - t1);
            Vector3 a3 = ((t3 - t) * p2 + (t - t2) * p3) / (t3 - t2);
            Vector3 b1 = ((t2 - t) * a1 + (t - t0) * a2) / (t2 - t0);
            Vector3 b2 = ((t3 - t) * a2 + (t - t1) * a3) / (t3 - t1);
            return ((t2 - t) * b1 + (t - t1) * b2) / (t2 - t1);
        }
    }
}
