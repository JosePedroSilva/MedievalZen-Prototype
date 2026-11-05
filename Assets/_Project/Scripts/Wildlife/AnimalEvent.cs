using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Wildlife
{
    /// <summary>The scenes between animals.</summary>
    public enum EventKind
    {
        FoxAtHenHouse = 0,
        DogAndRabbits = 1,
        WolfAndDeer = 2,
        SheepdogHerding = 3,
        HorsesRunning = 4,
        BirdsFlushing = 5,
        StagsSquaringUp = 6,
    }

    /// <summary>
    /// A small scripted scene with roles (a predator and its prey, a herd and a dog). It takes the animals it needs from
    /// their brains (<see cref="AnimalAgent.Scripted"/>), moves them through a few phases, and hands them back; visitors it
    /// brings in (a fox, a wolf, a sheepdog) stay as ordinary far-away groups. Gentle by design: a predator keeps a gap
    /// from its prey (<see cref="AnimalAgent.Avoid"/>), prey always gets away, nothing is hurt. Every event ends within
    /// <see cref="MaxSeconds"/>. Scene coordinates (shifted with the origin). Pure.
    /// </summary>
    public abstract class AnimalEvent
    {
        /// <summary>A scene point an event remembers.</summary>
        protected struct Spot
        {
            public float X, Z;

            public Spot(float x, float z)
            {
                X = x;
                Z = z;
            }
        }

        protected WildlifeSim Sim;
        protected AgentRandom Rng;
        protected readonly List<AnimalAgent> Cast = new List<AnimalAgent>(16);
        protected readonly List<AnimalGroup> Used = new List<AnimalGroup>(4);
        protected readonly Spot[] P = new Spot[8];
        protected int Phase;
        protected float PhaseTime;

        /// <summary>Which event.</summary>
        public abstract EventKind Kind { get; }

        /// <summary>Its name in the log and the overlay.</summary>
        public abstract string Name { get; }

        /// <summary>Id for the points of interest.</summary>
        public long Id { get; private set; }

        public float Elapsed { get; private set; }
        public bool Finished { get; private set; }
        public string EndReason { get; private set; }

        /// <summary>Longest it may run.</summary>
        public float MaxSeconds { get; protected set; } = 120f;

        /// <summary>Closest a predator came to its prey during the event (∞ without a chase).</summary>
        public float MinGap { get; private set; } = float.PositiveInfinity;

        /// <summary>True when the event has a predator and prey.</summary>
        public bool HasChase { get; private set; }

        /// <summary>Where the scene is (for the camera, the log and aborting when the traveller leaves).</summary>
        public float FocusX, FocusZ;

        /// <summary>A few words for the start line ("3 hens, a golden retriever").</summary>
        public string Detail = string.Empty;

        /// <summary>The groups the event uses.</summary>
        public IReadOnlyList<AnimalGroup> Groups => Used;

        internal void Start(WildlifeSim sim, ulong seed, long id)
        {
            Sim = sim;
            Rng = new AgentRandom(seed);
            Id = id;
            Begin();
            UpdateFocus();
        }

        internal void Step(float dt, in WildlifeContext ctx)
        {
            if (Finished) return;
            Elapsed += dt;
            PhaseTime += dt;
            if (Elapsed > MaxSeconds)
            {
                Finish("timed out");
                return;
            }

            Tick(dt, ctx);
            if (Finished) return;
            UpdateFocus();
            Sim.Points.Publish(Id, new Vector3(FocusX, FocusY(), FocusZ), 12f, 1.6f, ctx.Time + 2.5f);
        }

        /// <summary>Takes the roles (called once).</summary>
        protected abstract void Begin();

        /// <summary>One step of the scene.</summary>
        protected abstract void Tick(float dt, in WildlifeContext ctx);

        /// <summary>Ends the event: every animal goes back to its brain, the groups are free again.</summary>
        public void Finish(string reason)
        {
            if (Finished) return;
            Finished = true;
            EndReason = reason;
            foreach (AnimalAgent a in Cast)
            {
                if (a.Alive) Free(a);
            }

            foreach (AnimalGroup g in Used)
            {
                if (g.Event == this) g.Event = null;
            }

            Sim.Points.Remove(Id);
        }

        /// <summary>Moves the remembered spots (floating origin; the animals move with the simulation).</summary>
        public void Shift(float dx, float dz)
        {
            for (int i = 0; i < P.Length; i++)
            {
                P[i].X += dx;
                P[i].Z += dz;
            }

            FocusX += dx;
            FocusZ += dz;
        }

        // ------------------------------------------------------------------ helpers

        protected void Use(AnimalGroup g)
        {
            g.Event = this;
            g.Disturbed = true;
            if (!Used.Contains(g)) Used.Add(g);
        }

        protected void Take(AnimalAgent a)
        {
            a.Scripted = true;
            a.SetMode(BrainMode.Scripted, 1e9f);
            a.HasFace = false;
            if (!Cast.Contains(a)) Cast.Add(a);
        }

        protected static void Free(AnimalAgent a)
        {
            if (!a.Scripted) return;
            a.Scripted = false;
            a.Avoid = null;
            a.AvoidGap = 0f;
            a.Stop();
            a.HasFace = false;
            a.SetMode(BrainMode.Idle, a.Rng.Range(1f, 4f));
            a.Pose = Pose.Idle;
        }

        protected void SetPhase(int phase)
        {
            Phase = phase;
            PhaseTime = 0f;
        }

        /// <summary>Records the gap between a predator and its prey.</summary>
        protected void Measure(AnimalAgent predator, AnimalGroup prey, AnimalAgent except = null)
        {
            if (predator == null || prey == null) return;
            HasChase = true;
            foreach (AnimalAgent p in prey.Members)
            {
                if (p == predator || p == except || p.Info.Id == predator.Info.Id) continue;
                MinGap = Math.Min(MinGap, predator.DistanceTo(p));
            }
        }

        protected static void Run(AnimalAgent a, float x, float z, float speed = 0f)
        {
            a.GoTo(x, z, true);
            if (speed > 0f) a.WantedSpeed = speed;
            a.Pose = Pose.Run;
        }

        protected static void Walk(AnimalAgent a, float x, float z, float speed = 0f)
        {
            a.GoTo(x, z, false);
            if (speed > 0f) a.WantedSpeed = speed;
            a.Pose = Pose.Walk;
        }

        protected static void Hold(AnimalAgent a, Pose pose)
        {
            a.Stop();
            a.Arrived = false;
            a.Pose = a.Info.Has(pose) ? pose : Pose.Idle;
        }

        /// <summary>A point <paramref name="distance"/> from (x, z) in direction (dx, dz) (normalised here).</summary>
        protected static Spot Away(float x, float z, float dx, float dz, float distance)
        {
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-4f) return new Spot(x + distance, z);
            return new Spot(x + dx / len * distance, z + dz / len * distance);
        }

        protected virtual void UpdateFocus()
        {
            float sx = 0f, sz = 0f;
            int n = 0;
            foreach (AnimalAgent a in Cast)
            {
                if (!a.Alive) continue;
                sx += a.X;
                sz += a.Z;
                n++;
            }

            if (n == 0)
            {
                // Nobody is cast (doves fly by themselves): the groups' centre.
                foreach (AnimalGroup g in Used)
                {
                    if (g.Members.Count == 0) continue;
                    g.Centroid(out float gx, out float gz);
                    sx += gx;
                    sz += gz;
                    n++;
                }
            }

            if (n == 0) return;
            FocusX = sx / n;
            FocusZ = sz / n;
        }

        private float FocusY()
        {
            foreach (AnimalAgent a in Cast)
            {
                if (a.Alive) return a.GroundY;
            }

            return 0f;
        }

        // ------------------------------------------------------------------ finding

        /// <summary>Distance range an event is chosen in (forced by F6: wider and in any direction).</summary>
        protected static bool InRange(in WildlifeContext ctx, float x, float z, bool forced, float min, float max, out float score)
        {
            score = 0f;
            float dx = x - ctx.TravellerX, dz = z - ctx.TravellerZ;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            float lo = forced ? Math.Min(min, 12f) : min, hi = forced ? Math.Max(max, 220f) : max;
            if (d < lo || d > hi) return false;
            float front = d > 1e-3f ? (dx * ctx.FacingX + dz * ctx.FacingZ) / d : 0f;
            if (!forced && front < -0.2f) return false;   // ahead or beside, preferably ahead
            score = (forced ? 1f : 0.4f + 0.6f * Math.Max(0f, front)) * (1f - Math.Min(0.8f, Math.Abs(d - 80f) / 220f));
            return true;
        }

        /// <summary>
        /// A spot to bring a visitor in: around (cx, cz) at <paramref name="minR"/>–<paramref name="maxR"/>, as close to
        /// direction (ax, az) as the ground allows, standable, off the road and at least <paramref name="fromTraveller"/> from
        /// the traveller (so nothing appears near them).
        /// </summary>
        protected static bool FindSpawn(WildlifeSim sim, in WildlifeContext ctx, float cx, float cz, float ax, float az, float minR, float maxR,
            float fromTraveller, out float x, out float z)
        {
            float len = (float)Math.Sqrt(ax * ax + az * az);
            if (len < 1e-4f)
            {
                ax = 1f;
                az = 0f;
                len = 1f;
            }

            ax /= len;
            az /= len;
            for (int i = 0; i < 14; i++)
            {
                float turn = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 22f * 0.01745f;
                float r = minR + (maxR - minR) * ((i * 37) % 5) / 4f;
                float cs = (float)Math.Cos(turn), sn = (float)Math.Sin(turn);
                float dx = ax * cs - az * sn, dz = ax * sn + az * cs;
                x = cx + dx * r;
                z = cz + dz * r;
                float tx = x - ctx.TravellerX, tz = z - ctx.TravellerZ;
                if (tx * tx + tz * tz < fromTraveller * fromTraveller) continue;
                if (!sim.CanStand(x, z, 0.6f)) continue;
                return true;
            }

            x = cx;
            z = cz;
            return false;
        }

        /// <summary>True when every member is calm enough to be cast (not fleeing, flying or in another event).</summary>
        protected static bool Free(AnimalGroup g)
        {
            if (g.Event != null) return false;
            foreach (AnimalAgent a in g.Members)
            {
                if (a.Scripted || a.Mode == BrainMode.Flee || a.Mode == BrainMode.Fly || a.Altitude > 0.1f) return false;
            }

            return true;
        }

        protected static float Dist(float ax, float az, float bx, float bz) => (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
    }
}
