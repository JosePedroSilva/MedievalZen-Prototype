using UnityEngine;

namespace MedievalZen.Environment
{
    /// <summary>
    /// A very quiet synthesised wind bed (there are no audio assets): per-channel white noise through two one-pole low-pass
    /// filters (a soft "whoosh" band), with slow gusts that follow the <see cref="WeatherSystem"/> wind. Left and right use
    /// independent noise so the bed is wide. Off by default (<see cref="playWind"/>). Generated in
    /// <c>OnAudioFilterRead</c> on the audio thread; the main thread only publishes two target values.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class ProceduralWindAudio : MonoBehaviour
    {
        [SerializeField, Tooltip("Play the synthesised wind bed.")]
        private bool playWind = false;

        [SerializeField, Range(0f, 0.3f), Tooltip("Master volume (keep it low — it is a bed, not an effect).")]
        private float volume = 0.06f;

        [SerializeField, Tooltip("Low-pass cutoff range in Hz: calm → gusting.")]
        private Vector2 cutoffRange = new Vector2(220f, 900f);

        [SerializeField, Range(0.005f, 0.2f), Tooltip("Speed of the gust envelope (Hz-ish).")]
        private float gustSpeed = 0.045f;

        private volatile float targetGain;
        private volatile float targetCutoff = 300f;
        private float sampleRate = 48000f;
        private float gain;
        private float cutoff = 300f;
        private uint seedL = 0x9E3779B9u;
        private uint seedR = 0x7F4A7C15u;
        private float l1, l2, r1, r2;

        /// <summary>Whether the wind bed plays (fades in and out smoothly on the audio thread).</summary>
        public bool PlayWind
        {
            get => playWind;
            set => playWind = value;
        }

        /// <summary>Master volume, 0..0.3.</summary>
        public float Volume
        {
            get => volume;
            set => volume = Mathf.Clamp(value, 0f, MaxVolume);
        }

        /// <summary>Largest <see cref="Volume"/>.</summary>
        public const float MaxVolume = 0.3f;

        private void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000f;
            var source = GetComponent<AudioSource>();
            source.clip = null;
            source.loop = true;
            source.spatialBlend = 0f;
            source.playOnAwake = true;
            source.volume = 1f;
            if (!source.isPlaying) source.Play();
        }

        private void Update()
        {
            if (!playWind)
            {
                targetGain = 0f;
                return;
            }

            float wind = WeatherSystem.Current != null ? WeatherSystem.Current.Weather.wind : 0.3f;
            float gust = Mathf.PerlinNoise(Time.time * gustSpeed, 0.37f);
            gust = gust * gust;
            float level = Mathf.Clamp01(0.25f + 0.75f * wind) * (0.45f + 0.55f * gust);
            targetGain = volume * level;
            targetCutoff = Mathf.Lerp(cutoffRange.x, cutoffRange.y, Mathf.Clamp01(wind * 0.6f + gust * 0.6f));
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float goalGain = targetGain;
            float goalCutoff = targetCutoff;
            if (goalGain <= 0f && gain <= 1e-5f)
            {
                gain = 0f;
                return;
            }

            // Parameter smoothing per buffer keeps it click-free.
            float dt = 1f / sampleRate;
            int frames = data.Length / Mathf.Max(1, channels);
            for (int i = 0; i < frames; i++)
            {
                gain += (goalGain - gain) * 0.0005f;
                cutoff += (goalCutoff - cutoff) * 0.0005f;
                float rc = 1f / (2f * Mathf.PI * cutoff);
                float a = dt / (rc + dt);

                float nl = Noise(ref seedL);
                float nr = Noise(ref seedR);
                l1 += a * (nl - l1);
                l2 += a * (l1 - l2);
                r1 += a * (nr - r1);
                r2 += a * (r1 - r2);

                // Boost to compensate the filter's loss of energy at low cutoffs.
                float boost = 3f;
                int o = i * channels;
                if (channels >= 2)
                {
                    data[o] += l2 * gain * boost;
                    data[o + 1] += r2 * gain * boost;
                }
                else if (channels == 1)
                {
                    data[o] += (l2 + r2) * 0.5f * gain * boost;
                }
            }
        }

        private static float Noise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFF) / 8388608f - 1f;
        }
    }
}
