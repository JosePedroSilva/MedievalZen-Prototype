using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.Sound;

namespace MedievalZen.UI
{
    /// <summary>How fast the day passes, relative to <c>WorldSettings.dayLengthMinutes</c>.</summary>
    public enum FlowOfTime
    {
        /// <summary>Time stands still.</summary>
        Paused = 0,
        /// <summary>Half speed (a day lasts twice as long).</summary>
        Slow = 1,
        /// <summary>The world's day length.</summary>
        Normal = 2,
        /// <summary>Double speed.</summary>
        Fast = 3,
        /// <summary>Four times as fast.</summary>
        VeryFast = 4,
    }

    /// <summary>
    /// The player's Journey-menu preferences, persisted between sessions as one small versioned JSON object (PlayerPrefs).
    /// Pure data: clamping, defaults, JSON round trip and version handling live here and are unit tested; the menu applies
    /// the values to the scene. The time of day itself is deliberately not part of it.
    /// </summary>
    public sealed class JourneySettings
    {
        /// <summary>
        /// Version written by <see cref="ToJson"/>. 2 (2026-10-02) added the cinematic camera (<see cref="CinematicCamera"/>,
        /// <see cref="CinematicWhenTravelling"/>, <see cref="Letterbox"/>) and <see cref="HudAlways"/>; a version-1 blob loads with
        /// their defaults. 3 (2026-10-02) added the animals (<see cref="Animals"/>, <see cref="AnimalVolume"/>); older blobs load
        /// with theirs. 4 (2026-10-02) added <see cref="RichLight"/>; older blobs load with it on. 5 (2026-10-06) added the sound
        /// page (<see cref="MasterVolume"/>, <see cref="AmbienceVolume"/>, <see cref="MusicVolume"/>, <see cref="FootstepVolume"/>,
        /// <see cref="MusicFrequency"/>, <see cref="MusicTitles"/>); older blobs load with the <see cref="SoundMix"/> defaults.
        /// 6 (2026-10-06) added <see cref="WalkingPace"/>; older blobs load with pace 1. 7 (2026-10-06) added
        /// <see cref="Season"/>; older blobs load with fixed autumn (the look before seasons).
        /// </summary>
        public const int CurrentVersion = 7;

        /// <summary>Range of <see cref="FogAmount"/>, <see cref="HazeDistance"/> and <see cref="CloudSoftness"/>.</summary>
        public const float AtmosphereMax = 2f;

        /// <summary>Range of <see cref="WindVolume"/> (the wind bed's own volume scale).</summary>
        public const float WindVolumeMax = ProceduralWindAudio.MaxVolume;

        /// <summary>Smallest and largest <see cref="LookSpeed"/>.</summary>
        public const float LookSpeedMin = 0.25f, LookSpeedMax = 3f;

        /// <summary>Flow of time.</summary>
        public FlowOfTime Flow = FlowOfTime.Normal;

        /// <summary>Weather choice (<see cref="WeatherOverride.Auto"/> = the seeded schedule).</summary>
        public WeatherOverride Weather = WeatherOverride.Auto;

        /// <summary>Master fog amount, 0..2 (1 = as designed).</summary>
        public float FogAmount = 1f;

        /// <summary>Haze distance multiplier, 0..2 (1 = as designed, 2 = twice as clear).</summary>
        public float HazeDistance = 1f;

        /// <summary>Cloud softness, 0..2 (1 = as designed).</summary>
        public float CloudSoftness = 1f;

        /// <summary>Whether the synthesised wind bed plays.</summary>
        public bool WindSound;

        /// <summary>Wind bed volume, 0..<see cref="WindVolumeMax"/>.</summary>
        public float WindVolume = 0.06f;

        /// <summary>First or third person.</summary>
        public CameraMode Camera = CameraMode.ThirdPerson;

        /// <summary>Multiplier on the look sensitivity (1 = the rig's inspector values).</summary>
        public float LookSpeed = 1f;

        /// <summary>Invert vertical look.</summary>
        public bool InvertY;

        /// <summary>Show the clock, destination and mode chips.</summary>
        public bool ShowHud = true;

        /// <summary>The camera choice "Cinematic": the cinematic camera takes over after a few idle seconds (<see cref="Camera"/> stays the mode it hands back to).</summary>
        public bool CinematicCamera;

        /// <summary>Film auto-travel with the cinematic camera after a few seconds without input (third person).</summary>
        public bool CinematicWhenTravelling = true;

        /// <summary>Thin black bars during cinematic shots.</summary>
        public bool Letterbox = true;

        /// <summary>Keep the clock and destination on screen even during cinematic shots and while seated ("Always").</summary>
        public bool HudAlways;

        /// <summary>Animals along the road are shown.</summary>
        public bool Animals = true;

        /// <summary>Volume of the animals' calls, 0..1.</summary>
        public float AnimalVolume = 0.5f;

        /// <summary>The rich light and colour look (<see cref="LightLook"/>); off shows the look before it, for comparison.</summary>
        public bool RichLight = LightLook.DefaultRich;

        /// <summary>Master volume, 0..1 (<c>AudioListener.volume</c>).</summary>
        public float MasterVolume = 1f;

        /// <summary>Ambience slider, 0..1 (<see cref="SoundMix.Ambience"/>).</summary>
        public float AmbienceVolume = SoundMix.DefaultAmbience;

        /// <summary>Music slider, 0..1 (<see cref="SoundMix.Music"/>).</summary>
        public float MusicVolume = SoundMix.DefaultMusic;

        /// <summary>Footsteps slider, 0..1 (<see cref="SoundMix.Footsteps"/>).</summary>
        public float FootstepVolume = SoundMix.DefaultFootsteps;

        /// <summary>How often a piece of music may start (<see cref="SoundMix.Frequency"/>); Rare by default, the user's choice.</summary>
        public MusicFrequency MusicFrequency = MusicFrequency.Rare;

        /// <summary>Show a piece's title when it starts (<see cref="SoundMix.ShowMusicTitles"/>).</summary>
        public bool MusicTitles = true;

        /// <summary>Walking pace (<see cref="WalkPace"/>: 0.5..1.5 in steps of 0.1; 1 = the validated walking and travel speeds).</summary>
        public float WalkingPace = WalkPace.Default;

        /// <summary>The season (<see cref="SeasonChoice.Autumn"/> = the look before seasons, the default; Auto follows the journey's days).</summary>
        public SeasonChoice Season = SeasonChoice.Autumn;

        /// <summary>A copy of this object.</summary>
        public JourneySettings Clone() => (JourneySettings)MemberwiseClone();

        /// <summary>Brings every value into its valid range (NaN → the default for that field); returns this.</summary>
        public JourneySettings Clamp()
        {
            if (!Enum.IsDefined(typeof(FlowOfTime), Flow)) Flow = FlowOfTime.Normal;
            if (!Enum.IsDefined(typeof(WeatherOverride), Weather)) Weather = WeatherOverride.Auto;
            if (!Enum.IsDefined(typeof(CameraMode), Camera)) Camera = CameraMode.ThirdPerson;
            FogAmount = ClampFloat(FogAmount, 0f, AtmosphereMax, 1f);
            HazeDistance = ClampFloat(HazeDistance, 0f, AtmosphereMax, 1f);
            CloudSoftness = ClampFloat(CloudSoftness, 0f, AtmosphereMax, 1f);
            WindVolume = ClampFloat(WindVolume, 0f, WindVolumeMax, 0.06f);
            LookSpeed = ClampFloat(LookSpeed, LookSpeedMin, LookSpeedMax, 1f);
            AnimalVolume = ClampFloat(AnimalVolume, 0f, 1f, 0.5f);
            if (!Enum.IsDefined(typeof(MusicFrequency), MusicFrequency)) MusicFrequency = MusicFrequency.Rare;
            MasterVolume = ClampFloat(MasterVolume, 0f, 1f, 1f);
            AmbienceVolume = ClampFloat(AmbienceVolume, 0f, 1f, SoundMix.DefaultAmbience);
            MusicVolume = ClampFloat(MusicVolume, 0f, 1f, SoundMix.DefaultMusic);
            FootstepVolume = ClampFloat(FootstepVolume, 0f, 1f, SoundMix.DefaultFootsteps);
            WalkingPace = WalkPace.Clamp(WalkingPace);
            if (!Enum.IsDefined(typeof(SeasonChoice), Season)) Season = SeasonChoice.Autumn;
            return this;
        }

        /// <summary>True when every field matches (floats within 1e-4).</summary>
        public bool SameAs(JourneySettings other)
        {
            if (other == null) return false;
            return Flow == other.Flow && Weather == other.Weather && Near(FogAmount, other.FogAmount) &&
                   Near(HazeDistance, other.HazeDistance) && Near(CloudSoftness, other.CloudSoftness) &&
                   WindSound == other.WindSound && Near(WindVolume, other.WindVolume) && Camera == other.Camera &&
                   Near(LookSpeed, other.LookSpeed) && InvertY == other.InvertY && ShowHud == other.ShowHud &&
                   CinematicCamera == other.CinematicCamera && CinematicWhenTravelling == other.CinematicWhenTravelling &&
                   Letterbox == other.Letterbox && HudAlways == other.HudAlways && Animals == other.Animals &&
                   Near(AnimalVolume, other.AnimalVolume) && RichLight == other.RichLight &&
                   Near(MasterVolume, other.MasterVolume) && Near(AmbienceVolume, other.AmbienceVolume) &&
                   Near(MusicVolume, other.MusicVolume) && Near(FootstepVolume, other.FootstepVolume) &&
                   MusicFrequency == other.MusicFrequency && MusicTitles == other.MusicTitles &&
                   Near(WalkingPace, other.WalkingPace) && Season == other.Season;
        }

        /// <summary>
        /// Serialises to one flat JSON object, e.g.
        /// <c>{"version":1,"flow":"Normal","weather":"Auto","fog":1,"haze":1,"clouds":1,"wind":false,"windVolume":0.06,
        /// "camera":"ThirdPerson","lookSpeed":1,"invertY":false,"showHud":true,"cinematic":false,"cinematicTravel":true,
        /// "letterbox":true,"hudAlways":false,"animals":true,"animalVolume":0.5,"richLight":true,"master":1,"ambience":0.8,"music":0.8,
        /// "footsteps":0.7,"musicFrequency":"Rare","musicTitles":true,"walkPace":1}</c>. Enums are written by name so reordering
        /// them never changes a saved choice.
        /// </summary>
        public string ToJson()
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            Append(sb, "version", CurrentVersion.ToString(CultureInfo.InvariantCulture), false, true);
            Append(sb, "flow", Flow.ToString(), true);
            Append(sb, "weather", Weather.ToString(), true);
            Append(sb, "fog", Number(FogAmount), false);
            Append(sb, "haze", Number(HazeDistance), false);
            Append(sb, "clouds", Number(CloudSoftness), false);
            Append(sb, "wind", WindSound ? "true" : "false", false);
            Append(sb, "windVolume", Number(WindVolume), false);
            Append(sb, "camera", Camera.ToString(), true);
            Append(sb, "lookSpeed", Number(LookSpeed), false);
            Append(sb, "invertY", InvertY ? "true" : "false", false);
            Append(sb, "showHud", ShowHud ? "true" : "false", false);
            Append(sb, "cinematic", CinematicCamera ? "true" : "false", false);
            Append(sb, "cinematicTravel", CinematicWhenTravelling ? "true" : "false", false);
            Append(sb, "letterbox", Letterbox ? "true" : "false", false);
            Append(sb, "hudAlways", HudAlways ? "true" : "false", false);
            Append(sb, "animals", Animals ? "true" : "false", false);
            Append(sb, "animalVolume", Number(AnimalVolume), false);
            Append(sb, "richLight", RichLight ? "true" : "false", false);
            Append(sb, "master", Number(MasterVolume), false);
            Append(sb, "ambience", Number(AmbienceVolume), false);
            Append(sb, "music", Number(MusicVolume), false);
            Append(sb, "footsteps", Number(FootstepVolume), false);
            Append(sb, "musicFrequency", MusicFrequency.ToString(), true);
            Append(sb, "musicTitles", MusicTitles ? "true" : "false", false);
            Append(sb, "walkPace", Number(WalkingPace), false);
            Append(sb, "season", Season.ToString(), true);
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Reads <paramref name="json"/> over a copy of <paramref name="defaults"/> and clamps the result.
        /// Version rules: a blob with no <c>version</c> or an older one is read field by field (missing or unknown keys keep
        /// the defaults, values of the wrong type are skipped) and re-stamped as <see cref="CurrentVersion"/> on the next save;
        /// a blob from a newer version, or one that is not a JSON object, is rejected (<paramref name="result"/> = the defaults,
        /// returns false) so an old build never half-reads a newer format.
        /// </summary>
        public static bool TryFromJson(string json, JourneySettings defaults, out JourneySettings result)
        {
            result = (defaults ?? new JourneySettings()).Clone().Clamp();
            if (string.IsNullOrWhiteSpace(json) || !FlatJson.TryParse(json, out Dictionary<string, object> values)) return false;

            int version = 0;
            if (values.TryGetValue("version", out object v) && v is double dv) version = (int)dv;
            if (version > CurrentVersion) return false;

            var s = result;
            if (TryEnum(values, "flow", out FlowOfTime flow)) s.Flow = flow;
            if (TryEnum(values, "weather", out WeatherOverride weather)) s.Weather = weather;
            if (TryNumber(values, "fog", out float fog)) s.FogAmount = fog;
            if (TryNumber(values, "haze", out float haze)) s.HazeDistance = haze;
            if (TryNumber(values, "clouds", out float clouds)) s.CloudSoftness = clouds;
            if (TryBool(values, "wind", out bool wind)) s.WindSound = wind;
            if (TryNumber(values, "windVolume", out float windVolume)) s.WindVolume = windVolume;
            if (TryEnum(values, "camera", out CameraMode camera)) s.Camera = camera;
            if (TryNumber(values, "lookSpeed", out float look)) s.LookSpeed = look;
            if (TryBool(values, "invertY", out bool invert)) s.InvertY = invert;
            if (TryBool(values, "showHud", out bool hud)) s.ShowHud = hud;
            if (version >= 2)
            {
                // Added in version 2; older blobs keep the defaults for these.
                if (TryBool(values, "cinematic", out bool cinematic)) s.CinematicCamera = cinematic;
                if (TryBool(values, "cinematicTravel", out bool cinematicTravel)) s.CinematicWhenTravelling = cinematicTravel;
                if (TryBool(values, "letterbox", out bool bars)) s.Letterbox = bars;
                if (TryBool(values, "hudAlways", out bool always)) s.HudAlways = always;
            }

            if (version >= 3)
            {
                // Added in version 3; older blobs keep the defaults for these.
                if (TryBool(values, "animals", out bool animals)) s.Animals = animals;
                if (TryNumber(values, "animalVolume", out float animalVolume)) s.AnimalVolume = animalVolume;
            }

            if (version >= 4)
            {
                // Added in version 4; older blobs keep the default (on).
                if (TryBool(values, "richLight", out bool richLight)) s.RichLight = richLight;
            }

            if (version >= 5)
            {
                // Added in version 5 (the sound page); older blobs keep the SoundMix defaults.
                if (TryNumber(values, "master", out float master)) s.MasterVolume = master;
                if (TryNumber(values, "ambience", out float ambience)) s.AmbienceVolume = ambience;
                if (TryNumber(values, "music", out float music)) s.MusicVolume = music;
                if (TryNumber(values, "footsteps", out float footsteps)) s.FootstepVolume = footsteps;
                if (TryEnum(values, "musicFrequency", out MusicFrequency frequency)) s.MusicFrequency = frequency;
                if (TryBool(values, "musicTitles", out bool titles)) s.MusicTitles = titles;
            }

            if (version >= 6)
            {
                // Added in version 6; older blobs keep pace 1.
                if (TryNumber(values, "walkPace", out float walkPace)) s.WalkingPace = walkPace;
            }

            if (version >= 7)
            {
                // Added in version 7; older blobs keep fixed autumn (the look before seasons).
                if (TryEnum(values, "season", out SeasonChoice season)) s.Season = season;
            }

            s.Clamp();
            return true;
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-4f;

        private static float ClampFloat(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value)) return Math.Max(min, Math.Min(max, fallback));
            return Math.Max(min, Math.Min(max, value));
        }

        private static string Number(float value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

        private static void Append(StringBuilder sb, string key, string value, bool quoted, bool first = false)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(key).Append("\":");
            if (quoted) sb.Append('"').Append(value).Append('"');
            else sb.Append(value);
        }

        private static bool TryNumber(Dictionary<string, object> values, string key, out float result)
        {
            result = 0f;
            if (!values.TryGetValue(key, out object o) || !(o is double d) || double.IsNaN(d) || double.IsInfinity(d)) return false;
            result = (float)d;
            return true;
        }

        private static bool TryBool(Dictionary<string, object> values, string key, out bool result)
        {
            result = false;
            if (!values.TryGetValue(key, out object o) || !(o is bool b)) return false;
            result = b;
            return true;
        }

        private static bool TryEnum<T>(Dictionary<string, object> values, string key, out T result) where T : struct, Enum
        {
            result = default;
            if (!values.TryGetValue(key, out object o) || !(o is string name)) return false;
            if (int.TryParse(name, out _)) return false;   // names only; Enum.TryParse would accept "7"
            return Enum.TryParse(name, false, out result) && Enum.IsDefined(typeof(T), result);
        }
    }

    /// <summary>
    /// A minimal parser for one flat JSON object whose values are strings, numbers, booleans or null (no nesting) — all the
    /// settings blob needs, without depending on the engine's JsonUtility (so the tests stay pure).
    /// Numbers become <see cref="double"/>, strings <see cref="string"/>, booleans <see cref="bool"/>, null <c>null</c>.
    /// </summary>
    public static class FlatJson
    {
        /// <summary>Parses <paramref name="json"/>; false on any syntax error or nested value.</summary>
        public static bool TryParse(string json, out Dictionary<string, object> values)
        {
            values = new Dictionary<string, object>(StringComparer.Ordinal);
            if (json == null) return false;
            int i = 0;
            SkipWhite(json, ref i);
            if (i >= json.Length || json[i] != '{') return false;
            i++;
            SkipWhite(json, ref i);
            if (i < json.Length && json[i] == '}') return OnlyWhiteAfter(json, i + 1);

            while (i < json.Length)
            {
                SkipWhite(json, ref i);
                if (!TryString(json, ref i, out string key)) return false;
                SkipWhite(json, ref i);
                if (i >= json.Length || json[i] != ':') return false;
                i++;
                SkipWhite(json, ref i);
                if (!TryValue(json, ref i, out object value)) return false;
                values[key] = value;
                SkipWhite(json, ref i);
                if (i >= json.Length) return false;
                if (json[i] == ',')
                {
                    i++;
                    continue;
                }

                if (json[i] == '}') return OnlyWhiteAfter(json, i + 1);
                return false;
            }

            return false;
        }

        private static bool OnlyWhiteAfter(string s, int i)
        {
            SkipWhite(s, ref i);
            return i >= s.Length;
        }

        private static void SkipWhite(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static bool TryValue(string s, ref int i, out object value)
        {
            value = null;
            if (i >= s.Length) return false;
            char c = s[i];
            if (c == '"')
            {
                bool ok = TryString(s, ref i, out string str);
                value = str;
                return ok;
            }

            if (Match(s, ref i, "true"))
            {
                value = true;
                return true;
            }

            if (Match(s, ref i, "false"))
            {
                value = false;
                return true;
            }

            if (Match(s, ref i, "null")) return true;

            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            if (i == start) return false;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return false;
            value = d;
            return true;
        }

        private static bool Match(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        private static bool TryString(string s, ref int i, out string result)
        {
            result = null;
            if (i >= s.Length || s[i] != '"') return false;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    result = sb.ToString();
                    return true;
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (i >= s.Length) return false;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)) return false;
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: return false;
                }
            }

            return false;
        }
    }
}
