using MedievalZen.Endless.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedievalZen.Endless
{
    /// <summary>
    /// A near tile's bridge walls (2026-10-06, endless only): coping stones and lining, end posts and approach walls prepared on the
    /// generating thread (<see cref="BridgeWallMeshData"/>, about 1,360 faceted triangles and 2,750 vertices per bridge, each stone its
    /// own atlas grey) uploaded in one go under the tile's root and
    /// drawn with the bridge stone material (<c>WorldPalette.bridgeMaterial</c>), with thin box colliders along the parapets, posts
    /// and approach walls. The mesh is destroyed with the tile's root (<see cref="OnDestroy"/>).
    /// </summary>
    public sealed class BridgeWallsView : MonoBehaviour
    {
        private Mesh mesh;

        // Unity lays the buffer out in VertexAttribute order (position, normal, tangent, colour, uv): BridgeWallVertex matches it.
        private static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };

        /// <summary>Adds the walls under <paramref name="parent"/> (the tile's root, at the tile's corner); false when there is nothing to draw.</summary>
        public static bool Attach(Transform parent, BridgeWallMeshData d, Material material)
        {
            if (parent == null || d == null || d.VertexCount == 0 || d.IndexCount == 0 || material == null) return false;
            var go = new GameObject("Bridge walls");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            var view = go.AddComponent<BridgeWallsView>();
            view.mesh = new Mesh { name = "Endless bridge walls" };
            const MeshUpdateFlags flags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;
            var bounds = new Bounds();
            bounds.SetMinMax(d.BoundsMin, d.BoundsMax);
            view.mesh.SetVertexBufferParams(d.VertexCount, Layout);
            view.mesh.SetVertexBufferData(d.Vertices, 0, 0, d.VertexCount, 0, flags);
            view.mesh.SetIndexBufferParams(d.IndexCount, IndexFormat.UInt16);
            view.mesh.SetIndexBufferData(d.Indices, 0, 0, d.IndexCount, flags);
            view.mesh.subMeshCount = 1;
            view.mesh.SetSubMesh(0, new SubMeshDescriptor(0, d.IndexCount, MeshTopology.Triangles) { bounds = bounds, firstVertex = 0, vertexCount = d.VertexCount }, flags);
            view.mesh.bounds = bounds;
            view.mesh.UploadMeshData(true);
            go.AddComponent<MeshFilter>().sharedMesh = view.mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            foreach (BridgeWallBox b in d.Boxes)
            {
                if (!(b.Size.x > 0f && b.Size.y > 0f && b.Size.z > 0f)) continue;
                var box = new GameObject("Bridge wall collider");
                box.transform.SetParent(go.transform, false);
                box.transform.localPosition = b.Centre;
                box.transform.localRotation = Quaternion.Euler(b.Pitch, b.Yaw, 0f);
                box.AddComponent<BoxCollider>().size = b.Size;
            }

            return true;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            mesh = null;
        }
    }
}
