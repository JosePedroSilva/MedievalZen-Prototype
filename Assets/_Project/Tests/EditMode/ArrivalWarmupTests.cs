using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Review;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    /// <summary>
    /// The arrival stall hunt (2026-10-09): the warm-up's per-frame budget and glyph sizes, and the walk report's first-use flags
    /// (a scope's first run, named first uses, garbage collections). Pure.
    /// </summary>
    public sealed class ArrivalWarmupTests
    {
        [Test]
        public void TheFirstItemAlwaysRunsThenTheBudgetDecides()
        {
            Assert.That(ArrivalWarmupRules.MayContinue(50f, 0), Is.True);
            Assert.That(ArrivalWarmupRules.MayContinue(ArrivalWarmupRules.BudgetMs - 0.1f, 3), Is.True);
            Assert.That(ArrivalWarmupRules.MayContinue(ArrivalWarmupRules.BudgetMs, 1), Is.False);
            Assert.That(ArrivalWarmupRules.BudgetMs, Is.LessThanOrEqualTo(PropWarmup.WarmBudgetMs));
        }

        [Test]
        public void GlyphSizesCoverTheScaledCanvas()
        {
            var sizes = new List<int>();
            ArrivalWarmupRules.Sizes(20, 0.75f, sizes);
            Assert.That(sizes, Is.EquivalentTo(new[] { 20, 15 }));
            ArrivalWarmupRules.Sizes(21, 0.895f, sizes);
            Assert.That(sizes, Is.EquivalentTo(new[] { 21, 18, 19 }));
            ArrivalWarmupRules.Sizes(28, 1f, sizes);
            Assert.That(sizes, Is.EquivalentTo(new[] { 28 }));
            ArrivalWarmupRules.Sizes(18, float.NaN, sizes);
            Assert.That(sizes, Is.EquivalentTo(new[] { 18 }));
            ArrivalWarmupRules.Sizes(0, 1f, sizes);
            Assert.That(sizes, Is.Empty);
        }

        [Test]
        public void GlyphsHoldEveryPrintableAsciiLetterOnce()
        {
            string g = ArrivalWarmupRules.Glyphs;
            for (char c = ' '; c <= '~'; c++) Assert.That(g.IndexOf(c), Is.GreaterThanOrEqualTo(0), "missing " + c);
            Assert.That(g, Does.Contain("·"));   // the pace toast's separator
            Assert.That(new HashSet<char>(g).Count, Is.EqualTo(g.Length), "a glyph is listed twice");
        }

        [Test]
        public void WalkReportFlagsFirstUses()
        {
            var first = new CostEntry("PeopleManager.Instantiate", 41.3f, true);
            Assert.That(first.ToString(), Is.EqualTo("PeopleManager.Instantiate 41.3 ms (first)"));
            Assert.That(new CostEntry("EndlessWorld", 1f).ToString(), Is.EqualTo("EndlessWorld 1.0 ms"));

            List<object> rows = ReviewWalkBench.CostRows(first, new CostEntry("EndlessWorld", 1f));
            var row0 = (List<KeyValuePair<string, object>>)rows[0];
            var row1 = (List<KeyValuePair<string, object>>)rows[1];
            Assert.That(row0.Count, Is.EqualTo(3));
            Assert.That(row0[2].Key, Is.EqualTo("first"));
            Assert.That(row1.Count, Is.EqualTo(2));

            var frame = new WalkFrame { FrameMs = 277f, Scope1 = first, Firsts = new[] { "Instantiate Villager_Farmer", "Instantiate FX_Smoke_01" }, Gc = 1 };
            Assert.That(ReviewWalkBench.CulpritText(frame),
                Is.EqualTo("PeopleManager.Instantiate 41.3 ms (first) [first: Instantiate Villager_Farmer; Instantiate FX_Smoke_01; gc x1]"));
            Assert.That(ReviewWalkBench.CulpritText(new WalkFrame { FrameMs = 30f, Gc = 2 }), Is.EqualTo("[gc x2]"));
            Assert.That(ReviewWalkBench.CulpritText(new WalkFrame { FrameMs = 30f, Scope1 = new CostEntry("A", 2f) }), Is.EqualTo("A 2.0 ms"));

            List<WalkFrame> frames = new List<WalkFrame>
            {
                new WalkFrame { T = 0f, S = 0.0, Region = "Meadow", FrameMs = 8f },
                new WalkFrame { T = 1f, S = 10.0, Region = "Meadow", FrameMs = 277f, Scope1 = first, Firsts = new[] { "Instantiate FX_Smoke_01" }, Gc = 1 },
            };
            string json = ReviewJson.Write(ReviewWalkBench.Report(null, frames, null, new List<WalkPlace>()));
            Assert.That(json, Does.Contain("\"first\": true"));
            Assert.That(json, Does.Contain("\"firsts\": [\"Instantiate FX_Smoke_01\"]"));
            Assert.That(json, Does.Contain("\"gcCollections\": 1"));
            string[] csv = ReviewWalkBench.Csv(frames).Split('\n');
            Assert.That(csv[0], Does.EndWith(",held,culprit"));
            Assert.That(csv[2], Does.EndWith(",0,PeopleManager.Instantiate 41.3 ms (first) [first: Instantiate FX_Smoke_01; gc x1]"));
        }

        [Test]
        public void TourSidecarWritesCulpritRowsAsJson()
        {
            var fields = new List<KeyValuePair<string, object>>();
            ReviewWalkBench.CulpritFields(fields, "stopWorst", new CostEntry("AmbientFxSpawner", 12f), default, default, null);
            string text = ReviewTourPlan.Sidecar(fields);
            Assert.That(text, Does.Not.Contain("System.Collections"));
            Assert.That(text, Does.Contain("\"name\": \"AmbientFxSpawner\""));
            Assert.That(text, Does.Contain("\"stopWorstEngineMarkers\": []"));
        }
    }
}
