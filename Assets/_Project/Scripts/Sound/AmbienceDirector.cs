using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.Environment;
using MedievalZen.World;
using UnityEngine;
using Random = System.Random;

namespace MedievalZen.Sound
{
    /// <summary>
    /// The always-on ambience of the endless road (2026-10-06): the user's direction is "like Death Stranding, where the music stops and there
    /// are long periods with just ambience". Three layers, all multiplied by <see cref="SoundMix.AmbienceGain"/>:
    /// <list type="bullet">
    /// <item>Beds (2D): <see cref="AmbienceModel"/> weights per role, eased by <see cref="AmbienceSmoother"/>; each audible bed plays on two
    /// sources crossfading into itself (3 s equal power, scheduled on <see cref="AudioSettings.dspTime"/>, random start), now and then onto
    /// another clip of its role. Rain is synthesised (<see cref="AmbienceRain"/>).</item>
    /// <item>Positioned loops (3D, at most <see cref="MaxEmitters"/>): streams, lake shores, settlements and landmarks, found by
    /// <see cref="AmbienceScan"/> on a worker about once a second (a cold region never stalls a frame) and placed from absolute
    /// coordinates every frame, so the floating origin needs nothing more.</item>
    /// <item>Spot sounds: Poisson-timed birds round the listener (pigeons near places, gulls in lake country), at most two at once.</item>
    /// </list>
    /// Creates itself in Play mode on a scene with an <see cref="EndlessWorld"/> (as the people do). During a streaming hold it falls to 30 %.
    /// </summary>
    [DefaultExecutionOrder(-29)]
    public sealed class AmbienceDirector : MonoBehaviour
    {
        /// <summary>Most positioned loops at once.</summary>
        public const int MaxEmitters = 6;

        /// <summary>Most spot sounds at once.</summary>
        public const int MaxSpotVoices = 2;

        /// <summary>A bed's self-crossfade, seconds.</summary>
        public const double CrossfadeSeconds = 3.0;

        /// <summary>A bed starts above this weight (−34 dB under a full bed) and stops after this many seconds below half of it.</summary>
        private const float StartWeight = 0.02f, StopAfter = 4f;

        /// <summary>Chance that a bed's next loop is another clip of its role (variety over tens of minutes).</summary>
        private const double SwitchClipChance = 0.35;

        /// <summary>Gain during a streaming hold (black screen).</summary>
        private const float HoldGain = 0.3f;

        private sealed class BedVoice
        {
            public AmbienceRole Role;
            public readonly AudioSource[] Sources = new AudioSource[2];
            public readonly AmbiencePalette.Entry[] Entries = new AmbiencePalette.Entry[2];
            public int Active;
            public bool Playing, NextScheduled;
            public double FadeStart;
            public float ZeroFor;
            public int LastIndex = -1;
        }

        private sealed class Emitter
        {
            public AudioSource Source;
            public long Key = -1;
            public AmbiencePalette.Entry Entry;
            public AmbienceSpot Target;
            public double X, Z;
            public float Y, Envelope;
            public bool Wanted;
        }

        private sealed class SpotVoice
        {
            public AudioSource Source;
            public double X, Z;
            public float Y;
        }

        private static AmbienceDirector current;
        private static bool paletteWarned;

        private AmbiencePalette palette;
        private readonly BedVoice[] beds = new BedVoice[AmbienceCatalog.BedCount];
        private readonly AmbienceSmoother smoother = new AmbienceSmoother();
        private readonly float[] targets = new float[AmbienceCatalog.BedCount];
        private readonly Emitter[] emitters = new Emitter[MaxEmitters];
        private readonly SpotVoice[] spots = new SpotVoice[MaxSpotVoices];
        private readonly Random rng = new Random(System.Environment.TickCount);
        private AmbienceRain rain;
        private AmbienceInputs inputs;
        private ITreeDensity density;
        private float cover, nextInputs, hold = 1f;
        private float baseHeight = float.NaN;
        private bool wasHolding;
        private double lastX = double.NaN, lastZ = double.NaN;
        private Task<List<AmbienceSpot>> scan;
        private List<AmbienceSpot> found = new List<AmbienceSpot>();
        private float nextScan;
        private float nextSpot = float.NaN;
        private int lastSpotClip = -1;
        private AmbienceRole lastSpotRole;
        private string lastSpotName = string.Empty;
        private float lastSpotTime = float.NegativeInfinity;
        private AudioListener listener;
        private float nextListenerSearch;

        /// <summary>The director in this Play session, or null.</summary>
        public static AmbienceDirector Current => current;

        /// <summary>Creates the director if the scene has none (Play mode only).</summary>
        public static void Ensure()
        {
            if (current != null || !Application.isPlaying) return;
            new GameObject("Ambience").AddComponent<AmbienceDirector>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            current = null;
            paletteWarned = false;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>No scene change needed: the director is created in Play mode whenever a scene with the endless road loads.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            if (FindFirstObjectByType<EndlessWorld>() != null) Ensure();
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (FindFirstObjectByType<EndlessWorld>() != null) Ensure();
        }

        private void Awake()
        {
            if (current != null && current != this)
            {
                Destroy(gameObject);
                return;
            }

            current = this;
            transform.position = Vector3.zero;
            palette = Resources.Load<AmbiencePalette>(AmbiencePalette.ResourcePath);
            if (palette == null && !paletteWarned)
            {
                paletteWarned = true;
                Debug.LogWarning("Ambience: there is no AmbiencePalette in a Resources folder, so only the synthesised rain plays. Run MedievalZen/Sound/Build Ambience Palette " +
                                 "(it also runs by itself when the editor loads).", this);
            }

            for (int k = 0; k < AmbienceCatalog.BedCount; k++)
            {
                var voice = new BedVoice { Role = AmbienceCatalog.Beds[k] };
                if (AmbienceCatalog.HasClips(voice.Role))
                {
                    for (int n = 0; n < 2; n++) voice.Sources[n] = MakeSource($"Bed {voice.Role} {n}", false);
                }

                beds[k] = voice;
            }

            for (int k = 0; k < MaxEmitters; k++) emitters[k] = new Emitter { Source = MakeSource($"Emitter {k}", true) };
            for (int k = 0; k < MaxSpotVoices; k++) spots[k] = new SpotVoice { Source = MakeSource($"Spot {k}", true) };
            var rainObject = new GameObject("Rain");
            rainObject.transform.SetParent(transform, false);
            rainObject.AddComponent<AudioSource>();
            rain = rainObject.AddComponent<AmbienceRain>();
            SoundMix.AmbienceStatus = StatusLine;
        }

        private void OnDestroy()
        {
            if (current != this) return;
            current = null;
            SoundMix.AmbienceStatus = null;
        }

        private AudioSource MakeSource(string name, bool positioned)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = positioned;
            s.dopplerLevel = 0f;   // the floating origin teleports everything
            s.spatialBlend = positioned ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.priority = positioned ? 96 : 64;
            s.volume = 0f;
            go.AddComponent<SoundBoost>();   // several loops are quieter than their target: volume alone stops at 1
            return s;
        }

        // ------------------------------------------------------------------ frame

        private static readonly int FrameCostId = Review.FrameCost.Register("AmbienceDirector");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            EndlessWorld endless = EndlessWorld.Current;
            Vector3 ear = ListenerPosition();
            (double ax, double az) = endless != null ? endless.ToAbsolute(ear) : (ear.x, ear.z);
            if (!double.IsNaN(lastX) && (ax - lastX) * (ax - lastX) + (az - lastZ) * (az - lastZ) > 60.0 * 60.0)
            {
                // A teleport (a fast journey, a review stop): follow quickly and look round at once.
                smoother.Hurry();
                nextScan = 0f;
                nextInputs = 0f;
            }

            lastX = ax;
            lastZ = az;
            bool holding = endless != null && endless.IsHolding;
            if (wasHolding && !holding) smoother.Hurry();
            wasHolding = holding;
            hold += ((holding ? HoldGain : 1f) - hold) * (1f - Mathf.Exp(-dt / 0.8f));
            float bus = SoundMix.AmbienceGain * hold;

            if (Time.unscaledTime >= nextInputs)
            {
                nextInputs = Time.unscaledTime + 0.25f;
                GatherInputs(endless, ear);
                AmbienceModel.Targets(inputs, targets);
            }

            smoother.Step(targets, dt);
            UpdateBeds(dt, bus);
            UpdateEmitters(dt, bus, endless, ax, az, ear);
            UpdateSpots(bus, endless, ax, az, ear);
        }

        private Vector3 ListenerPosition()
        {
            if ((listener == null || !listener.isActiveAndEnabled) && Time.unscaledTime >= nextListenerSearch)
            {
                nextListenerSearch = Time.unscaledTime + 1f;
                listener = FindFirstObjectByType<AudioListener>();
            }

            if (listener != null) return listener.transform.position;
            return Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        }

        private void GatherInputs(EndlessWorld endless, Vector3 ear)
        {
            RegionKind region = endless != null && !string.IsNullOrEmpty(endless.RegionName) ? endless.CurrentRegion.Kind : RegionKind.Meadow;
            GameWorld world = GameWorld.Current;
            float ground = float.NaN;
            if (world != null && world.Surface != null && world.Surface.HasGround)
            {
                if (density == null) density = world.Surface.CreateTreeDensity(16f);
                cover = ForestMood.CanopyFor(ForestMood.MeanDensity(density.Sample, ear.x, ear.z));
                ground = world.Surface.GroundHeight(ear);
            }

            if (float.IsNaN(baseHeight) && endless != null && endless.Generator != null) baseHeight = (float)endless.Generator.Road(0.0).Height;
            DayCycle day = DayCycle.Current;
            WeatherPreset w = WeatherSystem.CurrentWeather;
            float hour = day != null ? day.Hour : 12f;
            inputs = new AmbienceInputs
            {
                Region = region,
                Cover = cover,
                Exposure = AmbienceModel.Exposure(region, ground, float.IsNaN(baseHeight) ? 0f : baseHeight, cover),
                SunElevation = day != null ? day.SunElevation : 40f,
                Morning = hour < 12f,
                Wind = w.wind,
                Gust = WeatherSystem.Current != null ? WeatherSystem.Current.Gust : 0.3f,
                Rain = w.rain,
                Mist = w.mistAmount,
            };
        }

        // ------------------------------------------------------------------ beds

        private void UpdateBeds(float dt, float bus)
        {
            float[] weights = smoother.Weights;
            double now = AudioSettings.dspTime;
            for (int k = 0; k < AmbienceCatalog.BedCount; k++)
            {
                BedVoice v = beds[k];
                float weight = weights[k];
                if (v.Role == AmbienceRole.Rain)
                {
                    rain.Level = weight * bus;
                    rain.Density = inputs.Rain;
                    continue;
                }

                List<AmbiencePalette.Entry> clips = palette != null ? palette.For(v.Role) : null;
                if (clips == null || clips.Count == 0) continue;
                if (!v.Playing)
                {
                    if (weight < StartWeight) continue;
                    StartBed(v, clips, now);
                }

                v.ZeroFor = weight < StartWeight * 0.5f ? v.ZeroFor + dt : 0f;
                if (v.ZeroFor > StopAfter)
                {
                    StopBed(v);
                    continue;
                }

                AdvanceBed(v, clips, now);
                float fadeOut = 1f, fadeIn = 0f;
                if (v.NextScheduled && now >= v.FadeStart) AmbienceModel.EqualPower((float)((now - v.FadeStart) / CrossfadeSeconds), out fadeOut, out fadeIn);
                SetBedVolume(v, v.Active, fadeOut * weight * bus);
                SetBedVolume(v, 1 - v.Active, v.NextScheduled ? fadeIn * weight * bus : 0f);
            }
        }

        private static void SetBedVolume(BedVoice v, int n, float gain)
        {
            AudioSource s = v.Sources[n];
            AmbiencePalette.Entry e = v.Entries[n];
            if (s == null) return;
            SoundBoost.Apply(s, e == null ? 0f : gain * e.Gain);
        }

        private void StartBed(BedVoice v, List<AmbiencePalette.Entry> clips, double now)
        {
            int pick = rng.Next(clips.Count);
            AmbiencePalette.Entry e = clips[pick];
            v.LastIndex = pick;
            v.Active = 0;
            AudioSource s = v.Sources[0];
            s.clip = e.clip;
            v.Entries[0] = e;
            float length = e.clip.length;
            float offset = length > 10f ? (float)(rng.NextDouble() * (length - 8f)) : 0f;
            s.time = offset;
            s.volume = 0f;
            s.Play();
            v.FadeStart = now + (length - offset) - CrossfadeSeconds;
            v.NextScheduled = false;
            v.Playing = true;
            v.ZeroFor = 0f;
        }

        private void AdvanceBed(BedVoice v, List<AmbiencePalette.Entry> clips, double now)
        {
            if (!v.NextScheduled && now >= v.FadeStart - 1.0)
            {
                // Schedule the next loop to start exactly when this one begins to fade.
                int pick = v.LastIndex >= 0 && v.LastIndex < clips.Count && rng.NextDouble() >= SwitchClipChance ? v.LastIndex : AmbienceModel.PickOther(clips.Count, v.LastIndex, rng);
                v.LastIndex = pick;
                int next = 1 - v.Active;
                AudioSource s = v.Sources[next];
                v.Entries[next] = clips[pick];
                s.Stop();
                s.clip = clips[pick].clip;
                s.time = 0f;
                s.volume = 0f;
                s.PlayScheduled(Math.Max(v.FadeStart, now + 0.05));
                v.FadeStart = Math.Max(v.FadeStart, now + 0.05);
                v.NextScheduled = true;
            }
            else if (v.NextScheduled && now >= v.FadeStart + CrossfadeSeconds)
            {
                v.Sources[v.Active].Stop();
                v.Active = 1 - v.Active;
                AmbiencePalette.Entry e = v.Entries[v.Active];
                v.FadeStart = v.FadeStart + (e != null && e.clip != null ? e.clip.length : 60f) - CrossfadeSeconds;
                v.NextScheduled = false;
            }
        }

        private static void StopBed(BedVoice v)
        {
            foreach (AudioSource s in v.Sources)
            {
                if (s == null) continue;
                s.Stop();
                s.volume = 0f;
            }

            v.Playing = false;
            v.NextScheduled = false;
            v.ZeroFor = 0f;
        }

        // ------------------------------------------------------------------ positioned loops

        private void UpdateEmitters(float dt, float bus, EndlessWorld endless, double ax, double az, Vector3 ear)
        {
            if (endless == null || !(endless.Generator is EndlessGenerator g)) return;
            if (scan != null && scan.IsCompleted)
            {
                if (scan.Status == TaskStatus.RanToCompletion && scan.Result != null) found = scan.Result;
                else if (scan.IsFaulted) Debug.LogWarning($"Ambience: the emitter scan failed: {scan.Exception?.GetBaseException().Message}");
                scan = null;
            }

            if (scan == null && Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1f;
                double x = ax, z = az, s = endless.S;
                scan = Task.Run(() =>
                {
                    var list = new List<AmbienceSpot>();
                    AmbienceScan.Around(g, x, z, s, list);
                    return list;
                });
            }

            float hour = DayCycle.Current != null ? DayCycle.Current.Hour : 12f;
            foreach (Emitter e in emitters) e.Wanted = false;
            int used = 0;
            foreach (AmbienceSpot spot in found)
            {
                if (used >= MaxEmitters) break;
                if (DayGain(spot.Role, hour) <= 0f) continue;
                Emitter e = FindEmitter(spot.Key);
                if (e == null)
                {
                    e = FreeEmitter();
                    if (e == null) break;
                    List<AmbiencePalette.Entry> clips = palette != null ? palette.For(spot.Role) : null;
                    if (clips == null || clips.Count == 0) continue;
                    e.Key = spot.Key;
                    e.Entry = clips[(int)((ulong)spot.Key % (ulong)clips.Count)];   // the same place keeps its clip
                    e.X = spot.X;
                    e.Z = spot.Z;
                    e.Y = spot.Y;
                    e.Envelope = 0f;
                    e.Source.clip = e.Entry.clip;
                    e.Source.time = (float)(rng.NextDouble() * Math.Max(0.0, e.Entry.clip.length - 2.0));
                    e.Source.volume = 0f;
                    e.Source.Play();
                }

                e.Target = spot;
                e.Wanted = true;
                used++;
            }

            float follow = 1f - Mathf.Exp(-dt / 0.7f);
            foreach (Emitter e in emitters)
            {
                if (e.Key < 0) continue;
                e.Envelope = Mathf.MoveTowards(e.Envelope, e.Wanted ? 1f : 0f, dt / 2f);
                if (!e.Wanted && e.Envelope <= 0f)
                {
                    e.Source.Stop();
                    e.Source.volume = 0f;
                    e.Key = -1;
                    e.Entry = null;
                    continue;
                }

                // Slide along the water as the traveller walks.
                e.X += (e.Target.X - e.X) * follow;
                e.Z += (e.Target.Z - e.Z) * follow;
                e.Y += (e.Target.Y - e.Y) * follow;
                Vector3 at = endless.ToScene(e.X, e.Y, e.Z);
                e.Source.transform.position = at;
                float d = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(ear.x, ear.z));
                bool water = e.Target.Role == AmbienceRole.Stream || e.Target.Role == AmbienceRole.Lake;
                e.Source.spread = water ? AmbienceModel.SpreadAt(d) : 0f;
                e.Source.minDistance = Mathf.Max(1f, e.Target.Reference);
                e.Source.maxDistance = e.Target.Reach * 3f;
                float edge = AmbienceModel.SmoothStep(e.Target.Reach + 10f, e.Target.Reach * 0.75f, d);
                SoundBoost.Apply(e.Source, e.Entry.Gain * bus * edge * e.Envelope * DayGain(e.Target.Role, hour));
            }
        }

        private float DayGain(AmbienceRole role, float hour)
        {
            switch (role)
            {
                case AmbienceRole.Market:
                case AmbienceRole.InnYard:
                    return AmbienceModel.PeopleDayGain(hour) * (1f - 0.5f * inputs.Rain);
                case AmbienceRole.FarmAnimals:
                    return AmbienceModel.FarmDayGain(inputs.SunElevation);
                default:
                    return 1f;
            }
        }

        private Emitter FindEmitter(long key)
        {
            foreach (Emitter e in emitters)
            {
                if (e.Key == key) return e;
            }

            return null;
        }

        private Emitter FreeEmitter()
        {
            foreach (Emitter e in emitters)
            {
                if (e.Key < 0) return e;
            }

            return null;
        }

        // ------------------------------------------------------------------ spot sounds

        private void UpdateSpots(float bus, EndlessWorld endless, double ax, double az, Vector3 ear)
        {
            float now = Time.unscaledTime;
            float birds = AmbienceModel.BirdGain(inputs);
            float u = Mathf.PerlinNoise(now * 0.003f, 0.71f);
            float mean = AmbienceModel.SpotMeanGap(inputs.Cover, birds, inputs.Rain, u);
            if (float.IsNaN(nextSpot) || float.IsInfinity(nextSpot)) nextSpot = now + AmbienceModel.PoissonGap(mean, rng.NextDouble());
            foreach (SpotVoice v in spots)
            {
                if (!v.Source.isPlaying) continue;
                v.Source.transform.position = endless != null ? endless.ToScene(v.X, v.Y, v.Z) : new Vector3((float)v.X, v.Y, (float)v.Z);
            }

            if (now < nextSpot) return;
            nextSpot = now + AmbienceModel.PoissonGap(mean, rng.NextDouble());
            if (palette == null || float.IsInfinity(mean) || hold < 0.9f) return;
            SpotVoice voice = null;
            foreach (SpotVoice v in spots)
            {
                if (!v.Source.isPlaying)
                {
                    voice = v;
                    break;
                }
            }

            if (voice == null) return;
            AmbienceRole role = AmbienceRole.SpotBirds;
            if (inputs.Region == RegionKind.LakeCountry && rng.NextDouble() < 0.12) role = AmbienceRole.SpotGull;
            else if (NearSettlement() && rng.NextDouble() < 0.35) role = AmbienceRole.SpotPigeon;
            List<AmbiencePalette.Entry> clips = palette.For(role);
            if (clips.Count == 0) return;
            int pick = AmbienceModel.PickOther(clips.Count, role == lastSpotRole ? lastSpotClip : -1, rng);
            AmbiencePalette.Entry e = clips[pick];
            lastSpotRole = role;
            lastSpotClip = pick;
            lastSpotName = e.Name;
            lastSpotTime = now;

            double angle = rng.NextDouble() * Math.PI * 2.0, dist = 15.0 + 25.0 * rng.NextDouble();
            voice.X = ax + Math.Cos(angle) * dist;
            voice.Z = az + Math.Sin(angle) * dist;
            float ground = ear.y - 1.6f;
            voice.Y = ground + 4f + 8f * (float)rng.NextDouble();
            voice.Source.transform.position = endless != null ? endless.ToScene(voice.X, voice.Y, voice.Z) : new Vector3((float)voice.X, voice.Y, (float)voice.Z);
            voice.Source.loop = false;
            voice.Source.minDistance = 12f;
            voice.Source.maxDistance = 120f;
            voice.Source.spread = 0f;
            voice.Source.clip = e.clip;
            voice.Source.pitch = 1f + 0.04f * (float)(rng.NextDouble() * 2.0 - 1.0);
            float level = SoundMix.DbToGain(3f * (float)(rng.NextDouble() * 2.0 - 1.0));
            SoundBoost.Apply(voice.Source, e.Gain * level * bus * Mathf.Min(1f, birds));
            voice.Source.Play();
        }

        private bool NearSettlement()
        {
            foreach (AmbienceSpot s in found)
            {
                if ((s.Role == AmbienceRole.InnYard || s.Role == AmbienceRole.Market || s.Role == AmbienceRole.FarmAnimals) && s.Distance < 120f) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ overlay

        private readonly StringBuilder status = new StringBuilder(160);

        private string StatusLine()
        {
            status.Clear();
            status.Append("Ambience:");
            float[] w = smoother.Weights;
            int shown = 0;
            for (int k = 0; k < AmbienceCatalog.BedCount; k++)
            {
                if (w[k] < 0.03f) continue;
                status.Append(shown++ == 0 ? " " : ", ").Append(AmbienceCatalog.Beds[k]).Append(' ').Append(SoundMix.GainToDb(w[k]).ToString("0"));
            }

            if (shown == 0) status.Append(" silent");
            status.Append(" | ");
            int n = 0;
            foreach (Emitter e in emitters)
            {
                if (e.Key < 0) continue;
                status.Append(n++ == 0 ? string.Empty : ", ").Append(e.Target.Role).Append(' ').Append(e.Target.Distance.ToString("0")).Append('m');
            }

            if (n == 0) status.Append("no emitters");
            if (!float.IsNegativeInfinity(lastSpotTime)) status.Append(" | ").Append(lastSpotName).Append(' ').Append((Time.unscaledTime - lastSpotTime).ToString("0")).Append("s ago");
            if (palette == null) status.Append(" | NO PALETTE");
            return status.ToString();
        }
    }
}
