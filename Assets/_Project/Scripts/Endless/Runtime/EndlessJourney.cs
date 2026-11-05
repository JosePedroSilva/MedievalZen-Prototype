using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MedievalZen.UI;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The saved state of an endless-road journey: which road (seed) and how far along it the traveller is, plus the day
    /// and hour. Pure data with a versioned flat-JSON round trip (stored in PlayerPrefs under <see cref="PrefsKey"/> by
    /// <see cref="EndlessWorld"/>, separate from the Journey menu's settings). Unit tested.
    /// </summary>
    public sealed class EndlessJourney
    {
        /// <summary>PlayerPrefs key of the journey blob.</summary>
        public const string PrefsKey = "MedievalZen.EndlessJourney";

        /// <summary>Version written by <see cref="ToJson"/>.</summary>
        public const int CurrentVersion = 2;

        /// <summary>World seed of the road.</summary>
        public int Seed = 12345;

        /// <summary>Distance along the road from the journey's start, in metres (may be negative).</summary>
        public double S;

        /// <summary>Day counter (1 = the first day); 0 = not saved (keep the scene's).</summary>
        public int Day;

        /// <summary>Hour of the day, 0..24; negative = not saved.</summary>
        public float Hour = -1f;

        /// <summary>Version 2: the traveller was on horseback (a version-1 blob reads as on foot).</summary>
        public bool Mounted;

        /// <summary>Version 2: the riding gait, 0 walk, 1 trot, 2 canter (clamped).</summary>
        public int Gait;

        /// <summary>True when a day and hour were saved.</summary>
        public bool HasTime => Day >= 1 && Hour >= 0f && Hour < 24f;

        /// <summary>A fresh journey on <paramref name="seed"/> from the start of the road.</summary>
        public static EndlessJourney New(int seed) => new EndlessJourney { Seed = seed, S = 0.0, Day = 0, Hour = -1f };

        /// <summary>A copy of this object.</summary>
        public EndlessJourney Clone() => (EndlessJourney)MemberwiseClone();

        /// <summary>
        /// Brings the values into range: a non-finite or absurd distance (beyond ±10 000 km) becomes 0, the hour wraps into
        /// 0..24 (or is dropped when not finite), a day below 1 drops the saved time. Returns this.
        /// </summary>
        public EndlessJourney Clamp()
        {
            if (double.IsNaN(S) || double.IsInfinity(S) || Math.Abs(S) > 1e7) S = 0.0;
            if (float.IsNaN(Hour) || float.IsInfinity(Hour)) Hour = -1f;
            else if (Hour >= 0f) Hour %= 24f;
            if (Day < 1) { Day = 0; Hour = -1f; }
            if (Hour < 0f) Day = 0;
            Gait = Math.Max(0, Math.Min(2, Gait));
            return this;
        }

        /// <summary>One flat JSON object, e.g. <c>{"version":2,"seed":12345,"s":5321.25,"day":3,"hour":14.5,"mounted":1,"gait":1}</c> (the riding fields only when mounted).</summary>
        public string ToJson()
        {
            var sb = new StringBuilder(96);
            sb.Append("{\"version\":").Append(CurrentVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"seed\":").Append(Seed.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"s\":").Append(S.ToString("R", CultureInfo.InvariantCulture));
            if (HasTime)
            {
                sb.Append(",\"day\":").Append(Day.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"hour\":").Append(Math.Round(Hour, 4).ToString("0.####", CultureInfo.InvariantCulture));
            }

            if (Mounted)
            {
                sb.Append(",\"mounted\":1");
                sb.Append(",\"gait\":").Append(Gait.ToString(CultureInfo.InvariantCulture));
            }

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Reads a saved journey. Rules (as for the Journey settings): no version or an older one is read field by field;
        /// a newer version, a blob that is not a JSON object, or one without a seed is rejected (returns false and
        /// <paramref name="result"/> is null) so the caller starts a new journey.
        /// </summary>
        public static bool TryFromJson(string json, out EndlessJourney result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json) || !FlatJson.TryParse(json, out Dictionary<string, object> values)) return false;
            int version = 0;
            if (values.TryGetValue("version", out object v) && v is double dv) version = (int)dv;
            if (version > CurrentVersion) return false;
            if (!values.TryGetValue("seed", out object seedValue) || !(seedValue is double seed) || seed < int.MinValue || seed > int.MaxValue ||
                Math.Abs(seed - Math.Round(seed)) > 1e-9)
            {
                return false;
            }

            var journey = new EndlessJourney { Seed = (int)seed };
            if (values.TryGetValue("s", out object sv) && sv is double s) journey.S = s;
            if (values.TryGetValue("day", out object dayValue) && dayValue is double day && day >= 1 && day < int.MaxValue) journey.Day = (int)day;
            if (values.TryGetValue("hour", out object hourValue) && hourValue is double hour) journey.Hour = (float)hour;
            // Version 2 (riding); a version-1 blob has neither and resumes on foot.
            if (values.TryGetValue("mounted", out object mountedValue))
            {
                journey.Mounted = mountedValue is double m ? m != 0.0 : mountedValue is bool b && b;
            }

            if (values.TryGetValue("gait", out object gaitValue) && gaitValue is double gait && !double.IsNaN(gait)) journey.Gait = (int)Math.Max(0.0, Math.Min(2.0, gait));
            result = journey.Clamp();
            return true;
        }
    }
}
