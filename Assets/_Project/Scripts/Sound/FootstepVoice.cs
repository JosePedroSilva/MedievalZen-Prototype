using System;
using System.Threading;

namespace MedievalZen.Sound
{
    /// <summary>One step to sound.</summary>
    public struct FootstepRequest
    {
        /// <summary>What the foot lands on.</summary>
        public FootstepSurface Surface;

        /// <summary>0 left (panned a little left), 1 right.</summary>
        public int Foot;

        /// <summary>Sprinting: +2 dB and no heel-toe roll. For a hoof: the accented first beat of the gait's cycle (+1.5 dB).</summary>
        public bool Sprint;

        /// <summary>A horse's hoof (the traveller rides) instead of a foot: a hard clop, no heel-toe roll, same bus and level.</summary>
        public bool Hoof;
    }

    /// <summary>
    /// Single-producer single-consumer ring of step requests, lock-free: the main thread pushes, the audio thread pops. A full ring
    /// drops the newest step (it never blocks either thread).
    /// </summary>
    public sealed class FootstepQueue
    {
        private readonly FootstepRequest[] items;
        private int head;   // next to read (audio thread)
        private int tail;   // next to write (main thread)

        public FootstepQueue(int capacity = 16)
        {
            items = new FootstepRequest[Math.Max(2, capacity)];
        }

        /// <summary>Main thread: queues a step; false when the ring is full.</summary>
        public bool Push(in FootstepRequest request)
        {
            int t = Volatile.Read(ref tail);
            int next = (t + 1) % items.Length;
            if (next == Volatile.Read(ref head)) return false;
            items[t] = request;
            Volatile.Write(ref tail, next);
            return true;
        }

        /// <summary>Audio thread: takes the oldest step; false when empty.</summary>
        public bool TryPop(out FootstepRequest request)
        {
            int h = Volatile.Read(ref head);
            if (h == Volatile.Read(ref tail))
            {
                request = default;
                return false;
            }

            request = items[h];
            Volatile.Write(ref head, (h + 1) % items.Length);
            return true;
        }
    }

    /// <summary>
    /// The synthesised placeholder footsteps (pure DSP, tested offline; a sampled pack will replace it). Each step is rendered whole
    /// when it is triggered into a buffer of its own (at most <see cref="MaxSeconds"/>): one short shaped burst per surface, a
    /// heel-toe second hit 60–90 ms later at −8 dB when walking, the mean removed along the envelope (no DC), and the peak set to
    /// <see cref="SoundMix.FootstepPeakDb"/> (+2 dB sprinting) ±15 %. Mixing then pans it ±0.1 by foot and applies the bus gain,
    /// smoothed per sample. Every burst starts and ends at zero, so nothing clicks; nothing allocates after construction.
    /// <list type="bullet">
    /// <item>Road: a low thump (110→60 Hz, 60 ms) and gritty mid noise (0.8–2.5 kHz, 70 ms).</item>
    /// <item>Grass: a soft broadband swish (1–5 kHz, 110 ms, 12 ms attack), no thump.</item>
    /// <item>ForestFloor: 3–5 tiny grains (2–6 kHz, 8–20 ms each over 90 ms) and a soft thump.</item>
    /// <item>Rock: a tight click-thud (300–1200 Hz, 40 ms).</item>
    /// <item>Water: a splash (noise swept 600 Hz → 3 kHz, 180 ms) and a low plop.</item>
    /// </list>
    /// </summary>
    public sealed class FootstepVoice
    {
        /// <summary>Longest step (with its heel-toe hit), seconds.</summary>
        public const float MaxSeconds = 0.3f;

        /// <summary>Steps sounding at once.</summary>
        public const int Voices = 6;

        /// <summary>Level of the heel-toe second hit relative to the first, dB.</summary>
        public const float HeelToeDb = -8f;

        /// <summary>Extra level of a sprinting step, dB.</summary>
        public const float SprintDb = 2f;

        /// <summary>Random level per step (±, linear fraction).</summary>
        public const float LevelJitter = 0.15f;

        /// <summary>Random filter frequencies per step (±, fraction).</summary>
        public const float FilterJitter = 0.1f;

        /// <summary>Pan of each foot (left −, right +).</summary>
        public const float FootPan = 0.1f;

        private sealed class Voice
        {
            public float[] Samples;
            public int Length, Position;
            public float GainL, GainR;
            public bool Active;
        }

        private readonly float sampleRate;
        private readonly Voice[] voices = new Voice[Voices];
        private readonly float[] window;
        private uint rng;
        private float busGain = -1f;

        public FootstepVoice(int sampleRate, uint seed = 0x2545F491u)
        {
            this.sampleRate = sampleRate > 0 ? sampleRate : 48000;
            int length = (int)Math.Ceiling(this.sampleRate * MaxSeconds) + 1;
            for (int i = 0; i < voices.Length; i++) voices[i] = new Voice { Samples = new float[length] };
            window = new float[length];
            rng = seed == 0 ? 1u : seed;
        }

        /// <summary>Sample rate the steps are rendered at.</summary>
        public float SampleRate => sampleRate;

        /// <summary>Steps still sounding.</summary>
        public int ActiveCount
        {
            get
            {
                int n = 0;
                foreach (Voice v in voices) if (v.Active) n++;
                return n;
            }
        }

        /// <summary>The peak a step aims at before the bus gain (linear).</summary>
        public static float TargetPeak(bool sprint) => SoundMix.DbToGain(SoundMix.FootstepPeakDb + (sprint ? SprintDb : 0f));

        /// <summary>Starts a step (audio thread, or a test). The quietest-ending voice is reused when all are busy.</summary>
        public void Trigger(in FootstepRequest request)
        {
            Voice voice = null;
            int mostLeft = int.MaxValue;
            foreach (Voice v in voices)
            {
                if (!v.Active)
                {
                    voice = v;
                    break;
                }

                int left = v.Length - v.Position;
                if (left < mostLeft)
                {
                    mostLeft = left;
                    voice = v;
                }
            }

            float[] buffer = voice.Samples;
            Array.Clear(buffer, 0, buffer.Length);
            Array.Clear(window, 0, window.Length);
            float tone = 1f + FilterJitter * Signed();
            int length = request.Hoof ? RenderHoof(request.Surface, buffer, 0, 1f, tone) : RenderHit(request.Surface, buffer, 0, 1f, tone);
            if (!request.Sprint && !request.Hoof)
            {
                // The heel-toe roll: the same surface again, a little later and quieter.
                int offset = (int)(sampleRate * (0.06f + 0.03f * Unit()));
                float tone2 = tone * (1f + 0.04f * Signed());
                length = Math.Max(length, RenderHit(request.Surface, buffer, offset, SoundMix.DbToGain(HeelToeDb), tone2));
            }

            length = Math.Min(length, buffer.Length);
            RemoveMean(buffer, length);
            float peak = 0f;
            for (int i = 0; i < length; i++)
            {
                float a = Math.Abs(buffer[i]);
                if (a > peak) peak = a;
            }

            float target = (request.Hoof ? HoofPeak(request.Sprint) : TargetPeak(request.Sprint)) * (1f + LevelJitter * Signed());
            float scale = peak > 1e-9f ? target / peak : 0f;
            for (int i = 0; i < length; i++) buffer[i] *= scale;

            float pan = (request.Foot == 0 ? -FootPan : FootPan) + 0.02f * Signed();
            voice.GainL = Math.Min(1f, 1f - pan);
            voice.GainR = Math.Min(1f, 1f + pan);
            voice.Length = length;
            voice.Position = 0;
            voice.Active = length > 0;
        }

        /// <summary>Adds the sounding steps into <paramref name="data"/> (interleaved, <paramref name="channels"/>), times <paramref name="gain"/>.</summary>
        public void Render(float[] data, int channels, float gain)
        {
            if (data == null || channels <= 0) return;
            if (!(gain >= 0f)) gain = 0f;
            if (busGain < 0f) busGain = gain;
            int frames = data.Length / channels;
            bool any = false;
            foreach (Voice v in voices) any |= v.Active;
            if (!any)
            {
                busGain = gain;
                return;
            }

            for (int i = 0; i < frames; i++)
            {
                busGain += (gain - busGain) * 0.002f;
                float l = 0f, r = 0f;
                foreach (Voice v in voices)
                {
                    if (!v.Active) continue;
                    float s = v.Samples[v.Position++];
                    l += s * v.GainL;
                    r += s * v.GainR;
                    if (v.Position >= v.Length) v.Active = false;
                }

                l *= busGain;
                r *= busGain;
                if (float.IsNaN(l) || float.IsInfinity(l)) l = 0f;
                if (float.IsNaN(r) || float.IsInfinity(r)) r = 0f;
                int o = i * channels;
                if (channels == 1)
                {
                    data[o] += 0.5f * (l + r);
                }
                else
                {
                    data[o] += l;
                    data[o + 1] += r;
                }
            }
        }

        /// <summary>Extra level of the accented hoofbeat (the first of the gait's cycle), dB.</summary>
        public const float HoofAccentDb = 1.5f;

        /// <summary>The peak a hoofbeat aims at before the bus gain: the footsteps' level, the accented beat a little louder.</summary>
        public static float HoofPeak(bool accent) => SoundMix.DbToGain(SoundMix.FootstepPeakDb + (accent ? HoofAccentDb : 0f));

        /// <summary>
        /// One hoofbeat at <paramref name="offset"/>; returns the end sample. Hard ground: a woody clop (a resonant 900–1300 Hz knock over a
        /// 140→70 Hz thud, 70 ms). Grass and forest floor: a dull thud and a short soft swish. Rock: a brighter clack. Water: the splash.
        /// </summary>
        private int RenderHoof(FootstepSurface surface, float[] buffer, int offset, float level, float tone)
        {
            switch (surface)
            {
                case FootstepSurface.Road:
                {
                    int a = Thump(buffer, offset, level * 0.9f, 140f * tone, 70f * tone, 0.001f, 0.070f, 0.020f);
                    int b = Band(buffer, offset, level * 0.8f, 1100f * tone, 950f * tone, 2.4f, 0.001f, 0.050f, 0.012f, 0f);
                    return Math.Max(a, b);
                }
                case FootstepSurface.Rock:
                {
                    int a = Band(buffer, offset, level, 1500f * tone, 1300f * tone, 2.8f, 0.0008f, 0.045f, 0.009f, 0f);
                    int b = Thump(buffer, offset, level * 0.6f, 170f * tone, 90f * tone, 0.001f, 0.060f, 0.015f);
                    return Math.Max(a, b);
                }
                case FootstepSurface.Grass:
                case FootstepSurface.ForestFloor:
                {
                    int a = Thump(buffer, offset, level, 95f * tone, 55f * tone, 0.004f, 0.090f, 0.030f);
                    int b = Band(buffer, offset, level * 0.35f, 2000f * tone, 2200f * tone, 0.6f, 0.006f, 0.060f, 0.020f, 0f);
                    return Math.Max(a, b);
                }
                default:
                    return RenderHit(surface, buffer, offset, level, tone);
            }
        }

        // ------------------------------------------------------------------ the surfaces

        /// <summary>Adds one hit at <paramref name="offset"/>; returns the end sample.</summary>
        private int RenderHit(FootstepSurface surface, float[] buffer, int offset, float level, float tone)
        {
            switch (surface)
            {
                case FootstepSurface.Road:
                {
                    int a = Thump(buffer, offset, level * 1f, 110f * tone, 60f * tone, 0.002f, 0.060f, 0.022f);
                    int b = Band(buffer, offset, level * 0.55f, 1400f * tone, 1400f * tone, 0.9f, 0.003f, 0.070f, 0.025f, 0.6f);
                    return Math.Max(a, b);
                }
                case FootstepSurface.Grass:
                    return Band(buffer, offset, level, 2400f * tone, 2600f * tone, 0.55f, 0.012f, 0.110f, 0.045f, 0f);
                case FootstepSurface.ForestFloor:
                {
                    int end = Thump(buffer, offset, level * 0.45f, 95f * tone, 55f * tone, 0.004f, 0.055f, 0.02f);
                    int grains = 3 + (int)(Unit() * 2.999f);
                    for (int g = 0; g < grains; g++)
                    {
                        int start = offset + (int)(sampleRate * 0.07f * Unit());
                        float length = 0.008f + 0.012f * Unit();
                        float f = (2000f + 4000f * Unit()) * tone;
                        float amp = level * (0.6f + 0.4f * Unit());
                        end = Math.Max(end, Band(buffer, start, amp, f, f, 1.6f, 0.0015f, length, length * 0.35f, 0f));
                    }

                    return end;
                }
                case FootstepSurface.Rock:
                {
                    int a = Band(buffer, offset, level, 650f * tone, 650f * tone, 1.1f, 0.001f, 0.040f, 0.010f, 0f);
                    int b = Thump(buffer, offset, level * 0.5f, 190f * tone, 130f * tone, 0.001f, 0.035f, 0.010f);
                    return Math.Max(a, b);
                }
                default:
                {
                    int a = Band(buffer, offset, level, 600f * tone, 3000f * tone, 1.0f, 0.010f, 0.180f, 0.080f, 0.3f);
                    int b = Thump(buffer, offset + (int)(0.012f * sampleRate), level * 0.55f, 210f * tone, 95f * tone, 0.004f, 0.070f, 0.03f);
                    return Math.Max(a, b);
                }
            }
        }

        /// <summary>
        /// The envelope of a burst <paramref name="duration"/> s long: a raised-sine attack, an exponential decay with time constant
        /// <paramref name="decay"/>, and a cosine taper over the last 30 % so it ends exactly at zero.
        /// </summary>
        private static float Envelope(float t, float attack, float duration, float decay)
        {
            if (t <= 0f || t >= duration) return 0f;
            float e = t < attack ? (float)Math.Sin(0.5 * Math.PI * t / attack) : (float)Math.Exp(-(t - attack) / decay);
            if (t < attack) e *= e;
            float taperFrom = duration * 0.7f;
            if (t > taperFrom) e *= 0.5f + 0.5f * (float)Math.Cos(Math.PI * (t - taperFrom) / (duration - taperFrom));
            return e;
        }

        /// <summary>A sine whose pitch glides from <paramref name="f0"/> to <paramref name="f1"/>, under <see cref="Envelope"/>.</summary>
        private int Thump(float[] buffer, int offset, float amp, float f0, float f1, float attack, float duration, float decay)
        {
            int n = (int)(duration * sampleRate);
            double phase = 0.0;
            int end = Math.Min(buffer.Length, offset + n);
            for (int i = offset; i < end; i++)
            {
                float t = (i - offset) / sampleRate;
                float f = f0 + (f1 - f0) * (t / duration);
                phase += 2.0 * Math.PI * f / sampleRate;
                float env = Envelope(t, attack, duration, decay);
                buffer[i] += amp * env * (float)Math.Sin(phase);
                window[i] += amp * env;
            }

            return end;
        }

        /// <summary>
        /// White noise through a band-pass (centre gliding from <paramref name="f0"/> to <paramref name="f1"/>, quality
        /// <paramref name="q"/>), under <see cref="Envelope"/>; <paramref name="grit"/> 0..1 adds a random ~1 ms grain to its level.
        /// The band-pass has no gain at DC and the envelope follows the filter, so the burst ends at zero.
        /// </summary>
        private int Band(float[] buffer, int offset, float amp, float f0, float f1, float q, float attack, float duration, float decay, float grit)
        {
            int n = (int)(duration * sampleRate);
            int end = Math.Min(buffer.Length, offset + n);
            float x1 = 0f, x2 = 0f, y1 = 0f, y2 = 0f, z1 = 0f, z2 = 0f;
            float gritLevel = 1f;
            int gritStep = Math.Max(1, (int)(0.0012f * sampleRate));
            float nyquistSafe = sampleRate * 0.45f;
            // Level the band to a peak near 1 whatever its width: white noise (variance 1/3) through two 2nd-order band-passes in
            // series keeps a share of about (π/4 · f/q) / (fs/2) of its power.
            double fc = Math.Min(nyquistSafe, Math.Sqrt(f0 * f1));
            double rms = Math.Sqrt((1.0 / 3.0) * Math.Min(1.0, (0.25 * Math.PI * fc / q) / (0.5 * sampleRate)));
            amp *= (float)(0.33 / Math.Max(1e-3, rms));
            float b0 = 0f, a1 = 0f, a2 = 0f;
            bool sweep = f0 != f1;
            for (int i = offset, k = 0; i < end; i++, k++)
            {
                float t = k / sampleRate;
                if (k == 0 || (sweep && (k & 15) == 0))
                {
                    // RBJ band-pass, 0 dB peak gain (state: input history x, output history y; z is the second pass). A sweep
                    // updates its coefficients every 16 samples, which keeps the audio thread's cost per step small.
                    float f = Math.Min(nyquistSafe, f0 + (f1 - f0) * (t / duration));
                    double w = 2.0 * Math.PI * f / sampleRate;
                    double alpha = Math.Sin(w) / (2.0 * q);
                    double a0 = 1.0 + alpha;
                    b0 = (float)(alpha / a0);
                    a1 = (float)(-2.0 * Math.Cos(w) / a0);
                    a2 = (float)((1.0 - alpha) / a0);
                }

                float b2 = -b0;
                float x = Noise();
                // Two passes of the same band-pass (4th order): steeper skirts, so each surface keeps to its band.
                float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1;
                x1 = x;
                float z = b0 * y + b2 * y2 - a1 * z1 - a2 * z2;
                y2 = y1;
                y1 = y;
                z2 = z1;
                z1 = z;
                y = z;
                if (grit > 0f && k % gritStep == 0) gritLevel = 1f - grit + grit * 2f * Unit();
                float env = Envelope(t, attack, duration, decay) * gritLevel;
                buffer[i] += amp * env * y;
                window[i] += amp * env;
            }

            return end;
        }

        /// <summary>Subtracts the mean along the summed envelope, so the step has no DC and still starts and ends at zero.</summary>
        private void RemoveMean(float[] buffer, int length)
        {
            double sum = 0.0, weight = 0.0;
            for (int i = 0; i < length; i++)
            {
                sum += buffer[i];
                weight += window[i];
            }

            if (weight <= 1e-12) return;
            float k = (float)(sum / weight);
            for (int i = 0; i < length; i++) buffer[i] -= k * window[i];
        }

        private float Noise() => Unit() * 2f - 1f;

        private float Signed() => Unit() * 2f - 1f;

        private float Unit()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 16777216f;
        }
    }
}
