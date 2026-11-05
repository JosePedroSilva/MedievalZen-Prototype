using System;
using System.Collections.Generic;

namespace MedievalZen.Wildlife
{
    /// <summary>What an animal's brain is doing.</summary>
    public enum BrainMode
    {
        Idle = 0,
        Graze = 1,
        Wander = 2,
        Rest = 3,
        Alert = 4,
        Flee = 5,
        Calm = 6,
        Return = 7,
        Follow = 8,
        Fly = 9,
        StepAside = 10,
        Sit = 11,
        Howl = 12,

        /// <summary>An event moves the animal (its brain waits).</summary>
        Scripted = 13,
    }

    /// <summary>A tiny allocation-free random generator (xorshift*), seeded per animal.</summary>
    public struct AgentRandom
    {
        private ulong state;

        /// <summary>Seeds the generator.</summary>
        public AgentRandom(ulong seed)
        {
            state = seed != 0 ? seed : 0x9E3779B97F4A7C15UL;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float Next()
        {
            unchecked
            {
                state ^= state >> 12;
                state ^= state << 25;
                state ^= state >> 27;
                ulong r = state * 0x2545F4914F6CDD1DUL;
                return (r >> 40) * (1f / 16777216f);
            }
        }

        /// <summary>Uniform in [a, b).</summary>
        public float Range(float a, float b) => a + (b - a) * Next();
    }

    /// <summary>
    /// One animal in the simulation, in scene coordinates (shifted with the floating origin). Plain data driven by
    /// <see cref="AnimalBrain"/>, an event, and <see cref="WildlifeSim"/>'s mover; the Unity view reads it every frame.
    /// </summary>
    public sealed class AnimalAgent
    {
        public int Serial;
        public SpeciesInfo Info;
        public AnimalGroup Group;
        public int Variant;

        // Body.
        public float X, Y, Z;
        public float Yaw;
        public float Pitch;
        public float Speed;

        /// <summary>Height above the ground (flying birds).</summary>
        public float Altitude;

        /// <summary>Individual home spot (scene).</summary>
        public float HomeX, HomeZ;

        // Brain.
        public BrainMode Mode;
        public float ModeTime, ModeDuration;
        public float NextThink;
        public float ThinkAccumulator;
        public bool HasTarget, Arrived;
        public float TargetX, TargetZ;
        public float WantedSpeed;
        public bool Running;
        public bool HasFace;
        public float FaceYaw;
        public float AltitudeTarget;
        public float CircleX, CircleZ, CircleRadius, CircleAngle, CircleDirection = 1f;

        /// <summary>The traveller's horizontal distance (updated every step).</summary>
        public float TravellerDistance = float.MaxValue;

        /// <summary>Next time the animal may make a call.</summary>
        public float NextSound;

        /// <summary>The pose shown, driven through <see cref="Driver"/>.</summary>
        public Pose Pose
        {
            get => Driver.Wanted;
            set => Driver.Wanted = value;
        }

        public readonly AnimatorBoolDriver Driver;
        public AgentRandom Rng;

        // Event control.
        public bool Scripted;

        /// <summary>The animal keeps at least <see cref="AvoidGap"/> from every member of this group (a predator from its prey).</summary>
        public AnimalGroup Avoid;

        public float AvoidGap;

        /// <summary>A mother to follow (chicks follow a hen).</summary>
        public AnimalAgent Mother;

        /// <summary>Ground last sampled at.</summary>
        public float GroundX = float.NaN, GroundZ = float.NaN, GroundY;

        /// <summary>The Unity side's view of this animal (null until shown).</summary>
        public object View;

        /// <summary>True while the animal belongs to the simulation.</summary>
        public bool Alive;

        /// <summary>Creates an agent (pooled by the simulation).</summary>
        public AnimalAgent(SpeciesInfo info)
        {
            Info = info;
            Driver = new AnimatorBoolDriver(info);
        }

        /// <summary>Horizontal distance to a point.</summary>
        public float DistanceTo(float x, float z)
        {
            float dx = x - X, dz = z - Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Horizontal distance to another animal.</summary>
        public float DistanceTo(AnimalAgent o) => DistanceTo(o.X, o.Z);

        /// <summary>Sets a walking or running target.</summary>
        public void GoTo(float x, float z, bool run)
        {
            TargetX = x;
            TargetZ = z;
            HasTarget = true;
            Arrived = false;
            Running = run;
            WantedSpeed = run ? Info.RunSpeed : Info.WalkSpeed;
            HasFace = false;
        }

        /// <summary>Stops where it is.</summary>
        public void Stop()
        {
            HasTarget = false;
            WantedSpeed = 0f;
            Running = false;
        }

        /// <summary>Turns to face a point (without walking).</summary>
        public void FacePoint(float x, float z)
        {
            HasFace = true;
            FaceYaw = (float)(Math.Atan2(x - X, z - Z) * 180.0 / Math.PI);
        }

        /// <summary>Enters a brain mode for <paramref name="seconds"/>.</summary>
        public void SetMode(BrainMode mode, float seconds)
        {
            Mode = mode;
            ModeTime = 0f;
            ModeDuration = seconds;
        }

        public override string ToString() => $"{Info.Name}#{Serial} {Mode} at ({X:0.0}, {Z:0.0})";
    }

    /// <summary>A group of animals sharing a home (a herd, a farmyard, a flock), in scene coordinates.</summary>
    public sealed class AnimalGroup
    {
        public long Id;
        public GroupKind Kind;
        public Species Main;
        public float HomeX, HomeZ, HomeRadius;

        /// <summary>Pasture rectangle (scene centre, half sizes, yaw in degrees).</summary>
        public bool HasRect;

        public float RectX, RectZ, HalfX, HalfZ, Yaw;
        public int SettlementId = -1;
        public MedievalZen.Endless.RegionKind Region;

        /// <summary>Planner cell (or a sentinel for visitors).</summary>
        public long CellX, CellZ;

        /// <summary>Brought in by an event (released when far away, never re-planned).</summary>
        public bool Visitor;

        /// <summary>Moved or scattered by an event or the traveller: remembered when released.</summary>
        public bool Disturbed;

        /// <summary>The event using this group, or null.</summary>
        public AnimalEvent Event;

        /// <summary>Simulation time of activation.</summary>
        public float ActivatedAt;

        public readonly List<AnimalAgent> Members = new List<AnimalAgent>();

        /// <summary>True when (x, z) lies in the home range (rectangle for pastures, else circle).</summary>
        public bool InHome(float x, float z, float margin = 0f)
        {
            if (HasRect)
            {
                double rad = Yaw * Math.PI / 180.0;
                double cs = Math.Cos(rad), sn = Math.Sin(rad);
                double dx = x - RectX, dz = z - RectZ;
                double u = dx * cs - dz * sn, v = dx * sn + dz * cs;
                return Math.Abs(u) <= HalfX + margin && Math.Abs(v) <= HalfZ + margin;
            }

            float ex = x - HomeX, ez = z - HomeZ;
            float r = HomeRadius + margin;
            return ex * ex + ez * ez <= r * r;
        }

        /// <summary>A point in the home range from two uniform numbers.</summary>
        public void HomePoint(float a, float b, out float x, out float z)
        {
            if (HasRect)
            {
                double rad = Yaw * Math.PI / 180.0;
                double cs = Math.Cos(rad), sn = Math.Sin(rad);
                double u = (a * 2.0 - 1.0) * HalfX, v = (b * 2.0 - 1.0) * HalfZ;
                x = (float)(RectX + u * cs + v * sn);
                z = (float)(RectZ - u * sn + v * cs);
                return;
            }

            double ang = a * Math.PI * 2.0, r = Math.Sqrt(b) * HomeRadius;
            x = HomeX + (float)(Math.Cos(ang) * r);
            z = HomeZ + (float)(Math.Sin(ang) * r);
        }

        /// <summary>Centre of the members (horizontal).</summary>
        public void Centroid(out float x, out float z)
        {
            x = HomeX;
            z = HomeZ;
            if (Members.Count == 0) return;
            float sx = 0f, sz = 0f;
            for (int i = 0; i < Members.Count; i++)
            {
                sx += Members[i].X;
                sz += Members[i].Z;
            }

            x = sx / Members.Count;
            z = sz / Members.Count;
        }

        /// <summary>Number of members of a species.</summary>
        public int CountOf(Species s)
        {
            int n = 0;
            for (int i = 0; i < Members.Count; i++)
            {
                if (Members[i].Info.Id == s) n++;
            }

            return n;
        }

        /// <summary>The first member of a species, or null.</summary>
        public AnimalAgent First(Species s)
        {
            for (int i = 0; i < Members.Count; i++)
            {
                if (Members[i].Info.Id == s) return Members[i];
            }

            return null;
        }

        /// <summary>Moves every scene position of the group by (dx, dz).</summary>
        public void Shift(float dx, float dz)
        {
            HomeX += dx;
            HomeZ += dz;
            RectX += dx;
            RectZ += dz;
        }

        public override string ToString() => $"{Kind} {SpeciesCatalog.Get(Main).Name} ×{Members.Count} #{Id}";
    }
}
