using System;
using System.Collections.Generic;
using MedievalZen.Roads;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Tests
{
    /// <summary>
    /// 64-bit FNV-1a hashes of every part of a finite-world blueprint (exact float bit patterns), so a refactor of the shared
    /// stages can prove the finite world comes out bit-identical.
    /// </summary>
    public static class FiniteWorldHash
    {
        public static readonly string[] Parts = { "heights", "splat", "details", "trees", "props", "settlements", "roads", "landmarks" };

        public static ulong[] Compute(WorldBlueprint bp)
        {
            var result = new ulong[Parts.Length];
            var h = new Hasher();
            float[,] map = bp.Heightmap;
            for (int z = 0; z < map.GetLength(0); z++)
                for (int x = 0; x < map.GetLength(1); x++) h.Add(map[z, x]);
            result[0] = h.Value;

            h = new Hasher();
            float[,,] s = bp.Splat;
            for (int z = 0; z < s.GetLength(0); z++)
                for (int x = 0; x < s.GetLength(1); x++)
                    for (int l = 0; l < s.GetLength(2); l++) h.Add(s[z, x, l]);
            result[1] = h.Value;

            h = new Hasher();
            h.Add(bp.DetailResolution);
            foreach (DetailLayer layer in bp.DetailLayers)
            {
                h.Add(layer.family);
                h.Add(layer.variant);
                foreach (byte b in layer.density) h.AddByte(b);
            }

            result[2] = h.Value;

            h = new Hasher();
            foreach (TreePlacement t in bp.Trees)
            {
                h.Add(t.role);
                h.Add(t.variant);
                h.Add(t.position);
                h.Add(t.yaw);
                h.Add(t.scale);
            }

            result[3] = h.Value;

            h = new Hasher();
            foreach (PropPlacement p in bp.Props)
            {
                h.Add(p.role);
                h.Add(p.variant);
                h.Add(p.position);
                h.Add(p.yaw);
                h.Add(p.scale);
                h.Add(p.settlementId);
                h.Add(p.pitch);
                h.Add(p.roll);
            }

            result[4] = h.Value;

            h = new Hasher();
            foreach (SettlementSite site in bp.Settlements)
            {
                h.Add(site.Name);
                h.Add((int)site.Type);
                h.Add(site.Position);
                h.Add(site.Radius);
            }

            result[5] = h.Value;

            h = new Hasher();
            foreach (RoadSegment segment in bp.Roads.Segments)
            {
                h.Add(segment.Id);
                h.Add(segment.A.Id);
                h.Add(segment.B.Id);
                foreach (Vector3 point in segment.Points) h.Add(point);
            }

            foreach (RoadNode node in bp.Roads.Nodes)
            {
                h.Add(node.Id);
                h.Add(node.Position);
            }

            result[6] = h.Value;

            h = new Hasher();
            foreach (Landmark landmark in bp.Landmarks)
            {
                h.Add(landmark.name);
                h.Add((int)landmark.kind);
                h.Add(landmark.position);
            }

            h.Add(bp.WaterHeight);
            result[7] = h.Value;
            return result;
        }

        private sealed class Hasher
        {
            public ulong Value = 14695981039346656037UL;

            public void AddByte(byte b)
            {
                Value ^= b;
                Value *= 1099511628211UL;
            }

            public void Add(int v)
            {
                unchecked
                {
                    AddByte((byte)v);
                    AddByte((byte)(v >> 8));
                    AddByte((byte)(v >> 16));
                    AddByte((byte)(v >> 24));
                }
            }

            public void Add(float v) => Add(BitConverter.SingleToInt32Bits(v));

            public void Add(Vector3 v)
            {
                Add(v.x);
                Add(v.y);
                Add(v.z);
            }

            public void Add(string s)
            {
                if (s == null)
                {
                    Add(-1);
                    return;
                }

                Add(s.Length);
                foreach (char c in s) Add((int)c);
            }
        }
    }
}
