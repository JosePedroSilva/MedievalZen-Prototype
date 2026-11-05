using System.Collections.Generic;
using System.Linq;
using MedievalZen.Endless;
using MedievalZen.Travel;
using NUnit.Framework;

namespace MedievalZen.Tests
{
    public sealed class JournalTests
    {
        // A straight road along +X: road distance s is the point (s, 0).
        private static Waypoint Place(int id, WaypointKind kind, string name, double s, float radius = 40f, double offset = 0.0) =>
            new Waypoint { Id = id, Kind = kind, Name = name, S = s, X = s, Z = offset, Radius = radius };

        private static JournalTick At(double s, int region = 0, bool capture = false, bool suppressed = false, float hour = 10f) =>
            new JournalTick { S = s, X = s, Z = 0.0, Day = 1, Hour = hour, Weather = 0, RegionIndex = region, RegionName = "The Wexmoor Downs", Capture = capture, Suppressed = suppressed };

        private static void Walk(JournalRecorder r, List<Waypoint> near, double from, double to, double step = 2.0, bool capture = false, int region = 0)
        {
            int n = (int)System.Math.Ceiling(System.Math.Abs(to - from) / step);
            for (int i = 0; i <= n; i++)
            {
                double s = from + (to - from) * i / System.Math.Max(1, n);
                r.Tick(At(s, region, capture), near);
            }
        }

        private static int Count(Journal j, JournalKind kind) => j.Entries.Count(e => e.Kind == kind);

        [Test]
        public void FirstTickWritesTheSettingOut()
        {
            var j = new Journal(7);
            var r = new JournalRecorder(j);
            r.Tick(At(0.0), new List<Waypoint> { Place(1, WaypointKind.Inn, "The Muddy Fox", 20.0, 30f) });
            Assert.AreEqual(1, j.Entries.Count);
            Assert.AreEqual(JournalKind.SetOut, j.Entries[0].Kind);
            Assert.AreEqual("The Muddy Fox", j.Entries[0].Name);
            r.Tick(At(2.0), new List<Waypoint> { Place(1, WaypointKind.Inn, "The Muddy Fox", 20.0, 30f) });
            Assert.AreEqual(1, j.Entries.Count, "the inn set out from is not arrived at again");
        }

        [Test]
        public void OneEntryPerVisitAndAgainOnlyAfterReallyLeaving()
        {
            var j = new Journal(7);
            var r = new JournalRecorder(j);
            var near = new List<Waypoint> { Place(5, WaypointKind.Village, "Ashford", 1000.0, 40f) };
            Walk(r, near, 0, 1000);
            Assert.AreEqual(1, Count(j, JournalKind.Arrive));
            Walk(r, near, 1000, 1100);   // to the edge and a little beyond (60 m out): not a real leaving
            Walk(r, near, 1100, 1000);
            Assert.AreEqual(1, Count(j, JournalKind.Arrive));
            Walk(r, near, 1000, 1500);   // well away ...
            Walk(r, near, 1500, 1000);   // ... and back
            Assert.AreEqual(2, Count(j, JournalKind.Arrive));
            JournalEntry again = j.Entries.Last(e => e.Kind == JournalKind.Arrive);
            Assert.IsTrue(again.Again);
            StringAssert.Contains("Ashford", JournalWriter.Line(again, true));
            Assert.AreEqual(1, j.Totals(1).Villages);
        }

        [Test]
        public void WalkingPastCounts()
        {
            var j = new Journal(7);
            var r = new JournalRecorder(j);
            var near = new List<Waypoint>
            {
                Place(1, WaypointKind.Hamlet, "Low Brigg", 600.0, 30f),
                Place(2, WaypointKind.Bridge, "Mossy Bridge", 900.0, 6f),
                Place(3, WaypointKind.Ford, null, 950.0, 6f),                          // unnamed: not recorded
                Place(4, WaypointKind.Landmark, "Hollin Mill", 1300.0, 8f, 180.0),       // 180 m off the road
                Place(5, WaypointKind.Landmark, "Far Tower", 1500.0, 8f, 900.0),         // too far, no view point
                Place(6, WaypointKind.Milestone, null, 1000.0, 0f),
                Place(7, WaypointKind.Pass, "Hob's Pass", 1700.0, 0f),
                Place(8, (WaypointKind)57, "Kingsbridge", 2000.0, 120f),               // a kind this code does not know
            };
            Walk(r, near, 0, 2500, 1.5);
            Assert.AreEqual(2, Count(j, JournalKind.Arrive));
            Assert.AreEqual(1, Count(j, JournalKind.Crossing));
            Assert.AreEqual(1, Count(j, JournalKind.Landmark));
            Assert.AreEqual(1, Count(j, JournalKind.Pass));
            Assert.AreEqual("town", j.Entries.Last(e => e.Kind == JournalKind.Arrive).Detail);
            if (System.Enum.IsDefined(typeof(WaypointKind), "City")) Assert.AreEqual("city", JournalRecorder.SettlementWord((WaypointKind)System.Enum.Parse(typeof(WaypointKind), "City")));
            Assert.AreEqual(2500.0, j.Walked, 1.0);
        }

        [Test]
        public void ATeleportRecordsOnlyTheArrival()
        {
            var j = new Journal(7);
            var r = new JournalRecorder(j);
            var near = new List<Waypoint>
            {
                Place(1, WaypointKind.Village, "Ashford", 2000.0),
                Place(2, WaypointKind.Bridge, "Mossy Bridge", 3000.0, 6f),
                Place(3, WaypointKind.Inn, "The Drowsy Fox", 5040.0, 30f),
            };
            r.Tick(At(0.0), near);
            int before = j.Entries.Count;
            r.Tick(At(100.0, suppressed: true), near);          // the fade and the hold of a "Go now"
            r.Tick(At(5000.0, region: 3), near);                // landed just before the inn, in another region
            Walk(r, near, 5000.0, 5100.0, 2.0, region: 3);      // and walks in
            List<JournalEntry> added = j.Entries.Skip(before).ToList();
            Assert.AreEqual(1, added.Count, string.Join(" / ", added.Select(e => JournalWriter.Line(e, true))));
            Assert.AreEqual("The Drowsy Fox", added[0].Name);
            Assert.Less(j.Walked, 150.0, "a teleport is not walked");
        }

        [Test]
        public void NothingIsRecordedDuringACapture()
        {
            var j = new Journal(7);
            var r = new JournalRecorder(j);
            var near = new List<Waypoint> { Place(1, WaypointKind.Village, "Ashford", 1000.0), Place(2, WaypointKind.Inn, "The Owl", 3000.0, 30f) };
            r.Tick(At(0.0), near);
            int before = j.Entries.Count;
            Walk(r, near, 0, 1000, 5.0, capture: true);         // a tour drives through the village ...
            Walk(r, near, 1000, 1040, 2.0);                     // ... and leaves the traveller in it
            Assert.AreEqual(before, j.Entries.Count);
            Assert.IsFalse(r.Rest("on a log", null, At(1040, capture: true)));
            Walk(r, near, 1040, 3000, 2.0);
            Assert.AreEqual(before + 1, j.Entries.Count);
            Assert.AreEqual("The Owl", j.Entries.Last().Name);
        }

        [Test]
        public void RegionsAreRecordedOnEntering()
        {
            var j = new Journal(7);
            var r = new JournalRecorder(j);
            var near = new List<Waypoint>();
            Walk(r, near, 0, 100, 2.0, region: 0);
            Walk(r, near, 100, 200, 2.0, region: 1);
            Walk(r, near, 200, 100, 2.0, region: 0);
            Assert.AreEqual(2, Count(j, JournalKind.Region));
            Assert.IsTrue(j.Entries.Last().Again);
        }

        [Test]
        public void PhrasesAreDeterministicAndVary()
        {
            var variants = new HashSet<string>();
            for (int i = 0; i < 60; i++)
            {
                var e = new JournalEntry { Kind = JournalKind.Arrive, Day = 1 + i % 3, Hour = 6f + i * 0.2f, Weather = i % 5, S = 1000.0 * i, Name = "Place" + i, Detail = "village" };
                string a = JournalWriter.Sentence(e);
                Assert.AreEqual(a, JournalWriter.Sentence(e.Clone()));
                variants.Add(a.Replace("Place" + i, "#").Substring(0, 10));
            }

            Assert.GreaterOrEqual(variants.Count, 4);
            var muddy = new JournalEntry { Kind = JournalKind.Arrive, Day = 2, Hour = 10f, Weather = 0, S = 19100.0, Name = "The Muddy Fox", Detail = "inn" };
            string line = JournalWriter.Line(muddy, true);
            StringAssert.StartsWith("Day 2, mid-morning. ", line);
            StringAssert.Contains("The Muddy Fox", line);
            StringAssert.StartsWith("Mid-morning. ", JournalWriter.Line(muddy, false));
            Assert.IsFalse(line.Contains("{"), line);
        }

        [Test]
        public void TimesAndDistancesInWords()
        {
            Assert.AreEqual("dawn", JournalWriter.TimeOfDay(5.5f));
            Assert.AreEqual("mid-morning", JournalWriter.TimeOfDay(10f));
            Assert.AreEqual("noon", JournalWriter.TimeOfDay(12f));
            Assert.AreEqual("late afternoon", JournalWriter.TimeOfDay(16f));
            Assert.AreEqual("dusk", JournalWriter.TimeOfDay(20f));
            Assert.AreEqual("the small hours", JournalWriter.TimeOfDay(2f));
            Assert.AreEqual("midnight", JournalWriter.TimeOfDay(23.9f));
            Assert.AreEqual("hardly a step", JournalWriter.Distance(80));
            Assert.AreEqual("less than a kilometre", JournalWriter.Distance(600));
            Assert.AreEqual("about a kilometre", JournalWriter.Distance(1100));
            Assert.AreEqual("a kilometre and a half", JournalWriter.Distance(1600));
            Assert.AreEqual("two and a half kilometres", JournalWriter.Distance(2600));
            Assert.AreEqual("nineteen kilometres", JournalWriter.Distance(19200));
            Assert.AreEqual("about forty-five kilometres", JournalWriter.Distance(45200));
            Assert.AreEqual("about a hundred and twenty kilometres", JournalWriter.Distance(123000));
            Assert.AreEqual("two thousand and ten", JournalWriter.Number(2010));
        }

        [Test]
        public void JsonRoundTripAndVersions()
        {
            var j = new Journal(4242) { Walked = 12345.6 };
            j.Add(new JournalEntry { Kind = JournalKind.SetOut, Day = 1, Hour = 7.25f, Weather = 2, S = 0, Detail = "The \"Quoted\" Fells" });
            j.Add(new JournalEntry { Kind = JournalKind.Arrive, Day = 2, Hour = 13.5f, Weather = -1, S = -1520.5, Name = "Bar|Pipe", Detail = "inn", Again = true });
            j.Add(new JournalEntry { Kind = JournalKind.Folded, Day = 2, Text = "Day 2, in short: came to Ashford." });
            j.Seen.Add("i12");
            j.Seen.Add("r3");
            Assert.IsTrue(Journal.TryFromJson(j.ToJson(), out Journal back));
            Assert.AreEqual(4242, back.Seed);
            Assert.AreEqual(3, back.Entries.Count);
            Assert.AreEqual(12345.6, back.Walked, 0.05);
            Assert.AreEqual("The \"Quoted\" Fells", back.Entries[0].Detail);
            Assert.IsNull(back.Entries[0].Name);
            Assert.AreEqual("Bar/Pipe", back.Entries[1].Name);
            Assert.IsTrue(back.Entries[1].Again);
            Assert.AreEqual(-1520.5, back.Entries[1].S, 0.01);
            Assert.AreEqual(13.5f, back.Entries[1].Hour, 0.001f);
            Assert.AreEqual(JournalKind.Folded, back.Entries[2].Kind);
            Assert.AreEqual(1, back.Totals(0).Inns);
            Assert.AreEqual(2, back.Totals(0).Days);
            Assert.AreEqual(back.ToJson(), j.ToJson().Replace("Bar|Pipe", "Bar/Pipe"));

            // An unversioned blob without "count", with short entries, is read as far as it goes.
            Assert.IsTrue(Journal.TryFromJson("{\"seed\":5,\"e0\":\"1|3|9.5|0|2500\",\"e1\":\"6|3|10|4|2600|0|The Fells\"}", out Journal old));
            Assert.AreEqual(2, old.Entries.Count);
            Assert.AreEqual(3, old.FirstDay);
            Assert.AreEqual("The Fells", old.Entries[1].Name);
            Assert.IsFalse(Journal.TryFromJson("{\"version\":99,\"seed\":5}", out _));
            Assert.IsFalse(Journal.TryFromJson("not json", out _));
            Assert.IsFalse(Journal.TryFromJson("{\"version\":1}", out _));
        }

        [Test]
        public void TheCapFoldsTheOldestDays()
        {
            var j = new Journal(1) { Cap = 50 };
            for (int day = 1; day <= 10; day++)
            {
                for (int k = 0; k < 10; k++)
                {
                    j.Seen.Add(Journal.SeenKey(Journal.VillageKey, day * 100 + k));
                    j.Add(new JournalEntry { Kind = JournalKind.Arrive, Day = day, Hour = 8f + k, Weather = 0, S = day * 10000.0 + k * 900.0, Name = "Village " + day + "." + k, Detail = "village" });
                }
            }

            Assert.LessOrEqual(j.Entries.Count, 50);
            Assert.AreEqual(JournalKind.Folded, j.Entries[0].Kind);
            StringAssert.StartsWith("Day 1, in short: came to Village 1.0, ", j.Entries[0].Text);
            Assert.AreEqual(10, j.Entries.Count(e => e.Day == 10), "the newest day is never folded");
            Assert.AreEqual(100, j.Totals(10).Villages, "the tallies outlive the folding");
            Assert.AreEqual(10, j.Totals(10).Days);
        }

        [Test]
        public void ANewSeedStartsFreshAndAnOldSeedComesBack()
        {
            var first = new Journal(1);
            first.Add(new JournalEntry { Kind = JournalKind.SetOut, Day = 1, Hour = 8f, Detail = "The Fells" });
            string saved = first.ToJson();
            Journal other = Journal.LoadOrNew(2, saved);
            Assert.AreEqual(2, other.Seed);
            Assert.AreEqual(0, other.Entries.Count);
            Journal again = Journal.LoadOrNew(1, saved);
            Assert.AreEqual(1, again.Entries.Count);
            Assert.AreEqual(0, Journal.LoadOrNew(3, null).Entries.Count);
            Assert.AreNotEqual(Journal.PrefsKeyFor(1), Journal.PrefsKeyFor(2));
        }
    }
}
