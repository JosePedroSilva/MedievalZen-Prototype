using System;
using System.Collections.Generic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;

namespace MedievalZen.Traffic
{
    /// <summary>Who is on the road.</summary>
    public enum RoadUserKind
    {
        /// <summary>A traveller on foot with a pack.</summary>
        Walker = 0,

        /// <summary>A farmer on a short errand between fields and a place.</summary>
        Farmer = 1,

        /// <summary>Two people walking side by side, talking.</summary>
        Pair = 2,

        /// <summary>A rider at a walk.</summary>
        Rider = 3,

        /// <summary>A cart drawn by a horse, its driver seated.</summary>
        Cart = 4,

        /// <summary>A walker with a lantern, at dusk.</summary>
        Lantern = 5,
    }

    /// <summary>Where a road user is in its journey.</summary>
    public enum TrafficPhase
    {
        /// <summary>Not on the road at this time (before its start or after it has gone).</summary>
        Absent = 0,

        /// <summary>Coming onto the road from its right-hand side (out of a place or a field track).</summary>
        Joining = 1,

        /// <summary>On the road.</summary>
        Road = 2,

        /// <summary>Leaving the road to its right-hand side (into a place or a field track), then standing there.</summary>
        Leaving = 3,
    }

    /// <summary>A settlement the road passes through, as the traffic plan sees it.</summary>
    public struct TrafficPlace
    {
        /// <summary>Stable id of the place.</summary>
        public int Id;

        /// <summary>Road distance of its centre, metres.</summary>
        public double S;

        /// <summary>Radius along the road, metres.</summary>
        public float Radius;

        /// <summary>True for a village (more people about in the morning and late afternoon).</summary>
        public bool Village;

        /// <summary>True for an inn.</summary>
        public bool Inn;
    }

    /// <summary>The road as the traffic plan reads it. Pure; the answers depend only on the seed (never on what was asked before).</summary>
    public interface ITrafficRoad
    {
        /// <summary>
        /// Adds the settlements with road distance in [<paramref name="sFrom"/>, <paramref name="sTo"/>] to <paramref name="results"/>, sorted
        /// by road distance. False (nothing added) when they are not known yet (not warmed): the caller then plans nothing this time.
        /// </summary>
        bool Places(double sFrom, double sTo, List<TrafficPlace> results);

        /// <summary>The region's traffic factor at road distance <paramref name="s"/> (1 farmland, meadow and lake country; 0.5 forest and downs; low in a pass).</summary>
        float Density(double s);
    }

    /// <summary>One road user of the plan: everything about it is fixed when it is drawn, so where it is at any time is a formula.</summary>
    public struct RoadUser
    {
        /// <summary>Stable id (a hash of the seed, the cell or place and the time slot it was drawn from).</summary>
        public long Id;

        /// <summary>What it is.</summary>
        public RoadUserKind Kind;

        /// <summary>+1 when it goes towards growing road distance (the traveller's way), Ã¢Ë†â€™1 the other way.</summary>
        public int Dir;

        /// <summary>Road distance where it joins the road.</summary>
        public double S0;

        /// <summary>Road distance where it leaves the road.</summary>
        public double S1;

        /// <summary>Plan time (seconds) when it is on the road at <see cref="S0"/>.</summary>
        public double T0;

        /// <summary>Speed along the road, m/s.</summary>
        public float Speed;

        /// <summary>Its lane: distance of its body's centre from the centre line, on its own right-hand side, metres.</summary>
        public float Lane;

        /// <summary>A free random number for its looks (which character, which horse, which cart).</summary>
        public int Look;

        /// <summary>True when it nods or greets the traveller as they pass.</summary>
        public bool Greets;

        /// <summary>True when it comes out of a settlement (false: a field track).</summary>
        public bool FromPlace;

        /// <summary>True when it goes into a settlement (false: a field track).</summary>
        public bool ToPlace;

        /// <summary>Plan time when it reaches <see cref="S1"/>.</summary>
        public double T1 => T0 + Math.Abs(S1 - S0) / Speed;

        /// <summary>Plan time when it appears (at the start of its joining path, off the road).</summary>
        public double Appear => T0 - RoadTrafficPlan.TailLength / Speed;

        /// <summary>Plan time when it has walked off the road (its "arrived" state begins).</summary>
        public double Gone => T1 + RoadTrafficPlan.TailLength / Speed;

        /// <summary>True for a horse-drawn cart or a rider (the plan's "vehicle" cap).</summary>
        public bool IsVehicle => Kind == RoadUserKind.Cart || Kind == RoadUserKind.Rider;

        /// <summary>Bodies it shows (2 for a pair).</summary>
        public int Members => Kind == RoadUserKind.Pair ? 2 : 1;

        /// <summary>
        /// Where it is at plan time <paramref name="tau"/>: road distance <paramref name="s"/> and the planned lateral distance from the centre
        /// line on its own right-hand side (the lane on the road; further out on its joining and leaving paths). Continuous in time.
        /// </summary>
        public TrafficPhase Sample(double tau, out double s, out double lateral)
        {
            double t1 = T1;
            if (tau < T0)
            {
                s = S0;
                double off = (T0 - tau) * Speed;
                lateral = Lane + Math.Min(off, RoadTrafficPlan.TailLength);
                return off > RoadTrafficPlan.TailLength ? TrafficPhase.Absent : TrafficPhase.Joining;
            }

            if (tau <= t1)
            {
                s = S0 + Dir * Speed * (tau - T0);
                lateral = Lane;
                return TrafficPhase.Road;
            }

            s = S1;
            double out1 = (tau - t1) * Speed;
            lateral = Lane + Math.Min(out1, RoadTrafficPlan.TailLength);
            return out1 > RoadTrafficPlan.TailLength + RoadTrafficPlan.LingerLength ? TrafficPhase.Absent : TrafficPhase.Leaving;
        }
    }

    /// <summary>Reusable inputs and scratch lists of <see cref="RoadTrafficPlan.Query"/> (one per caller; not thread-safe).</summary>
    public sealed class TrafficQuery
    {
        /// <summary>World seed.</summary>
        public int Seed;

        /// <summary>Game hours per plan second (the day cycle's rate: 24 h over its day length).</summary>
        public double HoursPerSecond = 1.0 / 60.0;

        /// <summary>The road.</summary>
        public ITrafficRoad Road;

        internal readonly List<TrafficPlace> Places = new List<TrafficPlace>();
    }

    /// <summary>
    /// Other people on the endless road (2026-10-06), as a closed-form plan: the road is cut into 500 m cells and plan time into one-minute
    /// slots; each cell and slot draws, from the seed alone (<see cref="EndlessNoise"/> streams 1430Ã¢â‚¬â€œ1433), the people who set off there
    /// then from a field track, and each settlement draws the people who leave it. Each road user has a start, an end (the next settlement
    /// within its stride, else a field track), a departure time and a speed, so its road distance at any time is a formula: the same day's
    /// road is the same every time, whatever was asked before, and right at once after a teleport. By day, in farmland, meadow and lake
    /// country, the traveller meets someone every few minutes, half as often in the woods and on the downs, rarely in a pass; departures
    /// fade at dusk and every journey ends by <see cref="DuskHour"/> (the day cycle is compressed: a 1.5 km walk is half a game day), so the
    /// night road is empty but for a rare lantern-carrier. Plan time is seconds of the day cycle's clock at its real rate. Pure.
    /// </summary>
    public static class RoadTrafficPlan
    {
        /// <summary>Randomness streams: departures from field tracks, departures from places, a user's details, lantern-carriers.</summary>
        public const int CellStream = 1430, PlaceStream = 1431, UserStream = 1432, LanternStream = 1433;

        /// <summary>Length of a departure cell along the road, metres.</summary>
        public const double CellLength = 500.0;

        /// <summary>Length of a departure slot, plan seconds.</summary>
        public const double SlotSeconds = 60.0;

        /// <summary>Longest stretch of road one user walks, metres.</summary>
        public const double MaxLeg = 1500.0;

        /// <summary>Shortest leg worth planning, metres.</summary>
        public const double MinLeg = 80.0;

        /// <summary>Length of the path off the road to the user's right at each end, metres.</summary>
        public const double TailLength = 25.0;

        /// <summary>After leaving the road a user stands this long (as metres of its walk) before it is gone from the plan.</summary>
        public const double LingerLength = 15.0;

        /// <summary>Slowest speed of any user, m/s.</summary>
        public const double MinSpeed = 1.1;

        /// <summary>Every day journey has left the road by this hour (lantern-carriers excepted).</summary>
        public const float DuskHour = 20.5f;

        /// <summary>Mean departures per cell, slot and direction at full day weight in farmland.</summary>
        public static double CellRate = 0.030;

        /// <summary>Mean departures per settlement, slot and direction at full day weight (a hamlet; inns and villages more).</summary>
        public static double PlaceRate = 0.055;

        /// <summary>Departures the traveller's way are this much more common (they are met far less often: slow overtakes).</summary>
        public static double SameWayShare = 2.5;

        /// <summary>Mean lantern-carriers per cell and slot at full dusk weight.</summary>
        public static double LanternRate = 0.030;

        // Lanes (body centre from the centre line): bodies keep at least 1.6 m off it (a walker's half width is 0.3 m, a horse's 0.4 m, a cart's 0.98 m).
        /// <summary>Lane of a walker (centre), metres; a pair's second walker is <see cref="PairSpacing"/> further out.</summary>
        public const float WalkerLane = 1.95f;

        /// <summary>Side-by-side spacing of a pair, metres.</summary>
        public const float PairSpacing = 0.65f;

        /// <summary>Lane of a rider, metres.</summary>
        public const float RiderLane = 2.15f;

        /// <summary>Lane of a cart (its centre line; off-side wheels at 2.65 + 0.75 m, on the verge), metres.</summary>
        public const float CartLane = 2.65f;

        /// <summary>Half widths of the bodies, metres.</summary>
        public const float WalkerHalfWidth = 0.3f, RiderHalfWidth = 0.4f, CartHalfWidth = 0.98f;

        /// <summary>Half width of a body of <paramref name="kind"/>.</summary>
        public static float HalfWidth(RoadUserKind kind) => kind == RoadUserKind.Cart ? CartHalfWidth : kind == RoadUserKind.Rider ? RiderHalfWidth : WalkerHalfWidth;

        /// <summary>Departure weight by hour: none at night, rising at dawn, full by day, falling in the evening.</summary>
        public static float DayWeight(float hour)
        {
            if (hour < 5.5f || hour >= 20f) return 0f;
            if (hour < 7.5f) return (hour - 5.5f) / 2f;
            if (hour < 17f) return 1f;
            if (hour < 19.5f) return 1f - 0.75f * (hour - 17f) / 2.5f;
            return 0.25f * (20f - hour) / 0.5f;
        }

        /// <summary>Lantern-carriers set off at dusk only.</summary>
        public static float LanternWeight(float hour) => hour >= 18.5f && hour < 21.5f ? 1f - Math.Abs(hour - 20f) / 1.5f : 0f;

        /// <summary>Villagers leaving for the fields in the morning (0..1).</summary>
        public static float MorningBump(float hour) => Bump(hour, 6.5f, 10f);

        /// <summary>People heading home to a village in the late afternoon (0..1).</summary>
        public static float AfternoonBump(float hour) => Bump(hour, 15f, 19f);

        private static float Bump(float h, float a, float b) => h <= a || h >= b ? 0f : (float)Math.Sin(Math.PI * (h - a) / (b - a));

        /// <summary>Traffic factor of a region kind.</summary>
        public static float RegionFactor(RegionKind kind)
        {
            switch (kind)
            {
                case RegionKind.Forest:
                case RegionKind.Downs: return 0.5f;
                case RegionKind.HighlandPass: return 0.1f;
                default: return 1f;
            }
        }

        /// <summary>Hour of day at plan time <paramref name="tau"/>.</summary>
        public static float HourAt(double tau, double hoursPerSecond)
        {
            double h = tau * hoursPerSecond;
            return (float)(h - 24.0 * Math.Floor(h / 24.0));
        }

        /// <summary>Plan time of the next <see cref="DuskHour"/> at or after plan time <paramref name="tau"/>.</summary>
        public static double DuskAfter(double tau, double hoursPerSecond) => HourAfter(tau, hoursPerSecond, DuskHour);

        /// <summary>Lantern-carriers have left the road by this hour.</summary>
        public const float LanternHomeHour = 22.5f;

        /// <summary>Plan time of the next <paramref name="hour"/> of day at or after plan time <paramref name="tau"/>.</summary>
        public static double HourAfter(double tau, double hoursPerSecond, float hour)
        {
            double h = tau * hoursPerSecond;
            double day = Math.Floor(h / 24.0);
            double dusk = day * 24.0 + hour;
            if (dusk < h) dusk += 24.0;
            return dusk / hoursPerSecond;
        }

        /// <summary>
        /// Every road user present (joining, on the road or leaving) at plan time <paramref name="tau"/> whose road distance lies in
        /// [<paramref name="sLo"/>, <paramref name="sHi"/>], added to <paramref name="results"/> (in no particular order). False when the
        /// road's places there are not known yet (nothing added).
        /// </summary>
        public static bool Query(TrafficQuery q, double tau, double sLo, double sHi, List<RoadUser> results)
        {
            if (q == null || q.Road == null || results == null || q.HoursPerSecond <= 0.0) return false;
            List<TrafficPlace> places = q.Places;
            places.Clear();
            double reach = MaxLeg + TailLength + 50.0;
            double fetch = reach + CellLength + MaxLeg + 450.0;   // every place any leg drawn here can end at
            if (!q.Road.Places(sLo - fetch, sHi + fetch, places)) return false;

            double longest = MaxLeg / MinSpeed + 2.0 * (TailLength + LingerLength) / MinSpeed;
            long j0 = (long)Math.Floor((tau - longest) / SlotSeconds) - 1, j1 = (long)Math.Floor((tau + TailLength / MinSpeed) / SlotSeconds) + 1;
            long k0 = (long)Math.Floor((sLo - reach) / CellLength), k1 = (long)Math.Floor((sHi + reach) / CellLength);
            for (long j = j0; j <= j1; j++)
            {
                double slotStart = j * SlotSeconds;
                float hour = HourAt(slotStart + 0.5 * SlotSeconds, q.HoursPerSecond);
                float day = DayWeight(hour), dusk = LanternWeight(hour);
                if (day <= 0f && dusk <= 0f) continue;
                for (long k = k0; k <= k1; k++)
                {
                    double mid = (k + 0.5) * CellLength;
                    float density = q.Road.Density(mid);
                    for (int di = 0; di < 2; di++)
                    {
                        int dir = di == 0 ? 1 : -1;
                        if (day > 0f)
                        {
                            double mean = CellRate * day * density * (dir > 0 ? SameWayShare : 1.0);
                            int villageAhead = VillageAhead(places, mid, dir, 1200.0);
                            if (villageAhead >= 0) mean *= 1.0 + 1.5 * AfternoonBump(hour);
                            Draw(q, places, CellStream, k, j * 2 + di, mean, slotStart, dir, k * CellLength, CellLength, false, tau, sLo, sHi, results);
                        }

                        if (dusk > 0f)
                        {
                            Draw(q, places, LanternStream, k, j * 2 + di, LanternRate * dusk * density, slotStart, dir, k * CellLength, CellLength, true, tau, sLo, sHi, results);
                        }
                    }
                }

                if (day <= 0f) continue;
                foreach (TrafficPlace p in places)
                {
                    if (p.S < sLo - reach || p.S > sHi + reach) continue;
                    float density = q.Road.Density(p.S);
                    double kindRate = p.Village ? 1.6 * (1.0 + 1.5 * MorningBump(hour)) : p.Inn ? 1.2 : 1.0;
                    for (int di = 0; di < 2; di++)
                    {
                        int dir = di == 0 ? 1 : -1;
                        double exit = p.S + dir * Math.Min(p.Radius * 0.4, 30.0);
                        Draw(q, places, PlaceStream, p.Id, j * 2 + di, PlaceRate * day * kindRate * Math.Max(0.35, density) * (dir > 0 ? SameWayShare : 1.0), slotStart, dir, exit, 0.0, false, tau, sLo, sHi, results);
                    }
                }
            }

            return true;
        }

        /// <summary>The index of the first village within <paramref name="within"/> metres ahead of <paramref name="s"/> in direction <paramref name="dir"/>, or Ã¢Ë†â€™1.</summary>
        private static int VillageAhead(List<TrafficPlace> places, double s, int dir, double within)
        {
            for (int i = 0; i < places.Count; i++)
            {
                double ahead = (places[i].S - s) * dir;
                if (places[i].Village && ahead > 0.0 && ahead < within) return i;
            }

            return -1;
        }

        private static void Draw(TrafficQuery q, List<TrafficPlace> places, int stream, long a, long b, double mean, double slotStart, int dir, double sBase, double sSpan,
            bool lantern, double tau, double sLo, double sHi, List<RoadUser> results)
        {
            if (mean <= 0.0) return;
            var rng = new TrafficRng(EndlessNoise.Hash(q.Seed, stream, a, b));
            int n = rng.Poisson(mean);
            for (int i = 0; i < n; i++)
            {
                long id = (long)(EndlessNoise.Hash(q.Seed, UserStream, (long)EndlessNoise.Hash(q.Seed, stream, a, b), i) & 0x7fffffffffffffffUL);
                double t0 = slotStart + rng.Next() * SlotSeconds;
                double s0 = sBase + rng.Next() * sSpan;
                if (!Make(q, places, id, lantern, sSpan == 0.0, dir, s0, t0, ref rng, out RoadUser u)) continue;
                if (tau < u.Appear || tau > u.Gone + 1.0) continue;
                if (u.Sample(tau, out double s, out _) == TrafficPhase.Absent) continue;
                if (s < sLo || s > sHi) continue;
                results.Add(u);
            }
        }

        private static bool Make(TrafficQuery q, List<TrafficPlace> places, long id, bool lantern, bool fromPlace, int dir, double s0, double t0, ref TrafficRng rng, out RoadUser u)
        {
            u = new RoadUser { Id = id, Dir = dir, S0 = s0, T0 = t0, FromPlace = fromPlace };
            float density = q.Road.Density(s0);
            double r = rng.Next();
            RoadUserKind kind;
            if (lantern) kind = RoadUserKind.Lantern;
            else if (r < 0.40) kind = RoadUserKind.Walker;
            else if (r < 0.58) kind = density >= 0.99f ? RoadUserKind.Farmer : RoadUserKind.Walker;
            else if (r < 0.70) kind = RoadUserKind.Pair;
            else if (r < 0.84) kind = RoadUserKind.Cart;
            else kind = RoadUserKind.Rider;
            if (kind == RoadUserKind.Cart && density < 0.2f) kind = RoadUserKind.Rider;   // no carts over a pass
            u.Kind = kind;
            double speed, leg;
            switch (kind)
            {
                case RoadUserKind.Farmer: speed = 1.1 + 0.3 * rng.Next(); leg = 150.0 + 450.0 * rng.Next(); u.Lane = WalkerLane + 0.25f * (float)rng.Next(); break;
                case RoadUserKind.Pair: speed = 1.1 + 0.2 * rng.Next(); leg = 300.0 + 1200.0 * rng.Next(); u.Lane = WalkerLane + 0.1f * (float)rng.Next(); break;
                case RoadUserKind.Rider: speed = 1.6 + 0.4 * rng.Next(); leg = 500.0 + 1000.0 * rng.Next(); u.Lane = RiderLane + 0.2f * (float)rng.Next(); break;
                case RoadUserKind.Cart: speed = 1.2 + 0.6 * rng.Next(); leg = 400.0 + 1100.0 * rng.Next(); u.Lane = CartLane + 0.1f * (float)rng.Next(); break;
                case RoadUserKind.Lantern: speed = 1.1 + 0.2 * rng.Next(); leg = 300.0 + 600.0 * rng.Next(); u.Lane = WalkerLane + 0.2f * (float)rng.Next(); break;
                default: speed = 1.1 + 0.4 * rng.Next(); leg = 400.0 + 1100.0 * rng.Next(); u.Lane = WalkerLane + 0.3f * (float)rng.Next(); break;
            }

            u.Speed = (float)speed;
            u.Look = (int)(rng.Next() * int.MaxValue);
            u.Greets = rng.Next() < 0.7;

            // The next settlement ahead within the stride ends the journey there (not the one it is leaving).
            double end = s0 + dir * leg;
            u.ToPlace = false;
            for (int i = 0; i < places.Count; i++)
            {
                TrafficPlace p = places[dir > 0 ? i : places.Count - 1 - i];
                double ahead = (p.S - s0) * dir;
                if (ahead <= Math.Max(40.0, p.Radius * 0.5)) continue;
                double stop = p.S - dir * Math.Min(p.Radius * 0.4, 30.0);
                if ((stop - s0) * dir > leg + (kind == RoadUserKind.Farmer ? 0.0 : 400.0)) break;
                end = stop;
                u.ToPlace = true;
                break;
            }

            // Home before dark (the day cycle is compressed): the journey is cut short at a field track if it would run past dusk.
            // Lantern-carriers are home by LanternHomeHour.
            {
                double latest = ((lantern ? HourAfter(t0, q.HoursPerSecond, LanternHomeHour) : DuskAfter(t0, q.HoursPerSecond)) - t0) * speed;
                if (Math.Abs(end - s0) > latest)
                {
                    end = s0 + dir * latest;
                    u.ToPlace = false;
                }
            }

            u.S1 = end;
            return Math.Abs(end - s0) >= MinLeg;
        }
    }

    /// <summary>A small allocation-free random sequence (SplitMix64) seeded from <see cref="EndlessNoise.Hash"/>.</summary>
    public struct TrafficRng
    {
        private ulong state;

        /// <summary>Starts the sequence at <paramref name="seed"/>.</summary>
        public TrafficRng(ulong seed)
        {
            state = seed;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double Next()
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>A Poisson count with mean <paramref name="mean"/> (small means).</summary>
        public int Poisson(double mean)
        {
            double u = Next(), p = Math.Exp(-mean), c = p;
            int n = 0;
            while (u > c && n < 8)
            {
                n++;
                p *= mean / n;
                c += p;
            }

            return n;
        }
    }
}
