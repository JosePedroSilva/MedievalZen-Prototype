using System.Collections.Generic;
using MedievalZen.Environment;
using MedievalZen.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MedievalZen.Endless
{
    /// <summary>
    /// Endless road, seasons pass (2026-10-06, colours half): applies <see cref="SeasonLook"/> at <see cref="Season.Year"/> to what
    /// the endless world draws, only when <see cref="Season.Revision"/> moves (every other frame: one comparison).
    /// <list type="bullet">
    /// <item>Near terrain layers: the seasonal channels (lush, dry, flower meadow, heath, leaf litter) are run-time copies of the
    /// palette's layers (<see cref="Layers"/>); their <c>diffuseRemapMax</c> is the asset's value times the season factor. The assets
    /// on disk never change.</item>
    /// <item>Sward details: the grass clumps' prototypes are run-time clones (under the streamer's inactive pool) carrying material
    /// copies (<see cref="Detail"/>); their leaf base, noise and large-noise colours are the asset's times the season factor.</item>
    /// <item>Near trees: <see cref="EndlessTreeRenderer"/>'s own copies of the birch, Meadow oak and fruit leaf materials take
    /// <see cref="SeasonLook.LeafColour"/> on the same three colours.</item>
    /// <item>Distant trees and far tiles: the global tints of <c>FarTerrain.shader</c> (<c>_MZSeasonTints</c>), one per season class.</item>
    /// </list>
    /// At autumn every value set is exactly the original. No new pass, no extra terrain layer, no per-frame cost.
    /// </summary>
    internal sealed class SeasonPainter
    {
        private static readonly int TintsId = Shader.PropertyToID("_MZSeasonTints");
        private static readonly int TintsOnId = Shader.PropertyToID("_MZSeasonOn");

        /// <summary>The foliage graph's three leaf colours (the sward bake in <c>WorldPaletteMenu.SwardMaterial</c> tints the same three).</summary>
        private static readonly int[] LeafColourIds =
        {
            Shader.PropertyToID("_Leaf_Base_Color"), Shader.PropertyToID("_Leaf_Noise_Color"), Shader.PropertyToID("_Leaf_Noise_Large_Color"),
        };

        private struct LayerCopy
        {
            public TerrainLayer Copy;
            public Vector4 Original;
            public int Channel;
        }

        private struct ColourCopy
        {
            public Material Material;
            public Color[] Originals;   // per LeafColourIds; alpha unused by the graph but kept
            public bool[] Has;
            public string Family;       // sward family (null for leaves)
            public int LeafClass;       // leaf class (0 for sward)
        }

        private readonly Transform holder;
        private readonly Dictionary<(TerrainLayer, int), TerrainLayer> layerCopies = new Dictionary<(TerrainLayer, int), TerrainLayer>();
        private readonly List<LayerCopy> layers = new List<LayerCopy>();
        private readonly List<ColourCopy> colours = new List<ColourCopy>();
        private readonly List<GameObject> clones = new List<GameObject>();
        private readonly List<Material> ownMaterials = new List<Material>();
        private readonly HashSet<Material> seenTreeMaterials = new HashSet<Material>();
        private readonly SeasonGate gate = new SeasonGate();
        private readonly Vector4[] tints = new Vector4[SeasonLook.ClassCount];
        private int registrations;
        private float paintedYear = float.NaN;

        /// <summary>Called after the terrain layer copies changed, so live terrains pick the new tints up (set by the tile builder).</summary>
        public System.Action LayersChanged;

        /// <param name="holder">Inactive parent for the detail prototype clones (the streamer's pool: nothing under it awakes).</param>
        public SeasonPainter(Transform holder)
        {
            this.holder = holder;
        }

        /// <summary>Number of terrain layers, sward materials and leaf materials the painter recolours (log).</summary>
        public string Summary => $"season painter: {layers.Count} terrain layers, {colours.Count} materials";

        /// <summary>
        /// The palette's layers for channels <paramref name="firstChannel"/>.. with the seasonal channels replaced by run-time copies
        /// (cached per source and channel); other entries, and nulls, pass through.
        /// </summary>
        public TerrainLayer[] Layers(TerrainLayer[] source, int firstChannel)
        {
            if (source == null) return null;
            var result = (TerrainLayer[])source.Clone();
            for (int i = 0; i < result.Length; i++)
            {
                int channel = firstChannel + i;
                TerrainLayer layer = result[i];
                if (layer == null || !SeasonLook.IsSeasonalLayer(channel)) continue;
                if (!layerCopies.TryGetValue((layer, channel), out TerrainLayer copy))
                {
                    copy = Object.Instantiate(layer);
                    copy.name = layer.name + " (season)";
                    layerCopies[(layer, channel)] = copy;
                    layers.Add(new LayerCopy { Copy = copy, Original = layer.diffuseRemapMax, Channel = channel });
                    registrations++;
                }

                result[i] = copy;
            }

            return result;
        }

        /// <summary>
        /// A grass family's detail prototype with its prefab replaced by a run-time clone whose renderers carry material copies the
        /// painter recolours; any other family (flowers, sunflowers, crops) is returned unchanged.
        /// </summary>
        public DetailPrototype Detail(DetailPrototype prototype, string family)
        {
            if (prototype == null || prototype.prototype == null || holder == null || SeasonLook.SwardFactor(family, Season.Spring) == Vector3.one) return prototype;
            GameObject clone = Object.Instantiate(prototype.prototype, holder, false);
            clone.name = prototype.prototype.name + " (season)";
            clones.Add(clone);
            foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int m = 0; m < materials.Length; m++)
                {
                    if (materials[m] == null || !HasLeafColours(materials[m])) continue;
                    var copy = new Material(materials[m]) { name = materials[m].name + " (season)" };
                    ownMaterials.Add(copy);
                    colours.Add(Capture(copy, family, 0));
                    materials[m] = copy;
                }

                renderer.sharedMaterials = materials;
            }

            registrations++;
            prototype.prototype = clone;
            return prototype;
        }

        /// <summary>Once a frame: picks up new leaf material copies of <paramref name="trees"/>, and repaints when the year has moved.</summary>
        public void Tick(EndlessTreeRenderer trees)
        {
            if (trees != null && trees.InstancedMaterials.Count != seenTreeMaterials.Count) ScanTrees(trees);
            if (!gate.Due(Season.Revision, registrations)) return;
            Apply(Season.Year);
        }

        /// <summary>Sets every registered colour for <paramref name="year"/> (exactly the originals at the identity).</summary>
        public void Apply(float year)
        {
            bool identity = Season.Wrap(year) == Season.Identity;
            bool layersMoved = !(paintedYear == year);
            foreach (LayerCopy l in layers)
            {
                if (l.Copy == null) continue;
                Vector3 f = SeasonLook.LayerFactor(l.Channel, year);
                l.Copy.diffuseRemapMax = identity ? l.Original : new Vector4(l.Original.x * f.x, l.Original.y * f.y, l.Original.z * f.z, l.Original.w);
            }

            foreach (ColourCopy c in colours)
            {
                if (c.Material == null) continue;
                for (int i = 0; i < LeafColourIds.Length; i++)
                {
                    if (!c.Has[i]) continue;
                    Color original = c.Originals[i];
                    if (identity)
                    {
                        c.Material.SetColor(LeafColourIds[i], original);
                        continue;
                    }

                    Color linear = original.linear;
                    var v = new Vector3(linear.r, linear.g, linear.b);
                    Vector3 now = c.Family != null ? Vector3.Scale(v, SeasonLook.SwardFactor(c.Family, year)) : SeasonLook.LeafColour(c.LeafClass, v, year);
                    c.Material.SetColor(LeafColourIds[i], new Color(now.x, now.y, now.z, linear.a).gamma);
                }
            }

            SeasonLook.ClassTints(year, tints);
            Shader.SetGlobalVectorArray(TintsId, tints);
            Shader.SetGlobalFloat(TintsOnId, 1f);
            paintedYear = year;
            if (layersMoved && layers.Count > 0) LayersChanged?.Invoke();
        }

        /// <summary>Scene end: destroys the clones, copies and layer copies, and switches the far tints off.</summary>
        public void Dispose()
        {
            foreach (GameObject clone in clones)
            {
                if (clone != null) Object.Destroy(clone);
            }

            foreach (Material m in ownMaterials)
            {
                if (m != null) Object.Destroy(m);
            }

            foreach (TerrainLayer copy in layerCopies.Values)
            {
                if (copy != null) Object.Destroy(copy);
            }

            clones.Clear();
            ownMaterials.Clear();
            layerCopies.Clear();
            layers.Clear();
            colours.Clear();
            seenTreeMaterials.Clear();
            gate.Reset();
            Shader.SetGlobalFloat(TintsOnId, 0f);
        }

        private void ScanTrees(EndlessTreeRenderer trees)
        {
            foreach (KeyValuePair<Material, Material> pair in trees.InstancedMaterials)
            {
                if (pair.Key == null || pair.Value == null || !seenTreeMaterials.Add(pair.Key)) continue;
                int cls = SeasonLook.LeafClassOfMaterial(pair.Key.name);
                if (cls == SeasonLook.ClassNone || !HasLeafColours(pair.Value)) continue;
                colours.Add(Capture(pair.Value, null, cls));
                registrations++;
            }
        }

        private static bool HasLeafColours(Material m)
        {
            foreach (int id in LeafColourIds)
            {
                if (m.HasProperty(id)) return true;
            }

            return false;
        }

        private static ColourCopy Capture(Material m, string family, int leafClass)
        {
            var c = new ColourCopy { Material = m, Originals = new Color[LeafColourIds.Length], Has = new bool[LeafColourIds.Length], Family = family, LeafClass = leafClass };
            for (int i = 0; i < LeafColourIds.Length; i++)
            {
                c.Has[i] = m.HasProperty(LeafColourIds[i]);
                if (c.Has[i]) c.Originals[i] = m.GetColor(LeafColourIds[i]);
            }

            return c;
        }
    }
}
