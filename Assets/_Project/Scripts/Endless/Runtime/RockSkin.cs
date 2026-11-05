using UnityEngine;
using UnityEngine.Rendering;

namespace MedievalZen.Endless
{
    /// <summary>
    /// A near tile's faceted rock skin (2026-10-06, endless only): the mesh prepared on the generating thread (<see cref="RockSkinMeshData"/>,
    /// up to 25k triangles with their own vertices) uploaded in one go, in its own builder slice, under the tile's root and drawn with the
    /// opaque <c>MedievalZen/RockSkin</c> material the palette menu builds into <c>Resources/RockSkin</c>. No collider (the terrain stays
    /// the ground); casts and receives shadows, since self-shadowing is what gives the relief its form. The mesh is destroyed with the
    /// tile's root (<see cref="OnDestroy"/>).
    /// Without the material (palette not rebuilt yet) the tile simply has no skin.
    /// </summary>
    public sealed class RockSkin : MonoBehaviour
    {
        /// <summary>Resources path of the skin's material (built by MedievalZen/Create Default World Palette).</summary>
        public const string MaterialResource = "RockSkin";

        private static Material material;
        private static bool looked;
        private Mesh mesh;

        private static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
        };

        /// <summary>Adds the skin under <paramref name="parent"/> (the tile's root, at the tile's corner); false when there is nothing to draw.</summary>
        public static bool Attach(Transform parent, RockSkinMeshData d)
        {
            if (parent == null || d == null || d.VertexCount == 0 || d.IndexCount == 0) return false;
            if (!looked)
            {
                looked = true;
                material = Resources.Load<Material>(MaterialResource);
                if (material == null) Debug.LogWarning("Endless road: no Resources/RockSkin material (MedievalZen/Create Default World Palette builds it); pass faces keep the terrain's own rock.");
            }

            if (material == null) return false;
            var go = new GameObject("Rock skin");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            var skin = go.AddComponent<RockSkin>();
            skin.mesh = new Mesh { name = "Endless rock skin" };
            const MeshUpdateFlags flags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;
            var bounds = new Bounds();
            bounds.SetMinMax(d.BoundsMin, d.BoundsMax);
            skin.mesh.SetVertexBufferParams(d.VertexCount, Layout);
            skin.mesh.SetVertexBufferData(d.Vertices, 0, 0, d.VertexCount, 0, flags);
            skin.mesh.SetIndexBufferParams(d.IndexCount, IndexFormat.UInt32);
            skin.mesh.SetIndexBufferData(d.Indices, 0, 0, d.IndexCount, flags);
            skin.mesh.subMeshCount = 1;
            skin.mesh.SetSubMesh(0, new SubMeshDescriptor(0, d.IndexCount, MeshTopology.Triangles) { bounds = bounds, firstVertex = 0, vertexCount = d.VertexCount }, flags);
            skin.mesh.bounds = bounds;
            skin.mesh.UploadMeshData(true);
            go.AddComponent<MeshFilter>().sharedMesh = skin.mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            return true;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            mesh = null;
        }
    }
}
