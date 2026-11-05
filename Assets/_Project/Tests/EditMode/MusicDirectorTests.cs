using System;
using System.Collections.Generic;
using MedievalZen.Sound;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    /// <summary>The music director (<see cref="MusicDirector"/>): gaps, threshold, runway, plans, graceful ends, rest, bag. Pure.</summary>
    public class MusicDirectorTests
    {
        private const double Dt = 0.25;

        private static MusicTickInput Input(MusicFrequency f = MusicFrequency.Rare, double placeAhead = double.PositiveInfinity, MusicMoment m = MusicMoment.None,
                                            float strength = 0f) =>
            new MusicTickInput { Dt = Dt, Frequency = f, Speed = 1.4f, PlaceAhead = placeAhead, Moment = m, Strength = strength, TrackReady = true, Mood = MusicMood.OpenDay };

        private static MusicDirector New(int seed = 1) => new MusicDirector(MusicDirectorTrack.FromCatalog(), seed);

        private static void Run(MusicDirector d, MusicTickInput input, double seconds)
        {
            for (double t = 0; t < seconds; t += Dt) d.Tick(input);
        }

        [Test]
        public void GapsPerFrequency()
        {
            Assert.IsTrue(MusicDirector.Gaps(MusicFrequency.Rare, false, out float min, out float late));
            Assert.AreEqual(1200f, min); Assert.AreEqual(2400f, late);
            MusicDirector.Gaps(MusicFrequency.Sometimes, false, out min, out late);
            Assert.AreEqual(480f, min); Assert.AreEqual(960f, late);
            MusicDirector.Gaps(MusicFrequency.Often, false, out min, out late);
            Assert.AreEqual(180f, min); Assert.AreEqual(420f, late);
            MusicDirector.Gaps(MusicFrequency.Rare, true, out min, out late);
            Assert.AreEqual(180f, min); Assert.AreEqual(480f, late);
            Assert.IsFalse(MusicDirector.Gaps(MusicFrequency.Off, true, out _, out _));
        }

        [Test]
        public void ThresholdFallsFromStrongToAnyMoment()
        {
            Assert.IsTrue(float.IsPositiveInfinity(MusicDirector.ThresholdAt(1199, 1200, 2400)));
            Assert.AreEqual(0.8f, MusicDirector.ThresholdAt(1200, 1200, 2400), 1e-5f);
            Assert.AreEqual(0.525f, MusicDirector.ThresholdAt(1800, 1200, 2400), 1e-5f);
            Assert.AreEqual(0.25f, MusicDirector.ThresholdAt(2400, 1200, 2400), 1e-5f);
            Assert.AreEqual(0.25f, MusicDirector.ThresholdAt(9999, 1200, 2400), 1e-5f);
        }

        [Test]
        public void OpeningPieceNeedsAStrongMomentAt3MinAnyBy8()
        {
            var d = New();
            Run(d, Input(m: MusicMoment.Wander, strength: 0.3f), 179);
            Assert.IsNull(d.Piece);
            Run(d, Input(m: MusicMoment.Wander, strength: 0.3f), 200);   // 6.3 min: a weak moment is not enough yet
            Assert.IsNull(d.Piece);
            Run(d, Input(m: MusicMoment.Wander, strength: 0.3f), 110);   // by 8 min it is
            Assert.IsNotNull(d.Piece);
            var e = New();
            Run(e, Input(m: MusicMoment.PassSummit, strength: 0.9f), 181);
            Assert.IsNotNull(e.Piece, "a strong moment at 3 min starts the first piece");
        }

        [Test]
        public void RunwayRuleNeverStartsAPieceLongerThanTheRoadAhead()
        {
            var rng = new Random(7);
            for (int run = 0; run < 20; run++)
            {
                var d = New(run);
                double s = 0, nextPlace = 1500 + rng.NextDouble() * 1500;
                int lastId = 0;
                for (double t = 0; t < 4 * 3600; t += Dt)
                {
                    s += 1.4 * Dt;
                    if (s > nextPlace) nextPlace += 1500 + rng.NextDouble() * 1500;
                    var moment = (MusicMoment)(1 + rng.Next(11));
                    var input = Input(MusicFrequency.Often, nextPlace - s, moment, (float)rng.NextDouble());
                    d.Tick(input);
                    if (d.Piece == null || d.Piece.Id == lastId) continue;
                    lastId = d.Piece.Id;
                    double runway = MusicDirector.RunwaySeconds(input);
                    Assert.LessOrEqual(d.Piece.Plan.Length, runway - MusicDirector.RunwayMargin + 1e-6, $"run {run} at {t:0} s: {d.Piece.Plan.Describe()}");
                    if (moment == MusicMoment.Arrival) Assert.IsTrue(d.Piece.Plan.IsCut, "an arrival is only ever a cut");
                }
            }
        }

        [Test]
        public void PlansFollowTheRunway()
        {
            var d = New();
            MusicDirectorTrack t = d.Tracks[0];
            MusicPlan Plan(double runwaySeconds, MusicMoment m = MusicMoment.Crest) =>
                d.PlanFor(m, Input(placeAhead: MusicDirector.PlaceLead + runwaySeconds * 1.4), 0, false);
            for (int i = 0; i < 20; i++)
            {
                MusicPlan p = Plan(10000);
                Assert.IsFalse(p.IsCut);
                Assert.That(p.Loops, Is.InRange(2, 4));
            }

            Assert.AreEqual(1, Plan(t.Loop + t.Tail + 21).Loops);
            Assert.AreEqual(MusicVariant.Cut60, Plan(t.Cut60 + 21).Cut);
            Assert.AreEqual(MusicVariant.Cut30, Plan(t.Cut30 + 21).Cut);
            Assert.IsNull(Plan(t.Cut30 + 19));
            Assert.IsTrue(Plan(10000, MusicMoment.Arrival).IsCut);
            Assert.AreEqual("1 loop I2>Main", MusicPlan.Looped(1, 90, 5, false).Describe());
            Assert.AreEqual("2 loops I1>I2|Main", MusicPlan.Looped(2, 90, 5, false).Describe());
            Assert.AreEqual("3 loops I1|I2>Main|Main>I2", MusicPlan.Looped(3, 90, 5, false).Describe());
            Assert.AreEqual("4 loops I1|I2|Main|I2", MusicPlan.Looped(4, 90, 5, false).Describe());
        }

        /// <summary>Every source's gain moves smoothly (no step over 0.03 in 10 ms) except at an iteration's composed start.</summary>
        private static void AssertNoCut(MusicPiece p, double endAt, float fade)
        {
            double prevMax = 0;
            var prev = new Dictionary<int, float>();
            for (double t = 0; t < p.Plan.Length + 1; t += 0.01)
            {
                if (!double.IsNaN(endAt) && t >= endAt && !p.EndingEarly) { p.EndAt = t; p.EndFade = fade; }
                for (int i = 0; i < p.Plan.Loops; i++)
                {
                    for (MusicVariant v = MusicVariant.Intensity1; v <= MusicVariant.Cut60; v++)
                    {
                        int key = i * 10 + (int)v;
                        float g = p.Gain(i, v, t);
                        double u = t - p.IterationStart(i);
                        if (prev.TryGetValue(key, out float before) && u > 0.011 && u < p.FileLength - 0.02)
                            Assert.LessOrEqual(Math.Abs(g - before), 0.03f, $"{p.Plan.Describe()} it {i} {v} at {t:0.00}");
                        prev[key] = g;
                    }
                }

                prevMax = Math.Max(prevMax, p.Envelope(t));
            }

            if (!double.IsNaN(endAt)) Assert.IsTrue(p.Finished(endAt + fade + 0.01));
        }

        [Test]
        public void EarlyEndsStepDownAndFadeNeverCut()
        {
            foreach (int loops in new[] { 1, 2, 3, 4 })
            foreach (double endAt in new[] { double.NaN, 1.0, 43.0, 47.0, 95.0, 200.0 })
            foreach (float fade in new[] { MusicDirector.EarlyFade, MusicDirector.OffFade })
            {
                var p = new MusicPiece { Plan = MusicPlan.Looped(loops, 90f, 5.6f, false) };
                AssertNoCut(p, endAt, fade);
            }

            AssertNoCut(new MusicPiece { Plan = MusicPlan.CutOf(MusicVariant.Cut60, 65f, 5f) }, 30.0, MusicDirector.EarlyFade);

            // The step down: Main at an early end becomes Intensity 2 within the 8 s crossfade.
            var q = new MusicPiece { Plan = MusicPlan.Looped(4, 90f, 5f, false), EndAt = 200.0, EndFade = MusicDirector.EarlyFade };
            Assert.AreEqual(0f, q.Gain(2, MusicVariant.Main, 209.0), 1e-5f);
            Assert.Greater(q.Gain(2, MusicVariant.Intensity2, 205.0), 0f);
        }

        [Test]
        public void DirectorEndsGracefullyOnCaptureOffAndF7()
        {
            foreach (string why in new[] { "capture", "off", "f7" })
            {
                var d = New();
                Assert.IsTrue(d.RequestNow(Input()));
                Run(d, Input(), 30);
                MusicTickInput input = Input(why == "off" ? MusicFrequency.Off : MusicFrequency.Rare);
                input.Capture = why == "capture";
                if (why == "f7") Assert.IsTrue(d.RequestEnd());
                d.Tick(input);
                MusicPiece p = d.Piece;
                Assert.IsNotNull(p, why);
                Assert.IsTrue(p.EndingEarly, why);
                Assert.AreEqual(why == "off" ? MusicDirector.OffFade : MusicDirector.EarlyFade, p.EndFade, why);
                Assert.Greater(p.Envelope(p.EndAt + 1.0), 0.5f, why + ": still sounding one second later");
                Run(d, input, p.EndFade + 1);
                Assert.IsNull(d.Piece, why);
            }
        }

        [Test]
        public void OffAndCaptureNeverStart()
        {
            var d = New();
            Run(d, Input(MusicFrequency.Off, m: MusicMoment.PassSummit, strength: 1f), 3 * 3600);
            Assert.AreEqual(0, d.History.Count);
            Assert.IsFalse(d.RequestNow(Input(MusicFrequency.Off)));
            var e = New();
            MusicTickInput capture = Input(m: MusicMoment.PassSummit, strength: 1f);
            capture.Capture = true;
            Run(e, capture, 3 * 3600);
            Assert.AreEqual(0, e.History.Count);
            Assert.IsFalse(e.RequestNow(capture));
        }

        [Test]
        public void RestPiecesStayAtIntensity2AndStandingUpFinishesTheLoop()
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var d = New(seed);
                MusicTickInput seated = Input(placeAhead: 50, m: MusicMoment.Rest, strength: 0.7f);
                seated.Seated = true;
                seated.SeatedSeconds = 100;
                seated.Mood = MusicMood.Rest;
                Run(d, seated, 300);   // the opening threshold falls to the rest moment's 0.7 at about 234 s
                MusicPiece p = d.Piece;
                Assert.IsNotNull(p, "a rest piece is allowed at a place");
                Assert.IsTrue(p.Plan.Rest);
                Assert.LessOrEqual(p.Plan.Loops, 3);
                for (int i = 0; i < p.Plan.Loops; i++)
                {
                    Assert.AreNotEqual(MusicVariant.Main, p.Plan.From[i]);
                    Assert.AreNotEqual(MusicVariant.Main, p.Plan.To[i]);
                }

                double t0 = p.Time;
                int iteration = p.IterationAt(t0);
                MusicTickInput standing = Input(placeAhead: 50);
                d.Tick(standing);
                Assert.IsFalse(p.EndingEarly, "standing up is not an early fade");
                Assert.LessOrEqual(p.Plan.Loops, iteration + 2);
                Run(d, standing, p.Remaining(p.Time) + 1);
                Assert.IsNull(d.Piece);
            }
        }

        [Test]
        public void BagNeverRepeatsAtOnceAndEmptiesBeforeRepeating()
        {
            var d = New(3);
            var order = new List<int>();
            for (int k = 0; k < 95; k++)
            {
                Assert.IsTrue(d.RequestNow(Input()));
                order.Add(d.Piece.Track);
                d.RequestEnd();
                Run(d, Input(), MusicDirector.EarlyFade + 1);
                Assert.IsNull(d.Piece);
            }

            for (int i = 1; i < order.Count; i++) Assert.AreNotEqual(order[i - 1], order[i], $"pick {i}");
            Assert.AreEqual(30, new HashSet<int>(order.GetRange(0, 30)).Count, "the first 30 picks empty the bag");
            Assert.AreEqual(30, new HashSet<int>(order.GetRange(30, 30)).Count, "and so do the next 30");
        }

        [Test]
        public void F8WalksTheCatalogueInOrderWithACrossfade()
        {
            var d = New();
            Assert.IsTrue(d.NextTrack(Input()));
            int first = d.Piece.Track;
            Run(d, Input(), 10);
            Assert.IsTrue(d.NextTrack(Input()));
            Assert.AreEqual((first + 1) % 30, d.Piece.Track);
            Assert.IsNotNull(d.SwitchedOut);
            Assert.AreEqual(MusicDirector.SwitchFade, d.SwitchedOut.EndFade);
            Run(d, Input(), 4);
            Assert.IsNull(d.SwitchedOut);
        }
    }
}
