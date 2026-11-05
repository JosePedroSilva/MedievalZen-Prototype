using System;

namespace MedievalZen.Sound
{
    /// <summary>The measured levels of one ambience clip (pure; the palette menu and the offline checks share it).</summary>
    public struct AmbienceLevel
    {
        /// <summary>Length in seconds, channels.</summary>
        public double Seconds;
        public int Channels;

        /// <summary>Whole-file RMS and peak (dBFS).</summary>
        public float RmsDb, PeakDb;

        /// <summary>Quietest and loudest 1 s window (dBFS RMS).</summary>
        public float WindowMinDb, WindowMaxDb;

        /// <summary>Loop seam: level of the last 0.5 s minus the first 0.5 s (dB), and the largest per-channel sample jump from the last to the first frame (0..2).</summary>
        public float SeamLevelDb, SeamJump;

        /// <summary>The level the trim is computed from: whole-file RMS, or the loudest second for a one-shot.</summary>
        public float ReferenceDb(AmbienceCategory category) => category == AmbienceCategory.Spot ? WindowMaxDb : RmsDb;

        /// <summary>The 1 s window range (dB): how dynamic the clip is.</summary>
        public float RangeDb => WindowMaxDb - WindowMinDb;
    }

    /// <summary>Measures WAV files for the ambience (<see cref="WavFile"/>), pure.</summary>
    public static class AmbienceLevels
    {
        /// <summary>A seam whose level step exceeds this (dB) is flagged.</summary>
        public const float SeamLevelWarnDb = 3f;

        /// <summary>A seam whose sample jump exceeds this (full scale 2) is flagged as a likely click.</summary>
        public const float SeamJumpWarn = 0.05f;

        /// <summary>A loop whose 1 s windows span more than this (dB) is flagged as unusually dynamic.</summary>
        public const float RangeWarnDb = 18f;

        /// <summary>
        /// Measures a WAV file's bytes; false when it cannot be read. With <paramref name="mono"/> the channels are averaged
        /// first: that is what a clip imported with Force To Mono (positioned loops, spot sounds) plays, and for a wide stereo
        /// recording it is up to 3 dB below the level of its channels.
        /// </summary>
        public static bool Measure(byte[] bytes, out AmbienceLevel level, bool mono = false)
        {
            level = default;
            if (bytes == null || !WavFile.TryRead(bytes, out WavFile.Info info, out float[] samples) || info.Frames <= 0) return false;
            int ch = Math.Max(1, info.Channels);
            long frames = info.Frames;
            if (mono && ch > 1)
            {
                var mixed = new float[frames];
                for (long f = 0; f < frames; f++)
                {
                    float sum = 0f;
                    for (int c = 0; c < ch; c++) sum += samples[f * ch + c];
                    mixed[f] = sum / ch;
                }

                samples = mixed;
                ch = 1;
            }

            level.Seconds = info.Seconds;
            level.Channels = ch;
            WavFile.Level(samples, ch, out level.RmsDb, out level.PeakDb);

            int second = Math.Max(1, info.SampleRate);
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            if (frames < second)
            {
                lo = hi = level.RmsDb;
            }
            else
            {
                // Windows of one second, half a second apart.
                for (long f = 0; f + second <= frames; f += second / 2)
                {
                    WavFile.Level(samples, ch, f, f + second, out float r, out _);
                    if (r < lo) lo = r;
                    if (r > hi) hi = r;
                }
            }

            level.WindowMinDb = lo;
            level.WindowMaxDb = hi;
            long half = Math.Min(frames / 2, Math.Max(1, info.SampleRate / 2));
            WavFile.Level(samples, ch, frames - half, frames, out float tail, out _);
            WavFile.Level(samples, ch, 0, half, out float head, out _);
            level.SeamLevelDb = tail - head;
            float jump = 0f;
            for (int c = 0; c < ch; c++)
            {
                float d = Math.Abs(samples[(frames - 1) * ch + c] - samples[c]);
                if (d > jump) jump = d;
            }

            level.SeamJump = jump;
            return true;
        }

        /// <summary>The notes worth a warning about a measured clip ("" when none).</summary>
        public static string Flags(in AmbienceLevel l, AmbienceCategory category)
        {
            string s = string.Empty;
            if (category != AmbienceCategory.Spot)
            {
                if (Math.Abs(l.SeamLevelDb) > SeamLevelWarnDb) s += $" seam level step {l.SeamLevelDb:+0.0;-0.0} dB;";
                if (l.SeamJump > SeamJumpWarn) s += $" seam sample jump {l.SeamJump:0.000};";
                if (l.RangeDb > RangeWarnDb) s += $" dynamic ({l.RangeDb:0.0} dB between its quietest and loudest second);";
            }

            if (l.PeakDb > -1f) s += $" peak {l.PeakDb:0.0} dBFS;";
            return s.Trim();
        }
    }
}
