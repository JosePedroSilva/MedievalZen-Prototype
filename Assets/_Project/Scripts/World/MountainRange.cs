using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>One ridge of a <see cref="MountainRange"/>: a polyline with a crest height and a width at every point.</summary>
    public sealed class RidgeLine
    {
        /// <summary>
        /// Creates a ridge from parallel arrays (world metres, crest 0..1 of the mountain amplitude, cross-section width in metres,
        /// optional asymmetry -1..1: positive makes the left side (looking along the ridge) steeper and the right side gentler).
        /// </summary>
        public RidgeLine(Vector2[] points, float[] crest, float[] width, float[] asymmetry = null)
        {
            Points = points ?? throw new ArgumentNullException(nameof(points));
            Crest = crest ?? throw new ArgumentNullException(nameof(crest));
            Width = width ?? throw new ArgumentNullException(nameof(width));
            Asymmetry = asymmetry ?? new float[points.Length];
            Min = new Vector2(float.MaxValue, float.MaxValue);
            Max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector2 pt in points)
            {
                Min = Vector2.Min(Min, pt);
                Max = Vector2.Max(Max, pt);
            }

            foreach (float w in width) MaxWidth = Mathf.Max(MaxWidth, w);
            MaxWidth *= 1f + MaxAsymmetry;
        }

        /// <summary>Largest width change of one side from <see cref="Asymmetry"/>.</summary>
        public const float MaxAsymmetry = 0.5f;

        /// <summary>
        /// Per-point asymmetry -1..1: the left side's width is multiplied by (1 - <see cref="MaxAsymmetry"/> a), the right side's by
        /// (1 + <see cref="MaxAsymmetry"/> a), so peaks get one steep face and one gentler back.
        /// </summary>
        public float[] Asymmetry { get; }

        /// <summary>Crest line in world metres (x, z).</summary>
        public Vector2[] Points { get; }

        /// <summary>Crest height at each point as a fraction of the mountain amplitude.</summary>
        public float[] Crest { get; }

        /// <summary>Width (metres) of the ridge cross-section (see MountainRange.Profile) at each point.</summary>
        public float[] Width { get; }

        /// <summary>Bounding box of the crest line.</summary>
        public Vector2 Min { get; }

        /// <summary>Bounding box of the crest line.</summary>
        public Vector2 Max { get; }

        /// <summary>Largest width along the ridge.</summary>
        public float MaxWidth { get; }
    }

    /// <summary>
    /// The skeleton of the highland: a main ridge (spine) running from the highland side of the map towards its centre,
    /// with named-peak summits and a lower col (the pass) between the second and third summit, side spurs that fall away
    /// to the lowland (valleys form between them; none leave the spine at the col, so a valley climbs to it from each
    /// side) and a few foothill knolls. Height = smooth maximum over ridges of crest × <see cref="Profile"/>(distance / width), so
    /// slopes are steepest just below the crests and concave at the foot: a range of peaks, not a table-land. Deterministic
    /// (stage seed + 120). Pure data; <see cref="HeightmapGenerator"/> samples it through a coarse grid.
    /// </summary>
    public sealed class MountainRange
    {
        /// <summary>Ridge influence ends this many widths from the crest line.</summary>
        public const float MaxReach = 4f;

        /// <summary>
        /// Cross-section of a ridge at <paramref name="x"/> widths from its crest: exp(-x^1.25). Steepest about a quarter
        /// width below the crest, concave all the way down, so summits are craggy and the foot runs out into foothills.
        /// </summary>
        public static float Profile(float x) => Mathf.Exp(-Mathf.Pow(x, 1.25f));

        /// <summary>Smooth-maximum blend width between ridges (fraction of the amplitude).</summary>
        public const float BlendWidth = 0.07f;

        /// <summary>Extra crest height (fraction of the amplitude) of the horn on each summit, falling off along the spine.</summary>
        public const float SummitHorn = 0.06f;

        /// <summary>Half width of the flat floor of a pass valley, metres.</summary>
        public const float ValleyFloorHalfWidth = 38f;

        /// <summary>Slope (rise over run) of a pass valley's sides where they are cut into a flank.</summary>
        public const float ValleyBankSlope = 0.6f;

        /// <summary>Length of the level shelf at the top of each pass valley, beside the col, metres.</summary>
        public const float ValleyShelf = 70f;

        private readonly List<RidgeLine> ridges = new List<RidgeLine>();
        private readonly List<Valley> valleys = new List<Valley>();
        private float bankPerMetre;

        private MountainRange()
        {
        }

        /// <summary>World position (metres) where the spine meets the highland side; the range grows from here.</summary>
        public Vector2 Anchor { get; private set; }

        /// <summary>Unit direction of the spine from the anchor towards the map interior.</summary>
        public Vector2 Direction { get; private set; }

        /// <summary>Spine length in metres.</summary>
        public float Length { get; private set; }

        /// <summary>All ridges; index 0 is the spine.</summary>
        public IReadOnlyList<RidgeLine> Ridges => ridges;

        /// <summary>Nominal col between the two central summits (skeleton space, before domain warp).</summary>
        public Vector2 Saddle { get; private set; }

        /// <summary>Nominal summits either side of the col (skeleton space).</summary>
        public Vector2 PeakBeforeSaddle { get; private set; }

        /// <summary>Nominal summits either side of the col (skeleton space).</summary>
        public Vector2 PeakAfterSaddle { get; private set; }

        /// <summary>Crest height of the col, fraction of the mountain amplitude.</summary>
        public float SaddleCrest { get; private set; }

        /// <summary>Nominal summit positions along the spine (skeleton space).</summary>
        public List<Vector2> Summits { get; } = new List<Vector2>();

        /// <summary>
        /// The two pass valleys (skeleton space): graded trenches from the col down each side of the range to the lowland,
        /// with a flat floor, so a road can climb to the col from both sides.
        /// </summary>
        public IReadOnlyList<Valley> Valleys => valleys;

        /// <summary>A graded valley from the col to the lowland.</summary>
        public sealed class Valley
        {
            /// <summary>Floor centre line from the col outwards (skeleton space, metres).</summary>
            public Vector2[] Points;
            /// <summary>Floor height at each point, fraction of the mountain amplitude.</summary>
            public float[] Floor;
            /// <summary>Which side of the spine (+1 / -1 along the spine's left normal).</summary>
            public int Side;
        }

        /// <summary>Builds the range for <paramref name="p"/> around the highland centre <paramref name="anchorNormalised"/> (0..1 map coordinates).</summary>
        public static MountainRange Create(WorldGenParams p, Vector2 anchorNormalised)
        {
            var rng = new System.Random(p.seed + 120);
            float size = p.worldSize;
            var range = new MountainRange();
            Vector2 anchor = anchorNormalised * size;
            var centre = new Vector2(size * 0.5f, size * 0.5f);
            Vector2 inward = centre - anchor;
            if (inward.sqrMagnitude < 1f)
            {
                float a0 = (float)(rng.NextDouble() * Math.PI * 2.0);
                inward = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
            }

            inward.Normalize();
            inward = Rotate(inward, (float)(rng.NextDouble() - 0.5) * 40f * Mathf.Deg2Rad);
            float length = Mathf.Clamp(p.mountainMaskRadius, 0.1f, 1.5f) * size * 1.05f;
            range.Anchor = anchor;
            range.Direction = inward;
            range.Length = length;
            float scale = length / 1260f;   // widths were tuned on the default 2 km map
            Vector2 normal = new Vector2(-inward.y, inward.x);

            // Crest control points along the spine (t = 0 behind the anchor, 1 at the tip). Each is an extremum.
            float t1 = 0.12f + 0.06f * F(rng);
            float t2 = t1 + 0.16f + 0.04f * F(rng);
            float ts = t2 + 0.12f + 0.03f * F(rng);
            float t3 = ts + 0.12f + 0.03f * F(rng);
            float t4 = Mathf.Min(0.9f, t3 + 0.14f + 0.04f * F(rng));
            float cs = Mathf.Clamp(p.passHeight, 0.3f, 0.9f);
            float c1 = 0.96f + 0.04f * F(rng);
            float c2 = Mathf.Max(cs + 0.12f, 0.86f + 0.08f * F(rng));
            float c3 = Mathf.Max(cs + 0.12f, 0.85f + 0.08f * F(rng));
            float c4 = 0.6f + 0.12f * F(rng);
            var ct = new[] { 0f, t1, (t1 + t2) * 0.5f, t2, ts, t3, (t3 + t4) * 0.5f, t4, 1f };
            var cc = new[] { 0.5f, c1, c2 - 0.2f - 0.08f * F(rng), c2, cs, c3, Mathf.Min(c3, c4) - 0.16f - 0.08f * F(rng), c4, 0.08f };
            var cw = new[] { 220f, 185f, 210f, 180f, 330f, 185f, 205f, 180f, 150f };
            for (int i = 0; i < cw.Length; i++) cw[i] *= scale * (0.9f + 0.2f * F(rng));

            // Spine polyline with a gentle lateral meander.
            const int SpinePoints = 56;
            var offsets = new float[6];
            for (int i = 0; i < offsets.Length; i++) offsets[i] = (F(rng) - 0.5f) * 2f * 80f * scale;
            var spine = new Vector2[SpinePoints];
            var spineCrest = new float[SpinePoints];
            var spineWidth = new float[SpinePoints];
            var spineAsym = new float[SpinePoints];
            Vector2 start = anchor - inward * (0.15f * length);

            // Asymmetry: each summit gets one steep face and one gentler back (the side alternates from summit to summit, with a
            // seeded wobble), fading out at the col so the pass stays even on both sides. Separate stream (seed + 121) so the
            // rest of the range is laid out exactly as before.
            var shape = new System.Random(p.seed + 121);
            var summitSides = new float[4];
            float firstSide = shape.Next(2) == 0 ? -1f : 1f;
            for (int k = 0; k < summitSides.Length; k++) summitSides[k] = (k % 2 == 0 ? firstSide : -firstSide) * (0.6f + 0.4f * F(shape));
            var summitT = new[] { t1, t2, t3, t4 };
            var notches = new float[SpinePoints];
            for (int i = 0; i < SpinePoints; i++) notches[i] = (F(shape) - 0.5f) * 2f;
            for (int i = 0; i < SpinePoints; i++)
            {
                float t = i / (float)(SpinePoints - 1);
                float lateral = SmoothCurve(offsets, t) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.2f));
                spine[i] = start + inward * (t * length) + normal * lateral;
                spineCrest[i] = Extrema(ct, cc, t);
                spineWidth[i] = Extrema(ct, cw, t);

                float asym = 0f, weight = 0f;
                for (int k = 0; k < summitT.Length; k++)
                {
                    float wk = Mathf.Exp(-Sq((t - summitT[k]) / 0.09f));
                    asym += summitSides[k] * wk;
                    weight += wk;
                }

                float colFade = LandCover.Smooth(0.03f, 0.1f, Mathf.Abs(t - ts));
                spineAsym[i] = weight > 1e-4f ? Mathf.Clamp(asym / Mathf.Max(weight, 0.35f), -1f, 1f) * colFade : 0f;

                // Slightly irregular crest line high up: notches and gendarmes of a few metres (none near the col).
                float high = LandCover.Smooth(0.7f, 0.85f, spineCrest[i]) * colFade;
                spineCrest[i] += notches[i] * 0.018f * high;

                // Pointed summits: a horn on each summit instead of the rounded top of the smooth crest curve.
                for (int k = 0; k < summitT.Length; k++) spineCrest[i] += SummitHorn * Mathf.Exp(-Mathf.Abs(t - summitT[k]) / 0.025f);
            }

            range.ridges.Add(new RidgeLine(spine, spineCrest, spineWidth, spineAsym));
            range.Saddle = PointAt(spine, ts);
            range.SaddleCrest = cs;
            range.PeakBeforeSaddle = PointAt(spine, t2);
            range.PeakAfterSaddle = PointAt(spine, t3);
            foreach (float t in new[] { t1, t2, t3, t4 }) range.Summits.Add(PointAt(spine, t));

            // Spurs: from each summit to both sides. Those beside the col lean away from it, so the valley that climbs to the
            // col on each side opens like a funnel between them.
            var branches = new List<(float t, int lean)>
            {
                (t1 * 0.4f, 0), (t1, 0), ((t1 + t2) * 0.5f, 0), (t2, -1), (t3, 1), ((t3 + t4) * 0.5f, 0), (t4, 0), ((t4 + 1f) * 0.5f, 0),
            };
            foreach (var (tb, lean) in branches)
            {
                Vector2 at = PointAt(spine, tb);
                Vector2 tangent = TangentAt(spine, tb);
                float crestAt = Extrema(ct, cc, tb);
                for (int side = -1; side <= 1; side += 2)
                {
                    if (lean == 0 && rng.NextDouble() < 0.2) continue;   // the odd summit has a spur on one side only
                    float angle = lean == 0 ? 60f + 35f * F(rng) : 62f + 20f * F(rng);
                    // lean -1: spur points back (away from the col, which lies ahead); +1: forward (col lies behind).
                    if (lean < 0) angle = 180f - angle;
                    Vector2 dir = Rotate(tangent, side * angle * Mathf.Deg2Rad);
                    bool minor = tb != t1 && tb != t2 && tb != t3 && tb != t4;
                    if (minor && rng.NextDouble() < 0.35) continue;
                    float spurLength = (0.26f + 0.14f * F(rng)) * length * (tb > 0.85f ? 0.7f : minor ? 0.75f : 1f);
                    float bend = (F(rng) - 0.5f) * 40f * Mathf.Deg2Rad;
                    float knob = 0.04f + 0.08f * F(rng);
                    float knobAt = 0.35f + 0.25f * F(rng);
                    float baseWidth = (125f + 40f * F(rng)) * scale;
                    const int SpurPoints = 18;
                    var pts = new Vector2[SpurPoints];
                    var crest = new float[SpurPoints];
                    var width = new float[SpurPoints];
                    Vector2 pos = at;
                    Vector2 d = dir;
                    float step = spurLength / (SpurPoints - 1);
                    for (int i = 0; i < SpurPoints; i++)
                    {
                        float u = i / (float)(SpurPoints - 1);
                        pts[i] = pos;
                        float c0 = crestAt * 0.8f;
                        crest[i] = 0.08f + (c0 - 0.08f) * Mathf.Pow(1f - u, 1.3f) + knob * Mathf.Exp(-Sq((u - knobAt) / 0.14f));
                        width[i] = baseWidth * (1f - 0.25f * u);
                        d = Rotate(d, bend / (SpurPoints - 1));
                        pos += d * step;
                    }

                    // One steep flank per spur, strongest near the spine (separate stream, see above).
                    float spurSide = (shape.Next(2) == 0 ? -1f : 1f) * (0.4f + 0.4f * F(shape));
                    var asymmetry = new float[SpurPoints];
                    for (int i = 0; i < SpurPoints; i++) asymmetry[i] = spurSide * (1f - 0.6f * i / (SpurPoints - 1f)) * (lean != 0 ? 0.4f : 1f);
                    range.ridges.Add(new RidgeLine(pts, crest, width, asymmetry));
                }
            }

            // Pass valleys: from the col straight out across the range on both sides (between the spurs that lean away from
            // the col), wandering a little, until the ridges have fallen away to the lowland. The floor is level for a shelf
            // beside the col, then falls evenly to the plain.
            range.bankPerMetre = ValleyBankSlope / Mathf.Max(1f, p.mountainAmplitude);
            Vector2 across = TangentAt(spine, ts);
            across = new Vector2(-across.y, across.x);
            for (int side = 1; side >= -1; side -= 2)
            {
                var pts = new List<Vector2> { range.Saddle };
                Vector2 dir = across * side;
                float turn = (F(rng) - 0.5f) * 1.6f * Mathf.Deg2Rad;
                const float ValleyStep = 20f;
                float travelled = 0f;
                int beyond = 0;
                while (travelled < 1100f * scale && beyond < 3)
                {
                    dir = Rotate(dir, turn + (F(rng) - 0.5f) * 3f * Mathf.Deg2Rad);
                    Vector2 next = pts[pts.Count - 1] + dir * ValleyStep;
                    pts.Add(next);
                    travelled += ValleyStep;
                    if (travelled > 250f * scale && range.EvaluateRidges(next.x, next.y) < 0.05f) beyond++;
                }

                var floor = new float[pts.Count];
                float shelf = ValleyShelf * scale;
                float top = cs - 0.025f;
                for (int i = 0; i < pts.Count; i++)
                {
                    float d = i * ValleyStep;
                    float u = Mathf.Clamp01((d - shelf) / Mathf.Max(1f, travelled - shelf));
                    floor[i] = top * (1f - u);
                }

                range.valleys.Add(new Valley { Points = pts.ToArray(), Floor = floor, Side = side });
            }

            // Foothill knolls around the range: low rounded hills so the highland steps up instead of rising as one wall.
            int knolls = 4 + rng.Next(3);
            for (int k = 0; k < knolls; k++)
            {
                float t = 0.2f + 0.75f * F(rng);
                int side = rng.Next(2) == 0 ? -1 : 1;
                Vector2 basePoint = PointAt(spine, t) + normal * side * (0.42f + 0.25f * F(rng)) * length;
                if (range.ValleyDistance(basePoint.x, basePoint.y, out _, out _) < 150f * scale) continue;   // keep the pass valleys open
                Vector2 dir = Rotate(inward, (F(rng) - 0.5f) * 3f);
                float c = 0.14f + 0.14f * F(rng);
                float w = (70f + 40f * F(rng)) * scale;
                var pts = new[] { basePoint - dir * 40f * scale, basePoint + dir * 40f * scale };
                range.ridges.Add(new RidgeLine(pts, new[] { c, c * 0.9f }, new[] { w, w }));
            }

            return range;
        }

        /// <summary>
        /// Skeleton height at (x, z) in skeleton space, 0..~1 as a fraction of the mountain amplitude: the ridges
        /// (<see cref="EvaluateRidges"/>) with the pass valleys cut into them.
        /// </summary>
        public float Evaluate(float x, float z) => Evaluate(x, z, out _);

        /// <summary>
        /// <see cref="Evaluate(float, float)"/>, plus <paramref name="valleyWeight"/>: 1 on a pass valley floor, fading to 0 up
        /// its sides (later stages keep the floor smooth: no crags, no drainage gullies).
        /// </summary>
        public float Evaluate(float x, float z, out float valleyWeight)
        {
            float s = EvaluateRidges(x, z);
            valleyWeight = 0f;
            for (int v = 0; v < valleys.Count; v++)
            {
                float d = DistanceTo(valleys[v], x, z, out float floor);
                if (d > 600f) continue;
                float target = floor + Mathf.Max(0f, d - ValleyFloorHalfWidth) * bankPerMetre;
                s = SmoothMin(s, target, 0.03f);
                valleyWeight = Mathf.Max(valleyWeight, 1f - LandCover.Smooth(ValleyFloorHalfWidth, ValleyFloorHalfWidth + 40f, d));
            }

            return Mathf.Max(0f, s);
        }

        /// <summary>Distance to the nearest pass valley centre line and its floor height and side there.</summary>
        public float ValleyDistance(float x, float z, out float floor, out int side)
        {
            float best = float.PositiveInfinity;
            floor = 0f;
            side = 0;
            for (int v = 0; v < valleys.Count; v++)
            {
                float d = DistanceTo(valleys[v], x, z, out float f);
                if (d >= best) continue;
                best = d;
                floor = f;
                side = valleys[v].Side;
            }

            return best;
        }

        private static float DistanceTo(Valley valley, float x, float z, out float floor)
        {
            float best = float.PositiveInfinity;
            floor = 0f;
            Vector2[] pts = valley.Points;
            for (int i = 1; i < pts.Length; i++)
            {
                Vector2 a = pts[i - 1];
                Vector2 b = pts[i];
                float abx = b.x - a.x;
                float abz = b.y - a.y;
                float lengthSqr = abx * abx + abz * abz;
                float t = lengthSqr > 0f ? Mathf.Clamp01(((x - a.x) * abx + (z - a.y) * abz) / lengthSqr) : 0f;
                float dx = x - (a.x + abx * t);
                float dz = z - (a.y + abz * t);
                float d = dx * dx + dz * dz;
                if (d >= best) continue;
                best = d;
                floor = valley.Floor[i - 1] + (valley.Floor[i] - valley.Floor[i - 1]) * t;
            }

            return Mathf.Sqrt(best);
        }

        /// <summary>Polynomial smooth minimum.</summary>
        public static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return Mathf.Min(a, b) - h * h * k * 0.25f;
        }

        /// <summary>
        /// Ridge height at (x, z) in skeleton space without the pass valleys: smooth maximum over ridges of
        /// crest × <see cref="Profile"/>(d / width).
        /// </summary>
        public float EvaluateRidges(float x, float z)
        {
            float result = 0f;
            for (int r = 0; r < ridges.Count; r++)
            {
                RidgeLine ridge = ridges[r];
                float reach = ridge.MaxWidth * MaxReach;
                if (x < ridge.Min.x - reach || x > ridge.Max.x + reach || z < ridge.Min.y - reach || z > ridge.Max.y + reach) continue;
                float best = 0f;
                Vector2[] pts = ridge.Points;
                for (int i = 1; i < pts.Length; i++)
                {
                    Vector2 a = pts[i - 1];
                    Vector2 b = pts[i];
                    float abx = b.x - a.x;
                    float abz = b.y - a.y;
                    float lengthSqr = abx * abx + abz * abz;
                    float t = lengthSqr > 0f ? Mathf.Clamp01(((x - a.x) * abx + (z - a.y) * abz) / lengthSqr) : 0f;
                    float dx = x - (a.x + abx * t);
                    float dz = z - (a.y + abz * t);
                    float crest = ridge.Crest[i - 1] + (ridge.Crest[i] - ridge.Crest[i - 1]) * t;
                    float width = ridge.Width[i - 1] + (ridge.Width[i] - ridge.Width[i - 1]) * t;
                    float asym = ridge.Asymmetry[i - 1] + (ridge.Asymmetry[i] - ridge.Asymmetry[i - 1]) * t;
                    if (asym != 0f)
                    {
                        // Left of the ridge direction (cross > 0) narrows for positive asymmetry: that face is steeper.
                        float side = abx * dz - abz * dx >= 0f ? 1f : -1f;
                        width *= 1f - RidgeLine.MaxAsymmetry * asym * side;
                    }

                    float q = (dx * dx + dz * dz) / (width * width);
                    if (q > MaxReach * MaxReach) continue;
                    float v = crest * Profile(Mathf.Sqrt(q));
                    if (v > best) best = v;
                }

                if (best > 0f) result = SmoothMax(result, best, BlendWidth);
            }

            return result;
        }

        /// <summary>
        /// Polynomial smooth maximum of <paramref name="a"/> and <paramref name="b"/> (both ≥ 0) with blend width up to
        /// <paramref name="k"/>; the blend narrows to nothing as the smaller value approaches 0, so a faint ridge tail never
        /// lifts the lowland.
        /// </summary>
        public static float SmoothMax(float a, float b, float k)
        {
            k = Mathf.Min(k, 2f * Mathf.Min(a, b));
            if (k <= 1e-6f) return Mathf.Max(a, b);
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return Mathf.Max(a, b) + h * h * k * 0.25f;
        }

        private static float F(System.Random rng) => (float)rng.NextDouble();

        private static float Sq(float v) => v * v;

        private static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians);
            float s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>Cosine interpolation through control points that are all extrema (zero slope at each).</summary>
        private static float Extrema(float[] t, float[] v, float x)
        {
            if (x <= t[0]) return v[0];
            for (int i = 1; i < t.Length; i++)
            {
                if (x > t[i]) continue;
                float u = (x - t[i - 1]) / Mathf.Max(1e-5f, t[i] - t[i - 1]);
                return v[i - 1] + (v[i] - v[i - 1]) * (1f - Mathf.Cos(Mathf.PI * u)) * 0.5f;
            }

            return v[v.Length - 1];
        }

        /// <summary>Smooth (cosine) curve through evenly spaced control values over 0..1.</summary>
        private static float SmoothCurve(float[] values, float t)
        {
            float x = Mathf.Clamp01(t) * (values.Length - 1);
            int i = Mathf.Min((int)x, values.Length - 2);
            float u = x - i;
            return values[i] + (values[i + 1] - values[i]) * (1f - Mathf.Cos(Mathf.PI * u)) * 0.5f;
        }

        private static Vector2 PointAt(Vector2[] line, float t)
        {
            float x = Mathf.Clamp01(t) * (line.Length - 1);
            int i = Mathf.Min((int)x, line.Length - 2);
            return Vector2.Lerp(line[i], line[i + 1], x - i);
        }

        private static Vector2 TangentAt(Vector2[] line, float t)
        {
            float x = Mathf.Clamp01(t) * (line.Length - 1);
            int i = Mathf.Min((int)x, line.Length - 2);
            return (line[i + 1] - line[i]).normalized;
        }
    }
}
