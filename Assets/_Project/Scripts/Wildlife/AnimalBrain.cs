using System;

namespace MedievalZen.Wildlife
{
    /// <summary>
    /// Per-animal behaviour (pure): idle, graze, wander within the home range with herd cohesion, rest (farm animals sleep at
    /// night, cats nap), alert (looks at the traveller), flee then calm down and return, step aside (a hen underfoot),
    /// follow (chicks follow the hen), sit (dogs), fly (doves take off when startled, circle and land; the eagle circles).
    /// Reactions depend on the traveller's distance and speed and on whether they sit: after a while seated, wild animals
    /// let them be much closer and drift nearer. The state lives on <see cref="AnimalAgent"/>.
    /// </summary>
    public static class AnimalBrain
    {
        /// <summary>Seconds seated after which wild animals relax.</summary>
        public const float SeatedCalm = 20f;

        /// <summary>The traveller's alert and flee distances for a species right now (closer when seated, further when running).</summary>
        public static void Reach(SpeciesInfo info, in WildlifeContext ctx, out float alert, out float flee)
        {
            alert = info.AlertDistance;
            flee = info.FleeDistance;
            if (ctx.Seated && ctx.SeatedTime > SeatedCalm)
            {
                alert *= 0.35f;
                flee *= 0.35f;
            }
            else if (ctx.Speed > 3f)
            {
                alert *= 1.25f;
                flee *= 1.5f;
            }
        }

        /// <summary>One decision step for an animal not under an event's control.</summary>
        public static void Think(WildlifeSim sim, AnimalAgent a, float dt, in WildlifeContext ctx)
        {
            SpeciesInfo info = a.Info;
            if (info.Id == Species.Eagle)
            {
                a.Pose = Pose.Fly;
                return;
            }

            float d = ctx.HasTraveller ? a.TravellerDistance : float.MaxValue;
            if (a.Mode == BrainMode.Fly) return;   // the flight step lands the bird and hands back

            // A farm dog gives one bark when the traveller comes up (then not again for a while).
            if (info.Has(Pose.Bark) && d < 14f && ctx.Time >= a.NextSound && a.Mode != BrainMode.Rest)
            {
                a.Driver.Pulse(Pose.Bark);
                Voice(sim, a, Pose.Bark);
                a.NextSound = ctx.Time + a.Rng.Range(45f, 90f);
                a.FacePoint(ctx.TravellerX, ctx.TravellerZ);
            }

            // Wild animals (and wary farm ones) react to the traveller.
            if (info.AlertDistance > 0f && ctx.HasTraveller)
            {
                Reach(info, ctx, out float alert, out float flee);
                bool moving = ctx.Speed > 0.3f;
                if (d < flee && a.Mode != BrainMode.Flee && a.Mode != BrainMode.Fly && (moving || d < flee * 0.6f))
                {
                    StartFlee(sim, a, ctx.TravellerX, ctx.TravellerZ, ctx);
                    Alarm(sim, a, ctx);
                    return;
                }

                if (d < alert && (moving || !ctx.Seated) && IsCalm(a.Mode))
                {
                    a.Stop();
                    a.FacePoint(ctx.TravellerX, ctx.TravellerZ);
                    a.SetMode(BrainMode.Alert, a.Rng.Range(3f, 7f));
                    a.Pose = info.Has(Pose.Alert) ? Pose.Alert : Pose.Idle;
                    if (ctx.Time >= a.NextSound && a.Rng.Next() < 0.25f)
                    {
                        Voice(sim, a, Pose.Alert);
                        a.NextSound = ctx.Time + 40f;
                    }
                    return;
                }
            }

            // A tame animal underfoot steps aside.
            if (ctx.HasTraveller && info.AlertDistance <= 0f && a.Mode != BrainMode.StepAside && a.Mode != BrainMode.Rest)
            {
                float space = info.PersonalSpace + (ctx.Speed > 2.5f ? 1.2f : 0f);
                if (d < space)
                {
                    StepAside(sim, a, ctx);
                    return;
                }
            }

            switch (a.Mode)
            {
                case BrainMode.Flee:
                    if (a.Arrived || !a.HasTarget || a.ModeTime >= a.ModeDuration)
                    {
                        a.Stop();
                        a.SetMode(BrainMode.Calm, a.Rng.Range(6f, 14f));
                        a.Pose = info.Has(Pose.Alert) ? Pose.Alert : Pose.Idle;
                        if (ctx.HasTraveller) a.FacePoint(ctx.TravellerX, ctx.TravellerZ);
                    }

                    return;

                case BrainMode.Alert:
                    if (ctx.HasTraveller) a.FacePoint(ctx.TravellerX, ctx.TravellerZ);
                    if (a.ModeTime >= a.ModeDuration) Next(sim, a, ctx);
                    return;

                case BrainMode.Wander:
                case BrainMode.Return:
                case BrainMode.StepAside:
                    if (a.Arrived || !a.HasTarget || a.ModeTime >= a.ModeDuration) Next(sim, a, ctx);
                    return;

                case BrainMode.Follow:
                    Follow(sim, a, ctx);
                    return;

                case BrainMode.Rest:
                    // Woken by the morning (farm animals) or when the nap is over.
                    if (a.ModeTime >= a.ModeDuration || (info.SleepsAtNight && !ctx.IsNight && a.ModeTime > 5f)) Next(sim, a, ctx);
                    return;

                default:
                    if (a.ModeTime >= a.ModeDuration) Next(sim, a, ctx);
                    return;
            }
        }

        private static bool IsCalm(BrainMode m) =>
            m == BrainMode.Idle || m == BrainMode.Graze || m == BrainMode.Wander || m == BrainMode.Rest || m == BrainMode.Follow;

        /// <summary>Chooses what to do next.</summary>
        public static void Next(WildlifeSim sim, AnimalAgent a, in WildlifeContext ctx)
        {
            SpeciesInfo info = a.Info;
            AnimalGroup g = a.Group;
            a.HasFace = false;

            // Far from home (after fleeing, or scattered by an event): walk back first.
            if (g != null && !g.InHome(a.X, a.Z, 6f) && a.Mode != BrainMode.Return)
            {
                g.HomePoint(a.Rng.Next(), a.Rng.Next(), out float hx, out float hz);
                if (sim.CanStand(hx, hz, 0.5f))
                {
                    a.GoTo(hx, hz, false);
                    a.SetMode(BrainMode.Return, 60f);
                    a.Pose = Pose.Walk;
                    return;
                }
            }

            if (info.SleepsAtNight && ctx.IsNight && info.Has(Pose.Sleep))
            {
                a.Stop();
                a.SetMode(BrainMode.Rest, a.Rng.Range(90f, 240f));
                a.Pose = Pose.Sleep;
                return;
            }

            if (a.Mother != null && a.Mother.Alive && a.Rng.Next() < 0.7f)
            {
                a.SetMode(BrainMode.Follow, a.Rng.Range(8f, 20f));
                Follow(sim, a, ctx);
                return;
            }

            float r = a.Rng.Next();
            switch (info.Id)
            {
                case Species.Retriever:
                case Species.GreatDane:
                    if (r < 0.35f) Sit(a, a.Rng.Range(20f, 60f));
                    else if (r < 0.6f) Idle(a, a.Rng.Range(3f, 8f));
                    else Wander(sim, a, ctx, 3f, 9f);
                    return;

                case Species.Cat:
                    if (r < 0.4f && info.Has(Pose.Sleep))
                    {
                        a.Stop();
                        a.SetMode(BrainMode.Rest, a.Rng.Range(60f, 200f));
                        a.Pose = Pose.Sleep;
                    }
                    else if (r < 0.7f) Idle(a, a.Rng.Range(4f, 10f));
                    else Wander(sim, a, ctx, 2f, 7f);

                    return;

                case Species.Wolf:
                    if (r < 0.12f && ctx.IsNight)
                    {
                        a.Stop();
                        a.SetMode(BrainMode.Howl, 4.5f);
                        a.Pose = Pose.Howl;
                        sim.Call(a, "SFX_Wolf_Howl", 0.7f);
                    }
                    else if (r < 0.45f) Idle(a, a.Rng.Range(4f, 10f));
                    else Wander(sim, a, ctx, 6f, 18f);

                    return;

                case Species.Squirrel:
                    if (r < 0.5f) Idle(a, a.Rng.Range(1.5f, 5f));
                    else Wander(sim, a, ctx, 2f, 6f, a.Rng.Next() < 0.5f);
                    return;
            }

            // Grazers and the rest: mostly eat, sometimes look about, take a few steps.
            if (info.Has(Pose.Eat) && r < 0.5f)
            {
                a.Stop();
                a.SetMode(BrainMode.Graze, a.Rng.Range(8f, 25f));
                a.Pose = Pose.Eat;
                return;
            }

            if (r < 0.72f)
            {
                Idle(a, a.Rng.Range(3f, 9f));
                if (info.Has(Pose.Idle2) && a.Rng.Next() < 0.35f) a.Pose = Pose.Idle2;
                return;
            }

            // Wild animals drift towards a traveller who has sat still for a while.
            if (info.Wild && ctx.HasTraveller && ctx.Seated && ctx.SeatedTime > AnimalBrain.SeatedCalm && a.TravellerDistance < 80f && a.Rng.Next() < 0.6f)
            {
                Reach(info, ctx, out _, out float flee);
                float stop = Math.Max(5f, flee + 3f);
                float tx = ctx.TravellerX - a.X, tz = ctx.TravellerZ - a.Z;
                float dist = (float)Math.Sqrt(tx * tx + tz * tz);
                float stepLength = Math.Min(8f, dist - stop);
                if (stepLength > 1f)
                {
                    float x = a.X + tx / dist * stepLength, z = a.Z + tz / dist * stepLength;
                    if (sim.CanStand(x, z, 0.4f))
                    {
                        a.GoTo(x, z, false);
                        a.SetMode(BrainMode.Wander, 20f);
                        a.Pose = Pose.Walk;
                        return;
                    }
                }
            }

            Wander(sim, a, ctx, 2f, 8f);
        }

        private static void Idle(AnimalAgent a, float seconds)
        {
            a.Stop();
            a.SetMode(BrainMode.Idle, seconds);
            a.Pose = Pose.Idle;
        }

        private static void Sit(AnimalAgent a, float seconds)
        {
            a.Stop();
            a.SetMode(BrainMode.Sit, seconds);
            a.Pose = a.Info.Has(Pose.Sit) ? Pose.Sit : Pose.Idle;
        }

        /// <summary>A few steps to a point in the home range, near the group's centre (herds keep together).</summary>
        private static void Wander(WildlifeSim sim, AnimalAgent a, in WildlifeContext ctx, float min, float max, bool run = false)
        {
            AnimalGroup g = a.Group;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                float ang = a.Rng.Range(0f, 6.2832f), len = a.Rng.Range(min, max);
                float bx = a.X, bz = a.Z;
                if (g != null && g.Members.Count > 2)
                {
                    // Cohesion: lean towards the herd's centre.
                    g.Centroid(out float cx, out float cz);
                    bx = a.X * 0.5f + cx * 0.5f;
                    bz = a.Z * 0.5f + cz * 0.5f;
                }

                float x = bx + (float)Math.Cos(ang) * len, z = bz + (float)Math.Sin(ang) * len;
                if (g != null && !g.InHome(x, z)) continue;
                if (!sim.CanStand(x, z, 0.5f)) continue;
                a.GoTo(x, z, run);
                a.SetMode(BrainMode.Wander, 30f);
                a.Pose = run ? Pose.Run : Pose.Walk;
                return;
            }

            Idle(a, a.Rng.Range(2f, 5f));
        }

        private static void Follow(WildlifeSim sim, AnimalAgent a, in WildlifeContext ctx)
        {
            AnimalAgent m = a.Mother;
            if (m == null || !m.Alive || a.ModeTime >= a.ModeDuration)
            {
                a.Mother = m != null && m.Alive ? m : null;
                a.SetMode(BrainMode.Idle, 0.1f);
                Next(sim, a, ctx);
                return;
            }

            float d = a.DistanceTo(m);
            if (d > 1.6f)
            {
                float yaw = (a.Serial * 77) % 360 * 0.01745f;
                a.GoTo(m.X + (float)Math.Sin(yaw) * 0.8f, m.Z + (float)Math.Cos(yaw) * 0.8f, d > 6f);
                a.Pose = d > 6f ? Pose.Run : Pose.Walk;
            }
            else if (!a.HasTarget)
            {
                a.Stop();
                a.Pose = a.Info.Has(Pose.Eat) && m.Pose == Pose.Eat ? Pose.Eat : Pose.Idle;
            }
        }

        private static void StepAside(WildlifeSim sim, AnimalAgent a, in WildlifeContext ctx)
        {
            float ax = a.X - ctx.TravellerX, az = a.Z - ctx.TravellerZ;
            // Away from the traveller and off their line of travel.
            float side = ax * ctx.FacingZ - az * ctx.FacingX >= 0f ? 1f : -1f;
            float ox = ax + ctx.FacingZ * side * 1.5f, oz = az - ctx.FacingX * side * 1.5f;
            float len = (float)Math.Sqrt(ox * ox + oz * oz);
            if (len < 1e-3f)
            {
                ox = 1f;
                oz = 0f;
                len = 1f;
            }

            float dist = a.Info.PersonalSpace + 2f;
            float x = ctx.TravellerX + ox / len * dist, z = ctx.TravellerZ + oz / len * dist;
            bool run = ctx.Speed > 2.5f || a.TravellerDistance < a.Info.PersonalSpace * 0.7f;
            a.GoTo(x, z, run);
            a.SetMode(BrainMode.StepAside, 4f);
            a.Pose = run ? Pose.Run : Pose.Walk;
        }

        /// <summary>Bolts away from (fromX, fromZ): a run to a standable point well out of reach (birds take off instead).</summary>
        public static void StartFlee(WildlifeSim sim, AnimalAgent a, float fromX, float fromZ, in WildlifeContext ctx)
        {
            SpeciesInfo info = a.Info;
            if (info.Flies && info.Id == Species.Dove)
            {
                TakeOff(a, a.Group != null ? a.Group.HomeX : a.X, a.Group != null ? a.Group.HomeZ : a.Z, a.Rng.Range(8f, 14f));
                return;
            }

            float ax = a.X - fromX, az = a.Z - fromZ;
            float len = (float)Math.Sqrt(ax * ax + az * az);
            if (len < 1e-3f)
            {
                ax = 1f;
                az = 0f;
                len = 1f;
            }

            ax /= len;
            az /= len;
            float distance = Math.Max(10f, info.FleeDistance * 1.6f);
            for (int attempt = 0; attempt < 7; attempt++)
            {
                float turn = (attempt == 0 ? a.Rng.Range(-20f, 20f) : (attempt % 2 == 1 ? 1 : -1) * 30f * ((attempt + 1) / 2)) * 0.01745f;
                float cs = (float)Math.Cos(turn), sn = (float)Math.Sin(turn);
                float dx = ax * cs - az * sn, dz = ax * sn + az * cs;
                float x = a.X + dx * distance, z = a.Z + dz * distance;
                if (!sim.CanStand(x, z, 0.4f, true)) continue;
                a.GoTo(x, z, true);
                a.SetMode(BrainMode.Flee, distance / Math.Max(1f, info.RunSpeed) + 3f);
                a.Pose = Pose.Run;
                if (a.Group != null) a.Group.Disturbed = true;
                return;
            }

            // Nowhere to go: stand alert instead.
            a.Stop();
            a.SetMode(BrainMode.Alert, 4f);
            a.Pose = info.Has(Pose.Alert) ? Pose.Alert : Pose.Idle;
        }

        /// <summary>The rest of the group follows a fleeing animal a moment later.</summary>
        private static void Alarm(WildlifeSim sim, AnimalAgent a, in WildlifeContext ctx)
        {
            AnimalGroup g = a.Group;
            if (g == null) return;
            foreach (AnimalAgent o in g.Members)
            {
                if (o == a || o.Scripted || o.Mode == BrainMode.Flee || o.Mode == BrainMode.Fly || o.Info.AlertDistance <= 0f) continue;
                if (o.DistanceTo(a) > 30f) continue;
                StartFlee(sim, o, ctx.TravellerX, ctx.TravellerZ, ctx);
            }
        }

        // ------------------------------------------------------------------ flight

        /// <summary>Takes off and circles (centre, radius) for a while before landing back home.</summary>
        public static void TakeOff(AnimalAgent a, float centreX, float centreZ, float seconds)
        {
            a.Stop();
            a.SetMode(BrainMode.Fly, seconds);
            a.Pose = Pose.Fly;
            a.CircleX = centreX;
            a.CircleZ = centreZ;
            a.CircleRadius = a.Rng.Range(9f, 16f);
            a.CircleAngle = (float)Math.Atan2(a.Z - centreZ, a.X - centreX);
            a.CircleDirection = a.Rng.Next() < 0.5f ? -1f : 1f;
            a.AltitudeTarget = a.Rng.Range(7f, 12f);
            if (a.Group != null) a.Group.Disturbed = true;
        }

        /// <summary>Moves a flying bird (true while it is airborne and the ground mover must not run).</summary>
        public static bool FlyStep(WildlifeSim sim, AnimalAgent a, float dt)
        {
            SpeciesInfo info = a.Info;
            if (info.Id == Species.Eagle)
            {
                // A slow, wide circle with a gentle rise and fall.
                float w = info.FlySpeed / Math.Max(10f, a.CircleRadius);
                a.CircleAngle += w * dt * a.CircleDirection;
                float x = a.CircleX + (float)Math.Cos(a.CircleAngle) * a.CircleRadius;
                float z = a.CircleZ + (float)Math.Sin(a.CircleAngle) * a.CircleRadius;
                a.Yaw = (float)(Math.Atan2(x - a.X, z - a.Z) * 180.0 / Math.PI);
                a.X = x;
                a.Z = z;
                a.Speed = info.FlySpeed;
                a.Altitude += (float)Math.Sin(a.CircleAngle * 0.5f) * 0.3f * dt;
                return true;
            }

            bool landing = a.Mode != BrainMode.Fly || a.ModeTime >= a.ModeDuration;
            if (!landing)
            {
                // Rise, then circle.
                a.Altitude = WildlifeSim.MoveTowards(a.Altitude, a.AltitudeTarget, 2.2f * dt);
                float w = info.FlySpeed / Math.Max(4f, a.CircleRadius);
                a.CircleAngle += w * dt * a.CircleDirection;
                float x = a.CircleX + (float)Math.Cos(a.CircleAngle) * a.CircleRadius;
                float z = a.CircleZ + (float)Math.Sin(a.CircleAngle) * a.CircleRadius;
                float sx = x - a.X, sz = z - a.Z;
                float len = (float)Math.Sqrt(sx * sx + sz * sz);
                float step = info.FlySpeed * dt;
                if (len > step && len > 1e-4f)
                {
                    x = a.X + sx / len * step;
                    z = a.Z + sz / len * step;
                }

                if (len > 1e-4f) a.Yaw = (float)(Math.Atan2(sx, sz) * 180.0 / Math.PI);
                a.X = x;
                a.Z = z;
                a.Speed = info.FlySpeed;
                a.Pose = Pose.Fly;
                return true;
            }

            // Glide home and settle.
            float hx = a.HomeX - a.X, hz = a.HomeZ - a.Z;
            float hd = (float)Math.Sqrt(hx * hx + hz * hz);
            float glide = Math.Min(hd, info.FlySpeed * 0.6f * dt);
            if (hd > 1e-3f)
            {
                a.X += hx / hd * glide;
                a.Z += hz / hd * glide;
                a.Yaw = (float)(Math.Atan2(hx, hz) * 180.0 / Math.PI);
            }

            a.Altitude = WildlifeSim.MoveTowards(a.Altitude, hd < 2f ? 0f : Math.Min(a.Altitude, hd * 0.6f), 2.5f * dt);
            a.Speed = glide / Math.Max(1e-3f, dt);
            if (a.Altitude > 0.02f || hd > 0.3f) return true;
            a.Altitude = 0f;
            a.Speed = 0f;
            a.Pose = Pose.Idle;
            a.SetMode(BrainMode.Idle, a.Rng.Range(2f, 6f));
            return false;
        }

        // ------------------------------------------------------------------ voice

        /// <summary>Spontaneous calls within earshot (a moo, a cow bell while walking, grass being eaten close by).</summary>
        public static void Voice(WildlifeSim sim, AnimalAgent a, float dt, in WildlifeContext ctx)
        {
            SpeciesInfo info = a.Info;
            if (info.Sounds.Length == 0 || a.TravellerDistance > 85f || ctx.Time < a.NextSound) return;
            for (int i = 0; i < info.Sounds.Length; i++)
            {
                SpeciesSound s = info.Sounds[i];
                if (s.MeanInterval <= 0f)
                {
                    // Cue-only calls with no pose (a cat's meow) come now and then when the traveller is close.
                    if (s.When != Pose.Idle || a.TravellerDistance > 6f || a.Rng.Next() > dt / 12f) continue;
                    sim.Call(a, s.Clip, s.Volume);
                    a.NextSound = ctx.Time + 25f;
                    return;
                }

                if (s.When != Pose.Idle && a.Driver.Shown != s.When) continue;
                if (s.Clip == "SFX_Eating_Grass" && a.TravellerDistance > 12f) continue;
                if (a.Rng.Next() > dt / s.MeanInterval) continue;
                sim.Call(a, s.Clip, s.Volume);
                a.NextSound = ctx.Time + Math.Max(4f, s.MeanInterval * 0.3f);
                return;
            }
        }

        /// <summary>The species' call for a cue pose (alarm, bark, howl), if it has one.</summary>
        public static void Voice(WildlifeSim sim, AnimalAgent a, Pose cue)
        {
            SpeciesInfo info = a.Info;
            for (int i = 0; i < info.Sounds.Length; i++)
            {
                if (info.Sounds[i].When != cue || info.Sounds[i].MeanInterval > 0f) continue;
                sim.Call(a, info.Sounds[i].Clip, info.Sounds[i].Volume);
                return;
            }
        }
    }
}
