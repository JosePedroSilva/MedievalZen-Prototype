using System;
using System.Collections.Generic;
using System.Text;

namespace MedievalZen.Sound
{
    /// <summary>Where the music director is.</summary>
    public enum MusicState
    {
        /// <summary>Ambience only; the gap since the last piece is still too short.</summary>
        Silent = 0,
        /// <summary>The gap is long enough: a track is picked and loaded, waiting for a moment with enough runway.</summary>
        Armed = 1,
        /// <summary>A piece is playing.</summary>
        Playing = 2,
        /// <summary>No further iteration will start: the last one's tail rings out, or an early fade runs.</summary>
        Ending = 3,
    }

    /// <summary>What can start a piece (<see cref="MusicMoments"/> rates them 0..1).</summary>
    public enum MusicMoment
    {
        None = 0,
        /// <summary>Plain open road with a long way to go: the weakest moment, enough once the gap is long.</summary>
        Wander,
        /// <summary>Walked over a rise with a long open view ahead.</summary>
        Crest,
        /// <summary>The canopy opened within the last 100 m.</summary>
        LeavingWoods,
        /// <summary>A landmark comes into view ahead.</summary>
        LandmarkSight,
        /// <summary>150-300 m out of an inn or village, heading on.</summary>
        LeavingPlace,
        /// <summary>The top of a mountain pass.</summary>
        PassSummit,
        /// <summary>The sun clears the horizon, seen from open country.</summary>
        Daybreak,
        /// <summary>The evening sun sinks into the golden hour, seen from open country.</summary>
        GoldenHour,
        /// <summary>Rain or mist ends.</summary>
        WeatherClearing,
        /// <summary>The traveller has sat for 30 s.</summary>
        Rest,
        /// <summary>A village first in view 250-450 m ahead: only ever a short cut.</summary>
        Arrival,
        /// <summary>F7.</summary>
        Manual,
    }

    /// <summary>A track as the director sees it (lengths, mood weights, the user's choices).</summary>
    public struct MusicDirectorTrack
    {
        public string Title;
        /// <summary>Musical length of an iteration (the full mixes' duration minus the tail), seconds.</summary>
        public float Loop;
        /// <summary>Reverb tail, seconds.</summary>
        public float Tail;
        /// <summary>Lengths of the two edits including their tails, seconds.</summary>
        public float Cut30, Cut60;
        /// <summary>Weight per <see cref="MusicMood"/> (4 entries).</summary>
        public float[] Mood;
        public bool Enabled;
        /// <summary>The user's extra weight (1 = as measured).</summary>
        public float UserWeight;

        /// <summary>The director's view of a catalogue entry.</summary>
        public static MusicDirectorTrack From(MusicTrackInfo t) => new MusicDirectorTrack
        {
            Title = t.Title,
            Loop = t.Loop,
            Tail = t.Tail,
            Cut30 = t.Cut30Seconds,
            Cut60 = t.Cut60Seconds,
            Mood = new[] { t.MoodWeight(MusicMood.OpenDay), t.MoodWeight(MusicMood.Forest), t.MoodWeight(MusicMood.Night), t.MoodWeight(MusicMood.Rest) },
            Enabled = true,
            UserWeight = 1f,
        };

        /// <summary>The whole catalogue.</summary>
        public static MusicDirectorTrack[] FromCatalog()
        {
            var tracks = new MusicDirectorTrack[MusicCatalog.Tracks.Length];
            for (int i = 0; i < tracks.Length; i++) tracks[i] = From(MusicCatalog.Tracks[i]);
            return tracks;
        }
    }

    /// <summary>What the director needs to know each tick (filled by the player, or by a test or a simulation).</summary>
    public struct MusicTickInput
    {
        /// <summary>Seconds since the last tick (real time).</summary>
        public double Dt;
        public MusicFrequency Frequency;
        /// <summary>A review tour or benchmark runs.</summary>
        public bool Capture;
        /// <summary>The world holds the traveller (streaming): the silence does not count.</summary>
        public bool Holding;
        /// <summary>Speed along the road, m/s.</summary>
        public float Speed;
        /// <summary>Road metres to the centre of the next inn, hamlet or village ahead; +inf when none within the search, NaN when unknown.</summary>
        public double PlaceAhead;
        public bool Seated;
        /// <summary>Seconds the traveller has been seated.</summary>
        public float SeatedSeconds;
        public MusicMood Mood;
        /// <summary>The best moment now and its strength 0..1.</summary>
        public MusicMoment Moment;
        public float Strength;
        /// <summary>The clips of <see cref="MusicDirector.WantedTrack"/> are loaded.</summary>
        public bool TrackReady;
    }

    /// <summary>
    /// The shape of a piece: a number of iterations of the aligned mixes (each starts over the previous one's tail) with a variant arc,
    /// or one edit (<see cref="MusicVariant.Cut30"/>, <see cref="MusicVariant.Cut60"/>).
    /// </summary>
    public sealed class MusicPlan
    {
        public bool IsCut;
        public MusicVariant Cut;
        public float CutLength;
        public float Loop, Tail;
        public bool Rest;
        /// <summary>Iterations planned (may shrink while playing, never grow).</summary>
        public int Loops;
        /// <summary>Per iteration: the variant it starts in and the one it crossfades to at its middle (equal when none).</summary>
        public MusicVariant[] From, To;

        /// <summary>Planned sounding length, tail included, seconds.</summary>
        public double Length => IsCut ? CutLength : Loops * (double)Loop + Tail;

        /// <summary>Start of the mid-iteration crossfade, seconds into an iteration.</summary>
        public double CrossStart => Loop * 0.5 - MusicDirector.CrossFade * 0.5;

        /// <summary>A plan of <paramref name="loops"/> iterations (1-4) with the arc of the design; a rest plan never goes above Intensity 2.</summary>
        public static MusicPlan Looped(int loops, float loop, float tail, bool rest)
        {
            loops = Math.Max(1, Math.Min(4, loops));
            var p = new MusicPlan { Loop = loop, Tail = tail, Rest = rest, Loops = loops, From = new MusicVariant[loops], To = new MusicVariant[loops] };
            const MusicVariant i1 = MusicVariant.Intensity1, i2 = MusicVariant.Intensity2, m = MusicVariant.Main;
            switch (loops)
            {
                case 1: Set(p, 0, i2, m); break;
                case 2: Set(p, 0, i1, i2); Set(p, 1, m, m); break;
                case 3: Set(p, 0, i1, i1); Set(p, 1, i2, m); Set(p, 2, m, i2); break;
                default: Set(p, 0, i1, i1); Set(p, 1, i2, i2); Set(p, 2, m, m); Set(p, 3, i2, i2); break;
            }

            if (rest)
            {
                for (int i = 0; i < loops; i++)
                {
                    if (p.From[i] == m) p.From[i] = i2;
                    if (p.To[i] == m) p.To[i] = i2;
                }

                if (loops == 3) Set(p, 2, i2, i1);   // a resting piece settles instead of climbing
            }

            return p;
        }

        /// <summary>A one-off edit.</summary>
        public static MusicPlan CutOf(MusicVariant cut, float length, float tail) =>
            new MusicPlan { IsCut = true, Cut = cut, CutLength = length, Tail = tail, Loop = length - tail, Loops = 1, From = new[] { cut }, To = new[] { cut } };

        private static void Set(MusicPlan p, int i, MusicVariant from, MusicVariant to)
        {
            p.From[i] = from;
            p.To[i] = to;
        }

        /// <summary>"3 loops I1 | I2>Main | Main>I2" or "Cut 60".</summary>
        public string Describe()
        {
            if (IsCut) return Cut == MusicVariant.Cut30 ? "Cut 30" : "Cut 60";
            var b = new StringBuilder();
            b.Append(Loops).Append(Loops == 1 ? " loop " : " loops ");
            for (int i = 0; i < Loops; i++)
            {
                if (i > 0) b.Append('|');
                b.Append(Short(From[i]));
                if (To[i] != From[i]) b.Append('>').Append(Short(To[i]));
            }

            return b.ToString();
        }

        /// <summary>A variant's short name.</summary>
        public static string Short(MusicVariant v) =>
            v == MusicVariant.Intensity1 ? "I1" : v == MusicVariant.Intensity2 ? "I2" : v == MusicVariant.Main ? "Main" : v == MusicVariant.Cut30 ? "C30" : "C60";
    }

    /// <summary>One piece being played: its plan, its clock and its gains. Pure: the player asks it what each source's volume is.</summary>
    public sealed class MusicPiece
    {
        public int Id;
        public int Track;
        public string Title;
        public MusicPlan Plan;
        public MusicMoment Moment;
        public float Strength;
        /// <summary>Seconds since the piece started sounding (advances once its clips are loaded).</summary>
        public double Time;
        /// <summary>Fade-in, seconds.</summary>
        public float FadeIn = MusicDirector.FadeIn;
        /// <summary>Piece time at which an early end began (NaN: none) and its fade length.</summary>
        public double EndAt = double.NaN;
        public float EndFade;
        public string EndReason;
        /// <summary>Director clock at the start.</summary>
        public double StartClock;

        /// <summary>An early end is under way.</summary>
        public bool EndingEarly => !double.IsNaN(EndAt);

        /// <summary>Piece time at which iteration <paramref name="i"/> starts.</summary>
        public double IterationStart(int i) => i * (double)Plan.Loop;

        /// <summary>Length of one iteration's file (the full mix or the edit), seconds.</summary>
        public double FileLength => Plan.IsCut ? Plan.CutLength : Plan.Loop + Plan.Tail;

        /// <summary>The iteration whose musical part contains <paramref name="t"/> (clamped to the plan).</summary>
        public int IterationAt(double t) => Plan.IsCut ? 0 : Math.Max(0, Math.Min(Plan.Loops - 1, (int)Math.Floor(t / Plan.Loop)));

        /// <summary>Whether iteration <paramref name="i"/> may still start (planned and not cut off by an early end before it).</summary>
        public bool IterationWillPlay(int i) => i < Plan.Loops && (!EndingEarly || IterationStart(i) < EndAt);

        /// <summary>The piece is over: the last tail has rung out, or the early fade has finished.</summary>
        public bool Finished(double t)
        {
            if (EndingEarly && t >= EndAt + EndFade) return true;
            return t >= IterationStart(Plan.Loops - 1) + FileLength;
        }

        /// <summary>No further iteration will start: the last musical part is over (the tail rings), or an early fade runs.</summary>
        public bool Ending(double t) => EndingEarly || t >= IterationStart(Plan.Loops - 1) + (Plan.IsCut ? Plan.CutLength - Plan.Tail : Plan.Loop);

        /// <summary>Sounding seconds left in the plan from <paramref name="t"/>, tail included.</summary>
        public double Remaining(double t) => EndingEarly ? Math.Max(0.0, EndAt + EndFade - t) : Math.Max(0.0, IterationStart(Plan.Loops - 1) + FileLength - t);

        /// <summary>The piece's fade envelope 0..1 at <paramref name="t"/> (fade-in, early fade-out).</summary>
        public float Envelope(double t)
        {
            double e = FadeIn > 0f ? Clamp01(t / FadeIn) : 1.0;
            e = Math.Sin(e * Math.PI * 0.5);
            if (EndingEarly) e *= Math.Cos(Clamp01((t - EndAt) / Math.Max(0.01, EndFade)) * Math.PI * 0.5);
            return (float)e;
        }

        /// <summary>
        /// Gain (0..1, envelope included) of <paramref name="variant"/> in iteration <paramref name="i"/> at piece time <paramref name="t"/>:
        /// the arc's 8 s equal-power crossfades, and an early end's step down by one variant.
        /// </summary>
        public float Gain(int i, MusicVariant variant, double t)
        {
            if (!IterationWillPlay(i)) return 0f;
            double u = t - IterationStart(i);
            if (u < 0.0 || u >= FileLength) return 0f;
            double w;
            if (Plan.IsCut) w = variant == Plan.Cut ? 1.0 : 0.0;
            else if (EndingEarly)
            {
                double s = Clamp01((t - EndAt) / MusicDirector.CrossFade) * Math.PI * 0.5;
                double own = Base(i, variant, u), from = 0.0;
                for (MusicVariant v = MusicVariant.Intensity1; v <= MusicVariant.Main; v++)
                    if (v != variant && Down(v) == variant) from += Base(i, v, u);
                w = (Down(variant) == variant ? own : own * Math.Cos(s)) + from * Math.Sin(s);
            }
            else w = Base(i, variant, u);

            return (float)(w * Envelope(t));
        }

        /// <summary>The variant the arc weights most at piece time <paramref name="t"/>.</summary>
        public MusicVariant DominantVariant(double t)
        {
            if (Plan.IsCut) return Plan.Cut;
            int i = IterationAt(t);
            double u = t - IterationStart(i);
            return Base(i, Plan.From[i], u) >= Base(i, Plan.To[i], u) ? Plan.From[i] : Plan.To[i];
        }

        /// <summary>The highest variant iteration <paramref name="i"/> can use (the player starts sources for it and those below).</summary>
        public MusicVariant HighestVariant(int i) => Plan.IsCut ? Plan.Cut : Plan.From[i] > Plan.To[i] ? Plan.From[i] : Plan.To[i];

        /// <summary>One variant down (Intensity 1 stays).</summary>
        public static MusicVariant Down(MusicVariant v) => v == MusicVariant.Main ? MusicVariant.Intensity2 : v == MusicVariant.Intensity2 ? MusicVariant.Intensity1 : v;

        private double Base(int i, MusicVariant v, double u)
        {
            MusicVariant a = Plan.From[i], b = Plan.To[i];
            if (a == b) return v == a ? 1.0 : 0.0;
            double x = Clamp01((u - Plan.CrossStart) / MusicDirector.CrossFade) * Math.PI * 0.5;
            if (v == a) return Math.Cos(x);
            if (v == b) return Math.Sin(x);
            return 0.0;
        }

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
    }

    /// <summary>A finished (or running) piece, for the log, the overlay and the offline simulation.</summary>
    public sealed class MusicPieceLog
    {
        public int Id;
        public double StartClock, EndClock = double.NaN;
        public string Title;
        public MusicMoment Moment;
        public float Strength;
        public string Plan;
        public double PlannedLength;
        public bool EndedEarly;
        public string Reason;
        public double Silence;
    }

    /// <summary>
    /// The music's pure state machine (2026-10-06, the user's direction: "like Death Stranding, where the music stops and there are long
    /// periods with just ambience"): Silent → Armed → Playing → Ending → Silent. A piece is an event: it waits for a minimum gap, then
    /// for a moment (a crest, leaving the woods, a landmark, daybreak...) whose strength beats a threshold that falls from 0.8 at the
    /// minimum gap to 0.25 at the later time, and only starts when the road ahead holds the whole piece before the next place (the
    /// runway rule: Death Stranding's songs were cut off 20 s before a facility). A piece ends by not starting another iteration, so
    /// the tail rings out; an early end steps down one variant and fades over 12 s. Never a hard cut.
    /// </summary>
    public sealed class MusicDirector
    {
        public const float FadeIn = 4f, CrossFade = 8f, EarlyFade = 12f, OffFade = 6f, SwitchFade = 3f;
        /// <summary>Seconds seated before the rest moment.</summary>
        public const float RestDelay = 30f;
        /// <summary>A piece must end this many seconds before the place point.</summary>
        public const float RunwayMargin = 20f;
        /// <summary>The place point: this far before an inn, hamlet or village centre.</summary>
        public const float PlaceLead = 120f;
        /// <summary>The runway is reckoned at no less than this speed (m/s).</summary>
        public const float MinRunwaySpeed = 1.2f;
        /// <summary>Lowest strength any moment needs (at and after the later time).</summary>
        public const float LateThreshold = 0.25f, EarlyThreshold = 0.8f;
        /// <summary>The session's first piece: from 3 min with a strong moment, by about 8 min with any.</summary>
        public const float OpeningMin = 180f, OpeningLate = 480f;
        /// <summary>An iteration this close to starting is committed (the player has scheduled it).</summary>
        public const float CommitAhead = 3f;

        private readonly MusicDirectorTrack[] tracks;
        private readonly Random random;
        private readonly List<int> bag = new List<int>();
        private int previousTrack = -1;
        private int lastPlayedTrack = -1;
        private bool opening = true;
        private double armRoll;
        private int nextId = 1;

        public MusicDirector(MusicDirectorTrack[] tracks, int seed)
        {
            this.tracks = tracks ?? new MusicDirectorTrack[0];
            random = new Random(seed);
        }

        public MusicState State { get; private set; }
        /// <summary>The piece playing (null when silent).</summary>
        public MusicPiece Piece { get; private set; }
        /// <summary>A piece switched out by F8, fading over <see cref="SwitchFade"/> (the player fades it; null otherwise).</summary>
        public MusicPiece SwitchedOut { get; private set; }
        /// <summary>The track picked while armed (-1: none).</summary>
        public int ArmedTrack { get; private set; } = -1;
        /// <summary>The track whose clips must be loaded now: the piece's, else the armed one (-1: none).</summary>
        public int WantedTrack => Piece != null ? Piece.Track : ArmedTrack;
        /// <summary>Seconds since the director started.</summary>
        public double Clock { get; private set; }
        /// <summary>Counted seconds of silence since the last piece ended (holds excluded).</summary>
        public double Silence { get; private set; }
        /// <summary>The strength a moment needs right now (+inf: not armed).</summary>
        public float Threshold { get; private set; } = float.PositiveInfinity;
        /// <summary>Why nothing starts (for the overlay).</summary>
        public string Waiting { get; private set; } = string.Empty;
        /// <summary>Every piece so far (capped).</summary>
        public readonly List<MusicPieceLog> History = new List<MusicPieceLog>();
        public IReadOnlyList<MusicDirectorTrack> Tracks => tracks;

        /// <summary>The minimum gap and the later time (seconds) for a frequency; false for Off.</summary>
        public static bool Gaps(MusicFrequency frequency, bool opening, out float min, out float late)
        {
            switch (frequency)
            {
                case MusicFrequency.Rare: min = 20f * 60f; late = 40f * 60f; break;
                case MusicFrequency.Sometimes: min = 8f * 60f; late = 16f * 60f; break;
                case MusicFrequency.Often: min = 3f * 60f; late = 7f * 60f; break;
                default: min = late = float.PositiveInfinity; return false;
            }

            if (opening)
            {
                min = Math.Min(min, OpeningMin);
                late = Math.Min(late, OpeningLate);
            }

            return true;
        }

        /// <summary>The strength needed after <paramref name="silence"/> s: 0.8 at the minimum gap, falling linearly to 0.25 at the later time.</summary>
        public static float ThresholdAt(double silence, float min, float late)
        {
            if (silence < min) return float.PositiveInfinity;
            if (late <= min) return LateThreshold;
            double x = (silence - min) / (late - min);
            if (x >= 1.0) return LateThreshold;
            return (float)(EarlyThreshold + (LateThreshold - EarlyThreshold) * x);
        }

        /// <summary>Seconds of road before the place point at the reckoning speed (0 when unknown).</summary>
        public static double RunwaySeconds(in MusicTickInput input)
        {
            if (double.IsNaN(input.PlaceAhead)) return 0.0;
            if (double.IsPositiveInfinity(input.PlaceAhead)) return double.PositiveInfinity;
            return Math.Max(0.0, input.PlaceAhead - PlaceLead) / Math.Max(input.Speed, MinRunwaySpeed);
        }

        /// <summary>Advances the director by one tick.</summary>
        public void Tick(in MusicTickInput input)
        {
            Clock += input.Dt;
            if (SwitchedOut != null)
            {
                SwitchedOut.Time += input.Dt;
                if (SwitchedOut.Time >= SwitchedOut.EndAt + SwitchedOut.EndFade) SwitchedOut = null;
            }

            if (Piece != null)
            {
                TickPiece(input);
                if (Piece != null) return;
            }

            if (input.Frequency == MusicFrequency.Off || !Gaps(input.Frequency, opening, out float min, out float late))
            {
                State = MusicState.Silent;
                ArmedTrack = -1;
                Threshold = float.PositiveInfinity;
                Waiting = "music off";
                return;
            }

            if (!input.Holding) Silence += input.Dt;
            Threshold = ThresholdAt(Silence, min, late);
            if (float.IsPositiveInfinity(Threshold))
            {
                State = MusicState.Silent;
                ArmedTrack = -1;
                Waiting = $"silence {Silence / 60.0:0.0}/{min / 60f:0} min";
                return;
            }

            if (State != MusicState.Armed || ArmedTrack < 0)
            {
                State = MusicState.Armed;
                armRoll = random.NextDouble();
            }

            int wanted = Peek(input.Mood);
            if (wanted != ArmedTrack) ArmedTrack = wanted;
            if (ArmedTrack < 0) { Waiting = "no enabled track"; return; }
            if (input.Capture) { Waiting = "capture running"; return; }
            if (input.Holding) { Waiting = "world holding"; return; }
            if (!input.TrackReady) { Waiting = "loading " + tracks[ArmedTrack].Title; return; }
            if (input.Moment == MusicMoment.None || input.Strength < Threshold)
            {
                Waiting = $"armed, needs {Threshold:0.00}, best {input.Moment} {input.Strength:0.00}";
                return;
            }

            MusicPlan plan = PlanFor(input.Moment, input, ArmedTrack, false);
            if (plan == null)
            {
                Waiting = $"armed, {input.Moment} {input.Strength:0.00} but runway {RunwaySeconds(input):0} s";
                return;
            }

            Start(ArmedTrack, plan, input.Moment, input.Strength, FadeIn);
        }

        /// <summary>F7 while silent or armed: a piece now (ignoring the gap and the moment; not when Off). False when it cannot.</summary>
        public bool RequestNow(in MusicTickInput input)
        {
            if (input.Frequency == MusicFrequency.Off || Piece != null || input.Capture) return false;
            if (ArmedTrack < 0 || State != MusicState.Armed)
            {
                armRoll = random.NextDouble();
                ArmedTrack = Peek(input.Mood);
            }

            if (ArmedTrack < 0) return false;
            MusicPlan plan = PlanFor(MusicMoment.Manual, input, ArmedTrack, true);
            Start(ArmedTrack, plan, MusicMoment.Manual, 1f, FadeIn);
            return true;
        }

        /// <summary>F7 while playing: end gracefully (step down, 12 s fade). False when nothing plays or it already ends early.</summary>
        public bool RequestEnd(string reason = "F7")
        {
            if (Piece == null || Piece.EndingEarly) return false;
            EndEarly(EarlyFade, reason);
            return true;
        }

        /// <summary>F8: the next enabled track in catalogue order, crossfaded over 3 s. Works while silent too (starts that track).</summary>
        public bool NextTrack(in MusicTickInput input)
        {
            if (input.Frequency == MusicFrequency.Off || input.Capture || tracks.Length == 0) return false;
            int from = Piece != null ? Piece.Track : lastPlayedTrack;
            int next = -1;
            for (int k = 1; k <= tracks.Length; k++)
            {
                int c = ((from < 0 ? -1 : from) + k + tracks.Length) % tracks.Length;
                if (tracks[c].Enabled) { next = c; break; }
            }

            if (next < 0) return false;
            if (Piece != null)
            {
                MusicPiece old = Piece;
                if (!old.EndingEarly) { old.EndAt = old.Time; old.EndFade = SwitchFade; old.EndReason = "F8"; }
                Close(old, true);
                SwitchedOut = old;
                Piece = null;
            }

            MusicPlan plan = PlanFor(MusicMoment.Manual, input, next, true);
            Start(next, plan, MusicMoment.Manual, 1f, SwitchFade);
            return true;
        }

        private void TickPiece(in MusicTickInput input)
        {
            MusicPiece p = Piece;
            if (input.TrackReady || p.Time > 0.0) p.Time += input.Dt;
            double t = p.Time;
            if (p.Finished(t))
            {
                Close(p, p.EndingEarly);
                Piece = null;
                State = MusicState.Silent;
                ArmedTrack = -1;
                Silence = 0.0;
                return;
            }

            if (!p.EndingEarly)
            {
                if (input.Frequency == MusicFrequency.Off) EndEarly(OffFade, "music off");
                else if (input.Capture) EndEarly(EarlyFade, "capture");
                else if (p.Plan.Rest)
                {
                    if (!input.Seated) Truncate(p, "stood up");
                }
                else if (p.Moment != MusicMoment.Manual)   // F7 / F8 pieces were asked for: only Off, capture and F7 end them
                {
                    double runway = RunwaySeconds(input) + RunwayMargin * 0.5;   // half the margin may be used once playing
                    if (p.Remaining(t) > runway)
                    {
                        Truncate(p, "place ahead");
                        if (p.Remaining(t) > runway && runway <= EarlyFade + RunwayMargin * 0.5) EndEarly(EarlyFade, "place reached");
                    }
                }
            }

            State = p.Ending(t) ? MusicState.Ending : MusicState.Playing;
            Waiting = string.Empty;
        }

        /// <summary>Drops the iterations not yet committed (the tail of the last committed one rings out).</summary>
        private static void Truncate(MusicPiece p, string reason)
        {
            if (p.Plan.IsCut) return;
            int committed = Math.Max(0, (int)Math.Floor((p.Time + CommitAhead) / p.Plan.Loop));
            int keep = Math.Min(p.Plan.Loops, committed + 1);
            if (keep < p.Plan.Loops)
            {
                p.Plan.Loops = keep;
                p.EndReason = reason;
            }
        }

        private void EndEarly(float fade, string reason)
        {
            Piece.EndAt = Piece.Time;
            Piece.EndFade = fade;
            Piece.EndReason = reason;
        }

        private void Start(int track, MusicPlan plan, MusicMoment moment, float strength, float fadeIn)
        {
            Commit(track);
            Piece = new MusicPiece
            {
                Id = nextId++, Track = track, Title = tracks[track].Title, Plan = plan, Moment = moment, Strength = strength, FadeIn = fadeIn,
                StartClock = Clock,
            };
            if (History.Count >= 256) History.RemoveAt(0);
            History.Add(new MusicPieceLog
            {
                Id = Piece.Id, StartClock = Clock, Title = Piece.Title, Moment = moment, Strength = strength, Plan = plan.Describe(),
                PlannedLength = plan.Length, Silence = Silence,
            });
            opening = false;
            lastPlayedTrack = track;
            ArmedTrack = -1;
            State = MusicState.Playing;
        }

        private void Close(MusicPiece p, bool early)
        {
            for (int i = History.Count - 1; i >= 0; i--)
            {
                if (History[i].Id != p.Id) continue;
                if (double.IsNaN(History[i].EndClock))
                {
                    History[i].EndClock = Clock;
                    History[i].EndedEarly = early;
                    History[i].Reason = p.EndReason;
                }

                break;
            }
        }

        /// <summary>
        /// The plan for a moment: rest pieces 2-3 loops capped at Intensity 2; an arrival only an edit; otherwise 2-4 loops, 1 loop,
        /// Cut 60 or Cut 30, the longest kind that fits the runway (null when none does). <paramref name="manual"/> always plays something.
        /// </summary>
        public MusicPlan PlanFor(MusicMoment moment, in MusicTickInput input, int track, bool manual)
        {
            MusicDirectorTrack t = tracks[track];
            if (input.Seated && (moment == MusicMoment.Rest || manual)) return MusicPlan.Looped(2 + random.Next(2), t.Loop, t.Tail, true);
            double runway = RunwaySeconds(input) - RunwayMargin;
            if (manual && double.IsNaN(input.PlaceAhead)) runway = double.PositiveInfinity;
            if (moment != MusicMoment.Arrival)
            {
                double fit = (runway - t.Tail) / t.Loop;
                int loops = double.IsPositiveInfinity(fit) ? 4 : (int)Math.Floor(Math.Min(4.0, fit));
                if (loops >= 2) return MusicPlan.Looped(2 + random.Next(loops - 1), t.Loop, t.Tail, false);
                if (loops == 1) return MusicPlan.Looped(1, t.Loop, t.Tail, false);
            }

            if (t.Cut60 <= runway) return MusicPlan.CutOf(MusicVariant.Cut60, t.Cut60, t.Tail);
            if (t.Cut30 <= runway || manual) return MusicPlan.CutOf(MusicVariant.Cut30, t.Cut30, t.Tail);
            return null;
        }

        /// <summary>The track the bag would give for <paramref name="mood"/> with this arming's roll (no state change).</summary>
        private int Peek(MusicMood mood)
        {
            double total = Candidates(mood);
            if (total <= 0.0)
            {
                Refill();
                total = Candidates(mood);
            }

            if (total <= 0.0) return previousTrack >= 0 && tracks[previousTrack].Enabled ? previousTrack : -1;
            double pick = armRoll * total;
            int last = -1;
            for (int i = 0; i < bag.Count; i++)
            {
                double w = WeightOf(bag[i], mood);
                if (w <= 0.0) continue;
                last = bag[i];
                pick -= w;
                if (pick < 0.0) return bag[i];
            }

            return last;
        }

        private double Candidates(MusicMood mood)
        {
            double total = 0.0;
            for (int i = 0; i < bag.Count; i++) total += WeightOf(bag[i], mood);
            return total;
        }

        private double WeightOf(int track, MusicMood mood)
        {
            if (track == previousTrack || !tracks[track].Enabled) return 0.0;
            float[] m = tracks[track].Mood;
            double w = m != null && m.Length > (int)mood ? m[(int)mood] : 1.0;
            return Math.Max(0.0, w * Math.Max(0f, tracks[track].UserWeight));
        }

        private void Refill()
        {
            bag.Clear();
            for (int i = 0; i < tracks.Length; i++)
                if (tracks[i].Enabled) bag.Add(i);
        }

        private void Commit(int track)
        {
            bag.Remove(track);
            previousTrack = track;
        }

        /// <summary>One line for the F3 overlay.</summary>
        public string Status()
        {
            var b = new StringBuilder("Music: ");
            b.Append(State);
            if (Piece != null)
            {
                b.Append("  ♪ ").Append(Piece.Title).Append("  ").Append(Piece.Plan.Describe()).Append("  ")
                 .Append(Piece.Time.ToString("0")).Append('/').Append(Piece.Plan.Length.ToString("0")).Append(" s  ")
                 .Append(MusicPlan.Short(Piece.DominantVariant(Piece.Time))).Append("  (").Append(Piece.Moment).Append(')');
                if (Piece.EndingEarly) b.Append("  ending: ").Append(Piece.EndReason);
            }
            else
            {
                b.Append("  silence ").Append((Silence / 60.0).ToString("0.0")).Append(" min");
                if (!string.IsNullOrEmpty(Waiting)) b.Append("  ").Append(Waiting);
            }

            return b.ToString();   // the overlay itself advertises F7 / F8
        }
    }
}
