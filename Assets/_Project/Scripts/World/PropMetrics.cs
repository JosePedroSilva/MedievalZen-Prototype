using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Measured shape of one prefab in its own (root) space, in metres at scale 1: mesh bounds, the direction its main face
    /// points, and the underside (lowest mesh point) over a 5 × 5 grid of footprint cells. Measured from the meshes by the
    /// palette menu (<see cref="FromTriangles"/>); <see cref="PropMetricsTable"/> holds the same numbers measured offline
    /// from the Synty model files, so pure generation and tests work without the editor.
    /// </summary>
    [Serializable]
    public struct PropMetrics
    {
        /// <summary>Cells per side of <see cref="underside"/>.</summary>
        public const int Grid = 5;

        /// <summary>Value of an <see cref="underside"/> cell that no mesh covers.</summary>
        public const float Empty = 1e6f;

        [Tooltip("Mesh bounds minimum in prefab space (m).")]
        public Vector3 min;

        [Tooltip("Mesh bounds maximum in prefab space (m).")]
        public Vector3 max;

        [Tooltip("Yaw (degrees, prefab space) of the main face: 0 = local +Z, 90 = local +X. For blocks it is the short horizontal axis.")]
        public float faceYaw;

        [Tooltip("Lowest mesh point (y, m) in each of 5 x 5 footprint cells, row-major from (min.x, min.z); 1e6 = no mesh over the cell.")]
        public float[] underside;

        /// <summary>Creates metrics from bounds, face yaw and an underside grid (<see cref="Grid"/>² values, <see cref="Empty"/> for none).</summary>
        public PropMetrics(Vector3 min, Vector3 max, float faceYaw, float[] underside)
        {
            this.min = min;
            this.max = max;
            this.faceYaw = faceYaw;
            this.underside = underside;
        }

        /// <summary>True when the metrics describe a real mesh.</summary>
        public bool IsValid => max.y > min.y && max.x > min.x && max.z > min.z && underside != null && underside.Length == Grid * Grid;

        /// <summary>Bounds size (m).</summary>
        public Vector3 Size => max - min;

        /// <summary>Height of the mesh (m at scale 1).</summary>
        public float Height => max.y - min.y;

        /// <summary>Longer horizontal extent (m at scale 1).</summary>
        public float Length => Mathf.Max(max.x - min.x, max.z - min.z);

        /// <summary>Half the diagonal of the footprint (m at scale 1): radius of a circle that covers it around the bounds centre.</summary>
        public float FootprintRadius => 0.5f * Mathf.Sqrt((max.x - min.x) * (max.x - min.x) + (max.z - min.z) * (max.z - min.z));

        /// <summary>Local x of the centre of footprint column <paramref name="i"/>.</summary>
        public float CellX(int i) => min.x + (i + 0.5f) * (max.x - min.x) / Grid;

        /// <summary>Local z of the centre of footprint row <paramref name="j"/>.</summary>
        public float CellZ(int j) => min.z + (j + 0.5f) * (max.z - min.z) / Grid;

        /// <summary>
        /// True when cell (i, j) holds mesh and lies on the edge of the footprint (grid border or next to an empty cell): where
        /// the piece meets the ground as seen from outside. Inner cells of open-bottomed shells (slabs, mounds) show only their
        /// top surface as their lowest point and are not contact cells.
        /// </summary>
        public bool IsPerimeter(int i, int j)
        {
            if (Underside(i, j) >= Empty) return false;
            if (i == 0 || j == 0 || i == Grid - 1 || j == Grid - 1) return true;
            return Underside(i - 1, j) >= Empty || Underside(i + 1, j) >= Empty || Underside(i, j - 1) >= Empty || Underside(i, j + 1) >= Empty;
        }

        /// <summary>
        /// True for a <see cref="IsPerimeter"/> cell whose lowest point is within the bottom fifth of the mesh: where the piece
        /// stands on the ground. Edge cells whose lowest point is higher are overhangs modelled into the mesh (a stone jutting out
        /// of a cairn), not contact.
        /// </summary>
        public bool IsContact(int i, int j) => IsPerimeter(i, j) && Underside(i, j) <= min.y + 0.2f * (max.y - min.y);

        /// <summary>Underside of cell (i, j), or <see cref="Empty"/>.</summary>
        public float Underside(int i, int j) => underside[j * Grid + i];

        /// <summary>
        /// Measures a mesh given in prefab space (Unity winding: front face normal = cross(b − a, c − a)). The face yaw is the
        /// short horizontal axis of the bounds (sign from the area-weighted horizontal normal of the steep faces), or, for
        /// footprints within 15 % of square, the direction of that normal. The underside is the lowest point of the mesh
        /// surface over each footprint cell (triangles rasterised on a 60 × 60 grid).
        /// </summary>
        public static PropMetrics FromTriangles(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> triangles)
        {
            if (vertices == null || vertices.Count == 0 || triangles == null || triangles.Count < 3) return default;
            var lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < vertices.Count; i++)
            {
                lo = Vector3.Min(lo, vertices[i]);
                hi = Vector3.Max(hi, vertices[i]);
            }

            const int Fine = 60;
            var fine = new float[Fine * Fine];
            for (int i = 0; i < fine.Length; i++) fine[i] = Empty;
            float sx = Mathf.Max(1e-4f, hi.x - lo.x) / Fine;
            float sz = Mathf.Max(1e-4f, hi.z - lo.z) / Fine;
            double fx = 0.0, fz = 0.0;
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                Vector3 a = vertices[triangles[t]];
                Vector3 b = vertices[triangles[t + 1]];
                Vector3 c = vertices[triangles[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                float area2 = n.magnitude;
                if (area2 > 1e-10f && Mathf.Abs(n.y) / area2 < 0.6f)
                {
                    fx += n.x;
                    fz += n.z;
                }

                // Rasterise the triangle's footprint, keeping the lowest surface height per fine cell.
                float au = (a.x - lo.x) / sx, av = (a.z - lo.z) / sz;
                float bu = (b.x - lo.x) / sx, bv = (b.z - lo.z) / sz;
                float cu = (c.x - lo.x) / sx, cv = (c.z - lo.z) / sz;
                Lower(fine, Fine, au, av, a.y);
                Lower(fine, Fine, bu, bv, b.y);
                Lower(fine, Fine, cu, cv, c.y);
                float den = (bv - cv) * (au - cu) + (cu - bu) * (av - cv);
                if (Mathf.Abs(den) < 1e-9f) continue;
                int i0 = Math.Max(0, (int)Math.Floor(Math.Min(au, Math.Min(bu, cu))));
                int i1 = Math.Min(Fine - 1, (int)Math.Floor(Math.Max(au, Math.Max(bu, cu))));
                int j0 = Math.Max(0, (int)Math.Floor(Math.Min(av, Math.Min(bv, cv))));
                int j1 = Math.Min(Fine - 1, (int)Math.Floor(Math.Max(av, Math.Max(bv, cv))));
                for (int j = j0; j <= j1; j++)
                {
                    float pv = j + 0.5f;
                    for (int i = i0; i <= i1; i++)
                    {
                        float pu = i + 0.5f;
                        float w0 = ((bv - cv) * (pu - cu) + (cu - bu) * (pv - cv)) / den;
                        float w1 = ((cv - av) * (pu - cu) + (au - cu) * (pv - cv)) / den;
                        float w2 = 1f - w0 - w1;
                        if (w0 < -1e-5f || w1 < -1e-5f || w2 < -1e-5f) continue;
                        float y = w0 * a.y + w1 * b.y + w2 * c.y;
                        int k = j * Fine + i;
                        if (y < fine[k]) fine[k] = y;
                    }
                }
            }

            var grid = new float[Grid * Grid];
            for (int gz = 0; gz < Grid; gz++)
            {
                for (int gx = 0; gx < Grid; gx++)
                {
                    float m = Empty;
                    for (int j = gz * Fine / Grid; j < (gz + 1) * Fine / Grid; j++)
                    {
                        for (int i = gx * Fine / Grid; i < (gx + 1) * Fine / Grid; i++) m = Mathf.Min(m, fine[j * Fine + i]);
                    }

                    grid[gz * Grid + gx] = m;
                }
            }

            float sizeX = hi.x - lo.x;
            float sizeZ = hi.z - lo.z;
            float face;
            if (sizeX >= sizeZ * 1.15f) face = fz >= 0.0 ? 0f : 180f;
            else if (sizeZ >= sizeX * 1.15f) face = fx >= 0.0 ? 90f : -90f;
            else face = Mathf.Round((float)(Math.Atan2(fx, fz) * 180.0 / Math.PI));
            return new PropMetrics(lo, hi, face, grid);
        }

        private static void Lower(float[] fine, int n, float u, float v, float y)
        {
            int i = Math.Min(n - 1, Math.Max(0, (int)u));
            int j = Math.Min(n - 1, Math.Max(0, (int)v));
            if (y < fine[j * n + i]) fine[j * n + i] = y;
        }

        /// <summary>Largest difference (m) between two metrics' bounds, face ignored (for editor-vs-offline logs).</summary>
        public static float BoundsDifference(PropMetrics a, PropMetrics b)
        {
            Vector3 d0 = a.min - b.min;
            Vector3 d1 = a.max - b.max;
            return Mathf.Max(Mathf.Max(Mathf.Abs(d0.x), Mathf.Abs(d0.y), Mathf.Abs(d0.z)), Mathf.Max(Mathf.Abs(d1.x), Mathf.Abs(d1.y), Mathf.Abs(d1.z)));
        }

        /// <inheritdoc />
        public override string ToString() =>
            $"size {max.x - min.x:0.00} x {max.y - min.y:0.00} x {max.z - min.z:0.00} m, base {min.y:0.00}, top {max.y:0.00}, centre ({(min.x + max.x) * 0.5f:0.00}, {(min.z + max.z) * 0.5f:0.00}), face {faceYaw:0}°";
    }
}
