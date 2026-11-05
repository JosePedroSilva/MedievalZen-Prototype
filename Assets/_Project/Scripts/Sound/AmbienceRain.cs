using System;
using UnityEngine;

namespace MedievalZen.Sound
{
    /// <summary>
    /// A placeholder rain bed synthesised on the audio thread (2026-10-06: the packs hold no open-air rain). The main thread only publishes
    /// the level and the rain amount; <see cref="RainSynth"/> does the work.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AmbienceRain : MonoBehaviour
    {
        private readonly RainSynth synth = new RainSynth();
        private volatile float level;
        private volatile float density;

        /// <summary>Linear gain of the bed (weight × bus); 0 silences it.</summary>
        public float Level
        {
            get => level;
            set => level = value < 0f ? 0f : value > 1f ? 1f : value;
        }

        /// <summary>Rain amount 0..1: more drops and a brighter hiss.</summary>
        public float Density
        {
            get => density;
            set => density = value < 0f ? 0f : value > 1f ? 1f : value;
        }

        private void Awake()
        {
            synth.SampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000f;
            var source = GetComponent<AudioSource>();
            source.clip = null;
            source.loop = true;
            source.spatialBlend = 0f;
            source.playOnAwake = true;
            source.volume = 1f;
            source.dopplerLevel = 0f;
            if (!source.isPlaying) source.Play();
        }

        private void OnAudioFilterRead(float[] data, int channels) => synth.Render(data, channels, level, density);
    }

    /// <summary>
    /// The rain bed's sound, pure: band-limited noise for the hiss (high-passed at 350 Hz, low-passed twice at 3.5–6.5 kHz) plus a Poisson
    /// stream of short decaying drop bursts, randomly panned, normalised by a running RMS so the bed sits at <see cref="SoundMix.BedTargetDb"/>
    /// at level 1. Adds into the buffer (the source plays no clip).
    /// </summary>
    public sealed class RainSynth
    {
        /// <summary>Output sample rate.</summary>
        public float SampleRate = 48000f;

        private float gain;
        private uint seed = 0x6C8E9CF5u;
        private float hpL, hpR, lpL1, lpL2, lpR1, lpR2, prevL, prevR;
        private float drop, dropPan;
        private float meanSquare = 0.1f;   // about the filtered noise's own: no burst while the estimate settles

        /// <summary>Adds a buffer of rain at <paramref name="level"/> (linear, 1 = the bed target) into <paramref name="data"/>.</summary>
        public void Render(float[] data, int channels, float level, float rain)
        {
            float goal = level * SoundMix.DbToGain(SoundMix.BedTargetDb);
            if (goal <= 0f && gain <= 1e-6f)
            {
                gain = 0f;
                return;
            }

            rain = rain < 0f ? 0f : rain > 1f ? 1f : rain;
            float dt = 1f / SampleRate;
            float aLp = dt / (1f / (2f * (float)Math.PI * (3500f + 3000f * rain)) + dt);
            float aHp = 1f / (1f + 2f * (float)Math.PI * 350f * dt);
            float dropsPerSample = (25f + 115f * rain) / SampleRate;
            float decay = (float)Math.Exp(-dt / 0.004f);
            int frames = data.Length / Math.Max(1, channels);
            for (int i = 0; i < frames; i++)
            {
                gain += (goal - gain) * 0.00005f;
                float nl = Noise(), nr = Noise();
                hpL = aHp * (hpL + nl - prevL);
                hpR = aHp * (hpR + nr - prevR);
                prevL = nl;
                prevR = nr;
                lpL1 += aLp * (hpL - lpL1);
                lpL2 += aLp * (lpL1 - lpL2);
                lpR1 += aLp * (hpR - lpR1);
                lpR2 += aLp * (lpR1 - lpR2);
                if (Noise() * 0.5f + 0.5f < dropsPerSample)
                {
                    float u = Noise() * 0.5f + 0.5f;
                    drop = 0.4f + 1.6f * u * u;
                    dropPan = Noise() * 0.5f + 0.5f;
                }

                float burst = drop * hpL;
                drop *= decay;
                float l = lpL2 * 1.6f + burst * (1f - dropPan);
                float r = lpR2 * 1.6f + burst * dropPan;
                meanSquare += ((l * l + r * r) * 0.5f - meanSquare) * 0.00002f;
                float norm = 1f / (float)Math.Sqrt(Math.Max(meanSquare, 1e-8f));
                if (norm > 30f) norm = 30f;
                l *= norm * gain;
                r *= norm * gain;
                if (float.IsNaN(l) || float.IsNaN(r) || float.IsInfinity(l) || float.IsInfinity(r))
                {
                    l = r = 0f;
                    hpL = hpR = lpL1 = lpL2 = lpR1 = lpR2 = prevL = prevR = drop = 0f;
                    meanSquare = 0.1f;
                }

                int o = i * channels;
                if (channels >= 2)
                {
                    data[o] += l;
                    data[o + 1] += r;
                }
                else if (channels == 1)
                {
                    data[o] += 0.5f * (l + r);
                }
            }
        }

        private float Noise()
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return (seed & 0xFFFFFF) / 8388608f - 1f;
        }
    }
}
