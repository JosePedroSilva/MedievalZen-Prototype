using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// How one prefab family is scattered over the countryside. Density is per hectare where the biome affinity is
    /// 1; the affinity at a point is the dot product of <see cref="biomes"/> with the local
    /// <see cref="BiomeWeights"/>. Everything is kept out of the road corridor, settlements, fields and (unless
    /// <see cref="onWater"/>) water.
    /// </summary>
    public sealed class ScatterRule
    {
        /// <summary>Family id (<see cref="PropRole"/>); tree families become terrain tree instances.</summary>
        public string family;
        /// <summary>Affinity per biome (meadow, forest, highland, farm, shore), 0..1.</summary>
        public BiomeWeights biomes;
        /// <summary>Instances per hectare at affinity 1.</summary>
        public float density;
        /// <summary>Footprint radius in metres (instances of any family never overlap footprints).</summary>
        public float footprint = 1f;
        /// <summary>Allowed slope (rise over run).</summary>
        public float minSlope;
        /// <summary>Allowed slope (rise over run).</summary>
        public float maxSlope = 10f;
        /// <summary>Only within this many metres of a village/town edge (0 = anywhere).</summary>
        public float nearSettlement;
        /// <summary>Placed on lakes at water level, only where the water is shallower than <see cref="maxDepth"/>.</summary>
        public bool onWater;
        /// <summary>Maximum water depth for <see cref="onWater"/> families.</summary>
        public float maxDepth = 1.5f;
        /// <summary>0 = uniform, 1 = strongly clumped (large-scale noise gates placement).</summary>
        public float clump;
        /// <summary>Uniform scale range.</summary>
        public float scaleMin = 0.8f;
        /// <summary>Uniform scale range.</summary>
        public float scaleMax = 1.2f;
        /// <summary>Nothing is placed at or above this normalised height (fraction of the total relief); 2 = no limit.</summary>
        public float maxHeight = 2f;
        /// <summary>Below <see cref="maxHeight"/> the density fades out over this band of normalised height.</summary>
        public float heightFade = 0.06f;
        /// <summary>
        /// Rock-like families (<see cref="PropRole.IsRock"/>): target size range in metres of mesh height (the planner converts it
        /// to a scale from the measured prefab; 0 = the family default in <see cref="RockPlacer.SpecFor"/>). Replaces
        /// <see cref="scaleMin"/> / <see cref="scaleMax"/> for them.
        /// </summary>
        public float heightMin;
        /// <summary>See <see cref="heightMin"/>.</summary>
        public float heightMax;
        /// <summary>Rock-like families: exponent on the size roll (&gt; 1 favours the small end of the range).</summary>
        public float sizeBias = 1f;

        /// <summary>Creates a rule.</summary>
        public ScatterRule(string family, BiomeWeights biomes, float density, float footprint)
        {
            this.family = family;
            this.biomes = biomes;
            this.density = density;
            this.footprint = footprint;
        }
    }

    /// <summary>
    /// The default scatter table. To add a family: add its id to <see cref="PropRole"/>, its prefabs to
    /// <see cref="SyntyCatalog"/>, and one line here.
    /// </summary>
    public static class ScatterRules
    {
        /// <summary>Biome weights constructor shorthand: meadow, forest, highland, farm, shore.</summary>
        private static BiomeWeights B(float meadow, float forest, float highland, float farm, float shore = 0f) =>
            new BiomeWeights(meadow, forest, highland, farm, shore);

        /// <summary>
        /// Rules in placement order (big things first so small props fill the gaps). Densities scale with
        /// <see cref="WorldGenParams.forestDensity"/> only through the forest biome itself. Broadleaf trees stop below
        /// <paramref name="treeLine"/> (fraction of the total relief); pines and the odd dead tree thin out above it and
        /// are gone 16 % higher, leaving rock, dry grass and cairns on the summits.
        /// </summary>
        public static List<ScatterRule> Default(float treeLine = 0.7f)
        {
            float broadleaf = Mathf.Clamp(treeLine, 0.2f, 1.5f);
            float conifer = broadleaf + 0.16f;
            return new List<ScatterRule>
            {
                // Trees (terrain tree instances). Forest ~110/ha gives ~15-20k trees on the default 2 km map.
                new ScatterRule(PropRole.ForestTree, B(0.02f, 1f, 0.1f, 0f), 140f, 2.2f) { maxSlope = 0.8f, scaleMin = 0.8f, scaleMax = 1.3f, maxHeight = broadleaf, heightFade = 0.1f },
                new ScatterRule(PropRole.MeadowTree, B(1f, 0f, 0.4f, 0.1f, 0.5f), 2.5f, 3.5f) { maxSlope = 0.6f, clump = 0.5f, scaleMin = 0.85f, scaleMax = 1.3f, maxHeight = broadleaf, heightFade = 0.1f },
                new ScatterRule(PropRole.PineTree, B(0f, 0.15f, 0.45f, 0f), 45f, 2.5f) { maxSlope = 0.9f, clump = 0.6f, scaleMin = 0.85f, scaleMax = 1.35f, maxHeight = conifer, heightFade = 0.2f },
                new ScatterRule(PropRole.DeadTree, B(0.05f, 0.05f, 0.1f, 0f, 0.4f), 0.4f, 3f) { maxSlope = 0.7f, clump = 0.5f, maxHeight = conifer, heightFade = 0.1f },
                new ScatterRule(PropRole.FruitTree, B(0.6f, 0f, 0f, 1f), 6f, 3.5f) { maxSlope = 0.35f, nearSettlement = 80f, clump = 0.4f, scaleMin = 0.9f, scaleMax = 1.2f, maxHeight = broadleaf, heightFade = 0.1f },

                // Highland: cliff blocks against steep slopes, outcrops, mounds. Rock-like families are placed by RockPlacer from
                // their measured shape: sized in metres, sunk into the ground, cliffs only where higher ground backs them. Only
                // Meadow pieces (one rock look, matching the RockWall terrain layer) are used in the range; the dark Generic dirt
                // cliffs stay on lowland escarpments; the Ground_Cliff_Large mesas are not scattered (they only fit level ground).
                new ScatterRule(PropRole.DirtCliff, B(1f, 0.6f, 0f, 0f), 2f, 5f) { minSlope = 0.75f, maxHeight = 0.42f, heightFade = 0.08f, heightMin = 3f, heightMax = 6.5f, sizeBias = 1.5f },
                new ScatterRule(PropRole.Cliff, B(0.5f, 0.35f, 1f, 0f), 7f, 4.5f) { minSlope = 0.75f, heightMin = 3f, heightMax = 8f, sizeBias = 1.8f },
                new ScatterRule(PropRole.RockGround, B(0.1f, 0f, 1f, 0f), 1.8f, 4f) { minSlope = 0.15f, maxSlope = 0.9f, clump = 0.4f, heightMin = 1.2f, heightMax = 3.2f },
                new ScatterRule(PropRole.Mound, B(0.15f, 0f, 1f, 0f), 0.35f, 10f) { maxSlope = 0.12f, heightMin = 1.5f, heightMax = 3.2f },

                // Forest floor.
                new ScatterRule(PropRole.Bush, B(0.12f, 0.6f, 0.1f, 0.05f, 0.4f), 16f, 1.4f) { maxSlope = 0.8f, clump = 0.3f },
                new ScatterRule(PropRole.RockPile, B(0.08f, 0.5f, 0.5f, 0f), 2.5f, 1.8f) { clump = 0.3f, heightMin = 1.2f, heightMax = 3.5f, sizeBias = 1.5f },
                new ScatterRule(PropRole.Shrub, B(0.05f, 0.7f, 0.15f, 0f, 0.3f), 10f, 1f) { maxSlope = 0.8f, clump = 0.5f },
                new ScatterRule(PropRole.Stump, B(0.02f, 1f, 0.1f, 0f), 4f, 1.2f) { maxSlope = 0.6f, clump = 0.3f },
                new ScatterRule(PropRole.Twig, B(0f, 1f, 0f, 0f), 5f, 0.6f) { maxSlope = 0.6f },
                new ScatterRule(PropRole.Leaves, B(0f, 1f, 0f, 0f), 7f, 0.9f) { maxSlope = 0.6f },
                new ScatterRule(PropRole.MushroomGroup, B(0f, 1f, 0f, 0f), 4f, 0.8f) { maxSlope = 0.6f, clump = 0.6f },
                new ScatterRule(PropRole.Mushroom, B(0.02f, 1f, 0f, 0f), 9f, 0.4f) { maxSlope = 0.6f, clump = 0.6f },

                // Meadow and shore rocks.
                new ScatterRule(PropRole.Rock, B(0.6f, 0.2f, 1f, 0.05f, 0.6f), 1.6f, 1.3f) { clump = 0.5f, heightMin = 0.6f, heightMax = 2.5f, sizeBias = 1.4f },
                new ScatterRule(PropRole.LowlandRock, B(0.8f, 0.2f, 0f, 0.1f, 0.8f), 1f, 1.3f) { clump = 0.5f, maxHeight = 0.4f, heightFade = 0.08f, heightMin = 0.6f, heightMax = 2f, sizeBias = 1.4f },
                new ScatterRule(PropRole.RockSmallPile, B(1f, 0.2f, 0.6f, 0.4f, 0.6f), 1.8f, 0.8f) { clump = 0.3f, heightMin = 0.15f, heightMax = 0.4f },

                // Sunflower clusters in meadows (strongly clumped so they read as yellow patches).
                new ScatterRule(PropRole.Sunflower, B(0.9f, 0f, 0f, 0.6f), 3f, 0.5f) { maxSlope = 0.3f, clump = 0.9f, scaleMin = 0.9f, scaleMax = 1.15f },

                // Lakes.
                new ScatterRule(PropRole.Lilies, B(0f, 0f, 0f, 0f), 50f, 1.2f) { onWater = true, maxDepth = 1.6f, clump = 0.5f },
            };
        }

        /// <summary>
        /// The endless road's table (the finite world keeps <see cref="Default"/>): the same rules in the same order, plus trees
        /// along the streams and lake shores, more and more strongly grouped meadow trees, and an understorey in open country
        /// (bushes, shrubs and ferns, small stones). The structured planting (roadside lines, hedgerows, copses, crest trees, wood
        /// edges) is added by the endless generator on top of it.
        /// </summary>
        public static List<ScatterRule> Endless(float treeLine)
        {
            List<ScatterRule> rules = Default(treeLine);
            float broadleaf = Mathf.Clamp(treeLine, 0.2f, 1.5f);
            foreach (ScatterRule rule in rules)
            {
                switch (rule.family)
                {
                    case PropRole.MeadowTree:
                        rule.biomes = B(1f, 0f, 0.4f, 0.25f, 0.5f);
                        rule.density = 8f;
                        rule.clump = 0.75f;
                        break;
                    case PropRole.Bush:
                        rule.biomes = B(0.45f, 0.6f, 0.15f, 0.08f, 0.6f);
                        rule.density = 20f;
                        rule.clump = 0.55f;
                        break;
                    case PropRole.Shrub:
                        rule.biomes = B(0.25f, 0.8f, 0.15f, 0f, 0.5f);
                        rule.density = 12f;
                        break;
                    case PropRole.RockSmallPile:
                        rule.density = 3f;
                        break;
                    case PropRole.LowlandRock:
                        rule.density = 1.4f;
                        break;
                }
            }

            // Trees along the water: alder-like broadleaves on stream banks and lake margins (the shore biome), after the woods.
            int at = rules.FindIndex(r => r.family == PropRole.DeadTree);
            rules.Insert(at, new ScatterRule(PropRole.ForestTree, B(0f, 0f, 0f, 0f, 1f), 70f, 3f) { maxSlope = 0.6f, clump = 0.35f, scaleMin = 0.85f, scaleMax = 1.25f, maxHeight = broadleaf, heightFade = 0.1f });
            return rules;
        }
    }
}
