using System;
using UnityEngine;

namespace MedievalZen.People
{
    /// <summary>
    /// One person's behaviour as plain state (absolute coordinates; no Unity objects, so it is tested offline): walks the round of its
    /// <see cref="PersonPlan"/> out and back at a slow pace, waits at each stop for a seeded time facing the stop's way, sits where the
    /// stop is a bench (<see cref="SitWeight"/> eases in and out), turns to look at the traveller who comes within
    /// <see cref="LookDistance"/> while standing, and goes home (to stop 0) and indoors (<see cref="Indoors"/>) outside its hours. It only
    /// ever moves along the round's straight legs, which the planner checked keep off the road and out of the walls.
    /// </summary>
    public sealed class VillagerBrain
    {
        /// <summary>Walking pace, m/s (slower than the traveller's 1.45 m/s walk: these people are at home).</summary>
        public const float WalkSpeed = 1.05f;

        /// <summary>The traveller within this distance (m) is looked at by a standing person.</summary>
        public const float LookDistance = 7f;

        /// <summary>Degrees per second a person turns.</summary>
        public const float TurnRate = 140f;

        private enum Mode
        {
            Waiting,
            Walking,
            GoingHome,
            Indoors,
        }

        private readonly PersonPlan plan;
        private readonly System.Random rng;
        private Mode mode;
        private int stop;
        private int direction = 1;
        private float wait;

        /// <summary>Creates the brain at a seeded stop of the round, waiting.</summary>
        public VillagerBrain(PersonPlan plan)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            rng = new System.Random(plan.Seed);
            stop = plan.Stops.Length > 0 ? rng.Next(plan.Stops.Length) : 0;
            RouteStop s = plan.Stops[stop];
            X = s.X;
            Z = s.Z;
            Yaw = s.Facing;
            mode = Mode.Waiting;
            wait = (float)(rng.NextDouble() * Mathf.Max(1f, s.MaxSeconds));
            SitWeight = s.Kind == StopKind.Sit ? 1f : 0f;
        }

        /// <summary>The plan.</summary>
        public PersonPlan Plan => plan;

        /// <summary>Absolute position.</summary>
        public double X, Z;

        /// <summary>Body yaw, degrees.</summary>
        public float Yaw;

        /// <summary>Current ground speed, m/s.</summary>
        public float Speed { get; private set; }

        /// <summary>0 standing … 1 seated.</summary>
        public float SitWeight { get; private set; }

        /// <summary>Seat height of the bench being sat on (or last sat on), metres.</summary>
        public float SeatHeight { get; private set; }

        /// <summary>True while indoors (outside the person's hours): not shown.</summary>
        public bool Indoors => mode == Mode.Indoors;

        /// <summary>Index of the stop the person is at or walking to.</summary>
        public int StopIndex => stop;

        /// <summary>True when the hour lies within the person's hours (which may wrap past midnight).</summary>
        public static bool OutAt(float hour, float from, float to)
        {
            hour = ((hour % 24f) + 24f) % 24f;
            return from <= to ? hour >= from && hour < to : hour >= from || hour < to;
        }

        /// <summary>
        /// Advances by <paramref name="dt"/> seconds at <paramref name="hour"/>; the traveller is at (<paramref name="tx"/>, <paramref name="tz"/>)
        /// (absolute; NaN when unknown).
        /// </summary>
        public void Step(float dt, float hour, double tx, double tz)
        {
            if (plan.Stops.Length == 0 || dt <= 0f) return;
            bool out_ = OutAt(hour, plan.FromHour, plan.ToHour);
            switch (mode)
            {
                case Mode.Indoors:
                    Speed = 0f;
                    SitWeight = 0f;
                    if (!out_) return;
                    stop = 0;
                    X = plan.Stops[0].X;
                    Z = plan.Stops[0].Z;
                    Yaw = plan.Stops[0].Facing;
                    mode = Mode.Waiting;
                    wait = WaitAt(0);
                    return;

                case Mode.Waiting:
                    if (!out_)
                    {
                        StartGoingHome();
                        break;
                    }

                    RouteStop here = plan.Stops[stop];
                    float targetSit = here.Kind == StopKind.Sit ? 1f : 0f;
                    if (targetSit > 0f) SeatHeight = here.SeatHeight;
                    SitWeight = Mathf.MoveTowards(SitWeight, targetSit, dt / 0.9f);
                    Speed = 0f;
                    float face = here.Facing;
                    if (here.Kind == StopKind.Stand && !double.IsNaN(tx))
                    {
                        double dx = tx - X, dz = tz - Z;
                        if (dx * dx + dz * dz < LookDistance * LookDistance && dx * dx + dz * dz > 0.25) face = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
                    }

                    Yaw = Turn(Yaw, face, TurnRate * dt);
                    wait -= dt;
                    if (wait <= 0f && plan.Stops.Length > 1)
                    {
                        if (SitWeight > 0.01f)
                        {
                            SitWeight = Mathf.MoveTowards(SitWeight, 0f, dt / 0.9f);   // stand up before walking off
                            break;
                        }

                        int next = stop + direction;
                        if (next < 0 || next >= plan.Stops.Length)
                        {
                            direction = -direction;
                            next = stop + direction;
                        }

                        stop = Mathf.Clamp(next, 0, plan.Stops.Length - 1);
                        mode = Mode.Walking;
                    }
                    else if (wait <= 0f)
                    {
                        wait = WaitAt(stop);
                    }

                    break;

                case Mode.Walking:
                case Mode.GoingHome:
                    if (mode == Mode.Walking && !out_)
                    {
                        StartGoingHome();
                    }

                    SitWeight = Mathf.MoveTowards(SitWeight, 0f, dt / 0.9f);
                    if (SitWeight > 0.01f)
                    {
                        Speed = 0f;
                        break;
                    }

                    RouteStop target = plan.Stops[stop];
                    double ddx = target.X - X, ddz = target.Z - Z;
                    double dist = Math.Sqrt(ddx * ddx + ddz * ddz);
                    float heading = dist > 1e-4 ? (float)(Math.Atan2(ddx, ddz) * 180.0 / Math.PI) : Yaw;
                    Yaw = Turn(Yaw, heading, TurnRate * dt);
                    // Slow down while turning sharply and when arriving.
                    float turnFactor = Mathf.Clamp01(1f - Mathf.Abs(Mathf.DeltaAngle(Yaw, heading)) / 90f);
                    float arrive = Mathf.Clamp01((float)dist / 0.8f);
                    Speed = Mathf.MoveTowards(Speed, WalkSpeed * Mathf.Max(0.25f, turnFactor) * Mathf.Max(0.35f, arrive), dt * 1.6f);
                    double move = Math.Min(dist, Speed * dt);
                    if (dist > 1e-6)
                    {
                        // Along the leg's straight line (never cut across): the step toward the stop.
                        X += ddx / dist * move;
                        Z += ddz / dist * move;
                    }

                    if (dist - move <= 0.05)
                    {
                        X = target.X;
                        Z = target.Z;
                        if (mode == Mode.GoingHome && stop == 0)
                        {
                            mode = Mode.Indoors;
                            Speed = 0f;
                            break;
                        }

                        if (mode == Mode.GoingHome)
                        {
                            stop = Math.Max(0, stop - 1);
                            break;
                        }

                        mode = Mode.Waiting;
                        wait = WaitAt(stop);
                    }

                    break;
            }
        }

        private void StartGoingHome()
        {
            // Back along the round, stop by stop, to stop 0 (every leg is one the planner checked).
            mode = Mode.GoingHome;
            if (stop > 0)
            {
                RouteStop at = plan.Stops[stop];
                bool there = Math.Abs(at.X - X) < 0.05 && Math.Abs(at.Z - Z) < 0.05;
                if (there) stop--;
            }
        }

        private float WaitAt(int index)
        {
            RouteStop s = plan.Stops[index];
            return s.MinSeconds + (float)rng.NextDouble() * Mathf.Max(0f, s.MaxSeconds - s.MinSeconds);
        }

        private static float Turn(float from, float to, float maxStep) => from + Mathf.Clamp(Mathf.DeltaAngle(from, to), -maxStep, maxStep);
    }
}
