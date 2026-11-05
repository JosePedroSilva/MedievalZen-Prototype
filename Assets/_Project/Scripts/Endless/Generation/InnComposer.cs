using System;
using System.Collections.Generic;
using MedievalZen.People;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// Dresses the endless road's inns (endless only; the finite world keeps <see cref="SettlementDresser"/>) by archetype
    /// (<see cref="InnStyles"/> picks it): the coaching inn (tavern, stables and a barn round a yard open to the road behind a forecourt),
    /// the wayside alehouse (a large house close to the road, a bench round a big tree, a lean-to, barrels), the farm inn (skewed to
    /// the road down a lane, barn, haystacks, a fenced paddock, kitchen garden and fruit trees), the forest lodge (woodshed, log piles,
    /// a woodcutter's corner, log seats round a fire), the waterside inn (turned toward the lake or stream, a landing with a bench,
    /// reeds and tubs at the edge) and the refuge at a col (a hut with a windbreak arc, a walled pen, or two lean-tos and a cairn row).
    /// Within an archetype the side of the road, the setback, the angle, the main building and the props vary. Exactly one building of
    /// an inn takes its rest point (<see cref="IsRestBuilding"/>): the tavern, the alehouse's house or the refuge's hut. Nothing stands
    /// on the road or its verge (<see cref="SettlementKit"/>). Pure; deterministic from the place's random stream (1291 roadside,
    /// 1292 refuge).
    /// </summary>
    public static class InnComposer
    {
        /// <summary>Randomness stream of a roadside inn's dressing.</summary>
        public const int Stream = 1291;

        /// <summary>Randomness stream of a refuge's dressing.</summary>
        public const int RefugeStream = 1292;

        /// <summary>Front of the tavern (its stair landing) from the road's centre: the road's half width, the verge and a 5.5 m front yard.</summary>
        public const float FrontYard = 5.5f;

        /// <summary>Seat height of the Fantasy Kingdom benches, metres.</summary>
        public const float BenchSeat = 0.45f;

        /// <summary>The house presets that serve as an inn (House_05, House_07, House_01_A, House_09_C: the larger ones).</summary>
        public static readonly int[] InnHouses = { 3, 4, 0, 8 };

        /// <summary>
        /// True for a building that takes an inn's rest point: the tavern, the refuge's hut, the shared dresser's cabin, or a house (an
        /// inn's dressing holds at most one of them; in a village a house is only a house).
        /// </summary>
        public static bool IsRestBuilding(string role) => PropRole.IsInnBuilding(role) || role == PropRole.House;

        /// <summary>Composes a roadside inn as a coaching inn, or the refuge (the archetype-free entry kept for callers without a site).</summary>
        public static bool Compose(SettlementKit kit, bool refuge) =>
            Compose(kit, new InnSite { Archetype = refuge ? InnArchetype.Refuge : InnArchetype.Coaching }, out _);

        /// <summary>
        /// Composes the inn as <paramref name="site"/>'s archetype; when its main building finds no ground, the coaching inn and then the
        /// alehouse are tried (<paramref name="built"/> says which stands). Returns false (nothing placed) when the palette lacks the
        /// tavern (or the hut) or no site fits, so the caller can fall back to the shared dresser.
        /// </summary>
        public static bool Compose(SettlementKit kit, InnSite site, out InnArchetype built)
        {
            built = site.Archetype;
            if (site.Archetype == InnArchetype.Refuge) return ComposeRefuge(kit, site.RefugeVariant);
            if (!kit.Keys.Has(PropRole.Tavern)) return false;
            foreach (InnArchetype a in new[] { site.Archetype, InnArchetype.Coaching, InnArchetype.Alehouse })
            {
                int props = kit.Props.Count;
                bool ok;
                switch (a)
                {
                    case InnArchetype.Alehouse: ok = Alehouse(kit); break;
                    case InnArchetype.Farm: ok = Farm(kit); break;
                    case InnArchetype.ForestLodge: ok = Lodge(kit); break;
                    case InnArchetype.Waterside: ok = Waterside(kit, site); break;
                    case InnArchetype.ForgeInn: ok = ForgeInn(kit); break;
                    default: ok = Coaching(kit); break;
                }

                if (ok)
                {
                    built = a;
                    return true;
                }

                if (kit.Props.Count != props) return false;   // a main building only fails before anything is placed
            }

            return false;
        }

        // ------------------------------------------------------------------ the archetypes

        private static bool Coaching(SettlementKit kit)
        {
            System.Random rng = kit.Rng;
            float setback = 13f + 5f * (float)rng.NextDouble();
            PlacedBuilding inn = Main(kit, PropRole.Tavern, 0, setback, (float)(rng.NextDouble() - 0.5) * 12f, 9f);
            if (inn == null) return false;
            Frame(kit, inn, out BuildingShape s, out Vector3 f, out Vector3 across, out float along, out Vector3 tangent);
            float depth = RoadGap(kit, inn.At(new Vector3(s.ExtentCentre.x, 0f, s.ExtentZ1)));
            Vector3 front = inn.At(new Vector3(s.ExtentCentre.x, 0f, s.ExtentZ1));
            float yardHalf = 8f + 1.5f * (float)rng.NextDouble();
            Vector3 yard = front + f * (depth * 0.5f);
            kit.Yard(yard, Mathf.Min(yardHalf + 1.5f, depth * 0.5f + 3f));
            kit.Yard(front + f * 1.5f, 6f);

            // The wings: the stables on one side, a barn or cart shed on the other, their open fronts to the yard.
            int stableSide = rng.NextDouble() < 0.5 ? 1 : -1;
            int shed = rng.NextDouble() < 0.55 ? 0 : 1;
            PlacedBuilding stables = null, barn = null;
            foreach (int k in new[] { stableSide, -stableSide })
            {
                string role = k == stableSide ? PropRole.Stables : PropRole.Shelter;
                int variant = k == stableSide ? 0 : shed;
                BuildingShape ws = BuildingTable.For(role, variant);
                if (ws == null) continue;
                PlacedBuilding wing = null;
                foreach (float back in new[] { 0f, 2f, -2f, 4f })
                {
                    foreach (float wide in new[] { 0f, 2f, 4f })
                    {
                        float mid = Mathf.Clamp(depth - ws.Width * 0.5f - 1.2f, ws.Width * 0.5f - 2f, 40f) - back;
                        Vector3 at = front + f * mid + across * (k * (yardHalf + wide));
                        wing = kit.TryBuilding(role, variant, at, -across * k, inn.Side);
                        if (wing != null) break;
                    }

                    if (wing != null) break;
                }

                if (k == stableSide) stables = wing;
                else barn = wing;
            }

            Anchors(kit, inn, yard);
            WallBenches(kit, inn);
            if (stables != null)
            {
                kit.Anchor(AnchorKind.Work, stables.At(stables.Shape.Work), ScatterPlanner.Yaw(stables.Facing), inn.Side, stables.PropIndex);
                Vector3 rail = stables.At(new Vector3(stables.Shape.ExtentCentre.x, 0f, stables.Shape.ExtentZ1 + 1.6f));
                Try(kit, PropRole.HorseHitch, 0, rail, SettlementKit.YawAlongX(stables.Across), 0.6f, stables.Facing);
                if (kit.Keys.Count(PropRole.Stump) >= 5) Try(kit, PropRole.Stump, 3, rail + stables.Across * 2.6f, 0f, 0.4f, stables.Facing);   // the mounting block
                Try(kit, PropRole.Haystack, kit.Keys.PickVariant(PropRole.Haystack, rng), stables.At(new Vector3(stables.Shape.ExtentX1 + 2.6f, 0f, stables.Shape.ExtentZ0 + 2.5f)), (float)(rng.NextDouble() * 360.0), 1.9f, stables.Across);
            }

            if (barn != null)
            {
                for (int k = 0; k < 2 + rng.Next(2); k++)
                {
                    Try(kit, PropRole.YardGoods, kit.Keys.PickVariant(PropRole.YardGoods, rng), barn.At(new Vector3(barn.Shape.ExtentX0 + 1.5f + 1.6f * k, 0f, barn.Shape.ExtentZ1 + 1.2f)),
                        (float)(rng.NextDouble() * 360.0), 0.5f, barn.Facing);
                }
            }

            // A cart in the yard, the well at a corner by the tavern, lamps at the yard's mouth.
            Try(kit, PropRole.FarmCart, kit.Keys.PickVariant(PropRole.FarmCart, rng), yard + across * (stableSide * yardHalf * 0.3f) + f * (depth * 0.15f),
                ScatterPlanner.Yaw(f) + 60f + (float)(rng.NextDouble() * 60.0), 1.8f, -across * stableSide);
            Well(kit, inn, front + f * 4f + across * (-stableSide * yardHalf * 0.45f));
            foreach (int k in new[] { -1, 1 })
            {
                kit.TryProp(PropRole.Lamp, kit.Keys.PickVariant(PropRole.Lamp, rng), yard + f * (depth * 0.5f - 1.2f) + across * (k * (yardHalf - 0.8f)), ScatterPlanner.Yaw(-f), 1f, 0.5f);
            }

            // A table with benches on the free side of the yard, sacks and barrels by the tavern's corner.
            Table(kit, front + f * (depth * 0.6f) + across * (-stableSide * yardHalf * 0.55f), f, inn.Side);
            for (int k = 0; k < 2; k++)
            {
                Try(kit, PropRole.YardGoods, kit.Keys.PickVariant(PropRole.YardGoods, rng), inn.At(new Vector3(stableSide > 0 ? s.ExtentX0 - 1.3f : s.ExtentX1 + 1.3f, 0f, -3f - 1.7f * k)),
                    (float)(rng.NextDouble() * 360.0), 0.5f, -across * stableSide);
            }

            // Behind: firewood, the privy.
            Try(kit, PropRole.WoodPile, kit.Keys.PickVariant(PropRole.WoodPile, rng), inn.At(new Vector3(s.ExtentCentre.x + 3f, 0f, s.ExtentZ0 - 2.2f)), SettlementKit.YawAlongX(across), 0.9f, -f);
            Privy(kit, inn, -stableSide);
            Roadside(kit, inn, yard, along, tangent, rng.NextDouble() < 0.7);
            return true;
        }

        private static bool Alehouse(SettlementKit kit)
        {
            System.Random rng = kit.Rng;
            int variant = InnHouses[rng.Next(InnHouses.Length)];
            double r = rng.NextDouble();
            float angle = r < 0.4 ? 0f : r < 0.75 ? (rng.NextDouble() < 0.5 ? -1f : 1f) * (12f + 10f * (float)rng.NextDouble()) : (rng.NextDouble() < 0.5 ? -90f : 90f);
            PlacedBuilding inn = Main(kit, PropRole.House, variant, 0.6f + 3f * (float)rng.NextDouble(), angle, 3f);
            if (inn == null) return false;
            Frame(kit, inn, out BuildingShape s, out Vector3 f, out Vector3 across, out float along, out Vector3 tangent);
            Vector3 porch = inn.Door + f * 1.6f;
            kit.Yard(porch, 4.5f);
            Anchors(kit, inn, porch);
            WallBenches(kit, inn);

            // The big tree on one side with benches round it, the lean-to on the other, barrels by the door.
            int treeSide = rng.NextDouble() < 0.5 ? 1 : -1;
            foreach (int k in new[] { treeSide, -treeSide })
            {
                Vector3 tree = inn.At(new Vector3(k > 0 ? s.ExtentX1 + 5.5f : s.ExtentX0 - 5.5f, 0f, s.ExtentZ1 - 1f));
                if (!kit.Clear(tree, 2.2f) || kit.RoadDistance(tree) < kit.Half + SettlementKit.Verge + 4.5f) continue;
                kit.AddTree(PropRole.MeadowTree, tree);
                kit.Occupancy.Add(tree, 1.0f);
                foreach (float a in new[] { 0f, 180f })
                {
                    Vector3 dir = Turn(f, a + 90f * k);
                    Vector3 at = tree + dir * 1.9f;
                    if (kit.TryProp(PropRole.Bench, 0, at, SettlementKit.YawAlongX(Turn(dir, 90f)), 1f, 0.5f)) kit.Anchor(AnchorKind.Bench, at, ScatterPlanner.Yaw(dir), inn.Side, -1, BenchSeat);
                }

                treeSide = k;
                break;
            }

            BuildingShape ls = BuildingTable.For(PropRole.Shelter, 1);
            if (ls != null)
            {
                float x = treeSide > 0 ? s.ExtentX0 - 3.2f - ls.Width * 0.5f : s.ExtentX1 + 3.2f + ls.Width * 0.5f;
                foreach (float back in new[] { 2f, 4.5f, 7f, 10f })
                {
                    if (kit.TryBuilding(PropRole.Shelter, 1, inn.At(new Vector3(x, 0f, s.ExtentZ1 - back)), f, inn.Side) != null) break;
                    if (kit.TryBuilding(PropRole.Shelter, 1, inn.At(new Vector3(x + (treeSide > 0 ? -2f : 2f), 0f, s.ExtentZ1 - back)), f, inn.Side) != null) break;
                }
            }

            for (int k = 0; k < 3; k++)
            {
                Try(kit, PropRole.YardGoods, kit.Keys.PickVariant(PropRole.YardGoods, rng), inn.Door + across * ((k == 1 ? -1f : 1f) * (1.4f + 0.9f * k)) + f * 0.9f, (float)(rng.NextDouble() * 360.0), 0.45f, f);
            }

            Try(kit, PropRole.Lamp, kit.Keys.PickVariant(PropRole.Lamp, rng), inn.Door + across * -2.4f + f * 1.6f, ScatterPlanner.Yaw(f), 0.4f, f);
            Vector3 table = inn.At(new Vector3(treeSide > 0 ? s.ExtentX1 + 3.2f : s.ExtentX0 - 3.2f, 0f, s.ExtentZ0 + 2.5f));
            Table(kit, table, f, inn.Side);
            if (rng.NextDouble() < 0.5) Privy(kit, inn, -treeSide);
            Roadside(kit, inn, porch, along, tangent, rng.NextDouble() < 0.4);
            return true;
        }

        private static bool Farm(SettlementKit kit)
        {
            System.Random rng = kit.Rng;
            bool tavern = rng.NextDouble() < 0.6;
            int variant = tavern ? 0 : InnHouses[rng.Next(2)];
            float angle = (rng.NextDouble() < 0.5 ? -1f : 1f) * (15f + 15f * (float)rng.NextDouble());
            PlacedBuilding inn = Main(kit, tavern ? PropRole.Tavern : PropRole.House, variant, 9f + 8f * (float)rng.NextDouble(), angle, 6f);
            if (inn == null) return false;
            Frame(kit, inn, out BuildingShape s, out Vector3 f, out Vector3 across, out float along, out Vector3 tangent);
            Vector3 front = inn.At(new Vector3(s.ExtentCentre.x, 0f, s.ExtentZ1 + 3f));
            kit.Yard(front, 7f);
            Anchors(kit, inn, front);
            WallBenches(kit, inn);

            // The barn at right angles beside the inn: the farmyard between them.
            int barnSide = rng.NextDouble() < 0.5 ? 1 : -1;
            PlacedBuilding barn = null;
            BuildingShape bs = BuildingTable.For(PropRole.Shelter, 0);
            foreach (int k in new[] { barnSide, -barnSide })
            {
                if (bs == null) break;
                foreach (float gap in new[] { 6f, 8f, 10f })
                {
                    float x = k > 0 ? s.ExtentX1 + gap : s.ExtentX0 - gap;
                    barn = kit.TryBuilding(PropRole.Shelter, 0, inn.At(new Vector3(x, 0f, s.ExtentZ1 - bs.Width * 0.5f + 2f)), -across * k, inn.Side);
                    if (barn != null) break;
                }

                if (barn != null)
                {
                    barnSide = k;
                    break;
                }
            }

            Vector3 farmyard = inn.At(new Vector3(barnSide > 0 ? s.ExtentX1 + 3f : s.ExtentX0 - 3f, 0f, s.ExtentZ1 - 1f));
            kit.Yard(farmyard, 6f);
            if (barn != null)
            {
                kit.Anchor(AnchorKind.Work, barn.Door + barn.Facing * 1.2f, ScatterPlanner.Yaw(barn.Facing), inn.Side, barn.PropIndex);
                for (int k = 0; k < 2 + rng.Next(2); k++)
                {
                    Try(kit, PropRole.Haystack, kit.Keys.PickVariant(PropRole.Haystack, rng), barn.At(new Vector3(barn.Shape.ExtentX0 - 2.8f, 0f, barn.Shape.ExtentZ0 + 2f + 3.4f * k)), (float)(rng.NextDouble() * 360.0), 1.7f, -barn.Across);
                }
            }

            Try(kit, PropRole.FarmCart, kit.Keys.PickVariant(PropRole.FarmCart, rng), farmyard + f * 3f, ScatterPlanner.Yaw(across) + (float)(rng.NextDouble() * 40.0), 1.8f, f);
            Well(kit, inn, farmyard - across * barnSide * 1.5f + f * 4.5f);

            // The paddock on the far side, the kitchen garden and the fruit trees behind.
            Vector3 paddock = inn.At(new Vector3(barnSide > 0 ? s.ExtentX0 - 9f : s.ExtentX1 + 9f, 0f, s.ExtentCentre.z - 1f));
            Paddock(kit, paddock, across, f, 3, 2);
            Vector3 garden = inn.At(new Vector3(s.ExtentCentre.x, 0f, s.ExtentZ0 - 4.5f));
            Garden(kit, garden, across, f, 4, 2);
            Fence(kit, garden - f * 2.3f, across, 3);
            for (int k = 0; k < 3; k++)
            {
                Vector3 tree = garden - f * (6f + 2f * (float)rng.NextDouble()) + across * ((k - 1) * 6f + (float)(rng.NextDouble() - 0.5) * 1.5f);
                if (kit.Clear(tree, 1.6f) && kit.AddTree(PropRole.FruitTree, tree)) kit.Occupancy.Add(tree, 0.8f);
            }

            Try(kit, PropRole.WoodPile, kit.Keys.PickVariant(PropRole.WoodPile, rng), inn.At(new Vector3(barnSide > 0 ? s.ExtentX0 - 1.6f : s.ExtentX1 + 1.6f, 0f, s.ExtentZ0 + 1.5f)), ScatterPlanner.Yaw(across), 0.9f, -across * barnSide);
            Privy(kit, inn, -barnSide);
            Roadside(kit, inn, front, along, tangent, rng.NextDouble() < 0.3);
            return true;
        }

        private static bool Lodge(SettlementKit kit)
        {
            System.Random rng = kit.Rng;
            bool tavern = rng.NextDouble() < 0.55;
            int variant = tavern ? 0 : (rng.NextDouble() < 0.5 ? 4 : 0);
            PlacedBuilding inn = Main(kit, tavern ? PropRole.Tavern : PropRole.House, variant, 5f + 9f * (float)rng.NextDouble(), (float)(rng.NextDouble() - 0.5) * 50f, 6f);
            if (inn == null) return false;
            Frame(kit, inn, out BuildingShape s, out Vector3 f, out Vector3 across, out float along, out Vector3 tangent);
            Vector3 front = inn.At(new Vector3(s.ExtentCentre.x, 0f, s.ExtentZ1 + 3f));
            kit.Yard(front, 7.5f);
            Anchors(kit, inn, front);
            WallBenches(kit, inn);

            // The woodshed beside the lodge with log piles along it; the woodcutter's corner beyond.
            int shedSide = rng.NextDouble() < 0.5 ? 1 : -1;
            PlacedBuilding shed = null;
            BuildingShape ss = BuildingTable.For(PropRole.Shelter, 1);
            foreach (int k in new[] { shedSide, -shedSide })
            {
                if (ss == null) break;
                float x = k > 0 ? s.ExtentX1 + 3f + ss.Width * 0.5f : s.ExtentX0 - 3f - ss.Width * 0.5f;
                foreach (float back in new[] { 1f, 4f, -1f })
                {
                    shed = kit.TryBuilding(PropRole.Shelter, 1, inn.At(new Vector3(x, 0f, s.ExtentZ1 - back)), f, inn.Side);
                    if (shed != null) break;
                }

                if (shed != null)
                {
                    shedSide = k;
                    break;
                }
            }

            Vector3 corner = inn.At(new Vector3(shedSide > 0 ? s.ExtentX1 + 4f : s.ExtentX0 - 4f, 0f, s.ExtentZ0 - 3f));
            if (shed != null)
            {
                for (int k = 0; k < 3; k++) Try(kit, PropRole.WoodPile, kit.Keys.PickVariant(PropRole.WoodPile, rng), shed.At(new Vector3(shed.Shape.ExtentX0 + 1.6f + 2.6f * k, 0f, shed.Shape.ExtentZ0 - 1.4f)), SettlementKit.YawAlongX(shed.Across), 0.9f, -shed.Facing);
                corner = shed.At(new Vector3(shedSide > 0 ? shed.Shape.ExtentX1 + 3.5f : shed.Shape.ExtentX0 - 3.5f, 0f, shed.Shape.ExtentCentre.z));
            }

            if (kit.Keys.Count(PropRole.Stump) >= 5 && kit.TryProp(PropRole.Stump, 3, corner, 0f, 1f, 0.6f)) kit.Anchor(AnchorKind.Work, corner + f * 1.1f, ScatterPlanner.Yaw(-f), inn.Side);
            kit.TryProp(PropRole.WoodPile, corner + across * 2.4f, ScatterPlanner.Yaw(f), 1.0f);
            kit.TryProp(PropRole.WoodPile, corner - across * 2.4f, ScatterPlanner.Yaw(f) + 15f, 1.0f);
            kit.TryProp(PropRole.TanningRack, corner - f * 2.8f, SettlementKit.YawAlongX(across), 1.0f);
            kit.TryProp(PropRole.FarmCart, kit.Keys.PickVariant(PropRole.FarmCart, rng), corner + f * 3.5f, ScatterPlanner.Yaw(across) + 20f, 1f, 2.2f);

            // The fire on the other side, log seats and a bench round it.
            Vector3 fire = inn.At(new Vector3(shedSide > 0 ? s.ExtentX0 - 5f : s.ExtentX1 + 5f, 0f, s.ExtentZ1 - 2f));
            if (kit.TryProp(PropRole.Fireplace, fire, 0f, 1.5f))
            {
                kit.Force(PropRole.FireTripod, fire, (float)(rng.NextDouble() * 360.0));
                kit.Yard(fire, 4f);
                Vector3 bench = fire + f * 2.6f;
                if (kit.TryProp(PropRole.Bench, 2, bench, SettlementKit.YawAlongX(across), 1f, 0.9f)) kit.Anchor(AnchorKind.Bench, bench, ScatterPlanner.Yaw(-f), inn.Side, -1, BenchSeat);
                if (kit.Keys.Count(PropRole.Stump) >= 5)
                {
                    foreach (float a in new[] { 100f, 180f, 260f }) kit.TryProp(PropRole.Stump, 3 + rng.Next(2), fire + Turn(f, a) * 2.2f, (float)(rng.NextDouble() * 360.0), 1f, 0.5f);
                }
            }

            Try(kit, PropRole.Lamp, kit.Keys.PickVariant(PropRole.Lamp, rng), inn.Door + across * 2.6f + f * 1.4f, ScatterPlanner.Yaw(f), 0.4f, f);
            if (rng.NextDouble() < 0.6) Privy(kit, inn, -shedSide);
            Roadside(kit, inn, front, along, tangent, rng.NextDouble() < 0.5);
            return true;
        }

        private static bool Waterside(SettlementKit kit, InnSite site)
        {
            System.Random rng = kit.Rng;
            if (site.WaterSide == 0) return false;
            bool tavern = rng.NextDouble() < 0.55;
            int variant = tavern ? 0 : InnHouses[rng.Next(3)];
            int ws = site.WaterSide;
            // Gable-on to the road with its front turned toward the water (the angle measured from the road's normal).
            float turn = (rng.NextDouble() < 0.5 ? -1f : 1f) * (55f + 20f * (float)rng.NextDouble());
            float room = site.WaterAcross - kit.Half - SettlementKit.Verge;
            float setback = Mathf.Clamp(room - 20f, 1.5f, 12f);
            PlacedBuilding inn = Main(kit, tavern ? PropRole.Tavern : PropRole.House, variant, setback, turn, 4f, ws);
            if (inn == null) return false;
            Frame(kit, inn, out BuildingShape s, out Vector3 f, out Vector3 across, out float along, out Vector3 tangent);
            Vector3 front = inn.At(new Vector3(s.ExtentCentre.x, 0f, s.ExtentZ1 + 2.5f));
            kit.Yard(front, 6f);
            Anchors(kit, inn, front);
            WallBenches(kit, inn);

            // The landing: a few metres short of the water across from the inn, flat stones, a bench looking out, tubs, reeds.
            Vector3 n = SettlementKit.Right(tangent) * inn.Side;
            float a0 = kit.AlongOf(inn.At(s.ExtentCentre));
            Vector3 landing = Vector3.zero;
            bool landed = false;
            foreach (float back in new[] { 3.5f, 5f, 7f, 9f })
            {
                foreach (float d in new[] { 0f, 8f, -8f })
                {
                    Vector3 at = kit.Beside(a0 + d, inn.Side, site.WaterAcross - back);
                    if (!kit.Clear(at, 1.4f)) continue;
                    landing = at;
                    landed = true;
                    break;
                }

                if (landed) break;
            }

            if (landed)
            {
                kit.Yard(landing, 3.5f);
                kit.Path(front, landing);
                kit.Anchor(AnchorKind.Work, landing, ScatterPlanner.Yaw(n), inn.Side);
                Vector3 bench = landing - Turn(n, 90f) * 2.4f;
                if (kit.TryProp(PropRole.Bench, 0, bench, SettlementKit.YawAlongX(Turn(n, 90f)), 1f, 0.6f)) kit.Anchor(AnchorKind.Bench, bench, ScatterPlanner.Yaw(n), inn.Side, -1, BenchSeat);
                for (int k = 0; k < 2; k++) kit.TryProp(PropRole.YardGoods, kit.Keys.PickVariant(PropRole.YardGoods, rng), landing + Turn(n, 90f) * (1.8f + 1.1f * k), (float)(rng.NextDouble() * 360.0), 1f, 0.5f);
                for (int k = 0; k < 6; k++)
                {
                    Vector3 reed = landing + n * (2.2f + 1.5f * (float)rng.NextDouble()) + Turn(n, 90f) * ((k - 2.5f) * 2.6f + (float)(rng.NextDouble() - 0.5));
                    kit.TryProp(PropRole.Reeds, kit.Keys.PickVariant(PropRole.Reeds, rng), reed, (float)(rng.NextDouble() * 360.0), 0.9f + 0.3f * (float)rng.NextDouble(), 0.4f);
                }
            }

            // The terrace on the water side of the inn: a table with benches; a willow-like tree by it.
            Vector3 terrace = inn.At(new Vector3(s.ExtentCentre.x, 0f, s.ExtentZ1 + 1f)) + n * 6f;
            Table(kit, terrace, Turn(n, 90f), inn.Side);
            Vector3 tree = terrace + n * 5f + Turn(n, 90f) * 4f;
            if (kit.Clear(tree, 2f) && kit.AddTree(PropRole.MeadowTree, tree)) kit.Occupancy.Add(tree, 1.0f);
            Try(kit, PropRole.Lamp, kit.Keys.PickVariant(PropRole.Lamp, rng), inn.Door + across * 2.4f + f * 1.4f, ScatterPlanner.Yaw(f), 0.4f, f);
            for (int k = 0; k < 2; k++)
            {
                kit.TryProp(PropRole.YardGoods, kit.Keys.PickVariant(PropRole.YardGoods, rng), inn.At(new Vector3(s.ExtentX0 - 1.2f, 0f, s.ExtentZ1 - 2f - 1.7f * k)), (float)(rng.NextDouble() * 360.0), 1f, 0.7f);
            }

            Try(kit, PropRole.WoodPile, kit.Keys.PickVariant(PropRole.WoodPile, rng), inn.At(new Vector3(s.ExtentCentre.x + 2f, 0f, s.ExtentZ0 - 2.2f)), SettlementKit.YawAlongX(across), 0.9f, -f);
            Privy(kit, inn, rng.NextDouble() < 0.5 ? 1 : -1);
            Roadside(kit, inn, front, along, tangent, rng.NextDouble() < 0.5);
            return true;
        }

        private static bool ForgeInn(SettlementKit kit)
        {
            System.Random rng = kit.Rng;
            bool tavern = rng.NextDouble() < 0.6;
            int variant = tavern ? 0 : InnHouses[rng.Next(InnHouses.Length)];
            PlacedBuilding inn = Main(kit, tavern ? PropRole.Tavern : PropRole.House, variant, 2f + 6f * (float)rng.NextDouble(), (float)(rng.NextDouble() - 0.5) * 16f, 4f);
            if (inn == null) return false;
            Frame(kit, inn, out BuildingShape s, out Vector3 f, out Vector3 across, out float along, out Vector3 tangent);
            Vector3 front = inn.At(new Vector3(s.ExtentCentre.x, 0f, s.ExtentZ1 + 2.5f));
            kit.Yard(front, 6.5f);
            Anchors(kit, inn, front);
            WallBenches(kit, inn);

            // The forge across the road, a little along it, its open workshop to the road; the hitching rail and a cart before it.
            PlacedBuilding forge = null;
            float shift = (rng.NextDouble() < 0.5 ? -1f : 1f) * (8f + 6f * (float)rng.NextDouble());
            foreach (float d in new[] { shift, -shift, shift * 1.6f, 0f })
            {
                foreach (float back in new[] { 2.5f, 5f, 8f })
                {
                    kit.Frame(along + d, out Vector3 on, out Vector3 t);
                    Vector3 n = SettlementKit.Right(t) * -inn.Side;
                    forge = kit.TryBuilding(PropRole.Smithy, 0, on + n * (kit.Half + SettlementKit.Verge + back), -n, -inn.Side);
                    if (forge != null) break;
                }

                if (forge != null) break;
            }

            if (forge != null)
            {
                Vector3 apron = forge.At(new Vector3(forge.Shape.ExtentCentre.x, 0f, forge.Shape.ExtentZ1 + 1.5f));
                kit.Yard(apron, 5f);
                // The smith keeps to the forge's side of the road (the planner's rounds stay on an anchor's side).
                kit.Anchor(AnchorKind.Work, forge.At(forge.Shape.Work), ScatterPlanner.Yaw(forge.Facing), -inn.Side, forge.PropIndex);
                for (int k = 0; k < 2 && kit.Keys.Has(PropRole.SmithyTool); k++)
                {
                    Try(kit, PropRole.SmithyTool, k, forge.At(new Vector3(-2.8f + 2.6f * k, 0f, forge.Shape.ExtentZ1 + 1.2f)), forge.Yaw, 0.6f, forge.Facing);
                }

                Try(kit, PropRole.FarmCart, kit.Keys.PickVariant(PropRole.FarmCart, rng), forge.At(new Vector3(forge.Shape.ExtentX1 + 3f, 0f, forge.Shape.ExtentZ1 - 2f)), forge.Yaw + 80f, 1.8f, forge.Across);
                Try(kit, PropRole.WoodPile, kit.Keys.PickVariant(PropRole.WoodPile, rng), forge.At(new Vector3(forge.Shape.ExtentX0 - 1.6f, 0f, forge.Shape.ExtentCentre.z)), forge.Yaw, 0.9f, -forge.Across);
                kit.TryProp(PropRole.HorseHitch, 0, kit.Beside(kit.AlongOf(apron) + 6f, -inn.Side, kit.Half + SettlementKit.Verge + 1.3f), SettlementKit.YawAlongX(tangent), 1f, 0.7f);
            }

            // The inn's side: a well, barrels by the door, a lamp, a table under the eaves, the stable shed behind.
            int k2 = rng.NextDouble() < 0.5 ? 1 : -1;
            Well(kit, inn, inn.At(new Vector3(k2 > 0 ? s.ExtentX1 + 3.5f : s.ExtentX0 - 3.5f, 0f, s.ExtentZ1 - 1f)));
            for (int k = 0; k < 3; k++)
            {
                Try(kit, PropRole.YardGoods, kit.Keys.PickVariant(PropRole.YardGoods, rng), inn.Door + across * ((k == 1 ? -1f : 1f) * (1.5f + 0.9f * k)) + f * 0.9f, (float)(rng.NextDouble() * 360.0), 0.45f, f);
            }

            Try(kit, PropRole.Lamp, kit.Keys.PickVariant(PropRole.Lamp, rng), inn.Door + across * 2.6f + f * 1.6f, ScatterPlanner.Yaw(f), 0.4f, f);
            Table(kit, inn.At(new Vector3(k2 > 0 ? s.ExtentX0 - 3.2f : s.ExtentX1 + 3.2f, 0f, s.ExtentZ1 - 3f)), f, inn.Side);
            BuildingShape ls = BuildingTable.For(PropRole.Shelter, 1);
            if (ls != null)
            {
                foreach (float back in new[] { 3f, 6f })
                {
                    float x = k2 > 0 ? s.ExtentX1 + 3.5f + ls.Width * 0.5f : s.ExtentX0 - 3.5f - ls.Width * 0.5f;
                    if (kit.TryBuilding(PropRole.Shelter, 1, inn.At(new Vector3(x, 0f, s.ExtentZ0 + back)), f, inn.Side) != null) break;
                }
            }

            Privy(kit, inn, -k2);
            Roadside(kit, inn, front, along, tangent, rng.NextDouble() < 0.4);
            return true;
        }

        // ------------------------------------------------------------------ shared pieces

        /// <summary>
        /// Places the main building with the middle of its front <paramref name="setback"/> metres beyond the verge, its front turned
        /// <paramref name="angle"/> degrees from facing the road; tried on <paramref name="side"/> (0: a random side) then the other, at
        /// shifts along the road up to <paramref name="spread"/> metres and with the setback a little deeper, then shallower.
        /// </summary>
        private static PlacedBuilding Main(SettlementKit kit, string role, int variant, float setback, float angle, float spread, int side = 0)
        {
            System.Random rng = kit.Rng;
            int first = side != 0 ? side : rng.NextDouble() < 0.5 ? -1 : 1;
            float a0 = (float)(rng.NextDouble() - 0.5) * spread;
            foreach (int k in new[] { first, -first })
            {
                foreach (float extra in new[] { 0f, 3f, -3f, 6f })
                {
                    foreach (float shift in new[] { 0f, 6f, -6f, 12f, -12f })
                    {
                        float back = Mathf.Max(0.3f, setback + extra);
                        kit.Frame(a0 + shift, out Vector3 on, out Vector3 t);
                        Vector3 n = SettlementKit.Right(t) * k;
                        PlacedBuilding b = kit.TryBuilding(role, variant, on + n * (kit.Half + SettlementKit.Verge + back), Turn(-n, angle), k);
                        if (b != null) return b;
                    }
                }
            }

            return null;
        }

        /// <summary>A prop placed like <c>SettlementKit.TryProp</c>, moved up to three times a metre along <paramref name="away"/> (off a wall) until it fits.</summary>
        private static bool Try(SettlementKit kit, string role, int variant, Vector3 at, float yaw, float radius, Vector3 away, float scale = 1f)
        {
            for (int k = 0; k < 4; k++)
            {
                if (kit.TryProp(role, variant, at + away * k, yaw, scale, radius)) return true;
            }

            return false;
        }

        private static void Frame(SettlementKit kit, PlacedBuilding b, out BuildingShape s, out Vector3 f, out Vector3 across, out float along, out Vector3 tangent)
        {
            s = b.Shape;
            f = b.Facing;
            across = b.Across;
            along = kit.AlongOf(b.At(s.ExtentCentre));
            kit.Frame(along, out _, out tangent);
        }

        /// <summary>Distance from a point to the verge's outer line, metres.</summary>
        private static float RoadGap(SettlementKit kit, Vector3 at) => kit.RoadDistance(at) - kit.Half - SettlementKit.Verge;

        /// <summary>The direction <paramref name="v"/> turned <paramref name="degrees"/> about the vertical.</summary>
        public static Vector3 Turn(Vector3 v, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), sn = Mathf.Sin(a);
            return new Vector3(v.x * c + v.z * sn, 0f, -v.x * sn + v.z * c);
        }

        /// <summary>The door (the rest point), the porch's work spot, the gate at the verge and the worn way from the door through <paramref name="via"/> to it.</summary>
        private static void Anchors(SettlementKit kit, PlacedBuilding inn, Vector3 via)
        {
            float yaw = ScatterPlanner.Yaw(inn.Facing);
            kit.Anchor(AnchorKind.InnDoor, inn.Door, yaw, inn.Side, inn.PropIndex);
            Vector3 work = inn.Shape.Work != Vector3.zero ? inn.At(inn.Shape.Work) : inn.Door + inn.Facing * 1.4f + inn.Across * 1.2f;
            kit.Anchor(AnchorKind.Work, work, yaw, inn.Side, inn.PropIndex);
            Vector3 gate = kit.GateFor(via, kit.AlongOf(via), inn.Side);
            kit.Path(inn.Door, via);
            kit.Path(via, gate);
            kit.Anchor(AnchorKind.Gate, gate, ScatterPlanner.Yaw(-inn.Facing), inn.Side);
        }

        /// <summary>Two benches against the main building's front, facing out (the tavern's under its balcony, clear of the stair).</summary>
        private static void WallBenches(SettlementKit kit, PlacedBuilding inn)
        {
            BuildingShape s = inn.Shape;
            float wallYaw = SettlementKit.YawAlongX(inn.Across), yaw = ScatterPlanner.Yaw(inn.Facing);
            float[] xs = inn.Role == PropRole.Tavern ? new[] { -6.0f, -1.0f } : new[] { s.Door.x - 2.4f, s.Door.x + 2.4f };
            float z = inn.Role == PropRole.Tavern ? -1.45f : s.ExtentZ1 + 0.45f;
            foreach (float x in xs)
            {
                if (x < s.BodyX0 + 0.6f || x > s.BodyX1 - 0.6f) continue;
                Vector3 at = inn.At(new Vector3(x, 0f, z));
                if (inn.Role != PropRole.Tavern && !kit.Clear(at, 0.4f)) continue;
                int variant = kit.Rng.Next(Math.Min(3, kit.Keys.Count(PropRole.Bench)));
                if (kit.ForceVariant(PropRole.Bench, variant, at, wallYaw, inn.Role == PropRole.Tavern ? 0f : 0.5f)) kit.Anchor(AnchorKind.Bench, at, yaw, inn.Side, -1, BenchSeat);
            }
        }

        private static void Well(SettlementKit kit, PlacedBuilding inn, Vector3 at)
        {
            foreach (Vector3 d in new[] { Vector3.zero, inn.Facing * 2f, -inn.Facing * 2f, inn.Across * 2.5f, -inn.Across * 2.5f, inn.Facing * 4f })
            {
                if (!kit.TryProp(PropRole.Well, at + d, 0f, 1.8f)) continue;
                kit.Yard(at + d, 3.2f);
                kit.Anchor(AnchorKind.Well, at + d + inn.Facing * 1.8f, ScatterPlanner.Yaw(-inn.Facing), inn.Side);
                return;
            }
        }

        private static void Table(SettlementKit kit, Vector3 at, Vector3 runs, int side)
        {
            float yaw = SettlementKit.YawAlongX(runs);
            if (!kit.TryProp(PropRole.Table, at, yaw, 1.6f)) return;
            Vector3 n = Turn(runs, 90f);
            foreach (float k in new[] { -1f, 1f })
            {
                Vector3 b = at + n * (k * 1.25f);
                if (kit.ForceVariant(PropRole.Bench, 1, b, yaw)) kit.Anchor(AnchorKind.Bench, b, ScatterPlanner.Yaw(-n * k), side, -1, BenchSeat);
            }
        }

        private static void Privy(SettlementKit kit, PlacedBuilding inn, int k)
        {
            BuildingShape s = inn.Shape;
            float x = k > 0 ? s.ExtentX1 + 2.5f : s.ExtentX0 - 2.5f;
            foreach (float back in new[] { 4f, 8f, 1f })
            {
                if (kit.TryBuilding(PropRole.Outhouse, 0, inn.At(new Vector3(x, 0f, s.ExtentZ0 - back)), inn.Across * -k, inn.Side) != null) return;
            }
        }

        /// <summary>A fenced paddock: <paramref name="wide"/> fence pieces along <paramref name="across"/> and <paramref name="deep"/> along <paramref name="f"/>, posts at the ends.</summary>
        private static void Paddock(SettlementKit kit, Vector3 centre, Vector3 across, Vector3 f, int wide, int deep)
        {
            float length = Mathf.Max(0.5f, kit.Keys.fenceLength);
            float hw = wide * length * 0.5f, hd = deep * length * 0.5f;
            if (!kit.Clear(centre, Mathf.Min(hw, hd))) return;
            Fence(kit, centre + f * hd, across, wide);
            Fence(kit, centre - f * hd, across, wide);
            Fence(kit, centre + across * hw, f, deep);
            Fence(kit, centre - across * hw, f, deep);
            kit.Occupancy.Add(centre, Mathf.Min(hw, hd));
            kit.Yard(centre, Mathf.Min(hw, hd));
        }

        /// <summary>The fingerpost and the old sign at the verge, a hitching rail, and last the inn's name board.</summary>
        private static void Roadside(SettlementKit kit, PlacedBuilding inn, Vector3 yard, float along, Vector3 tangent, bool hitch)
        {
            System.Random rng = kit.Rng;
            float yardAlong = kit.AlongOf(yard);
            float edge = kit.Half + SettlementKit.Verge;
            foreach (float d in new[] { 7f, -7f, 11f, -11f })
            {
                Vector3 at = kit.Beside(yardAlong + d, inn.Side, edge + 0.7f);
                if (kit.TryProp(PropRole.Fingerpost, kit.Keys.PickVariant(PropRole.Fingerpost, rng), at, ScatterPlanner.Yaw(tangent) + 90f * inn.Side, 1f, 0.5f)) break;
            }

            if (hitch)
            {
                foreach (float d in new[] { 9f, -9f, 14f, -14f })
                {
                    if (kit.TryProp(PropRole.HorseHitch, 0, kit.Beside(yardAlong + d, inn.Side, edge + 1.3f), SettlementKit.YawAlongX(tangent), 1f, 0.7f)) break;
                }
            }

            // Last, so nothing above moves: the inn's name board by the road (see InnSign).
            PlaceNameBoard(kit, along, inn.Side, tangent);
        }

        /// <summary>
        /// The inn's name board (<see cref="PropRole.InnSign"/>, built in code on the Unity side with the inn's name on both faces): a board
        /// hanging from a frame beside the road, its faces across the road so a traveller reads it while walking up from either way. Its
        /// footprint (<see cref="InnSign.FootprintRadius"/> around its middle) keeps off the road and its verge. Tried beside the tavern,
        /// then a little further along the road on either side of it. Yaw: the board's faces look along <paramref name="tangent"/>.
        /// </summary>
        public static bool PlaceNameBoard(SettlementKit kit, float along, int roadSide, Vector3 tangent)
        {
            foreach (float d in InnSign.Along)
            {
                Vector3 at = kit.Beside(along + d, roadSide, kit.Half + SettlementKit.Verge + InnSign.FromVerge);
                kit.Frame(along + d, out _, out Vector3 t);
                if (kit.TryBuilt(PropRole.InnSign, at, ScatterPlanner.Yaw(t), InnSign.FootprintRadius)) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ the refuge at a col

        private static bool ComposeRefuge(SettlementKit kit, int layout)
        {
            if (!kit.Keys.Has(PropRole.Hut)) return false;
            System.Random rng = kit.Rng;
            int firstSide = rng.NextDouble() < 0.5 ? -1 : 1;
            int variant = kit.Keys.PickVariant(PropRole.Hut, rng);
            PlacedBuilding hut = null;
            foreach (int side in new[] { firstSide, -firstSide })
            {
                foreach (float shift in new[] { 0f, 6f, -6f, 12f, -12f, 18f, -18f })
                {
                    kit.Frame(shift, out Vector3 on, out Vector3 t);
                    Vector3 n = SettlementKit.Right(t) * side;
                    hut = kit.TryBuilding(PropRole.Hut, variant, on + n * (kit.Half + SettlementKit.Verge + 4f + 3f * layout), -n, side);
                    if (hut != null) break;
                }

                if (hut != null) break;
            }

            if (hut == null) return false;
            BuildingShape s = hut.Shape;
            Vector3 f = hut.Facing, across = hut.Across;
            int roadSide = hut.Side;
            float frontYaw = ScatterPlanner.Yaw(f);
            float along = kit.AlongOf(hut.At(s.ExtentCentre));
            kit.Frame(along, out _, out Vector3 tangent);
            Vector3 centre = hut.At(s.BodyCentre);
            float halfDepth = s.Depth * 0.5f, halfWidth = s.Width * 0.5f;

            kit.Anchor(AnchorKind.InnDoor, hut.Door, frontYaw, roadSide, hut.PropIndex);
            Vector3 gate = kit.GateFor(hut.Door, kit.AlongOf(hut.Door), roadSide);
            kit.Path(hut.Door, gate);
            kit.Yard(centre + f * (halfDepth + 2.5f), 6f);
            kit.Anchor(AnchorKind.Gate, gate, ScatterPlanner.Yaw(-f), roadSide);

            // A lean-to shelter beside the hut.
            int shelterSide = rng.NextDouble() < 0.5 ? 1 : -1;
            PlacedBuilding shelter = null;
            if (kit.Keys.Has(PropRole.Shelter))
            {
                BuildingShape ss = BuildingTable.For(PropRole.Shelter, 1);
                foreach (int k in new[] { shelterSide, -shelterSide })
                {
                    if (ss == null) break;
                    Vector3 front = centre + across * k * (halfWidth + 2.5f + ss.Width * 0.5f) + f * (halfDepth - 1.5f);
                    shelter = kit.TryBuilding(PropRole.Shelter, 1, front, f, roadSide);
                    if (shelter != null)
                    {
                        shelterSide = k;
                        break;
                    }
                }
            }

            // The fire with its tripod beside the hut on the side away from the shelter, a bench and a log by it, a lantern by the door.
            Vector3 fire = centre - across * shelterSide * (halfWidth + 4.2f) + f * (halfDepth - 2.5f);
            if (kit.TryProp(PropRole.Fireplace, fire, 0f, 1.5f))
            {
                kit.Force(PropRole.FireTripod, fire, (float)(rng.NextDouble() * 360.0));
                Vector3 bench = fire - f * 2.7f;
                if (kit.TryProp(PropRole.Bench, 2, bench, SettlementKit.YawAlongX(across), 1f, 1.0f)) kit.Anchor(AnchorKind.Bench, bench, ScatterPlanner.Yaw(f), roadSide, -1, BenchSeat);
                if (kit.Keys.Count(PropRole.Stump) >= 5) kit.TryProp(PropRole.Stump, 3, fire - across * shelterSide * 2.1f, ScatterPlanner.Yaw(f), 1f, 0.6f);
            }

            kit.TryProp(PropRole.Lamp, 0, hut.Door + across * 1.6f + f * 0.4f, frontYaw, 1f, 0.45f);
            kit.Anchor(AnchorKind.Work, hut.Door + f * 1.4f, frontYaw, roadSide, hut.PropIndex);

            // The windbreak: a dry-stone wall arc behind the hut (after the shelter and the fire, which it must not block).
            if (layout == 0 && kit.Keys.Has(PropRole.StoneWall))
            {
                float length = Mathf.Max(0.5f, kit.Keys.wallLength);
                float radius = Mathf.Max(halfDepth, halfWidth) + 3.5f;
                float step = length / radius;
                float back = Mathf.Atan2(-f.z, -f.x);
                const int Pieces = 3;
                for (int k = 0; k < Pieces; k++)
                {
                    float a = back + (k - (Pieces - 1) * 0.5f) * step;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var runs = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                    bool end = k == 0 || k == Pieces - 1;
                    string piece = end && kit.Keys.Has(PropRole.StoneWallSmall) ? PropRole.StoneWallSmall : PropRole.StoneWall;
                    Vector3 at = centre + dir * radius;
                    if (kit.TryProp(piece, 0, at, ScatterPlanner.Yaw(runs), 1f, length * 0.4f) && end)
                    {
                        kit.Force(PropRole.StoneWallPillar, at + runs * ((k == 0 ? -1f : 1f) * length * 0.5f), ScatterPlanner.Yaw(runs));
                    }
                }
            }

            // Layout 1: a walled pen for the pack animals on the side away from the shelter; layout 2: a second lean-to and a row of cairns.
            if (layout == 1 && kit.Keys.Has(PropRole.StoneWall))
            {
                float length = Mathf.Max(0.5f, kit.Keys.wallLength);
                Vector3 pen = centre + across * shelterSide * (halfWidth + 5f + length) - f * (halfDepth * 0.5f);
                foreach (int k in new[] { -1, 1 })
                {
                    kit.TryProp(PropRole.StoneWall, 0, pen + across * (k * length), SettlementKit.YawAlongX(f), 1f, length * 0.4f);
                    kit.TryProp(PropRole.StoneWall, 0, pen - f * length + across * (k * length * 0.5f), SettlementKit.YawAlongX(across), 1f, length * 0.4f);
                }

                kit.Force(PropRole.StoneWallPillar, pen + across * length - f * length, 0f);
                kit.Force(PropRole.StoneWallPillar, pen - across * length - f * length, 0f);
                kit.Yard(pen, length);
            }
            else if (layout == 2)
            {
                BuildingShape ss = BuildingTable.For(PropRole.Shelter, 1);
                if (ss != null && kit.Keys.Has(PropRole.Shelter))
                {
                    kit.TryBuilding(PropRole.Shelter, 1, centre - across * shelterSide * (halfWidth + 2.5f + ss.Width * 0.5f) + f * (halfDepth - 4f) - f * 5f, f, roadSide);
                }

                for (int k = 0; k < 3; k++)
                {
                    kit.TryRock(PropRole.StoneStack, centre - f * (halfDepth + 6f + 5f * k) + across * ((float)(rng.NextDouble() - 0.5) * 3f), (float)(rng.NextDouble() * 360.0), 0.5f, 1.1f);
                }
            }

            // Firewood stacked against the hut, a few goods by the door.
            kit.TryProp(PropRole.WoodPile, centre - across * shelterSide * (halfWidth + 1.9f) - f * 1.5f, ScatterPlanner.Yaw(f), 0.8f);
            for (int k = 0; k < 2; k++)
            {
                kit.TryProp(PropRole.YardGoods, kit.Keys.PickVariant(PropRole.YardGoods, rng), hut.Door - across * (1.6f + 1.2f * k) + f * 0.6f, (float)(rng.NextDouble() * 360.0), 1f, 0.7f);
            }

            // The hitch and the sign by the road; a cairn across it.
            foreach (float d in new[] { 9f, -9f, 14f, -14f })
            {
                if (kit.TryProp(PropRole.HorseHitch, 0, kit.Beside(along + d, roadSide, kit.Half + SettlementKit.Verge + 1.3f), SettlementKit.YawAlongX(tangent), 1f, 0.7f)) break;
            }

            foreach (float d in new[] { 6f, -6f, 11f })
            {
                if (kit.TryProp(PropRole.Sign, kit.Beside(along + d, roadSide, kit.Half + SettlementKit.Verge + 0.9f), ScatterPlanner.Yaw(-f), 0.6f)) break;
            }

            foreach (float d in new[] { 4f, -4f, 10f, -10f })
            {
                if (kit.TryRock(PropRole.StoneStack, kit.Beside(along + d, -roadSide, kit.Half + SettlementKit.Verge + 1.6f), (float)(rng.NextDouble() * 360.0), 0.6f, 1.3f)) break;
            }

            return true;
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>A kitchen garden: <paramref name="cols"/> x <paramref name="rows"/> cabbages and pumpkin plants 1.3 m apart round <paramref name="centre"/>.</summary>
        public static int Garden(SettlementKit kit, Vector3 centre, Vector3 across, Vector3 forward, int cols, int rows)
        {
            int placed = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Vector3 at = centre + across * ((c - (cols - 1) * 0.5f) * 1.3f) + forward * ((r - (rows - 1) * 0.5f) * 1.4f);
                    at += new Vector3((float)(kit.Rng.NextDouble() - 0.5) * 0.25f, 0f, (float)(kit.Rng.NextDouble() - 0.5) * 0.25f);
                    if (kit.TryProp(PropRole.GardenPlant, kit.Keys.PickVariant(PropRole.GardenPlant, kit.Rng), at, (float)(kit.Rng.NextDouble() * 360.0), 0.9f + 0.2f * (float)kit.Rng.NextDouble(), 0.55f)) placed++;
                }
            }

            return placed;
        }

        /// <summary>A run of <paramref name="pieces"/> fence pieces centred on <paramref name="centre"/> along <paramref name="runs"/>, with a post at each end.</summary>
        public static int Fence(SettlementKit kit, Vector3 centre, Vector3 runs, int pieces)
        {
            if (!kit.Keys.Has(PropRole.Fence)) return 0;
            float length = Mathf.Max(0.5f, kit.Keys.fenceLength);
            Vector3 start = centre - runs * (length * pieces * 0.5f);
            int variant = kit.Keys.PickVariant(PropRole.Fence, kit.Rng);
            int placed = 0;
            float yaw = ScatterPlanner.Yaw(runs);
            for (int i = 0; i < pieces; i++)
            {
                if (kit.TryProp(PropRole.Fence, variant, start + runs * ((i + 0.5f) * length), yaw, 1f, 0.7f)) placed++;
            }

            if (placed > 0 && kit.Keys.Has(PropRole.FencePost))
            {
                kit.Force(PropRole.FencePost, start, yaw);
                kit.Force(PropRole.FencePost, start + runs * (length * pieces), yaw);
            }

            return placed;
        }
    }

    /// <summary>
    /// The measures of an inn's name board (<see cref="PropRole.InnSign"/>), shared by the composer (its spot) and the Unity side that
    /// builds it from boxes and letters it (<c>SettlementLife.PlaceNameBoard</c>). Local frame: x across the road (the board's width),
    /// y up, z along the road (the board's faces look along ±z). Pure.
    /// </summary>
    public static class InnSign
    {
        /// <summary>Spots tried along the road from the tavern's middle, metres.</summary>
        public static readonly float[] Along = { 4f, -4f, 9f, -9f, 14f, -14f, 19f, -19f };

        /// <summary>The board's middle stands this far beyond the verge (its near post 0.3 m off it), metres.</summary>
        public const float FromVerge = 1.5f;

        /// <summary>Footprint radius around the board's middle (half the frame's width), metres.</summary>
        public const float FootprintRadius = 1.4f;

        /// <summary>The board: width, height, thickness, height of its lower edge above the ground, metres.</summary>
        public const float BoardWidth = 2.4f, BoardHeight = 0.62f, BoardThickness = 0.07f, BoardBottom = 1.8f;

        /// <summary>The frame: post height and size, metres.</summary>
        public const float PostHeight = 2.85f, PostSize = 0.15f;

        /// <summary>Largest capital height of the lettering and its margin to the board's edge, metres (legible at 25 m: about 8 px at 1080p).</summary>
        public const float CapHeight = 0.25f, Margin = 0.12f;

        /// <summary>The lettering is drawn only within about this distance, metres.</summary>
        public const float LettersVisible = 60f;
    }
}
