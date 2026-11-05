using System.Runtime.InteropServices;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// One vertex of a drawn stream mesh, laid out exactly as the GPU vertex buffer (52 bytes): position (float3), normal (float3),
    /// tangent (float4, along the flow), texture coordinate (float2) and colour (4 × unorm8).
    /// <para>
    /// Water vertices: uv.x across the stream −1 (left edge) … 1 (right edge), uv.y along it in metres /
    /// <see cref="StreamRibbonMeshData.TextureLength"/>; colour r depth toward the middle, g flow speed, b foam, a 1.
    /// Bank vertices: uv.x metres beyond the water's edge (negative under the water; + 100 on the right bank), uv.y as for the
    /// water; colour r mud, g wet, b worn path (also a ford's road bed and a bridge's deck), a drape (1 where the strip lies over the
    /// ground and shows the terrain's plain grass). Bridge deck vertices (from <see cref="StreamRibbonMeshData.DeckVertexStart"/>):
    /// uv.x 200 ± metres across, colour (0, 0, 255, 0).
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StreamVertex
    {
        /// <summary>Tile-local position (x, z from the tile's minimum corner; y absolute).</summary>
        public Vector3 position;

        /// <summary>Unit normal (straight up on the water; from the bank's own shape on the banks).</summary>
        public Vector3 normal;

        /// <summary>Tangent along the flow (w = 1).</summary>
        public Vector4 tangent;

        /// <summary>
        /// Per-vertex data (see the type's remarks). Before <see cref="uv"/>: Unity lays a vertex buffer out in the order of the
        /// <c>VertexAttribute</c> enum (position, normal, tangent, colour, texture coordinates) whatever order the descriptors are
        /// given in. With the uv first, the shaders read the uv's bytes as the colour and the colour's bytes as uv.y: a huge or
        /// infinite number wherever alpha was 255 (all the water, the banks' drape), which is where the NaN glow at streams came
        /// from, and why depth, flow, foam, mud, wet and drape never did what they were written to do (found 2026-10-06 with the
        /// shaders' debug view).
        /// </summary>
        public Color32 color;

        /// <summary>Texture coordinate.</summary>
        public Vector2 uv;
    }

    /// <summary>
    /// The drawn streams of a near tile as one plain mesh, built on the generating thread (pure, no Unity objects; see
    /// <c>TileStreams.Mesh</c>): indices [0, <see cref="WaterIndexCount"/>) are the water (first submesh, transparent), the rest the
    /// banks (second submesh, opaque, receives shadows). The bank triangles are repeated, compacted, for the collider. 16-bit
    /// indices. The main thread only uploads it.
    /// </summary>
    public sealed class StreamRibbonMeshData
    {
        /// <summary>Metres of stream per texture repeat along the flow.</summary>
        public const float TextureLength = 6f;

        /// <summary>
        /// First vertex of the bridges' deck surfaces (after every stream section; <see cref="VertexCount"/> when the tile has no
        /// bridge). Vertices before it come in sections of <c>TileStreams.SectionVertices</c>.
        /// </summary>
        public int DeckVertexStart;

        /// <summary>First index of the decks' triangles (the end of the bank triangles; <see cref="IndexCount"/> without a bridge).</summary>
        public int DeckIndexStart;

        /// <summary>Vertices (exactly <see cref="VertexCount"/> long).</summary>
        public StreamVertex[] Vertices;

        /// <summary>Triangle list indices (exactly <see cref="IndexCount"/> long): water first, then banks.</summary>
        public ushort[] Indices;

        /// <summary>Number of vertices.</summary>
        public int VertexCount;

        /// <summary>Number of indices (3 per triangle).</summary>
        public int IndexCount;

        /// <summary>Number of indices of the water (the first submesh); the banks follow.</summary>
        public int WaterIndexCount;

        /// <summary>Tile-local positions of the bank vertices (for the collider).</summary>
        public Vector3[] ColliderPositions;

        /// <summary>Triangle list into <see cref="ColliderPositions"/>: the bank triangles.</summary>
        public int[] ColliderIndices;

        /// <summary>Tile-local bounds of every vertex.</summary>
        public Vector3 BoundsMin, BoundsMax;

        /// <summary>Total triangles.</summary>
        public int Triangles => IndexCount / 3;

        /// <summary>Number of indices of the banks (the second submesh).</summary>
        public int BankIndexCount => IndexCount - WaterIndexCount;
    }
}
