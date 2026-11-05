using System;

namespace MedievalZen.Sound
{
    /// <summary>The five files of a track. The first three are time-aligned mixes of rising density (measured 2026-10-06).</summary>
    public enum MusicVariant
    {
        /// <summary>The sparsest full-length mix.</summary>
        Intensity1 = 0,
        /// <summary>The middle full-length mix.</summary>
        Intensity2 = 1,
        /// <summary>The full mix.</summary>
        Main = 2,
        /// <summary>A 30 s edit with its own ending (plus the tail).</summary>
        Cut30 = 3,
        /// <summary>A 60 s edit with its own ending (plus the tail).</summary>
        Cut60 = 4,
    }

    /// <summary>The situation a track is weighed for.</summary>
    public enum MusicMood
    {
        /// <summary>Open country by day: brighter tracks.</summary>
        OpenDay = 0,
        /// <summary>Under or just out of the woods: softer, darker tracks.</summary>
        Forest = 1,
        /// <summary>Dusk and night: the darkest tracks with the least pulse.</summary>
        Night = 2,
        /// <summary>The traveller is resting: the calmest tracks.</summary>
        Rest = 3,
    }

    /// <summary>One track of the Ethereal music packs, as measured offline from its WAV files.</summary>
    public sealed class MusicTrackInfo
    {
        /// <summary>Title shown on the card (the folder name without the RT part).</summary>
        public string Title;
        /// <summary>The folder under <c>Assets/Sounds</c>, exactly as on disk.</summary>
        public string Folder;
        /// <summary>Reverb tail in seconds (the folder's RT): the last part of each file that only rings out.</summary>
        public float Tail;
        /// <summary>Length of the three full mixes in seconds (tail included).</summary>
        public float Duration;
        /// <summary>Lengths of the two edits in seconds (tail included).</summary>
        public float Cut30Seconds, Cut60Seconds;
        /// <summary>RMS (dBFS) of the full mix over its musical part, and of the two sparser mixes.</summary>
        public float MainRmsDb, Intensity1RmsDb, Intensity2RmsDb;
        /// <summary>Brightness: the mean frequency estimated from the first difference of the full mix, Hz.</summary>
        public float BrightnessHz;
        /// <summary>Pulse: RMS deviation (dB) of the 20 ms envelope from its 0.6 s average (1-25 Hz modulation).</summary>
        public float Pulse;
        /// <summary>Onsets per second (rises over 4 dB within 40 ms).</summary>
        public float Onsets;

        /// <summary>Musical length of one iteration: the next one starts here, over this one's tail.</summary>
        public float Loop => Duration - Tail;

        /// <summary>Weight of the track for a mood (overall factor included), from its brightness and pulse.</summary>
        public float MoodWeight(MusicMood mood) => MusicCatalog.MoodWeight(mood, BrightnessHz, Pulse, Onsets);
    }

    /// <summary>
    /// The 30 tracks of the three Ethereal music packs (<c>Assets/Sounds/Ethereal Music Pack*</c>, third-party: referenced, never
    /// edited), measured 2026-10-06 with <see cref="WavFile"/>. Pure: the director's tests and the offline simulation read it; the
    /// editor's <c>MedievalZen/Sound/Build Music Library</c> re-measures the files and bakes them into <c>MusicLibrary</c>.
    /// <para>
    /// Findings: every file's level falls 8-35 dB in the first second after (duration - RT), so RT is the reverb tail and the loop
    /// length is duration - RT, even where that is not a round number (Vol. 3). The Main, Intensity 1 and Intensity 2 files are
    /// sample-aligned (waveform correlation 0.1-0.9 at lag 0, flat over ±2 samples, ~0 at 100 ms) but are not stems: Main minus the
    /// sum of the other two leaves as much energy as Main itself. They are alternative mixes, crossfaded, never summed.
    /// </para>
    /// </summary>
    public static class MusicCatalog
    {
        /// <summary>Bump when the table or the mood formula changes: the library is rebuilt on the next editor load.</summary>
        public const int Version = 1;

        /// <summary>The packs' root, relative to the project.</summary>
        public const string Root = "Assets/Sounds";

        private const string V1 = "Ethereal Music Pack/Ethereal Music Pack/";
        private const string V3 = "Ethereal Music Pack Vol. 3/Ethereal Music Pack Vol. 3/";
        private const string V4 = "Ethereal Music Pack Vol. 4/Ethereal Music Pack Vol. 4/";

        /// <summary>The tracks, in pack order (F8 walks them in this order).</summary>
        public static readonly MusicTrackInfo[] Tracks =
        {
            T(V1, "Cloud Cathedral (RT 5.625)", 5.625f, 95.625f, 35.63f, 65.63f, -17.2f, -21.1f, -18.1f, 554f, 1.20f, 0.48f),
            T(V1, "Enigmatic Badger (RT 7.2)", 7.2f, 93.600f, 36.00f, 64.80f, -14.1f, -20.7f, -16.0f, 397f, 1.20f, 0.76f),
            T(V1, "Epiphone (RT 4)", 4f, 100.000f, 36.00f, 68.00f, -16.3f, -19.8f, -17.2f, 469f, 2.00f, 1.55f),
            T(V1, "Ether (RT 3.556)", 3.556f, 90.667f, 37.33f, 69.33f, -16.7f, -18.7f, -17.3f, 446f, 1.65f, 0.80f),
            T(V1, "Golden Clouds (RT 3.692)", 3.692f, 92.308f, 33.23f, 62.77f, -14.8f, -19.3f, -17.2f, 498f, 1.08f, 0.08f),
            T(V1, "Heartbeat (RT 3.2)", 3.2f, 96.000f, 28.80f, 67.20f, -16.8f, -19.8f, -17.6f, 447f, 1.37f, 0.44f),
            T(V1, "Luminesce (RT 4.091)", 4.091f, 91.364f, 36.82f, 69.55f, -17.4f, -24.0f, -22.5f, 395f, 1.61f, 0.45f),
            T(V1, "Poison Meadow (RT 2)", 2f, 98.000f, 34.00f, 66.00f, -18.2f, -24.5f, -20.8f, 751f, 1.12f, 0.07f),
            T(V1, "Stairway (RT 4)", 4f, 89.333f, 36.00f, 68.00f, -15.2f, -20.9f, -17.4f, 582f, 1.41f, 0.74f),
            T(V1, "Wind Chime (RT 5.647)", 5.647f, 96.000f, 39.53f, 62.12f, -12.4f, -21.9f, -17.9f, 338f, 1.15f, 0.08f),
            T(V3, "Air Flows (RT 7.124)", 7.124f, 97.617f, 37.62f, 71.37f, -13.7f, -13.4f, -16.2f, 792f, 1.86f, 0.97f),
            T(V3, "Angelic Dunes (RT 7.184)", 7.184f, 100.669f, 40.67f, 71.64f, -14.3f, -14.9f, -13.7f, 409f, 2.05f, 0.85f),
            T(V3, "Direct Sunlight (RT 11.045)", 11.045f, 100.480f, 41.88f, 68.39f, -13.4f, -13.6f, -14.4f, 409f, 1.12f, 0.37f),
            T(V3, "Glowing (RT 8.000)", 8f, 104.008f, 38.01f, 69.51f, -13.7f, -14.7f, -13.9f, 526f, 0.96f, 0.22f),
            T(V3, "Meteor Darts (RT 14.028)", 14.028f, 104.485f, 38.13f, 69.19f, -13.2f, -14.8f, -14.1f, 600f, 1.23f, 0.15f),
            T(V3, "Mystics (RT 13.197)", 13.197f, 102.098f, 37.27f, 68.30f, -12.8f, -14.8f, -16.4f, 342f, 0.84f, 0.11f),
            T(V3, "Space Time (RT 10.000)", 10f, 106.010f, 39.01f, 72.01f, -13.2f, -14.7f, -15.6f, 452f, 1.44f, 0.58f),
            T(V3, "Time Warps (RT 8.092)", 8.092f, 97.703f, 37.70f, 68.40f, -13.5f, -16.0f, -14.1f, 583f, 1.27f, 0.45f),
            T(V3, "Timelapse (RT 12.000)", 12f, 108.015f, 38.42f, 69.62f, -14.5f, -13.5f, -13.8f, 605f, 1.13f, 0.14f),
            T(V3, "Winds of Time (RT 8.092)", 8.092f, 97.696f, 39.77f, 68.39f, -12.2f, -14.9f, -14.0f, 338f, 1.13f, 0.13f),
            T(V4, "Angels (RT 3.75)", 3.75f, 93.750f, 33.75f, 63.75f, -15.1f, -18.5f, -17.5f, 453f, 1.00f, 0.09f),
            T(V4, "Banana Trees (RT 2.667)", 2.667f, 88.000f, 34.67f, 61.33f, -15.7f, -28.5f, -21.8f, 301f, 0.75f, 0.09f),
            T(V4, "Bits (RT 5.333)", 5.333f, 90.667f, 37.33f, 69.33f, -16.8f, -28.0f, -20.3f, 484f, 0.63f, 0.00f),
            T(V4, "Cybernetic (RT 4.8)", 4.8f, 91.200f, 33.60f, 62.40f, -14.8f, -21.6f, -15.2f, 499f, 1.54f, 1.31f),
            T(V4, "Doves (RT 4.364)", 4.364f, 91.636f, 39.27f, 65.45f, -16.4f, -23.0f, -17.8f, 598f, 0.90f, 0.00f),
            T(V4, "Dramatic Bell (RT 3.692)", 3.692f, 92.308f, 33.23f, 62.77f, -13.7f, -21.3f, -14.2f, 427f, 1.14f, 0.39f),
            T(V4, "Light Flourish (RT 4.174)", 4.174f, 96.000f, 37.57f, 62.61f, -15.2f, -18.1f, -17.9f, 469f, 1.51f, 0.95f),
            T(V4, "Mermaid (RT 3.2)", 3.2f, 92.800f, 35.20f, 67.20f, -15.7f, -18.8f, -17.6f, 715f, 1.29f, 0.63f),
            T(V4, "Peace Lily (RT 4)", 4f, 92.000f, 36.00f, 68.00f, -14.9f, -19.1f, -18.8f, 280f, 0.79f, 0.02f),
            T(V4, "The Tropics (RT 4.8)", 4.8f, 100.800f, 33.60f, 62.40f, -17.0f, -19.9f, -18.3f, 593f, 1.22f, 0.45f),
        };

        private static MusicTrackInfo T(string pack, string folder, float tail, float duration, float cut30, float cut60, float main, float i1,
                                        float i2, float bright, float pulse, float onsets)
        {
            int paren = folder.LastIndexOf(" (RT ", StringComparison.Ordinal);
            return new MusicTrackInfo
            {
                Title = paren > 0 ? folder.Substring(0, paren) : folder,
                Folder = pack + folder,
                Tail = tail,
                Duration = duration,
                Cut30Seconds = cut30,
                Cut60Seconds = cut60,
                MainRmsDb = main,
                Intensity1RmsDb = i1,
                Intensity2RmsDb = i2,
                BrightnessHz = bright,
                Pulse = pulse,
                Onsets = onsets,
            };
        }

        /// <summary>The title of a track folder ("Cloud Cathedral (RT 5.625)" → "Cloud Cathedral").</summary>
        public static string TitleOf(string folder)
        {
            if (folder == null) return string.Empty;
            int slash = folder.LastIndexOf('/');
            if (slash >= 0) folder = folder.Substring(slash + 1);
            int paren = folder.LastIndexOf(" (RT ", StringComparison.Ordinal);
            return paren > 0 ? folder.Substring(0, paren) : folder;
        }

        /// <summary>
        /// Which variant a WAV file of a track is, from its name ("... Main", "... Intensity 1", "... Cut 30"); Vol. 3 writes
        /// "Intesnity", so the word is matched loosely. False for anything else.
        /// </summary>
        public static bool TryVariantOf(string fileNameWithoutExtension, out MusicVariant variant)
        {
            variant = MusicVariant.Main;
            if (string.IsNullOrEmpty(fileNameWithoutExtension)) return false;
            string n = fileNameWithoutExtension.TrimEnd();
            if (n.EndsWith(" Main", StringComparison.OrdinalIgnoreCase)) { variant = MusicVariant.Main; return true; }
            if (n.EndsWith(" Cut 30", StringComparison.OrdinalIgnoreCase)) { variant = MusicVariant.Cut30; return true; }
            if (n.EndsWith(" Cut 60", StringComparison.OrdinalIgnoreCase)) { variant = MusicVariant.Cut60; return true; }
            int space = n.LastIndexOf(' ');
            if (space <= 0) return false;
            string word = n.Substring(0, space);
            int wordStart = word.LastIndexOf(' ') + 1;
            word = word.Substring(wordStart).ToLowerInvariant();
            bool intensity = word.Length >= 8 && word.StartsWith("int", StringComparison.Ordinal) && word.EndsWith("ty", StringComparison.Ordinal);
            if (!intensity) return false;
            string number = n.Substring(space + 1);
            if (number == "1") { variant = MusicVariant.Intensity1; return true; }
            if (number == "2") { variant = MusicVariant.Intensity2; return true; }
            return false;
        }

        /// <summary>0..1 brightness from the mean frequency (300 Hz dark, 700 Hz bright).</summary>
        public static float Brightness01(float brightnessHz) => Clamp01((brightnessHz - 300f) / 400f);

        /// <summary>0..1 pulse from the envelope modulation and the onset density.</summary>
        public static float Pulse01(float pulse, float onsets) => Clamp01(0.5f * Clamp01((pulse - 0.7f) / 1.2f) + 0.5f * Clamp01(onsets / 1.2f));

        /// <summary>
        /// A track's weight for <paramref name="mood"/>: the mood's preference for brightness and calm, times an overall factor that a
        /// strong pulse lowers (a walking game wants pads more than beats). Never 0: the user can disable a track in the library.
        /// </summary>
        public static float MoodWeight(MusicMood mood, float brightnessHz, float pulse, float onsets)
        {
            float b = Brightness01(brightnessHz), p = Pulse01(pulse, onsets);
            float w;
            switch (mood)
            {
                case MusicMood.OpenDay: w = 0.45f + 0.9f * b - 0.2f * p; break;
                case MusicMood.Forest: w = 0.4f + 0.8f * (1f - b) - 0.3f * p; break;
                case MusicMood.Night: w = 0.2f + 1.0f * (1f - b) - 0.6f * p; break;
                default: w = 0.3f + 0.5f * (1f - b) + 0.7f * (1f - p) - 0.4f; break;
            }

            float overall = 1f - 0.6f * p;
            return Math.Max(0.05f, w * overall);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
