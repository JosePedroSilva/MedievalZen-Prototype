using System;

namespace MedievalZen.Wildlife
{
    /// <summary>
    /// A fox trots in towards the hens; they scatter, the dog barks and sees it off into the distance, then trots back and
    /// sits. The fox never comes within <see cref="FoxGap"/> of a hen, the dog never reaches the fox.
    /// </summary>
    public sealed class FoxAtHenHouseEvent : AnimalEvent
    {
        public const float FoxGap = 7f;
        public const float DogGap = 5f;
        private const int Spawn = 0, Yard = 1, Exit = 2, DogHome = 3;

        private AnimalGroup yard, foxGroup;
        private AnimalAgent dog, fox;
        private int barks;

        public override EventKind Kind => EventKind.FoxAtHenHouse;
        public override string Name => "Fox at the hen house";

        /// <summary>A candidate near the traveller, or null with the reason.</summary>
        public static FoxAtHenHouseEvent Prepare(WildlifeSim sim, in WildlifeContext ctx, bool forced, out float score, out string why)
        {
            score = 0f;
            why = "no farmyard with hens and a dog nearby";
            foreach (AnimalGroup g in sim.Groups)
            {
                if (g.Kind != GroupKind.Farmyard || g.CountOf(Species.Hen) < 2 || !Free(g)) continue;
                AnimalAgent d = g.First(Species.Retriever) ?? g.First(Species.GreatDane);
                if (d == null) continue;
                g.Centroid(out float cx, out float cz);
                if (!InRange(ctx, cx, cz, forced, 30f, 150f, out float s) || s <= score) continue;
                // The fox comes from the side away from the traveller, out of their sight at first.
                if (!FindSpawn(sim, ctx, cx, cz, cx - ctx.TravellerX, cz - ctx.TravellerZ, 50f, 70f, forced ? 40f : 80f, out float fx, out float fz))
                {
                    why = "no cover for a fox to come from";
                    continue;
                }

                var e = new FoxAtHenHouseEvent { yard = g, dog = d };
                e.P[Spawn] = new Spot(fx, fz);
                e.P[Yard] = new Spot(cx, cz);
                score = s;
                return e;
            }

            return null;
        }

        protected override void Begin()
        {
            MaxSeconds = 90f;
            Use(yard);
            foxGroup = Sim.AddVisitor(Species.Fox, 1, P[Spawn].X, P[Spawn].Z, 0f, Id ^ 0x0F0F);
            Use(foxGroup);
            fox = foxGroup.Members[0];
            fox.FacePoint(P[Yard].X, P[Yard].Z);
            fox.Yaw = fox.FaceYaw;
            P[DogHome] = new Spot(dog.HomeX, dog.HomeZ);
            Detail = $"{yard.CountOf(Species.Hen)} hens, a {dog.Info.Name}";

            foreach (AnimalAgent a in yard.Members)
            {
                Take(a);
                if (a != dog) Hold(a, Pose.Eat);
            }

            Take(fox);
            fox.Avoid = yard;
            fox.AvoidGap = FoxGap;
            // Trot to the edge of the yard.
            Spot edge = Away(P[Yard].X, P[Yard].Z, P[Spawn].X - P[Yard].X, P[Spawn].Z - P[Yard].Z, 9f);
            Walk(fox, edge.X, edge.Z, 2.1f);
            Hold(dog, Pose.Idle);
            SetPhase(0);
        }

        protected override void Tick(float dt, in WildlifeContext ctx)
        {
            Measure(fox, yard, dog);
            switch (Phase)
            {
                case 0: // the fox approaches
                {
                    float nearest = float.MaxValue;
                    foreach (AnimalAgent h in yard.Members)
                    {
                        if (h != dog) nearest = Math.Min(nearest, h.DistanceTo(fox));
                    }

                    if (nearest < 13f || fox.Arrived || PhaseTime > 40f)
                    {
                        Scatter();
                        SetPhase(1);
                    }

                    break;
                }

                case 1: // alarm: the dog barks twice and goes for the fox, which turns tail
                    dog.FacePoint(fox.X, fox.Z);
                    if (barks < 2 && PhaseTime > barks * 0.9f)
                    {
                        dog.Driver.Pulse(Pose.Bark);
                        AnimalBrain.Voice(Sim, dog, Pose.Bark);
                        barks++;
                    }

                    if (PhaseTime > 1.6f)
                    {
                        Sim.Call(dog, "SFX_Dog_Barking", 0.5f);
                        Spot exit = Away(P[Yard].X, P[Yard].Z, P[Spawn].X - P[Yard].X, P[Spawn].Z - P[Yard].Z, 85f);
                        P[Exit] = exit;
                        Run(fox, exit.X, exit.Z);
                        dog.Avoid = foxGroup;
                        dog.AvoidGap = DogGap;
                        SetPhase(2);
                    }

                    break;

                case 2: // the chase: the dog gives up well before the fox is caught
                    Run(dog, fox.X, fox.Z, dog.Info.RunSpeed * 0.85f);
                    if (dog.DistanceTo(P[Yard].X, P[Yard].Z) > 28f || PhaseTime > 7f || dog.DistanceTo(fox) > 30f)
                    {
                        Hold(dog, Pose.Idle);
                        dog.Driver.Pulse(Pose.Bark);
                        AnimalBrain.Voice(Sim, dog, Pose.Bark);
                        SetPhase(3);
                    }

                    break;

                case 3: // the dog trots home; the hens settle
                    if (PhaseTime > 1.5f && !dog.HasTarget && !dog.Arrived)
                    {
                        dog.Avoid = null;
                        Walk(dog, P[DogHome].X, P[DogHome].Z, dog.Info.WalkSpeed * 1.4f);
                    }

                    if (dog.Arrived)
                    {
                        Hold(dog, Pose.Sit);
                        SetPhase(4);
                    }

                    break;

                case 4:
                    if (PhaseTime > 2f) Finish("the fox was seen off");
                    break;
            }

            if (Phase >= 2 && fox.Arrived && Phase < 4)
            {
                // The fox stops far off and becomes an ordinary wary fox there.
                foxGroup.HomeX = fox.X;
                foxGroup.HomeZ = fox.Z;
                foxGroup.HomeRadius = 14f;
            }
        }

        private void Scatter()
        {
            foreach (AnimalAgent h in yard.Members)
            {
                if (h == dog) continue;
                Spot s = Away(h.X, h.Z, h.X - fox.X, h.Z - fox.Z, h.Rng.Range(5f, 9f));
                if (!Sim.CanStand(s.X, s.Z, 0.3f)) s = Away(h.X, h.Z, P[Yard].X - fox.X, P[Yard].Z - fox.Z, 4f);
                Run(h, s.X, s.Z);
            }

            fox.Stop();
            fox.Pose = Pose.Idle;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// The farm dog dashes at rabbits in the meadow; they bolt for cover well before it gets near; it trots back and sits.
    /// </summary>
    public sealed class DogAndRabbitsEvent : AnimalEvent
    {
        public const float Gap = 6f;
        private const int DogHome = 0, Origin = 1;

        private AnimalGroup yard, rabbits;
        private AnimalAgent dog;
        private bool spawned;

        public override EventKind Kind => EventKind.DogAndRabbits;
        public override string Name => "Dog and rabbits";

        public static DogAndRabbitsEvent Prepare(WildlifeSim sim, in WildlifeContext ctx, bool forced, out float score, out string why)
        {
            score = 0f;
            why = "no farm dog nearby";
            foreach (AnimalGroup g in sim.Groups)
            {
                if (g.Kind != GroupKind.Farmyard || !Free(g)) continue;
                AnimalAgent d = g.First(Species.Retriever) ?? g.First(Species.GreatDane);
                if (d == null || d.Mode == BrainMode.Rest) continue;
                if (!InRange(ctx, d.X, d.Z, forced, 20f, 150f, out float s) || s <= score) continue;
                AnimalGroup found = null;
                foreach (AnimalGroup r in sim.Groups)
                {
                    if (r.Main != Species.Rabbit || !Free(r)) continue;
                    r.Centroid(out float rx, out float rz);
                    float dd = d.DistanceTo(rx, rz);
                    if (dd > 22f && dd < 110f) found = r;
                }

                var e = new DogAndRabbitsEvent { yard = g, dog = d, rabbits = found };
                if (found == null)
                {
                    // Rabbits in the meadow next to the farm, on the side away from the traveller.
                    if (!FindSpawn(sim, ctx, d.X, d.Z, d.X - ctx.TravellerX, d.Z - ctx.TravellerZ, 26f, 36f, forced ? 30f : 70f, out float x, out float z))
                    {
                        why = "no open ground for rabbits beside the farm";
                        continue;
                    }

                    e.P[Origin] = new Spot(x, z);
                    e.spawned = true;
                }

                score = s;
                return e;
            }

            return null;
        }

        protected override void Begin()
        {
            MaxSeconds = 70f;
            Use(yard);
            if (spawned) rabbits = Sim.AddVisitor(Species.Rabbit, 3, P[Origin].X, P[Origin].Z, 0f, Id ^ 0x0A0A);
            Use(rabbits);
            P[DogHome] = new Spot(dog.HomeX, dog.HomeZ);
            Detail = $"a {dog.Info.Name}, {rabbits.Members.Count} rabbits";
            Take(dog);
            foreach (AnimalAgent r in rabbits.Members)
            {
                Take(r);
                Hold(r, Pose.Idle);
            }

            dog.Avoid = rabbits;
            dog.AvoidGap = Gap;
            rabbits.Centroid(out float cx, out float cz);
            P[Origin] = new Spot(dog.X, dog.Z);
            Hold(dog, Pose.Idle);
            dog.FacePoint(cx, cz);
            SetPhase(0);
        }

        protected override void Tick(float dt, in WildlifeContext ctx)
        {
            Measure(dog, rabbits);
            rabbits.Centroid(out float cx, out float cz);
            switch (Phase)
            {
                case 0: // the dog notices
                    dog.FacePoint(cx, cz);
                    if (PhaseTime > 2f)
                    {
                        Run(dog, cx, cz, dog.Info.RunSpeed * 0.9f);
                        SetPhase(1);
                    }

                    break;

                case 1: // the dash; the rabbits bolt when it is still well short
                {
                    Run(dog, cx, cz, dog.Info.RunSpeed * 0.9f);
                    float nearest = float.MaxValue;
                    foreach (AnimalAgent r in rabbits.Members) nearest = Math.Min(nearest, r.DistanceTo(dog));
                    if (nearest < 15f || PhaseTime > 8f)
                    {
                        foreach (AnimalAgent r in rabbits.Members)
                        {
                            Spot s = Away(r.X, r.Z, r.X - dog.X, r.Z - dog.Z, r.Rng.Range(30f, 40f));
                            if (!Sim.CanStand(s.X, s.Z, 0.3f, true)) s = Away(r.X, r.Z, r.X - dog.X + (r.Z - dog.Z) * 0.8f, r.Z - dog.Z - (r.X - dog.X) * 0.8f, 30f);
                            Run(r, s.X, s.Z);
                        }

                        SetPhase(2);
                    }

                    break;
                }

                case 2: // a few more bounds, then it gives up
                    Run(dog, cx, cz, dog.Info.RunSpeed * 0.8f);
                    if (PhaseTime > 3f || dog.DistanceTo(P[Origin].X, P[Origin].Z) > 32f)
                    {
                        Hold(dog, Pose.Idle);
                        dog.Driver.Pulse(Pose.Bark);
                        AnimalBrain.Voice(Sim, dog, Pose.Bark);
                        SetPhase(3);
                    }

                    break;

                case 3:
                    if (PhaseTime > 1.5f && !dog.HasTarget && !dog.Arrived)
                    {
                        dog.Avoid = null;
                        Walk(dog, P[DogHome].X, P[DogHome].Z, dog.Info.WalkSpeed * 1.3f);
                    }

                    if (dog.Arrived)
                    {
                        Hold(dog, Pose.Sit);
                        SetPhase(4);
                    }

                    break;

                case 4:
                    if (PhaseTime > 2f) Finish("the rabbits got away");
                    break;
            }

            // Rabbits that reach cover settle there (their new home).
            bool allIn = true;
            foreach (AnimalAgent r in rabbits.Members)
            {
                if (Phase >= 2 && r.Arrived && r.Scripted)
                {
                    Hold(r, Pose.Alert);
                    r.Arrived = false;
                }

                allIn &= !r.HasTarget;
            }

            if (Phase >= 2 && allIn)
            {
                rabbits.HomeX = cx;
                rabbits.HomeZ = cz;
            }
        }
    }

    /// <summary>
    /// At dawn, dusk or night: deer raise their heads, then bolt across the road well ahead of the traveller; a wolf lopes
    /// after them, slower and never within <see cref="Gap"/>, stops short of the road, howls and leaves.
    /// </summary>
    public sealed class WolfAndDeerEvent : AnimalEvent
    {
        public const float Gap = 25f;
        private const int Crossing = 0, Far = 1, WolfStart = 2, WolfExit = 3, Centre = 4;

        private AnimalGroup deer, wolfGroup;
        private AnimalAgent wolf;
        private float roadDirX, roadDirZ;

        public override EventKind Kind => EventKind.WolfAndDeer;
        public override string Name => "Wolf and deer";

        public static WolfAndDeerEvent Prepare(WildlifeSim sim, in WildlifeContext ctx, bool forced, out float score, out string why)
        {
            score = 0f;
            why = forced || ctx.IsTwilightOrNight ? "no deer near the road ahead" : "wolves only come out at dawn, dusk and night";
            if (!forced && !ctx.IsTwilightOrNight) return null;
            if (sim.Road == null)
            {
                why = "no road to cross";
                return null;
            }

            foreach (AnimalGroup g in sim.Groups)
            {
                if ((g.Main != Species.Deer && g.Main != Species.Stag) || g.Members.Count < 2 || !Free(g)) continue;
                g.Centroid(out float cx, out float cz);
                if (!InRange(ctx, cx, cz, forced, 40f, 150f, out float s) || s <= score) continue;
                // A crossing far enough ahead that the deer are over long before the traveller gets there.
                float toRoad = sim.Road.Nearest(cx, cz, out _, out _, out _, out _, out float dr) ? dr : 80f;
                float cross = (toRoad + 35f) / 7f;
                float ahead = Math.Max(55f, ctx.Speed * (cross + 10f) + 35f);
                if (!sim.Road.Along(ctx.TravellerX, ctx.TravellerZ, ctx.FacingX, ctx.FacingZ, ahead, out float rx, out float rz)) continue;
                if (Dist(cx, cz, rx, rz) > 140f)
                {
                    why = "the deer are too far from the road ahead";
                    continue;
                }

                Spot far = Away(rx, rz, rx - cx, rz - cz, 38f);
                if (!sim.CanStand(far.X, far.Z, 0.6f))
                {
                    far = Away(rx, rz, rx - cx, rz - cz, 55f);
                    if (!sim.CanStand(far.X, far.Z, 0.6f))
                    {
                        why = "nowhere for the deer to cross to";
                        continue;
                    }
                }

                if (!FindSpawn(sim, ctx, cx, cz, cx - rx, cz - rz, 45f, 60f, forced ? 60f : 90f, out float wx, out float wz))
                {
                    why = "nowhere for a wolf to come from";
                    continue;
                }

                var e = new WolfAndDeerEvent { deer = g };
                e.P[Crossing] = new Spot(rx, rz);
                e.P[Far] = far;
                e.P[WolfStart] = new Spot(wx, wz);
                e.P[Centre] = new Spot(cx, cz);
                score = s;
                return e;
            }

            return null;
        }

        protected override void Begin()
        {
            MaxSeconds = 60f;
            Use(deer);
            wolfGroup = Sim.AddVisitor(Species.Wolf, 1, P[WolfStart].X, P[WolfStart].Z, 0f, Id ^ 0x0B0B);
            Use(wolfGroup);
            wolf = wolfGroup.Members[0];
            Take(wolf);
            wolf.Avoid = deer;
            wolf.AvoidGap = Gap;
            Walk(wolf, P[Centre].X, P[Centre].Z);
            Detail = $"{deer.Members.Count} deer";
            if (Sim.Road.Nearest(P[Crossing].X, P[Crossing].Z, out _, out _, out float dx, out float dz, out _))
            {
                roadDirX = dx;
                roadDirZ = dz;
            }

            foreach (AnimalAgent d in deer.Members)
            {
                Take(d);
                Hold(d, Pose.Idle);
                d.FacePoint(wolf.X, wolf.Z);
            }

            AnimalBrain.Voice(Sim, deer.Members[0], Pose.Alert);
            SetPhase(0);
        }

        protected override void Tick(float dt, in WildlifeContext ctx)
        {
            Measure(wolf, deer);
            switch (Phase)
            {
                case 0: // heads up
                    foreach (AnimalAgent d in deer.Members) d.FacePoint(wolf.X, wolf.Z);
                    if (PhaseTime > 3f)
                    {
                        // Bolt over the road in a loose line, each to its own spot on the far side.
                        int i = 0;
                        foreach (AnimalAgent d in deer.Members)
                        {
                            float off = (i - (deer.Members.Count - 1) * 0.5f) * 3.5f;
                            Run(d, P[Far].X + roadDirX * off, P[Far].Z + roadDirZ * off);
                            i++;
                        }

                        Run(wolf, P[Crossing].X, P[Crossing].Z, wolf.Info.RunSpeed);
                        SetPhase(1);
                    }

                    break;

                case 1: // the chase; the wolf stops short of the road
                {
                    bool over = true;
                    foreach (AnimalAgent d in deer.Members) over &= !d.HasTarget;
                    float roadD = Sim.Road.Nearest(wolf.X, wolf.Z, out _, out _, out _, out _, out float rd) ? rd : 999f;
                    if (over || roadD < Sim.Corridor + 8f || PhaseTime > 14f)
                    {
                        Hold(wolf, Pose.Idle);
                        wolf.FacePoint(P[Far].X, P[Far].Z);
                        SetPhase(2);
                    }

                    break;
                }

                case 2: // a howl
                    if (PhaseTime > 1f && wolf.Pose != Pose.Howl && PhaseTime < 2f)
                    {
                        Hold(wolf, Pose.Howl);
                        AnimalBrain.Voice(Sim, wolf, Pose.Howl);
                    }

                    if (PhaseTime > 6f)
                    {
                        Spot exit = Away(P[Centre].X, P[Centre].Z, P[WolfStart].X - P[Crossing].X, P[WolfStart].Z - P[Crossing].Z, 110f);
                        P[WolfExit] = exit;
                        Walk(wolf, exit.X, exit.Z, wolf.Info.WalkSpeed * 1.3f);
                        SetPhase(3);
                    }

                    break;

                case 3: // the wolf leaves; the deer settle on the far side
                    if (PhaseTime > 6f)
                    {
                        wolfGroup.HomeX = P[WolfExit].X;
                        wolfGroup.HomeZ = P[WolfExit].Z;
                        wolfGroup.HomeRadius = 20f;
                        deer.HomeX = P[Far].X;
                        deer.HomeZ = P[Far].Z;
                        deer.HasRect = false;
                        deer.HomeRadius = 14f;
                        foreach (AnimalAgent d in deer.Members)
                        {
                            d.HomeX = d.X;
                            d.HomeZ = d.Z;
                        }

                        Finish("the deer got away");
                    }

                    break;
            }

            foreach (AnimalAgent d in deer.Members)
            {
                if (Phase >= 1 && d.Arrived && d.Scripted)
                {
                    Hold(d, Pose.Idle);
                    d.FacePoint(wolf.X, wolf.Z);
                    d.Arrived = false;
                }
            }
        }
    }

    /// <summary>
    /// A sheepdog circles a flock and drives it across the road to new grass on the other side, timed to be clear of the
    /// road before the traveller gets there (and hurrying over if they come close anyway).
    /// </summary>
    public sealed class SheepdogHerdingEvent : AnimalEvent
    {
        public const float Gap = 4f;
        private const int Target = 0, Crossing = 1, DogStart = 2, DogExit = 3;

        private AnimalGroup flock, dogGroup;
        private AnimalAgent dog;
        private float circleAngle, circleStart;

        public override EventKind Kind => EventKind.SheepdogHerding;
        public override string Name => "Sheepdog herding";

        public static SheepdogHerdingEvent Prepare(WildlifeSim sim, in WildlifeContext ctx, bool forced, out float score, out string why)
        {
            score = 0f;
            why = "no flock of sheep nearby";
            if (sim.Road == null)
            {
                why = "no road to cross";
                return null;
            }

            foreach (AnimalGroup g in sim.Groups)
            {
                if (g.Main != Species.Sheep || g.Members.Count < 4 || !Free(g)) continue;
                g.Centroid(out float cx, out float cz);
                if (!InRange(ctx, cx, cz, forced, 30f, 150f, out float s) || s <= score) continue;
                if (!sim.Road.Nearest(cx, cz, out float rx, out float rz, out _, out _, out float dRoad) || dRoad > 120f)
                {
                    why = "the flock is too far from the road";
                    continue;
                }

                float beyond = Math.Max(25f, Math.Min(60f, dRoad));
                Spot target = Away(rx, rz, rx - cx, rz - cz, beyond);
                if (!sim.CanStand(target.X, target.Z, 1f))
                {
                    why = "no grass across the road";
                    continue;
                }

                // Clear of the road before the traveller arrives (or the crossing is behind them).
                float crossTime = (dRoad + 16f) / 1.0f;
                float tx = rx - ctx.TravellerX, tz = rz - ctx.TravellerZ;
                float toCrossing = (float)Math.Sqrt(tx * tx + tz * tz);
                bool ahead = tx * ctx.FacingX + tz * ctx.FacingZ > 0f;
                float need = ahead ? ctx.Speed * (crossTime + 15f) + 30f : 25f;
                if (toCrossing < need && !forced)
                {
                    why = "the traveller would reach the crossing first";
                    continue;
                }

                if (!FindSpawn(sim, ctx, cx, cz, cx - rx, cz - rz, 30f, 45f, forced ? 30f : 80f, out float dx, out float dz))
                {
                    why = "nowhere for a sheepdog to come from";
                    continue;
                }

                var e = new SheepdogHerdingEvent { flock = g };
                e.P[Target] = target;
                e.P[Crossing] = new Spot(rx, rz);
                e.P[DogStart] = new Spot(dx, dz);
                score = s;
                return e;
            }

            return null;
        }

        protected override void Begin()
        {
            MaxSeconds = 200f;
            Use(flock);
            dogGroup = Sim.AddVisitor(Species.Retriever, 1, P[DogStart].X, P[DogStart].Z, 0f, Id ^ 0x0C0C);
            Use(dogGroup);
            dog = dogGroup.Members[0];
            Take(dog);
            dog.Avoid = flock;
            dog.AvoidGap = Gap;
            Detail = $"{flock.Members.Count} sheep";
            foreach (AnimalAgent sh in flock.Members)
            {
                Take(sh);
                Hold(sh, Pose.Eat);
            }

            flock.Centroid(out float cx, out float cz);
            circleAngle = (float)Math.Atan2(dog.Z - cz, dog.X - cx);
            circleStart = circleAngle;
            SetPhase(0);
        }

        protected override void Tick(float dt, in WildlifeContext ctx)
        {
            Measure(dog, flock);
            flock.Centroid(out float cx, out float cz);
            float spread = 0f;
            foreach (AnimalAgent sh in flock.Members) spread = Math.Max(spread, sh.DistanceTo(cx, cz));
            switch (Phase)
            {
                case 0: // run out and round the flock once
                {
                    float r = spread + 9f;
                    if (dog.DistanceTo(cx, cz) > r + 3f && PhaseTime < 15f)
                    {
                        Run(dog, cx + (float)Math.Cos(circleAngle) * r, cz + (float)Math.Sin(circleAngle) * r, dog.Info.RunSpeed * 0.8f);
                        break;
                    }

                    circleAngle += dog.Info.RunSpeed * 0.7f / r * dt;
                    Run(dog, cx + (float)Math.Cos(circleAngle + 0.4f) * r, cz + (float)Math.Sin(circleAngle + 0.4f) * r, dog.Info.RunSpeed * 0.7f);
                    foreach (AnimalAgent sh in flock.Members)
                    {
                        if (sh.Pose == Pose.Eat) Hold(sh, Pose.Idle);
                        sh.FacePoint(dog.X, dog.Z);
                    }

                    if (circleAngle - circleStart > 6.6f || PhaseTime > 30f) SetPhase(1);
                    break;
                }

                case 1: // drive the flock over the road
                {
                    float hurry = 1f;
                    float tx = P[Crossing].X - ctx.TravellerX, tz = P[Crossing].Z - ctx.TravellerZ;
                    if (tx * tx + tz * tz < 30f * 30f) hurry = 2.4f;   // the traveller is close: the flock hurries over
                    float ax = P[Target].X - cx, az = P[Target].Z - cz;
                    float al = (float)Math.Sqrt(ax * ax + az * az);
                    foreach (AnimalAgent sh in flock.Members)
                    {
                        float ox = (sh.X - cx) * 0.7f, oz = (sh.Z - cz) * 0.7f;
                        if (hurry > 1.5f) Run(sh, P[Target].X + ox, P[Target].Z + oz, sh.Info.RunSpeed * 0.7f);
                        else Walk(sh, P[Target].X + ox, P[Target].Z + oz, 1.0f);
                    }

                    if (al > 1e-3f)
                    {
                        // Behind the flock, on the far side from the target.
                        float bx = cx - ax / al * (spread + 7f), bz = cz - az / al * (spread + 7f);
                        Walk(dog, bx, bz, 1.6f);
                    }

                    if (al < 6f || PhaseTime > 150f)
                    {
                        foreach (AnimalAgent sh in flock.Members) Hold(sh, Pose.Eat);
                        flock.HomeX = P[Target].X;
                        flock.HomeZ = P[Target].Z;
                        flock.HasRect = false;
                        flock.HomeRadius = Math.Max(10f, spread + 4f);
                        foreach (AnimalAgent sh in flock.Members)
                        {
                            sh.HomeX = sh.X;
                            sh.HomeZ = sh.Z;
                        }

                        Spot exit = Away(cx, cz, dog.X - cx, dog.Z - cz, 70f);
                        P[DogExit] = exit;
                        dog.Avoid = null;
                        Walk(dog, exit.X, exit.Z, dog.Info.WalkSpeed * 1.4f);
                        SetPhase(2);
                    }

                    break;
                }

                case 2: // the dog trots off
                    if (PhaseTime > 6f)
                    {
                        dogGroup.HomeX = P[DogExit].X;
                        dogGroup.HomeZ = P[DogExit].Z;
                        dogGroup.HomeRadius = 10f;
                        Finish("the flock is on new grass");
                    }

                    break;
            }
        }
    }

    /// <summary>The horses in a pasture gallop a wide loop together (at golden hour), then walk and graze again.</summary>
    public sealed class HorsesRunningEvent : AnimalEvent
    {
        private const int Centre = 0;
        private AnimalGroup herd;
        private float radiusX, radiusZ, yaw, theta, laps;
        private AnimalAgent lead;

        public override EventKind Kind => EventKind.HorsesRunning;
        public override string Name => "Horses running";

        public static HorsesRunningEvent Prepare(WildlifeSim sim, in WildlifeContext ctx, bool forced, out float score, out string why)
        {
            score = 0f;
            why = forced || ctx.IsGoldenHour ? "no horses with room to run nearby" : "horses run at golden hour";
            if (!forced && !ctx.IsGoldenHour) return null;
            foreach (AnimalGroup g in sim.Groups)
            {
                if (g.CountOf(Species.Horse) < 2 || !Free(g)) continue;
                g.Centroid(out float cx, out float cz);
                if (!InRange(ctx, cx, cz, forced, 30f, 150f, out float s) || s <= score) continue;
                float rx, rz, centreX, centreZ, yaw = 0f;
                if (g.HasRect)
                {
                    rx = g.HalfX - 3f;
                    rz = g.HalfZ - 3f;
                    centreX = g.RectX;
                    centreZ = g.RectZ;
                    yaw = g.Yaw;
                }
                else
                {
                    rx = rz = 14f;
                    centreX = g.HomeX;
                    centreZ = g.HomeZ;
                }

                if (Math.Min(rx, rz) < 10f)
                {
                    why = "the horses' pasture is too small to gallop";
                    continue;
                }

                // The loop must be on open, dry ground.
                bool ok = true;
                for (int k = 0; k < 8 && ok; k++)
                {
                    Ellipse(centreX, centreZ, rx, rz, yaw, k * 0.785f, out float px, out float pz);
                    ok = sim.CanStand(px, pz, 1f);
                }

                if (!ok)
                {
                    why = "no open ground for the horses to run";
                    continue;
                }

                var e = new HorsesRunningEvent { herd = g, radiusX = rx, radiusZ = rz, yaw = yaw };
                e.P[Centre] = new Spot(centreX, centreZ);
                score = s;
                return e;
            }

            return null;
        }

        private static void Ellipse(float cx, float cz, float rx, float rz, float yawDeg, float t, out float x, out float z)
        {
            double rad = yawDeg * Math.PI / 180.0, cs = Math.Cos(rad), sn = Math.Sin(rad);
            double u = Math.Cos(t) * rx, v = Math.Sin(t) * rz;
            x = (float)(cx + u * cs + v * sn);
            z = (float)(cz - u * sn + v * cs);
        }

        protected override void Begin()
        {
            MaxSeconds = 90f;
            Use(herd);
            Detail = $"{herd.CountOf(Species.Horse)} horses";
            foreach (AnimalAgent a in herd.Members)
            {
                Take(a);
                Hold(a, Pose.Idle);
            }

            lead = herd.First(Species.Horse);
            // Start the loop where the lead stands.
            double rad = yaw * Math.PI / 180.0, cs = Math.Cos(rad), sn = Math.Sin(rad);
            double dx = lead.X - P[Centre].X, dz = lead.Z - P[Centre].Z;
            double u = dx * cs - dz * sn, v = dx * sn + dz * cs;
            theta = (float)Math.Atan2(v / radiusZ, u / radiusX);
            Hold(lead, Pose.Display);   // the lead rears
            Sim.Call(lead, "SFX_Horse_Whinny", 0.55f);
            SetPhase(0);
        }

        protected override void Tick(float dt, in WildlifeContext ctx)
        {
            switch (Phase)
            {
                case 0:
                    if (PhaseTime > 1.4f)
                    {
                        Sim.Call(lead, "SFX_Horse_Run", 0.45f, 20f);
                        SetPhase(1);
                    }

                    break;

                case 1: // gallop the loop, in a line
                {
                    float speed = SpeciesCatalog.Get(Species.Horse).RunSpeed * 0.9f;
                    float r = (radiusX + radiusZ) * 0.5f;
                    float w = speed / r;
                    theta += w * dt;
                    laps += w * dt / 6.2832f;
                    int i = 0;
                    foreach (AnimalAgent a in herd.Members)
                    {
                        if (a.Info.Id != Species.Horse) continue;
                        float t = theta - i * 7f / r + 0.5f;   // a little ahead of each horse's own place on the loop
                        Ellipse(P[Centre].X, P[Centre].Z, radiusX, radiusZ, yaw, t, out float x, out float z);
                        Run(a, x, z, speed);
                        i++;
                    }

                    if (laps > 1.5f || PhaseTime > 60f)
                    {
                        foreach (AnimalAgent a in herd.Members)
                        {
                            if (a.Info.Id != Species.Horse) continue;
                            float rad = a.Yaw * 0.01745f;
                            Walk(a, a.X + (float)Math.Sin(rad) * 6f, a.Z + (float)Math.Cos(rad) * 6f);
                        }

                        SetPhase(2);
                    }

                    break;
                }

                case 2:
                    if (PhaseTime > 6f)
                    {
                        foreach (AnimalAgent a in herd.Members) Hold(a, Pose.Eat);
                        Finish("the horses settled");
                    }

                    break;
            }
        }
    }

    /// <summary>Doves take off together, circle and land; geese (whose controller cannot fly) scurry off along the shore together.</summary>
    public sealed class BirdsFlushingEvent : AnimalEvent
    {
        private AnimalGroup flock;
        private float[] delays;
        private int launched;

        public override EventKind Kind => EventKind.BirdsFlushing;
        public override string Name => "Birds flushing";

        public static BirdsFlushingEvent Prepare(WildlifeSim sim, in WildlifeContext ctx, bool forced, out float score, out string why)
        {
            score = 0f;
            why = "no doves or geese nearby";
            foreach (AnimalGroup g in sim.Groups)
            {
                bool doves = g.Kind == GroupKind.Doves && g.Members.Count >= 3;
                bool geese = g.Main == Species.Goose && g.Kind == GroupKind.Shore && g.Members.Count >= 3;
                if (!(doves || geese) || !Free(g)) continue;
                g.Centroid(out float cx, out float cz);
                if (!InRange(ctx, cx, cz, forced, 15f, 110f, out float s) || s <= score) continue;
                score = s * (doves ? 1f : 0.8f);
                return new BirdsFlushingEvent { flock = g };
            }

            return null;
        }

        protected override void Begin()
        {
            MaxSeconds = 45f;
            Use(flock);
            Detail = $"{flock.Members.Count} {SpeciesCatalog.Get(flock.Main).Name}s";
            delays = new float[flock.Members.Count];
            for (int i = 0; i < delays.Length; i++) delays[i] = Rng.Range(0f, 0.7f);
            if (flock.Main == Species.Goose)
            {
                flock.Centroid(out float cx, out float cz);
                float ax = cx - Sim.Context.TravellerX, az = cz - Sim.Context.TravellerZ;
                for (int t = 0; t < 6; t++)
                {
                    float ang = t * 1.05f;
                    float dx = ax * (float)Math.Cos(ang) - az * (float)Math.Sin(ang), dz = ax * (float)Math.Sin(ang) + az * (float)Math.Cos(ang);
                    Spot s = Away(cx, cz, dx, dz, 13f);
                    if (!Sim.CanStand(s.X, s.Z, 0.5f, true)) continue;
                    P[0] = s;
                    break;
                }
            }

            SetPhase(0);
        }

        protected override void Tick(float dt, in WildlifeContext ctx)
        {
            bool geese = flock.Main == Species.Goose;
            for (int i = launched; i < flock.Members.Count; i++)
            {
                if (PhaseTime < delays[i]) continue;
                AnimalAgent a = flock.Members[i];
                if (geese)
                {
                    Take(a);
                    Run(a, P[0].X + (a.X - flock.HomeX) * 0.6f, P[0].Z + (a.Z - flock.HomeZ) * 0.6f);
                }
                else
                {
                    AnimalBrain.TakeOff(a, flock.HomeX, flock.HomeZ, Rng.Range(8f, 14f));
                }

                launched = Math.Max(launched, i + 1);
            }

            if (launched < flock.Members.Count || PhaseTime < 2f) return;
            bool settled = true;
            foreach (AnimalAgent a in flock.Members)
            {
                if (geese) settled &= !a.HasTarget;
                else settled &= a.Altitude <= 0.01f && a.Mode != BrainMode.Fly;
            }

            if (!settled) return;
            if (geese)
            {
                flock.HomeX = P[0].X;
                flock.HomeZ = P[0].Z;
            }

            Finish(geese ? "the geese settled" : "the doves landed");
        }
    }

    /// <summary>Two stags face each other and feint (harmless display), then drift apart and graze.</summary>
    public sealed class StagsSquaringUpEvent : AnimalEvent
    {
        public const float Gap = 3f;
        private const int Mid = 0, Spawn = 1;
        private AnimalGroup herd, visitor;
        private AnimalAgent a, b;
        private int feints;
        private bool spawn;

        public override EventKind Kind => EventKind.StagsSquaringUp;
        public override string Name => "Stags squaring up";

        public static StagsSquaringUpEvent Prepare(WildlifeSim sim, in WildlifeContext ctx, bool forced, out float score, out string why)
        {
            score = 0f;
            why = "no stags nearby";
            foreach (AnimalGroup g in sim.Groups)
            {
                int stags = g.CountOf(Species.Stag);
                if (stags == 0 || !Free(g)) continue;
                g.Centroid(out float cx, out float cz);
                if (!InRange(ctx, cx, cz, forced, 25f, 150f, out float s) || s <= score) continue;
                var e = new StagsSquaringUpEvent { herd = g };
                if (stags < 2)
                {
                    // A rival walks in from the far side.
                    if (!FindSpawn(sim, ctx, cx, cz, cx - ctx.TravellerX, cz - ctx.TravellerZ, 30f, 40f, forced ? 35f : 80f, out float x, out float z))
                    {
                        why = "no room for a rival stag";
                        continue;
                    }

                    e.P[Spawn] = new Spot(x, z);
                    e.spawn = true;
                    s *= 0.7f;
                }

                score = s;
                return e;
            }

            return null;
        }

        protected override void Begin()
        {
            MaxSeconds = 60f;
            Use(herd);
            a = herd.First(Species.Stag);
            b = null;
            foreach (AnimalAgent m in herd.Members)
            {
                if (m != a && m.Info.Id == Species.Stag) b = m;
            }

            if (b == null || spawn)
            {
                visitor = Sim.AddVisitor(Species.Stag, 1, P[Spawn].X, P[Spawn].Z, 0f, Id ^ 0x0D0D);
                Use(visitor);
                b = visitor.Members[0];
            }

            Detail = spawn ? "a stag and a rival" : "two stags";
            Take(a);
            Take(b);
            float mx = (a.X + b.X) * 0.5f, mz = (a.Z + b.Z) * 0.5f;
            if (spawn)
            {
                mx = a.X * 0.7f + b.X * 0.3f;
                mz = a.Z * 0.7f + b.Z * 0.3f;
            }

            P[Mid] = new Spot(mx, mz);
            float ax = a.X - mx, az = a.Z - mz;
            Spot pa = Away(mx, mz, ax, az, 2.2f), pb = Away(mx, mz, -ax, -az, 2.2f);
            Walk(a, pa.X, pa.Z);
            Walk(b, pb.X, pb.Z);
            a.Avoid = b.Group;
            a.AvoidGap = Gap;
            b.Avoid = a.Group;
            b.AvoidGap = Gap;
            SetPhase(0);
        }

        protected override void Tick(float dt, in WildlifeContext ctx)
        {
            switch (Phase)
            {
                case 0: // come together
                    if ((!a.HasTarget && !b.HasTarget) || PhaseTime > 25f)
                    {
                        Hold(a, Pose.Idle);
                        Hold(b, Pose.Idle);
                        a.FacePoint(b.X, b.Z);
                        b.FacePoint(a.X, a.Z);
                        SetPhase(1);
                    }

                    break;

                case 1: // feints, in turn
                {
                    a.FacePoint(b.X, b.Z);
                    b.FacePoint(a.X, a.Z);
                    float beat = 2.4f;
                    int round = (int)(PhaseTime / beat);
                    float inBeat = PhaseTime - round * beat;
                    AnimalAgent mover = round % 2 == 0 ? a : b, other = mover == a ? b : a;
                    if (round >= 6)
                    {
                        Hold(a, Pose.Idle);
                        Hold(b, Pose.Idle);
                        // Drift apart.
                        Spot sa = Away(a.X, a.Z, a.X - b.X, a.Z - b.Z, 10f), sb = Away(b.X, b.Z, b.X - a.X, b.Z - a.Z, 10f);
                        if (Sim.CanStand(sa.X, sa.Z, 0.5f)) Walk(a, sa.X, sa.Z);
                        if (Sim.CanStand(sb.X, sb.Z, 0.5f)) Walk(b, sb.X, sb.Z);
                        SetPhase(2);
                        break;
                    }

                    Hold(other, Pose.Idle);
                    Hold(mover, inBeat < 1.1f ? Pose.Display : Pose.Idle);
                    if (round != feints && inBeat < 0.2f && mover.Rng.Next() < 0.5f) AnimalBrain.Voice(Sim, mover, Pose.Alert);
                    feints = round;
                    break;
                }

                case 2:
                    if ((!a.HasTarget && !b.HasTarget) || PhaseTime > 15f)
                    {
                        Hold(a, Pose.Eat);
                        Hold(b, Pose.Eat);
                        if (visitor != null)
                        {
                            visitor.HomeX = b.X;
                            visitor.HomeZ = b.Z;
                            visitor.HomeRadius = 12f;
                        }

                        Finish("the stags drifted apart");
                    }

                    break;
            }
        }

        /// <summary>Distance between the two stags (tests).</summary>
        public float StagDistance => a != null && b != null ? a.DistanceTo(b) : float.PositiveInfinity;
    }
}
