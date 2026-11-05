using System;
using System.Collections.Generic;
using MedievalZen.Cinematic;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.Environment;
using MedievalZen.Player;
using MedievalZen.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedievalZen.Sound
{
    /// <summary>
    /// Plays the music the <see cref="MusicDirector"/> decides on (2026-10-06): created in Play mode on scenes with an
    /// <see cref="EndlessWorld"/>, it gathers the context a few times a second (road, places, sky, weather, seat), ticks the director
    /// and the moments, and plays the pieces on 2D AudioSources scheduled sample-accurately on the DSP clock: each iteration of a
    /// piece starts the aligned mixes it may use together, at most two of them audible, and the next iteration starts over the last
    /// one's tail. Volume = trim × the piece's gain × <see cref="SoundMix.MusicGain"/>. Clips are loaded when the director arms and
    /// unloaded once their piece is over. F7 starts a piece now or ends the playing one gracefully; F8 crossfades to the next track.
    /// </summary>
    [DefaultExecutionOrder(-28)]
    public sealed class MusicPlayer : MonoBehaviour
    {
        /// <summary>The director ticks this often (seconds).</summary>
        public const float TickInterval = 0.25f;
        /// <summary>Sources are scheduled at least this far ahead of the DSP clock (seconds), and at most this far.</summary>
        public const double ScheduleLead = 0.5, ScheduleWindow = 2.0;
        /// <summary>Chance that a cinematic travel shot becomes the wide vista while a piece plays.</summary>
        public const float WideShotBias = 0.5f;

        private sealed class Voice
        {
            public AudioSource Source;
            public int Iteration;
            public MusicVariant Variant;
        }

        private sealed class Running
        {
            public MusicPiece Piece;
            public MusicLibrary.Track Track;
            public double DspStart = double.NaN;
            public int NextIteration;
            public readonly List<Voice> Voices = new List<Voice>();
            public bool Done;
        }

        public static MusicPlayer Current { get; private set; }

        private MusicLibrary library;
        private MusicDirector director;
        private readonly MusicMoments moments = new MusicMoments();
        private MusicRoadProbe probe;
        private EndlessGenerator probeGenerator;
        private MusicTitleCard card;
        private PlayerSeat seat;
        private float nextSeatSearch;
        private readonly List<Running> running = new List<Running>();
        private readonly Stack<AudioSource> pool = new Stack<AudioSource>();
        private readonly HashSet<int> loaded = new HashSet<int>();
        private MusicTickInput lastInput;
        private MusicMomentContext context;
        private float tickTimer;
        private double lastS = double.NaN;
        private float speed;
        private static bool missingLogged;

        /// <summary>Creates the player if the scene has none (play mode, endless road only).</summary>
        public static void Ensure()
        {
            if (Current != null || !Application.isPlaying) return;
            new GameObject("Music").AddComponent<MusicPlayer>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            missingLogged = false;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

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
            if (Current != null && Current != this)
            {
                Destroy(gameObject);
                return;
            }

            Current = this;
            library = MusicLibrary.Load();
            if (library == null || library.tracks == null || library.tracks.Count == 0)
            {
                if (!missingLogged) Debug.LogWarning("Music: no MusicLibrary in Resources; run MedievalZen/Sound/Build Music Library. Ambience only.");
                missingLogged = true;
                enabled = false;
                return;
            }

            director = new MusicDirector(library.DirectorTracks(), System.Environment.TickCount);
            card = MusicTitleCard.Create(transform);
            SoundMix.MusicStatus = Status;
            int playable = 0;
            foreach (MusicLibrary.Track t in library.tracks) if (t.enabled && t.Complete) playable++;
            Debug.Log($"Music: {playable}/{library.tracks.Count} tracks, frequency {SoundMix.Frequency}. F7 music now / end, F8 next track.", this);
        }

        private void OnDestroy()
        {
            if (Current != this) return;
            Current = null;
            SoundMix.MusicPresence = 0f;
            CinematicDirector.MusicWideBias = 0f;
            if (SoundMix.MusicStatus == (Func<string>)Status) SoundMix.MusicStatus = null;
            if (library != null) foreach (int k in loaded) SetLoaded(library.tracks[k], false);
            loaded.Clear();
        }

        private string Status() => director != null ? director.Status() : null;

        private static readonly int FrameCostId = Review.FrameCost.Register("MusicPlayer");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            if (director == null) return;
            float dt = Time.unscaledDeltaTime;
            Gather(dt);
            tickTimer += dt;
            if (tickTimer >= TickInterval)
            {
                lastInput = Input(tickTimer);
                tickTimer = 0f;
                director.Tick(lastInput);
            }

            Keys();
            Load();
            Play();
        }

        // ------------------------------------------------------------------ context

        private void Gather(float dt)
        {
            EndlessWorld world = EndlessWorld.Current;
            if (world == null) return;
            double s = world.S;
            if (!double.IsNaN(lastS) && dt > 0f)
            {
                float v = (float)(Math.Abs(s - lastS) / dt);
                if (v > 30f) v = speed;   // a jump (new journey, travel to an inn): keep the last speed
                speed = Mathf.Lerp(speed, v, 1f - Mathf.Exp(-dt / 2f));
            }

            lastS = s;
        }

        private MusicTickInput Input(float dt)
        {
            EndlessWorld world = EndlessWorld.Current;
            context.Time = director.Clock + dt;
            context.Speed = speed;
            if (world != null && world.Generator is EndlessGenerator generator)
            {
                if (generator != probeGenerator)
                {
                    probeGenerator = generator;
                    probe = new MusicRoadProbe(generator);
                }

                probe.Fill(world.S, ref context);
            }
            else
            {
                context.PlaceAhead = double.NaN;
                context.PlaceBehind = double.PositiveInfinity;
            }

            DayCycle day = DayCycle.Current;
            context.SunElevation = day != null ? day.SunElevation : 30f;
            WeatherSystem weather = WeatherSystem.Current;
            context.Wet = weather != null && (weather.State == WeatherState.LightRain || weather.State == WeatherState.Misty || weather.Weather.rain > 0.2f);
            if (seat == null && Time.unscaledTime >= nextSeatSearch)
            {
                seat = FindFirstObjectByType<PlayerSeat>();
                nextSeatSearch = Time.unscaledTime + 5f;
            }

            context.Seated = seat != null && seat.IsSeated;
            context.SeatedSeconds = context.Seated ? seat.SeatedTime : 0f;
            moments.Update(context);
            int wanted = director.WantedTrack;
            return new MusicTickInput
            {
                Dt = dt,
                Frequency = SoundMix.Frequency,
                Capture = SoundMix.CaptureRunning,
                Holding = world == null || world.IsHolding,
                Speed = speed,
                PlaceAhead = context.PlaceAhead,
                Seated = context.Seated,
                SeatedSeconds = context.SeatedSeconds,
                Mood = MusicMoments.MoodOf(context),
                Moment = moments.Best,
                Strength = moments.Strength,
                TrackReady = wanted >= 0 && IsLoaded(library.tracks[wanted]),
            };
        }

        private void Keys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            HudController hud = HudController.Instance;
            if (keyboard.f7Key.wasPressedThisFrame)
            {
                if (director.Piece != null)
                {
                    if (director.RequestEnd()) hud?.ShowToast("Music: ending");
                }
                else if (SoundMix.Frequency == MusicFrequency.Off) hud?.ShowToast("Music is off (Journey menu)");
                else if (director.RequestNow(lastInput)) hud?.ShowToast("Music: " + director.Piece.Title);
            }

            if (keyboard.f8Key.wasPressedThisFrame)
            {
                if (SoundMix.Frequency == MusicFrequency.Off) hud?.ShowToast("Music is off (Journey menu)");
                else if (director.NextTrack(lastInput)) hud?.ShowToast($"Music: {director.Piece.Track + 1}/{library.tracks.Count} {director.Piece.Title}");
            }
        }

        // ------------------------------------------------------------------ loading

        private void Load()
        {
            int wanted = director.WantedTrack;
            if (wanted >= 0 && loaded.Add(wanted)) SetLoaded(library.tracks[wanted], true);
            if (loaded.Count <= 1) return;
            List<int> drop = null;
            foreach (int k in loaded)
            {
                if (k == wanted || Sounding(k)) continue;
                (drop ??= new List<int>()).Add(k);
            }

            if (drop == null) return;
            foreach (int k in drop)
            {
                loaded.Remove(k);
                SetLoaded(library.tracks[k], false);
            }
        }

        private bool Sounding(int track)
        {
            foreach (Running r in running)
                if (r.Piece.Track == track && !r.Done) return true;
            return false;
        }

        private static void SetLoaded(MusicLibrary.Track t, bool load)
        {
            for (MusicVariant v = MusicVariant.Intensity1; v <= MusicVariant.Cut60; v++)
            {
                AudioClip clip = t.Clip(v);
                if (clip == null) continue;
                if (load) clip.LoadAudioData();
                else clip.UnloadAudioData();
            }
        }

        private static bool IsLoaded(MusicLibrary.Track t)
        {
            if (!t.Complete) return false;
            for (MusicVariant v = MusicVariant.Intensity1; v <= MusicVariant.Cut60; v++)
                if (t.Clip(v).loadState != AudioDataLoadState.Loaded) return false;
            return true;
        }

        // ------------------------------------------------------------------ playing

        private void Play()
        {
            double dsp = AudioSettings.dspTime;
            Adopt(director.Piece);
            Adopt(director.SwitchedOut);
            float presence = 0f;
            bool playing = false;
            for (int k = running.Count - 1; k >= 0; k--)
            {
                Running r = running[k];
                bool owned = r.Piece == director.Piece || r.Piece == director.SwitchedOut;
                if (double.IsNaN(r.DspStart))
                {
                    if (!owned) { Finish(r); running.RemoveAt(k); continue; }
                    if (!IsLoaded(r.Track)) continue;
                    r.DspStart = dsp + ScheduleLead - r.Piece.Time;   // piece time and DSP time agree from here
                    if (r.Piece == director.Piece) card?.Show(r.Piece.Title);
                }

                double t = dsp - r.DspStart;
                Schedule(r, dsp);
                float trim = r.Track.TrimGain * SoundMix.MusicGain;
                bool any = false;
                foreach (Voice v in r.Voices)
                {
                    if (v.Source == null) continue;
                    float g = r.Piece.Gain(v.Iteration, v.Variant, t);
                    v.Source.volume = g * trim;
                    any |= v.Source.isPlaying || dsp < r.DspStart + r.Piece.IterationStart(v.Iteration);
                }

                bool over = r.Piece.Finished(t) || (!owned && !any);
                if (over)
                {
                    Finish(r);
                    running.RemoveAt(k);
                    continue;
                }

                float tailFade = 1f;
                double musicalEnd = r.Piece.IterationStart(r.Piece.Plan.Loops - 1) + r.Piece.FileLength - r.Piece.Plan.Tail;
                if (t > musicalEnd) tailFade = Mathf.Clamp01(1f - (float)((t - musicalEnd) / Math.Max(0.5, r.Piece.Plan.Tail)));
                presence = Mathf.Max(presence, r.Piece.Envelope(t) * tailFade);
                playing |= r.Piece == director.Piece && !r.Piece.Ending(t);
            }

            SoundMix.MusicPresence = presence;
            CinematicDirector.MusicWideBias = playing ? WideShotBias : 0f;
        }

        private void Adopt(MusicPiece piece)
        {
            if (piece == null) return;
            foreach (Running r in running)
                if (r.Piece == piece) return;
            running.Add(new Running { Piece = piece, Track = library.tracks[piece.Track] });
        }

        /// <summary>Starts the sources of every iteration due within the schedule window, sample-aligned to the piece's DSP start.</summary>
        private void Schedule(Running r, double dsp)
        {
            while (r.Piece.IterationWillPlay(r.NextIteration))
            {
                int i = r.NextIteration;
                double at = r.DspStart + r.Piece.IterationStart(i);
                if (at - dsp > ScheduleWindow) return;
                double startAt = Math.Max(at, dsp + ScheduleLead);
                double offset = startAt - at;
                if (offset >= r.Piece.FileLength - 0.5) { r.NextIteration++; continue; }   // a long stall: that iteration is gone
                MusicVariant top = r.Piece.HighestVariant(i);
                MusicVariant low = r.Piece.Plan.IsCut ? top : MusicVariant.Intensity1;
                for (MusicVariant v = low; v <= top; v++)
                {
                    AudioClip clip = r.Track.Clip(v);
                    if (clip == null) continue;
                    AudioSource source = Rent();
                    source.clip = clip;
                    source.volume = 0f;
                    source.timeSamples = offset > 0.0 ? Mathf.Clamp((int)(offset * clip.frequency), 0, clip.samples - 1) : 0;
                    source.PlayScheduled(startAt);
                    r.Voices.Add(new Voice { Source = source, Iteration = i, Variant = v });
                }

                r.NextIteration++;
            }

            // Sources whose iteration is over go back to the pool.
            for (int k = r.Voices.Count - 1; k >= 0; k--)
            {
                Voice v = r.Voices[k];
                if (v.Source == null || dsp < r.DspStart + r.Piece.IterationStart(v.Iteration) + 0.1 || v.Source.isPlaying) continue;
                Return(v.Source);
                r.Voices.RemoveAt(k);
            }
        }

        private void Finish(Running r)
        {
            r.Done = true;
            foreach (Voice v in r.Voices) if (v.Source != null) Return(v.Source);
            r.Voices.Clear();
        }

        private AudioSource Rent()
        {
            AudioSource s = pool.Count > 0 ? pool.Pop() : null;
            if (s == null)
            {
                s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 0f;
                s.priority = 0;
                s.dopplerLevel = 0f;
                s.ignoreListenerPause = true;
            }

            return s;
        }

        private void Return(AudioSource s)
        {
            s.Stop();
            s.clip = null;
            s.volume = 0f;
            pool.Push(s);
        }
    }
}
