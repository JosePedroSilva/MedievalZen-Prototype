using UnityEngine;

namespace MedievalZen.Sound
{
    /// <summary>
    /// Plays the synthesised placeholder footsteps (<see cref="FootstepVoice"/>) in <c>OnAudioFilterRead</c>, modelled on
    /// <c>ProceduralWindAudio</c>: a 2D source with no clip on a child of the player. The main thread only queues steps
    /// (<see cref="Play"/>, lock-free) and publishes the bus gain; the audio thread renders them.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class FootstepSynth : MonoBehaviour
    {
        private readonly FootstepQueue queue = new FootstepQueue(16);
        private FootstepVoice voice;
        private volatile float gain;

        private void Awake()
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            voice = new FootstepVoice(rate, (uint)System.Environment.TickCount | 1u);
            var source = GetComponent<AudioSource>();
            source.clip = null;
            source.loop = true;
            source.spatialBlend = 0f;
            source.playOnAwake = true;
            source.volume = 1f;
            source.priority = 32;
            if (!source.isPlaying) source.Play();
            gain = SoundMix.FootstepGain;
        }

        /// <summary>Queues one step (main thread).</summary>
        public void Play(FootstepSurface surface, int foot, bool sprint)
        {
            queue.Push(new FootstepRequest { Surface = surface, Foot = foot, Sprint = sprint });
        }

        /// <summary>Queues one hoofbeat (main thread): <paramref name="beat"/> its index in the gait's cycle (0 accented, odd ones panned right).</summary>
        public void PlayHoof(FootstepSurface surface, int beat)
        {
            queue.Push(new FootstepRequest { Surface = surface, Foot = beat & 1, Sprint = beat == 0, Hoof = true });
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("FootstepSynth");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            gain = SoundMix.FootstepGain;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            FootstepVoice v = voice;
            if (v == null) return;
            while (queue.TryPop(out FootstepRequest request)) v.Trigger(request);
            v.Render(data, channels, gain);
        }
    }
}
