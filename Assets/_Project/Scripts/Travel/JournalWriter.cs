using System;
using System.Collections.Generic;
using System.Text;

namespace MedievalZen.Travel
{
    /// <summary>
    /// Writes journal entries as short diary lines in a plain, warm voice: a small phrase table per kind of entry, the variant
    /// picked from a hash of the entry itself (so the same journey always reads the same), times of day in words and distances
    /// rounded as a walker would say them. Only what the entry records is said. Pure; unit tested.
    /// </summary>
    public static class JournalWriter
    {
        // Placeholders: {place} {Place} {akind} {weather} {dist} {Dist} {region} {seat} {near} {time}.
        private static readonly string[] InnFirst =
        {
            "Came to {place} {weather}, {dist} from where I set out.",
            "Reached {place}, an inn by the road, {weather}.",
            "Stopped by the door of {place} {weather}. {Dist} from where I set out.",
            "The road brought me to {place} {weather}.",
            "Found {place} waiting by the road {weather}, {dist} on.",
        };

        private static readonly string[] PlaceFirst =
        {
            "Came to {place} {weather}, {dist} from where I set out.",
            "Walked into {place}, {akind}, {weather}.",
            "Passed through {place} {weather}.",
            "{Place}, {akind}, {weather}. {Dist} from where I set out.",
            "The road led me into {place} {weather}.",
        };

        private static readonly string[] PlaceAgain =
        {
            "Back at {place} {weather}.",
            "Came again to {place} {weather}.",
            "Found myself at {place} once more, {weather}.",
            "Returned to {place} {weather}.",
        };

        private static readonly string[] Bridge =
        {
            "Crossed {place} {weather}.",
            "Over {place}, the stream running beneath.",
            "Crossed the stream by {place} {weather}.",
            "Took {place} over the water {weather}.",
        };

        private static readonly string[] Ford =
        {
            "Waded through {place} {weather}.",
            "Crossed the brook at {place} {weather}.",
            "Over {place}, stepping stones and all.",
            "Forded the water at {place} {weather}.",
        };

        private static readonly string[] Pass =
        {
            "Reached the top of {place} {weather}.",
            "Stood at the summit of {place} {weather}, {dist} from where I set out.",
            "Over the top of {place} at last, {weather}.",
            "Crested {place} {weather}.",
        };

        private static readonly string[] Viewpoint =
        {
            "Stopped at {place} for the view, {weather}.",
            "Looked out from {place} {weather}.",
            "Paused at {place} to look out over the country, {weather}.",
            "Took in the view from {place} {weather}.",
        };

        private static readonly string[] Landmark =
        {
            "Passed close by {place}, {akind}, {weather}.",
            "Saw {place}, {akind}, {weather}.",
            "Had a good look at {place}, {akind}.",
            "Came near {place}, {akind}, {weather}.",
        };

        private static readonly string[] LandmarkAgain =
        {
            "Saw {place} again {weather}.",
            "Passed {place} once more.",
            "{Place} again, {weather}.",
        };

        private static readonly string[] RegionFirst =
        {
            "Came into {region}.",
            "Crossed into {region} {weather}.",
            "The road took me into {region}.",
            "Entered {region}, {dist} from where I set out.",
            "New country: {region}.",
        };

        private static readonly string[] RegionAgain =
        {
            "Back in {region}.",
            "Into {region} again, {weather}.",
            "Crossed back into {region}.",
        };

        private static readonly string[] Rest =
        {
            "Sat a while {seat}{near}, {weather}.",
            "Took my ease {seat}{near}.",
            "Rested my feet {seat}{near}, {weather}.",
            "Stopped to sit {seat}{near} for a time.",
        };

        private static readonly string[] Night =
        {
            "Slept at {place} and woke at {time}.",
            "A night's rest at {place}; up again at {time}.",
            "Took a bed at {place} for the night.",
            "Rested the night at {place}.",
        };

        private static readonly string[] SetOutFrom =
        {
            "Set out on the road from {place}, {weather}.",
            "Took to the road at {place} {weather}.",
            "First steps on the road, from {place}, {weather}.",
            "Began the journey at {place} {weather}.",
        };

        private static readonly string[] SetOutRoad =
        {
            "Set out on the road in {region}, {weather}.",
            "Took to the road {weather}, in {region}.",
            "First steps on the road, in {region}, {weather}.",
            "Began the journey in {region} {weather}.",
        };

        /// <summary>"Day 2".</summary>
        public static string DayHeading(int day) => "Day " + Math.Max(1, day);

        /// <summary>The diary line of <paramref name="e"/>, e.g. "Day 2, mid-morning. Came to The Muddy Fox under a clear sky, ..."
        /// (without the "Day 2, " when <paramref name="withDay"/> is false: the book prints the day as a heading).</summary>
        /// <remarks>Without the day, the time is left out when it is the same words as <paramref name="previous"/>'s. The weather is said only when it changed since <paramref name="previous"/> (the entry before in the journal), on a new day, or after three hours without a word of it; pass null to always say it.</remarks>
        public static string Line(JournalEntry e, bool withDay, JournalEntry previous = null)
        {
            if (e == null) return string.Empty;
            if (e.Kind == JournalKind.Folded) return e.Text ?? string.Empty;
            string time = TimeOfDay(e.Hour);
            bool sameTime = !withDay && previous != null && previous.Kind != JournalKind.Folded && previous.Day == e.Day && TimeOfDay(previous.Hour) == time;
            string prefix = withDay ? DayHeading(e.Day) + ", " + time + ". " : sameTime ? string.Empty : Capitalise(time) + ". ";
            return prefix + Sentence(e, SaysWeather(e, previous));
        }

        /// <summary>The sentence of <paramref name="e"/> without the time.</summary>
        public static string Sentence(JournalEntry e, bool sayWeather = true)
        {
            string[] table = TableFor(e);
            if (table == null) return string.Empty;
            string template = table[Pick(e, table.Length)];
            return Fill(template, e, sayWeather);
        }

        private static string[] TableFor(JournalEntry e)
        {
            switch (e.Kind)
            {
                case JournalKind.SetOut: return string.IsNullOrEmpty(e.Name) ? SetOutRoad : SetOutFrom;
                case JournalKind.Arrive:
                    if (e.Again) return PlaceAgain;
                    return e.Detail == "inn" ? InnFirst : PlaceFirst;
                case JournalKind.Crossing: return e.Detail == "ford" ? Ford : Bridge;
                case JournalKind.Pass: return Pass;
                case JournalKind.Viewpoint: return Viewpoint;
                case JournalKind.Landmark: return e.Again ? LandmarkAgain : Landmark;
                case JournalKind.Region: return e.Again ? RegionAgain : RegionFirst;
                case JournalKind.Rest: return Rest;
                case JournalKind.Night: return Night;
                default: return null;
            }
        }

        /// <summary>True when the line of <paramref name="e"/> mentions the weather after <paramref name="previous"/>.</summary>
        public static bool SaysWeather(JournalEntry e, JournalEntry previous) =>
            previous == null || previous.Day != e.Day || previous.Weather != e.Weather || e.Hour - previous.Hour >= 3f || previous.Kind == JournalKind.Folded;

        private static string Fill(string template, JournalEntry e, bool sayWeather)
        {
            string place = PlaceName(e);
            string dist = Distance(Math.Abs(e.S));
            string region = e.Kind == JournalKind.SetOut ? RegionPhrase(e.Detail) : RegionPhrase(e.Name);
            var sb = new StringBuilder(template);
            sb.Replace("{Place}", Capitalise(place));
            sb.Replace("{place}", place);
            sb.Replace("{akind}", WithArticle(e.Detail));
            sb.Replace("{weather}", sayWeather ? WeatherPhrase(e.Weather, e.Hour) : string.Empty);
            sb.Replace("{Dist}", Capitalise(dist));
            sb.Replace("{dist}", dist);
            sb.Replace("{region}", region);
            sb.Replace("{seat}", string.IsNullOrEmpty(e.Detail) ? "by the road" : e.Detail);
            sb.Replace("{near}", string.IsNullOrEmpty(e.Name) ? string.Empty : " at " + e.Name);
            sb.Replace("{time}", TimeOfDay(e.Hour));
            return Tidy(sb.ToString());
        }

        private static string PlaceName(JournalEntry e)
        {
            if (!string.IsNullOrEmpty(e.Name)) return e.Name;
            switch (e.Kind)
            {
                case JournalKind.Crossing: return e.Detail == "ford" ? "a ford" : "a bridge";
                case JournalKind.Pass: return "the pass";
                case JournalKind.Viewpoint: return "a viewpoint";
                case JournalKind.Night: return "the inn";
                default: return WithArticle(e.Detail);
            }
        }

        /// <summary>"The Wexmoor Downs" → "the Wexmoor Downs" (mid-sentence); empty → "new country".</summary>
        public static string RegionPhrase(string name)
        {
            if (string.IsNullOrEmpty(name)) return "new country";
            return name.StartsWith("The ", StringComparison.Ordinal) ? "the " + name.Substring(4) : name;
        }

        /// <summary>"inn" → "an inn", "standing stones" → "a ring of standing stones", null → "a place".</summary>
        public static string WithArticle(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return "a place";
            if (kind == "standing stones") return "a ring of standing stones";
            char c = char.ToLowerInvariant(kind[0]);
            return (c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u' ? "an " : "a ") + kind;
        }

        /// <summary>The weather in words ("under a clear sky"), empty when unknown.</summary>
        public static string WeatherPhrase(int weather, float hour)
        {
            bool night = hour < 4.5f || hour >= 21.5f;
            switch (weather)
            {
                case 0: return night ? "under the stars" : "under a clear sky";
                case 1: return night ? "in the haze" : "in a warm haze";
                case 2: return "in the mist";
                case 3: return "under a grey sky";
                case 4: return "in a soft drizzle";
                default: return string.Empty;
            }
        }

        /// <summary>The hour in words: dawn, early morning, mid-morning, noon, early afternoon, late afternoon, evening, dusk, night, midnight, the small hours.</summary>
        public static string TimeOfDay(float hour)
        {
            if (float.IsNaN(hour)) return "some hour";
            hour = ((hour % 24f) + 24f) % 24f;
            if (hour < 0.75f || hour >= 23.25f) return "midnight";
            if (hour < 4.5f) return "the small hours";
            if (hour < 6.5f) return "dawn";
            if (hour < 9f) return "early morning";
            if (hour < 11.25f) return "mid-morning";
            if (hour < 12.75f) return "noon";
            if (hour < 15f) return "early afternoon";
            if (hour < 17.5f) return "late afternoon";
            if (hour < 19.5f) return "evening";
            if (hour < 21.5f) return "dusk";
            return "night";
        }

        /// <summary>
        /// A distance as a walker would say it: "hardly a step", "less than a kilometre", "about a kilometre", halves below ten
        /// ("two and a half kilometres"), whole kilometres below twenty, fives below a hundred ("about forty-five kilometres"),
        /// tens above ("about a hundred and twenty kilometres").
        /// </summary>
        public static string Distance(double metres)
        {
            if (double.IsNaN(metres) || double.IsInfinity(metres)) return "some way";
            double km = Math.Abs(metres) / 1000.0;
            if (km < 0.15) return "hardly a step";
            if (km < 0.75) return "less than a kilometre";
            if (km < 1.25) return "about a kilometre";
            if (km < 9.75)
            {
                int halves = (int)Math.Round(km * 2.0, MidpointRounding.AwayFromZero);
                int whole = halves / 2;
                if (halves % 2 == 0) return Number(whole) + " kilometres";
                return whole == 1 ? "a kilometre and a half" : Number(whole) + " and a half kilometres";
            }

            if (km < 19.5) return Number((int)Math.Round(km, MidpointRounding.AwayFromZero)) + " kilometres";
            if (km < 97.5) return "about " + Number(5 * (int)Math.Round(km / 5.0, MidpointRounding.AwayFromZero)) + " kilometres";
            int tens = 10 * (int)Math.Round(km / 10.0, MidpointRounding.AwayFromZero);
            return "about " + Number(tens) + " kilometres";
        }

        private static readonly string[] Units =
        {
            "nought", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen",
            "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
        };

        private static readonly string[] Tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

        /// <summary>A whole number in English words ("forty-five", "a hundred and twenty", "two thousand and ten"); digits beyond a million.</summary>
        public static string Number(int n)
        {
            if (n < 0) return "minus " + Number(-n);
            if (n < 20) return Units[n];
            if (n < 100) return Tens[n / 10] + (n % 10 != 0 ? "-" + Units[n % 10] : string.Empty);
            if (n < 1000)
            {
                string head = n / 100 == 1 ? "a hundred" : Units[n / 100] + " hundred";
                return n % 100 == 0 ? head : head + " and " + Number(n % 100);
            }

            if (n < 1000000)
            {
                string head = n / 1000 == 1 ? "a thousand" : Number(n / 1000) + " thousand";
                int rest = n % 1000;
                if (rest == 0) return head;
                return head + (rest < 100 ? " and " : " ") + Number(rest);
            }

            return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>One line for a whole old day: what was reached, crossed, seen and entered.</summary>
        public static string FoldDay(IReadOnlyList<JournalEntry> day)
        {
            int number = day.Count > 0 ? day[0].Day : 1;
            var places = new List<string>();
            var crossed = new List<string>();
            var seen = new List<string>();
            var regions = new List<string>();
            var slept = new List<string>();
            foreach (JournalEntry e in day)
            {
                switch (e.Kind)
                {
                    case JournalKind.SetOut: if (!string.IsNullOrEmpty(e.Name)) AddOnce(places, e.Name); break;
                    case JournalKind.Arrive: AddOnce(places, PlaceName(e)); break;
                    case JournalKind.Crossing:
                    case JournalKind.Pass: AddOnce(crossed, PlaceName(e)); break;
                    case JournalKind.Viewpoint:
                    case JournalKind.Landmark: AddOnce(seen, PlaceName(e)); break;
                    case JournalKind.Region: AddOnce(regions, RegionPhrase(e.Name)); break;
                    case JournalKind.Night: AddOnce(slept, PlaceName(e)); break;
                    case JournalKind.Folded: if (!string.IsNullOrEmpty(e.Text)) return e.Text; break;
                }
            }

            var parts = new List<string>();
            if (places.Count > 0) parts.Add("came to " + List(places));
            if (crossed.Count > 0) parts.Add("crossed " + List(crossed));
            if (seen.Count > 0) parts.Add("saw " + List(seen));
            if (regions.Count > 0) parts.Add("went into " + List(regions));
            if (slept.Count > 0) parts.Add("slept at " + List(slept));
            if (parts.Count == 0) return DayHeading(number) + ", in short: a day on the road.";
            return DayHeading(number) + ", in short: " + string.Join("; ", parts) + ".";
        }

        /// <summary>The tally lines of the last page.</summary>
        public static string[] TotalsLines(JournalTotals t)
        {
            return new[]
            {
                "Days on the road: " + Number(Math.Max(0, t.Days)),
                "Distance walked: " + Distance(t.Walked),
                "Inns: " + Number(t.Inns),
                "Hamlets, villages and towns: " + Number(t.Villages),
                "Landmarks: " + Number(t.Landmarks),
                "Bridges and fords: " + Number(t.Crossings),
                "Regions: " + Number(t.Regions),
            };
        }

        private static void AddOnce(List<string> list, string s)
        {
            if (!string.IsNullOrEmpty(s) && !list.Contains(s)) list.Add(s);
        }

        private static string List(List<string> items)
        {
            if (items.Count == 1) return items[0];
            return string.Join(", ", items.GetRange(0, items.Count - 1)) + " and " + items[items.Count - 1];
        }

        /// <summary>Index of the phrase variant: an FNV-1a hash of the entry's own fields (stable across runs and platforms).</summary>
        public static int Pick(JournalEntry e, int variants)
        {
            if (variants <= 1) return 0;
            uint h = 2166136261u;
            h = Mix(h, (uint)e.Kind);
            h = Mix(h, (uint)e.Day);
            h = Mix(h, (uint)(int)Math.Round(e.Hour * 60f));
            h = Mix(h, (uint)(long)Math.Round(e.S));
            h = Mix(h, e.Again ? 1u : 0u);
            h = Mix(h, e.Name);
            h = Mix(h, e.Detail);
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            return (int)(h % (uint)variants);
        }

        private static uint Mix(uint h, uint v)
        {
            for (int i = 0; i < 4; i++)
            {
                h ^= (v >> (8 * i)) & 0xFFu;
                h *= 16777619u;
            }

            return h;
        }

        private static uint Mix(uint h, string s)
        {
            if (s == null) return Mix(h, 0xFFFFFFFFu);
            foreach (char c in s) h = Mix(h, c);
            return h;
        }

        /// <summary>Upper-cases the first letter.</summary>
        public static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>Removes the gaps an empty placeholder leaves (" ,", "  ", " .").</summary>
        private static string Tidy(string s)
        {
            string prev;
            do
            {
                prev = s;
                s = s.Replace("  ", " ").Replace(" ,", ",").Replace(" .", ".").Replace(",.", ".").Replace(", .", ".").Replace(",,", ",").Replace(" ;", ";");
            } while (s != prev);

            return s.Trim();
        }
    }
}
