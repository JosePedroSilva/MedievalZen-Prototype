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
                SettleOnGround();
                ApplyFreeze();
                if (hud != null && holdFaded) hud.Fade(0f, HoldFadeInSeconds);
                holdFaded = false;
            }
        }

        /// <summary>Near tiles all built, far tiles within the start radius built (or the timeout passed), and the road window ready.</summary>
        private bool StartReady() => window != null && AreaReady(startupBegan);

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
                if (w.Lod == TileLod.Far && w.Distance <= startFarRadius && !timedOut) return false;
            }

            return GroundReadyAt(traveller.position);   // never released over missing ground
        }

        private void FinishStart()
        {
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

        // ------------------------------------------------------------------ Go now

        /// <summary>
        /// Takes the traveller to <paramref name="inn"/> along the road behind a fade: they arrive on the road just before it
        /// (coming from where they were), facing it; the tiles there are built before the fade lifts. False when busy.
        /// </summary>
        public bool TravelTo(Waypoint inn, Action done = null)
        {
            if (inn == null || scripted || startup || generator == null) return false;
            StartCoroutine(TravelRoutine(inn, done));
            return true;
        }

        private IEnumerator TravelRoutine(Waypoint inn, Action done)
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
            int direction = inn.S >= travellerS ? 1 : -1;
            double target = inn.S - direction * (RoadWindow.RadiusFor(inn) + 6.0);
            MoveTravellerTo(target, direction);
            float began = Time.unscaledTime;
            yield return new WaitForSecondsRealtime(0.6f);
            // As at the start: the near ground, then the nearer silhouettes (or a timeout), so the view opens complete.
            while (!AreaReady(began) && Time.unscaledTime - began < 40f) yield return null;

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
                hud.ShowToast($"The road brings you to {inn.Name}");
                yield return hud.Fade(0f, TravelFadeInSeconds);
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
