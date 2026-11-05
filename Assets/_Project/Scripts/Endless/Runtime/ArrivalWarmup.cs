using System.Collections.Generic;
using System.Diagnostics;
using MedievalZen.Environment;
using MedievalZen.People;
using MedievalZen.Sound;
using MedievalZen.Wildlife;
using UnityEngine;
using UnityEngine.UI;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The arrival warm-up (2026-10-09). The worst frames of the walk benchmarks were at places: things made or used for the first
    /// time when the traveller first comes near one (an effect prefab and its shader, a character's meshes and Animator controller,
    /// a farm animal, chimney smoke, new letters in the shared font atlas). Behind the startup hold's black screen (and any later
    /// black hold until it is done), once per session, a few per frame within <see cref="ArrivalWarmupRules.BudgetMs"/>:
    /// <list type="bullet">
    /// <item>every ambient effect (<see cref="AmbientFxSpawner.WarmStep"/>: made, shown one frame, parked in its pool);</item>
    /// <item>every palette character (<see cref="PeopleManager.WarmStep"/>: made, animated one frame, parked in the people's pool);</item>
    /// <item>every animal and horse of the wildlife palette, the chimney smoke, the campfire and the mist sheet (made, shown one
    ///   frame in front of the camera, destroyed: their assets stay loaded and their shaders have met the GPU);</item>
    /// <item>every ambience and animal clip's audio data (<see cref="AudioClip.LoadAudioData"/>; the music keeps its own loading);</item>
    /// <item>the glyphs of every text size and style in the scene and of the inn boards.</item>
    /// </list>
    /// Nothing here is random or holds a world position across frames (a shown instance lives one frame), so it is deterministic and
    /// origin-shift safe. Owned and stepped by <see cref="EndlessWorld"/>.
    /// </summary>
    internal sealed class ArrivalWarmup
    {
        private enum Lane
        {
            Glyphs,
            Audio,
            Effects,
            People,
            Prefabs,
        }

        private static readonly Lane[] Lanes = { Lane.Glyphs, Lane.Audio, Lane.Effects, Lane.People, Lane.Prefabs };

        private readonly bool[] laneDone = new bool[Lanes.Length];
        private readonly List<GameObject> shown = new List<GameObject>(4);
        private readonly List<int> sizes = new List<int>(3);
        private readonly Stopwatch clock = new Stopwatch();
        private List<AudioClip> clips;
        private List<GameObject> prefabs;
        private int clipNext, prefabNext;
        private Transform holder;
        private AmbientFxSpawner fx;
        private bool fxLooked;

        public bool Pending { get; private set; } = true;
        public float TotalMs { get; private set; }
        public float WorstFrameMs { get; private set; }
        public int Frames { get; private set; }
        public int Effects { get; private set; }
        public int People { get; private set; }
        public int Bodies { get; private set; }
        public int ClipsRequested { get; private set; }
        public int ClipsTotal => clips != null ? clips.Count : 0;
        public int GlyphSets { get; private set; }

        /// <summary>
        /// One frame of the warm-up behind a black screen: what was shown last frame is put away, then each lane takes its next item
        /// (instant lanes several) while the frame's budget lasts. <paramref name="at"/> is the stage in front of the camera.
        /// </summary>
        public void Step(EndlessWorld world, Vector3 at)
        {
            if (!Pending) return;
            clock.Restart();
            ClearShown();
            int items = 0;
            for (int l = 0; l < Lanes.Length; l++)
            {
                if (laneDone[l]) continue;
                if (!ArrivalWarmupRules.MayContinue((float)clock.Elapsed.TotalMilliseconds, items)) break;
                switch (Lanes[l])
                {
                    case Lane.Glyphs:
                        WarmGlyphs();
                        laneDone[l] = true;
                        items++;
                        break;
                    case Lane.Audio:
                        laneDone[l] = StepAudio(ref items);
                        break;
                    case Lane.Effects:
                        laneDone[l] = StepEffects(at, ref items);
                        break;
                    case Lane.People:
                        laneDone[l] = StepPeople(at, ref items);
                        break;
                    case Lane.Prefabs:
                        laneDone[l] = StepPrefabs(world, at, ref items);
                        break;
                }
            }

            float ms = (float)clock.Elapsed.TotalMilliseconds;
            TotalMs += ms;
            WorstFrameMs = Mathf.Max(WorstFrameMs, ms);
            Frames++;
            bool all = true;
            for (int l = 0; l < laneDone.Length; l++) all &= laneDone[l];
            if (!all && Frames < ArrivalWarmupRules.MaxFrames) return;
            if (!all)
            {
                // Something never became ready: give up rather than lengthen every later hold.
                ClearShown();
                if (fx != null) fx.EndWarm();
                if (PeopleManager.Current != null) PeopleManager.Current.EndWarm();
                fxStaged = peopleStaged = false;
                Pending = false;
                UnityEngine.Debug.LogWarning(Report($"stopped after {ArrivalWarmupRules.MaxFrames} frames with work left"));
                return;
            }

            if (shown.Count > 0) return;   // the last bodies are put away next frame
            Pending = false;
            UnityEngine.Debug.Log(Report("done"));
        }

        /// <summary>The hold ended: what is shown is put away now (the rest waits for the next black hold).</summary>
        public void EndHold()
        {
            if (shown.Count == 0 && !fxStaged && !peopleStaged) return;
            ClearShown();
            if (fx != null) fx.EndWarm();
            if (PeopleManager.Current != null) PeopleManager.Current.EndWarm();
            fxStaged = peopleStaged = false;
            if (Pending && Frames > 0) UnityEngine.Debug.Log(Report("cut short by the end of the hold"));
        }

        /// <summary>The Editor.log line: what was warmed and what it cost.</summary>
        public string Report(string how) =>
            $"Endless road arrival warm-up {how}: {TotalMs:0.0} ms over {Frames} frames (worst {WorstFrameMs:0.0} ms); effects {Effects}" +
            $"{(fx != null ? "/" + fx.WarmCount : string.Empty)}, people {People}/{(PeopleManager.Current != null ? PeopleManager.Current.WarmCount : 0)}, " +
            $"bodies {Bodies}/{(prefabs != null ? prefabs.Count : 0)} (animals, horses, smoke, fire, mist), clips asked to load {ClipsRequested}/{ClipsTotal}, " +
            $"glyph sets {GlyphSets}.";

        // ------------------------------------------------------------------ lanes

        private bool fxStaged, peopleStaged;

        private bool StepEffects(Vector3 at, ref int items)
        {
            if (!fxLooked)
            {
                fxLooked = true;
                fx = Object.FindFirstObjectByType<AmbientFxSpawner>();
            }

            if (fx == null || !fx.isActiveAndEnabled) return true;
            if (!fx.WarmReady) return false;   // its Start has not run yet: next frame
            if (!fx.WarmPending) return true;
            items++;
            fxStaged = fx.WarmStep(at);
            if (fxStaged) Effects++;
            return !fx.WarmPending;
        }

        private bool StepPeople(Vector3 at, ref int items)
        {
            PeopleManager people = PeopleManager.Current;
            if (people == null || !people.WarmPending) return true;
            items++;
            peopleStaged = people.WarmStep(at);
            if (peopleStaged) People++;
            return !people.WarmPending;
        }

        private bool StepAudio(ref int items)
        {
            if (clips == null)
            {
                clips = new List<AudioClip>(256);
                var seen = new HashSet<AudioClip>();
                AmbiencePalette ambience = Resources.Load<AmbiencePalette>(AmbiencePalette.ResourcePath);
                if (ambience != null)
                {
                    foreach (AmbiencePalette.Entry e in ambience.Entries)
                    {
                        if (e != null && e.clip != null && seen.Add(e.clip)) clips.Add(e.clip);
                    }
                }

                WildlifePalette wildlife = Resources.Load<WildlifePalette>(WildlifePalette.ResourcePath);
                if (wildlife != null)
                {
                    foreach (AudioClip c in wildlife.Clips)
                    {
                        if (c != null && seen.Add(c)) clips.Add(c);
                    }
                }
            }

            int instant = 0;
            while (clipNext < clips.Count && instant < ArrivalWarmupRules.InstantPerFrame &&
                   ArrivalWarmupRules.MayContinue((float)clock.Elapsed.TotalMilliseconds, items))
            {
                AudioClip clip = clips[clipNext++];
                if (clip == null || clip.loadState != AudioDataLoadState.Unloaded) continue;
                // A clip set to load in the background returns at once; the others were preloaded with their palette.
                clip.LoadAudioData();
                ClipsRequested++;
                instant++;
                items++;
            }

            return clipNext >= clips.Count;
        }

        private bool StepPrefabs(EndlessWorld world, Vector3 at, ref int items)
        {
            if (prefabs == null)
            {
                prefabs = new List<GameObject>(64);
                void Add(GameObject g)
                {
                    if (g != null && !prefabs.Contains(g)) prefabs.Add(g);
                }

                Add(world.SmokePrefab);
                Add(world.FirePrefab);
                MistField mist = Object.FindFirstObjectByType<MistField>();
                if (mist != null) Add(mist.MistPrefab);
                WildlifePalette wildlife = Resources.Load<WildlifePalette>(WildlifePalette.ResourcePath);
                if (wildlife != null)
                {
                    foreach (WildlifePalette.Entry e in wildlife.Entries)
                    {
                        if (e != null) Add(e.prefab);
                    }
                }
            }

            // One body per frame: it must be drawn once before it goes.
            if (prefabNext >= prefabs.Count) return true;
            GameObject prefab = prefabs[prefabNext++];
            if (holder == null)
            {
                holder = new GameObject("ArrivalWarmup").transform;
            }

            items++;
            Review.FrameCost.FirstUse("Instantiate " + prefab.name);
            GameObject go = Object.Instantiate(prefab, at, Quaternion.identity, holder);
            go.name = prefab.name + " (warm-up)";
            foreach (AudioSource s in go.GetComponentsInChildren<AudioSource>(true)) s.Stop();   // a pack's play-on-awake call stays silent
            shown.Add(go);
            Bodies++;
            return prefabNext >= prefabs.Count;
        }

        private void WarmGlyphs()
        {
            SettlementLife.WarmGlyphs(ArrivalWarmupRules.Glyphs);
            GlyphSets++;
            var done = new HashSet<(Font, int, FontStyle)>();
            foreach (Text text in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (text == null || text.font == null || !text.font.dynamic) continue;
                Canvas canvas = text.canvas;
                ArrivalWarmupRules.Sizes(text.fontSize, canvas != null ? canvas.scaleFactor : 1f, sizes);
                foreach (int size in sizes)
                {
                    if (!done.Add((text.font, size, text.fontStyle))) continue;
                    text.font.RequestCharactersInTexture(ArrivalWarmupRules.Glyphs, size, text.fontStyle);
                    GlyphSets++;
                }
            }
        }

        private void ClearShown()
        {
            for (int i = 0; i < shown.Count; i++)
            {
                if (shown[i] != null) Object.Destroy(shown[i]);
            }

            shown.Clear();
        }
    }
}
