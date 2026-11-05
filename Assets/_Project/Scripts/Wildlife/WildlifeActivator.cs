using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MedievalZen.Wildlife
{
    /// <summary>
    /// Which planned groups live in the simulation (pure; used by <see cref="WildlifeManager"/> and the headless simulation).
    /// Plans the 128 m cells around the traveller (on worker threads when the habitat allows), activates the groups whose home
    /// is within <see cref="ActivateRadius"/> (nearest first, on ground the caller says is built, night-only species only at
    /// night) up to <see cref="MaxGroups"/> groups and <see cref="MaxAnimals"/> animals, and releases those beyond
    /// <see cref="ReleaseRadius"/> unless an event uses them. Groups that were disturbed (fled, herded, moved by an event) are
    /// remembered for <see cref="MemorySeconds"/> with their new home, so they do not reset when the traveller turns round.
    /// </summary>
    public sealed class WildlifeActivator
    {
        public const float ActivateRadius = 220f;
        public const float ReleaseRadius = 260f;
        public const int MaxGroups = 10;
        public const int MaxAnimals = 60;
        public const float MemorySeconds = 600f;

        /// <summary>
        /// Once the area is filled, groups appear only this far from the traveller or further (a group held back by the caps, a
        /// wolf at dusk), so nothing pops in near the road. The first pass, and the first after a jump (Go now), fill everything.
        /// </summary>
        public const float AppearBeyond = 150f;

        private sealed class CellPlans
        {
            public long X, Z;
            public List<GroupPlan> Plans;
            public Task<List<GroupPlan>> Task;
            public float LastWanted;
        }

        private struct Memory
        {
            public double HomeX, HomeZ;
            public float Radius;
            public float Time;
        }

        private readonly WildlifePlanner planner;
        private readonly WildlifeSim sim;
        private readonly bool threaded;
        private readonly Dictionary<long, CellPlans> cells = new Dictionary<long, CellPlans>();
        private readonly HashSet<long> active = new HashSet<long>();
        private readonly Dictionary<long, Memory> memory = new Dictionary<long, Memory>();
        private readonly List<GroupPlan> candidates = new List<GroupPlan>(64);
        private readonly List<float> distances = new List<float>(64);
        private readonly List<long> evict = new List<long>(32);
        private double lastX = double.NaN, lastZ = double.NaN;
        private float fillUntil = float.NegativeInfinity;

        /// <summary>Creates an activator for a planner and a simulation; <paramref name="threaded"/> plans on worker threads.</summary>
        public WildlifeActivator(WildlifePlanner planner, WildlifeSim sim, bool threaded)
        {
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
            this.sim = sim ?? throw new ArgumentNullException(nameof(sim));
            this.threaded = threaded && planner.Habitat.ThreadSafe;
        }

        /// <summary>Groups activated by the last <see cref="Step"/> (cleared at each step).</summary>
        public readonly List<AnimalGroup> Activated = new List<AnimalGroup>(16);

        /// <summary>Cells planned so far.</summary>
        public int CellsPlanned { get; private set; }

        /// <summary>Groups activated so far.</summary>
        public int GroupsActivated { get; private set; }

        /// <summary>Disturbed groups remembered now.</summary>
        public int Remembered => memory.Count;

        /// <summary>Planning failures (the first message is kept).</summary>
        public string PlanningError { get; private set; }

        /// <summary>
        /// One pass: release, plan, activate. The traveller is at absolute (<paramref name="tx"/>, <paramref name="tz"/>);
        /// scene = absolute − origin. <paramref name="groundReady"/> says whether the ground around a scene point is built
        /// (null: always). <paramref name="now"/> is a clock in seconds for the memory and the cell cache.
        /// </summary>
        public void Step(double tx, double tz, double originX, double originZ, in WildlifeContext ctx, Func<float, float, float, bool> groundReady, float now)
        {
            Activated.Clear();
            Poll();
            Release(ctx, originX, originZ, now);
            bool fill = double.IsNaN(lastX) || (tx - lastX) * (tx - lastX) + (tz - lastZ) * (tz - lastZ) > 300.0 * 300.0;
            if (fill) fillUntil = now + 3f;   // planning may take a moment: keep filling briefly
            fill |= now < fillUntil;
            lastX = tx;
            lastZ = tz;

            double reach = ActivateRadius + WildlifePlanner.CellSize * 0.5;
            WildlifePlanner.CellOf(tx - reach, tz - reach, out long cx0, out long cz0);
            WildlifePlanner.CellOf(tx + reach, tz + reach, out long cx1, out long cz1);
            candidates.Clear();
            distances.Clear();
            int syncBudget = threaded ? 0 : 2;
            for (long cz = cz0; cz <= cz1; cz++)
            {
                for (long cx = cx0; cx <= cx1; cx++)
                {
                    double nx = Math.Max(cx * WildlifePlanner.CellSize, Math.Min(tx, (cx + 1) * WildlifePlanner.CellSize));
                    double nz = Math.Max(cz * WildlifePlanner.CellSize, Math.Min(tz, (cz + 1) * WildlifePlanner.CellSize));
                    if ((nx - tx) * (nx - tx) + (nz - tz) * (nz - tz) > reach * reach) continue;
                    long key = WildlifePlanner.Key(cx, cz);
                    if (!cells.TryGetValue(key, out CellPlans cell))
                    {
                        cell = new CellPlans { X = cx, Z = cz };
                        cells[key] = cell;
                    }

                    cell.LastWanted = now;
                    if (cell.Plans == null && cell.Task == null)
                    {
                        if (threaded)
                        {
                            long px = cx, pz = cz;
                            WildlifePlanner p = planner;
                            cell.Task = Task.Run(() => p.PlanCell(px, pz));
                        }
                        else if (syncBudget-- > 0)
                        {
                            cell.Plans = planner.PlanCell(cx, cz);
                            CellsPlanned++;
                        }
                    }

                    if (cell.Plans == null) continue;
                    foreach (GroupPlan plan in cell.Plans)
                    {
                        if (active.Contains(plan.Id)) continue;
                        HomeOf(plan, now, out double hx, out double hz);
                        float d = (float)Math.Sqrt((hx - tx) * (hx - tx) + (hz - tz) * (hz - tz));
                        if (d > ActivateRadius || (!fill && d < AppearBeyond)) continue;
                        if (SpeciesCatalog.Get(plan.Main).NightOnly && !ctx.IsTwilightOrNight) continue;
                        candidates.Add(plan);
                        distances.Add(d);
                    }
                }
            }

            // Nearest first, within the caps.
            for (int n = 0; n < candidates.Count; n++)
            {
                int best = -1;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i] != null && (best < 0 || distances[i] < distances[best])) best = i;
                }

                if (best < 0) break;
                GroupPlan plan = candidates[best];
                candidates[best] = null;
                if (sim.Groups.Count >= MaxGroups || sim.Agents.Count + plan.Count > MaxAnimals) break;
                HomeOf(plan, now, out double hx, out double hz);
                if (groundReady != null && !groundReady((float)(hx - originX), (float)(hz - originZ), plan.HomeRadius + 8f)) continue;
                GroupPlan use = plan;
                bool moved = memory.TryGetValue(plan.Id, out Memory m) && now - m.Time < MemorySeconds;
                if (moved) use = Moved(plan, m);
                AnimalGroup g = sim.AddGroup(use, originX, originZ);
                g.Disturbed = moved;
                active.Add(plan.Id);
                GroupsActivated++;
                Activated.Add(g);
            }

            // Forget cells far behind.
            evict.Clear();
            foreach (KeyValuePair<long, CellPlans> pair in cells)
            {
                if (now - pair.Value.LastWanted > 60f && (pair.Value.Task == null || pair.Value.Task.IsCompleted)) evict.Add(pair.Key);
            }

            foreach (long k in evict) cells.Remove(k);
        }

        /// <summary>Picks up finished worker plans.</summary>
        public void Poll()
        {
            foreach (CellPlans cell in cells.Values)
            {
                if (cell.Task == null || !cell.Task.IsCompleted) continue;
                if (cell.Task.Status == TaskStatus.RanToCompletion)
                {
                    cell.Plans = cell.Task.Result;
                }
                else
                {
                    cell.Plans = new List<GroupPlan>();
                    if (PlanningError == null) PlanningError = $"planning cell ({cell.X}, {cell.Z}) failed: {cell.Task.Exception?.GetBaseException()}";
                }

                cell.Task = null;
                CellsPlanned++;
            }
        }

        private void Release(in WildlifeContext ctx, double originX, double originZ, float now)
        {
            for (int i = sim.Groups.Count - 1; i >= 0; i--)
            {
                AnimalGroup g = sim.Groups[i];
                if (g.Event != null) continue;
                // Released when both its home and its animals are beyond the radius (a circling eagle or a wandering herd does
                // not flicker in and out at the edge).
                g.Centroid(out float cx, out float cz);
                float dc = (float)Math.Sqrt((cx - ctx.TravellerX) * (cx - ctx.TravellerX) + (cz - ctx.TravellerZ) * (cz - ctx.TravellerZ));
                float dh = (float)Math.Sqrt((g.HomeX - ctx.TravellerX) * (g.HomeX - ctx.TravellerX) + (g.HomeZ - ctx.TravellerZ) * (g.HomeZ - ctx.TravellerZ));
                float d = Math.Min(dc, dh);
                bool dayCame = g.Main == Species.Wolf && !ctx.IsTwilightOrNight && d > 120f;
                if (d <= ReleaseRadius && !dayCame) continue;
                if (g.Disturbed && !g.Visitor)
                {
                    if (memory.Count > 64) memory.Clear();
                    memory[g.Id] = new Memory { HomeX = g.HomeX + originX, HomeZ = g.HomeZ + originZ, Radius = g.HomeRadius, Time = now };
                }

                active.Remove(g.Id);
                sim.RemoveGroup(g);
            }
        }

        private void HomeOf(GroupPlan plan, float now, out double x, out double z)
        {
            x = plan.HomeX;
            z = plan.HomeZ;
            if (memory.TryGetValue(plan.Id, out Memory m) && now - m.Time < MemorySeconds)
            {
                x = m.HomeX;
                z = m.HomeZ;
            }
        }

        private static GroupPlan Moved(GroupPlan plan, Memory m)
        {
            double dx = m.HomeX - plan.HomeX, dz = m.HomeZ - plan.HomeZ;
            var copy = new GroupPlan
            {
                Id = plan.Id, Kind = plan.Kind, Main = plan.Main, HomeX = m.HomeX, HomeZ = m.HomeZ, HomeRadius = m.Radius,
                HasRect = false, SettlementId = plan.SettlementId, Region = plan.Region, CellX = plan.CellX, CellZ = plan.CellZ,
            };
            foreach (MemberPlan mp in plan.Members)
            {
                MemberPlan c = mp;
                c.X += dx;
                c.Z += dz;
                copy.Members.Add(c);
            }

            return copy;
        }

        /// <summary>Forgets everything (new journey).</summary>
        public void Clear()
        {
            lastX = lastZ = double.NaN;
            cells.Clear();
            active.Clear();
            memory.Clear();
            Activated.Clear();
        }

        /// <summary>Forgets which groups are active (the simulation was cleared).</summary>
        public void ForgetActive()
        {
            active.Clear();
            lastX = lastZ = double.NaN;
        }
    }
}
