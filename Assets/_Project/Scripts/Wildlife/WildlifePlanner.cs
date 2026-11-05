using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;

namespace MedievalZen.Wildlife
{
    /// <summary>Where a group lives and why.</summary>
    public enum GroupKind
    {
        /// <summary>A herd in a fallow field.</summary>
        Pasture = 0,

        /// <summary>Hens, a rooster, chicks, maybe a goose or a pig, a dog and a cat beside a settlement.</summary>
        Farmyard = 1,

        /// <summary>One or two horses beside an inn.</summary>
        Paddock = 2,

        /// <summary>Doves at a village or an inn.</summary>
        Doves = 3,

        /// <summary>Goats grazing near a hamlet.</summary>
        Goats = 4,

        /// <summary>Wild animals (deer, rabbits, boar, foxes, squirrels, free-ranging sheep and goats).</summary>
        Wild = 5,

        /// <summary>Geese or a beaver on a lake shore.</summary>
        Shore = 6,

        /// <summary>An eagle circling high over the pass.</summary>
        Sky = 7,

        /// <summary>Animals an event brought in (a fox, a wolf, a sheepdog); released like the others when far away.</summary>
        Visitor = 8,
    }

    /// <summary>One planned animal.</summary>
    public struct MemberPlan
    {
        public Species Species;

        /// <summary>Index into the species' prefab list (colour variant).</summary>
        public int Variant;

        /// <summary>Absolute position.</summary>
        public double X, Z;

        /// <summary>Facing, degrees about Y.</summary>
        public float Yaw;
    }

    /// <summary>A planned group in absolute coordinates: a pure function of the seed and the cell it belongs to.</summary>
    public sealed class GroupPlan
    {
        /// <summary>Stable id (the same group always has the same id for a given seed).</summary>
        public long Id;

        public GroupKind Kind;

        /// <summary>The species that names the group (the hens of a farmyard).</summary>
        public Species Main;

        /// <summary>Centre of the home range (absolute) and its radius.</summary>
        public double HomeX, HomeZ;

        public float HomeRadius;

        /// <summary>Pastures: the field rectangle (absolute centre, half sizes, yaw) the herd stays in.</summary>
        public bool HasRect;

        public double RectX, RectZ;
        public float HalfX, HalfZ, Yaw;

        /// <summary>The settlement it belongs to, or −1.</summary>
        public int SettlementId = -1;

        /// <summary>The region at the home.</summary>
        public RegionKind Region;

        /// <summary>Planner cell.</summary>
        public long CellX, CellZ;

        public readonly List<MemberPlan> Members = new List<MemberPlan>();

        /// <summary>Total animals.</summary>
        public int Count => Members.Count;

        /// <summary>Number of members of a species.</summary>
        public int CountOf(Species s)
        {
            int n = 0;
            foreach (MemberPlan m in Members)
            {
                if (m.Species == s) n++;
            }

            return n;
        }

        public override string ToString() => $"{Kind} {SpeciesCatalog.Get(Main).Name} ×{Count} #{Id}";
    }

    /// <summary>
    /// Deterministic placement of animal groups on a 128 m grid of absolute cells. Each cell is planned from
    /// <c>EndlessNoise.Seed(seed, 901, cellX, cellZ)</c> (and per-field / per-settlement streams 902–912) and the habitat
    /// only, so the same herd stands in the same field when the traveller walks back, whatever order cells are planned in.
    /// Rules: herds of cows, sheep or horses in fallow fields; farmyards just outside settlements, on a ring away from the
    /// road; goats near hamlets; doves at villages and inns; a horse at inns; deer, boar, squirrels in forest, foxes and
    /// rabbits at edges and in meadows; sheep and goats on the downs; geese and beavers on lake shores; goats, an eagle and a
    /// wolf (night) on the pass. Never in water, on steep ground, in the road corridor or in a settlement's dressed radius.
    /// Pure (no Unity calls).
    /// </summary>
    public sealed class WildlifePlanner
    {
        /// <summary>Edge of a planner cell, metres.</summary>
        public const double CellSize = 128.0;

        /// <summary>First noise stream of the planner (the generator uses 101–810).</summary>
        public const int Stream = 901;

        /// <summary>Most groups a cell holds.</summary>
        public const int MaxGroupsPerCell = 4;

        /// <summary>Wild groups keep at least this far from the road's centre line.</summary>
        public const float WildRoadClearance = 24f;

        private readonly IWildlifeHabitat habitat;
        private readonly int seed;

        /// <summary>Creates a planner over a habitat.</summary>
        public WildlifePlanner(IWildlifeHabitat habitat)
        {
            this.habitat = habitat ?? throw new ArgumentNullException(nameof(habitat));
            seed = habitat.Seed;
        }

        /// <summary>The habitat planned on.</summary>
        public IWildlifeHabitat Habitat => habitat;

        /// <summary>The cell containing an absolute position.</summary>
        public static void CellOf(double x, double z, out long cx, out long cz)
        {
            cx = (long)Math.Floor(x / CellSize);
            cz = (long)Math.Floor(z / CellSize);
        }

        /// <summary>Packs a cell into one key.</summary>
        public static long Key(long cx, long cz) => unchecked((cx << 32) ^ (cz & 0xffffffffL));

        /// <summary>Plans one cell (allocates; call off the frame or rarely).</summary>
        public List<GroupPlan> PlanCell(long cx, long cz)
        {
            var groups = new List<GroupPlan>();
            double x0 = cx * CellSize, z0 = cz * CellSize;
            PlanPastures(cx, cz, x0, z0, groups);
            PlanSettlements(cx, cz, x0, z0, groups);
            PlanWild(cx, cz, x0, z0, groups);
            if (groups.Count > MaxGroupsPerCell) groups.RemoveRange(MaxGroupsPerCell, groups.Count - MaxGroupsPerCell);
            foreach (GroupPlan g in groups)
            {
                g.CellX = cx;
                g.CellZ = cz;
            }

            return groups;
        }

        // ------------------------------------------------------------------ pastures

        private void PlanPastures(long cx, long cz, double x0, double z0, List<GroupPlan> groups)
        {
            var fields = new List<HabitatPasture>();
            habitat.Pastures(x0, z0, CellSize, fields);
            fields.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (HabitatPasture f in fields)
            {
                if (EndlessNoise.Unit(seed, Stream + 1, f.Id) >= 0.6) continue;   // most fallow fields hold a herd
                double roll = EndlessNoise.Unit(seed, Stream + 2, f.Id);
                Species species = roll < 0.4 ? Species.Cow : roll < 0.8 ? Species.Sheep : Species.Horse;
                var rng = new Random(EndlessNoise.Seed(seed, Stream + 3, f.Id));
                int count = species == Species.Cow ? rng.Next(3, 7) : species == Species.Sheep ? rng.Next(5, 10) : rng.Next(2, 5);
                var g = new GroupPlan
                {
                    Id = f.Id ^ 0x5A5A0000L, Kind = GroupKind.Pasture, Main = species, HomeX = f.X, HomeZ = f.Z,
                    HomeRadius = Math.Min(f.HalfX, f.HalfZ), HasRect = true, RectX = f.X, RectZ = f.Z,
                    HalfX = Math.Max(2f, f.HalfX - 4f), HalfZ = Math.Max(2f, f.HalfZ - 4f), Yaw = f.Yaw,
                };
                SpeciesInfo info = SpeciesCatalog.Get(species);
                double rad = f.Yaw * Math.PI / 180.0, cs = Math.Cos(rad), sn = Math.Sin(rad);
                // A loose cluster somewhere in the field (herds keep together).
                double cu = (rng.NextDouble() - 0.5) * g.HalfX, cv = (rng.NextDouble() - 0.5) * g.HalfZ;
                for (int i = 0; i < count * 3 && g.Members.Count < count; i++)
                {
                    double u = Clamp(cu + (rng.NextDouble() - 0.5) * 18.0, -g.HalfX, g.HalfX);
                    double v = Clamp(cv + (rng.NextDouble() - 0.5) * 18.0, -g.HalfZ, g.HalfZ);
                    double x = f.X + u * cs + v * sn, z = f.Z - u * sn + v * cs;
                    HabitatSample s = habitat.Sample(x, z);
                    if (!Standable(s, info, habitat.Corridor + 4f) || !DryBetween(f.X, f.Z, x, z)) continue;
                    g.Members.Add(new MemberPlan { Species = species, Variant = rng.Next(info.Prefabs.Length), X = x, Z = z, Yaw = (float)(rng.NextDouble() * 360.0) });
                }

                if (g.Members.Count < 2) continue;
                g.Region = habitat.Sample(f.X, f.Z).Region;
                groups.Add(g);
            }
        }

        // ------------------------------------------------------------------ settlements

        private void PlanSettlements(long cx, long cz, double x0, double z0, List<GroupPlan> groups)
        {
            var places = new List<HabitatSettlement>();
            habitat.Settlements(x0 - 200.0, z0 - 200.0, CellSize + 400.0, places);
            places.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (HabitatSettlement place in places)
            {
                var rng = new Random(EndlessNoise.Seed(seed, Stream + 4, place.Id));
                int side = rng.NextDouble() < 0.5 ? -1 : 1;
                // The farmyard: hens, a rooster, chicks, sometimes a goose or a pig, often a dog and a cat.
                if (TryAnchor(place, side, 14f, rng.NextDouble() * 0.8 - 0.4, out double fx, out double fz) && InCell(fx, fz, x0, z0))
                {
                    var g = NewGroup(place, GroupKind.Farmyard, Species.Hen, fx, fz, 7f, Stream + 5);
                    var mrng = new Random(EndlessNoise.Seed(seed, Stream + 5, place.Id));
                    AddMembers(g, Species.Hen, mrng.Next(3, 6), 5f, mrng);
                    AddMembers(g, Species.Rooster, 1, 4f, mrng);
                    if (mrng.NextDouble() < 0.6) AddMembers(g, Species.Chick, mrng.Next(2, 5), 3f, mrng);
                    if (place.Kind != HabitatSettlementKind.Inn || mrng.NextDouble() < 0.3)
                    {
                        if (mrng.NextDouble() < 0.5) AddMembers(g, Species.Goose, mrng.Next(1, 3), 5f, mrng);
                        else AddMembers(g, Species.Pig, mrng.Next(1, 3), 5f, mrng);
                    }

                    if (mrng.NextDouble() < 0.8) AddMembers(g, mrng.NextDouble() < 0.6 ? Species.Retriever : Species.GreatDane, 1, 5f, mrng);
                    if (mrng.NextDouble() < 0.55) AddMembers(g, Species.Cat, 1, 6f, mrng);
                    if (g.Members.Count >= 3) groups.Add(g);
                }

                // A horse or two at an inn, on the other side.
                if (place.Kind == HabitatSettlementKind.Inn && rng.NextDouble() < 0.8 &&
                    TryAnchor(place, -side, 16f, rng.NextDouble() * 0.6 - 0.3, out double hx, out double hz) && InCell(hx, hz, x0, z0))
                {
                    var g = NewGroup(place, GroupKind.Paddock, Species.Horse, hx, hz, 8f, Stream + 6);
                    var mrng = new Random(EndlessNoise.Seed(seed, Stream + 6, place.Id));
                    AddMembers(g, Species.Horse, mrng.Next(1, 3), 4f, mrng);
                    if (g.Members.Count > 0) groups.Add(g);
                }

                // Doves at villages (and some inns), on the farmyard side further along.
                double doveChance = place.Kind == HabitatSettlementKind.Inn ? 0.35 : 0.8;
                if (rng.NextDouble() < doveChance &&
                    TryAnchor(place, side, 8f, (rng.NextDouble() < 0.5 ? -0.75 : 0.75), out double dx, out double dz) && InCell(dx, dz, x0, z0))
                {
                    var g = NewGroup(place, GroupKind.Doves, Species.Dove, dx, dz, 5f, Stream + 7);
                    var mrng = new Random(EndlessNoise.Seed(seed, Stream + 7, place.Id));
                    AddMembers(g, Species.Dove, mrng.Next(4, 8), 3.5f, mrng);
                    if (g.Members.Count >= 3) groups.Add(g);
                }

                // Goats near hamlets, a little further out.
                if (place.Kind == HabitatSettlementKind.Hamlet && rng.NextDouble() < 0.75 &&
                    TryAnchor(place, -side, 45f, rng.NextDouble() * 0.8 - 0.4, out double gx, out double gz) && InCell(gx, gz, x0, z0))
                {
                    var g = NewGroup(place, GroupKind.Goats, Species.Goat, gx, gz, 12f, Stream + 8);
                    var mrng = new Random(EndlessNoise.Seed(seed, Stream + 8, place.Id));
                    AddMembers(g, Species.Goat, mrng.Next(3, 6), 9f, mrng);
                    if (g.Members.Count >= 2) groups.Add(g);
                }
            }
        }

        /// <summary>
        /// A point on the ring just outside a settlement's dressed radius: <paramref name="side"/> of the road (−1/+1), at
        /// <paramref name="beyond"/> metres past the radius, shifted along the road by <paramref name="along"/> × radius. Tries
        /// the other side and smaller shifts when the ground is wet, steep or too near the road.
        /// </summary>
        public bool TryAnchor(HabitatSettlement place, int side, float beyond, double along, out double x, out double z)
        {
            double h = place.RoadHeadingDeg * Math.PI / 180.0;
            double fx = Math.Sin(h), fz = Math.Cos(h);   // along the road
            double rx = Math.Cos(h), rz = -Math.Sin(h);  // its right
            SpeciesInfo hen = SpeciesCatalog.Get(Species.Hen);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int sd = attempt % 2 == 0 ? side : -side;
                double al = along * (1.0 - attempt / 6.0);
                // Across the road, outside the dressed radius; along it, within the radius' length.
                double across = place.Radius + beyond;
                double a = al * place.Radius;
                x = place.X + rx * sd * across + fx * a;
                z = place.Z + rz * sd * across + fz * a;
                double dxp = x - place.X, dzp = z - place.Z;
                if (dxp * dxp + dzp * dzp < (place.Radius + 4.0) * (place.Radius + 4.0)) continue;
                HabitatSample s = habitat.Sample(x, z);
                if (Standable(s, hen, habitat.Corridor + 6f)) return true;
            }

            x = place.X;
            z = place.Z;
            return false;
        }

        private GroupPlan NewGroup(HabitatSettlement place, GroupKind kind, Species main, double x, double z, float radius, int stream)
        {
            return new GroupPlan
            {
                Id = ((long)place.Id << 8) | (long)(stream - Stream), Kind = kind, Main = main, HomeX = x, HomeZ = z, HomeRadius = radius,
                SettlementId = place.Id, Region = habitat.Sample(x, z).Region,
            };
        }

        private void AddMembers(GroupPlan g, Species species, int count, float spread, Random rng)
        {
            SpeciesInfo info = SpeciesCatalog.Get(species);
            for (int i = 0, tries = 0; i < count && tries < count * 4; tries++)
            {
                double a = rng.NextDouble() * Math.PI * 2.0, r = Math.Sqrt(rng.NextDouble()) * spread;
                double x = g.HomeX + Math.Cos(a) * r, z = g.HomeZ + Math.Sin(a) * r;
                HabitatSample s = habitat.Sample(x, z);
                if (!Standable(s, info, habitat.Corridor + 3f) || !DryBetween(g.HomeX, g.HomeZ, x, z)) continue;
                g.Members.Add(new MemberPlan { Species = species, Variant = rng.Next(info.Prefabs.Length), X = x, Z = z, Yaw = (float)(rng.NextDouble() * 360.0) });
                i++;
            }
        }

        // ------------------------------------------------------------------ wild

        private void PlanWild(long cx, long cz, double x0, double z0, List<GroupPlan> groups)
        {
            var rng = new Random(EndlessNoise.Seed(seed, Stream, cx, cz));
            // Three candidate spots; the first that suits the roll wins. Where a lake is near, a spot on its shore comes first
            // (geese and beavers need one, and shores are narrow).
            double roll = rng.NextDouble();
            double sx = x0 + CellSize * 0.5, sz = z0 + CellSize * 0.5;
            bool shoreFirst = false;
            if (habitat.Sample(sx, sz).Region == RegionKind.LakeCountry || habitat.IsWater(sx, sz))
            {
                var probe = new Random(EndlessNoise.Seed(seed, Stream + 10, cx, cz));
                for (int k = 0; k < 16 && !shoreFirst; k++)
                {
                    double px = x0 + 8.0 + probe.NextDouble() * (CellSize - 16.0), pz = z0 + 8.0 + probe.NextDouble() * (CellSize - 16.0);
                    HabitatSample ps = habitat.Sample(px, pz);
                    // The road keeps to the lake shore, so shore birds may stand nearer to it than other wild animals.
                    if (!ps.Valid || ps.Water || ps.RoadDistance < habitat.Corridor + 5f) continue;
                    if (ps.Shore <= 0.35f && !NearWater(px, pz, 9.0)) continue;
                    sx = px;
                    sz = pz;
                    shoreFirst = true;
                }
            }

            for (int c = 0; c < 3; c++)
            {
                double x = x0 + 8.0 + rng.NextDouble() * (CellSize - 16.0);
                double z = z0 + 8.0 + rng.NextDouble() * (CellSize - 16.0);
                if (c == 0 && shoreFirst)
                {
                    x = sx;
                    z = sz;
                }

                HabitatSample s = habitat.Sample(x, z);
                bool shore = c == 0 && shoreFirst;
                if (shore) s.Shore = Math.Max(s.Shore, 0.8f);
                if (!s.Valid || s.Water || s.RoadDistance < (shore ? habitat.Corridor + 5f : WildRoadClearance)) continue;
                if (!ChooseWild(s, roll, rng.NextDouble(), out Species species, out int count, out GroupKind kind)) return;
                SpeciesInfo info = SpeciesCatalog.Get(species);
                if (!info.Flies && s.Slope > info.MaxSlope) continue;
                if (NearSettlement(x, z, 30f)) continue;
                var g = new GroupPlan
                {
                    Id = Key(cx, cz) * 8 + 7, Kind = kind, Main = species, HomeX = x, HomeZ = z, HomeRadius = WildRange(species), Region = s.Region,
                };
                if (species == Species.Eagle)
                {
                    g.Members.Add(new MemberPlan { Species = species, X = x, Z = z, Yaw = (float)(rng.NextDouble() * 360.0) });
                }
                else
                {
                    var mrng = new Random(EndlessNoise.Seed(seed, Stream + 9, cx, cz));
                    float spread = Math.Max(2f, g.HomeRadius * 0.6f);
                    if (species == Species.Deer)
                    {
                        // A herd of hinds with a stag now and then, and two in about one herd in four (they square up).
                        int stags = count >= 3 && mrng.NextDouble() < 0.25 ? 2 : mrng.NextDouble() < 0.45 ? 1 : 0;
                        AddWild(g, Species.Stag, stags, spread, mrng, WildRoadClearance - 4f);
                        AddWild(g, Species.Deer, count - stags, spread, mrng, WildRoadClearance - 4f);
                    }
                    else
                    {
                        AddWild(g, species, count, spread, mrng, kind == GroupKind.Shore ? habitat.Corridor + 3f : WildRoadClearance - 4f);
                    }
                }

                if (g.Members.Count == 0) continue;
                groups.Add(g);
                return;
            }
        }

        private void AddWild(GroupPlan g, Species species, int count, float spread, Random rng, float clearance)
        {
            SpeciesInfo info = SpeciesCatalog.Get(species);
            for (int i = 0, tries = 0; i < count && tries < count * 4; tries++)
            {
                double a = rng.NextDouble() * Math.PI * 2.0, r = Math.Sqrt(rng.NextDouble()) * spread;
                double x = g.HomeX + Math.Cos(a) * r, z = g.HomeZ + Math.Sin(a) * r;
                HabitatSample s = habitat.Sample(x, z);
                if (!Standable(s, info, clearance) || !DryBetween(g.HomeX, g.HomeZ, x, z)) continue;
                g.Members.Add(new MemberPlan { Species = species, Variant = rng.Next(info.Prefabs.Length), X = x, Z = z, Yaw = (float)(rng.NextDouble() * 360.0) });
                i++;
            }
        }

        /// <summary>
        /// The wild group a spot holds, from its habitat and two uniform rolls (<paramref name="roll"/> decides whether there is
        /// a group, <paramref name="pick"/> which). Pure rule, unit tested.
        /// </summary>
        public static bool ChooseWild(HabitatSample s, double roll, double pick, out Species species, out int count, out GroupKind kind)
        {
            species = Species.Rabbit;
            count = 0;
            kind = GroupKind.Wild;
            int n = (int)(pick * 997.0) % 7;
            if (s.Shore > 0.35f)
            {
                if (roll >= 0.6) return false;
                kind = GroupKind.Shore;
                if (pick < 0.62) { species = Species.Goose; count = 3 + n % 4; }
                else if (pick < 0.85) { species = Species.Beaver; count = 1; }
                else { species = Species.Deer; count = 2 + n % 3; kind = GroupKind.Wild; }
                return true;
            }

            switch (s.Region)
            {
                case RegionKind.Forest:
                    if (roll >= 0.42) return false;
                    if (s.Forest > 0.45f)
                    {
                        if (pick < 0.34) { species = Species.Deer; count = 2 + n % 3; }
                        else if (pick < 0.54) { species = Species.Boar; count = 1 + n % 3; }
                        else if (pick < 0.88) { species = Species.Squirrel; count = 1 + n % 2; }
                        else if (pick < 0.94) { species = Species.Wolf; count = 1; }
                        else { species = Species.Fox; count = 1; }
                    }
                    else
                    {
                        if (pick < 0.45) { species = Species.Deer; count = 2 + n % 4; }
                        else if (pick < 0.75) { species = Species.Rabbit; count = 2 + n % 3; }
                        else { species = Species.Fox; count = 1; }
                    }

                    return true;

                case RegionKind.Meadow:
                    if (roll >= 0.36) return false;
                    if (pick < 0.45) { species = Species.Rabbit; count = 2 + n % 3; }
                    else if (pick < 0.75) { species = Species.Deer; count = 2 + n % 3; }
                    else if (pick < 0.87) { species = Species.Fox; count = 1; }
                    else { species = Species.Sheep; count = 4 + n % 4; }
                    return true;

                case RegionKind.Downs:
                    if (roll >= 0.36) return false;
                    if (pick < 0.5) { species = Species.Sheep; count = 5 + n % 5; }
                    else if (pick < 0.7) { species = Species.Goat; count = 2 + n % 4; }
                    else if (pick < 0.9) { species = Species.Rabbit; count = 2 + n % 3; }
                    else { species = Species.Deer; count = 2 + n % 2; }
                    return true;

                case RegionKind.LakeCountry:
                    if (roll >= 0.28) return false;
                    if (pick < 0.4) { species = Species.Deer; count = 2 + n % 3; }
                    else if (pick < 0.8) { species = Species.Rabbit; count = 2 + n % 3; }
                    else { species = Species.Fox; count = 1; }
                    return true;

                case RegionKind.HighlandPass:
                    if (roll >= 0.32) return false;
                    if (pick < 0.6) { species = Species.Goat; count = 2 + n % 4; }
                    else if (pick < 0.72) { species = Species.Eagle; count = 1; kind = GroupKind.Sky; }
                    else { species = Species.Wolf; count = 1; }
                    return true;

                default: // farmland: the fields hold the herds; a few wild ones at the edges
                    if (roll >= 0.14) return false;
                    if (pick < 0.6) { species = Species.Rabbit; count = 2 + n % 3; }
                    else if (pick < 0.85) { species = Species.Deer; count = 2 + n % 2; }
                    else { species = Species.Fox; count = 1; }
                    return true;
            }
        }

        private static float WildRange(Species s)
        {
            switch (s)
            {
                case Species.Deer: return 14f;
                case Species.Sheep: return 16f;
                case Species.Goat: return 12f;
                case Species.Boar: return 9f;
                case Species.Rabbit: return 8f;
                case Species.Squirrel: return 6f;
                case Species.Goose: return 7f;
                case Species.Beaver: return 4f;
                case Species.Fox: return 14f;
                case Species.Wolf: return 18f;
                case Species.Eagle: return 80f;
                default: return 10f;
            }
        }

        private bool NearSettlement(double x, double z, float margin)
        {
            var places = new List<HabitatSettlement>();
            habitat.Settlements(x - 1.0, z - 1.0, 2.0, places);
            foreach (HabitatSettlement p in places)
            {
                double r = p.Radius + margin;
                if ((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < r * r) return true;
            }

            return false;
        }

        /// <summary>True when there is water within <paramref name="reach"/> metres of (x, z) (four probes).</summary>
        private bool NearWater(double x, double z, double reach) =>
            habitat.IsWater(x + reach, z) || habitat.IsWater(x - reach, z) || habitat.IsWater(x, z + reach) || habitat.IsWater(x, z - reach);

        /// <summary>True when the straight line from a group's home to a member is dry (no group sits astride a stream or a lake inlet).</summary>
        private bool DryBetween(double x0, double z0, double x1, double z1)
        {
            for (int k = 1; k <= 3; k++)
            {
                double t = k / 4.0;
                if (habitat.IsWater(x0 + (x1 - x0) * t, z0 + (z1 - z0) * t)) return false;
            }

            return true;
        }

        /// <summary>True when an animal of <paramref name="info"/> may stand on <paramref name="s"/> at least <paramref name="clearance"/> from the road.</summary>
        public static bool Standable(HabitatSample s, SpeciesInfo info, float clearance) =>
            s.Valid && !s.Water && s.Slope <= info.MaxSlope && s.RoadDistance >= clearance;

        private static bool InCell(double x, double z, double x0, double z0) => x >= x0 && x < x0 + CellSize && z >= z0 && z < z0 + CellSize;

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
