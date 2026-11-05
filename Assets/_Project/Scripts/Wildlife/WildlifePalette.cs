using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Wildlife
{
    /// <summary>
    /// The animals' assets, built by the editor menu MedievalZen/Wildlife/Build Wildlife Palette (and automatically when it is
    /// missing or out of date) into <c>Assets/_Project/Resources/WildlifePalette.asset</c>, loaded at runtime with
    /// <c>Resources.Load</c> so no scene needs setting up. Per prefab: the stripped first-party prefab variant (no pack AI,
    /// NavMeshAgent, CharacterController or sound script) and its measured idle-pose size; per species: a scale multiplier
    /// for tuning by eye; and the pack's sound clips.
    /// </summary>
    public sealed class WildlifePalette : ScriptableObject
    {
        /// <summary>Bumped when the palette's layout or the way it is built changes (rebuilds it).</summary>
        public const int CurrentVersion = 2;

        /// <summary>Path under a Resources folder.</summary>
        public const string ResourcePath = "WildlifePalette";

        /// <summary>One animal prefab with its measured size.</summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Pack prefab name (e.g. Cow_Brown).")]
            public string name;

            [Tooltip("First-party prefab variant with the pack's scripts and components removed.")]
            public GameObject prefab;

            [Tooltip("Measured height in metres of the idle pose at scale 1 (baked skinned meshes).")]
            public float height;

            [Tooltip("Measured body length along +Z at scale 1, metres.")]
            public float length;

            [Tooltip("Lowest point of the idle pose relative to the root, metres at scale 1 (feet below the pivot are negative).")]
            public float footOffset;

            [Tooltip("Name of the controller's default state (the idle the bool driver waits for).")]
            public string idleState;

            [Tooltip("The controller's bool parameters.")]
            public string[] bools = Array.Empty<string>();
        }

        /// <summary>A per-species scale multiplier for tuning.</summary>
        [Serializable]
        public sealed class Tuning
        {
            public Species species;

            [Range(0.5f, 2f), Tooltip("Multiplies the catalog's target height.")]
            public float scale = 1f;
        }

        [SerializeField] private int version;
        [SerializeField] private string catalogSignature = string.Empty;
        [SerializeField] private List<Entry> entries = new List<Entry>();
        [SerializeField] private List<Tuning> tuning = new List<Tuning>();
        [SerializeField] private List<AudioClip> clips = new List<AudioClip>();

        private Dictionary<string, Entry> byName;
        private Dictionary<string, AudioClip> clipByName;

        public int Version { get => version; set => version = value; }
        public string CatalogSignature { get => catalogSignature; set => catalogSignature = value; }
        public List<Entry> Entries => entries;
        public List<Tuning> TuningRows => tuning;
        public List<AudioClip> Clips => clips;

        /// <summary>True when built for this code (version and catalog) with every prefab present.</summary>
        public bool IsCurrent
        {
            get
            {
                if (version != CurrentVersion || catalogSignature != SpeciesCatalog.Signature) return false;
                foreach (Entry e in entries)
                {
                    if (e == null || e.prefab == null) return false;
                }

                return entries.Count > 0;
            }
        }

        /// <summary>The entry for a pack prefab name, or null.</summary>
        public Entry Find(string prefabName)
        {
            if (byName == null || byName.Count != entries.Count)
            {
                byName = new Dictionary<string, Entry>(StringComparer.Ordinal);
                foreach (Entry e in entries)
                {
                    if (e != null && !string.IsNullOrEmpty(e.name)) byName[e.name] = e;
                }
            }

            return prefabName != null && byName.TryGetValue(prefabName, out Entry found) ? found : null;
        }

        /// <summary>A clip by name, or null.</summary>
        public AudioClip Clip(string clipName)
        {
            if (clipByName == null || clipByName.Count != clips.Count)
            {
                clipByName = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
                foreach (AudioClip c in clips)
                {
                    if (c != null) clipByName[c.name] = c;
                }
            }

            return clipName != null && clipByName.TryGetValue(clipName, out AudioClip found) ? found : null;
        }

        /// <summary>The tuning multiplier of a species (1 when not set).</summary>
        public float ScaleMultiplier(Species s)
        {
            foreach (Tuning t in tuning)
            {
                if (t != null && t.species == s) return Mathf.Clamp(t.scale, 0.25f, 4f);
            }

            return 1f;
        }

        /// <summary>
        /// Uniform scale for a prefab of a species: the catalog's target height over the measured height (a stag uses the
        /// hind's scale so only its antlers add height), times the tuning multiplier. 1 when unmeasured.
        /// </summary>
        public float ScaleFor(SpeciesInfo info, Entry entry)
        {
            float measured = entry != null ? entry.height : 0f;
            float target = info.TargetHeight;
            if (info.ScaleLike.HasValue)
            {
                SpeciesInfo like = SpeciesCatalog.Get(info.ScaleLike.Value);
                Entry e = Find(like.Prefabs[0]);
                if (e != null && e.height > 0.05f)
                {
                    measured = e.height;
                    target = like.TargetHeight;
                }
            }

            // A measurement under 5 cm is a failed one (rebuild the palette): show the prefab as authored rather than giant.
            float scale = measured > 0.05f ? Mathf.Clamp(target / measured, 0.1f, 10f) : 1f;
            return scale * ScaleMultiplier(info.Id);
        }

        private void OnValidate()
        {
            byName = null;
            clipByName = null;
        }
    }
}
