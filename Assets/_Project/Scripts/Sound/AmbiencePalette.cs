using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Sound
{
    /// <summary>
    /// The ambience's clips with their measured levels (2026-10-06), built by <c>MedievalZen/Sound/Build Ambience Palette</c> (and by itself on
    /// editor load when missing or older than <see cref="AmbienceCatalog.Version"/>) into <c>Resources/AmbiencePalette</c>. Every clip is
    /// trimmed to its category's target (<see cref="SoundMix"/>); <see cref="Entry.userTrimDb"/> and <see cref="Entry.enabled"/> are the
    /// user's and survive every rebuild.
    /// </summary>
    [CreateAssetMenu(menuName = "MedievalZen/Ambience Palette", fileName = "AmbiencePalette")]
    public sealed class AmbiencePalette : ScriptableObject
    {
        /// <summary>Path under a Resources folder.</summary>
        public const string ResourcePath = "AmbiencePalette";

        /// <summary>One clip.</summary>
        [Serializable]
        public sealed class Entry
        {
            /// <summary>What it is for.</summary>
            public AmbienceRole role;

            /// <summary>How it is played.</summary>
            public AmbienceCategory category;

            /// <summary>Path relative to <c>Assets/Sounds</c>.</summary>
            public string path = string.Empty;

            /// <summary>The clip.</summary>
            public AudioClip clip;

            /// <summary>Measured whole-file RMS (dBFS); for spot sounds, the loudest second.</summary>
            public float rmsDb;

            /// <summary>Measured peak (dBFS).</summary>
            public float peakDb;

            /// <summary>Quietest and loudest 1 s window (dBFS RMS).</summary>
            public float windowMinDb, windowMaxDb;

            /// <summary>Loop seam: level of the last 0.5 s minus the first 0.5 s (dB), and the largest sample jump across the seam (0..2).</summary>
            public float seamLevelDb, seamJump;

            /// <summary>Gain to the target, dB (computed on every build).</summary>
            public float trimDb;

            [Tooltip("Your own correction on top of the measured trim, dB (kept on rebuild).")]
            public float userTrimDb;

            [Tooltip("Untick to never play this clip (kept on rebuild).")]
            public bool enabled = true;

            /// <summary>Linear gain of the trims (may exceed 1 for a quiet clip: the director spreads it over the source volume).</summary>
            public float Gain => SoundMix.DbToGain(trimDb + userTrimDb);

            /// <summary>File name without folder or extension.</summary>
            public string Name
            {
                get
                {
                    int slash = path.LastIndexOf('/');
                    string n = slash >= 0 ? path.Substring(slash + 1) : path;
                    return n.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 4) : n;
                }
            }
        }

        [SerializeField] private int version;
        [SerializeField] private List<Entry> entries = new List<Entry>();

        [NonSerialized] private Dictionary<AmbienceRole, List<Entry>> byRole;

        /// <summary>Catalog version the palette was built from.</summary>
        public int Version
        {
            get => version;
            set => version = value;
        }

        /// <summary>True when built from the current catalog.</summary>
        public bool IsCurrent => version == AmbienceCatalog.Version;

        /// <summary>Every entry (the editor fills it).</summary>
        public List<Entry> Entries => entries;

        /// <summary>The playable entries of a role (enabled, with a clip); empty when none.</summary>
        public List<Entry> For(AmbienceRole role)
        {
            if (byRole == null)
            {
                byRole = new Dictionary<AmbienceRole, List<Entry>>();
                foreach (Entry e in entries)
                {
                    if (e == null || !e.enabled || e.clip == null) continue;
                    if (!byRole.TryGetValue(e.role, out List<Entry> list)) byRole[e.role] = list = new List<Entry>();
                    list.Add(e);
                }
            }

            return byRole.TryGetValue(role, out List<Entry> found) ? found : Empty;
        }

        private static readonly List<Entry> Empty = new List<Entry>();

        /// <summary>Forget the role lookup (after an edit).</summary>
        public void Invalidate() => byRole = null;

        private void OnValidate() => byRole = null;
    }
}
