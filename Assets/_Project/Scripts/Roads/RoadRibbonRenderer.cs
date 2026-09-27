using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace MedievalZen.Roads
{
    /// <summary>
    /// Debug visualisation of a <see cref="RoadNetwork"/>: one flat ribbon mesh following every segment,
    /// lifted slightly above the ground. Intended for flat test ground, not for the final terrain.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    public sealed class RoadRibbonRenderer : MonoBehaviour
    {
        [SerializeField, Min(0.1f), Tooltip("Ribbon width in metres.")]
        private float width = 3.5f;

        [SerializeField, Tooltip("Vertical lift above the road polyline, in metres (avoids z-fighting with flat ground).")]
        private float heightOffset = 0.05f;

        [SerializeField, Min(0.1f), Tooltip("Metres of road per texture repeat along the ribbon.")]
        private float uvMetresPerTile = 4f;

        [SerializeField, Tooltip("On Start, rebuild from GameWorld.Current's roads.")]
        private bool rebuildOnStart = true;

        private Mesh mesh;

        /// <summary>Ribbon width in metres.</summary>
        public float Width
        {
            get => width;
            set => width = Mathf.Max(0.1f, value);
        }

        /// <summary>Rebuilds the ribbon mesh from <paramref name="network"/> (clears it when null).</summary>
        public void Rebuild(RoadNetwork network)
        {
            if (mesh == null)
            {
                mesh = new Mesh { name = "Road Ribbon" };
            }

            mesh.Clear();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            if (network == null) return;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float half = width * 0.5f;

            foreach (RoadSegment segment in network.Segments)
            {
                IReadOnlyList<Vector3> points = segment.Points;
                int count = points.Count;
                if (count < 2) continue;

                int first = vertices.Count;
                float travelled = 0f;
                for (int i = 0; i < count; i++)
                {
                    Vector3 previous = points[Mathf.Max(0, i - 1)];
                    Vector3 next = points[Mathf.Min(count - 1, i + 1)];
                    Vector3 tangent = next - previous;
                    tangent.y = 0f;
                    if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.forward;
                    tangent.Normalize();
                    Vector3 side = new Vector3(tangent.z, 0f, -tangent.x) * half;   // right of travel

                    if (i > 0) travelled += Vector3.Distance(points[i - 1], points[i]);
                    Vector3 centre = points[i] + Vector3.up * heightOffset;
                    float v = travelled / uvMetresPerTile;

                    vertices.Add(transform.InverseTransformPoint(centre - side));
                    vertices.Add(transform.InverseTransformPoint(centre + side));
                    normals.Add(Vector3.up);
                    normals.Add(Vector3.up);
                    uvs.Add(new Vector2(0f, v));
                    uvs.Add(new Vector2(1f, v));

                    if (i > 0)
                    {
                        int a = first + (i - 1) * 2;   // previous left
                        int b = a + 1;                 // previous right
                        int c = a + 2;                 // current left
                        int d = a + 3;                 // current right
                        triangles.Add(a); triangles.Add(c); triangles.Add(b);
                        triangles.Add(b); triangles.Add(c); triangles.Add(d);
                    }
                }
            }

            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void Start()
        {
            if (rebuildOnStart && GameWorld.Current != null)
            {
                Rebuild(GameWorld.Current.Roads);
            }
        }

        private void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
    }
}
