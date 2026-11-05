using System;
using System.Globalization;
using UnityEngine;

namespace MedievalZen.Player
{
    /// <summary>The three gaits of the traveller's horse.</summary>
    public enum HorseGait
    {
        Walk = 0,
        Trot = 1,
        Canter = 2,
    }

    /// <summary>Which of the horse controller's clips shows a speed (the controller has idle, walk and run; no trot).</summary>
    public enum HorseClip
    {
        Idle = 0,
        Walk = 1,
        Run = 2,
    }

    /// <summary>
    /// How the motor turns a ridden horse (<see cref="RidingMath"/>): the yaw rate is the speed over a turn radius that grows with the
    /// speed, so a walk turns tight and a canter wide, and a request far off the facing slows the horse to an arc at a walk instead of
    /// stopping it to spin.
    /// </summary>
    public struct RideTurn
    {
        /// <summary>Turn radius (metres) at the walk, trot and canter speeds; linear between, proportional beyond the canter.</summary>
        public float WalkRadius, TrotRadius, CanterRadius;

        /// <summary>Slowest and fastest yaw rate, degrees per second (the slowest lets a nearly stopped horse still come round).</summary>
        public float MinRate, MaxRate;

        /// <summary>A request up to this many degrees off the facing keeps its speed; at <see cref="SlowAngle"/> it is down to <see cref="ArcSpeed"/>.</summary>
        public float FullSpeedAngle, SlowAngle;

        /// <summary>The speed (m/s) a horse keeps while it comes round a large turn: a slow walk, never zero.</summary>
        public float ArcSpeed;

        /// <summary>Gentler than a walker's: m/s².</summary>
        public float Acceleration, Deceleration;

        /// <summary>The validated-by-nobody-yet defaults (a guess the user tunes in Play mode).</summary>
        public static RideTurn Default => new RideTurn
        {
            WalkRadius = 1.6f,
            TrotRadius = 4f,
            CanterRadius = 9f,
            MinRate = 20f,
            MaxRate = 90f,
            FullSpeedAngle = 30f,
            SlowAngle = 120f,
            ArcSpeed = 1.1f,
            Acceleration = 1.6f,
            Deceleration = 2.6f,
        };
    }

    /// <summary>
    /// The horse's gaits as numbers (pure, unit tested in <c>HorseRidingTests</c>): speeds, names and the HUD label, wheel steps,
    /// the turn radius by speed, which clip shows a speed and at what playback rate, the rider's rise and fall, and the speeds the
    /// controller and the auto-traveller ask of the motor. On foot <see cref="TravelSpeed"/> hands back exactly the
    /// auto-traveller's old speed (travel speed × <see cref="WalkPace.TravelScale"/>).
    /// </summary>
    public static class HorseGaits
    {
        /// <summary>Number of gaits.</summary>
        public const int Count = 3;

        /// <summary>Gait speeds, m/s.</summary>
        public const float WalkSpeed = 1.9f, TrotSpeed = 3.6f, CanterSpeed = 6.0f;

        /// <summary>Below this the horse stands (m/s).</summary>
        public const float StandSpeed = 0.15f;

        /// <summary>Clip speeds of the horse controller (<c>SpeciesCatalog</c>: walk 1.5 m/s, run 7.5 m/s) and the playback limits.</summary>
        public const float WalkClipSpeed = 1.5f, RunClipSpeed = 7.5f, MinPlayback = 0.6f, MaxPlayback = 2.3f;

        /// <summary>Auto-travel's default gait.</summary>
        public const HorseGait TravelDefault = HorseGait.Trot;

        /// <summary>The gait clamped into range.</summary>
        public static HorseGait Clamp(int gait) => (HorseGait)Math.Max(0, Math.Min(Count - 1, gait));

        /// <summary>Speed of a gait, m/s.</summary>
        public static float Speed(HorseGait gait) => gait == HorseGait.Canter ? CanterSpeed : gait == HorseGait.Trot ? TrotSpeed : WalkSpeed;

        /// <summary>"Walk", "Trot", "Canter".</summary>
        public static string Name(HorseGait gait) => gait == HorseGait.Canter ? "Canter" : gait == HorseGait.Trot ? "Trot" : "Walk";

        /// <summary>"Trot · 3.6 m/s" (as <see cref="WalkPace.Label"/>).</summary>
        public static string Label(HorseGait gait, float metresPerSecond)
        {
            if (float.IsNaN(metresPerSecond) || float.IsInfinity(metresPerSecond)) metresPerSecond = 0f;
            return Name(gait) + " · " + metresPerSecond.ToString("0.0", CultureInfo.InvariantCulture) + " m/s";
        }

        /// <summary>The gait moved by <paramref name="steps"/> (the wheel's +1 / −1), clamped at walk and canter.</summary>
        public static HorseGait Stepped(HorseGait gait, int steps) => Clamp((int)gait + steps);

        /// <summary>The gait a speed shows (for the clip, the hooves and the rider): walk up to 2.7 m/s, trot up to 4.8, canter beyond.</summary>
        public static HorseGait ShownGait(float speed) => speed < 2.7f ? HorseGait.Walk : speed < 4.8f ? HorseGait.Trot : HorseGait.Canter;

        /// <summary>
        /// The speed manual riding asks for: the gait's, or the canter while the sprint key is held (a canter burst, released back to the
        /// chosen gait).
        /// </summary>
        public static float ManualSpeed(HorseGait gait, bool sprinting) => sprinting ? CanterSpeed : Speed(gait);

        /// <summary>
        /// The auto-traveller's average speed in m/s: the walking benchmark's scale overrides everything (on-foot travel speed × scale,
        /// mounted or not, so benchmarks stay comparable); otherwise the gait's speed when riding, else travel speed × pace.
        /// </summary>
        public static float TravelSpeed(bool riding, HorseGait gait, float travelSpeed, float pace, float benchmarkScale)
        {
            if (benchmarkScale != 1f || !riding) return travelSpeed * WalkPace.TravelScale(pace, benchmarkScale);
            return Speed(gait);
        }

        /// <summary>True when the auto-traveller should use the riding turn (riding, and no benchmark scale).</summary>
        public static bool TravelRides(bool riding, float benchmarkScale) => riding && benchmarkScale == 1f;

        /// <summary>Turn radius at <paramref name="speed"/>: the walk's radius up to the walk speed, linear to the trot's and the canter's, proportional beyond.</summary>
        public static float TurnRadius(float speed, in RideTurn turn)
        {
            if (!(speed > WalkSpeed)) return turn.WalkRadius;
            if (speed <= TrotSpeed) return Lerp(turn.WalkRadius, turn.TrotRadius, (speed - WalkSpeed) / (TrotSpeed - WalkSpeed));
            if (speed <= CanterSpeed) return Lerp(turn.TrotRadius, turn.CanterRadius, (speed - TrotSpeed) / (CanterSpeed - TrotSpeed));
            return turn.CanterRadius * speed / CanterSpeed;
        }

        /// <summary>Yaw rate (degrees per second) the horse may turn at <paramref name="speed"/>: speed / radius, clamped to the turn's limits.</summary>
        public static float TurnRate(float speed, in RideTurn turn)
        {
            float radius = Math.Max(0.1f, TurnRadius(speed, turn));
            float rate = Math.Max(0f, speed) / radius * Mathf.Rad2Deg;
            return Math.Max(turn.MinRate, Math.Min(turn.MaxRate, rate));
        }

        /// <summary>
        /// The clip and playback rate that show <paramref name="speed"/>: idle when standing, the walk clip for the walk and (played fast)
        /// for the trot, the run clip (played slow) for the canter. The controller has no trot clip.
        /// </summary>
        public static HorseClip Clip(float speed, out float playback)
        {
            playback = 1f;
            if (!(speed >= StandSpeed)) return HorseClip.Idle;
            if (ShownGait(speed) == HorseGait.Canter)
            {
                playback = Math.Max(0.75f, Math.Min(MaxPlayback, speed / RunClipSpeed));
                return HorseClip.Run;
            }

            playback = Math.Max(MinPlayback, Math.Min(MaxPlayback, speed / WalkClipSpeed));
            return HorseClip.Walk;
        }

        /// <summary>Stride length (metres per full cycle of the four legs) by gait.</summary>
        public static float StrideLength(HorseGait gait) => gait == HorseGait.Canter ? 3.4f : gait == HorseGait.Trot ? 2.6f : 1.75f;

        /// <summary>Full leg cycles per second at <paramref name="speed"/> (0 standing).</summary>
        public static float CycleRate(float speed) => speed >= StandSpeed ? speed / StrideLength(ShownGait(speed)) : 0f;

        /// <summary>
        /// The rider's rise and fall in metres at <paramref name="phase"/> (0..1 of a leg cycle): a small double nod at the walk, a
        /// two-per-cycle bounce at the trot, one rocking swell per canter stride. Zero mean-ish and at most 4 cm.
        /// </summary>
        public static float Bob(HorseGait gait, float phase)
        {
            double a = 2.0 * Math.PI * phase;
            switch (gait)
            {
                case HorseGait.Trot: return (float)(0.032 * Math.Abs(Math.Sin(a)) - 0.02);
                case HorseGait.Canter: return (float)(0.04 * Math.Sin(a));
                default: return (float)(0.012 * Math.Sin(a * 2.0));
            }
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Max(0f, Math.Min(1f, t));
    }

    /// <summary>
    /// One frame of a ridden horse in the motor's terms (pure): the body turns towards the request at most at the speed-dependent rate
    /// of <see cref="HorseGaits.TurnRate"/>, the speed eases off towards <see cref="RideTurn.ArcSpeed"/> (never to zero) while the
    /// request is far off the facing, and the velocity follows the body's forward. A zero request keeps the facing and eases to a stop.
    /// </summary>
    public static class RidingMath
    {
        public static void Step(ref MotorState state, Vector2 request, float turnSharpness, in RideTurn turn, in MotorTuning tuning, float deltaTime)
        {
            if (deltaTime <= 0f) return;
            float wanted = request.magnitude;
            float targetSpeed = 0f;
            float speed = state.Velocity.magnitude;
            if (wanted > 1e-4f)
            {
                float delta = MotorMath.TurnDelta(state.Yaw, MotorMath.YawOf(request), state.TurnSign);
                float rate = HorseGaits.TurnRate(speed, turn);
                float step = MotorMath.YawStep(delta, turnSharpness, rate, deltaTime);
                state.TurnSign = Mathf.Abs(delta) > 1f ? Mathf.Sign(delta) : 0f;
                state.Yaw = LocomotionMath.WrapAngle(state.Yaw + step);
                float align = MotorMath.Alignment(delta - step, turn.FullSpeedAngle, turn.SlowAngle);
                float arc = Mathf.Min(wanted, turn.ArcSpeed);
                targetSpeed = arc + (wanted - arc) * align;
            }
            else
            {
                state.TurnSign = 0f;
            }

            state.Velocity = MotorMath.SteerVelocity(state.Velocity, MotorMath.FromYaw(state.Yaw), targetSpeed, tuning.VelocityTurnRate, 180f,
                turn.Acceleration, turn.Deceleration, deltaTime);
        }
    }

    /// <summary>Phases of getting on and off the horse.</summary>
    public enum MountPhase
    {
        OnFoot = 0,
        Mounting = 1,
        Riding = 2,
        Dismounting = 3,
    }

    /// <summary>
    /// The mount's state machine (pure): on foot → mounting (a short blend, the body held) → riding → dismounting (a short blend) → on
    /// foot. <see cref="Weight"/> is 0 on foot and 1 astride, eased by smoothstep in between; the rules of when one may mount or
    /// get down live here.
    /// </summary>
    public sealed class MountStateMachine
    {
        /// <summary>Blend times, seconds.</summary>
        public const float MountSeconds = 0.9f, DismountSeconds = 0.8f;

        /// <summary>The horse must be this close (planar metres) to be mounted.</summary>
        public const float MountReach = 2.8f;

        /// <summary>Faster than this (m/s) one does not get down: only at a walk or standing.</summary>
        public const float DismountMaxSpeed = HorseGaits.WalkSpeed + 0.25f;

        /// <summary>The traveller's own horse, left on foot further than this and unseen, is gone (metres).</summary>
        public const float LeaveBehindDistance = 400f;

        private float t;

        public MountPhase Phase { get; private set; }

        /// <summary>0 on foot … 1 astride (smoothstep of the blend).</summary>
        public float Weight
        {
            get
            {
                float x = Phase == MountPhase.Riding ? 1f : Phase == MountPhase.OnFoot ? 0f :
                    Phase == MountPhase.Mounting ? t / MountSeconds : 1f - t / DismountSeconds;
                x = Math.Max(0f, Math.Min(1f, x));
                return x * x * (3f - 2f * x);
            }
        }

        /// <summary>True while astride or getting on/off (the horse is the traveller's mount).</summary>
        public bool Mounted => Phase != MountPhase.OnFoot;

        /// <summary>True while the blend holds the body (no driver may move it).</summary>
        public bool HoldsBody => Phase == MountPhase.Mounting || Phase == MountPhase.Dismounting;

        /// <summary>True when riding controls apply.</summary>
        public bool Riding => Phase == MountPhase.Riding;

        /// <summary>May the traveller get on a horse <paramref name="distance"/> metres away now?</summary>
        public static bool CanMount(MountPhase phase, float distance, bool seated, bool busy) =>
            phase == MountPhase.OnFoot && !seated && !busy && distance <= MountReach;

        /// <summary>May the rider get down at <paramref name="speed"/> m/s now?</summary>
        public static bool CanDismount(MountPhase phase, float speed, bool busy) =>
            phase == MountPhase.Riding && !busy && !(speed > DismountMaxSpeed);

        /// <summary>Is a horse left <paramref name="distance"/> metres away, seen or not, gone?</summary>
        public static bool LeftBehind(float distance, bool visible, bool mounted) => !mounted && !visible && distance > LeaveBehindDistance;

        /// <summary>Starts getting on (false when not on foot).</summary>
        public bool BeginMount()
        {
            if (Phase != MountPhase.OnFoot) return false;
            Phase = MountPhase.Mounting;
            t = 0f;
            return true;
        }

        /// <summary>Starts getting down (false when not riding).</summary>
        public bool BeginDismount()
        {
            if (Phase != MountPhase.Riding) return false;
            Phase = MountPhase.Dismounting;
            t = 0f;
            return true;
        }

        /// <summary>Astride at once (a resumed journey).</summary>
        public void SetRiding()
        {
            Phase = MountPhase.Riding;
            t = 0f;
        }

        /// <summary>On foot at once (the horse vanished, a new journey).</summary>
        public void SetOnFoot()
        {
            Phase = MountPhase.OnFoot;
            t = 0f;
        }

        /// <summary>Advances a blend; returns true on the frame it ends.</summary>
        public bool Update(float dt)
        {
            if (!HoldsBody || !(dt > 0f)) return false;
            t += dt;
            if (Phase == MountPhase.Mounting && t >= MountSeconds)
            {
                Phase = MountPhase.Riding;
                t = 0f;
                return true;
            }

            if (Phase == MountPhase.Dismounting && t >= DismountSeconds)
            {
                Phase = MountPhase.OnFoot;
                t = 0f;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The hooves' rhythm (pure): a leg cycle advancing at <see cref="HorseGaits.CycleRate"/>, with the beats of the gait at fixed
    /// points of the cycle: four even beats at the walk, two at the trot (diagonal pairs), three at the canter followed by the
    /// moment of suspension. <see cref="Update"/> returns how many beats fell in the frame.
    /// </summary>
    public sealed class HoofRhythm
    {
        private static readonly float[] WalkBeats = { 0f, 0.25f, 0.5f, 0.75f };
        private static readonly float[] TrotBeats = { 0f, 0.5f };
        private static readonly float[] CanterBeats = { 0f, 0.2f, 0.4f };

        /// <summary>Position in the leg cycle, 0..1.</summary>
        public float Phase { get; private set; }

        /// <summary>The gait the rhythm is in.</summary>
        public HorseGait Gait { get; private set; }

        /// <summary>Index of the last beat within its cycle (0 = the first beat, the accented one).</summary>
        public int LastBeat { get; private set; }

        /// <summary>Beat positions in the cycle for a gait.</summary>
        public static float[] Beats(HorseGait gait) => gait == HorseGait.Canter ? CanterBeats : gait == HorseGait.Trot ? TrotBeats : WalkBeats;

        /// <summary>Back to the start of a cycle (a standing horse).</summary>
        public void Reset()
        {
            Phase = 0f;
            LastBeat = 0;
        }

        /// <summary>Advances by <paramref name="dt"/> at <paramref name="speed"/> m/s; returns the beats that fell (0 standing).</summary>
        public int Update(float dt, float speed)
        {
            if (!(dt > 0f)) return 0;
            float rate = HorseGaits.CycleRate(speed);
            if (rate <= 0f)
            {
                Phase = 0f;
                return 0;
            }

            Gait = HorseGaits.ShownGait(speed);
            float[] beats = Beats(Gait);
            float from = Phase, to = Phase + Math.Min(0.5f, rate * dt);
            int count = 0;
            // A standing start sounds its first beat at once (the phase begins exactly on beat 0).
            for (int lap = 0; lap < 2; lap++)
            {
                float offset = lap;
                for (int i = 0; i < beats.Length; i++)
                {
                    float b = beats[i] + offset;
                    bool hit = from == 0f && lap == 0 && i == 0 ? true : b > from && b <= to;
                    if (!hit) continue;
                    count++;
                    LastBeat = i;
                }
            }

            Phase = to >= 1f ? to - 1f : to;
            if (Phase == 0f) Phase = 1e-6f;
            return count;
        }
    }

    /// <summary>The camera anchor while riding (pure): exactly zero on foot, else the saddle's rise and the gait's bob, blended in.</summary>
    public static class RideCamera
    {
        /// <summary>Anchor offset for <paramref name="weight"/> (0 on foot) and the rider's rise and bob in metres.</summary>
        public static Vector3 Offset(float weight, float rise, float bob)
        {
            if (!(weight > 0f)) return Vector3.zero;
            return new Vector3(0f, (rise + bob) * Math.Min(1f, weight), 0f);
        }
    }
}
