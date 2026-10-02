using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>A named list of interchangeable prefabs (variants) plus how to orient them.</summary>
    [Serializable]
    public sealed class PrefabFamily
    {
        [Tooltip("Family id (see PropRole), e.g. \"Cabin\", \"ForestTree\", \"TallGrass\".")]
        public string id = string.Empty;

        [Tooltip("Variants; placement records pick one by index (wrapped). Null entries are ignored.")]
        public GameObject[] prefabs = Array.Empty<GameObject>();

        [Tooltip("Added to the placement yaw so the mesh matches the generator's convention (line pieces run along +Z, doors face +Z).")]
        public float yawOffset;

        [Tooltip("If on, the builder shifts each instance so its mesh bounds centre (not its pivot) lands on the placement point.")]
        public bool centerPivot;

        [Tooltip("Detail families only: visible height (m above the pivot) of each prefab's mesh, parallel to Prefabs. Measured by the palette menu.")]
        public float[] nativeHeights = Array.Empty<float>();

        [Tooltip("Detail families only: largest horizontal extent (m) of each prefab's mesh, parallel to Prefabs.")]
        public float[] nativeWidths = Array.Empty<float>();

        [Tooltip("Rock-like families: measured shape of each prefab (bounds, face direction, underside grid), parallel to Prefabs. Measured by the palette menu; the generator seats rocks and cliffs from these.")]
        public PropMetrics[] metrics = Array.Empty<PropMetrics>();

        /// <summary>
        /// Measured shape of <paramref name="prefab"/> in this family: the palette menu's measurement, else the offline table
        /// (<see cref="PropMetricsTable"/>) by prefab name, else invalid.
        /// </summary>
        public PropMetrics MetricsOf(GameObject prefab)
        {
            if (prefabs == null || prefab == null) return default;
            int i = Array.IndexOf(prefabs, prefab);
            if (i >= 0 && metrics != null && i < metrics.Length && metrics[i].IsValid) return metrics[i];
            return PropMetricsTable.TryGet(prefab.name, out PropMetrics table) ? table : default;
        }

        /// <summary>
        /// Recorded native (height, width) of <paramref name="prefab"/> in this family, or <see cref="Vector2.zero"/>
        /// when unknown (older palettes).
        /// </summary>
        public Vector2 NativeSize(GameObject prefab)
        {
            if (prefabs == null || prefab == null) return Vector2.zero;
            int i = Array.IndexOf(prefabs, prefab);
            if (i < 0 || nativeHeights == null || nativeWidths == null || i >= nativeHeights.Length || i >= nativeWidths.Length) return Vector2.zero;
            return new Vector2(nativeHeights[i], nativeWidths[i]);
        }

        /// <summary>Creates an empty family.</summary>
        public PrefabFamily()
        {
        }

        /// <summary>Creates a family.</summary>
        public PrefabFamily(string id, GameObject[] prefabs)
        {
            this.id = id;
            this.prefabs = prefabs ?? Array.Empty<GameObject>();
        }
    }

    /// <summary>
    /// Prefabs, terrain layers and materials the world is dressed with. Generation only sees <see cref="GetKeys"/>
    /// (variant counts and line-piece lengths); <see cref="WorldBuilder"/> resolves family + variant back to a
    /// prefab. Create the default Synty palette with <c>MedievalZen/Create Default World Palette</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "MedievalZen/World Palette", fileName = "WorldPalette")]
    public sealed class WorldPalette : ScriptableObject
    {
        /// <summary>
        /// Version of the palette pipeline (detail single-mesh copies, base normalisation, native sizes, curated families; v3:
        /// measured rock/cliff metrics, Meadow-only highland rocks, the RockWall terrain layer). Palettes with a lower
        /// <see cref="detailVersion"/> are refreshed by the editor before generating.
        /// </summary>
        public const int CurrentDetailVersion = 3;

        /// <summary>
        /// Set by the editor: refreshes a palette whose <see cref="detailVersion"/> is stale (rebuilds the detail
        /// prefabs). Called from <see cref="GetKeys"/>, i.e. before every generation.
        /// </summary>
        public static Action<WorldPalette> StaleDetailUpgrader;

        [Tooltip("Detail-prefab pipeline version this palette was built with (see CurrentDetailVersion). Set by the palette menu.")]
        public int detailVersion;

        [Tooltip("Prefab families for trees (terrain tree instances) and GameObject props.")]
        public List<PrefabFamily> families = new List<PrefabFamily>();

        [Tooltip("Terrain detail families (grass, flowers). Each prefab must be a single mesh (no LOD group).")]
        public List<PrefabFamily> details = new List<PrefabFamily>();

        [Header("Line pieces")]
        [Tooltip("Length of one straight fence piece in metres (measured from the mesh by the default-palette menu).")]
        [Min(0.1f)] public float fencePieceLength = 3f;

        [Tooltip("Length of one long stone-wall piece in metres.")]
        [Min(0.1f)] public float stoneWallLength = 4f;

        [Header("Terrain")]
        [Tooltip("Terrain layers in splat order: lush grass, dry grass, dirt, rock, forest floor, flower meadow, mud, cobblestone.")]
        public TerrainLayer[] terrainLayers = Array.Empty<TerrainLayer>();

        [Header("Water")]
        [Tooltip("Plane prefab placed at lake level, scaled to cover the map.")]
        public GameObject waterPlane;

        [Tooltip("Optional material override for the water plane.")]
        public Material waterMaterial;

        [Header("Horizon")]
        [Tooltip("Model ringed around the map edge beyond the playable area.")]
        public GameObject backgroundHill;

        [Tooltip("Material for the background hills (the Synty model ships without one).")]
        public Material backgroundHillMaterial;

        [Header("Animation")]
        [Tooltip("Rotates its object around local X (windmill blades facing ±X).")]
        public RuntimeAnimatorController rotateXController;

        [Tooltip("Rotates its object around local Z (windmill blades facing ±Z).")]
        public RuntimeAnimatorController rotateZController;

        /// <summary>The family called <paramref name="id"/>, or null.</summary>
        public PrefabFamily FindFamily(string id) => Find(families, id);

        /// <summary>The detail family called <paramref name="id"/>, or null.</summary>
        public PrefabFamily FindDetail(string id) => Find(details, id);

        /// <summary>Non-null prefabs of a family list entry, in list order.</summary>
        public static List<GameObject> ValidPrefabs(PrefabFamily family)
        {
            var result = new List<GameObject>();
            if (family?.prefabs == null) return result;
            foreach (GameObject prefab in family.prefabs)
            {
                if (prefab != null) result.Add(prefab);
            }

            return result;
        }

        /// <summary>Prefab for <paramref name="family"/> and <paramref name="variant"/> (wrapped), or null.</summary>
        public GameObject GetPrefab(string family, int variant)
        {
            List<GameObject> valid = ValidPrefabs(FindFamily(family));
            if (valid.Count == 0) return null;
            return valid[((variant % valid.Count) + valid.Count) % valid.Count];
        }

        /// <summary>Extra yaw (degrees) applied to placements of <paramref name="family"/>.</summary>
        public float GetYawOffset(string family) => FindFamily(family)?.yawOffset ?? 0f;

        /// <summary>What the generator needs to know about this palette.</summary>
        public PaletteKeys GetKeys()
        {
            if (detailVersion < CurrentDetailVersion) StaleDetailUpgrader?.Invoke(this);
            var keys = new PaletteKeys { fenceLength = fencePieceLength, wallLength = stoneWallLength };
            foreach (PrefabFamily family in families)
            {
                if (family == null || string.IsNullOrEmpty(family.id)) continue;
                List<GameObject> valid = ValidPrefabs(family);
                keys.SetCount(family.id, valid.Count);
                if (!PropRole.IsRock(family.id) && family.id != PropRole.StoneStack && family.id != PropRole.StonePile) continue;
                var measured = new PropMetrics[valid.Count];
                bool any = false;
                for (int v = 0; v < valid.Count; v++)
                {
                    measured[v] = family.MetricsOf(valid[v]);
                    any |= measured[v].IsValid;
                }

                if (any) keys.SetMetrics(family.id, measured);
            }

            foreach (PrefabFamily family in details)
            {
                if (family != null && !string.IsNullOrEmpty(family.id)) keys.SetDetailCount(family.id, ValidPrefabs(family).Count);
            }

            return keys;
        }

        private static PrefabFamily Find(List<PrefabFamily> list, string id)
        {
            if (list == null || string.IsNullOrEmpty(id)) return null;
            foreach (PrefabFamily family in list)
            {
                if (family != null && family.id == id) return family;
            }

            return null;
        }
    }
}
