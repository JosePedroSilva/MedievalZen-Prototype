using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.People
{
    /// <summary>
    /// The people's first-party character prefabs (one per <see cref="PeopleCatalog"/> entry: the Fantasy Kingdom character with only its own
    /// body mesh, no colliders, an Animator on the villager controller with a humanoid avatar whose hips follow the clips), built by
    /// <c>MedievalZen/People/Build People Palette</c> (and by itself on editor load when missing or out of date) into
    /// <c>Resources/PeoplePalette</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "MedievalZen/People Palette", fileName = "PeoplePalette")]
    public sealed class PeoplePalette : ScriptableObject
    {
        /// <summary>Path under a Resources folder.</summary>
        public const string ResourcePath = "PeoplePalette";

        /// <summary>Version of the build pipeline; a palette built by an older one is rebuilt.</summary>
        public const int CurrentVersion = 1;

        /// <summary>One character.</summary>
        [Serializable]
        public sealed class Entry
        {
            /// <summary>Synty character name.</summary>
            public string name = string.Empty;

            /// <summary>The first-party prefab.</summary>
            public GameObject prefab;

            /// <summary>Feminine clip set.</summary>
            public bool feminine;

            /// <summary>Measured height of the character in its idle pose, metres.</summary>
            public float height;

            /// <summary>Height of the hips above the feet in the idle pose (for sitting), metres.</summary>
            public float hipHeight = 0.9f;
        }

        [SerializeField] private int version;
        [SerializeField] private string catalogSignature = string.Empty;
        [SerializeField] private List<Entry> entries = new List<Entry>();

        /// <summary>Build version.</summary>
        public int Version { get => version; set => version = value; }

        /// <summary>Catalogue the palette was built from.</summary>
        public string CatalogSignature { get => catalogSignature; set => catalogSignature = value ?? string.Empty; }

        /// <summary>The characters.</summary>
        public List<Entry> Entries => entries;

        /// <summary>True when built by this code from this catalogue.</summary>
        public bool IsCurrent => version == CurrentVersion && catalogSignature == PeopleCatalog.Signature;

        /// <summary>The entry of a character, or null.</summary>
        public Entry Find(string name)
        {
            foreach (Entry e in entries)
            {
                if (e != null && e.name == name && e.prefab != null) return e;
            }

            return null;
        }
    }
}
