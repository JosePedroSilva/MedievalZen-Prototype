using System;
using System.Collections.Generic;
using MedievalZen.Endless.Generation;

namespace MedievalZen.Traffic
{
    /// <summary>A bridge on the road: its crossing's road distance and the road's height there (its waypoint).</summary>
    public struct TrafficBridge
    {
        /// <summary>Road distance of the crossing.</summary>
        public double S;

        /// <summary>Road height at the crossing (the profile; the bridge's pivot sits <see cref="BridgeShape.Sink"/> below it).</summary>
        public float RoadHeight;
    }

    /// <summary>
    /// The walking surface of the road users: on a bridge the deck's hump (the same <see cref="BridgeShape.DeckHeight"/> the deck's road
    /// surface in <see cref="TileStreams.DeckSurface"/> and the parapets follow), elsewhere the ground. Lanes are pulled in on a bridge so
    /// every body stays inside the parapets. Pure.
    /// </summary>
    public static class TrafficGround
    {
        /// <summary>Bodies keep their outer edge within this distance of the centre line on a bridge (the parapets' inner face is about 2.8 m).</summary>
        public const float BridgeOuterLimit = 2.65f;

        /// <summary>The bridge whose deck covers road distance <paramref name="s"/>, or −1.</summary>
        public static int BridgeAt(IReadOnlyList<TrafficBridge> bridges, double s)
        {
            if (bridges == null) return -1;
            for (int i = 0; i < bridges.Count; i++)
            {
                if (Math.Abs(s - bridges[i].S) <= BridgeShape.HalfLength) return i;
            }

            return -1;
        }

        /// <summary>The deck's walking height at road distance <paramref name="s"/>, or NaN off every bridge.</summary>
        public static float DeckHeight(IReadOnlyList<TrafficBridge> bridges, double s)
        {
            int i = BridgeAt(bridges, s);
            if (i < 0) return float.NaN;
            float along = (float)(s - bridges[i].S);
            float h = BridgeShape.DeckHeight(along);
            return float.IsNaN(h) ? float.NaN : BridgeShape.PivotY(bridges[i].RoadHeight) + h + TileStreams.DeckLift;
        }

        /// <summary>The walking height at road distance <paramref name="s"/>: the deck on a bridge, else <paramref name="ground"/>.</summary>
        public static float Height(IReadOnlyList<TrafficBridge> bridges, double s, float ground)
        {
            float deck = DeckHeight(bridges, s);
            return float.IsNaN(deck) ? ground : deck;
        }

        /// <summary>The largest lane of a body of <paramref name="kind"/> on a bridge (its outer edge at <see cref="BridgeOuterLimit"/>).</summary>
        public static float BridgeLane(RoadUserKind kind) => BridgeOuterLimit - RoadTrafficPlan.HalfWidth(kind);
    }

    /// <summary>
    /// Manners of one road user with a body (pure; the manager keeps one per shown user): keeps to its lane on its right-hand side, drifts
    /// to the edge within <see cref="YieldDistance"/> of the traveller (a cart's off-side wheels onto the verge), goes round outside the
    /// traveller when the traveller stands in its lane (inside only where a bridge's parapet leaves no room), never closer to the centre
    /// line than its lane allows, turns its head to the traveller and may nod once. The plan's road distance is never changed: it stops
    /// for nothing.
    /// </summary>
    public sealed class TrafficBrain
    {
        /// <summary>Within this distance along the road of the traveller a user drifts to the edge, metres.</summary>
        public const float YieldDistance = 12f;

        /// <summary>Gap kept between a body's edge and the traveller's, metres.</summary>
        public const float Clearance = 0.6f;

        /// <summary>The traveller's half width, metres.</summary>
        public const float TravellerRadius = 0.35f;

        /// <summary>Within this distance the head turns to the traveller, metres.</summary>
        public const float LookDistance = 12f;

        /// <summary>The nod comes when the traveller is this near, metres.</summary>
        public const float NodDistance = 8f;

        /// <summary>The user shown.</summary>
        public RoadUser User;

        /// <summary>Road distance now.</summary>
        public double S;

        /// <summary>Lateral distance from the centre line on the user's right, now (smoothed on the road).</summary>
        public float Lateral;

        /// <summary>The phase now.</summary>
        public TrafficPhase Phase;

        /// <summary>True while it walks (false standing at the end of its leaving path).</summary>
        public bool Moving;

        /// <summary>Head turn towards the traveller, 0..1.</summary>
        public float Look;

        /// <summary>Seconds since the nod began (negative: none yet).</summary>
        public float NodTime = -1f;

        /// <summary>Distance to the traveller, metres.</summary>
        public float Distance;

        private bool nodded;

        /// <summary>Binds the brain to <paramref name="user"/> at plan time <paramref name="tau"/> (lateral at the plan's).</summary>
        public void Reset(RoadUser user, double tau)
        {
            User = user;
            Phase = user.Sample(tau, out S, out double lat);
            Lateral = (float)lat;
            Look = 0f;
            NodTime = -1f;
            nodded = false;
        }

        /// <summary>The lateral the user aims for on the road (before the rate limit).</summary>
        public static float TargetLateral(RoadUser u, bool onBridge, double alongToTraveller, float travellerLateral)
        {
            float half = RoadTrafficPlan.HalfWidth(u.Kind);
            // The pair's second walker is further out (on a bridge it falls in behind: see PairOffset).
            float span = u.Kind == RoadUserKind.Pair && !onBridge ? RoadTrafficPlan.PairSpacing : 0f;
            float lane = u.Lane;
            float near = Math.Abs((float)alongToTraveller) < YieldDistance ? 1f : 0f;
            if (near > 0f) lane += u.Kind == RoadUserKind.Cart ? 0.35f : u.Kind == RoadUserKind.Rider ? 0.45f : 0.6f;
            float limit = onBridge ? TrafficGround.BridgeLane(u.Kind) - span : float.MaxValue;
            if (lane > limit) lane = limit;

            // The traveller in the lane, close along the road: round the outside if there is room, else (a bridge) the inside.
            if (Math.Abs((float)alongToTraveller) < YieldDistance)
            {
                float keep = TravellerRadius + Clearance + half;
                bool overlaps = travellerLateral + keep > lane && travellerLateral - keep < lane + span;
                if (overlaps)
                {
                    float outside = travellerLateral + keep;
                    if (outside <= limit) lane = Math.Max(lane, outside);
                    else lane = travellerLateral - keep - span;
                }
            }

            return lane;
        }

        /// <summary>
        /// Advances by <paramref name="dt"/> seconds to plan time <paramref name="tau"/>, with the traveller at road distance
        /// <paramref name="travellerS"/> and lateral <paramref name="travellerRoadLateral"/> (road frame: positive right of growing road
        /// distance). <paramref name="onBridge"/>: the user is on a bridge's deck.
        /// </summary>
        public void Step(float dt, double tau, double travellerS, float travellerRoadLateral, bool onBridge, float distance)
        {
            Distance = distance;
            Phase = User.Sample(tau, out S, out double planLat);
            Moving = Phase == TrafficPhase.Road || Phase == TrafficPhase.Joining ||
                     (Phase == TrafficPhase.Leaving && planLat < User.Lane + RoadTrafficPlan.TailLength - 0.01);
            if (Phase != TrafficPhase.Road)
            {
                // Off the road the plan's path rules (it starts and ends at the lane, so the hand-over is smooth).
                Lateral = Math.Max((float)planLat, MoveTowards(Lateral, (float)planLat, 0.7f * Math.Max(dt, 0f)));
                Look = MoveTowards(Look, 0f, dt * 1.5f);
                return;
            }

            double along = (travellerS - S) * User.Dir;   // positive: the traveller is ahead of the user
            float lat = TargetLateral(User, onBridge, along, travellerRoadLateral * User.Dir);
            float speed = User.Kind == RoadUserKind.Cart ? 0.45f : User.Kind == RoadUserKind.Rider ? 0.55f : 0.7f;
            Lateral = MoveTowards(Lateral, lat, speed * Math.Max(dt, 0f));

            // Head and nod: only towards a traveller ahead or just alongside.
            bool sees = distance < LookDistance && along > -2.0;
            Look = MoveTowards(Look, sees ? 1f : 0f, dt * (sees ? 1.2f : 0.8f));
            if (NodTime >= 0f) NodTime += dt;
            if (!nodded && User.Greets && distance < NodDistance && along > 0.0)
            {
                nodded = true;
                NodTime = 0f;
            }
        }

        private static float MoveTowards(float a, float b, float step) => Math.Abs(b - a) <= step ? b : a + Math.Sign(b - a) * step;
    }

    /// <summary>One candidate for a body: a planned user near the traveller.</summary>
    public struct TrafficCandidate
    {
        /// <summary>The user.</summary>
        public RoadUser User;

        /// <summary>Distance to the traveller, metres.</summary>
        public float Distance;

        /// <summary>True when it has a body now.</summary>
        public bool Shown;

        /// <summary>True when a new body may be given to it now (built ground, nobody watching its joining path, no hold or capture).</summary>
        public bool MaySpawn;
    }

    /// <summary>
    /// Which planned users get bodies (pure): those already shown keep theirs within <see cref="HideBeyond"/>, new ones get one within
    /// <see cref="ShowWithin"/>, nearest first, under the caps (<see cref="MaxWalkers"/> walking bodies, a pair counting two;
    /// <see cref="MaxVehicles"/> carts or riders).
    /// </summary>
    public static class TrafficSelector
    {
        /// <summary>A body is given within this distance of the traveller, metres.</summary>
        public const float ShowWithin = 220f;

        /// <summary>A body is taken back beyond this distance, metres.</summary>
        public const float HideBeyond = 260f;

        /// <summary>Animators run within this distance, metres.</summary>
        public const float AnimateWithin = 120f;

        /// <summary>Most walking bodies at once.</summary>
        public const int MaxWalkers = 6;

        /// <summary>Most carts and riders at once.</summary>
        public const int MaxVehicles = 2;

        /// <summary>Fills <paramref name="chosen"/> with the indices into <paramref name="candidates"/> that have bodies (sorts the candidates by distance).</summary>
        public static void Select(List<TrafficCandidate> candidates, List<int> chosen)
        {
            chosen.Clear();
            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            int walkers = 0, vehicles = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    TrafficCandidate c = candidates[i];
                    bool keep = pass == 0 ? c.Shown && c.Distance <= HideBeyond : !c.Shown && c.MaySpawn && c.Distance <= ShowWithin;
                    if (!keep) continue;
                    if (c.User.IsVehicle)
                    {
                        if (vehicles + 1 > MaxVehicles) continue;
                        vehicles++;
                    }
                    else
                    {
                        int n = c.User.Members;
                        if (walkers + n > MaxWalkers) continue;
                        walkers += n;
                    }

                    chosen.Add(i);
                }
            }
        }
    }
}
