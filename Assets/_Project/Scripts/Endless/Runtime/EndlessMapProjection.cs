using System;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The scroll map's layout: absolute (x, z) ↔ strip (u, v) in metres. The road's <b>trend line</b> — the road smoothed with
    /// a Gaussian over its distance (σ = <see cref="KernelSigma"/>, 500 m by default, which removes the ~430 m meanders and most
    /// of the shorter swings) — is straightened into the strip's vertical axis: <c>v</c> is arc length along the trend line
    /// (measured from the <see cref="AnchorS"/> it was built around), <c>u</c> the signed distance from it, positive to the right
    /// of the direction of travel. The road's meanders still show as meanders (it wanders up to about 120 m either side of the
    /// middle) while the strip stays a clean vertical band.
    /// <para>
    /// The trend line is sampled every <see cref="SampleSpacing"/> metres of road distance and joined by a Catmull-Rom spline,
    /// so its tangent (and the strip's normals) turn continuously: features never tear at a sample. The map folds only where
    /// |u| exceeds the trend's radius of curvature; on the generator's roads the smallest radius is about 2 km, so at the strip's
    /// edge (384 m) lengths along v are stretched or squeezed by at most about 20 % (typically under 5 %). Pure, deterministic for
    /// a generator and anchor, thread-safe: the sample table grows outward from the anchor under a lock and is read without one.
    /// </para>
    /// </summary>
    public sealed class StripProjection
    {
        /// <summary>Road distance between trend samples, metres.</summary>
        public const double SampleSpacing = 8.0;

        /// <summary>Default standard deviation of the smoothing kernel along the road, metres.</summary>
        public const double DefaultKernelSigma = 500.0;

        private const int BlockSize = 256;

        private readonly IEndlessGenerator generator;
        private readonly double[] weights;
        private readonly int halfTaps;
        private readonly object growLock = new object();
        private volatile Block[] forward = Array.Empty<Block>();
        private volatile Block[] backward = Array.Empty<Block>();

        /// <summary>A run of <see cref="BlockSize"/> trend samples (immutable once published).</summary>
        private sealed class Block
        {
            public readonly double[] X = new double[BlockSize];
            public readonly double[] Z = new double[BlockSize];

            /// <summary>Arc length (v) at each sample.</summary>
            public readonly double[] V = new double[BlockSize];
        }

        /// <summary>
        /// The projection of <paramref name="generator"/>'s road, with v = 0 at the trend sample nearest road distance
        /// <paramref name="anchorS"/> (snapped to a multiple of 1024 m so nearby anchors give the same strip).
        /// </summary>
        public StripProjection(IEndlessGenerator generator, double anchorS, double kernelSigma = DefaultKernelSigma)
        {
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            if (double.IsNaN(anchorS) || double.IsInfinity(anchorS)) anchorS = 0.0;
            AnchorS = Math.Round(anchorS / 1024.0) * 1024.0;
            KernelSigma = Math.Max(SampleSpacing, kernelSigma);
            halfTaps = (int)Math.Ceiling(3.0 * KernelSigma / SampleSpacing);
            weights = new double[2 * halfTaps + 1];
            double sum = 0.0;
            for (int j = -halfTaps; j <= halfTaps; j++)
            {
                double t = j * SampleSpacing / KernelSigma;
                double w = Math.Exp(-0.5 * t * t);
                weights[j + halfTaps] = w;
                sum += w;
            }

            for (int j = 0; j < weights.Length; j++) weights[j] /= sum;
        }

        /// <summary>The generator whose road this strip follows.</summary>
        public IEndlessGenerator Generator => generator;

        /// <summary>Road distance of trend sample 0 (where v = 0).</summary>
        public double AnchorS { get; }

        /// <summary>Standard deviation of the smoothing kernel, metres of road.</summary>
        public double KernelSigma { get; }

        // ------------------------------------------------------------------ public mapping

        /// <summary>Absolute (x, z) of strip point (u, v).</summary>
        public void ToWorld(double u, double v, out double x, out double z)
        {
            double tau = TauAtV(v);
            Curve(tau, out double cx, out double cz, out double dx, out double dz);
            double len = Math.Sqrt(dx * dx + dz * dz);
            double nx = dz / len, nz = -dx / len;   // right of the direction of travel
            x = cx + u * nx;
            z = cz + u * nz;
        }

        /// <summary>
        /// The trend line's point (<paramref name="cx"/>, <paramref name="cz"/>) and unit right normal (<paramref name="nx"/>,
        /// <paramref name="nz"/>) at strip height <paramref name="v"/>: every point of that row is (cx + u·nx, cz + u·nz).
        /// </summary>
        public void Frame(double v, out double cx, out double cz, out double nx, out double nz)
        {
            Curve(TauAtV(v), out cx, out cz, out double dx, out double dz);
            double len = Math.Sqrt(dx * dx + dz * dz);
            nx = dz / len;
            nz = -dx / len;
        }

        /// <summary>
        /// Strip (u, v) of absolute (x, z): the foot of the perpendicular on the trend line, found by Newton's method from
        /// <paramref name="hintV"/> (NaN: from the point's z).
        /// </summary>
        public void ToStrip(double x, double z, double hintV, out double u, out double v)
        {
            double tau = double.IsNaN(hintV) || double.IsInfinity(hintV) ? TauFromZ(z) : TauAtV(hintV);
            tau = Foot(x, z, tau);
            Curve(tau, out double cx, out double cz, out double dx, out double dz);
            double len = Math.Sqrt(dx * dx + dz * dz);
            u = ((x - cx) * dz - (z - cz) * dx) / len;
            v = VAtTau(tau);
        }

        /// <summary>v of the road's centre line at road distance <paramref name="s"/>.</summary>
        public double VOfS(double s)
        {
            RoadPoint p = generator.Road(s);
            double tau = Foot(p.X, p.Z, (s - AnchorS) / SampleSpacing);
            return VAtTau(tau);
        }

        /// <summary>Road point (absolute x, z) at road distance <paramref name="s"/> in strip coordinates.</summary>
        public void RoadToStrip(double s, out double u, out double v)
        {
            RoadPoint p = generator.Road(s);
            double tau = Foot(p.X, p.Z, (s - AnchorS) / SampleSpacing);
            Curve(tau, out double cx, out double cz, out double dx, out double dz);
            double len = Math.Sqrt(dx * dx + dz * dz);
            u = ((p.X - cx) * dz - (p.Z - cz) * dx) / len;
            v = VAtTau(tau);
        }

        /// <summary>Road distance whose centre-line point lies at strip height <paramref name="v"/> (inverse of <see cref="VOfS"/>).</summary>
        public double SAtV(double v)
        {
            // The trend sample at v is the smoothed road around its own road distance: an excellent first guess; then secant steps.
            double s0 = AnchorS + TauAtV(v) * SampleSpacing;
            double f0 = VOfS(s0) - v;
            if (Math.Abs(f0) < 1e-7) return s0;
            double s1 = s0 - f0;
            for (int i = 0; i < 12; i++)
            {
                double f1 = VOfS(s1) - v;
                if (Math.Abs(f1) < 1e-7) return s1;
                double slope = (f1 - f0) / (s1 - s0);
                if (!(Math.Abs(slope) > 1e-6)) slope = 1.0;
                double next = s1 - f1 / slope;
                s0 = s1;
                f0 = f1;
                s1 = next;
            }

            return s1;
        }

        /// <summary>Heading of the trend line at strip height <paramref name="v"/>, degrees about Y (0 = +Z = north, 90 = +X).</summary>
        public double HeadingAt(double v)
        {
            Curve(TauAtV(v), out _, out _, out double dx, out double dz);
            return Math.Atan2(dx, dz) * 180.0 / Math.PI;
        }

        /// <summary>Signed curvature of the trend line at <paramref name="v"/> (1/m, positive turning right).</summary>
        public double CurvatureAt(double v)
        {
            double tau = TauAtV(v);
            const double h = 1e-3;
            Curve(tau - h, out _, out _, out double ax, out double az);
            Curve(tau + h, out _, out _, out double bx, out double bz);
            Curve(tau, out _, out _, out double dx, out double dz);
            double ddx = (bx - ax) / (2 * h), ddz = (bz - az) / (2 * h);
            double speed = Math.Sqrt(dx * dx + dz * dz);
            // Turning right (clockwise seen from above, x east, z north) is negative in the x-z cross product.
            return -(dx * ddz - dz * ddx) / (speed * speed * speed);
        }

        // ------------------------------------------------------------------ curve over the sample index τ

        /// <summary>The trend point and its derivative d/dτ at fractional sample index <paramref name="tau"/> (Catmull-Rom).</summary>
        private void Curve(double tau, out double x, out double z, out double dx, out double dz)
        {
            int i = (int)Math.Floor(tau);
            double t = tau - i;
            Sample(i - 1, out double x0, out double z0, out _);
            Sample(i, out double x1, out double z1, out _);
            Sample(i + 1, out double x2, out double z2, out _);
            Sample(i + 2, out double x3, out double z3, out _);
            CatmullRom(x0, x1, x2, x3, t, out x, out dx);
            CatmullRom(z0, z1, z2, z3, t, out z, out dz);
        }

        private static void CatmullRom(double p0, double p1, double p2, double p3, double t, out double value, out double derivative)
        {
            double a = -0.5 * p0 + 1.5 * p1 - 1.5 * p2 + 0.5 * p3;
            double b = p0 - 2.5 * p1 + 2.0 * p2 - 0.5 * p3;
            double c = -0.5 * p0 + 0.5 * p2;
            value = ((a * t + b) * t + c) * t + p1;
            derivative = (3.0 * a * t + 2.0 * b) * t + c;
        }

        // 3-point Gauss-Legendre on [0, 1].
        private static readonly double[] GaussT = { 0.5 - 0.5 * Math.Sqrt(0.6), 0.5, 0.5 + 0.5 * Math.Sqrt(0.6) };
        private static readonly double[] GaussW = { 5.0 / 18.0, 8.0 / 18.0, 5.0 / 18.0 };

        /// <summary>Arc length of one spline interval from t = 0 to <paramref name="t"/>, from the four control points.</summary>
        private static double PartialLength(double x0, double x1, double x2, double x3, double z0, double z1, double z2, double z3, double t)
        {
            double sum = 0.0;
            for (int k = 0; k < 3; k++)
            {
                double tt = GaussT[k] * t;
                CatmullRom(x0, x1, x2, x3, tt, out _, out double dx);
                CatmullRom(z0, z1, z2, z3, tt, out _, out double dz);
                sum += GaussW[k] * Math.Sqrt(dx * dx + dz * dz);
            }

            return sum * t;
        }

        private double VAtTau(double tau)
        {
            int i = (int)Math.Floor(tau);
            double t = tau - i;
            Sample(i - 1, out double x0, out double z0, out _);
            Sample(i, out double x1, out double z1, out double v1);
            Sample(i + 1, out double x2, out double z2, out _);
            Sample(i + 2, out double x3, out double z3, out _);
            return v1 + PartialLength(x0, x1, x2, x3, z0, z1, z2, z3, t);
        }

        /// <summary>Fractional sample index whose arc length is <paramref name="v"/>.</summary>
        private double TauAtV(double v)
        {
            // v per sample is close to the spacing (a little less: the trend is shorter than the road), so start there and walk.
            int i = (int)Math.Floor(v / SampleSpacing);
            Sample(i, out _, out _, out double vi);
            Sample(i + 1, out _, out _, out double vn);
            int guard = 0;
            while (v < vi && guard++ < 100000)
            {
                i--;
                vn = vi;
                Sample(i, out _, out _, out vi);
            }

            while (v >= vn && guard++ < 100000)
            {
                i++;
                vi = vn;
                Sample(i + 1, out _, out _, out vn);
            }

            Sample(i - 1, out double x0, out double z0, out _);
            Sample(i, out double x1, out double z1, out _);
            Sample(i + 1, out double x2, out double z2, out _);
            Sample(i + 2, out double x3, out double z3, out _);
            double target = v - vi;
            double t = vn > vi ? target / (vn - vi) : 0.0;
            for (int k = 0; k < 8; k++)
            {
                double f = PartialLength(x0, x1, x2, x3, z0, z1, z2, z3, t) - target;
                CatmullRom(x0, x1, x2, x3, t, out _, out double dx);
                CatmullRom(z0, z1, z2, z3, t, out _, out double dz);
                double speed = Math.Sqrt(dx * dx + dz * dz);
                if (speed < 1e-9) break;
                double step = f / speed;
                t -= step;
                if (Math.Abs(step) < 1e-13) break;
            }

            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            return i + t;
        }

        /// <summary>A first guess of the sample index whose trend point has absolute z near <paramref name="z"/>.</summary>
        private double TauFromZ(double z)
        {
            Sample(0, out _, out double z0, out _);
            double tau = (z - z0) / SampleSpacing;
            for (int k = 0; k < 40; k++)
            {
                Curve(tau, out _, out double cz, out _, out double dz);
                if (dz < 0.05) dz = 0.05;
                double step = (z - cz) / dz;
                if (step > 64.0) step = 64.0;
                if (step < -64.0) step = -64.0;
                tau += step;
                if (Math.Abs(step) < 1e-3) break;
            }

            return tau;
        }

        /// <summary>Sample index of the foot of the perpendicular from (x, z), by Newton's method from <paramref name="tau"/>.</summary>
        private double Foot(double x, double z, double tau)
        {
            for (int k = 0; k < 40; k++)
            {
                Curve(tau, out double cx, out double cz, out double dx, out double dz);
                const double h = 1e-4;
                Curve(tau + h, out _, out _, out double ex, out double ez);
                double ddx = (ex - dx) / h, ddz = (ez - dz) / h;
                double rx = x - cx, rz = z - cz;
                double f = rx * dx + rz * dz;
                double df = -(dx * dx + dz * dz) + rx * ddx + rz * ddz;
                double step;
                if (df < -1e-9) step = -f / df;
                else step = f / (dx * dx + dz * dz);   // far outside the fold-free band: plain projection step
                if (step > 8.0) step = 8.0;
                if (step < -8.0) step = -8.0;
                tau += step;
                if (Math.Abs(step) < 1e-12) break;
            }

            return tau;
        }

        // ------------------------------------------------------------------ sample table

        /// <summary>Trend sample <paramref name="i"/>: position and arc length.</summary>
        private void Sample(int i, out double x, out double z, out double v)
        {
            Block block;
            int k;
            if (i >= 0)
            {
                int b = i / BlockSize;
                k = i - b * BlockSize;
                Block[] list = forward;
                block = b < list.Length ? list[b] : Grow(true, b);
            }
            else
            {
                int j = -i - 1;
                int b = j / BlockSize;
                k = BlockSize - 1 - (j - b * BlockSize);   // backward blocks store indices in increasing order
                Block[] list = backward;
                block = b < list.Length ? list[b] : Grow(false, b);
            }

            x = block.X[k];
            z = block.Z[k];
            v = block.V[k];
        }

        private Block Grow(bool isForward, int wanted)
        {
            lock (growLock)
            {
                Block[] list = isForward ? forward : backward;
                while (list.Length <= wanted)
                {
                    int b = list.Length;
                    Block made = isForward ? BuildForward(b, b > 0 ? list[b - 1] : null) : BuildBackward(b, b > 0 ? list[b - 1] : null);
                    var next = new Block[b + 1];
                    Array.Copy(list, next, b);
                    next[b] = made;
                    list = next;
                    if (isForward) forward = next;
                    else backward = next;
                }

                return list[wanted];
            }
        }

        /// <summary>Smoothed road positions for sample indices [first, first + count) (count + 2·halfTaps road samples).</summary>
        private void Trend(int first, int count, double[] xs, double[] zs)
        {
            int n = count + 2 * halfTaps;
            var rx = new double[n];
            var rz = new double[n];
            for (int k = 0; k < n; k++)
            {
                RoadPoint p = generator.Road(AnchorS + (first - halfTaps + k) * SampleSpacing);
                rx[k] = p.X;
                rz[k] = p.Z;
            }

            for (int k = 0; k < count; k++)
            {
                double sx = 0.0, sz = 0.0;
                for (int j = 0; j < weights.Length; j++)
                {
                    sx += weights[j] * rx[k + j];
                    sz += weights[j] * rz[k + j];
                }

                xs[k] = sx;
                zs[k] = sz;
            }
        }

        private Block BuildForward(int b, Block previous)
        {
            // Positions for indices first-1 .. first+BlockSize+1 (the spline needs one before and two after).
            int first = b * BlockSize;
            var xs = new double[BlockSize + 3];
            var zs = new double[BlockSize + 3];
            Trend(first - 1, BlockSize + 3, xs, zs);
            var block = new Block();
            for (int k = 0; k < BlockSize; k++)
            {
                block.X[k] = xs[k + 1];
                block.Z[k] = zs[k + 1];
            }

            double v;
            if (previous == null)
            {
                v = 0.0;
            }
            else
            {
                // Interval (first-1, first): control points first-2 .. first+1.
                var px = new double[1];
                var pz = new double[1];
                Trend(first - 2, 1, px, pz);
                v = previous.V[BlockSize - 1] + PartialLength(px[0], xs[0], xs[1], xs[2], pz[0], zs[0], zs[1], zs[2], 1.0);
            }

            block.V[0] = v;
            for (int k = 1; k < BlockSize; k++)
            {
                // Interval (first+k-1, first+k): control points first+k-2 .. first+k+1 = xs[k-1] .. xs[k+2].
                v += PartialLength(xs[k - 1], xs[k], xs[k + 1], xs[k + 2], zs[k - 1], zs[k], zs[k + 1], zs[k + 2], 1.0);
                block.V[k] = v;
            }

            return block;
        }

        private Block BuildBackward(int b, Block previous)
        {
            // Indices [last - BlockSize + 1, last] with last = -b*BlockSize - 1, stored in increasing order.
            int last = -b * BlockSize - 1;
            int first = last - BlockSize + 1;
            var xs = new double[BlockSize + 3];
            var zs = new double[BlockSize + 3];
            Trend(first - 1, BlockSize + 3, xs, zs);   // xs[k] = index first-1+k
            var block = new Block();
            for (int k = 0; k < BlockSize; k++)
            {
                block.X[k] = xs[k + 1];
                block.Z[k] = zs[k + 1];
            }

            // v at index last+1 (the next block up, or sample 0 = 0) minus the interval (last, last+1).
            double vAbove;
            if (previous == null)
            {
                vAbove = 0.0;
            }
            else
            {
                vAbove = previous.V[0];
            }

            var ex = new double[1];
            var ez = new double[1];
            Trend(last + 2, 1, ex, ez);
            // Interval (last, last+1): control points last-1, last, last+1, last+2 = xs[BlockSize-1], xs[BlockSize], xs[BlockSize+1], ex.
            double v = vAbove - PartialLength(xs[BlockSize - 1], xs[BlockSize], xs[BlockSize + 1], ex[0], zs[BlockSize - 1], zs[BlockSize], zs[BlockSize + 1], ez[0], 1.0);
            block.V[BlockSize - 1] = v;
            for (int k = BlockSize - 2; k >= 0; k--)
            {
                // Interval (first+k, first+k+1): control points first+k-1 .. first+k+2 = xs[k] .. xs[k+3].
                v -= PartialLength(xs[k], xs[k + 1], xs[k + 2], xs[k + 3], zs[k], zs[k + 1], zs[k + 2], zs[k + 3], 1.0);
                block.V[k] = v;
            }

            return block;
        }
    }
}
