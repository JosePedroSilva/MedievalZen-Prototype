using System;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// The colours the ground and the grass are given so that grass clumps, bare ground between them and distant ground all
    /// read as the same sward (pure data, shared by the palette menu, which bakes them into first-party terrain layers and
    /// detail materials, and by the endless road's far tiles, which colour distant ground with them).
    /// <para>
    /// Measured on 2026-10-02 (linear RGB): the Meadow grass clumps take their colour from their material (Synty Foliage
    /// graph, "Leaf Flat Color" with world-space colour noise: lerp(lerp(noise, base, small), large, big), mean over 40,000
    /// positions) – tall (0.167, 0.267, 0.005), medium and short (0.190, 0.267, 0.005), i.e. sRGB (113-121, 141, 15) – while the
    /// lush ground texture averages (0.136, 0.185, 0.018) = sRGB (103, 119, 36) and the dry one (0.072, 0.097, 0.022) = sRGB
    /// (76, 88, 41): a CIE76 ΔE of 17 on lush ground and 38 on dry ground, clumps 1.4× and 2.6× brighter than the ground.
    /// </para>
    /// <para>
    /// Targets: the lush sward (tall and medium clumps) (0.169, 0.243, 0.009) = sRGB (114, 135, 23); short clumps (the base
    /// cover of dry ground) lean 30 % toward straw (0.30, 0.25, 0.06): (0.208, 0.245, 0.024) = sRGB (126, 136, 43). Lush ground
    /// is 6 % darker than its tall/medium clumps (ΔE 3); dry ground is 6 % darker than a 42 % straw sward, sRGB (126, 132, 47)
    /// (ΔE 3.9 to the short clumps, 6.9 from short clumps to lush ground), so clumps still read as texture and the two zones
    /// stay distinct (ΔE 10 between lush and dry ground).
    /// </para>
    /// </summary>
    public static class GroundLook
    {
        /// <summary>
        /// Average colour of each terrain layer's diffuse texture (linear RGB, mean of the sRGB-decoded texels of the full 2048²
        /// PNG), in <see cref="TerrainShaper"/> channel order: lush grass (Grass_Texture_01), dry grass (Grass_Texture_02), dirt
        /// (Dirt_Texture_01), rock (RockWall_Texture_01), forest floor (Grass_Leaves_Texture_01), flower meadow
        /// (Grass_Flowers_Texture_01), mud (Mud_Texture_01), cobblestone (Cobblestone_Texture_01); then the endless road's mountain
        /// channels (<see cref="TerrainShaper.EndlessLayerCount"/>, measured 2026-10-05 the same way): mountain rock
        /// (Rock_Rough_Texture_01), scree (Gravel_Texture_01), heath (Grass_Debris_Texture_01).
        /// </summary>
        public static readonly Vector3[] TextureMeans =
        {
            new Vector3(0.1357f, 0.1851f, 0.0176f),
            new Vector3(0.0722f, 0.0971f, 0.0221f),
            new Vector3(0.3208f, 0.2368f, 0.1619f),
            new Vector3(0.2973f, 0.2167f, 0.1472f),
            new Vector3(0.1378f, 0.1746f, 0.0180f),
            new Vector3(0.2152f, 0.1977f, 0.0153f),
            new Vector3(0.0560f, 0.0399f, 0.0255f),
            new Vector3(0.2319f, 0.1990f, 0.1548f),
            new Vector3(0.2290f, 0.1983f, 0.1603f),
            new Vector3(0.2198f, 0.2017f, 0.1731f),
            new Vector3(0.1485f, 0.1653f, 0.0223f),
            // The endless road's field channels (2026-10-06, measured the same way): field gold (Grass_Texture_02, low contrast:
            // p99 within 8 % of the mean, so a strong tint gives no hot texels), field yellow (Grass_Texture_01), field green (Grass_Clovers_Texture_01).
            new Vector3(0.0723f, 0.0971f, 0.0221f),
            new Vector3(0.1358f, 0.1851f, 0.0176f),
            new Vector3(0.1542f, 0.1854f, 0.0181f),
            // The woods' leaf litter (2026-10-06, Grass_Leaves_Heavy_Texture_01, every 4th texel of the 2048² PNG): s.d. (0.095, 0.038,
            // 0.013); 53 % of the texels are fallen leaves (0.241, 0.143, 0.033), 47 % dark grass between them (0.083, 0.104, 0.011).
            new Vector3(0.1667f, 0.1241f, 0.0223f),
        };

        /// <summary>
        /// Per-channel multiplier each terrain layer applies to its texture (<c>TerrainLayer.diffuseRemapMax</c> of the first-party
        /// layers; URP Terrain Lit multiplies the sampled albedo by it). Grass layers are pulled to their sward colour; the others
        /// are unchanged.
        /// </summary>
        public static readonly Vector3[] LayerTints =
        {
            new Vector3(1.1416f, 1.1990f, 0.5150f),   // lush grass → (0.155, 0.222, 0.009)
            new Vector3(2.9132f, 2.3791f, 1.2844f),   // dry grass → (0.210, 0.231, 0.028)
            Vector3.one,
            Vector3.one,
            Vector3.one,
            new Vector3(0.8916f, 1.1138f, 0.7816f),   // flower meadow → halfway to the lush sward
            Vector3.one,
            Vector3.one,
            new Vector3(1.0060f, 1.1597f, 1.3700f),   // mountain rock → neutral grey, sRGB (132, 132, 129), L* 55 (the warm rock: 55, b* 14)
            new Vector3(1.4639f, 1.5001f, 1.5389f),   // scree → pale grey gravel, sRGB (154, 149, 141), L* 62
            new Vector3(0.5402f, 0.5104f, 1.6542f),   // heath → dark olive-green-brown upland turf, sRGB (80, 82, 54), L* 34 (2026-10-06; was the tan-reading 107, 100, 63)
            new Vector3(7.6350f, 3.2337f, 1.5538f),   // field gold → ripe grain, sRGB (196, 152, 52)
            new Vector3(4.5449f, 2.5894f, 1.2045f),   // field yellow → flowering rapeseed, sRGB (206, 184, 40)
            new Vector3(0.3972f, 1.1643f, 1.7622f),   // field green → young crop, blue-green, sRGB (70, 128, 50)
            // leaf litter → (0.125, 0.079, 0.023), sRGB (99, 79, 42), L* 35, 44 % of the lush ground's luminance: the leaves turn autumn
            // brown-ochre, sRGB (118, 84, 51), the grass between them dark olive soil (71, 73, 29); green pulled most, so no lime reads through.
            new Vector3(0.7499f, 0.6366f, 1.0314f),
        };

        /// <summary>Terrain channels whose layer is a first-party tinted copy (see <see cref="LayerTints"/>).</summary>
        public static bool IsTinted(int channel) => channel == TerrainShaper.LushGrassLayer || channel == TerrainShaper.DryGrassLayer || channel == TerrainShaper.FlowerLayer ||
            channel >= TerrainShaper.LayerCount;

        /// <summary>The colour a layer shows from a distance (linear): its texture mean times its tint.</summary>
        public static Vector3 LayerColor(int channel) => Vector3.Scale(TextureMeans[channel], LayerTints[channel]);

        /// <summary>Every layer's distant colour, in channel order (the endless road's eleven channels; the finite world uses the first eight).</summary>
        public static Vector3[] LayerColors()
        {
            var colors = new Vector3[TextureMeans.Length];
            for (int c = 0; c < colors.Length; c++) colors[c] = LayerColor(c);
            return colors;
        }

        /// <summary>
        /// Endless road, the floor of a wood between the crowns (2026-10-06, leaf-litter pass): the forest-floor layer (leaves in grass)
        /// with mud, a little dirt and drifts of the leaf-litter layer, the litter share following a slow noise <paramref name="litter"/>
        /// (0 mossy .. 1 littered). Returns the shares of (forest floor, mud, dirt, leaf litter); they sum to 1. At litter 0.5 the mix
        /// is linear (0.115, 0.104, 0.029), sRGB (95, 91, 48), 53 % of the lush ground's luminance: the dark moss between the crowns (under them, <see cref="LeafLitterShares"/>; <see cref="WoodFloorBlend"/>
        /// mixes the two).
        /// </summary>
        public static Vector4 WoodFloorShares(float litter)
        {
            // Leaf-litter pass (2026-10-06): the litter layer drifts into the moss (0.18-0.40) in place of mud (0.44-0.64 → 0.30-0.38),
            // which read as grey; dirt 0.06-0.11 → 0.04-0.06 (its pale texture read as sand).
            float t = litter < 0f ? 0f : litter > 1f ? 1f : litter;
            float leaves = 0.18f + 0.22f * t, mud = 0.30f + 0.08f * t, dirt = 0.04f + 0.02f * t;
            return new Vector4(1f - leaves - mud - dirt, mud, dirt, leaves);
        }

        /// <summary>
        /// Endless road, the leaf litter under a tree's crown (leaf-litter pass, 2026-10-06): mostly the leaf-litter layer
        /// (<see cref="TerrainShaper.LeafLitterLayer"/>, fallen leaves on dark soil) with a little mud and dirt and a trace of the
        /// forest-floor layer, the shares following the same litter noise as <see cref="WoodFloorShares"/>. Returns (forest floor,
        /// mud, dirt, leaf litter), summing to 1. At litter 0.5 the mix is linear (0.125, 0.086, 0.027), sRGB (99, 83, 46), L* 36, 47 % of the lush ground's luminance
        /// (lush ground (0.155, 0.222, 0.009), sRGB (110, 130, 24), L* 51): autumn brown on dark soil (was mud and dirt, sRGB (96, 87, 63), which
        /// read as pale sand-grey).
        /// </summary>
        public static Vector4 LeafLitterShares(float litter)
        {
            float t = litter < 0f ? 0f : litter > 1f ? 1f : litter;
            float leaves = 0.74f + 0.14f * t, mud = 0.12f - 0.04f * t, dirt = 0.03f;
            return new Vector4(1f - leaves - mud - dirt, mud, dirt, leaves);
        }

        /// <summary>
        /// Mean crown cover of a deep wood's floor (share of the ground under a crown, measured on seed 12345's near tiles in Asherford
        /// Chase): the distant tiles, which have no crowns, blend <see cref="LeafLitterShares"/> into the moss by this much.
        /// </summary>
        public const float WoodCanopyMean = 0.45f;

        /// <summary>The wood floor's shares (forest floor, mud, dirt, leaf litter) at <paramref name="litter"/> with <paramref name="crown"/> (0..1) of it under a crown.</summary>
        public static Vector4 WoodFloorBlend(float litter, float crown)
        {
            float c = crown < 0f ? 0f : crown > 1f ? 1f : crown;
            return WoodFloorShares(litter) * (1f - c) + LeafLitterShares(litter) * c;
        }

        /// <summary>Endless road field paint (2026-10-06): pasture, hay meadow, green crop, ripe grain, rapeseed, stubble, ploughed earth, and the grass balk between strips.</summary>
        public const int FieldPasture = 0, FieldHayMeadow = 1, FieldGreenCrop = 2, FieldRipeGrain = 3, FieldRapeseed = 4, FieldStubble = 5, FieldPloughed = 6, FieldBalk = 7;

        /// <summary>
        /// The village's inner ground (2026-10-06, <c>VillageCloses</c>): a close or paddock (grazed and trodden pasture), a kitchen
        /// garden (dug beds and greens), an orchard's grass, and a worn track (headland and field tracks, the lanes' continuation).
        /// </summary>
        public const int FieldClose = 8, FieldGarden = 9, FieldOrchard = 10, FieldTrack = 11;

        /// <summary>
        /// Near tiles only (lived-in closes, 2026-10-06): a paddock's trodden edge along its fence, and a kitchen garden's two kinds of
        /// row, dug soil and greens (2 m each). From a distance a garden is painted <see cref="FieldGarden"/>, the mean of its rows.
        /// </summary>
        public const int FieldWorn = 12, FieldGardenSoil = 13, FieldGardenGreens = 14;

        /// <summary>Number of field looks.</summary>
        public const int FieldLookCount = 15;

        /// <summary>Names of the field looks (logs, tests).</summary>
        public static readonly string[] FieldLookNames = { "pasture", "hay meadow", "green crop", "ripe grain", "rapeseed", "stubble", "ploughed", "balk", "close", "garden", "orchard", "track", "worn", "garden soil", "garden greens" };

        /// <summary>
        /// True for the looks of a village's closes. They carry no mud: the Meadow mud layer is metallic 0.3 and smooth 0.5, and its
        /// share (0.18 in the paddocks, 0.45 in the gardens) turned the closes into sky-reflecting slate "ponds" from above and at a
        /// low angle (the Foxerley overview, 2026-10-06: closes sRGB (121-125, 133-135, 100-103) beside lawn (138-144, 152-157, 65-67)).
        /// </summary>
        public static bool IsCloseLook(int look) => look == FieldClose || look == FieldGarden || look == FieldOrchard || look == FieldWorn || look == FieldGardenSoil || look == FieldGardenGreens;

        /// <summary>
        /// Endless road field paint (2026-10-06, second pass): the share of each of the endless road's fourteen channels (lush, dry,
        /// dirt, rock, forest floor, flowers, mud, cobble, mountain rock, scree, heath, field gold, field yellow, field green) per field
        /// look. The crops lead with their own field channel (ripe grain gold, rapeseed yellow, green crop the blue-green), so they carry
        /// from a distance; stubble is gold with pale dirt, ploughed dirt with mud. Distant faces whose weights match a look take its
        /// colour (<see cref="MatchFieldLook"/>), so near and far agree whatever channel leads. sRGB and CIE76 ΔE from the lawn
        /// (lush 0.27, dry 0.67, flowers 0.04, dirt 0.02 = (123, 131, 44)) in the comments.
        /// </summary>
        public static readonly float[][] FieldShares =
        {
            //     lush   dry   dirt  rock  ff    flwr  mud   cob   mrk   scr   hth   gold  yell  green
            new[] { 0.40f, 0.60f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },             // pasture (120, 131, 39), ΔE 2: the herds' grass
            new[] { 0f, 0.45f, 0f, 0f, 0f, 0.25f, 0f, 0f, 0f, 0f, 0f, 0.30f, 0f, 0f },           // hay meadow (151, 138, 45), ΔE 11
            new[] { 0f, 0f, 0f, 0f, 0f, 0f, 0.10f, 0f, 0f, 0f, 0f, 0f, 0f, 0.90f },             // green crop (70, 123, 49), ΔE 21
            new[] { 0f, 0f, 0.08f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.92f, 0f, 0f },             // ripe grain (193, 151, 60), ΔE 26
            new[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.85f, 0.15f },             // rapeseed (193, 177, 42), ΔE 29
            new[] { 0f, 0f, 0.55f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.45f, 0f, 0f },             // stubble (174, 142, 91), ΔE 26
            new[] { 0f, 0f, 0.50f, 0f, 0f, 0f, 0.50f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },             // ploughed (120, 104, 86), ΔE 38
            new[] { 0.45f, 0.55f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },             // balk
            // Lived-in closes (2026-10-06): no mud (see IsCloseLook); was paddock (120, 122, 57) with mud 0.18, garden (94, 105, 66) with mud 0.45.
            new[] { 0.45f, 0f, 0f, 0f, 0f, 0.10f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.45f },          // paddock: grazed bright green (96, 129, 38), ΔE 10
            new[] { 0.15f, 0.10f, 0.40f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.35f },       // kitchen garden from afar: the mean of its rows
            new[] { 0.30f, 0f, 0f, 0f, 0f, 0.45f, 0f, 0f, 0f, 0f, 0f, 0f, 0.10f, 0.15f },       // orchard grass: lush, flowering (125, 136, 33)
            new[] { 0f, 0.30f, 0.62f, 0f, 0f, 0f, 0.08f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },          // worn track
            new[] { 0f, 0.50f, 0.50f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },             // worn: a paddock's trodden edge (141, 133, 87)
            new[] { 0f, 0.20f, 0.80f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },             // garden soil: dug beds
            new[] { 0.30f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.70f },             // garden greens: cabbage and bean rows (84, 128, 44)
        };

        /// <summary>
        /// True for the looks a distant face takes by matching its weights (every look that is not the lawn's own grass, nor one of the
        /// near tiles' fine looks: a distant tile paints a garden and a paddock whole).
        /// </summary>
        public static bool IsMatchedFieldLook(int look) => look != FieldPasture && look != FieldBalk && look != FieldWorn && look != FieldGardenSoil && look != FieldGardenGreens;

        /// <summary>
        /// The field look whose mix <paramref name="weights"/> (normalised, channel order) matches within <paramref name="tolerance"/>
        /// (sum of absolute differences), or -1. Used by the distant tiles so a field reads in its own colour whatever channel leads.
        /// </summary>
        public static int MatchFieldLook(float[] weights, int channels, float tolerance = 0.3f)
        {
            int best = -1;
            float bestD = tolerance;
            for (int look = 0; look < FieldShares.Length; look++)
            {
                if (!IsMatchedFieldLook(look)) continue;
                float[] mix = FieldShares[look];
                float d = 0f;
                int n = Math.Max(channels, mix.Length);
                for (int c = 0; c < n && d < bestD; c++)
                {
                    float w = c < channels ? weights[c] : 0f, m = c < mix.Length ? mix[c] : 0f;
                    d += Math.Abs(w - m);
                }

                if (d >= bestD) continue;
                bestD = d;
                best = look;
            }

            return best;
        }

        /// <summary>The distant colour (linear) of a field look: its shares of the layer colours.</summary>
        public static Vector3 FieldColor(int look)
        {
            float[] s = FieldShares[look];
            Vector3 c = Vector3.zero;
            for (int i = 0; i < s.Length; i++) c += LayerColor(i) * s[i];
            return c;
        }

        /// <summary>
        /// Rapeseed flower heads (sRGB 0..1): the palette menu bakes a first-party copy of the Meadow flower texture with its white heads
        /// recoloured to this yellow for the rapeseed prefabs (the shared Flowers_Field material left a field of white heads: it read
        /// as snow). Stems and leaves keep their colour.
        /// </summary>
        public static readonly Color RapeseedFlower = new Color(0.93f, 0.80f, 0.16f, 1f);

        /// <summary>
        /// Distant colour (linear) of the wood floor mix (<see cref="WoodFloorBlend"/>) at <paramref name="litter"/> with <paramref name="crown"/>
        /// of it under a crown: at the defaults (0.120, 0.096, 0.028), sRGB (97, 87, 47), 50 % of the lush ground's luminance.
        /// </summary>
        public static Vector3 WoodFloorColor(float litter = 0.5f, float crown = WoodCanopyMean) => WoodFloorMixColor(WoodFloorBlend(litter, crown));

        /// <summary>Colour (linear) of wood floor shares (forest floor, mud, dirt, leaf litter).</summary>
        public static Vector3 WoodFloorMixColor(Vector4 s) =>
            LayerColor(TerrainShaper.ForestFloorLayer) * s.x + LayerColor(TerrainShaper.MudLayer) * s.y + LayerColor(TerrainShaper.DirtLayer) * s.z +
            LayerColor(TerrainShaper.LeafLitterLayer) * s.w;

        /// <summary>
        /// The field looks are written with the fourteen channels they use; the leaf litter (2026-10-06) and any later endless channel
        /// are 0 in every look, so the rows are padded to <see cref="TerrainShaper.EndlessLayerCount"/> once.
        /// </summary>
        static GroundLook()
        {
            for (int i = 0; i < FieldShares.Length; i++)
            {
                if (FieldShares[i].Length >= TerrainShaper.EndlessLayerCount) continue;
                float[] row = FieldShares[i];
                Array.Resize(ref row, TerrainShaper.EndlessLayerCount);
                FieldShares[i] = row;
            }
        }

        /// <summary>Measured mean albedo of the Synty grass clumps (linear) before any tint, per detail family.</summary>
        public static Vector3 MeasuredGrass(string family)
        {
            switch (family)
            {
                case PropRole.TallGrass: return new Vector3(0.1666f, 0.2672f, 0.0048f);
                case PropRole.MedGrass: return new Vector3(0.1901f, 0.2677f, 0.0047f);
                case PropRole.ShortGrass: return new Vector3(0.1912f, 0.2672f, 0.0048f);
                default: return Vector3.zero;
            }
        }

        /// <summary>Lush sward colour (tall and medium clumps), linear.</summary>
        public static readonly Vector3 LushSward = new Vector3(0.1686f, 0.2427f, 0.0086f);

        /// <summary>Dry sward colour (short clumps), linear.</summary>
        public static readonly Vector3 DrySward = new Vector3(0.2080f, 0.2449f, 0.0240f);

        /// <summary>
        /// Per-channel multiplier the first-party material of a grass family applies to the Synty leaf colours (base, noise and
        /// large noise), so the clumps average their sward colour; <see cref="Vector3.one"/> for families that keep their look.
        /// </summary>
        public static Vector3 GrassTint(string family)
        {
            Vector3 measured = MeasuredGrass(family);
            if (measured == Vector3.zero) return Vector3.one;
            Vector3 target = family == PropRole.ShortGrass ? DrySward : LushSward;
            return new Vector3(target.x / measured.x, target.y / measured.y, target.z / measured.z);
        }

        /// <summary>
        /// How far the sward details' normals are turned up (1 = straight up: a clump shades exactly like flat ground; lower
        /// keeps some of the blades' own shading, which brings back bright speckles under a low sun).
        /// </summary>
        public const float SwardNormalUp = 1f;

        /// <summary>
        /// Every smoothness and metallic property the Synty foliage graph (and its older property names) exposes. The sward
        /// materials set them to <see cref="SwardSmoothness"/> / <see cref="SwardMetallic"/>: with upward normals each blade is a
        /// mirror for the sky at grazing angles, which made the grass look frosted white at dusk (smoothness 0.2, metallic 0.132).
        /// </summary>
        public static readonly string[] SwardSmoothnessProperties = { "_Leaf_Smoothness", "_Trunk_Smoothness", "_Smoothness", "_LeafSmoothness", "_TrunkSmoothness" };

        /// <summary>Metallic properties of the foliage graph the sward materials set to <see cref="SwardMetallic"/>.</summary>
        public static readonly string[] SwardMetallicProperties = { "_Leaf_Metallic", "_Trunk_Metallic", "_Metallic", "_LeafMetallic", "_TrunkMetallic" };

        /// <summary>Smoothness of the sward materials: matte, like the terrain layers (all 0).</summary>
        public const float SwardSmoothness = 0f;

        /// <summary>Metallic of the sward materials.</summary>
        public const float SwardMetallic = 0f;

        /// <summary>
        /// True for detail families drawn as a soft sward: no shadows of their own and normals pointing up (they shade like the
        /// ground they stand on). Tall sunflowers keep their own normals and shadows.
        /// </summary>
        public static bool IsSward(string family) => family != PropRole.SunflowerDetail;

        /// <summary>CIE 1976 ΔE between two linear RGB colours (sRGB primaries, D65).</summary>
        public static float DeltaE(Vector3 a, Vector3 b)
        {
            Vector3 la = Lab(a), lb = Lab(b);
            return (la - lb).magnitude;
        }

        /// <summary>CIELAB (L*, a*, b*) of a linear RGB colour.</summary>
        public static Vector3 Lab(Vector3 c)
        {
            double x = (0.4124 * c.x + 0.3576 * c.y + 0.1805 * c.z) / 0.95047;
            double y = 0.2126 * c.x + 0.7152 * c.y + 0.0722 * c.z;
            double z = (0.0193 * c.x + 0.1192 * c.y + 0.9505 * c.z) / 1.08883;
            double fx = F(x), fy = F(y), fz = F(z);
            return new Vector3((float)(116.0 * fy - 16.0), (float)(500.0 * (fx - fy)), (float)(200.0 * (fy - fz)));
        }

        private static double F(double t) => t > 0.008856 ? Math.Pow(t, 1.0 / 3.0) : 7.787 * t + 16.0 / 116.0;
    }
}
