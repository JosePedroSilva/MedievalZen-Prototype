using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Wildlife
{
    /// <summary>The ground as the animals walk on it, in scene coordinates (the built terrain in the game, the generator offline).</summary>
    public interface IWildlifeGround
    {
        /// <summary>Ground height at (x, z).</summary>
        float Height(float x, float z);

        /// <summary>True where (x, z) is under a lake.</summary>
        bool IsWater(float x, float z);

        /// <summary>True where a tree trunk or a building stands within <paramref name="radius"/> of (x, z).</summary>
        bool IsBlocked(float x, float z, float radius);
    }

    /// <summary>The road, for events that cross it (scene coordinates).</summary>
    public interface IWildlifeRoad
    {
        /// <summary>The road point nearest to (x, z), the road's unit direction there, and the horizontal distance.</summary>
        bool Nearest(float x, float z, out float roadX, out float roadZ, out float dirX, out float dirZ, out float distance);

        /// <summary>The road point <paramref name="ahead"/> metres along the road from the one nearest (x, z), in the direction closest to (dirX, dirZ).</summary>
        bool Along(float x, float z, float dirX, float dirZ, float ahead, out float roadX, out float roadZ);
    }

    /// <summary>What the animals know about the traveller and the world this frame.</summary>
    public struct WildlifeContext
    {
        /// <summary>Simulation clock, seconds.</summary>
        public float Time;

        public bool HasTraveller;
        public float TravellerX, TravellerY, TravellerZ;
        public float VelocityX, VelocityZ;

        /// <summary>Horizontal speed, m/s.</summary>
        public float Speed;

        /// <summary>Horizontal unit direction the traveller faces (or moves).</summary>
        public float FacingX, FacingZ;

        public bool Seated;
        public float SeatedTime;

        /// <summary>Hour of the day, 0..24.</summary>
        public float Hour;

        /// <summary>The streamer holds the traveller (start, Go now): nothing starts.</summary>
        public bool Holding;

        /// <summary>Night (farm animals sleep, wolves come out).</summary>
        public bool IsNight => Hour < 5.5f || Hour > 21.5f;

        /// <summary>Dawn, dusk or night (the wolf and deer event).</summary>
        public bool IsTwilightOrNight => Hour < 7.5f || Hour > 18.5f;

        /// <summary>Golden hour (the horses event).</summary>
        public bool IsGoldenHour => (Hour > 6f && Hour < 9f) || (Hour > 17f && Hour < 20.5f);
    }

    /// <summary>A call an animal makes this frame (played by the Unity side where the animal stands).</summary>
    public struct SoundRequest
    {
        public AnimalAgent Agent;
        public string Clip;
        public float Volume;

        /// <summary>Seconds of looping (0 = once).</summary>
        public float Loop;
    }

    /// <summary>
    /// The animals' world: groups and their members in scene coordinates, the brains (<see cref="AnimalBrain"/>), the mover
    /// (ground following, water and trunk avoidance, herd separation, keep-away gaps for events), calls, events
    /// (<see cref="EventDirector"/>) and points of interest. Pure: the Unity side feeds it a ground, a road and a context,
    /// and shows what it holds. Agents are pooled; nothing allocates per step.
    /// </summary>
    public sealed class WildlifeSim
    {
        /// <summary>Brains of animals nearer than this think every step.</summary>
        public const float NearThink = 70f;

        /// <summary>Brains of animals beyond <see cref="NearThink"/> think five times a second, beyond this twice.</summary>
        public const float MidThink = 140f;

        private readonly List<AnimalAgent> pool = new List<AnimalAgent>();
        private int serial;
        private ulong seedBase;

        /// <summary>Creates an empty simulation.</summary>
        public WildlifeSim(int seed, IWildlifeGround ground, IWildlifeRoad road = null, float corridor = 12f)
        {
            Ground = ground;
            Road = road;
            Corridor = corridor;
            Seed = seed;
            seedBase = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 1UL;
            Events = new EventDirector(this, seed);
        }

        public int Seed { get; }
        public IWildlifeGround Ground { get; set; }
        public IWildlifeRoad Road { get; set; }

        /// <summary>Half width of the road strip animals keep out of (except when an event crosses it).</summary>
        public float Corridor { get; set; }

        public readonly List<AnimalGroup> Groups = new List<AnimalGroup>();
        public readonly List<AnimalAgent> Agents = new List<AnimalAgent>();

        /// <summary>Agents removed since the Unity side last drained the list (their views go back to the pool).</summary>
        public readonly List<AnimalAgent> Removed = new List<AnimalAgent>();

        /// <summary>Calls made since the Unity side last drained the list.</summary>
        public readonly List<SoundRequest> Sounds = new List<SoundRequest>();

        public readonly PointsOfInterest Points = new PointsOfInterest(32);
        public EventDirector Events { get; }

        /// <summary>The context of the last step.</summary>
        public WildlifeContext Context;

        // ------------------------------------------------------------------ groups

        /// <summary>
        /// Adds a planned group: absolute plan positions minus (<paramref name="originX"/>, <paramref name="originZ"/>) give
        /// scene positions. Members land on the ground facing their planned way.
        /// </summary>
        public AnimalGroup AddGroup(GroupPlan plan, double originX, double originZ)
        {
            var g = new AnimalGroup
            {
                Id = plan.Id, Kind = plan.Kind, Main = plan.Main, HomeX = (float)(plan.HomeX - originX), HomeZ = (float)(plan.HomeZ - originZ),
                HomeRadius = plan.HomeRadius, HasRect = plan.HasRect, RectX = (float)(plan.RectX - originX), RectZ = (float)(plan.RectZ - originZ),
                HalfX = plan.HalfX, HalfZ = plan.HalfZ, Yaw = plan.Yaw, SettlementId = plan.SettlementId, Region = plan.Region,
                CellX = plan.CellX, CellZ = plan.CellZ, ActivatedAt = Context.Time,
            };
            foreach (MemberPlan m in plan.Members) AddMember(g, m.Species, m.Variant, (float)(m.X - originX), (float)(m.Z - originZ), m.Yaw);
            LinkChicks(g);
            Groups.Add(g);
            return g;
        }

        /// <summary>Adds a group brought in by an event (a fox, a wolf, a sheepdog): released like any other when far away.</summary>
        public AnimalGroup AddVisitor(Species species, int count, float x, float z, float yaw, long id)
        {
            var g = new AnimalGroup
            {
                Id = id, Kind = GroupKind.Visitor, Main = species, HomeX = x, HomeZ = z, HomeRadius = 6f, Visitor = true, ActivatedAt = Context.Time,
                CellX = long.MinValue, CellZ = long.MinValue,
            };
            for (int i = 0; i < count; i++) AddMember(g, species, 0, x + i * 1.3f, z + (i % 2) * 1.1f, yaw);
            Groups.Add(g);
            return g;
        }

        /// <summary>Adds one animal to a group.</summary>
        public AnimalAgent AddMember(AnimalGroup g, Species species, int variant, float x, float z, float yaw)
        {
            SpeciesInfo info = SpeciesCatalog.Get(species);
            AnimalAgent a = Take(info);
            a.Serial = ++serial;
            a.Group = g;
            a.Variant = Math.Max(0, Math.Min(info.Prefabs.Length - 1, variant));
            a.X = x;
            a.Z = z;
            a.Y = Ground != null ? Ground.Height(x, z) : 0f;
            a.GroundX = x;
            a.GroundZ = z;
            a.GroundY = a.Y;
            a.Yaw = yaw;
            a.HomeX = x;
            a.HomeZ = z;
            a.Rng = new AgentRandom(seedBase ^ ((ulong)g.Id * 0xBF58476D1CE4E5B9UL) ^ ((ulong)(g.Members.Count + 1) * 0x94D049BB133111EBUL));
            a.NextSound = Context.Time + a.Rng.Range(5f, 40f);
            a.Alive = true;
            a.SetMode(BrainMode.Idle, a.Rng.Range(0.5f, 4f));
            if (info.Id == Species.Eagle)
            {
                a.Altitude = 45f + a.Rng.Range(0f, 20f);
                a.CircleX = g.HomeX;
                a.CircleZ = g.HomeZ;
                a.CircleRadius = 60f + a.Rng.Range(0f, 30f);
                a.CircleAngle = a.Rng.Range(0f, 6.283f);
                a.SetMode(BrainMode.Fly, 1e9f);
                a.Pose = Pose.Fly;
            }

            g.Members.Add(a);
            Agents.Add(a);
            return a;
        }

        private void LinkChicks(AnimalGroup g)
        {
            AnimalAgent hen = g.First(Species.Hen);
            if (hen == null) return;
            foreach (AnimalAgent a in g.Members)
            {
                if (a.Info.Id == Species.Chick) a.Mother = hen;
            }
        }

        /// <summary>Removes a group and its animals (an event using it ends first).</summary>
        public void RemoveGroup(AnimalGroup g)
        {
            if (g.Event != null) Events.Abort("group released");
            foreach (AnimalAgent a in g.Members)
            {
                a.Alive = false;
                Agents.Remove(a);
                Removed.Add(a);
            }

            g.Members.Clear();
            Groups.Remove(g);
        }

        /// <summary>Removes one animal (it stood in a building and found no free ground); its group stays, even when empty.</summary>
        public void RemoveMember(AnimalAgent a)
        {
            if (!a.Alive) return;
            a.Alive = false;
            if (a.Group != null)
            {
                a.Group.Members.Remove(a);
                foreach (AnimalAgent o in a.Group.Members)
                {
                    if (o.Mother == a) o.Mother = null;
                }
            }

            Agents.Remove(a);
            Removed.Add(a);
        }

        /// <summary>Removes every group (new journey, animals switched off).</summary>
        public void Clear()
        {
            Events.Abort("cleared");
            for (int i = Groups.Count - 1; i >= 0; i--) RemoveGroup(Groups[i]);
            Points.Clear();
        }

        /// <summary>Returns a removed agent to the pool once its view has been released.</summary>
        public void Recycle(AnimalAgent a)
        {
            a.View = null;
            a.Group = null;
            a.Mother = null;
            a.Avoid = null;
            a.Scripted = false;
            pool.Add(a);
        }

        private AnimalAgent Take(SpeciesInfo info)
        {
            AnimalAgent a;
            if (pool.Count > 0)
            {
                a = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
                a.Info = info;
            }
            else
            {
                a = new AnimalAgent(info);
            }

            a.Driver.Reset(info);
            a.Speed = 0f;
            a.Pitch = 0f;
            a.Altitude = 0f;
            a.AltitudeTarget = 0f;
            a.HasTarget = false;
            a.Arrived = false;
            a.WantedSpeed = 0f;
            a.Running = false;
            a.HasFace = false;
            a.Scripted = false;
            a.Avoid = null;
            a.AvoidGap = 0f;
            a.Mother = null;
            a.ThinkAccumulator = 0f;
            a.TravellerDistance = float.MaxValue;
            a.View = null;
            return a;
        }

        // ------------------------------------------------------------------ origin

        /// <summary>Moves every scene position (animals, homes, targets, events, points of interest) by <paramref name="delta"/>.</summary>
        public void Shift(Vector3 delta)
        {
            float dx = delta.x, dz = delta.z;
            foreach (AnimalGroup g in Groups) g.Shift(dx, dz);
            foreach (AnimalAgent a in Agents)
            {
                a.X += dx;
                a.Z += dz;
                a.HomeX += dx;
                a.HomeZ += dz;
                a.TargetX += dx;
                a.TargetZ += dz;
                a.CircleX += dx;
                a.CircleZ += dz;
                a.GroundX += dx;
                a.GroundZ += dz;
            }

            Points.Shift(delta);
            Events.Shift(dx, dz);
        }

        // ------------------------------------------------------------------ step

        /// <summary>Advances everything by <paramref name="dt"/> seconds.</summary>
        public void Step(float dt, in WildlifeContext context)
        {
            Context = context;
            Points.Tick(context.Time);
            Events.Step(dt, context);
            for (int i = 0; i < Agents.Count; i++)
            {
                AnimalAgent a = Agents[i];
                float dx = a.X - context.TravellerX, dz = a.Z - context.TravellerZ;
                a.TravellerDistance = context.HasTraveller ? (float)Math.Sqrt(dx * dx + dz * dz) : float.MaxValue;
                a.ThinkAccumulator += dt;
                float rate = a.TravellerDistance < NearThink ? 0f : a.TravellerDistance < MidThink ? 0.2f : 0.5f;
                if (a.ThinkAccumulator >= rate)
                {
                    float adt = a.ThinkAccumulator;
                    a.ThinkAccumulator = 0f;
                    a.ModeTime += adt;
                    if (!a.Scripted) AnimalBrain.Think(this, a, adt, context);
                    AnimalBrain.Voice(this, a, adt, context);
                }

                Move(a, dt, context);
            }

            PublishHerds(context);
        }

        private void PublishHerds(in WildlifeContext context)
        {
            // Large herds near the traveller are worth a look (low weight: landmarks win when there are any).
            if (((int)(context.Time * 2f) & 3) != 0) return;
            foreach (AnimalGroup g in Groups)
            {
                if (g.Members.Count < 5 || g.Event != null) continue;
                g.Centroid(out float x, out float z);
                Points.Publish(g.Id, new Vector3(x, g.Members[0].Y, z), 8f, 0.5f, context.Time + 3f);
            }
        }

        /// <summary>Requests a call (played where the animal stands).</summary>
        public void Call(AnimalAgent a, string clip, float volume, float loop = 0f)
        {
            if (string.IsNullOrEmpty(clip) || Sounds.Count >= 16) return;
            Sounds.Add(new SoundRequest { Agent = a, Clip = clip, Volume = volume, Loop = loop });
        }

        // ------------------------------------------------------------------ mover

        private void Move(AnimalAgent a, float dt, in WildlifeContext context)
        {
            SpeciesInfo info = a.Info;
            bool flying = a.Mode == BrainMode.Fly || a.Altitude > 0.05f;
            if (flying && AnimalBrain.FlyStep(this, a, dt))
            {
                SampleGround(a, true);
                a.Y = a.GroundY + a.Altitude;
                return;
            }

            float turn = info.TurnRate * (a.Running ? 1.6f : 1f);
            if (a.HasTarget && a.WantedSpeed > 0f)
            {
                float tx = a.TargetX - a.X, tz = a.TargetZ - a.Z;
                float dist = (float)Math.Sqrt(tx * tx + tz * tz);
                float arrive = Math.Max(0.25f, a.Speed * 0.35f);
                if (dist <= arrive)
                {
                    a.HasTarget = false;
                    a.Arrived = true;
                    a.WantedSpeed = 0f;
                }
                else
                {
                    float want = (float)(Math.Atan2(tx, tz) * 180.0 / Math.PI);
                    a.Yaw = MoveTowardsAngle(a.Yaw, want, turn * dt);
                    float error = Math.Abs(DeltaAngle(a.Yaw, want));
                    float target = a.WantedSpeed * (error < 50f ? 1f : error < 100f ? 0.45f : 0.15f);
                    // Slow down near the target (no overshoot and no circling).
                    target = Math.Min(target, Math.Max(info.WalkSpeed * 0.5f, dist * 1.5f));
                    a.Speed = MoveTowards(a.Speed, target, (a.Running ? 5f : 1.6f) * dt);
                }
            }
            else
            {
                a.Speed = MoveTowards(a.Speed, 0f, (a.Speed > info.WalkSpeed * 1.5f ? 5f : 2f) * dt);
                if (a.HasFace) a.Yaw = MoveTowardsAngle(a.Yaw, a.FaceYaw, turn * 0.6f * dt);
            }

            float rad = a.Yaw * Mathf.Deg2Rad;
            float fx = (float)Math.Sin(rad), fz = (float)Math.Cos(rad);
            float nx = a.X + fx * a.Speed * dt, nz = a.Z + fz * a.Speed * dt;

            // Herd separation (within the group only; groups are small).
            AnimalGroup g = a.Group;
            if (g != null && info.Separation > 0f)
            {
                for (int i = 0; i < g.Members.Count; i++)
                {
                    AnimalAgent o = g.Members[i];
                    if (o == a || o.Altitude > 0.5f) continue;
                    float ox = nx - o.X, oz = nz - o.Z;
                    float sep = Math.Max(info.Separation, o.Info.Separation) * 0.8f;
                    float d2 = ox * ox + oz * oz;
                    if (d2 >= sep * sep || d2 < 1e-6f) continue;
                    float d = (float)Math.Sqrt(d2);
                    float push = (sep - d) * Math.Min(1f, dt * 2f);
                    nx += ox / d * push;
                    nz += oz / d * push;
                }
            }

            // Never inside the traveller.
            if (context.HasTraveller)
            {
                float px = nx - context.TravellerX, pz = nz - context.TravellerZ;
                float d2 = px * px + pz * pz;
                const float body = 0.9f;
                if (d2 < body * body && d2 > 1e-6f)
                {
                    float d = (float)Math.Sqrt(d2);
                    nx = context.TravellerX + px / d * body;
                    nz = context.TravellerZ + pz / d * body;
                }
            }

            // Events keep a predator a gap away from its prey: a step that would close it is not taken.
            if (a.Avoid != null && a.AvoidGap > 0f)
            {
                float gap2 = a.AvoidGap * a.AvoidGap;
                foreach (AnimalAgent prey in a.Avoid.Members)
                {
                    float qx = nx - prey.X, qz = nz - prey.Z;
                    float before = (a.X - prey.X) * (a.X - prey.X) + (a.Z - prey.Z) * (a.Z - prey.Z);
                    if (qx * qx + qz * qz < gap2 && qx * qx + qz * qz < before)
                    {
                        nx = a.X;
                        nz = a.Z;
                        a.Speed = Math.Min(a.Speed, info.WalkSpeed);
                        break;
                    }
                }
            }

            float moved2 = (nx - a.X) * (nx - a.X) + (nz - a.Z) * (nz - a.Z);
            if (moved2 > 1e-8f && Ground != null)
            {
                if (Ground.IsWater(nx, nz))
                {
                    a.Stop();
                    a.Speed = 0f;
                    return;
                }

                float h = Ground.Height(nx, nz);
                float step = (float)Math.Sqrt(moved2);
                if (step > 0.01f && (h - a.GroundY) / step > Math.Max(0.6f, info.MaxSlope * 1.6f) && a.DistanceTo(a.GroundX, a.GroundZ) < 0.01f)
                {
                    // Too steep up: stop and let the brain choose again.
                    a.Stop();
                    a.Speed = 0f;
                    return;
                }

                a.X = nx;
                a.Z = nz;
                a.GroundX = nx;
                a.GroundZ = nz;
                a.GroundY = h;
            }

            a.Y = a.GroundY + a.Altitude;
        }

        /// <summary>Samples the ground under the animal when it has moved since the last sample.</summary>
        internal void SampleGround(AnimalAgent a, bool force)
        {
            if (Ground == null) return;
            if (!force && Math.Abs(a.GroundX - a.X) < 0.02f && Math.Abs(a.GroundZ - a.Z) < 0.02f) return;
            a.GroundX = a.X;
            a.GroundZ = a.Z;
            a.GroundY = Ground.Height(a.X, a.Z);
        }

        /// <summary>The slope pitch of an animal of body length <paramref name="length"/>, in degrees (nose up positive).</summary>
        public float PitchAt(AnimalAgent a, float length)
        {
            if (Ground == null || length < 0.05f) return 0f;
            float rad = a.Yaw * Mathf.Deg2Rad;
            float fx = (float)Math.Sin(rad) * length * 0.5f, fz = (float)Math.Cos(rad) * length * 0.5f;
            float hf = Ground.Height(a.X + fx, a.Z + fz), hb = Ground.Height(a.X - fx, a.Z - fz);
            float p = (float)(Math.Atan2(hf - hb, length) * 180.0 / Math.PI);
            return Math.Max(-25f, Math.Min(25f, p));
        }

        /// <summary>True when an animal may stand at (x, z): dry, not blocked, out of the road corridor (unless <paramref name="allowRoad"/>).</summary>
        public bool CanStand(float x, float z, float radius, bool allowRoad = false)
        {
            if (Ground != null && (Ground.IsWater(x, z) || Ground.IsBlocked(x, z, radius))) return false;
            if (!allowRoad && Road != null && Road.Nearest(x, z, out _, out _, out _, out _, out float d) && d < Corridor) return false;
            return true;
        }

        // ------------------------------------------------------------------ math

        public static float MoveTowards(float v, float target, float maxDelta) =>
            Math.Abs(target - v) <= maxDelta ? target : v + Math.Sign(target - v) * maxDelta;

        public static float DeltaAngle(float a, float b)
        {
            float d = (b - a) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }

        public static float MoveTowardsAngle(float a, float b, float maxDelta)
        {
            float d = DeltaAngle(a, b);
            if (Math.Abs(d) <= maxDelta) return b;
            return a + Math.Sign(d) * maxDelta;
        }

        /// <summary>A fresh seed for an event or a visitor.</summary>
        internal ulong NextSeed() => seedBase ^ ((ulong)(++serial) * 0xD6E8FEB86659FD93UL);
    }
}
