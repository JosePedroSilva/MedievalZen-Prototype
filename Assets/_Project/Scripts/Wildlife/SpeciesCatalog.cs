using System;
using System.Collections.Generic;
using System.Text;

namespace MedievalZen.Wildlife
{
    /// <summary>The animals this game shows (polyperfect Low Poly Animated Animals, see <c>Docs/ASSET_CATALOG.md</c>).</summary>
    public enum Species
    {
        Cow = 0,
        Sheep = 1,
        Goat = 2,
        Pig = 3,
        Horse = 4,
        Hen = 5,
        Rooster = 6,
        Chick = 7,
        Goose = 8,
        Retriever = 9,
        GreatDane = 10,
        Cat = 11,
        Dove = 12,
        Deer = 13,
        Stag = 14,
        Fox = 15,
        Rabbit = 16,
        Squirrel = 17,
        Boar = 18,
        Wolf = 19,
        Eagle = 20,
        Beaver = 21,
    }

    /// <summary>What an animal is doing, as far as its animation is concerned. Each controller maps a pose to one bool (or none).</summary>
    public enum Pose
    {
        Idle = 0,
        Walk = 1,
        Run = 2,
        Eat = 3,
        Sleep = 4,
        Sit = 5,
        Alert = 6,
        Idle2 = 7,
        Fly = 8,
        Bark = 9,
        Howl = 10,
        Display = 11,
    }

    /// <summary>How a pose's bool is driven.</summary>
    public enum BindKind
    {
        /// <summary>Held true while the pose is wanted; clearing it returns to Idle after a short blend.</summary>
        Hold = 0,

        /// <summary>Held true; leaving plays a get-up (or similar) clip first, so Idle comes back only after <see cref="PoseBinding.LeaveSeconds"/>.</summary>
        Chain = 1,

        /// <summary>Set true for a moment only: the state returns to Idle by exit time (a bark).</summary>
        Pulse = 2,
    }

    /// <summary>How a species lives together.</summary>
    public enum GroupStyle
    {
        Herd = 0,
        Flock = 1,
        Pair = 2,
        Solitary = 3,
    }

    /// <summary>One pose → bool mapping of a controller.</summary>
    public struct PoseBinding
    {
        /// <summary>The pose.</summary>
        public Pose Pose;

        /// <summary>Animator bool parameter name.</summary>
        public string Bool;

        /// <summary>How the bool is driven.</summary>
        public BindKind Kind;

        /// <summary>Seconds after clearing the bool until the controller is back in Idle (blend, get-up clip, exit time).</summary>
        public float LeaveSeconds;

        /// <summary>Pulse only: how long the bool stays true.</summary>
        public float PulseSeconds;

        /// <summary>Index of <see cref="Bool"/> in <see cref="SpeciesInfo.Bools"/> (filled by the catalog).</summary>
        public int BoolIndex;
    }

    /// <summary>A sound a species makes, by the pack's clip name (<c>Sounds/*.ogg</c> without the extension).</summary>
    public struct SpeciesSound
    {
        /// <summary>Clip name, e.g. "SFX_Cow_Moo".</summary>
        public string Clip;

        /// <summary>Volume before the player's "Animal sounds" setting, 0..1.</summary>
        public float Volume;

        /// <summary>Mean seconds between calls while the animal is within earshot (0 = only on cue: events, alarms).</summary>
        public float MeanInterval;

        /// <summary>Only while the animal is in this pose (Idle = any pose).</summary>
        public Pose When;
    }

    /// <summary>
    /// Everything the planner, brains and bool driver need about one species. Plain data (no Unity calls). Speeds are
    /// ground speeds in m/s at the scale the animal is shown; the clip speeds are what the walk and run cycles look right
    /// at with the animator at speed 1 (first guesses by eye: the clips carry no speed data), used to match foot motion.
    /// </summary>
    public sealed class SpeciesInfo
    {
        public Species Id;
        public string Name;

        /// <summary>Pack prefab names (colour variants), first is the default.</summary>
        public string[] Prefabs;

        /// <summary>Animator controller file (without extension) whose graph the bindings follow (the hen's override controller uses the rooster's graph).</summary>
        public string Controller;

        /// <summary>Name of the controller's default (idle) state.</summary>
        public string IdleState;

        /// <summary>Overall height in metres at idle (horse: head height).</summary>
        public float TargetHeight;

        /// <summary>When set, scale like this species' first prefab (a stag uses the hind's body scale; its antlers add height).</summary>
        public Species? ScaleLike;

        public GroupStyle Style;
        public float WalkSpeed, RunSpeed, FlySpeed;
        public float WalkClipSpeed, RunClipSpeed;

        /// <summary>Degrees per second the animal turns at most.</summary>
        public float TurnRate = 160f;

        /// <summary>Traveller distance at which a wild animal looks up (0: tame, never alarmed).</summary>
        public float AlertDistance;

        /// <summary>Traveller distance at which it bolts.</summary>
        public float FleeDistance;

        /// <summary>A tame animal steps aside when the traveller comes this close (hens underfoot).</summary>
        public float PersonalSpace = 1.6f;

        /// <summary>Keeps this far from its neighbours.</summary>
        public float Separation = 1.2f;

        public bool Wild;
        public bool SleepsAtNight;
        public bool Nocturnal;
        public bool Flies;

        /// <summary>Shown only between dusk and dawn (the wolf).</summary>
        public bool NightOnly;

        /// <summary>Largest ground slope (rise over run) it stands on.</summary>
        public float MaxSlope = 0.45f;

        public PoseBinding[] Poses = Array.Empty<PoseBinding>();
        public SpeciesSound[] Sounds = Array.Empty<SpeciesSound>();

        /// <summary>Distinct bool names the bindings use (index = <see cref="PoseBinding.BoolIndex"/>).</summary>
        public string[] Bools = Array.Empty<string>();

        /// <summary>The binding for a pose, or false when the controller has no state for it (the animal then shows Idle).</summary>
        public bool TryGetBinding(Pose pose, out PoseBinding binding)
        {
            for (int i = 0; i < Poses.Length; i++)
            {
                if (Poses[i].Pose != pose) continue;
                binding = Poses[i];
                return true;
            }

            binding = default;
            return false;
        }

        /// <summary>True when the controller has a state for <paramref name="pose"/>.</summary>
        public bool Has(Pose pose) => TryGetBinding(pose, out _);

        /// <summary>The ground speed a gait moves at.</summary>
        public float SpeedFor(Pose pose) => pose == Pose.Run ? RunSpeed : pose == Pose.Fly ? FlySpeed : pose == Pose.Walk ? WalkSpeed : 0f;

        public override string ToString() => Name;
    }

    /// <summary>
    /// The species used along the road. Each row's bools are taken from the pack's controllers (verified by
    /// <c>WildlifeTests.CatalogMatchesTheControllers</c> and by the palette builder): never <c>isDead</c>, attack bools only
    /// as harmless display (a stag's feint, a horse rearing).
    /// </summary>
    public static class SpeciesCatalog
    {
        private static readonly SpeciesInfo[] all = Build();

        /// <summary>Every species, indexed by <see cref="Species"/>.</summary>
        public static IReadOnlyList<SpeciesInfo> All => all;

        /// <summary>One species.</summary>
        public static SpeciesInfo Get(Species s) => all[(int)s];

        /// <summary>Number of species.</summary>
        public static int Count => all.Length;

        /// <summary>Stable text of everything the palette depends on (prefab names); the palette is rebuilt when it changes.</summary>
        public static string Signature
        {
            get
            {
                var sb = new StringBuilder(512);
                foreach (SpeciesInfo s in all)
                {
                    sb.Append(s.Name).Append(':');
                    foreach (string p in s.Prefabs) sb.Append(p).Append(',');
                    sb.Append(';');
                }

                return sb.ToString();
            }
        }

        /// <summary>Every distinct prefab name in the catalog.</summary>
        public static List<string> PrefabNames()
        {
            var list = new List<string>();
            foreach (SpeciesInfo s in all)
            {
                foreach (string p in s.Prefabs)
                {
                    if (!list.Contains(p)) list.Add(p);
                }
            }

            return list;
        }

        /// <summary>Every distinct sound clip name in the catalog (plus the gallop used by the horses event).</summary>
        public static List<string> ClipNames()
        {
            var list = new List<string>();
            foreach (SpeciesInfo s in all)
            {
                foreach (SpeciesSound snd in s.Sounds)
                {
                    if (!list.Contains(snd.Clip)) list.Add(snd.Clip);
                }
            }

            foreach (string extra in ExtraClips)
            {
                if (!list.Contains(extra)) list.Add(extra);
            }

            return list;
        }

        /// <summary>Clips used by events only.</summary>
        public static readonly string[] ExtraClips = { "SFX_Horse_Run", "SFX_Dog_Barking" };

        // ------------------------------------------------------------------ rows

        private static PoseBinding Hold(Pose p, string b, float leave = 0.35f) => new PoseBinding { Pose = p, Bool = b, Kind = BindKind.Hold, LeaveSeconds = leave };
        private static PoseBinding Chain(Pose p, string b, float leave) => new PoseBinding { Pose = p, Bool = b, Kind = BindKind.Chain, LeaveSeconds = leave };
        private static PoseBinding Pulse(Pose p, string b, float pulse, float leave) => new PoseBinding { Pose = p, Bool = b, Kind = BindKind.Pulse, PulseSeconds = pulse, LeaveSeconds = leave };
        private static SpeciesSound Snd(string clip, float volume, float interval, Pose when = Pose.Idle) => new SpeciesSound { Clip = clip, Volume = volume, MeanInterval = interval, When = when };

        private static SpeciesInfo[] Build()
        {
            var rows = new SpeciesInfo[22];

            // Farm animals. Sleep is a chain in every controller (lie-down clip → loop; on false → get-up → Idle).
            rows[(int)Species.Cow] = new SpeciesInfo
            {
                Id = Species.Cow, Name = "cow", Prefabs = new[] { "Cow_Brown", "Cow_Black", "Cow_White", "Cow_Horns_Brown" },
                Controller = "CTL_Cow", IdleState = "Idle", TargetHeight = 1.45f, Style = GroupStyle.Herd,
                WalkSpeed = 0.9f, RunSpeed = 3.6f, WalkClipSpeed = 1.0f, RunClipSpeed = 4.0f, TurnRate = 70f,
                PersonalSpace = 1.8f, Separation = 2.4f, SleepsAtNight = true, MaxSlope = 0.4f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning"), Hold(Pose.Eat, "isEating"), Hold(Pose.Idle2, "isIdle2"),
                    Hold(Pose.Alert, "isIdle2"), Chain(Pose.Sleep, "isSleeping", 2.2f) },
                Sounds = new[] { Snd("SFX_Cow_Moo", 0.55f, 55f), Snd("SFX_Cow_Bell", 0.3f, 18f, Pose.Walk), Snd("SFX_Eating_Grass", 0.22f, 14f, Pose.Eat) },
            };
            rows[(int)Species.Sheep] = new SpeciesInfo
            {
                Id = Species.Sheep, Name = "sheep", Prefabs = new[] { "Sheep", "Sheep_Wool" },
                Controller = "CTL_Sheep", IdleState = "idle", TargetHeight = 0.9f, Style = GroupStyle.Flock,
                WalkSpeed = 0.8f, RunSpeed = 3.4f, WalkClipSpeed = 0.9f, RunClipSpeed = 3.6f, TurnRate = 110f,
                AlertDistance = 9f, FleeDistance = 4f, Separation = 1.4f, SleepsAtNight = true, MaxSlope = 0.55f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning"), Hold(Pose.Eat, "isEating"), Chain(Pose.Sleep, "isSleeping", 1.6f) },
                Sounds = new[] { Snd("SFX_Eating_Grass", 0.18f, 16f, Pose.Eat) },
            };
            rows[(int)Species.Goat] = new SpeciesInfo
            {
                Id = Species.Goat, Name = "goat", Prefabs = new[] { "Goat" },
                Controller = "CTL_Goat", IdleState = "idle", TargetHeight = 0.85f, Style = GroupStyle.Herd,
                WalkSpeed = 0.8f, RunSpeed = 3.8f, WalkClipSpeed = 0.9f, RunClipSpeed = 4.0f, TurnRate = 130f,
                AlertDistance = 10f, FleeDistance = 5f, Separation = 1.5f, SleepsAtNight = true, MaxSlope = 0.8f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning"), Hold(Pose.Eat, "isEating"), Chain(Pose.Sleep, "isSleeping", 1.6f) },
            };
            rows[(int)Species.Pig] = new SpeciesInfo
            {
                Id = Species.Pig, Name = "pig", Prefabs = new[] { "Pig" },
                Controller = "CTL_Pig", IdleState = "Idle", TargetHeight = 0.75f, Style = GroupStyle.Solitary,
                WalkSpeed = 0.6f, RunSpeed = 2.8f, WalkClipSpeed = 0.7f, RunClipSpeed = 3.0f, TurnRate = 90f,
                Separation = 1.4f, SleepsAtNight = true, MaxSlope = 0.35f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning"), Hold(Pose.Eat, "isEating"), Chain(Pose.Sleep, "isSleeping", 1.6f) },
            };
            rows[(int)Species.Horse] = new SpeciesInfo
            {
                Id = Species.Horse, Name = "horse", Prefabs = new[] { "Horse_Brown", "Horse_Grey", "Horse_Black" },
                Controller = "CTL_Horse", IdleState = "Idle", TargetHeight = 1.9f, Style = GroupStyle.Herd,
                WalkSpeed = 1.3f, RunSpeed = 6.5f, WalkClipSpeed = 1.5f, RunClipSpeed = 7.5f, TurnRate = 80f,
                PersonalSpace = 2.2f, Separation = 3f, SleepsAtNight = true, MaxSlope = 0.4f,
                // Leaving Sleep waits for the loop to wrap (exit time 0) before the get-up clip.
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning"), Hold(Pose.Eat, "isEating"), Chain(Pose.Sleep, "isSleeping", 3.2f),
                    Hold(Pose.Display, "isAttacking", 0.6f) },
                Sounds = new[] { Snd("SFX_Horse_Whinny", 0.5f, 75f), Snd("SFX_Horse_Foot", 0.22f, 12f, Pose.Walk), Snd("SFX_Eating_Grass", 0.2f, 16f, Pose.Eat) },
            };
            rows[(int)Species.Hen] = new SpeciesInfo
            {
                Id = Species.Hen, Name = "hen", Prefabs = new[] { "Hen" },
                Controller = "CTL_Rooster", IdleState = "Idle", TargetHeight = 0.4f, Style = GroupStyle.Flock,
                WalkSpeed = 0.45f, RunSpeed = 2.4f, WalkClipSpeed = 0.5f, RunClipSpeed = 2.4f, TurnRate = 220f,
                PersonalSpace = 1.7f, Separation = 0.6f, SleepsAtNight = true, MaxSlope = 0.5f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning"), Hold(Pose.Eat, "isEating"), Hold(Pose.Idle2, "isIdle2"),
                    Chain(Pose.Sleep, "isSleeping", 2.0f) },
            };
            rows[(int)Species.Rooster] = new SpeciesInfo
            {
                Id = Species.Rooster, Name = "rooster", Prefabs = new[] { "Rooster" },
                Controller = "CTL_Rooster", IdleState = "Idle", TargetHeight = 0.5f, Style = GroupStyle.Solitary,
                WalkSpeed = 0.5f, RunSpeed = 2.5f, WalkClipSpeed = 0.55f, RunClipSpeed = 2.5f, TurnRate = 220f,
                PersonalSpace = 1.7f, Separation = 0.8f, SleepsAtNight = true, MaxSlope = 0.5f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning"), Hold(Pose.Eat, "isEating"), Hold(Pose.Idle2, "isIdle2"),
                    Hold(Pose.Alert, "isIdle2"), Chain(Pose.Sleep, "isSleeping", 2.0f) },
            };
            rows[(int)Species.Chick] = new SpeciesInfo
            {
                Id = Species.Chick, Name = "chick", Prefabs = new[] { "Chick" },
                Controller = "CTL_Chick", IdleState = "Idle 0", TargetHeight = 0.12f, Style = GroupStyle.Flock,
                WalkSpeed = 0.35f, RunSpeed = 1.6f, WalkClipSpeed = 0.35f, RunClipSpeed = 1.6f, TurnRate = 260f,
                PersonalSpace = 1.4f, Separation = 0.25f, SleepsAtNight = true, MaxSlope = 0.5f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning"), Hold(Pose.Eat, "isEating"), Hold(Pose.Idle2, "isIdle2"),
                    Chain(Pose.Sleep, "isSleeping", 1.0f) },
            };
            // The goose controller has no flying state (only the clip): geese never take off here.
            rows[(int)Species.Goose] = new SpeciesInfo
            {
                Id = Species.Goose, Name = "goose", Prefabs = new[] { "Goose" },
                Controller = "CTL_Goose", IdleState = "Idle", TargetHeight = 0.75f, Style = GroupStyle.Flock,
                WalkSpeed = 0.55f, RunSpeed = 2.4f, WalkClipSpeed = 0.55f, RunClipSpeed = 2.4f, TurnRate = 160f,
                AlertDistance = 8f, FleeDistance = 3.5f, PersonalSpace = 1.8f, Separation = 0.9f, SleepsAtNight = true, MaxSlope = 0.5f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning"), Hold(Pose.Eat, "isEating"), Hold(Pose.Idle2, "isIdle2"),
                    Hold(Pose.Alert, "isIdle3"), Chain(Pose.Sleep, "isSleeping", 1.5f) },
            };
            // Dogs: Sit → Stand → Idle chain; Bark returns to Idle by exit time only, so it is a pulse.
            rows[(int)Species.Retriever] = new SpeciesInfo
            {
                Id = Species.Retriever, Name = "golden retriever", Prefabs = new[] { "Dog_GoldenRetriever" },
                Controller = "CTL_Dog_GoldenRetriever", IdleState = "Idle", TargetHeight = 0.6f, Style = GroupStyle.Solitary,
                WalkSpeed = 1.2f, RunSpeed = 4.8f, WalkClipSpeed = 1.3f, RunClipSpeed = 5.0f, TurnRate = 200f,
                PersonalSpace = 1.4f, Separation = 1.2f, MaxSlope = 0.6f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning", 0.6f), Chain(Pose.Sit, "isSitting", 1.2f),
                    Pulse(Pose.Bark, "isBarking", 0.15f, 0.9f) },
                Sounds = new[] { Snd("SFX_Dog_Bark_Sigle", 0.5f, 0f, Pose.Bark), Snd("SFX_Dog_Whining", 0.25f, 120f, Pose.Sit) },
            };
            rows[(int)Species.GreatDane] = new SpeciesInfo
            {
                Id = Species.GreatDane, Name = "great dane", Prefabs = new[] { "Dog_GreatDane", "Dog_GreatDane_Black" },
                Controller = "CTL_Dog_GreatDane", IdleState = "Idle", TargetHeight = 0.8f, Style = GroupStyle.Solitary,
                WalkSpeed = 1.3f, RunSpeed = 5.2f, WalkClipSpeed = 1.4f, RunClipSpeed = 5.5f, TurnRate = 180f,
                PersonalSpace = 1.5f, Separation = 1.4f, MaxSlope = 0.6f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning"), Chain(Pose.Sit, "isSitting", 1.6f),
                    Pulse(Pose.Bark, "isBarking", 0.15f, 0.9f) },
                Sounds = new[] { Snd("SFX_Dog_Bark_Sigle", 0.55f, 0f, Pose.Bark) },
            };
            // Cat: entering sleep waits for the idle loop (exit time 0.925); it has no eating state.
            rows[(int)Species.Cat] = new SpeciesInfo
            {
                Id = Species.Cat, Name = "cat", Prefabs = new[] { "Cat_Orange", "Cat_Black", "Cat_White" },
                Controller = "CTL_Cat", IdleState = "Idle", TargetHeight = 0.3f, Style = GroupStyle.Solitary,
                WalkSpeed = 0.55f, RunSpeed = 3.0f, WalkClipSpeed = 0.6f, RunClipSpeed = 3.0f, TurnRate = 200f,
                AlertDistance = 4f, FleeDistance = 1.8f, Separation = 0.6f, SleepsAtNight = false, MaxSlope = 0.7f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning"), Chain(Pose.Sleep, "isSleeping", 1.6f) },
                Sounds = new[] { Snd("SFX_Cat_Meow", 0.35f, 0f), Snd("SFX_Cat_Purr", 0.22f, 40f, Pose.Sleep) },
            };
            rows[(int)Species.Dove] = new SpeciesInfo
            {
                Id = Species.Dove, Name = "dove", Prefabs = new[] { "Dove_Rock" },
                Controller = "CTL_Dove_Rock", IdleState = "Idle", TargetHeight = 0.22f, Style = GroupStyle.Flock,
                WalkSpeed = 0.3f, RunSpeed = 1.2f, FlySpeed = 6f, WalkClipSpeed = 0.3f, RunClipSpeed = 1.2f, TurnRate = 300f,
                AlertDistance = 6f, FleeDistance = 4f, Separation = 0.5f, Wild = true, SleepsAtNight = false, Flies = true, MaxSlope = 0.7f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning"), Hold(Pose.Fly, "isFlying") },
            };

            // Wild animals.
            rows[(int)Species.Deer] = new SpeciesInfo
            {
                Id = Species.Deer, Name = "deer", Prefabs = new[] { "Deer" },
                // Deer and Deer_Gray share one mesh, which measures taller than long (antlers): 1.75 m overall gives a fallow-deer body.
                Controller = "CTL_Deer", IdleState = "Idle", TargetHeight = 1.75f, Style = GroupStyle.Herd,
                WalkSpeed = 1.1f, RunSpeed = 7f, WalkClipSpeed = 1.2f, RunClipSpeed = 8f, TurnRate = 140f,
                AlertDistance = 55f, FleeDistance = 28f, Separation = 2f, Wild = true, MaxSlope = 0.6f,
                // Run → Idle waits for exit time 0.9 of the run loop.
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning", 0.8f), Hold(Pose.Eat, "isEating") },
                Sounds = new[] { Snd("SFX_Deer", 0.35f, 0f, Pose.Alert) },
            };
            rows[(int)Species.Stag] = new SpeciesInfo
            {
                Id = Species.Stag, Name = "stag", Prefabs = new[] { "Deer_Gray" }, ScaleLike = Species.Deer,
                Controller = "CTL_Deer", IdleState = "Idle", TargetHeight = 1.75f, Style = GroupStyle.Herd,
                WalkSpeed = 1.1f, RunSpeed = 7f, WalkClipSpeed = 1.2f, RunClipSpeed = 8f, TurnRate = 130f,
                AlertDistance = 55f, FleeDistance = 28f, Separation = 2.2f, Wild = true, MaxSlope = 0.6f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning", 0.8f), Hold(Pose.Eat, "isEating"),
                    Hold(Pose.Display, "isAttacking", 0.4f) },
                Sounds = new[] { Snd("SFX_Deer", 0.4f, 0f, Pose.Alert) },
            };
            rows[(int)Species.Fox] = new SpeciesInfo
            {
                Id = Species.Fox, Name = "fox", Prefabs = new[] { "Fox" },
                Controller = "CTL_Fox", IdleState = "Idle", TargetHeight = 0.45f, Style = GroupStyle.Solitary,
                WalkSpeed = 1.2f, RunSpeed = 6f, WalkClipSpeed = 1.2f, RunClipSpeed = 6f, TurnRate = 200f,
                AlertDistance = 40f, FleeDistance = 22f, Separation = 1f, Wild = true, MaxSlope = 0.7f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning") },
            };
            // Rabbit: hop walk is isJumping, sitting up alert is isLookingOut. Its two death states are sticky: never isDead_*.
            rows[(int)Species.Rabbit] = new SpeciesInfo
            {
                Id = Species.Rabbit, Name = "rabbit", Prefabs = new[] { "Rabbit_Brown", "Rabbit_White" },
                Controller = "CTL_Rabbit", IdleState = "Rabbit_Idle", TargetHeight = 0.28f, Style = GroupStyle.Flock,
                WalkSpeed = 0.7f, RunSpeed = 6f, WalkClipSpeed = 0.8f, RunClipSpeed = 6f, TurnRate = 300f,
                AlertDistance = 22f, FleeDistance = 11f, Separation = 0.8f, Wild = true, MaxSlope = 0.7f,
                Poses = new[] { Hold(Pose.Walk, "isJumping"), Hold(Pose.Run, "isRunning"), Hold(Pose.Alert, "isLookingOut") },
            };
            rows[(int)Species.Squirrel] = new SpeciesInfo
            {
                Id = Species.Squirrel, Name = "squirrel", Prefabs = new[] { "Squirrel" },
                Controller = "CTL_Squirrel", IdleState = "idle", TargetHeight = 0.2f, Style = GroupStyle.Pair,
                WalkSpeed = 0.6f, RunSpeed = 3.5f, WalkClipSpeed = 0.6f, RunClipSpeed = 3.5f, TurnRate = 320f,
                AlertDistance = 10f, FleeDistance = 6f, Separation = 0.6f, Wild = true, MaxSlope = 0.9f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning") },
            };
            rows[(int)Species.Boar] = new SpeciesInfo
            {
                Id = Species.Boar, Name = "boar", Prefabs = new[] { "Boar" },
                Controller = "CTL_Boar", IdleState = "Idle", TargetHeight = 0.8f, Style = GroupStyle.Herd,
                WalkSpeed = 0.9f, RunSpeed = 4.8f, WalkClipSpeed = 1.0f, RunClipSpeed = 5f, TurnRate = 110f,
                AlertDistance = 35f, FleeDistance = 18f, Separation = 1.5f, Wild = true, MaxSlope = 0.6f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning", 0.8f), Hold(Pose.Alert, "isLooking") },
            };
            rows[(int)Species.Wolf] = new SpeciesInfo
            {
                Id = Species.Wolf, Name = "wolf", Prefabs = new[] { "Wolf" },
                Controller = "CTL_Wolf", IdleState = "Idle", TargetHeight = 0.8f, Style = GroupStyle.Solitary,
                WalkSpeed = 1.3f, RunSpeed = 5.5f, WalkClipSpeed = 1.4f, RunClipSpeed = 6f, TurnRate = 150f,
                AlertDistance = 80f, FleeDistance = 60f, Separation = 1.5f, Wild = true, Nocturnal = true, NightOnly = true, MaxSlope = 0.7f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning"), Hold(Pose.Howl, "isHowling") },
                Sounds = new[] { Snd("SFX_Wolf_Howl", 0.7f, 0f, Pose.Howl) },
            };
            rows[(int)Species.Eagle] = new SpeciesInfo
            {
                Id = Species.Eagle, Name = "eagle", Prefabs = new[] { "Eagle" },
                Controller = "CTL_Eagle", IdleState = "Idle", TargetHeight = 0.65f, Style = GroupStyle.Solitary,
                WalkSpeed = 0.5f, RunSpeed = 1.5f, FlySpeed = 9f, WalkClipSpeed = 0.5f, RunClipSpeed = 1.5f, TurnRate = 40f,
                Wild = true, Flies = true, MaxSlope = 2f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Fly, "isFlying") },
            };
            rows[(int)Species.Beaver] = new SpeciesInfo
            {
                Id = Species.Beaver, Name = "beaver", Prefabs = new[] { "Beaver" },
                Controller = "CTL_Beaver", IdleState = "idle", TargetHeight = 0.3f, Style = GroupStyle.Solitary,
                WalkSpeed = 0.4f, RunSpeed = 2f, WalkClipSpeed = 0.45f, RunClipSpeed = 2f, TurnRate = 160f,
                AlertDistance = 14f, FleeDistance = 7f, Separation = 0.8f, Wild = true, MaxSlope = 0.6f,
                Poses = new[] { Hold(Pose.Walk, "isWalking"), Hold(Pose.Run, "isRunning") },
            };

            foreach (SpeciesInfo s in rows) IndexBools(s);
            return rows;
        }

        private static void IndexBools(SpeciesInfo s)
        {
            var names = new List<string>();
            for (int i = 0; i < s.Poses.Length; i++)
            {
                int index = names.IndexOf(s.Poses[i].Bool);
                if (index < 0)
                {
                    index = names.Count;
                    names.Add(s.Poses[i].Bool);
                }

                s.Poses[i].BoolIndex = index;
            }

            s.Bools = names.ToArray();
        }
    }
}
