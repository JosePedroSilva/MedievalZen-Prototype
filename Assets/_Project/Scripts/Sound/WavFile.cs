using System;

namespace MedievalZen.Sound
{
    /// <summary>
    /// Reads a WAV file's samples from its bytes, with no Unity call: the palette menus measure every clip they pick (level, length,
    /// tail) from the file on disk, and the offline tests do the same. Nobody can listen offline; the numbers are what keeps beds,
    /// music and footsteps at sensible levels against each other (<see cref="SoundMix"/>).
    /// Supports PCM of 16, 24 and 32 bits and 32-bit float, any channel count, plain and extensible headers.
    /// </summary>
    public static class WavFile
    {
        /// <summary>What a WAV file holds.</summary>
        public struct Info
        {
            /// <summary>Frames per second.</summary>
            public int SampleRate;

            /// <summary>Channels per frame.</summary>
            public int Channels;

            /// <summary>Bits per sample.</summary>
            public int Bits;

            /// <summary>Frames in the data chunk.</summary>
            public long Frames;

            /// <summary>Length in seconds.</summary>
            public double Seconds => SampleRate > 0 ? Frames / (double)SampleRate : 0.0;
        }

        /// <summary>The header alone (no samples are decoded).</summary>
        public static bool TryReadInfo(byte[] bytes, out Info info) => Parse(bytes, out info, out _, out _, out _);

        /// <summary>The samples, interleaved, −1..1. False when the file is not a WAV this reader supports.</summary>
        public static bool TryRead(byte[] bytes, out Info info, out float[] samples)
        {
            samples = null;
            if (!Parse(bytes, out info, out int dataStart, out long dataBytes, out bool isFloat)) return false;
            int bytesPer = info.Bits / 8;
            long count = info.Frames * info.Channels;
            if (count > int.MaxValue) return false;
            samples = new float[count];
            int p = dataStart;
            for (int i = 0; i < samples.Length; i++, p += bytesPer)
            {
                switch (bytesPer)
                {
                    case 2:
                        samples[i] = (short)(bytes[p] | (bytes[p + 1] << 8)) / 32768f;
                        break;
                    case 3:
                        samples[i] = ((bytes[p] << 8) | (bytes[p + 1] << 16) | (bytes[p + 2] << 24)) / 2147483648f;
                        break;
                    default:
                        samples[i] = isFloat ? BitConverter.ToSingle(bytes, p) : BitConverter.ToInt32(bytes, p) / 2147483648f;
                        break;
                }
            }

            return true;
        }

        /// <summary>
        /// RMS and peak level (dBFS, all channels together) of the frames <paramref name="fromFrame"/> (inclusive) to
        /// <paramref name="toFrame"/> (exclusive); −120 for silence or an empty range.
        /// </summary>
        public static void Level(float[] samples, int channels, long fromFrame, long toFrame, out float rmsDb, out float peakDb)
        {
            rmsDb = -120f;
            peakDb = -120f;
            if (samples == null || channels <= 0) return;
            long from = Math.Max(0L, fromFrame) * channels, to = Math.Min(samples.Length, toFrame * channels);
            if (to <= from) return;
            double sum = 0.0;
            float peak = 0f;
            for (long i = from; i < to; i++)
            {
                float v = samples[i];
                sum += (double)v * v;
                float a = v < 0f ? -v : v;
                if (a > peak) peak = a;
            }

            double rms = Math.Sqrt(sum / (to - from));
            if (rms > 1e-6) rmsDb = (float)(20.0 * Math.Log10(rms));
            if (peak > 1e-6f) peakDb = (float)(20.0 * Math.Log10(peak));
        }

        /// <summary>RMS and peak level of the whole file.</summary>
        public static void Level(float[] samples, int channels, out float rmsDb, out float peakDb) =>
            Level(samples, channels, 0L, samples != null && channels > 0 ? samples.Length / channels : 0L, out rmsDb, out peakDb);

        private static bool Parse(byte[] b, out Info info, out int dataStart, out long dataBytes, out bool isFloat)
        {
            info = default;
            dataStart = 0;
            dataBytes = 0;
            isFloat = false;
            if (b == null || b.Length < 44 || !Tag(b, 0, "RIFF") || !Tag(b, 8, "WAVE")) return false;
            bool haveFormat = false;
            int p = 12;
            while (p + 8 <= b.Length)
            {
                long size = BitConverter.ToUInt32(b, p + 4);
                int body = p + 8;
                if (Tag(b, p, "fmt ") && body + 16 <= b.Length)
                {
                    int format = BitConverter.ToUInt16(b, body);
                    info.Channels = BitConverter.ToUInt16(b, body + 2);
                    info.SampleRate = BitConverter.ToInt32(b, body + 4);
                    info.Bits = BitConverter.ToUInt16(b, body + 14);
                    // Extensible: the real format is the first two bytes of the sub-format GUID.
                    if (format == 0xFFFE && size >= 40 && body + 26 <= b.Length) format = BitConverter.ToUInt16(b, body + 24);
                    if (format != 1 && format != 3) return false;
                    isFloat = format == 3;
                    haveFormat = true;
                }
                else if (Tag(b, p, "data"))
                {
                    if (!haveFormat) return false;
                    dataStart = body;
                    dataBytes = Math.Min(size, (long)b.Length - body);
                    break;
                }

                long next = body + size + (size & 1);   // chunks are padded to an even length
                if (next > int.MaxValue) return false;
                p = (int)next;
            }

            if (!haveFormat || dataStart == 0 || info.Channels <= 0 || info.SampleRate <= 0) return false;
            if (info.Bits != 16 && info.Bits != 24 && info.Bits != 32) return false;
            if (isFloat && info.Bits != 32) return false;
            info.Frames = dataBytes / (info.Bits / 8 * info.Channels);
            return info.Frames > 0;
        }

        private static bool Tag(byte[] b, int at, string tag) =>
            at + 4 <= b.Length && b[at] == tag[0] && b[at + 1] == tag[1] && b[at + 2] == tag[2] && b[at + 3] == tag[3];
    }
}
