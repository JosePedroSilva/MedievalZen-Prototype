using System.Collections.Generic;
using MedievalZen.World;

namespace MedievalZen.Endless
{
    /// <summary>
    /// Pure layout of the endless road's terrain layer sets (2026-10-06): the shared eight, plus the mountain group (pass ranges) and
    /// the field group (farmland) on the tiles whose paint uses them. URP's terrain shader draws four layers a pass: 8 layers 2 passes,
    /// 11 three, 14 (a pass-and-farmland tile) four. The woods' leaf litter takes the cobblestone's slot on the tiles that paint it
    /// (no wood floor has cobbles), so a forest tile keeps its eight layers and two passes; only a tile that paints both appends it.
    /// </summary>
    public static class EndlessLayerSets
    {
        /// <summary>Kind bits: mountain group, field group, leaf litter, leaf litter appended (the tile also paints cobbles).</summary>
        public const int MountainBit = 1, FieldBit = 2, LitterBit = 4, LitterAppendedBit = 8;

        /// <summary>Number of layer set kinds (<see cref="LayerSetKind"/> values).</summary>
        public const int KindCount = 16;

        /// <summary>
        /// Layer set kind of a tile: the mountain and field groups when its paint uses them, and the leaf litter when it paints the litter
        /// (in the cobblestone's slot, or appended when <paramref name="cobblePaint"/>).
        /// </summary>
        public static int LayerSetKind(bool mountainPaint, bool fieldPaint, bool litterPaint = false, bool cobblePaint = false) =>
            (mountainPaint ? MountainBit : 0) | (fieldPaint ? FieldBit : 0) | (litterPaint ? LitterBit : 0) | (litterPaint && cobblePaint ? LitterAppendedBit : 0);

        /// <summary>
        /// The shared channel each endless-only channel folds onto when its layer is not in a tile's set (rock, rock, dry; flowers, flowers,
        /// lush; mud for the leaf litter).
        /// </summary>
        public static readonly int[] EndlessStandIns =
        {
            TerrainShaper.RockLayer, TerrainShaper.RockLayer, TerrainShaper.DryGrassLayer,
            TerrainShaper.FlowerLayer, TerrainShaper.FlowerLayer, TerrainShaper.LushGrassLayer,
            TerrainShaper.MudLayer,
        };

        /// <summary>The shared channel the cobblestone folds onto when the leaf litter takes its slot (dirt).</summary>
        public const int CobbleStandIn = TerrainShaper.DirtLayer;

        /// <summary>
        /// Pure layout of a layer set of kind <paramref name="kind"/> (<see cref="LayerSetKind"/>): for every endless channel the slot it
        /// paints (0-7 the shared layers, then the appended ones in order), and the channels appended after the shared eight (each
        /// group only when its kind asks for it and its layer exists, <paramref name="available"/> indexed from channel 8). A pass-and-
        /// farmland tile appends both groups: 14 layers, four terrain passes. With the litter bit the leaf litter paints slot
        /// <see cref="TerrainShaper.CobbleLayer"/> (that slot then holds the litter layer, see <see cref="LitterInCobbleSlot"/>) and the
        /// cobblestone folds onto dirt; with <see cref="LitterAppendedBit"/> as well it is appended instead.
        /// </summary>
        public static int[] ChannelSlots(int kind, bool[] available, out int[] appended)
        {
            var slots = new int[TerrainShaper.EndlessLayerCount];
            var list = new List<int>();
            for (int c = 0; c < slots.Length; c++)
            {
                if (c < TerrainShaper.LayerCount) { slots[c] = c; continue; }
                int e = c - TerrainShaper.LayerCount;
                bool has = available != null && e < available.Length && available[e];
                if (c == TerrainShaper.LeafLitterLayer)
                {
                    if (has && (kind & LitterBit) != 0 && (kind & LitterAppendedBit) == 0)
                    {
                        slots[c] = TerrainShaper.CobbleLayer;
                        slots[TerrainShaper.CobbleLayer] = CobbleStandIn;
                        continue;
                    }

                    if (!has || (kind & LitterBit) == 0)
                    {
                        slots[c] = EndlessStandIns[e];
                        continue;
                    }
                }
                else
                {
                    bool mountain = c <= TerrainShaper.HeathLayer;
                    bool wanted = mountain ? (kind & MountainBit) != 0 : (kind & FieldBit) != 0;
                    if (!wanted || !has)
                    {
                        slots[c] = EndlessStandIns[e];
                        continue;
                    }
                }

                slots[c] = TerrainShaper.LayerCount + list.Count;
                list.Add(c);
            }

            appended = list.ToArray();
            return slots;
        }

        /// <summary>True when a set of kind <paramref name="kind"/> draws the leaf litter in the cobblestone's slot (<see cref="ChannelSlots"/>).</summary>
        public static bool LitterInCobbleSlot(int kind, bool[] available)
        {
            int e = TerrainShaper.LeafLitterLayer - TerrainShaper.LayerCount;
            return (kind & LitterBit) != 0 && (kind & LitterAppendedBit) == 0 && available != null && e < available.Length && available[e];
        }

        /// <summary>Terrain passes of a set of <paramref name="layers"/> layers (URP draws four a pass).</summary>
        public static int Passes(int layers) => (layers + 3) / 4;
    }
}
