using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Background hills ringed around the map edge beyond the playable area, with bigger background mountains
    /// on the highland side, so the square world does not end in a void.
    /// </summary>
    public static class HorizonPlanner
    {
        /// <summary>Plans horizon pieces around a world of <paramref name="p"/>; <paramref name="relief"/> gives edge heights and the highland side.</summary>
        public static List<HorizonPlacement> Plan(WorldGenParams p, Relief relief, PaletteKeys keys)
        {
            var result = new List<HorizonPlacement>();
            bool hills = keys.Has(PropRole.HorizonHill);
            bool mountains = keys.Has(PropRole.HorizonMountain);
            if (!hills && !mountains) return result;

            var rng = new System.Random(p.seed + 17);
            float size = p.worldSize;
            float spacing = Mathf.Max(150f, size * 0.12f);
            Vector2 highland = relief.Mask.Centre * size;
            var centre = new Vector2(size * 0.5f, size * 0.5f);
            Vector2 highDir = (highland - centre).normalized;

            // Walk the perimeter of the square.
            float perimeter = 4f * size;
            for (float s = (float)rng.NextDouble() * spacing; s < perimeter; s += spacing * (0.8f + 0.4f * (float)rng.NextDouble()))
            {
                int side = (int)(s / size);
                float t = s - side * size;
                Vector2 edge;
                Vector2 outward;
                switch (side)
                {
                    case 0: edge = new Vector2(t, 0f); outward = Vector2.down; break;
                    case 1: edge = new Vector2(size, t); outward = Vector2.right; break;
                    case 2: edge = new Vector2(size - t, size); outward = Vector2.up; break;
                    default: edge = new Vector2(0f, size - t); outward = Vector2.left; break;
                }

                float edgeHeight = HeightmapGenerator.SampleHeight(relief.Map, p, Mathf.Clamp(edge.x, 0f, size), Mathf.Clamp(edge.y, 0f, size));
                Vector2 fromCentre = (edge - centre).normalized;
                bool mountainSide = mountains && Vector2.Dot(fromCentre, highDir) > 0.55f;
                string family = mountainSide ? PropRole.HorizonMountain : hills ? PropRole.HorizonHill : PropRole.HorizonMountain;
                float distance = (mountainSide ? 450f : 250f) + (float)rng.NextDouble() * 250f;
                Vector2 pos = edge + outward * distance;
                float width = mountainSide ? 700f + (float)rng.NextDouble() * 400f : 350f + (float)rng.NextDouble() * 250f;
                float height = mountainSide ? p.hillAmplitude * (1.0f + (float)rng.NextDouble() * 0.6f) : 50f + (float)rng.NextDouble() * 70f;
                result.Add(new HorizonPlacement
                {
                    family = family,
                    variant = keys.PickVariant(family, rng),
                    position = new Vector3(pos.x, Mathf.Max(0f, edgeHeight - 15f), pos.y),
                    yaw = (float)(rng.NextDouble() * 360.0),
                    width = width,
                    height = height,
                });
            }

            return result;
        }
    }
}
