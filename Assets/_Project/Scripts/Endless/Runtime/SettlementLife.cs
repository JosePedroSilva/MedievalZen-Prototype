using System.Collections.Generic;
using MedievalZen.Environment;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The endless road's buildings as the tile builder brings them to life (2026-10-05): which placed props get chimney smoke and a
    /// lit window (every dwelling, from <see cref="BuildingTable"/>'s measured chimney or roof vent and the middle of the front wall) or a
    /// lamp light (street lamps), and where the inn's rest point stands (the foot of the tavern's or the refuge hut's door, facing out).
    /// The stone cabin keeps the old rules (measured chimney from the scene menu, light by its bounds) so places dressed by the shared
    /// dresser look as before.
    /// </summary>
    public static class SettlementLife
    {
        /// <summary>The lantern of the Fantasy Kingdom street lamps in the lamp's local space (arm 0.69 m toward -Z, about 2.3 m up).</summary>
        public static readonly Vector3 LampLight = new Vector3(0f, 2.25f, -0.62f);

        /// <summary>Props that get smoke and a window light, or a lamp light, from their settlement's ambience.</summary>
        public static bool HasAmbience(string role) => PropRole.IsDwelling(role) || role == PropRole.Lamp;

        /// <summary>
        /// The building an inn's rest point belongs to: the cabin (shared dresser), the tavern, the refuge's hut, or the house an alehouse
        /// (or a farm, lodge or waterside inn) keeps as its inn; an inn's dressing holds exactly one (the caller checks the place is an inn).
        /// </summary>
        public static bool IsInnBuilding(string role) => MedievalZen.Endless.Generation.InnComposer.IsRestBuilding(role);

        /// <summary>
        /// The ambience anchor of a measured building or lamp (prop-local), or false for the stone cabin and anything unknown (the caller
        /// keeps its own rule for those).
        /// </summary>
        public static bool TryAnchor(GameObject instance, PropPlacement prop, GameObject prefab, out SettlementAmbience.CabinAnchor anchor)
        {
            anchor = default;
            if (instance == null || prefab == null) return false;
            if (prop.role == PropRole.Lamp)
            {
                anchor = new SettlementAmbience.CabinAnchor { cabin = instance.transform, chimneyLocal = Vector3.zero, lightLocal = LampLight, smoke = false };
                return true;
            }

            if (prop.role == PropRole.Cabin || !PropRole.IsBuilding(prop.role)) return false;
            BuildingShape shape = BuildingTable.ForPrefab(prefab.name);
            if (shape == null) return false;
            anchor = new SettlementAmbience.CabinAnchor
            {
                cabin = instance.transform,
                chimneyLocal = shape.Smoke + new Vector3(0f, shape.Chimney ? 0.15f : 0f, 0f),
                lightLocal = shape.WindowLight,
                smoke = PropRole.IsDwelling(prop.role),
            };
            return true;
        }

        /// <summary>
        /// Puts the inn's <see cref="MedievalZen.Travel.InnRest"/> point at the foot of a measured building's door (0.5 m out, at the ground
        /// the composer seated it on, 0.55 m above the pivot), facing out of it; the stone cabin keeps <see cref="WorldBuilder.PlaceInnDoor"/>.
        /// </summary>
        public static GameObject PlaceInnDoor(GameObject prefab, PropPlacement prop, PrefabFamily family, Vector3 position, Quaternion rotation, Transform parent, string innName)
        {
            BuildingShape shape = prop.role == PropRole.Cabin ? null : BuildingTable.ForPrefab(prefab != null ? prefab.name : null);
            if (shape == null) return WorldBuilder.PlaceInnDoor(prefab, prop, family, position, rotation, parent, Vector3.zero, innName);
            Vector3 outward = rotation * shape.Outward;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.forward;
            Vector3 door = position + rotation * shape.Door + outward * 0.5f;
            door.y = position.y + GroundAbovePivot(shape);
            var go = new GameObject("InnDoor");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = door;
            go.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);
            go.AddComponent<MedievalZen.Travel.InnRest>().Initialize(prop.settlementId, innName);
            return go;
        }

        /// <summary>How far above the pivot the composer leaves the ground at a building's highest corner (stone bases are sunk 0.55 m).</summary>
        public static float GroundAbovePivot(BuildingShape shape) => shape != null && shape.BaseDepth > 0.5f ? 0.55f : 0f;

        /// <summary>
        /// Settlements with ambience props in a tile but no waypoint there (a village's houses spread over several tiles): their ids, so each
        /// part of the place gets its own smoke and lights.
        /// </summary>
        public static List<int> SettlementsWithoutWaypoint(List<(GameObject cabin, PropPlacement prop, GameObject prefab)> cabins, List<Waypoint> waypoints)
        {
            var ids = new List<int>();
            foreach ((GameObject cabin, PropPlacement prop, GameObject prefab) in cabins)
            {
                if (cabin == null || prop.settlementId < 0 || ids.Contains(prop.settlementId)) continue;
                bool here = false;
                if (waypoints != null)
                {
                    foreach (Waypoint w in waypoints)
                    {
                        if (w != null && w.IsSettlement && w.Id == prop.settlementId)
                        {
                            here = true;
                            break;
                        }
                    }
                }

                if (!here) ids.Add(prop.settlementId);
            }

            return ids;
        }
    

        // ------------------------------------------------------------------ inn name boards

        private static Font signFont;
        private static Material signLetters, signWoodFallback;
        private static Mesh cube;
        private static readonly List<(MeshFilter filter, string text)> lettered = new List<(MeshFilter, string)>();

        /// <summary>Pixel size the lettering is rasterised at in the font atlas.</summary>
        private const int LetterPixels = 64;

        /// <summary>
        /// Builds an inn's name board at <paramref name="prop"/> (a <see cref="PropRole.InnSign"/> spot, tile-local, under
        /// <paramref name="parent"/>): two posts, a cross beam and a board hanging from it (unit cubes in the palette's sign wood), with
        /// <paramref name="name"/> painted on both faces. The lettering is a mesh of glyph quads from Unity's built-in dynamic font
        /// (LegacyRuntime.ttf; no font asset or package), drawn with an alpha-clipped lit material (dark at night but for the lamps),
        /// and culled by a LODGroup beyond about InnSign.LettersVisible m. The board is in the tile's places, so it goes with the tile.
        /// </summary>
        public static GameObject PlaceNameBoard(PropPlacement prop, Transform parent, string name, WorldPalette palette)
        {
            if (cube == null) cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Material wood = palette != null && palette.signWoodMaterial != null ? palette.signWoodMaterial : WoodFallback();
            var root = new GameObject("InnSign " + name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = prop.position;
            root.transform.localRotation = Quaternion.Euler(0f, prop.yaw, 0f);

            float w = Generation.InnSign.BoardWidth, postX = w * 0.5f + 0.12f, top = Generation.InnSign.PostHeight;
            float bottom = Generation.InnSign.BoardBottom, height = Generation.InnSign.BoardHeight, boardMid = bottom + height * 0.5f;
            foreach (float side in new[] { -1f, 1f })
            {
                GameObject post = Box(root.transform, wood, new Vector3(side * postX, top * 0.5f - 0.3f, 0f), new Vector3(Generation.InnSign.PostSize, top + 0.6f, Generation.InnSign.PostSize));
                post.AddComponent<BoxCollider>();
                // The board's hangers, from its top edge to the beam.
                float hang = top - 0.14f - (bottom + height);
                Box(root.transform, wood, new Vector3(side * w * 0.3f, bottom + height + hang * 0.5f, 0f), new Vector3(0.03f, hang, 0.03f));
            }

            Box(root.transform, wood, new Vector3(0f, top - 0.07f, 0f), new Vector3(w + 0.6f, 0.14f, 0.14f));
            Box(root.transform, wood, new Vector3(0f, boardMid, 0f), new Vector3(w, height, Generation.InnSign.BoardThickness));

            Material letters = LetterMaterial(palette);
            if (letters == null || string.IsNullOrEmpty(name)) return root;
            var text = new GameObject("Lettering");
            text.transform.SetParent(root.transform, false);
            var renderers = new List<Renderer>();
            foreach (float face in new[] { 1f, -1f })
            {
                var go = new GameObject(face > 0f ? "Front" : "Back");
                go.transform.SetParent(text.transform, false);
                go.transform.localPosition = new Vector3(0f, boardMid, face * (Generation.InnSign.BoardThickness * 0.5f + 0.004f));
                // The glyph quads face -Z of their own frame: turned so each face reads left to right from its own side.
                go.transform.localRotation = Quaternion.Euler(0f, face > 0f ? 180f : 0f, 0f);
                var filter = go.AddComponent<MeshFilter>();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = letters;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                filter.sharedMesh = new Mesh { name = "Lettering " + name };
                Letter(filter, name);
                lettered.Add((filter, name));
                renderers.Add(renderer);
            }

            var lod = text.AddComponent<LODGroup>();
            lod.localReferencePoint = new Vector3(0f, boardMid, 0f);
            lod.size = w;
            // Screen height of the board's width at the cut-off distance with a 60 degree view: w / (2 d tan 30).
            float cut = w / (2f * Generation.InnSign.LettersVisible * Mathf.Tan(30f * Mathf.Deg2Rad));
            lod.SetLODs(new[] { new LOD(cut, renderers.ToArray()) });
            return root;
        }

        private static GameObject Box(Transform parent, Material material, Vector3 at, Vector3 size)
        {
            var go = new GameObject("Board");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static Material WoodFallback()
        {
            if (signWoodFallback != null) return signWoodFallback;
            Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) return null;
            signWoodFallback = new Material(shader) { name = "SignWood (runtime)" };
            signWoodFallback.SetColor("_BaseColor", new Color(0.62f, 0.48f, 0.31f));
            return signWoodFallback;
        }

        /// <summary>A runtime copy of the palette's lettering material whose base map is the built-in font's atlas (the asset is never touched).</summary>
        private static Material LetterMaterial(WorldPalette palette)
        {
            if (signLetters != null) return signLetters;
            if (signFont == null)
            {
                signFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (signFont == null) return null;
                Font.textureRebuilt += OnFontRebuilt;
            }

            Material source = palette != null ? palette.signLetterMaterial : null;
            if (source == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (shader == null) return null;
                source = new Material(shader);
                source.SetColor("_BaseColor", new Color(0.10f, 0.07f, 0.05f));
                source.SetFloat("_AlphaClip", 1f);
                source.SetFloat("_Cutoff", 0.5f);
                source.EnableKeyword("_ALPHATEST_ON");
                source.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }

            signLetters = new Material(source) { name = "SignLetters (font atlas)" };
            SetAtlas(signLetters, signFont);
            return signLetters;
        }

        private static void SetAtlas(Material material, Font font)
        {
            Texture atlas = font.material != null ? font.material.mainTexture : null;
            material.SetTexture("_BaseMap", atlas);
            material.SetTexture("_MainTex", atlas);
        }

        private static readonly int CostFontRebuilt = Review.FrameCost.Register("SettlementLife.OnFontRebuilt (font atlas rebuilt)");

        /// <summary>
        /// The arrival warm-up (2026-10-09): asks the built-in font for <paramref name="characters"/> at the boards' lettering size
        /// and style, so a new name's letters do not grow (rebuild) the shared atlas on the road.
        /// </summary>
        public static void WarmGlyphs(string characters)
        {
            if (string.IsNullOrEmpty(characters)) return;
            Font font = signFont != null ? signFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) font.RequestCharactersInTexture(characters, LetterPixels, FontStyle.Bold);
        }

        /// <summary>The font atlas was rebuilt (glyphs moved): point the material at it and re-letter every live board.</summary>
        private static void OnFontRebuilt(Font font)
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(CostFontRebuilt);   // stall hunt: the per-frame culprit
            if (font != signFont) return;
            if (signLetters != null) SetAtlas(signLetters, font);
            for (int i = lettered.Count - 1; i >= 0; i--)
            {
                if (lettered[i].filter == null) lettered.RemoveAt(i);
            }

            foreach (var (filter, text) in lettered.ToArray()) Letter(filter, text);
        }

        /// <summary>Lays <paramref name="text"/> out on one line, centred, capitals InnSign.CapHeight high, or smaller to fit the board.</summary>
        private static void Letter(MeshFilter filter, string text)
        {
            const FontStyle style = FontStyle.Bold;
            signFont.RequestCharactersInTexture(text + "H", LetterPixels, style);
            float advance = 0f;
            foreach (char ch in text)
            {
                if (signFont.GetCharacterInfo(ch, out CharacterInfo ci, LetterPixels, style)) advance += ci.advance;
            }

            float cap = signFont.GetCharacterInfo('H', out CharacterInfo h, LetterPixels, style) && h.maxY > 0 ? h.maxY : LetterPixels * 0.7f;
            float room = Generation.InnSign.BoardWidth - 2f * Generation.InnSign.Margin;
            float scale = Mathf.Min(Generation.InnSign.CapHeight / cap, advance > 0f ? room / advance : 1f);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float x = -advance * scale * 0.5f, y = -cap * scale * 0.5f;
            foreach (char ch in text)
            {
                if (!signFont.GetCharacterInfo(ch, out CharacterInfo ci, LetterPixels, style)) continue;
                int k = vertices.Count;
                vertices.Add(new Vector3(x + ci.minX * scale, y + ci.minY * scale, 0f));
                vertices.Add(new Vector3(x + ci.maxX * scale, y + ci.minY * scale, 0f));
                vertices.Add(new Vector3(x + ci.maxX * scale, y + ci.maxY * scale, 0f));
                vertices.Add(new Vector3(x + ci.minX * scale, y + ci.maxY * scale, 0f));
                uvs.Add(ci.uvBottomLeft);
                uvs.Add(ci.uvBottomRight);
                uvs.Add(ci.uvTopRight);
                uvs.Add(ci.uvTopLeft);
                // Clockwise as seen from -Z: the reader looks along +Z of this frame.
                triangles.AddRange(new[] { k, k + 3, k + 2, k, k + 2, k + 1 });
                x += ci.advance * scale;
            }

            Mesh mesh = filter.sharedMesh;
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            var normals = new Vector3[vertices.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
            mesh.normals = normals;
            mesh.RecalculateBounds();
        }
    }
}
