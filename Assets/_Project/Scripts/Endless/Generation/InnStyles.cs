using System;
using System.Collections.Generic;
using MedievalZen.World;

namespace MedievalZen.Endless.Generation
{
    /// <summary>The kinds of roadside inn (endless road only; see <see cref="InnStyles"/> and <see cref="InnComposer"/>).</summary>
    public enum InnArchetype
    {
        /// <summary>Tavern, stables and a barn round three sides of a yard open to the road, set back behind a forecourt.</summary>
        Coaching = 0,
        /// <summary>Small and close to the road: a large house as the inn, a bench round a big tree, a lean-to, barrels by the door.</summary>
        Alehouse = 1,
        /// <summary>The inn with a working farmyard, skewed to the road with a lane: barn, haystacks, paddock, kitchen garden, fruit trees.</summary>
        Farm = 2,
        /// <summary>In the woods: a clearing with a woodshed, log piles, a woodcutter's corner and log seats round a fire.</summary>
        ForestLodge = 3,
        /// <summary>Near a lake or a stream: turned toward the water, a landing with flat stones, reeds and a bench at the edge.</summary>
        Waterside = 4,
        /// <summary>The refuge at a col (three variants of its own).</summary>
        Refuge = 5,
        /// <summary>The inn on one side of the road and the forge across it: the only inn that holds both sides of the road.</summary>
        ForgeInn = 6,
    }

    /// <summary>Where an inn is and what it is: its archetype, the water near it and the refuge's variant. Pure data.</summary>
    public struct InnSite
    {
        /// <summary>The archetype.</summary>
        public InnArchetype Archetype;
        /// <summary>Side of the road the nearest water lies on (+1 right, -1 left), 0 when none is within reach.</summary>
        public int WaterSide;
        /// <summary>Distance from the road's centre to that water (metres, at the inn), or 0.</summary>
        public float WaterAcross;
        /// <summary>Refuge only: which of its three layouts (0 windbreak arc, 1 walled pen, 2 two lean-tos and a cairn row).</summary>
        public int RefugeVariant;
    }

    /// <summary>
    /// Picks each inn's archetype from the seed, the region and the site (woods, water), never the same as the inn before it on the
    /// road. Inns are numbered within the interval between two mandatory sites (the start, a col): the inn with an even number in its
    /// interval takes its own favourite; an odd one takes its favourite among those its two neighbours (both even, or a refuge) did
    /// not take. So any inn's archetype follows from the seed and the inns' ordinals alone, whichever tile asks first. Pure (the
    /// water queries are the generator's pure ones); randomness stream 1293.
    /// </summary>
    public static class InnStyles
    {
        /// <summary>Randomness stream of the archetype ranking.</summary>
        public const int Stream = 1293;

        /// <summary>Farthest water from the road's centre that makes an inn a waterside inn, metres.</summary>
        public const float WaterReach = 46f;

        private static readonly int[] Candidates = { 0, 1, 2, 3, 4, 6 };

        /// <summary>The inn's site: its archetype and the water near it.</summary>
        public static InnSite For(EndlessParams p, JourneyPlan plan, RoadCurve road, TerrainField field, Place inn)
        {
            var site = new InnSite();
            if (inn.Refuge)
            {
                site.Archetype = InnArchetype.Refuge;
                // Consecutive cols have consecutive ordinals: two refuges in a row never share a layout.
                long ordinal = (inn.Id >> 4) >> 4;
                site.RefugeVariant = (int)(((ordinal % 3) + 3) % 3);
                return site;
            }

            site.WaterSide = Water(road, field, inn.S, out site.WaterAcross);
            int j = (inn.Id >> 4) & 15;
            if (j % 2 == 0)
            {
                site.Archetype = Favourite(p, plan, road, field, inn, -1, -1);
                return site;
            }

            var list = new List<Place>();
            double reach = Math.Max(p.innSpacingMin, p.innSpacingMax) + 50.0;
            plan.Inns(inn.S - reach, inn.S + reach, list);
            int i = list.FindIndex(x => x.Id == inn.Id);
            int prev = -1, next = -1;
            if (i > 0 && !list[i - 1].Refuge) prev = (int)Favourite(p, plan, road, field, list[i - 1], -1, -1);
            if (i >= 0 && i + 1 < list.Count && !list[i + 1].Refuge) next = (int)Favourite(p, plan, road, field, list[i + 1], -1, -1);
            site.Archetype = Favourite(p, plan, road, field, inn, prev, next);
            return site;
        }

        /// <summary>The best-scoring eligible archetype other than <paramref name="avoidA"/> and <paramref name="avoidB"/>.</summary>
        private static InnArchetype Favourite(EndlessParams p, JourneyPlan plan, RoadCurve road, TerrainField field, Place inn, int avoidA, int avoidB)
        {
            RegionKind kind = plan.Regions.At(inn.S).Kind;
            bool water = Water(road, field, inn.S, out _) != 0;
            int best = (int)InnArchetype.Coaching;
            double bestScore = double.MinValue;
            foreach (int a in Candidates)
            {
                if (a == avoidA || a == avoidB) continue;
                double score = EndlessNoise.Unit(p.seed, Stream, inn.Id, a);
                switch ((InnArchetype)a)
                {
                    case InnArchetype.ForestLodge:
                        if (kind != RegionKind.Forest) continue;
                        score += 0.8;
                        break;
                    case InnArchetype.Waterside:
                        if (!water) continue;
                        score += 0.8;
                        break;
                    case InnArchetype.Farm:
                        if (kind == RegionKind.Farmland || kind == RegionKind.Meadow) score += 0.25;
                        if (kind == RegionKind.HighlandPass) score -= 0.3;
                        break;
                    case InnArchetype.ForgeInn:
                        if (kind == RegionKind.Forest || kind == RegionKind.HighlandPass) score -= 0.3;
                        break;
                    case InnArchetype.Coaching:
                        if (kind == RegionKind.Downs || kind == RegionKind.HighlandPass) score += 0.2;
                        break;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = a;
                }
            }

            return (InnArchetype)best;
        }

        /// <summary>
        /// The side of the road (+1 right, -1 left) with a lake or a stream within <see cref="WaterReach"/> of the road's centre at
        /// road distance <paramref name="s"/> (within 24 m along it), and how far across it is; 0 when there is none.
        /// </summary>
        public static int Water(RoadCurve road, TerrainField field, double s, out float across, float reach = WaterReach)
        {
            across = 0f;
            double z0 = road.ZAt(s - 1.0), z1 = road.ZAt(s + 1.0);
            double x0 = road.X(z0), x1 = road.X(z1);
            double tx = x1 - x0, tz = z1 - z0, len = Math.Sqrt(tx * tx + tz * tz);
            if (len < 1e-6) return 0;
            tx /= len;
            tz /= len;
            double cz = road.ZAt(s), cx = road.X(cz);
            int bestSide = 0;
            float best = float.MaxValue;
            foreach (int side in new[] { 1, -1 })
            {
                double nx = tz * side, nz = -tx * side;
                foreach (double along in new[] { 0.0, -24.0, 24.0 })
                {
                    for (float d = 12f; d <= reach; d += 6f)
                    {
                        double x = cx + tx * along + nx * d, z = cz + tz * along + nz * d;
                        bool wet = field.WaterHeight(x, z) >= 0f || (field.Streams != null && field.Streams.Clearance(x, z, 4.0) <= 0.5f);
                        if (!wet) continue;
                        if (d < best)
                        {
                            best = d;
                            bestSide = side;
                        }

                        break;
                    }
                }
            }

            if (bestSide != 0) across = best;
            return bestSide;
        }
    }
}
