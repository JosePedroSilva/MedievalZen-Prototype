using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Sound
{
    /// <summary>
    /// The music tracks baked by <c>MedievalZen/Sound/Build Music Library</c> (also automatically on editor load when missing or stale)
    /// into <c>Assets/_Project/Resources/MusicLibrary.asset</c>: per track its five clips, tail, loop length, measured levels, the one
    /// trim that puts the full mix on <see cref="SoundMix.MusicTargetDb"/> (the sparser mixes keep their composed balance), mood
    /// weights, and the user's own fields (<see cref="Track.enabled"/>, <see cref="Track.userWeight"/>), kept on rebuild.
    /// </summary>
    public sealed class MusicLibrary : ScriptableObject
    {
        /// <summary>The Resources path.</summary>
        public const string ResourcePath = "MusicLibrary";

        [Serializable]
        public sealed class Track
        {
            public string title;
            public string folder;
            [Tooltip("Off: the director never picks this track (F8 skips it too).")]
            public bool enabled = true;
            [Tooltip("Extra weight on top of the measured mood weights (1 = as measured).")]
            [Range(0f, 4f)] public float userWeight = 1f;
            public AudioClip intensity1, intensity2, main, cut30, cut60;
            [Tooltip("Reverb tail (the folder's RT), seconds.")] public float tail;
            [Tooltip("Length of the full mixes, seconds.")] public float duration;
            [Tooltip("Musical length of an iteration (duration - tail), seconds.")] public float loop;
            public float cut30Seconds, cut60Seconds;
            [Tooltip("Measured RMS over the musical part, dBFS.")] public float mainRmsDb, intensity1RmsDb, intensity2RmsDb;
            [Tooltip("Level change from the last musical seconds into the first tail second, dB (strongly negative: the tail decays).")]
            public float tailDropDb;
            public float brightnessHz, pulse, onsets;
            [Tooltip("One trim for all five files (dB): Main lands on SoundMix.MusicTargetDb.")] public float trimDb;
            [Tooltip("OpenDay, Forest, Night, Rest.")] public float[] mood = new float[4];

            /// <summary>The clip of a variant (null when missing).</summary>
            public AudioClip Clip(MusicVariant v)
            {
                switch (v)
                {
                    case MusicVariant.Intensity1: return intensity1;
                    case MusicVariant.Intensity2: return intensity2;
                    case MusicVariant.Main: return main;
                    case MusicVariant.Cut30: return cut30;
                    default: return cut60;
                }
            }

            /// <summary>All five clips are there.</summary>
            public bool Complete => intensity1 != null && intensity2 != null && main != null && cut30 != null && cut60 != null;

            /// <summary>The trim as a linear gain.</summary>
            public float TrimGain => SoundMix.DbToGain(trimDb);
        }

        [HideInInspector] public int version;
        public List<Track> tracks = new List<Track>();

        /// <summary>Built with the current catalogue.</summary>
        public bool IsCurrent => version == MusicCatalog.Version && tracks != null && tracks.Count == MusicCatalog.Tracks.Length;

        /// <summary>The library from Resources (null when not built).</summary>
        public static MusicLibrary Load() => Resources.Load<MusicLibrary>(ResourcePath);

        /// <summary>The director's view of the tracks (a track with a missing clip is disabled).</summary>
        public MusicDirectorTrack[] DirectorTracks()
        {
            var result = new MusicDirectorTrack[tracks.Count];
            for (int i = 0; i < tracks.Count; i++)
            {
                Track t = tracks[i];
                result[i] = new MusicDirectorTrack
                {
                    Title = t.title,
                    Loop = t.loop,
                    Tail = t.tail,
                    Cut30 = t.cut30Seconds,
                    Cut60 = t.cut60Seconds,
                    Mood = t.mood != null && t.mood.Length == 4 ? (float[])t.mood.Clone() : new[] { 1f, 1f, 1f, 1f },
                    Enabled = t.enabled && t.Complete && t.loop > 1f,
                    UserWeight = t.userWeight,
                };
            }

            return result;
        }
    }
}
