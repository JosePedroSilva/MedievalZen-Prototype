using System;
using MedievalZen.Sound;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    /// <summary>The placeholder footsteps: when a step lands (bones and stride), the surface rules and the synthesised sound.</summary>
    public sealed class FootstepTests
    {
        private const float Dt = 1f / 60f;

        // ------------------------------------------------------------------ cadence

        private static FootstepFrame Walking(float speed) => new FootstepFrame { Dt = Dt, Grounded = true, Speed = speed };

        /// <summary>Steps per second over <paramref name="seconds"/> of steady walking from the stride (no body).</summary>
        private static float StrideRate(float speed, float seconds = 20f)
        {
            var model = new FootstepModel();
            int steps = 0;
            for (float t = 0f; t < seconds; t += Dt)
            {
                if (model.Update(Walking(speed), out FootstepEvent e))
                {
                    steps++;
                    Assert.That(e.Source, Is.EqualTo(FootstepSource.Stride));
                }
            }

            return steps / seconds;
        }

        [Test]
        public void StrideCadenceAtWalkAndSprint()
        {
            Assert.That(StrideRate(1.6f), Is.EqualTo(1.6f / 0.84f).Within(0.08f), "walk: about 1.9 steps/s");
            Assert.That(StrideRate(3.52f), Is.EqualTo(3.52f / 1.25f).Within(0.12f), "sprint: about 2.8 steps/s");
            Assert.That(StrideRate(1.4f), Is.InRange(1.6f, 1.9f), "auto-travel");
        }

        /// <summary>
        /// A synthetic walk cycle: each foot rests on the ground for half the cycle, then swings up 12 cm and settles back with a
        /// smooth landing (sin²), the feet half a cycle apart, plus a millimetre of jitter.
        /// </summary>
        public static float FootHeight(float t, float period, float phaseOffset, uint noise)
        {
            float p = t / period + phaseOffset;
            p -= (float)Math.Floor(p);
            float jitter = ((noise * 2654435761u) >> 8 & 0xFFFF) / 65535f * 0.001f;
            if (p < 0.5f) return jitter;
            float s = (float)Math.Sin(Math.PI * (p - 0.5f) / 0.5f);
            return 0.12f * s * s + jitter;
        }

        private static float BoneRate(float speed, float stepsPerSecond, out int alternations, out int fromBones, float seconds = 20f, float dt = Dt)
        {
            var model = new FootstepModel();
            float period = 2f / stepsPerSecond;
            int steps = 0, lastFoot = -1;
            alternations = 0;
            fromBones = 0;
            uint n = 1;
            for (float t = 0f; t < seconds; t += dt)
            {
                FootstepFrame f = Walking(speed);
                f.Dt = dt;
                f.HasFeet = true;
                f.LeftFoot = FootHeight(t, period, 0f, n++);
                f.RightFoot = FootHeight(t, period, 0.5f, n++);
                if (!model.Update(f, out FootstepEvent e)) continue;
                steps++;
                if (e.Source == FootstepSource.Bones) fromBones++;
                if (e.Foot != lastFoot) alternations++;
                lastFoot = e.Foot;
            }

            return steps / seconds;
        }

        [Test]
        public void BoneCadenceFollowsTheFeetAtWalkAndSprint()
        {
            float walk = BoneRate(1.6f, 1.9f, out int altWalk, out int bonesWalk);
            Assert.That(walk, Is.EqualTo(1.9f).Within(0.08f));
            Assert.That(bonesWalk, Is.GreaterThan((int)(walk * 20f) - 2), "the feet, not the stride, time the steps");
            Assert.That(altWalk, Is.GreaterThan((int)(walk * 20f) - 2), "feet alternate");

            float sprint = BoneRate(3.52f, 2.8f, out int altSprint, out int bonesSprint);
            Assert.That(sprint, Is.EqualTo(2.8f).Within(0.1f));
            Assert.That(bonesSprint, Is.GreaterThan((int)(sprint * 20f) - 2));
            Assert.That(altSprint, Is.GreaterThan((int)(sprint * 20f) - 2));
        }

        [TestCase(30f)]
        [TestCase(144f)]
        public void BoneCadenceHoldsAtLowAndHighFrameRates(float fps)
        {
            float walk = BoneRate(1.6f, 1.9f, out int alternations, out int fromBones, 20f, 1f / fps);
            Assert.That(walk, Is.EqualTo(1.9f).Within(0.08f));
            Assert.That(fromBones, Is.GreaterThan((int)(walk * 20f) - 2));
            Assert.That(alternations, Is.GreaterThan((int)(walk * 20f) - 2));
        }

        [Test]
        public void BonesThatNeverPlantFallBackToTheStride()
        {
            var model = new FootstepModel();
            int steps = 0;
            for (float t = 0f; t < 10f; t += Dt)
            {
                FootstepFrame f = Walking(1.6f);
                f.HasFeet = true;   // a body that does not animate: the feet never move
                if (model.Update(f, out FootstepEvent e))
                {
                    steps++;
                    Assert.That(e.Source, Is.EqualTo(FootstepSource.Stride));
                }
            }

            Assert.That(steps / (10f - FootstepModel.BoneTimeout), Is.EqualTo(1.9f).Within(0.15f));
        }

        [Test]
        public void NoStepWhileStandingSeatedAirborneOrCarried()
        {
            var model = new FootstepModel();
            float period = 2f / 1.9f;
            uint n = 1;
            for (float t = 0f; t < 10f; t += Dt)
            {
                int phase = (int)(t / 2.5f);
                FootstepFrame f = Walking(1.6f);
                if (phase == 0) f.Speed = 0f;                 // standing (turning on the spot still moves the feet)
                else if (phase == 1) f.Seated = true;
                else if (phase == 2) f.Grounded = false;
                else f.Speed = 40f;                            // debug travel: carried faster than feet go
                f.HasFeet = true;
                f.LeftFoot = FootHeight(t, period, 0f, n++);
                f.RightFoot = FootHeight(t, period, 0.5f, n++);
                Assert.That(model.Update(f, out _), Is.False, $"phase {phase} at {t:0.00} s");
            }
        }

        [Test]
        public void NoStepAcrossAHoldOrTeleport()
        {
            var model = new FootstepModel();
            for (float t = 0f; t < 3f; t += Dt) model.Update(Walking(1.6f), out _);
            FootstepFrame hold = Walking(1.6f);
            hold.Hold = true;
            Assert.That(model.Update(hold, out _), Is.False, "the hold frame");
            int after = 0;
            for (float t = 0f; t < 0.45f; t += Dt)
            {
                if (model.Update(Walking(1.6f), out _)) after++;
            }

            Assert.That(after, Is.Zero, "the stride restarts: 0.72 m is less than one stride");
        }

        // ------------------------------------------------------------------ surfaces

        [Test]
        public void SurfaceRules()
        {
            var g = new FootstepGround { RoadDistance = 1f, RoadHalfWidth = 2.5f };
            Assert.That(FootstepSurfaces.Classify(g), Is.EqualTo(FootstepSurface.Road));
            g.Forest = 0.9f;
            Assert.That(FootstepSurfaces.Classify(g), Is.EqualTo(FootstepSurface.Road), "the road through a wood is still the road");
            g.RoadDistance = 6f;
            Assert.That(FootstepSurfaces.Classify(g), Is.EqualTo(FootstepSurface.ForestFloor));
            g.Rock = 0.8f;
            Assert.That(FootstepSurfaces.Classify(g), Is.EqualTo(FootstepSurface.Rock));
            g = new FootstepGround { RoadDistance = 9f, RoadHalfWidth = 2.5f };
            Assert.That(FootstepSurfaces.Classify(g), Is.EqualTo(FootstepSurface.Grass));
            g.RoadDistance = 1f;
            g.Bridge = true;
            Assert.That(FootstepSurfaces.Classify(g), Is.EqualTo(FootstepSurface.Rock), "a stone bridge");
            g.Bridge = false;
            g.Ford = true;
            Assert.That(FootstepSurfaces.Classify(g), Is.EqualTo(FootstepSurface.Water), "a ford");
            g = new FootstepGround { RoadDistance = 30f, RoadHalfWidth = 2.5f, WaterDepth = 0.1f };
            Assert.That(FootstepSurfaces.Classify(g), Is.EqualTo(FootstepSurface.Water), "wading at a lake's edge");
        }

        // ------------------------------------------------------------------ the sound

        /// <summary>Renders one step (stereo, 48 kHz, bus gain 1) into 0.5 s.</summary>
        public static float[] RenderStep(FootstepSurface surface, bool sprint, uint seed, int foot = 0)
        {
            var voice = new FootstepVoice(48000, seed);
            voice.Trigger(new FootstepRequest { Surface = surface, Foot = foot, Sprint = sprint });
            var data = new float[48000 / 2 * 2];
            voice.Render(data, 2, 1f);
            return data;
        }

        /// <summary>Peak level (dBFS) over both channels.</summary>
        public static float PeakDb(float[] stereo)
        {
            float peak = 0f;
            foreach (float s in stereo) peak = Math.Max(peak, Math.Abs(s));
            return SoundMix.GainToDb(peak);
        }

        /// <summary>Seconds until the last sample above 1e-6.</summary>
        public static float SoundingSeconds(float[] stereo)
        {
            int last = 0;
            for (int i = 0; i < stereo.Length; i++)
            {
                if (Math.Abs(stereo[i]) > 1e-6f) last = i;
            }

            return (last / 2 + 1) / 48000f;
        }

        /// <summary>Seconds until the level stays 30 dB under the peak (the audible length).</summary>
        public static float AudibleSeconds(float[] stereo)
        {
            float peak = 0f;
            foreach (float s in stereo) peak = Math.Max(peak, Math.Abs(s));
            float floor = peak * 0.0316f;
            int last = 0;
            for (int i = 0; i < stereo.Length; i++)
            {
                if (Math.Abs(stereo[i]) > floor) last = i;
            }

            return (last / 2 + 1) / 48000f;
        }

        /// <summary>Spectral centroid (Hz) of the left channel, by an FFT of the first 2^14 frames.</summary>
        public static float Centroid(float[] stereo)
        {
            const int n = 16384;
            var re = new double[n];
            var im = new double[n];
            for (int i = 0; i < n && 2 * i < stereo.Length; i++) re[i] = stereo[2 * i];
            Fft(re, im);
            double num = 0.0, den = 0.0;
            for (int k = 1; k < n / 2; k++)
            {
                double mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                num += mag * k * 48000.0 / n;
                den += mag;
            }

            return den > 0.0 ? (float)(num / den) : 0f;
        }

        private static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2.0 * Math.PI / len;
                double wr = Math.Cos(angle), wi = Math.Sin(angle);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1.0, ci = 0.0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                        double nr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = nr;
                    }
                }
            }
        }

        [Test]
        public void EverySurfaceHitsItsPeakEndsSilentAndIsClean([Values(FootstepSurface.Road, FootstepSurface.Grass, FootstepSurface.ForestFloor, FootstepSurface.Rock, FootstepSurface.Water)] FootstepSurface surface)
        {
            for (uint seed = 1; seed <= 40; seed++)
            {
                bool sprint = seed % 5 == 0;
                float[] data = RenderStep(surface, sprint, seed * 7919u, (int)(seed & 1));
                float target = SoundMix.FootstepPeakDb + (sprint ? FootstepVoice.SprintDb : 0f);
                Assert.That(PeakDb(data), Is.EqualTo(target).Within(1.5f), $"{surface} seed {seed} peak");
                Assert.That(SoundingSeconds(data), Is.LessThanOrEqualTo(0.3f), $"{surface} seed {seed} silent again within 300 ms");

                double sum = 0.0;
                float peak = 0f;
                foreach (float s in data)
                {
                    Assert.That(float.IsNaN(s) || float.IsInfinity(s), Is.False);
                    sum += s;
                    peak = Math.Max(peak, Math.Abs(s));
                }

                Assert.That(Math.Abs(sum / (data.Length / 2)), Is.LessThan(peak * 1e-3), $"{surface} seed {seed}: no DC offset");
                Assert.That(Math.Abs(data[0]) + Math.Abs(data[1]), Is.LessThan(peak * 1e-3), "starts at zero");
                int lastFrame = (int)(SoundingSeconds(data) * 48000f) - 1;
                Assert.That(Math.Abs(data[2 * lastFrame]), Is.LessThan(peak * 0.02f), "ends at zero");
            }
        }

        [Test]
        public void SurfacesSoundMeasurablyDifferent()
        {
            float Mean(FootstepSurface s, Func<float[], float> measure)
            {
                double sum = 0.0;
                for (uint seed = 1; seed <= 12; seed++) sum += measure(RenderStep(s, false, seed * 104729u));
                return (float)(sum / 12.0);
            }

            float road = Mean(FootstepSurface.Road, Centroid), grass = Mean(FootstepSurface.Grass, Centroid);
            float forest = Mean(FootstepSurface.ForestFloor, Centroid), rock = Mean(FootstepSurface.Rock, Centroid);
            Assert.That(grass, Is.GreaterThan(road * 1.5f), "grass is brighter than the road");
            Assert.That(forest, Is.GreaterThan(road * 1.5f), "leaf litter crunches above the road's thump");
            Assert.That(rock, Is.InRange(300f, 1500f), "rock is a mid click-thud");
            Assert.That(rock, Is.LessThan(road), "rock has no gritty highs");

            float water = Mean(FootstepSurface.Water, AudibleSeconds);
            foreach (FootstepSurface s in new[] { FootstepSurface.Road, FootstepSurface.Grass, FootstepSurface.ForestFloor, FootstepSurface.Rock })
            {
                Assert.That(water, Is.GreaterThan(Mean(s, AudibleSeconds)), $"water rings longer than {s}");
            }

            Assert.That(Mean(FootstepSurface.Rock, AudibleSeconds), Is.LessThan(Mean(FootstepSurface.Grass, AudibleSeconds)), "rock is the tightest");
        }

        [Test]
        public void BusGainScalesAndSilenceCostsNothing()
        {
            var voice = new FootstepVoice(48000, 99u);
            var quiet = new float[1024];
            voice.Render(quiet, 2, 1f);
            Assert.That(Array.TrueForAll(quiet, s => s == 0f), Is.True);
            var muted = new FootstepVoice(48000, 99u);
            muted.Trigger(new FootstepRequest { Surface = FootstepSurface.Road });
            var off = new float[48000];
            muted.Render(off, 2, 0f);
            Assert.That(Array.TrueForAll(off, s => s == 0f), Is.True, "slider at 0");

            float[] full = RenderStep(FootstepSurface.Road, false, 5u);
            var half = new FootstepVoice(48000, 5u);
            half.Trigger(new FootstepRequest { Surface = FootstepSurface.Road });
            var data = new float[48000];
            half.Render(data, 2, 0.5f);
            Assert.That(PeakDb(data), Is.EqualTo(PeakDb(full) - 6.02f).Within(0.05f), "the bus gain scales the step");
        }

        [Test]
        public void QueuePassesStepsInOrderAndDropsWhenFull()
        {
            var q = new FootstepQueue(4);
            Assert.That(q.Push(new FootstepRequest { Foot = 0 }), Is.True);
            Assert.That(q.Push(new FootstepRequest { Foot = 1 }), Is.True);
            Assert.That(q.Push(new FootstepRequest { Foot = 0, Surface = FootstepSurface.Rock }), Is.True);
            Assert.That(q.Push(new FootstepRequest { Foot = 1 }), Is.False, "a ring of 4 holds 3");
            Assert.That(q.TryPop(out FootstepRequest a) && a.Foot == 0, Is.True);
            Assert.That(q.TryPop(out FootstepRequest b) && b.Foot == 1, Is.True);
            Assert.That(q.TryPop(out FootstepRequest c) && c.Surface == FootstepSurface.Rock, Is.True);
            Assert.That(q.TryPop(out _), Is.False);
        }
    }
}
