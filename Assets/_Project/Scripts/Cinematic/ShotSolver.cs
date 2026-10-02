using System;
using UnityEngine;

namespace MedievalZen.Cinematic
{
    /// <summary>
    /// What the cinematic camera may ask about the world, injected so the shot logic stays pure: the scene implements it
    /// over <c>IWorldSurface</c>, physics and the tree data; the offline harness over the generator.
    /// </summary>
    public interface ICinematicQueries
    {
        /// <summary>Ground height at a position (x, z used).</summary>
        float GroundHeight(Vector3 p);

        /// <summary>Lake level around a position, negative when there is none.</summary>
        float WaterHeight(Vector3 p);

        /// <summary>Trees per 100 m² around a position.</summary>
        float TreeDensity(Vector3 p);

        /// <summary>True when something solid (ground, building, prop, tree) blocks the sight line from <paramref name="from"/> to <paramref name="to"/>.</summary>
        bool SightBlocked(Vector3 from, Vector3 to);

        /// <summary>True when a position is inside (or within <paramref name="pad"/> metres of) a tree trunk or crown.</summary>
        bool InsideTree(Vector3 p, float pad);

        /// <summary>True when the world around a position exists at full detail (the endless road's built near tiles).</summary>
        bool InBuiltArea(Vector3 p);
    }

    /// <summary>A camera pose: where it is, what it looks at, its vertical field of view in degrees.</summary>
    public struct CameraPose
    {
        /// <summary>Camera position.</summary>
        public Vector3 Position;
        /// <summary>Point the camera looks at.</summary>
        public Vector3 LookAt;
        /// <summary>Vertical field of view in degrees.</summary>
        public float Fov;

        /// <summary>Unit view direction.</summary>
        public Vector3 Forward => (LookAt - Position).normalized;
    }

    /// <summary>Everything a shot needs to know this frame, as plain values (reused; no allocation per frame).</summary>
    public sealed class ShotInputs
    {
        /// <summary>The traveller's feet.</summary>
        public Vector3 Traveller;
        /// <summary>Unit horizontal direction of travel (or the body's facing).</summary>
        public Vector3 Heading = Vector3.forward;
        /// <summary>Walking speed, m/s.</summary>
        public float Speed;
        /// <summary>The road around the traveller (may be unusable: then a straight line along <see cref="Heading"/>).</summary>
        public RoadPath Path;
        /// <summary>The traveller is seated.</summary>
        public bool Seated;
        /// <summary>Seated: the hip joint.</summary>
        public Vector3 Hip;
        /// <summary>Seated: the direction the traveller faces.</summary>
        public Vector3 Facing = Vector3.forward;
        /// <summary>A camp fire is near the seat.</summary>
        public bool HasFire;
        /// <summary>The fire.</summary>
        public Vector3 Fire;
        /// <summary>A distant subject for the vista shot exists.</summary>
        public bool HasSubject;
        /// <summary>The vista subject.</summary>
        public Vector3 Subject;
        /// <summary>Unit direction towards the sun.</summary>
        public Vector3 SunDirection = new Vector3(0f, 0.5f, 0.866f);
        /// <summary>Screen width / height.</summary>
        public float Aspect = 16f / 9f;
        /// <summary>World queries.</summary>
        public ICinematicQueries Queries;

        /// <summary>Copies every field from <paramref name="other"/> (the path and queries by reference).</summary>
        public void CopyFrom(ShotInputs other)
        {
            Traveller = other.Traveller;
            Heading = other.Heading;
            Speed = other.Speed;
            Path = other.Path;
            Seated = other.Seated;
            Hip = other.Hip;
            Facing = other.Facing;
            HasFire = other.HasFire;
            Fire = other.Fire;
            HasSubject = other.HasSubject;
            Subject = other.Subject;
            SunDirection = other.SunDirection;
            Aspect = other.Aspect;
            Queries = other.Queries;
        }

        /// <summary>Arc length of the traveller on <see cref="Path"/> (0 when there is no path).</summary>
        public float TravellerS => Path != null && Path.IsUsable ? Path.Project(Traveller) : 0f;

        /// <summary>The point the shots frame: the upper body (standing) or the seated chest.</summary>
        public Vector3 SubjectPoint => Seated ? Hip + Vector3.up * 0.4f : Traveller + Vector3.up * 1.15f;

        /// <summary>The chest and head points the sight-line checks aim at.</summary>
        public void SightPoints(out Vector3 chest, out Vector3 head)
        {
            if (Seated)
            {
                chest = Hip + Vector3.up * 0.35f;
                head = Hip + Vector3.up * 0.68f;
            }
            else
            {
                chest = Traveller + Vector3.up * 1.15f;
                head = Traveller + Vector3.up * 1.6f;
            }
        }
    }

    /// <summary>The fixed choices of one shot, made when it starts (side, random picks, anchor points). Reused.</summary>
    public sealed class ShotPlan
    {
        /// <summary>The shot.</summary>
        public ShotType Type;
        /// <summary>Run length in seconds.</summary>
        public float Duration;
        /// <summary>+1 = the camera right of the direction of travel (or of the seated facing), −1 = left.</summary>
        public int Side = 1;
        /// <summary>Crane downwards instead of upwards.</summary>
        public bool Reverse;
        /// <summary>Random picks 0..1 that vary distances, heights and lenses within the shot's ranges.</summary>
        public float A, B, C, D;
        /// <summary>A fixed camera point (tripod, low grass), scene space.</summary>
        public Vector3 Anchor;
        /// <summary>The vista subject, scene space.</summary>
        public Vector3 Subject;
        /// <summary>A fixed point the camera mostly looks at (low grass), scene space.</summary>
        public Vector3 Aim;
        /// <summary>Speed when the shot started.</summary>
        public float Speed;

        /// <summary>Copies <paramref name="other"/>.</summary>
        public void CopyFrom(ShotPlan other)
        {
            Type = other.Type;
            Duration = other.Duration;
            Side = other.Side;
            Reverse = other.Reverse;
            A = other.A;
            B = other.B;
            C = other.C;
            D = other.D;
            Anchor = other.Anchor;
            Subject = other.Subject;
            Aim = other.Aim;
            Speed = other.Speed;
        }

        /// <summary>Floating-origin shift.</summary>
        public void Shift(Vector3 delta)
        {
            Anchor += delta;
            Subject += delta;
            Aim += delta;
        }
    }

    /// <summary>
    /// The shots as geometry: <see cref="Plan"/> makes a shot's fixed choices, <see cref="Evaluate"/> gives the desired
    /// camera position, look target and lens at shot time t from plain inputs (traveller, road around them, seat, subject).
    /// Pure; the director damps and validates what it returns.
    /// </summary>
    public static class ShotSolver
    {
        private static readonly Vector3 Up = Vector3.up;

        /// <summary>Fills <paramref name="plan"/> for a new shot of <paramref name="type"/> lasting <paramref name="duration"/> seconds.</summary>
        public static void Plan(ShotPlan plan, ShotType type, float duration, ShotInputs input, System.Random random)
        {
            plan.Type = type;
            plan.Duration = duration;
            plan.A = (float)random.NextDouble();
            plan.B = (float)random.NextDouble();
            plan.C = (float)random.NextDouble();
            plan.D = (float)random.NextDouble();
            plan.Reverse = random.NextDouble() < 0.35;
            plan.Speed = Math.Max(0.6f, input.Speed);
            plan.Subject = input.HasSubject ? input.Subject : DefaultSubject(input);
            float sT = input.TravellerS;

            switch (type)
            {
                case ShotType.SideTracking:
                    plan.Side = ChooseSide(input, sT, 0f, Mathf.Lerp(9f, 18f, plan.A), random);
                    break;
                case ShotType.RoadsideTripod:
                {
                    float along = Mathf.Lerp(18f, 28f, plan.A);
                    float offset = Mathf.Lerp(5f, 8f, plan.B);
                    plan.Side = ChooseSide(input, sT, along, offset, random);
                    plan.Anchor = OffRoad(input, sT, along, plan.Side * offset, Mathf.Lerp(1.0f, 1.5f, plan.C), 0.6f);
                    break;
                }

                case ShotType.LowGrass:
                {
                    // Low in the grass beside the road ahead, looking back up the road: the traveller walks towards the lens
                    // and out past the edge of the frame (the view barely pans).
                    float along = Mathf.Lerp(10f, 14f, plan.A);
                    float offset = Mathf.Lerp(2.6f, 3.4f, plan.B);
                    plan.Side = ChooseSide(input, sT, along, offset, random);
                    plan.Anchor = OffRoad(input, sT, along, plan.Side * offset, 0.35f, 0.25f);
                    plan.Aim = Road(input, sT, along - 7f) + Up * 0.9f;
                    break;
                }

                case ShotType.RestProfile:
                case ShotType.RestPushIn:
                case ShotType.RestOverShoulder:
                case ShotType.RestSky:
                case ShotType.RestFire:
                    plan.Side = ChooseRestSide(input, random);
                    break;
                default:
                    plan.Side = random.NextDouble() < 0.5 ? -1 : 1;
                    break;
            }
        }

        /// <summary>True when a shot has run its course early (the traveller has walked well past a fixed camera).</summary>
        public static bool IsSpent(ShotPlan plan, ShotInputs input)
        {
            if (plan.Type != ShotType.RoadsideTripod && plan.Type != ShotType.LowGrass) return false;
            Vector3 toTraveller = input.Traveller - plan.Anchor;
            toTraveller.y = 0f;
            float past = Vector3.Dot(toTraveller, input.Heading);
            return past > (plan.Type == ShotType.LowGrass ? 2f : 42f);
        }

        /// <summary>The desired camera pose of <paramref name="plan"/> at <paramref name="t"/> seconds into the shot.</summary>
        public static CameraPose Evaluate(ShotPlan plan, float t, ShotInputs input)
        {
            float u = plan.Duration > 0f ? Mathf.Clamp01(t / plan.Duration) : 0f;
            float e = CinematicMath.Smooth(u);
            ShotDefinition def = ShotLibrary.Get(plan.Type);
            Vector3 subject = input.SubjectPoint;
            float sT = input.TravellerS;
            Vector3 h = CinematicMath.Planar(input.Heading, Vector3.forward);
            Vector3 camera;
            float fov;
            Vector3 look;

            switch (plan.Type)
            {
                case ShotType.TrailingWide:
                {
                    float back = Mathf.Lerp(13f, 17f, plan.A) + 2.5f * u;
                    float height = Mathf.Lerp(4.5f, 7.5f, plan.B);
                    float lateral = plan.Side * Mathf.Lerp(1.5f, 3.5f, plan.C) * (1f - 1.2f * u);
                    camera = OnRoad(input, sT, -back, lateral, height);
                    fov = Mathf.Lerp(def.FovMin + 2f, def.FovMax, plan.D) - 3f * e;
                    look = CinematicMath.FrameAt(camera, subject + h * 2f, h, fov, input.Aspect, 0f, -0.2f);
                    break;
                }

                case ShotType.SideTracking:
                {
                    float offset = Mathf.Lerp(9f, 18f, plan.A);
                    float height = Mathf.Lerp(1.6f, 3.5f, plan.B);
                    float along = Mathf.Lerp(-4f, 4f, u);
                    camera = OnRoadOrGround(input, sT, along, plan.Side * offset, height);
                    fov = Mathf.Lerp(def.FovMin, def.FovMax, plan.C);
                    look = CinematicMath.FrameAt(camera, subject, h, fov, input.Aspect, 1f / 3f, -0.1f);
                    break;
                }

                case ShotType.Leading:
                {
                    float ahead = Math.Max(7f, Mathf.Lerp(16f, 22f, plan.A) - plan.Speed * 0.4f * t);
                    float lateral = plan.Side * Mathf.Lerp(1.0f, 2.2f, plan.B);
                    camera = OnRoad(input, sT, ahead, lateral, Mathf.Lerp(1.5f, 2.3f, plan.C));
                    fov = Mathf.Lerp(def.FovMin, def.FovMax, plan.D);
                    look = CinematicMath.FrameAt(camera, subject, Vector3.zero, fov, input.Aspect, 0f, -0.1f);
                    break;
                }

                case ShotType.RoadsideTripod:
                {
                    camera = plan.Anchor;
                    float distance = Vector3.Distance(camera, subject);
                    fov = Mathf.Clamp(Mathf.Lerp(def.FovMax - 4f, def.FovMin, Mathf.InverseLerp(10f, 45f, distance)), def.FovMin, def.FovMax);
                    look = CinematicMath.FrameAt(camera, subject, h, fov, input.Aspect, 0.25f, 0f);
                    break;
                }

                case ShotType.Crane:
                {
                    // Low behind → high and wide (or back down): at the top the lens widens and the traveller sinks towards the
                    // bottom of the frame so the country ahead and the horizon fill it, rather than the road at their feet.
                    float k = plan.Reverse ? 1f - e : e;
                    float back = Mathf.Lerp(7f, 24f, k);
                    float lateral = plan.Side * Mathf.Lerp(2f, 6f, k);
                    float height = Mathf.Lerp(1.0f, Mathf.Lerp(10f, 14f, plan.A), k);
                    camera = OnRoad(input, sT, -back, lateral, height);
                    fov = Mathf.Lerp(def.FovMin, def.FovMax, k);
                    look = CinematicMath.FrameAt(camera, subject + h * 2f, h, fov, input.Aspect, 0f, Mathf.Lerp(0f, -0.5f, k));
                    break;
                }

                case ShotType.Vista:
                {
                    Vector3 toSubject = CinematicMath.Planar(plan.Subject - input.Traveller, h);
                    Vector3 side = CinematicMath.RightOf(toSubject) * plan.Side;
                    camera = input.Traveller - toSubject * Mathf.Lerp(10f, 15f, plan.A) + side * (Mathf.Lerp(2f, 5f, plan.B) + 1.5f * u)
                             + Up * Mathf.Lerp(2.5f, 5f, plan.C);
                    fov = Mathf.Lerp(def.FovMin, def.FovMax, plan.D);
                    // Aim between the far subject and the traveller: the subject above the centre, the traveller in the lower third.
                    Vector3 dirSubject = (plan.Subject - camera).normalized;
                    Vector3 dirTraveller = (subject - camera).normalized;
                    look = camera + (dirSubject * 0.62f + dirTraveller * 0.38f).normalized * 50f;
                    break;
                }

                case ShotType.LowGrass:
                {
                    camera = plan.Anchor;
                    fov = Mathf.Lerp(def.FovMin, def.FovMax, plan.C);
                    look = Vector3.Lerp(plan.Aim, subject + Up * 0.1f, 0.3f);
                    break;
                }

                default:
                    RestPose(plan, u, e, t, input, def, out camera, out look, out fov);
                    break;
            }

            return new CameraPose { Position = camera, LookAt = look, Fov = Mathf.Clamp(fov, 26f, 62f) };
        }

        private static void RestPose(ShotPlan plan, float u, float e, float t, ShotInputs input, ShotDefinition def, out Vector3 camera, out Vector3 look, out float fov)
        {
            Vector3 hip = input.Hip;
            Vector3 f = CinematicMath.Planar(input.Facing, Vector3.forward);
            Vector3 r = CinematicMath.RightOf(f) * plan.Side;
            Vector3 subject = input.SubjectPoint;
            Vector3 view = hip + f * 40f + Up * 1.0f;

            switch (plan.Type)
            {
                case ShotType.RestPushIn:
                {
                    Vector3 from = hip - f * Mathf.Lerp(6.5f, 8f, plan.A) + r * 1.2f + Up * 1.9f;
                    Vector3 to = hip - f * 3.2f + r * 0.8f + Up * 1.35f;
                    camera = Vector3.Lerp(from, to, e);
                    fov = Mathf.Lerp(def.FovMax, def.FovMin, e);
                    // The traveller low and a little off-centre, the view they face filling the rest.
                    look = CinematicMath.FrameAt(camera, subject, Vector3.zero, fov, input.Aspect, -0.12f * plan.Side, -0.38f);
                    break;
                }

                case ShotType.RestProfile:
                    camera = hip + r * Mathf.Lerp(8f, 12f, plan.A) + f * (Mathf.Lerp(1f, 3f, plan.B) + 0.8f * u) + Up * 1.5f;
                    fov = Mathf.Lerp(def.FovMin, def.FovMax, plan.C);
                    look = CinematicMath.FrameAt(camera, subject, f, fov, input.Aspect, 1f / 3f, 0f);
                    break;
                case ShotType.RestOrbit:
                {
                    float start = CinematicMath.YawOf(-f) + plan.Side * Mathf.Lerp(30f, 60f, plan.A);
                    float yaw = start + plan.Side * 1.6f * t;
                    camera = hip + CinematicMath.FromYaw(yaw) * Mathf.Lerp(12f, 16f, plan.B) + Up * Mathf.Lerp(6f, 9f, plan.C);
                    fov = Mathf.Lerp(def.FovMin, def.FovMax, plan.D);
                    look = subject + f * 2f;
                    break;
                }

                case ShotType.RestOverShoulder:
                    camera = hip - f * 1.7f + r * (0.55f + 0.2f * u) + Up * 0.85f;
                    fov = Mathf.Lerp(def.FovMin, def.FovMax, plan.A);
                    look = hip + f * 40f + Up * 0.6f;
                    break;
                case ShotType.RestSky:
                {
                    Vector3 sun = CinematicMath.Planar(input.SunDirection, f);
                    Vector3 dir = Vector3.Dot(sun, f) > 0.2f ? CinematicMath.Planar(sun + f, f) : f;
                    camera = hip - dir * 4f + CinematicMath.RightOf(dir) * plan.Side * 1.2f + Up * 0.6f;
                    fov = Mathf.Lerp(def.FovMin, def.FovMax, plan.A);
                    float pitch = Mathf.Lerp(6f, 9f, plan.B) * Mathf.Deg2Rad;
                    look = camera + (dir * Mathf.Cos(pitch) + Up * Mathf.Sin(pitch)) * 50f;
                    break;
                }

                case ShotType.RestFire:
                {
                    Vector3 fire = input.HasFire ? input.Fire : hip + f * 1.4f;
                    Vector3 across = CinematicMath.Planar(fire - hip, f);
                    camera = fire + across * 2.4f + CinematicMath.RightOf(across) * plan.Side * 0.8f + Up * 0.85f;
                    fov = Mathf.Lerp(def.FovMin, def.FovMax, plan.A);
                    look = subject;
                    break;
                }

                default:
                    camera = hip - f * 4f + Up * 1.5f;
                    fov = 45f;
                    look = subject;
                    break;
            }
        }

        // ------------------------------------------------------------------ placement helpers

        /// <summary>Road position <paramref name="s"/> metres from the traveller along the path (or a straight line along the heading).</summary>
        public static Vector3 Road(ShotInputs input, float sT, float s)
        {
            if (input.Path != null && input.Path.IsUsable) return input.Path.At(sT + s);
            return input.Traveller + CinematicMath.Planar(input.Heading, Vector3.forward) * s;
        }

        /// <summary>Unit horizontal road direction at <paramref name="s"/> metres from the traveller.</summary>
        public static Vector3 RoadTangent(ShotInputs input, float sT, float s)
        {
            if (input.Path != null && input.Path.IsUsable) return input.Path.TangentAt(sT + s);
            return CinematicMath.Planar(input.Heading, Vector3.forward);
        }

        /// <summary>A point beside the road: <paramref name="lateral"/> metres right of it, <paramref name="height"/> above the road.</summary>
        private static Vector3 OnRoad(ShotInputs input, float sT, float s, float lateral, float height)
        {
            Vector3 p = Road(input, sT, s);
            Vector3 right = CinematicMath.RightOf(RoadTangent(input, sT, s));
            return p + right * lateral + Up * height;
        }

        /// <summary>Like <see cref="OnRoad"/>, but never lower than 60 % of the height above the ground under the camera.</summary>
        private static Vector3 OnRoadOrGround(ShotInputs input, float sT, float s, float lateral, float height)
        {
            Vector3 p = OnRoad(input, sT, s, lateral, height);
            if (input.Queries != null)
            {
                float ground = input.Queries.GroundHeight(p);
                p.y = Math.Max(p.y, ground + height * 0.6f);
            }

            return p;
        }

        /// <summary>
        /// A fixed point beside the road at <paramref name="height"/> above the ground there (road height when no queries), and at
        /// least <paramref name="minAboveRoad"/> above the road so a camera below a road embankment still sees over it.
        /// </summary>
        private static Vector3 OffRoad(ShotInputs input, float sT, float s, float lateral, float height, float minAboveRoad)
        {
            Vector3 p = OnRoad(input, sT, s, lateral, 0f);
            float road = p.y;
            float ground = input.Queries != null ? input.Queries.GroundHeight(p) : p.y;
            p.y = Math.Max(ground + height, road + minAboveRoad);
            return p;
        }

        /// <summary>A subject when no landmark is given: the road 140 m ahead (or straight ahead).</summary>
        private static Vector3 DefaultSubject(ShotInputs input)
        {
            float sT = input.TravellerS;
            return Road(input, sT, 140f) + Up * 2f;
        }

        /// <summary>
        /// Which side of the road a camera <paramref name="offset"/> metres off it should stand on: the side that looks across
        /// the traveller towards falling ground or water, avoiding a bank rising above the road (a wall) and woodland at the
        /// camera spot. Returns +1 (right of travel) or −1; a coin flip when even.
        /// </summary>
        public static int ChooseSide(ShotInputs input, float sT, float along, float offset, System.Random random)
        {
            ICinematicQueries q = input.Queries;
            if (q == null) return random.NextDouble() < 0.5 ? -1 : 1;
            Vector3 basePoint = Road(input, sT, along);
            Vector3 right = CinematicMath.RightOf(RoadTangent(input, sT, along));
            float best = float.NegativeInfinity;
            int side = 1;
            for (int s = -1; s <= 1; s += 2)
            {
                float score = SideScore(q, basePoint, right * s, offset);
                if (score > best + 0.05f || (Math.Abs(score - best) <= 0.05f && random.NextDouble() < 0.5))
                {
                    best = Math.Max(best, score);
                    side = s;
                }
            }

            return side;
        }

        /// <summary>Score of putting the camera at <paramref name="basePoint"/> + <paramref name="toCamera"/> × offset (see <see cref="ChooseSide"/>).</summary>
        public static float SideScore(ICinematicQueries q, Vector3 basePoint, Vector3 toCamera, float offset)
        {
            Vector3 camera = basePoint + toCamera * offset;
            Vector3 far = basePoint - toCamera * 40f;
            float road = basePoint.y;
            float gCamera = q.GroundHeight(camera);
            float gFar = q.GroundHeight(far);
            float water = q.WaterHeight(far);
            float score = 0.8f * Mathf.Clamp((road - gFar) / 8f, -1f, 1f);
            if (water >= 0f && water > gFar - 0.1f) score += 0.9f;
            score -= 1.2f * Mathf.Clamp01((gCamera - road - 2f) / 6f);
            score -= 0.8f * Mathf.Clamp01(q.TreeDensity(camera) / 2f);
            return score;
        }

        private static int ChooseRestSide(ShotInputs input, System.Random random)
        {
            ICinematicQueries q = input.Queries;
            if (q == null) return random.NextDouble() < 0.5 ? -1 : 1;
            Vector3 f = CinematicMath.Planar(input.Facing, Vector3.forward);
            Vector3 right = CinematicMath.RightOf(f);
            float left = -0.5f * Mathf.Clamp01(q.TreeDensity(input.Hip - right * 8f) / 2f) - Mathf.Clamp01((q.GroundHeight(input.Hip - right * 8f) - input.Hip.y - 1.5f) / 4f);
            float rightScore = -0.5f * Mathf.Clamp01(q.TreeDensity(input.Hip + right * 8f) / 2f) - Mathf.Clamp01((q.GroundHeight(input.Hip + right * 8f) - input.Hip.y - 1.5f) / 4f);
            if (Math.Abs(left - rightScore) < 0.05f) return random.NextDouble() < 0.5 ? -1 : 1;
            return rightScore > left ? 1 : -1;
        }
    }

    /// <summary>
    /// The rules every cinematic pose must keep: above ground and water, out of trees, within the built world and 200 m of the
    /// traveller, the traveller in frame and in sight. Pure, over <see cref="ICinematicQueries"/>.
    /// </summary>
    public static class ShotRules
    {
        /// <summary>Clearance above water (m).</summary>
        public const float WaterMargin = 0.6f;

        /// <summary>Clearance the damped camera is held to at the very least (m), whatever the shot (the lowest shot margin).</summary>
        public const float SafetyMargin = 0.25f;

        /// <summary>Farthest the camera may be from the traveller (m).</summary>
        public const float MaxDistance = 200f;

        /// <summary>A planned pose the clearance had to lift more than this (m) is a bad shot (the camera was buried in a hill).</summary>
        public const float MaxLift = 3.5f;

        /// <summary>Lifts a pose clear of the ground and water; <paramref name="lift"/> is how far it moved.</summary>
        public static CameraPose Clear(CameraPose pose, ShotType type, ICinematicQueries q, out float lift)
        {
            lift = 0f;
            if (q == null) return pose;
            float margin = ShotLibrary.Get(type).GroundMargin;
            Vector3 before = pose.Position;
            pose.Position = CinematicMath.Clear(pose.Position, q.GroundHeight(pose.Position), q.WaterHeight(pose.Position), margin, WaterMargin);
            lift = pose.Position.y - before.y;
            return pose;
        }

        /// <summary>True when the camera may stand at <paramref name="camera"/>: in the built world, near the traveller, not in a tree.</summary>
        public static bool PlaceOk(Vector3 camera, Vector3 traveller, ICinematicQueries q)
        {
            if ((camera - traveller).sqrMagnitude > MaxDistance * MaxDistance) return false;
            if (q == null) return true;
            return q.InBuiltArea(camera) && !q.InsideTree(camera, 0.4f);
        }

        /// <summary>True when the chest or the head of the traveller is in sight from <paramref name="camera"/>.</summary>
        public static bool CanSee(Vector3 camera, ShotInputs input)
        {
            ICinematicQueries q = input.Queries;
            if (q == null) return true;
            input.SightPoints(out Vector3 chest, out Vector3 head);
            return !q.SightBlocked(camera, head) || !q.SightBlocked(camera, chest);
        }

        /// <summary>
        /// How many of <paramref name="samples"/> moments of the shot (start … end) are good: the place is allowed, the clearance
        /// lift is small, the traveller is in frame and in sight. Travel shots predict the traveller along the road at the plan's
        /// speed. <paramref name="scratch"/> is overwritten.
        /// </summary>
        public static int CountGood(ShotPlan plan, ShotInputs input, ShotInputs scratch, int samples = 3)
        {
            scratch.CopyFrom(input);
            float sT = input.TravellerS;
            int good = 0;
            for (int k = 0; k < samples; k++)
            {
                float t = samples > 1 ? plan.Duration * k / (samples - 1) : 0f;
                if (!input.Seated)
                {
                    float s = plan.Speed * t;
                    Vector3 p = ShotSolver.Road(input, sT, s);
                    if (input.Path == null || !input.Path.IsUsable) p.y = input.Traveller.y;
                    scratch.Traveller = p;
                    scratch.Heading = ShotSolver.RoadTangent(input, sT, s);
                }

                CameraPose pose = Clear(ShotSolver.Evaluate(plan, t, scratch), plan.Type, input.Queries, out float lift);
                if (lift > MaxLift || !PlaceOk(pose.Position, scratch.Traveller, input.Queries)) continue;
                Vector3 screen = CinematicMath.ScreenPosition(pose.Position, pose.LookAt, scratch.SubjectPoint, pose.Fov, input.Aspect);
                if (!CinematicMath.InFrame(screen, 0.08f)) continue;
                if (!CanSee(pose.Position, scratch)) continue;
                good++;
            }

            return good;
        }

        /// <summary>Farthest apart two camera positions may be for a blend instead of a dip through black (m).</summary>
        public const float BlendDistance = 12f;

        /// <summary>Largest turn of the view for a blend instead of a dip (degrees).</summary>
        public const float BlendAngle = 45f;

        /// <summary>True when moving from one pose to the next can be a slow blend (near, similar direction) rather than a dip to black.</summary>
        public static bool ShouldBlend(CameraPose from, CameraPose to)
        {
            if ((from.Position - to.Position).sqrMagnitude > BlendDistance * BlendDistance) return false;
            return CinematicMath.AngleBetween(from.Forward, to.Forward) < BlendAngle;
        }
    }
}
