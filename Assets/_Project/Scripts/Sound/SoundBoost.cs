using UnityEngine;

namespace MedievalZen.Sound
{
    /// <summary>
    /// Lets a source play above its clip's own level. An <see cref="AudioSource"/>'s volume stops at 1, and several of the packs'
    /// loops are 5–8 dB quieter than the level every bed is trimmed to (<see cref="SoundMix.BedTargetDb"/>): without this they
    /// would sit under the others whatever the sliders say. The gain above 1 is applied to the source's samples on the audio
    /// thread, ramped across each buffer; at 1 the filter does nothing.
    /// Sits on the source's own GameObject from the start (adding a filter to a playing source can glitch it).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SoundBoost : MonoBehaviour
    {
        /// <summary>The most a source is lifted (+12 dB).</summary>
        public const float MaxGain = 4f;

        private volatile float gain = 1f;
        private float current = 1f;   // audio thread only

        /// <summary>
        /// Sets a source's level from a linear gain that may exceed 1: the volume takes what it can, the source's
        /// <see cref="SoundBoost"/> (when it has one) the rest. Without one the gain stops at 1, as before.
        /// </summary>
        public static void Apply(AudioSource source, float gain)
        {
            if (source == null) return;
            if (!(gain > 0f))   // also catches NaN
            {
                source.volume = 0f;
                return;
            }

            bool has = source.TryGetComponent(out SoundBoost boost);
            if (gain <= 1f || !has)
            {
                source.volume = gain < 1f ? gain : 1f;
                if (has) boost.gain = 1f;
                return;
            }

            source.volume = 1f;
            boost.gain = gain < MaxGain ? gain : MaxGain;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float target = gain, g = current;
            if (target == 1f && g == 1f) return;
            if (channels <= 0 || data.Length < channels) return;
            int frames = data.Length / channels;
            float step = (target - g) / frames;
            for (int f = 0, i = 0; f < frames; f++)
            {
                g += step;
                for (int c = 0; c < channels; c++, i++) data[i] *= g;
            }

            current = target;
        }
    }
}
