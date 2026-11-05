using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MedievalZen.UI;

namespace MedievalZen.Travel
{
    /// <summary>What a journal entry tells of.</summary>
    public enum JournalKind
    {
        /// <summary>The first entry of a journey.</summary>
        SetOut = 0,
        /// <summary>Reaching an inn, hamlet, village (or any other settlement kind).</summary>
        Arrive = 1,
        /// <summary>Crossing a bridge or a ford (<see cref="JournalEntry.Detail"/> "bridge" or "ford").</summary>
        Crossing = 2,
        /// <summary>The top of a mountain pass.</summary>
        Pass = 3,
        /// <summary>A viewpoint by the road.</summary>
        Viewpoint = 4,
        /// <summary>A landmark seen from close by (<see cref="JournalEntry.Detail"/> its kind: windmill, chapel ...).</summary>
        Landmark = 5,
        /// <summary>Entering a named region.</summary>
        Region = 6,
        /// <summary>Sitting a while (<see cref="JournalEntry.Detail"/> the seat phrase, "on a log").</summary>
        Rest = 7,
        /// <summary>A night at an inn.</summary>
        Night = 8,
        /// <summary>A whole old day folded into one line (<see cref="JournalEntry.Text"/>).</summary>
        Folded = 9,
    }

    /// <summary>One line of the journal: only facts the game knows (when, where, the weather, what). Text is written from it by <see cref="JournalWriter"/>.</summary>
    public sealed class JournalEntry
    {
        /// <summary>What happened.</summary>
        public JournalKind Kind;
        /// <summary>Day of the day cycle (1 = the first day).</summary>
        public int Day = 1;
        /// <summary>Hour of the day, 0..24.</summary>
        public float Hour;
        /// <summary>Weather state (<c>WeatherState</c> as int: 0 clear .. 4 light rain), −1 unknown.</summary>
        public int Weather = -1;
        /// <summary>Road distance of the traveller from the journey's start, metres.</summary>
        public double S;
        /// <summary>True when the place (or region) was visited before.</summary>
        public bool Again;
        /// <summary>The place, landmark or region named; null when unnamed.</summary>
        public string Name;
        /// <summary>Kind word ("inn", "village", "bridge", "windmill") or the seat phrase of a rest, or the region of the setting out.</summary>
        public string Detail;
        /// <summary>The finished line of a folded day; null otherwise.</summary>
        public string Text;

        /// <summary>A copy.</summary>
        public JournalEntry Clone() => (JournalEntry)MemberwiseClone();
    }

    /// <summary>The tallies of the last page.</summary>
    public struct JournalTotals
    {
        public int Days;
        public double Walked;
        public int Inns;
        public int Villages;
        public int Landmarks;
        public int Crossings;
        public int Regions;
        public int Entries;
    }

    /// <summary>
    /// The traveller's journal of one journey (one road seed): entries in order, the places seen (for the tallies and the
    /// "back at" lines, kept when old days fold) and the distance walked. Pure data with a versioned flat-JSON round trip
    /// (PlayerPrefs key <see cref="PrefsKeyFor"/>, written by <see cref="JournalKeeper"/> when an entry is added). Unit tested.
    /// </summary>
    public sealed class Journal
    {
        /// <summary>Version written by <see cref="ToJson"/>.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Default cap on the number of entries: beyond it the oldest days fold into one line each.</summary>
        public const int DefaultCap = 600;

        /// <summary>Seen-place categories (first letter of a <see cref="Seen"/> key).</summary>
        public const char InnKey = 'i', VillageKey = 'v', LandmarkKey = 'l', CrossingKey = 'c', PassKey = 'p', ViewKey = 'w', RegionKey = 'r';

        /// <summary>PlayerPrefs key of the journal of road <paramref name="seed"/>.</summary>
        public static string PrefsKeyFor(int seed) => "MedievalZen.Journal." + seed.ToString(CultureInfo.InvariantCulture);

        /// <summary>Road seed of the journey.</summary>
        public int Seed;

        /// <summary>Entries, oldest first.</summary>
        public readonly List<JournalEntry> Entries = new List<JournalEntry>();

        /// <summary>Places and regions seen, as category letter + id ("i1234", "r7").</summary>
        public readonly HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Metres walked (or auto-travelled) along the road; teleports excluded.</summary>
        public double Walked;

        /// <summary>Day of the first entry (0 = none yet).</summary>
        public int FirstDay;

        /// <summary>Highest day of any entry so far.</summary>
        public int LastDay;

        /// <summary>Entry cap (<see cref="DefaultCap"/>).</summary>
        public int Cap = DefaultCap;

        public Journal(int seed) { Seed = seed; }

        /// <summary>Key of a place in <see cref="Seen"/>.</summary>
        public static string SeenKey(char category, long id) => category + id.ToString(CultureInfo.InvariantCulture);

        /// <summary>Appends <paramref name="entry"/> (folding the oldest days when over the cap) and returns it.</summary>
        public JournalEntry Add(JournalEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (entry.Day < 1) entry.Day = 1;
            if (FirstDay < 1 || entry.Day < FirstDay) FirstDay = entry.Day;
            if (entry.Day > LastDay) LastDay = entry.Day;
            Entries.Add(entry);
            Fold();
            return entry;
        }

        /// <summary>Folds the oldest unfolded days into one line each until the cap holds; drops the oldest lines if every day is already one line.</summary>
        public void Fold()
        {
            int cap = Math.Max(2, Cap);
            while (Entries.Count > cap)
            {
                int day = -1;
                for (int i = 0; i + 1 < Entries.Count; i++)
                {
                    // The first day with two or more lines (never the newest entry's day while it is still being written).
                    int d = Entries[i].Day;
                    int n = 0;
                    for (int j = i; j < Entries.Count && Entries[j].Day == d; j++) n++;
                    if (n >= 2 && d != Entries[Entries.Count - 1].Day) { day = d; break; }
                    i += n - 1;
                }

                if (day < 0)
                {
                    Entries.RemoveAt(0);
                    continue;
                }

                int first = Entries.FindIndex(e => e.Day == day);
                int count = 0;
                while (first + count < Entries.Count && Entries[first + count].Day == day) count++;
                List<JournalEntry> range = Entries.GetRange(first, count);
                var folded = new JournalEntry
                {
                    Kind = JournalKind.Folded, Day = day, Hour = range[0].Hour, Weather = range[0].Weather, S = range[0].S,
                    Text = JournalWriter.FoldDay(range),
                };
                Entries.RemoveRange(first, count);
                Entries.Insert(first, folded);
            }
        }

        /// <summary>Count of seen keys in <paramref name="category"/>.</summary>
        public int SeenCount(char category)
        {
            int n = 0;
            foreach (string key in Seen)
            {
                if (key.Length > 0 && key[0] == category) n++;
            }

            return n;
        }

        /// <summary>The tallies, with today being <paramref name="currentDay"/> (0 = the last entry's day).</summary>
        public JournalTotals Totals(int currentDay)
        {
            int last = Math.Max(LastDay, currentDay);
            return new JournalTotals
            {
                Days = FirstDay >= 1 ? Math.Max(1, last - FirstDay + 1) : 0,
                Walked = Walked,
                Inns = SeenCount(InnKey),
                Villages = SeenCount(VillageKey),
                Landmarks = SeenCount(LandmarkKey),
                Crossings = SeenCount(CrossingKey),
                Regions = SeenCount(RegionKey),
                Entries = Entries.Count,
            };
        }

        // ------------------------------------------------------------------ JSON

        /// <summary>
        /// One flat JSON object: <c>{"version":1,"seed":..,"firstDay":..,"lastDay":..,"walked":..,"seen":"i12 v5 r3","count":n,"e0":"..",..}</c>.
        /// Each entry is one string of '|'-separated fields: kind, day, hour, weather, s, again, name, detail, text.
        /// </summary>
        public string ToJson()
        {
            var sb = new StringBuilder(256 + Entries.Count * 64);
            sb.Append("{\"version\":").Append(CurrentVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"seed\":").Append(Seed.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"firstDay\":").Append(FirstDay.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"lastDay\":").Append(LastDay.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"walked\":").Append(Math.Round(Walked, 1).ToString("0.#", CultureInfo.InvariantCulture));
            var seen = new List<string>(Seen);
            seen.Sort(StringComparer.Ordinal);
            sb.Append(",\"seen\":");
            AppendString(sb, string.Join(" ", seen));
            sb.Append(",\"count\":").Append(Entries.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < Entries.Count; i++)
            {
                sb.Append(",\"e").Append(i.ToString(CultureInfo.InvariantCulture)).Append("\":");
                AppendString(sb, EncodeEntry(Entries[i]));
            }

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Reads a saved journal. As for the journey blob: no version (0) or an older one is read field by field, missing fields
        /// keep their defaults and an entry with fewer fields than today's is read as far as it goes; a newer version, a blob that
        /// is not a JSON object or one without a seed is rejected (false, <paramref name="result"/> null).
        /// </summary>
        public static bool TryFromJson(string json, out Journal result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json) || !FlatJson.TryParse(json, out Dictionary<string, object> values)) return false;
            int version = 0;
            if (values.TryGetValue("version", out object v) && v is double dv) version = (int)dv;
            if (version > CurrentVersion) return false;
            if (!values.TryGetValue("seed", out object seedValue) || !(seedValue is double seed) || seed < int.MinValue || seed > int.MaxValue) return false;

            var journal = new Journal((int)seed);
            journal.FirstDay = Int(values, "firstDay");
            journal.LastDay = Int(values, "lastDay");
            if (values.TryGetValue("walked", out object w) && w is double walked && !double.IsNaN(walked) && !double.IsInfinity(walked)) journal.Walked = Math.Max(0.0, walked);
            if (values.TryGetValue("seen", out object s) && s is string seenText)
            {
                foreach (string key in seenText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) journal.Seen.Add(key);
            }

            // Version 0 (before "count"): read e0, e1 ... until one is missing.
            int count = values.TryGetValue("count", out object c) && c is double dc ? (int)Math.Max(0, Math.Min(100000, dc)) : int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (!values.TryGetValue("e" + i.ToString(CultureInfo.InvariantCulture), out object e) || !(e is string line)) break;
                JournalEntry entry = DecodeEntry(line);
                if (entry != null) journal.Entries.Add(entry);
            }

            foreach (JournalEntry entry in journal.Entries)
            {
                if (journal.FirstDay < 1 || entry.Day < journal.FirstDay) journal.FirstDay = entry.Day;
                if (entry.Day > journal.LastDay) journal.LastDay = entry.Day;
            }

            result = journal;
            return true;
        }

        /// <summary>The journal saved as <paramref name="json"/> when it parses and belongs to <paramref name="seed"/>, else a new empty one for that seed.</summary>
        public static Journal LoadOrNew(int seed, string json)
        {
            if (TryFromJson(json, out Journal saved) && saved.Seed == seed) return saved;
            return new Journal(seed);
        }

        private static int Int(Dictionary<string, object> values, string key) =>
            values.TryGetValue(key, out object v) && v is double d && d >= 0 && d < int.MaxValue ? (int)d : 0;

        private static string Clean(string s) => s == null ? string.Empty : s.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');

        private static string EncodeEntry(JournalEntry e)
        {
            var sb = new StringBuilder(64);
            sb.Append(((int)e.Kind).ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(e.Day.ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(Math.Round(e.Hour, 3).ToString("0.###", CultureInfo.InvariantCulture)).Append('|');
            sb.Append(e.Weather.ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(Math.Round(e.S, 1).ToString("0.#", CultureInfo.InvariantCulture)).Append('|');
            sb.Append(e.Again ? '1' : '0').Append('|');
            sb.Append(e.Name == null ? "~" : Clean(e.Name)).Append('|');
            sb.Append(e.Detail == null ? "~" : Clean(e.Detail)).Append('|');
            sb.Append(e.Text == null ? "~" : Clean(e.Text));
            return sb.ToString();
        }

        private static JournalEntry DecodeEntry(string line)
        {
            string[] f = line.Split('|');
            if (f.Length < 5) return null;
            if (!int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kind) || kind < 0) return null;
            var e = new JournalEntry { Kind = (JournalKind)kind };
            if (int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day)) e.Day = Math.Max(1, day);
            if (float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float hour) && hour >= 0f && hour < 24f) e.Hour = hour;
            if (int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int weather)) e.Weather = weather;
            if (double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double s) && !double.IsNaN(s) && !double.IsInfinity(s)) e.S = s;
            if (f.Length > 5) e.Again = f[5] == "1";
            if (f.Length > 6) e.Name = Field(f[6]);
            if (f.Length > 7) e.Detail = Field(f[7]);
            if (f.Length > 8) e.Text = Field(f[8]);
            return e;
        }

        private static string Field(string s) => s == "~" ? null : s;

        private static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }

            sb.Append('"');
        }
    }
}
