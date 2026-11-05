using System;
using System.Text;

namespace MedievalZen.Endless
{
    /// <summary>The endless world's optional parts, each of which can be switched off (inspector toggles and the safety ladder).</summary>
    [Serializable]
    public struct EndlessFeatures
    {
        /// <summary>Distant tiles (meshes out to the far distance).</summary>
        public bool farTiles;

        /// <summary>Tree impostors on the distant tiles.</summary>
        public bool farTrees;

        /// <summary>Instanced trees on the walkable tiles (and their trunk colliders).</summary>
        public bool nearTrees;

        /// <summary>Grass and flower details on the walkable tiles.</summary>
        public bool grassDetails;

        /// <summary>Props (rocks, fences, buildings) on the walkable tiles.</summary>
        public bool props;

        /// <summary>Lake water planes.</summary>
        public bool water;

        /// <summary>Shadows cast by the instanced trees.</summary>
        public bool treeShadows;

        /// <summary>Animals along the road (<c>MedievalZen.Wildlife.WildlifeManager</c>).</summary>
        public bool animals;

        /// <summary>
        /// The forest pass's extras: ferns, undergrowth and ivy on the walkable tiles, sun shafts, dust and mist among the trunks,
        /// and the deeper haze and grade under the canopy. The first thing the safety ladder sheds (before any tree).
        /// </summary>
        public bool forestExtras;

        /// <summary>Everything on.</summary>
        public static EndlessFeatures All => new EndlessFeatures
        {
            farTiles = true, farTrees = true, nearTrees = true, grassDetails = true, props = true, water = true, treeShadows = true, animals = true,
            forestExtras = true,
        };

        /// <summary>What is on in both.</summary>
        public static EndlessFeatures And(EndlessFeatures a, EndlessFeatures b) => new EndlessFeatures
        {
            farTiles = a.farTiles && b.farTiles,
            farTrees = a.farTrees && b.farTrees && a.farTiles && b.farTiles,
            nearTrees = a.nearTrees && b.nearTrees,
            grassDetails = a.grassDetails && b.grassDetails,
            props = a.props && b.props,
            water = a.water && b.water,
            treeShadows = a.treeShadows && b.treeShadows && a.nearTrees && b.nearTrees,
            animals = a.animals && b.animals,
            forestExtras = a.forestExtras && b.forestExtras,
        };

        /// <summary>Compact list for the log and overlay, e.g. "farTiles+ farTrees- nearTrees+ …".</summary>
        public override string ToString()
        {
            var s = new StringBuilder(96);
            Flag(s, "farTiles", farTiles);
            Flag(s, "farTrees", farTrees);
            Flag(s, "nearTrees", nearTrees);
            Flag(s, "grass", grassDetails);
            Flag(s, "props", props);
            Flag(s, "water", water);
            Flag(s, "treeShadows", treeShadows);
            Flag(s, "animals", animals);
            Flag(s, "forestExtras", forestExtras);
            return s.ToString().TrimEnd();
        }

        private static void Flag(StringBuilder s, string name, bool on) => s.Append(name).Append(on ? "+ " : "- ");
    }

    /// <summary>How far a recorded run got.</summary>
    public enum RunPhase
    {
        /// <summary>Started; nothing more is known (a crash during start-up or shortly after leaves this).</summary>
        Started = 0,

        /// <summary>The world rendered for <see cref="SafetyLadder.RunningOkSeconds"/> seconds.</summary>
        RunningOk = 1,

        /// <summary>The run ended normally (Play stopped, scene unloaded, application quit).</summary>
        CleanExit = 2,
    }

    /// <summary>What the breadcrumb file remembers about the previous run.</summary>
    public struct SafetyRecord
    {
        /// <summary>Safety level the run used (0 = everything).</summary>
        public int Level;

        /// <summary>How far it got.</summary>
        public RunPhase Phase;
    }

    /// <summary>The safety level to start with and why.</summary>
    public struct SafetyDecision
    {
        /// <summary>Level to use.</summary>
        public int Level;

        /// <summary>True when the previous run did not end cleanly and the level went down a step (or was already at the bottom).</summary>
        public bool SteppedDown;

        /// <summary>One sentence for the log and the HUD, or null when nothing changed.</summary>
        public string Message;
    }

    /// <summary>
    /// The self-diagnosing safety ladder (pure, unit tested; the breadcrumb file I/O lives in <see cref="EndlessWorld"/>).
    /// A run that dies during start-up or within <see cref="RunningOkSeconds"/> of rendering starts the next run one level
    /// lower: 0 everything; 1 no forest extras (ferns, undergrowth, ivy, forest light and mist: shed before any tree); 2 also no
    /// distant trees; 3 no distant tiles; 4 also no near trees or animals; 5 also no grass; 6 ground, road and player only (no props,
    /// no water). A run that reaches "running OK" or ends cleanly keeps its level; the menu
    /// MedievalZen/Endless Road/Reset Safety Level returns to 0.
    /// </summary>
    public static class SafetyLadder
    {
        /// <summary>The lowest rung.</summary>
        public const int MaxLevel = 6;

        /// <summary>Seconds of rendering after which a run counts as "running OK".</summary>
        public const float RunningOkSeconds = 20f;

        /// <summary>The level to start at given the previous run's record (none = first run, level 0).</summary>
        public static SafetyDecision Decide(bool hasPrevious, SafetyRecord previous)
        {
            if (!hasPrevious) return new SafetyDecision { Level = 0 };
            int level = Clamp(previous.Level);
            if (previous.Phase != RunPhase.Started) return new SafetyDecision { Level = level };
            if (level >= MaxLevel)
            {
                return new SafetyDecision
                {
                    Level = MaxLevel, SteppedDown = true,
                    Message = $"The last run did not end cleanly at the lowest safety level ({MaxLevel}); starting with {Describe(MaxLevel)} again",
                };
            }

            int next = level + 1;
            return new SafetyDecision
            {
                Level = next, SteppedDown = true,
                Message = $"The last run did not end cleanly; starting {Describe(next)} (safety level {next})",
            };
        }

        /// <summary>The record written when a run starts at <paramref name="level"/>.</summary>
        public static SafetyRecord OnStart(int level) => new SafetyRecord { Level = Clamp(level), Phase = RunPhase.Started };

        /// <summary>The record after the world has rendered for <see cref="RunningOkSeconds"/> (a clean exit stays clean).</summary>
        public static SafetyRecord OnRunningOk(SafetyRecord r) => new SafetyRecord { Level = r.Level, Phase = r.Phase == RunPhase.CleanExit ? RunPhase.CleanExit : RunPhase.RunningOk };

        /// <summary>The record after a normal end.</summary>
        public static SafetyRecord OnCleanExit(SafetyRecord r) => new SafetyRecord { Level = r.Level, Phase = RunPhase.CleanExit };

        /// <summary>The record the reset menu writes.</summary>
        public static SafetyRecord Reset() => new SafetyRecord { Level = 0, Phase = RunPhase.CleanExit };

        /// <summary>What a level allows.</summary>
        public static EndlessFeatures FeaturesFor(int level)
        {
            level = Clamp(level);
            EndlessFeatures f = EndlessFeatures.All;
            if (level >= 1) f.forestExtras = false;
            if (level >= 2) f.farTrees = false;
            if (level >= 3) f.farTiles = false;
            if (level >= 4)
            {
                f.nearTrees = false;
                f.treeShadows = false;
                f.animals = false;
            }

            if (level >= 5) f.grassDetails = false;
            if (level >= 6)
            {
                f.props = false;
                f.water = false;
            }

            return f;
        }

        /// <summary>What a level leaves out, as words ("without distant trees").</summary>
        public static string Describe(int level)
        {
            switch (Clamp(level))
            {
                case 0: return "with everything";
                case 1: return "without the forest extras";
                case 2: return "without the forest extras or distant trees";
                case 3: return "without the forest extras or distant tiles";
                case 4: return "without the forest extras, distant tiles, near trees or animals";
                case 5: return "without the forest extras, distant tiles, near trees, animals or grass";
                default: return "with ground, road and player only (no distant tiles, trees, animals, grass, props or water)";
            }
        }

        /// <summary>Phase name in the breadcrumb file.</summary>
        public static string PhaseName(RunPhase phase) => phase == RunPhase.RunningOk ? "running OK" : phase == RunPhase.CleanExit ? "clean exit" : "started";

        /// <summary>Parses <see cref="PhaseName"/> (anything unknown reads as Started, the cautious answer).</summary>
        public static RunPhase ParsePhase(string name) => name == "running OK" ? RunPhase.RunningOk : name == "clean exit" ? RunPhase.CleanExit : RunPhase.Started;

        private static int Clamp(int level) => level < 0 ? 0 : level > MaxLevel ? MaxLevel : level;
    }

    /// <summary>The load the endless world puts on the GPU at a moment (for milestone log lines and the overlay).</summary>
    public struct EndlessLoad
    {
        /// <summary>Near tiles shown.</summary>
        public int NearTiles;

        /// <summary>Far tiles shown.</summary>
        public int FarTiles;

        /// <summary>Triangles of the far tiles shown.</summary>
        public int FarTriangles;

        /// <summary>Tree instances submitted by the instanced renderer this frame.</summary>
        public int TreeInstances;

        /// <summary>Draw calls submitted by the instanced renderer this frame.</summary>
        public int TreeDrawCalls;

        /// <summary>One line, e.g. "near 16, far 130 (412k tris), trees 5210 in 84 draws".</summary>
        public override string ToString() => $"near {NearTiles}, far {FarTiles} ({FarTriangles / 1000}k tris), trees {TreeInstances} in {TreeDrawCalls} draws";
    }

    /// <summary>
    /// Logs one line each time the load first passes a milestone (pure; the caller logs the line). Each metric has its own
    /// ascending thresholds; values that fall back and rise again do not log twice, and several crossings in one update give
    /// one line.
    /// </summary>
    public sealed class LoadMilestones
    {
        /// <summary>Near tile milestones.</summary>
        public static readonly int[] NearSteps = { 8, 16, 24, 32 };

        /// <summary>Far tile milestones.</summary>
        public static readonly int[] FarSteps = { 32, 64, 128, 192, 256 };

        /// <summary>Far triangle milestones.</summary>
        public static readonly int[] TriangleSteps = { 100000, 250000, 500000, 1000000, 1500000, 2000000 };

        /// <summary>Tree instance milestones.</summary>
        public static readonly int[] InstanceSteps = { 1000, 2500, 5000, 10000, 20000 };

        /// <summary>Tree draw call milestones.</summary>
        public static readonly int[] DrawSteps = { 50, 100, 200, 400 };

        private int nearIndex, farIndex, triangleIndex, instanceIndex, drawIndex;

        /// <summary>Lines produced so far.</summary>
        public int Lines { get; private set; }

        /// <summary>True (with a line) when <paramref name="load"/> passes at least one milestone not passed before.</summary>
        public bool Update(EndlessLoad load, out string line)
        {
            var crossed = new StringBuilder();
            Check(load.NearTiles, NearSteps, ref nearIndex, "near tiles", crossed);
            Check(load.FarTiles, FarSteps, ref farIndex, "far tiles", crossed);
            Check(load.FarTriangles, TriangleSteps, ref triangleIndex, "far triangles", crossed);
            Check(load.TreeInstances, InstanceSteps, ref instanceIndex, "tree instances", crossed);
            Check(load.TreeDrawCalls, DrawSteps, ref drawIndex, "tree draws", crossed);
            if (crossed.Length == 0)
            {
                line = null;
                return false;
            }

            Lines++;
            line = $"Endless road load: {load} (passed {crossed}).";
            return true;
        }

        private static void Check(int value, int[] steps, ref int index, string name, StringBuilder crossed)
        {
            int before = index;
            while (index < steps.Length && value >= steps[index]) index++;
            if (index == before) return;
            if (crossed.Length > 0) crossed.Append(", ");
            crossed.Append(name).Append(' ').Append(steps[index - 1]);
        }
    }
}
