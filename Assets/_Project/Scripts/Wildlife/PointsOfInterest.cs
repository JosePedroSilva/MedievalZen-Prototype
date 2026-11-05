using System;
using UnityEngine;

namespace MedievalZen.Wildlife
{
    /// <summary>
    /// A small registry of things worth looking at (an animal event, a large herd), published by the wildlife and read by
    /// the cinematic camera when it chooses a subject. Scene positions; shifted with the floating origin. Fixed capacity, no
    /// allocation after construction. Pure (only Vector3 arithmetic).
    /// </summary>
    public sealed class PointsOfInterest
    {
        /// <summary>Closest a point of interest is framed from, metres.</summary>
        public const float MinDistance = 25f;

        /// <summary>Farthest a point of interest is framed from, metres.</summary>
        public const float MaxDistance = 250f;

        private struct Entry
        {
            public long Id;
            public Vector3 Position;
            public float Radius;
            public float Weight;
            public float Expires;
            public bool Used;
        }

        private readonly Entry[] entries;
        private float now;

        /// <summary>A registry with room for <paramref name="capacity"/> points.</summary>
        public PointsOfInterest(int capacity = 32)
        {
            entries = new Entry[Math.Max(1, capacity)];
        }

        /// <summary>The registry of the running wildlife, or null (read by the cinematic camera).</summary>
        public static PointsOfInterest Current { get; set; }

        /// <summary>Number of live points.</summary>
        public int Count
        {
            get
            {
                int n = 0;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].Used && entries[i].Expires > now) n++;
                }

                return n;
            }
        }

        /// <summary>The registry's clock (the time of the last <see cref="Tick"/>): publishers outside the wildlife set expiry times from it.</summary>
        public float Now => now;

        /// <summary>Advances the registry's clock (expired points are dropped).</summary>
        public void Tick(float time)
        {
            now = time;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Used && entries[i].Expires <= now) entries[i].Used = false;
            }
        }

        /// <summary>Adds or refreshes point <paramref name="id"/>; it lives until <paramref name="expires"/> (registry time) unless refreshed.</summary>
        public void Publish(long id, Vector3 position, float radius, float weight, float expires)
        {
            int free = -1;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Used && entries[i].Id == id)
                {
                    entries[i].Position = position;
                    entries[i].Radius = radius;
                    entries[i].Weight = weight;
                    entries[i].Expires = expires;
                    return;
                }

                if (!entries[i].Used && free < 0) free = i;
            }

            if (free < 0)
            {
                // Full: replace the weakest point if this one matters more.
                float weakest = float.MaxValue;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].Weight < weakest)
                    {
                        weakest = entries[i].Weight;
                        free = i;
                    }
                }

                if (weakest >= weight) return;
            }

            entries[free] = new Entry { Id = id, Position = position, Radius = radius, Weight = weight, Expires = expires, Used = true };
        }

        /// <summary>Removes point <paramref name="id"/>.</summary>
        public void Remove(long id)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Used && entries[i].Id == id) entries[i].Used = false;
            }
        }

        /// <summary>Removes everything.</summary>
        public void Clear()
        {
            for (int i = 0; i < entries.Length; i++) entries[i].Used = false;
        }

        /// <summary>Moves every point by <paramref name="delta"/> (floating origin).</summary>
        public void Shift(Vector3 delta)
        {
            for (int i = 0; i < entries.Length; i++) entries[i].Position += delta;
        }

        /// <summary>
        /// The best point to frame from <paramref name="from"/> looking along <paramref name="heading"/>: within
        /// <see cref="MinDistance"/>–<see cref="MaxDistance"/>, in front unless <paramref name="anyDirection"/> (seated rest
        /// shots), scored weight × front × nearness. Events weigh about 1.6 and beat any landmark (whose score is at most 1);
        /// herds about 0.5 win only when no landmark is in view.
        /// </summary>
        public bool TryBest(Vector3 from, Vector3 heading, bool anyDirection, out Vector3 subject, out float score)
        {
            subject = default;
            score = 0f;
            bool found = false;
            for (int i = 0; i < entries.Length; i++)
            {
                Entry e = entries[i];
                if (!e.Used || e.Expires <= now) continue;
                float dx = e.Position.x - from.x, dz = e.Position.z - from.z;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d < MinDistance || d > MaxDistance) continue;
                float front = (dx * heading.x + dz * heading.z) / d;
                if (!anyDirection && front < 0.25f) continue;
                float s = e.Weight * (anyDirection ? 1f : front) * (1f - (d - MinDistance) / (MaxDistance * 1.6f));
                if (s <= score) continue;
                score = s;
                subject = e.Position + Vector3.up * Math.Max(0.6f, e.Radius * 0.15f);
                found = true;
            }

            return found;
        }
    }
}
