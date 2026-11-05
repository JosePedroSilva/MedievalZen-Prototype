using MedievalZen.Endless.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedievalZen.Endless
{
    /// <summary>
    /// A near tile's terrace decks (2026-10-09, the hill town): the cobbled strips over a walled city's terrace banks, prepared on the
    /// generating thread (<see cref="TerraceDeckMeshData"/>) and uploaded in one go under the tile's root with the deck material
    /// (<c>WorldPalette.terraceDeckMaterial</c>: the terrain's cobblestone layer, its tiling in metres), with a thin box collider per few
    /// metres of deck the traveller walks on. The mesh is destroyed with the tile's root (<see cref="OnDestroy"/>).
    /// </summary>
    public sealed class TerraceDeckView : MonoBehaviour
    {
        private Mesh mesh;

        /// <summary>Adds the decks under <paramref name="parent"/> (the tile's root, at the tile's corner); false when there is nothing to draw.</summary>
        public static bool Attach(Transform parent, TerraceDeckMeshData d, Material material)
        {
            if (parent == null || d == null || d.VertexCount == 0 || d.Indices == null || d.Indices.Length == 0 || material == null) return false;
            var go = new GameObject("Terrace decks");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            var view = go.AddComponent<TerraceDeckView>();
            view.mesh = new Mesh { name = "Endless terrace decks", indexFormat = d.VertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            view.mesh.SetVertices(d.Positions);
            view.mesh.SetNormals(d.Normals);
            view.mesh.SetUVs(0, d.Uv);
            view.mesh.SetTriangles(d.Indices, 0, false);
            var bounds = new Bounds();
            bounds.SetMinMax(d.BoundsMin, d.BoundsMax);
            view.mesh.bounds = bounds;
            view.mesh.RecalculateTangents();
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
                var box = new GameObject("Terrace deck collider");
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
