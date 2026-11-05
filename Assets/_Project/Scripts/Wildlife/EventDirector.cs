using System;
using System.Collections.Generic;

namespace MedievalZen.Wildlife
{
    /// <summary>
    /// Chooses and runs the animal events: one at a time, never during a streaming hold, with a seeded cooldown of a few
    /// minutes between them. Every <see cref="CheckInterval"/> seconds it asks each event type for a candidate near the
    /// traveller (30–150 m, ahead or beside) and starts the best one with probability <see cref="StartChance"/>. F6 forces
    /// the best candidate at once, anywhere within 220 m. Start and end lines are queued in <see cref="Log"/>.
    /// </summary>
    public sealed class EventDirector
    {
        public const float CheckInterval = 10f;
        public const float StartChance = 0.55f;
        public const float CooldownMin = 120f, CooldownMax = 240f;

        /// <summary>The traveller leaving an event this far behind ends it.</summary>
        public const float AbandonDistance = 320f;

        private static readonly float[] Weights = { 1f, 0.9f, 1f, 0.9f, 1f, 0.9f, 1f };

        private readonly WildlifeSim sim;
        private readonly Random rng;
        private float nextCheck;
        private float cooldownUntil;
        private long nextId = 1;
        private int lastKind = -1;

        /// <summary>Creates the director (seeded: the same journey gives the same rhythm of events).</summary>
        public EventDirector(WildlifeSim sim, int seed)
        {
            this.sim = sim;
            rng = new Random(MedievalZen.Endless.Generation.EndlessNoise.Seed(seed, WildlifePlanner.Stream + 20, 0));
            cooldownUntil = 60f;   // a quiet first minute
        }

        /// <summary>The event running, or null.</summary>
        public AnimalEvent Active { get; private set; }

        /// <summary>Start and end lines not yet written to the console.</summary>
        public readonly List<string> Log = new List<string>();

        /// <summary>Every event started (for the headless simulation's report).</summary>
        public readonly List<AnimalEvent> History = new List<AnimalEvent>();

        /// <summary>Switches automatic events off (tests).</summary>
        public bool Automatic { get; set; } = true;

        internal void Step(float dt, in WildlifeContext ctx)
        {
            if (Active != null)
            {
                float dx = Active.FocusX - ctx.TravellerX, dz = Active.FocusZ - ctx.TravellerZ;
                if (ctx.HasTraveller && dx * dx + dz * dz > AbandonDistance * AbandonDistance) Active.Finish("the traveller moved on");
                else Active.Step(dt, ctx);
                if (Active.Finished) End();
                return;
            }

            if (!Automatic || ctx.Holding || !ctx.HasTraveller || ctx.Time < nextCheck) return;
            nextCheck = ctx.Time + CheckInterval;
            if (ctx.Time < cooldownUntil || rng.NextDouble() >= StartChance) return;
            TryStart(ctx, false, out _);
        }

        /// <summary>Starts the best event possible now (F6). False with the reason when none is possible.</summary>
        public bool TryForce(in WildlifeContext ctx, out string why)
        {
            if (Active != null)
            {
                why = $"'{Active.Name}' is already under way";
                return false;
            }

            if (ctx.Holding)
            {
                why = "the road is still being laid";
                return false;
            }

            return TryStart(ctx, true, out why);
        }

        /// <summary>Ends the running event now.</summary>
        public void Abort(string reason)
        {
            if (Active == null) return;
            Active.Finish(reason);
            End();
        }

        /// <summary>Moves the running event's spots (floating origin).</summary>
        internal void Shift(float dx, float dz) => Active?.Shift(dx, dz);

        private bool TryStart(in WildlifeContext ctx, bool forced, out string why)
        {
            why = null;
            AnimalEvent best = null;
            float bestScore = 0f;
            string firstWhy = null;
            for (int k = 0; k < 7; k++)
            {
                AnimalEvent e = Prepare((EventKind)k, ctx, forced, out float score, out string reason);
                if (e == null)
                {
                    // Keep the most telling reason ("the traveller would reach the crossing first" over "no stags nearby").
                    if (reason != null && (firstWhy == null || (firstWhy.StartsWith("no ", StringComparison.Ordinal) && !reason.StartsWith("no ", StringComparison.Ordinal)))) firstWhy = reason;
                    continue;
                }

                // Variety: the kind that ran last is less likely to come again straight away.
                float repeat = lastKind == k ? 0.35f : 1f;
                float weighted = score * Weights[k] * repeat * (forced ? 1f : (float)(0.6 + 0.8 * rng.NextDouble()));
                if (weighted <= bestScore) continue;
                bestScore = weighted;
                best = e;
            }

            if (best == null)
            {
                why = AnyAnimalsNear(ctx) ? "No animals in the right place for an event (" + (firstWhy ?? "none suits") + ")" : "No animals nearby for an event";
                return false;
            }

            best.Start(sim, sim.NextSeed(), nextId++ | 0x4000_0000_0000L);
            Active = best;
            lastKind = (int)best.Kind;
            History.Add(best);
            float d = (float)Math.Sqrt((best.FocusX - ctx.TravellerX) * (best.FocusX - ctx.TravellerX) + (best.FocusZ - ctx.TravellerZ) * (best.FocusZ - ctx.TravellerZ));
            Log.Add($"Wildlife: event '{best.Name}' started {d:0} m from the traveller ({best.Detail}){(forced ? " — forced (F6)" : string.Empty)}.");
            return true;
        }

        private AnimalEvent Prepare(EventKind kind, in WildlifeContext ctx, bool forced, out float score, out string why)
        {
            switch (kind)
            {
                case EventKind.FoxAtHenHouse: return FoxAtHenHouseEvent.Prepare(sim, ctx, forced, out score, out why);
                case EventKind.DogAndRabbits: return DogAndRabbitsEvent.Prepare(sim, ctx, forced, out score, out why);
                case EventKind.WolfAndDeer: return WolfAndDeerEvent.Prepare(sim, ctx, forced, out score, out why);
                case EventKind.SheepdogHerding: return SheepdogHerdingEvent.Prepare(sim, ctx, forced, out score, out why);
                case EventKind.HorsesRunning: return HorsesRunningEvent.Prepare(sim, ctx, forced, out score, out why);
                case EventKind.BirdsFlushing: return BirdsFlushingEvent.Prepare(sim, ctx, forced, out score, out why);
                case EventKind.StagsSquaringUp: return StagsSquaringUpEvent.Prepare(sim, ctx, forced, out score, out why);
                default:
                    score = 0f;
                    why = "unknown event";
                    return null;
            }
        }

        /// <summary>Starts a given event if possible (tests).</summary>
        public bool TryStart(EventKind kind, in WildlifeContext ctx, out string why)
        {
            if (Active != null)
            {
                why = "busy";
                return false;
            }

            AnimalEvent e = Prepare(kind, ctx, true, out _, out why);
            if (e == null) return false;
            e.Start(sim, sim.NextSeed(), nextId++ | 0x4000_0000_0000L);
            Active = e;
            History.Add(e);
            Log.Add($"Wildlife: event '{e.Name}' started ({e.Detail}).");
            return true;
        }

        private bool AnyAnimalsNear(in WildlifeContext ctx)
        {
            foreach (AnimalAgent a in sim.Agents)
            {
                if (a.TravellerDistance < 220f) return true;
            }

            return false;
        }

        private void End()
        {
            AnimalEvent e = Active;
            Active = null;
            cooldownUntil = sim.Context.Time + (float)(CooldownMin + (CooldownMax - CooldownMin) * rng.NextDouble());
            string gap = e.HasChase ? $"; closest predator–prey gap {e.MinGap:0.0} m" : string.Empty;
            Log.Add($"Wildlife: event '{e.Name}' ended ({e.EndReason}) after {e.Elapsed:0} s{gap}.");
        }
    }
}
