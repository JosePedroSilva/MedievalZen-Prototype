using System;
using System.Collections;
using MedievalZen.Environment;
using MedievalZen.UI;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>Holding the traveller while the ground is missing, Go now, New journey, persistence.</summary>
    public sealed partial class EndlessWorld
    {
        private const float StartFadeInSeconds = 1.5f;
        private const float HoldFadeOutSeconds = 0.35f;
        private const float HoldFadeInSeconds = 0.8f;
        private const float TravelFadeOutSeconds = 0.9f;
        private const float TravelFadeInSeconds = 1.3f;

        private float nextSave;
        private float nextState;
        private bool timeRestored;
        private Action afterStart;

        /// <summary>True while Go now or New journey is running.</summary>
        public bool IsBusy => scripted || startup;

        // ------------------------------------------------------------------ holds

        private void ApplyFreeze()
        {
            if (body == null) return;
            bool frozen = frozenByStream || frozenByScript || startup;
            if (body.enabled == !frozen) return;
            body.enabled = !frozen;
        }

        private void UpdateHold()
        {
            if (traveller == null) return;
            UpdatePropWarm();
            if (startup)
            {
                if (StartReady()) FinishStart();
                return;
            }

            if (scripted) return;
            bool ready = GroundReadyAt(traveller.position);
            HudController hud = HudController.Instance;
            if (!ready && !frozenByStream)
            {
                // The traveller outran the streamer (sprinting a long way, a long auto-travel at a low frame rate): hold them on
                // black until the ground under them exists, instead of letting them fall through.
                frozenByStream = true;
                ApplyFreeze();
                if (hud != null && hud.FadeAlpha < 0.99f)
                {
                    hud.Fade(1f, HoldFadeOutSeconds);
                    holdFaded = true;
                }
            }
            else if (ready && frozenByStream)
            {
                frozenByStream = false;
                EndPropWarm();
                SettleOnGround();
                ApplyFreeze();
                if (hud != null && holdFaded) hud.Fade(0f, HoldFadeInSeconds);
                holdFaded = false;
            }
        }

        /// <summary>Near tiles all built, far tiles within the start radius built (or the timeout passed), and the road window ready.</summary>
        private bool StartReady() => window != null && AreaReady(startupBegan) && !HoldForWarmup();

        /// <summary>
        /// Every wanted near tile is built (a long safety timeout aside), and every far tile within
        /// <see cref="startFarRadius"/> too unless <see cref="startTimeout"/> has passed since <paramref name="since"/>.
        /// </summary>
        private bool AreaReady(float since)
        {
            if (traveller == null) return true;
            float waited = Time.unscaledTime - since;
            bool timedOut = waited > startTimeout;
            foreach (TileWant w in wants)
            {
                if (states.TryGetValue(w.Coord, out CoordState s) && s.Active != null && s.Active.Lod == w.Lod) continue;
                if (w.Lod == TileLod.Near && waited <= startTimeout * 4f) return false;   // all near tiles, unless something is badly stuck
                if (w.Lod != TileLod.Near && w.Distance <= startFarRadius && !timedOut) return false;
            }

            return GroundReadyAt(traveller.position);   // never released over missing ground
        }

        private void FinishStart()
        {
            EndPropWarm();
            startup = false;
            frozenByScript = false;
            PlaceTravellerAt(travellerS, startFacing);
            SettleOnGround();
            ApplyFreeze();
            RestoreTime();
            UpdateInns(true);
            regionKnown = false;
            UpdateRegion();
            HudController hud = HudController.Instance;
            if (hud != null)
            {
                hud.Fade(0f, StartFadeInSeconds);
                if (regionKnown) hud.ShowToast(region.Name);
            }

            holdFaded = false;
            scripted = false;
            string rocks = builder.TakeSafetyReport();
            Debug.Log($"Endless road: ready after {Time.unscaledTime - startupBegan:0.0} s at {travellerS:0} m — {CountTiles(TileLod.Near)} near tiles " +
                      $"({builder.TerrainsCreated} Unity terrains made), {builder.FarShown} far meshes ({builder.FarTrianglesShown / 1000}k triangles), " +
                      $"{(trees != null ? trees.RegisteredTrees : 0)} near trees; safety level {safetyLevel}{(rocks != null ? "; " + rocks : string.Empty)}.", this);
            NoteReady();
            Action done = afterStart;
            afterStart = null;
            done?.Invoke();
        }

        /// <summary>Puts the traveller back on the ground if they are under it or floating well above it.</summary>
        private void SettleOnGround()
        {
            if (traveller == null) return;
            Vector3 p = traveller.position;
            (double x, double z) = origin.ToAbsolute(p);
            float ground = GroundHeightAbsolute(x, z);
            if (p.y > ground - 0.3f && p.y < ground + 2.5f) return;
            bool enabledBefore = body != null && body.enabled;
            if (body != null) body.enabled = false;
            traveller.position = new Vector3(p.x, ground + 0.05f, p.z);
            if (body != null) body.enabled = enabledBefore;
            Physics.SyncTransforms();
        }

        // ------------------------------------------------------------------ heavy prop warm-up (third performance pass, 2026-10-06)

        private const float WarmStageDistance = 45f;
        private bool warmWasBlack;
        private float warmReadySince = -1f;
        private float warmPassMs;
        private float warmPassWorstMs;
        private int warmPassFrames;
        private float nextPlaceCheck;
        private double nearestPlaceRoad = double.PositiveInfinity;
        private readonly System.Collections.Generic.List<Waypoint> placeScratch = new System.Collections.Generic.List<Waypoint>();
        private readonly System.Diagnostics.Stopwatch stockClock = new System.Diagnostics.Stopwatch();

        /// <summary>
        /// Once per frame (from <see cref="UpdateHold"/>): behind a hold's black screen, warm the heavy props a few per frame in front
        /// of the camera; otherwise keep one idle instance of each in the pool, one instantiate per frame at most, never while tiles
        /// stream and never within <see cref="PropWarmup.StockClearance"/> of a place.
        /// </summary>
        private void UpdatePropWarm()
        {
            if (builder == null) return;
            bool holding = startup || frozenByScript || frozenByStream;
            bool black = holding && IsBlack();
            if (black && !warmWasBlack)
            {
                builder.BeginWarmup();
                warmPassMs = 0f;
                warmPassWorstMs = 0f;
                warmPassFrames = 0;
            }

            warmWasBlack = black;
            if (black)
            {
                if (!builder.WarmPending && !arrivalWarmup.Pending) return;
                Camera cam = Camera.main;
                Vector3 at = cam != null
                    ? cam.transform.position + cam.transform.forward * WarmStageDistance + Vector3.down * 4f
                    : traveller.position + Vector3.down * 2000f;   // no camera: still awake and cook, out of sight
                if (arrivalWarmup.Pending)
                {
                    using (Review.FrameCost.Measure(CostArrivalWarm)) arrivalWarmup.Step(this, at);
                }

                if (!builder.WarmPending) return;
                float ms = builder.WarmStep(at, Quaternion.identity);
                warmPassMs += ms;
                warmPassWorstMs = Mathf.Max(warmPassWorstMs, ms);
                warmPassFrames++;
                if (!builder.WarmPending) LogPropWarm("done");
                return;
            }

            EndPropWarm();
            if (Time.unscaledTime >= nextPlaceCheck)
            {
                nextPlaceCheck = Time.unscaledTime + 1f;
                nearestPlaceRoad = NearestPlaceRoadDistance();
            }

            bool busy = building.Count > 0 || releasing.Count > 0 || scripted;
            if (!PropWarmup.ShouldStock(builder.AnyHeavyPoolEmpty, busy, nearestPlaceRoad)) return;
            stockClock.Restart();
            if (builder.StockOne()) NoteSlice((float)stockClock.Elapsed.TotalMilliseconds, "stock heavy prop");
        }

        // The arrival warm-up (2026-10-09): effects, people, animals, clips and glyphs a place needs the first time, made behind the
        // first black hold (ArrivalWarmup).
        private readonly ArrivalWarmup arrivalWarmup = new ArrivalWarmup();
        private static readonly int CostArrivalWarm = Review.FrameCost.Register("EndlessWorld.ArrivalWarmup (hold)");

        /// <summary>True while a hold should wait (at most a second after the ground is ready) for the warm-up to finish.</summary>
        private bool HoldForWarmup()
        {
            bool pending = (builder != null && builder.WarmPending) || arrivalWarmup.Pending;
            if (!pending)
            {
                warmReadySince = -1f;
                return false;
            }

            if (warmReadySince < 0f) warmReadySince = Time.unscaledTime;
            return PropWarmup.HoldsForWarmup(true, IsBlack(), Time.unscaledTime - warmReadySince);
        }

        /// <summary>Ends a warm-up pass as its hold ends: staged props go to the pool now (never seen once the fade lifts).</summary>
        private void EndPropWarm()
        {
            warmReadySince = -1f;
            arrivalWarmup.EndHold();
            if (builder == null || !builder.WarmPending) return;
            builder.EndWarmup();
            LogPropWarm("cut short by the end of the hold");
        }

        private void LogPropWarm(string how)
        {
            Debug.Log($"Endless road prop warm-up {how}: {warmPassMs:0.0} ms over {warmPassFrames} frames (worst {warmPassWorstMs:0.0} ms); " +
                      $"heavy prefabs warmed {builder.HeavyPrefabsWarmed}/{builder.HeavyPrefabCount}, made ahead {builder.HeavyStockMade}, idle {builder.HeavyStockIdle}.", this);
        }

        /// <summary>Road distance from the traveller to the nearest settlement or landmark within the stock clearance (infinity: none).</summary>
        private double NearestPlaceRoadDistance()
        {
            if (generator == null) return double.PositiveInfinity;
            placeScratch.Clear();
            generator.Waypoints(travellerS - PropWarmup.StockClearance, travellerS + PropWarmup.StockClearance, placeScratch);
            double best = double.PositiveInfinity;
            foreach (Waypoint w in placeScratch)
            {
                if (w == null || !(w.IsSettlement || w.Kind == WaypointKind.Landmark)) continue;
                best = Math.Min(best, Math.Abs(w.S - travellerS));
            }

            placeScratch.Clear();
            return best;
        }

        // ------------------------------------------------------------------ Go now

        /// <summary>
        /// Takes the traveller to <paramref name="inn"/> along the road behind a fade: they arrive on the road just before it
        /// (coming from where they were), facing it; the tiles there are built before the fade lifts. False when busy.
        /// </summary>
        public bool TravelTo(Waypoint inn, Action done = null)
        {
            if (inn == null || scripted || startup || generator == null) return false;
            int direction = inn.S >= travellerS ? 1 : -1;
            double target = inn.S - direction * (RoadWindow.RadiusFor(inn) + 6.0);
            StartCoroutine(TravelRoutine(target, direction, $"The road brings you to {inn.Name}", true, done));
            return true;
        }

        /// <summary>
        /// Takes the traveller to road distance <paramref name="s"/>, facing along the road in <paramref name="direction"/> (+1 ahead,
        /// −1 back), by the same routine as Go now (behind a fade unless <paramref name="fade"/> is false; the near tiles and the
        /// distant ones within the start radius are built before it lifts), then calls <paramref name="done"/>. No toast. False
        /// when busy (used by the review capture tour).
        /// </summary>
        public bool TravelToRoadDistance(double s, int direction, bool fade, Action done = null)
        {
            if (scripted || startup || generator == null || double.IsNaN(s)) return false;
            StartCoroutine(TravelRoutine(s, direction >= 0 ? 1 : -1, null, fade, done));
            return true;
        }

        private IEnumerator TravelRoutine(double target, int direction, string toast, bool fade, Action done)
        {
            scripted = true;
            if (travel != null) travel.Cancel();
            HudController hud = HudController.Instance;
            if (hud != null)
            {
                hud.HidePrompt();
                if (fade) yield return hud.Fade(1f, TravelFadeOutSeconds);
            }

            frozenByScript = true;
            ApplyFreeze();
            // The inn finder, the road window and the people read the places round the target: build them on a worker first.
            yield return WarmWaypointsBefore(target);
            MoveTravellerTo(target, direction);
            float began = Time.unscaledTime;
            yield return new WaitForSecondsRealtime(0.6f);
            // As at the start: the near ground, then the nearer silhouettes (or a timeout), so the view opens complete.
            // Third performance pass: then up to a second more while the heavy props are warmed behind the black screen.
            while (!(AreaReady(began) && !HoldForWarmup()) && Time.unscaledTime - began < 40f) yield return null;
            EndPropWarm();

            PlaceTravellerAt(target, direction);
            SettleOnGround();
            frozenByScript = false;
            frozenByStream = false;
            ApplyFreeze();
            UpdateInns(true);
            regionKnown = false;
            UpdateRegion();
            SaveJourney();
            scripted = false;
            if (hud != null)
            {
                if (toast != null) hud.ShowToast(toast);
                if (fade || hud.FadeAlpha > 0.001f) yield return hud.Fade(0f, TravelFadeInSeconds);
            }

            done?.Invoke();
        }

        // ------------------------------------------------------------------ New journey

        /// <summary>Starts a new journey on a new random road, from its start (s = 0) in the morning. False when busy.</summary>
        public bool BeginNewJourney(Action done = null)
        {
            if (scripted || startup || generator == null) return false;
            StartCoroutine(NewJourneyRoutine(done));
            return true;
        }

        private IEnumerator NewJourneyRoutine(Action done)
        {
            scripted = true;
            if (travel != null) travel.Cancel();
            HudController hud = HudController.Instance;
            if (hud != null)
            {
                hud.HidePrompt();
                yield return hud.Fade(1f, TravelFadeOutSeconds);
            }

            frozenByScript = true;
            ApplyFreeze();
            ResetStreaming();
            int seed = NewSeed();
            journey = EndlessJourney.New(seed);
            CreateGenerator(seed);
            regionKnown = false;
            MoveTravellerTo(0.0, +1);
            startFacing = +1;
            if (DayCycle.Current != null) DayCycle.Current.SetTime(8f);
            timeRestored = true;
            SaveJourney();
            startup = true;
            startupBegan = Time.unscaledTime;
            afterStart = done;
            holdFaded = true;
        }

        /// <summary>Drops every tile, job, window and remembered place (the pools are kept for the next road).</summary>
        private void ResetStreaming()
        {
            CancelAll();
            foreach (CoordState state in states.Values)
            {
                if (state.Building != null) builder.ReleaseNow(state.Building);
                if (state.Active != null) builder.ReleaseNow(state.Active);
            }

            foreach (EndlessTile tile in releasing) builder.ReleaseNow(tile);
            trees?.Clear();
            states.Clear();
            building.Clear();
            releasing.Clear();
            wants.Clear();
            window = null;
            windowJob = null;
            knownWaypoints.Clear();
            innAhead = innBehind = hudInn = null;
            innCacheS = double.NaN;
            world.SetRoads(null);
            if (travel != null) travel.ContinueTravel(null, Vector3.zero);
        }

        private static int NewSeed()
        {
            var rng = new System.Random(unchecked(System.Environment.TickCount * 31 + Guid.NewGuid().GetHashCode()));
            return rng.Next(1, int.MaxValue);
        }

        // ------------------------------------------------------------------ persistence

        private int startFacing = +1;

        /// <summary>The loaded journey was saved on horseback (read once by <see cref="MedievalZen.Player.HorseMount"/> when the start hold ends).</summary>
        public bool ResumeMounted => journey != null && journey.Mounted;

        /// <summary>The saved riding gait (0 walk, 1 trot, 2 canter).</summary>
        public int ResumeGait => journey != null ? journey.Gait : 0;

        private EndlessJourney LoadJourney()
        {
            try
            {
                string json = PlayerPrefs.GetString(EndlessJourney.PrefsKey, string.Empty);
                if (!string.IsNullOrEmpty(json))
                {
                    if (EndlessJourney.TryFromJson(json, out EndlessJourney loaded)) return loaded;
                    Debug.Log("Endless road: the saved journey was unreadable or from a newer version; starting a new one.", this);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Endless road: could not read the saved journey ({e.Message}).", this);
            }

            return EndlessJourney.New(settings.seed);
        }

        private void SaveJourney(bool flush = true)
        {
            if (journey == null || generator == null || startup && !timeRestored) return;
            journey.Seed = Seed;
            journey.S = travellerS;
            DayCycle cycle = DayCycle.Current;
            if (cycle != null)
            {
                journey.Day = cycle.Day;
                journey.Hour = cycle.Hour;
            }

            // On horseback or not (only once the mount has picked up a resumed ride, so an early save cannot drop it).
            MedievalZen.Player.HorseMount mount = MedievalZen.Player.HorseMount.Current;
            if (mount != null && mount.ResumeChecked)
            {
                journey.Mounted = mount.Mounted;
                journey.Gait = (int)mount.Gait;
            }

            journey.Clamp();
            try
            {
                PlayerPrefs.SetString(EndlessJourney.PrefsKey, journey.ToJson());
                if (flush) PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Endless road: could not save the journey ({e.Message}).", this);
            }
        }

        /// <summary>Puts the saved day and hour back (once, when a resumed journey has started).</summary>
        private void RestoreTime()
        {
            if (timeRestored) return;
            timeRestored = true;
            DayCycle cycle = DayCycle.Current;
            if (cycle == null || journey == null || !journey.HasTime) return;
            double target = (journey.Day - 1) * 24.0 + journey.Hour;
            double now = (cycle.Day - 1) * 24.0 + cycle.Hour;
            if (target > now) cycle.Clock.AddHours((float)(target - now));
            cycle.SetHourOfDay(journey.Hour);
        }

        /// <summary>Periodic bookkeeping: inns, region, the GameWorld's landmarks, the save.</summary>
        private void UpdateJourneyState()
        {
            if (Time.unscaledTime >= nextState)
            {
                nextState = Time.unscaledTime + 0.5f;
                UpdateInns(false);
                if (!startup) UpdateRegion();
            }

            UpdateNature(false);
            if (!startup && !scripted && Time.unscaledTime >= nextSave)
            {
                nextSave = Time.unscaledTime + 15f;
                SaveJourney(false);   // written to disk on quit / scene end
            }
        }
    }
}
