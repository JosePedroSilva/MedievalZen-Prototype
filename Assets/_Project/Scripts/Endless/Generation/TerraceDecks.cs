using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// A deck strip of a walled city's terraces (<see cref="CityHill.Decks"/>, in a dressing window's frame): per sample its outer edge (on
    /// a retaining wall's top) and inner edge (on firm ground), both at the deck's top height.
    /// </summary>
    public sealed class TerraceDeck
    {
        public bool Ramp;
        public Vector3[] Outer, Inner;
        /// <summary>The parapet's lip per segment (fractions <see cref="LipA"/>..<see cref="LipB"/> of segment k to k + 1; none where B &lt;= A), and across the last edge (<see cref="EndLip"/>): <see cref="CityHill.DeckPlan"/>.</summary>
        public float[] LipA, LipB;
        public bool EndLip;

        /// <summary>Whether segment <paramref name="k"/> carries a lip, and its fractions.</summary>
        public bool Lip(int k, out float a, out float b)
        {
            a = b = 0f;
            if (LipA == null || k < 0 || k >= LipA.Length) return false;
            a = LipA[k];
            b = LipB[k];
            return b - a > 1e-3f;
        }
    }

    /// <summary>A near tile's terrace decks: the drawn mesh (tile-local), box colliders, and a height lookup for the ground queries.</summary>
    public sealed class TerraceDeckMeshData
    {
        public Vector3[] Positions, Normals;
        public Vector2[] Uv;
        public int[] Indices;
        public Vector3 BoundsMin, BoundsMax;
        /// <summary>Thin boxes under the deck's top, one per run of up to <see cref="TerraceDecks.BoxSamples"/> samples (centre, size, yaw, pitch).</summary>
        public readonly List<BridgeWallBox> Boxes = new List<BridgeWallBox>();
        /// <summary>The deck's top on a <see cref="TerraceDecks.GridCell"/> m grid over the tile (NaN where no deck), or null.</summary>
        public float[] Grid;
        public int GridSide;
        public int VertexCount => Positions?.Length ?? 0;
        public int Triangles => (Indices?.Length ?? 0) / 3;

        /// <summary>The deck's top at tile-local (x, z), or false where no deck covers it.</summary>
        public bool TryHeight(float x, float z, out float height)
        {
            height = 0f;
            if (Grid == null) return false;
            int i = (int)Math.Floor(x / TerraceDecks.GridCell), j = (int)Math.Floor(z / TerraceDecks.GridCell);
            if (i < 0 || j < 0 || i >= GridSide || j >= GridSide) return false;
            float h = Grid[j * GridSide + i];
            if (float.IsNaN(h)) return false;
            height = h;
            return true;
        }
    }

    /// <summary>
    /// The decks over the walled city's terrace banks (2026-10-09): each strip is drawn as a cobbled top (planar world UVs in metres, so the
    /// material's tiling matches the terrain's cobblestone layer) with a 0.35 m skirt down its inner edge and its ends, and gets a thin box
    /// collider per few samples. A near tile takes the segments whose middle lies in it. Pure (no Unity calls beyond the value types).
    /// </summary>
    public static class TerraceDecks
    {
        /// <summary>Depth of the skirt under the deck's free edges, metres (hides the terrain's lag at its inner edge).</summary>
        public const float Skirt = 0.35f;
        /// <summary>Thickness of a deck's collider box, metres.</summary>
        public const float BoxThickness = 0.4f;
        /// <summary>Samples (about a metre apart) per collider box.</summary>
        public const int BoxSamples = 4;
        /// <summary>Cell of the height lookup, metres.</summary>
        public const float GridCell = 0.5f;
        /// <summary>
        /// The lip's collider reaches this far above the deck (the drawn lip is <see cref="CityHill.LipHeight"/>): taller than the
        /// traveller's step offset (0.3 m) by a margin, so the edge stops him, and low enough to lean on.
        /// </summary>
        public const float LipColliderHeight = 1.0f;
        /// <summary>The lip's outer face reaches this far below the deck's top (over the wall's top and the chords' sag).</summary>
        public const float LipDrop = 0.2f;

        /// <summary>Triangles a strip draws (top, skirts and its lip), for the city's count.</summary>
        public static long TrianglesOf(TerraceDeck d)
        {
            if (d == null || d.Outer.Length < 2) return 0;
            long n = (d.Outer.Length - 1) * 6L + 4L;
            for (int k = 0; k + 1 < d.Outer.Length; k++) if (d.Lip(k, out _, out _)) n += 6 + 4;   // top, two faces; a cap at most per end
            if (d.EndLip) n += 10;
            return n;
        }

        /// <summary>The decks of <paramref name="dressings"/> whose segments' middles lie in the tile at (tx, tz), tile-local; null when none.</summary>
        public static TerraceDeckMeshData Build(List<Dressing> dressings, double tx, double tz, float tile)
        {
            if (dressings == null || dressings.Count == 0) return null;
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var uv = new List<Vector2>();
            var idx = new List<int>();
            var data = new TerraceDeckMeshData();
            var segments = new List<(Vector3 o0, Vector3 i0, Vector3 o1, Vector3 i1)>();
            var lips = new List<(Vector3 a, Vector3 da, Vector3 b, Vector3 db)>();
            foreach (Dressing d in dressings)
            {
                if (d?.City == null) continue;
                float ox = (float)(d.X0 - tx), oz = (float)(d.Z0 - tz);
                foreach (TerraceDeck deck in d.City.Decks)
                {
                    int n = deck.Outer.Length;
                    int runStart = -1;
                    for (int k = 0; k + 1 < n; k++)
                    {
                        Vector3 o0 = Shift(deck.Outer[k], ox, oz), i0 = Shift(deck.Inner[k], ox, oz), o1 = Shift(deck.Outer[k + 1], ox, oz), i1 = Shift(deck.Inner[k + 1], ox, oz);
                        Vector3 m = (o0 + i0 + o1 + i1) * 0.25f;
                        bool mine = m.x >= 0f && m.x < tile && m.z >= 0f && m.z < tile;
                        if (!mine)
                        {
                            // Drawn and given colliders by the tile holding its middle; its height still reaches this tile's lookup
                            // wherever it overlaps (a segment across a seam: the ground queries find the deck on both sides).
                            if (Math.Max(Math.Max(o0.x, o1.x), Math.Max(i0.x, i1.x)) >= 0f && Math.Min(Math.Min(o0.x, o1.x), Math.Min(i0.x, i1.x)) < tile &&
                                Math.Max(Math.Max(o0.z, o1.z), Math.Max(i0.z, i1.z)) >= 0f && Math.Min(Math.Min(o0.z, o1.z), Math.Min(i0.z, i1.z)) < tile)
                                Raster(data, tile, o0, o1, i1, i0);
                            Boxes(data, segments);
                            LipBoxes(data, lips);
                            runStart = -1;
                            continue;
                        }

                        bool first = k == 0, last = k + 2 == n;
                        Quad(pos, nrm, uv, idx, o0, o1, i1, i0);
                        // Skirts: the inner edge, the outer edge (inside the wall's top), and the strip's two ends.
                        SkirtEdge(pos, nrm, uv, idx, i0, i1, m);
                        SkirtEdge(pos, nrm, uv, idx, o1, o0, m);
                        if (first) SkirtEdge(pos, nrm, uv, idx, o0, i0, m);
                        if (last) SkirtEdge(pos, nrm, uv, idx, i1, o1, m);
                        Raster(data, tile, o0, o1, i1, i0);
                        if (runStart < 0) runStart = k;
                        segments.Add((o0, i0, o1, i1));
                        if (segments.Count >= BoxSamples) Boxes(data, segments);
                        if (deck.Lip(k, out float la, out float lb))
                        {
                            bool startCap = la > 1e-3f || !deck.Lip(k - 1, out _, out float pb) || pb < 0.999f;
                            bool endCap = lb < 0.999f || !deck.Lip(k + 1, out float na, out _) || na > 1e-3f;
                            Vector3 d0 = Outward(o0, i0), d1 = Outward(o1, i1);
                            Vector3 da = Vector3.Lerp(d0, d1, la).normalized, db = Vector3.Lerp(d0, d1, lb).normalized;
                            Vector3 pa = Vector3.Lerp(o0, o1, la), pb2 = Vector3.Lerp(o0, o1, lb);
                            LipSegment(pos, nrm, uv, idx, pa, da, pb2, db, startCap, endCap);
                            lips.Add((pa, da, pb2, db));
                            if (endCap || lips.Count >= BoxSamples) LipBoxes(data, lips);
                        }
                        else LipBoxes(data, lips);

                        if (last && deck.EndLip)
                        {
                            // Across the strip's end (a ramp's end wall): outward along the strip.
                            Vector3 along = (o1 + i1) * 0.5f - (o0 + i0) * 0.5f;
                            along.y = 0f;
                            if (along.sqrMagnitude > 1e-8f)
                            {
                                along.Normalize();
                                LipBoxes(data, lips);
                                LipSegment(pos, nrm, uv, idx, o1, along, i1, along, true, true);
                                lips.Add((o1, along, i1, along));
                                LipBoxes(data, lips);
                            }
                        }
                    }

                    Boxes(data, segments);
                    LipBoxes(data, lips);
                }
            }

            if (pos.Count == 0 && data.Grid == null) return null;
            data.Positions = pos.ToArray();
            data.Normals = nrm.ToArray();
            data.Uv = uv.ToArray();
            data.Indices = idx.ToArray();
            if (pos.Count == 0) return data;   // only the height lookup: the decks' segments are drawn by the neighbouring tiles
            Vector3 mn = pos[0], mx = pos[0];
            foreach (Vector3 p in pos) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            data.BoundsMin = mn;
            data.BoundsMax = mx;
            return data;
        }

        private static Vector3 Shift(Vector3 p, float ox, float oz) => new Vector3(p.x + ox, p.y, p.z + oz);

        /// <summary>Unit horizontal direction from a deck's inner edge to its outer edge (over the drop).</summary>
        private static Vector3 Outward(Vector3 outer, Vector3 inner)
        {
            var d = new Vector3(outer.x - inner.x, 0f, outer.z - inner.z);
            float l = d.magnitude;
            return l > 1e-6f ? d / l : new Vector3(0f, 0f, 1f);
        }

        /// <summary>
        /// One piece of the parapet's lip along a deck's outer edge from <paramref name="a"/> to <paramref name="b"/> (on the deck's top,
        /// <paramref name="da"/>/<paramref name="db"/> pointing over the drop): its outer face flush with the wall's low face
        /// (<see cref="CityHill.DeckInset"/> out from the edge) from <see cref="LipDrop"/> below the deck to <see cref="CityHill.LipHeight"/>
        /// above it, its top <see cref="CityHill.LipThick"/> deep, its inner face down to the deck, end caps where asked.
        /// </summary>
        private static void LipSegment(List<Vector3> pos, List<Vector3> nrm, List<Vector2> uv, List<int> idx, Vector3 a, Vector3 da, Vector3 b, Vector3 db, bool capA, bool capB)
        {
            float outer = CityHill.DeckInset, inner = CityHill.LipThick - CityHill.DeckInset;
            Vector3 up = new Vector3(0f, CityHill.LipHeight, 0f), down = new Vector3(0f, LipDrop, 0f);
            Vector3 ao = a + da * outer, bo = b + db * outer, ai = a - da * inner, bi = b - db * inner;
            Vector3 centre = (a + b) * 0.5f - (da + db) * (0.25f * CityHill.LipThick - 0.5f * outer) + up * 0.5f;
            Quad(pos, nrm, uv, idx, ao + up, bo + up, bi + up, ai + up);
            Vertical(pos, nrm, uv, idx, ao + up, bo + up, ao - down, bo - down, centre);
            Vertical(pos, nrm, uv, idx, ai + up, bi + up, ai, bi, centre);
            if (capA) Vertical(pos, nrm, uv, idx, ao + up, ai + up, ao - down, ai, centre);
            if (capB) Vertical(pos, nrm, uv, idx, bo + up, bi + up, bo - down, bi, centre);
        }

        /// <summary>A vertical quad from top edge a-b down to a1-b1, facing away from <paramref name="centre"/>; UVs in metres (along the world, height).</summary>
        private static void Vertical(List<Vector3> pos, List<Vector3> nrm, List<Vector2> uv, List<int> idx, Vector3 a, Vector3 b, Vector3 a1, Vector3 b1, Vector3 centre)
        {
            Vector3 e = b - a;
            e.y = 0f;
            float len = e.magnitude;
            if (len < 1e-5f) return;
            var n = new Vector3(e.z, 0f, -e.x) / len;
            Vector3 mid = (a + b) * 0.5f;
            if ((mid.x - centre.x) * n.x + (mid.z - centre.z) * n.z < 0f) n = -n;
            int s = pos.Count;
            // u along the world (x for a face turned along z, z for one turned along x), so neighbouring pieces' textures meet; v the height.
            float u0 = a.x * Math.Abs(n.z) + a.z * Math.Abs(n.x), u1 = b.x * Math.Abs(n.z) + b.z * Math.Abs(n.x);
            pos.Add(a); pos.Add(b); pos.Add(b1); pos.Add(a1);
            for (int k = 0; k < 4; k++) nrm.Add(n);
            uv.Add(new Vector2(u0, a.y)); uv.Add(new Vector2(u1, b.y)); uv.Add(new Vector2(u1, b1.y)); uv.Add(new Vector2(u0, a1.y));
            if (Vector3.Dot(Vector3.Cross(b - a, b1 - a), n) > 0f) { idx.Add(s); idx.Add(s + 1); idx.Add(s + 2); idx.Add(s); idx.Add(s + 2); idx.Add(s + 3); }
            else { idx.Add(s); idx.Add(s + 2); idx.Add(s + 1); idx.Add(s); idx.Add(s + 3); idx.Add(s + 2); }
        }

        /// <summary>One box for the collected lip pieces: the lip's thickness across, from a little under the deck to <see cref="LipColliderHeight"/> above it.</summary>
        private static void LipBoxes(TerraceDeckMeshData data, List<(Vector3 a, Vector3 da, Vector3 b, Vector3 db)> lips)
        {
            if (lips.Count == 0) return;
            float mid = 0.5f * CityHill.LipThick - CityHill.DeckInset;   // the lip's centre line, inside the outer edge
            Vector3 m0 = lips[0].a - lips[0].da * mid, m1 = lips[lips.Count - 1].b - lips[lips.Count - 1].db * mid;
            Vector3 along = m1 - m0;
            float lenFlat = new Vector2(along.x, along.z).magnitude;
            if (lenFlat < 0.05f) { lips.Clear(); return; }
            float sag = 0f;
            foreach (var l in lips) sag = Math.Max(sag, Math.Max(DistanceToLine(l.a - l.da * mid, m0, m1), DistanceToLine(l.b - l.db * mid, m0, m1)));
            float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(along.y, lenFlat) * Mathf.Rad2Deg;
            double ya = yaw * Math.PI / 180.0, pb = pitch * Math.PI / 180.0;
            var up = new Vector3((float)(Math.Sin(pb) * Math.Sin(ya)), (float)Math.Cos(pb), (float)(Math.Sin(pb) * Math.Cos(ya)));
            const float bottom = -0.1f;
            float height = LipColliderHeight - bottom;
            Vector3 centre = (m0 + m1) * 0.5f + up * (bottom + 0.5f * height);
            data.Boxes.Add(new BridgeWallBox { Centre = centre, Size = new Vector3(CityHill.LipThick + 2f * sag, height, along.magnitude + 0.1f), Yaw = yaw, Pitch = pitch });
            lips.Clear();
        }

        /// <summary>A top quad a-b-c-d (counter-clockwise seen from above is not assumed: the face is turned to point up).</summary>
        private static void Quad(List<Vector3> pos, List<Vector3> nrm, List<Vector2> uv, List<int> idx, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 n = Vector3.Cross(c - a, b - d);
            if (n.y < 0f) { (b, d) = (d, b); n = -n; }
            float l = n.magnitude;
            n = l > 1e-8f ? n / l : Vector3.up;
            int s = pos.Count;
            foreach (Vector3 p in new[] { a, b, c, d })
            {
                pos.Add(p);
                nrm.Add(n);
                uv.Add(new Vector2(p.x, p.z));
            }

            // Unity's front faces wind clockwise seen from the normal's side.
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) > 0f) { idx.Add(s); idx.Add(s + 1); idx.Add(s + 2); idx.Add(s); idx.Add(s + 2); idx.Add(s + 3); }
            else { idx.Add(s); idx.Add(s + 2); idx.Add(s + 1); idx.Add(s); idx.Add(s + 3); idx.Add(s + 2); }
        }

        /// <summary>A vertical skirt under edge a-b of a quad whose middle is <paramref name="centre"/>, facing away from it.</summary>
        private static void SkirtEdge(List<Vector3> pos, List<Vector3> nrm, List<Vector2> uv, List<int> idx, Vector3 a, Vector3 b, Vector3 centre)
        {
            Vector3 e = b - a;
            e.y = 0f;
            if (e.sqrMagnitude < 1e-8f) return;
            var n = new Vector3(e.z, 0f, -e.x).normalized;
            Vector3 mid = (a + b) * 0.5f;
            if ((mid.x - centre.x) * n.x + (mid.z - centre.z) * n.z < 0f) n = -n;
            Vector3 a1 = a - new Vector3(0f, Skirt, 0f), b1 = b - new Vector3(0f, Skirt, 0f);
            int s = pos.Count;
            float len = e.magnitude;
            pos.Add(a); pos.Add(b); pos.Add(b1); pos.Add(a1);
            for (int k = 0; k < 4; k++) nrm.Add(n);
            uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(len, 0f)); uv.Add(new Vector2(len, -Skirt)); uv.Add(new Vector2(0f, -Skirt));
            if (Vector3.Dot(Vector3.Cross(b - a, b1 - a), n) > 0f) { idx.Add(s); idx.Add(s + 1); idx.Add(s + 2); idx.Add(s); idx.Add(s + 2); idx.Add(s + 3); }
            else { idx.Add(s); idx.Add(s + 2); idx.Add(s + 1); idx.Add(s); idx.Add(s + 3); idx.Add(s + 2); }
        }

        /// <summary>One box for the collected segments: across their mean width, along from the first to the last, its top on the deck.</summary>
        private static void Boxes(TerraceDeckMeshData data, List<(Vector3 o0, Vector3 i0, Vector3 o1, Vector3 i1)> segs)
        {
            if (segs.Count == 0) return;
            var (o0, i0, _, _) = segs[0];
            var (_, _, o1, i1) = segs[segs.Count - 1];
            Vector3 m0 = (o0 + i0) * 0.5f, m1 = (o1 + i1) * 0.5f;
            Vector3 along = m1 - m0;
            float lenFlat = new Vector2(along.x, along.z).magnitude;
            if (lenFlat < 0.05f) { segs.Clear(); return; }
            float width = 0f;
            foreach (var s in segs) width = Math.Max(width, Math.Max(Flat(s.o0, s.i0), Flat(s.o1, s.i1)));
            // The chord of a curved run: grow the box by the samples' sag so it covers the strip.
            float sag = 0f;
            foreach (var s in segs) sag = Math.Max(sag, DistanceToLine((s.o0 + s.i0) * 0.5f, m0, m1));
            float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(along.y, lenFlat) * Mathf.Rad2Deg;
            float len = along.magnitude;
            // The box's up axis: Ry(yaw) Rx(pitch) (0, 1, 0), written out (no native Unity maths in generation).
            double ya = yaw * Math.PI / 180.0, pb = pitch * Math.PI / 180.0;
            var up = new Vector3((float)(Math.Sin(pb) * Math.Sin(ya)), (float)Math.Cos(pb), (float)(Math.Sin(pb) * Math.Cos(ya)));
            Vector3 centre = (m0 + m1) * 0.5f - up * (0.5f * BoxThickness);
            data.Boxes.Add(new BridgeWallBox { Centre = centre, Size = new Vector3(width + 2f * sag, BoxThickness, len + 0.1f), Yaw = yaw, Pitch = pitch });
            segs.Clear();
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static float DistanceToLine(Vector3 p, Vector3 a, Vector3 b)
        {
            float ex = b.x - a.x, ez = b.z - a.z, l = (float)Math.Sqrt(ex * ex + ez * ez);
            return l < 1e-6f ? Flat(p, a) : Math.Abs((p.x - a.x) * ez - (p.z - a.z) * ex) / l;
        }

        /// <summary>Writes the quad's height into the lookup grid (cells whose centre lies inside it).</summary>
        private static void Raster(TerraceDeckMeshData data, float tile, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (data.Grid == null)
            {
                data.GridSide = (int)Math.Ceiling(tile / GridCell);
                data.Grid = new float[data.GridSide * data.GridSide];
                for (int k = 0; k < data.Grid.Length; k++) data.Grid[k] = float.NaN;
            }

            Tri(data, a, b, c);
            Tri(data, a, c, d);
        }

        private static void Tri(TerraceDeckMeshData data, Vector3 a, Vector3 b, Vector3 c)
        {
            float x0 = Math.Min(a.x, Math.Min(b.x, c.x)), x1 = Math.Max(a.x, Math.Max(b.x, c.x));
            float z0 = Math.Min(a.z, Math.Min(b.z, c.z)), z1 = Math.Max(a.z, Math.Max(b.z, c.z));
            int i0 = Math.Max(0, (int)Math.Floor(x0 / GridCell)), i1 = Math.Min(data.GridSide - 1, (int)Math.Floor(x1 / GridCell));
            int j0 = Math.Max(0, (int)Math.Floor(z0 / GridCell)), j1 = Math.Min(data.GridSide - 1, (int)Math.Floor(z1 / GridCell));
            float den = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Math.Abs(den) < 1e-8f) return;
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    float px = (i + 0.5f) * GridCell, pz = (j + 0.5f) * GridCell;
                    float wa = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / den;
                    float wb = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / den;
                    float wc = 1f - wa - wb;
                    if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;
                    data.Grid[j * data.GridSide + i] = wa * a.y + wb * b.y + wc * c.y;
                }
            }
        }
    }
}
