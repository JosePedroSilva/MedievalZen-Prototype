using System;
using System.Text;
using UnityEngine;

namespace MedievalZen.Sound
{
    /// <summary>How often the music director may start a piece (Journey menu, sound page).</summary>
    public enum MusicFrequency
    {
        /// <summary>No music at all.</summary>
        Off = 0,

        /// <summary>The default: a piece is an event, with tens of minutes of ambience between two.</summary>
        Rare = 1,

        /// <summary>A piece every quarter of an hour or so.</summary>
        Sometimes = 2,

        /// <summary>A piece every few minutes.</summary>
        Often = 3,
    }

    /// <summary>
    /// The one place the sound systems meet (2026-10-06): the user's levels, the loudness every system calibrates to, and the little
    /// run-time state they share. The direction is the user's: "like Death Stranding, where the music stops and there are long periods
    /// with just ambience". Ambience is the default state of the game; a piece of music is a rare event.
    /// <para>
    /// Three systems read this and never each other: the ambience (<c>Ambience*</c>: beds, positioned water and places, spot sounds),
    /// the music (<c>Music*</c>: director, player, title card) and the footsteps (<c>Footstep*</c>). The Journey menu's sound page
    /// writes the sliders; the master volume is <see cref="AudioListener.volume"/> and is not repeated here.
    /// </para>
    /// <para>
    /// Loudness: nobody can listen offline, so every clip is measured (<see cref="WavFile"/>) and trimmed to a target, and the
    /// targets below are what a slider at 1 gives. Sliders are 0..1 and squared into a gain (<see cref="SliderGain"/>).
    /// </para>
    /// </summary>
    public static class SoundMix
    {
        /// <summary>RMS level (dBFS) of one ambience bed at full weight, slider at 1.</summary>
        public const float BedTargetDb = -30f;

        /// <summary>RMS level (dBFS) of a positioned loop (water, a place) at its reference distance, slider at 1.</summary>
        public const float EmitterTargetDb = -28f;

        /// <summary>RMS level (dBFS) of a piece of music at its fullest variant, slider at 1: just above the ambience.</summary>
        public const float MusicTargetDb = -23f;

        /// <summary>Peak level (dBFS) of one footstep, slider at 1.</summary>
        public const float FootstepPeakDb = -20f;

        /// <summary>How far the ambience steps back under a piece of music at full presence (dB). It never goes away.</summary>
        public const float DuckDb = -3f;

        /// <summary>Default slider positions (the Journey settings start from these).</summary>
        public const float DefaultAmbience = 0.8f, DefaultMusic = 0.8f, DefaultFootsteps = 0.7f;

        /// <summary>Ambience slider, 0..1 (beds, positioned loops, spot sounds).</summary>
        public static float Ambience = DefaultAmbience;

        /// <summary>Music slider, 0..1.</summary>
        public static float Music = DefaultMusic;

        /// <summary>Footsteps slider, 0..1.</summary>
        public static float Footsteps = DefaultFootsteps;

        /// <summary>How often a piece may start. Rare is the default (the user's choice, 2026-10-06).</summary>
        public static MusicFrequency Frequency = MusicFrequency.Rare;

        /// <summary>Whether a piece's title is shown for a few seconds when it starts.</summary>
        public static bool ShowMusicTitles = true;

        /// <summary>
        /// How audible the music is right now, 0..1 (its own fade envelope, before the slider). Written by the music player every
        /// frame, 0 when silent; the ambience steps back by <see cref="DuckDb"/> times this.
        /// </summary>
        public static float MusicPresence;

        /// <summary>
        /// A review tour or a walk benchmark is running (teleports, forced hours, scaled travel speed). The music must not start a
        /// piece and ends a playing one gracefully; ambience and footsteps carry on.
        /// </summary>
        public static bool CaptureRunning;

        /// <summary>One line each for the F3 overlay (null: nothing to say). Set by the system that owns it.</summary>
        public static Func<string> AmbienceStatus, MusicStatus, FootstepStatus;

        /// <summary>A 0..1 slider as a gain: squared, so the middle of the slider is a quarter of the power.</summary>
        public static float SliderGain(float slider) => slider <= 0f ? 0f : slider >= 1f ? 1f : slider * slider;

        /// <summary>Decibels to a linear gain.</summary>
        public static float DbToGain(float db) => (float)Math.Pow(10.0, db / 20.0);

        /// <summary>A linear gain in decibels (−120 for silence).</summary>
        public static float GainToDb(float gain) => gain <= 1e-6f ? -120f : (float)(20.0 * Math.Log10(gain));

        /// <summary>The ambience's bus gain: its slider, stepped back under music.</summary>
        public static float AmbienceGain
        {
            get
            {
                float presence = MusicPresence <= 0f ? 0f : MusicPresence >= 1f ? 1f : MusicPresence;
                return SliderGain(Ambience) * DbToGain(DuckDb * presence);
            }
        }

        /// <summary>The music's bus gain (its slider).</summary>
        public static float MusicGain => SliderGain(Music);

        /// <summary>The footsteps' bus gain (their slider).</summary>
        public static float FootstepGain => SliderGain(Footsteps);

        /// <summary>The sound systems' lines for the F3 overlay.</summary>
        public static void AppendOverlay(StringBuilder text)
        {
            if (text == null) return;
            Append(text, AmbienceStatus);
            Append(text, MusicStatus);
            Append(text, FootstepStatus);
        }

        private static void Append(StringBuilder text, Func<string> status)
        {
            if (status == null) return;
            string line;
            try
            {
                line = status();
            }
            catch (Exception)
            {
                return;   // an overlay line must never take the frame down
            }

            if (!string.IsNullOrEmpty(line)) text.Append(line).Append('\n');
        }

        /// <summary>The run-time state starts clean on every Play (the sliders are the settings' and stay).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntime()
        {
            MusicPresence = 0f;
            CaptureRunning = false;
            AmbienceStatus = null;
            MusicStatus = null;
            FootstepStatus = null;
        }
    }
}
