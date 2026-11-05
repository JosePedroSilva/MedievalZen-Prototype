using System;
using System.Collections.Generic;

namespace MedievalZen.Sound
{
    /// <summary>What an ambience clip is for. Beds first (2D, weighted by <see cref="AmbienceModel"/>), then positioned loops, then spot sounds.</summary>
    public enum AmbienceRole
    {
        /// <summary>Open country by day: birds, insects, a breath of air.</summary>
        OpenDay = 0,

        /// <summary>Under trees by day: woodland birds.</summary>
        ForestDay = 1,

        /// <summary>Deep inside a wood (cover above 0.75): a very subtle calm layer.</summary>
        DeepForest = 2,

        /// <summary>The pass's pines.</summary>
        Pines = 3,

        /// <summary>Night: crickets, an owl now and then.</summary>
        Night = 4,

        /// <summary>A light wind in the open.</summary>
        WindLight = 5,

        /// <summary>The open, high wind of the downs and the pass.</summary>
        WindHigh = 6,

        /// <summary>A strong wind in rough weather.</summary>
        WindStrong = 7,

        /// <summary>Farmland by day.</summary>
        Farm = 8,

        /// <summary>Rain (synthesised: there is no open-air rain in the packs; no clip).</summary>
        Rain = 9,

        /// <summary>A stream near the listener (positioned, slides along the water).</summary>
        Stream = 20,

        /// <summary>A lake's shore.</summary>
        Lake = 21,

        /// <summary>Farm animals at a hamlet or a farm inn.</summary>
        FarmAnimals = 22,

        /// <summary>A village market's murmur (people: may hold modern sounds, kept low, daytime only).</summary>
        Market = 23,

        /// <summary>An inn yard's murmur (people: as <see cref="Market"/>).</summary>
        InnYard = 24,

        /// <summary>A windmill landmark turning.</summary>
        Windmill = 25,

        /// <summary>The creak of a kirk, abbey, tower or keep in the wind.</summary>
        Church = 26,

        /// <summary>A ring of standing stones.</summary>
        Stones = 27,

        /// <summary>A group of birds somewhere near (spot one-shot).</summary>
        SpotBirds = 40,

        /// <summary>A pigeon near a settlement (spot one-shot).</summary>
        SpotPigeon = 41,

        /// <summary>A gull over lake country, rare (spot one-shot).</summary>
        SpotGull = 42,
    }

    /// <summary>How a clip is played and imported.</summary>
    public enum AmbienceCategory
    {
        /// <summary>A 2D stereo loop, crossfading into itself.</summary>
        Bed = 0,

        /// <summary>A 3D mono loop at a point in the world.</summary>
        Positioned = 1,

        /// <summary>A 3D mono loop of people: its own category because it may contain modern sounds; low, daytime only.</summary>
        People = 2,

        /// <summary>A 3D mono one-shot.</summary>
        Spot = 3,
    }

    /// <summary>One clip of the catalog.</summary>
    public readonly struct AmbienceClipSpec
    {
        /// <summary>What it is for.</summary>
        public readonly AmbienceRole Role;

        /// <summary>Path relative to <see cref="AmbienceCatalog.SoundsFolder"/>, with extension.</summary>
        public readonly string Path;

        /// <summary>How it is played.</summary>
        public readonly AmbienceCategory Category;

        /// <summary>Level relative to its category's target (dB): "low" clips sit below it.</summary>
        public readonly float LevelDb;

        public AmbienceClipSpec(AmbienceRole role, string path, AmbienceCategory category, float levelDb)
        {
            Role = role;
            Path = path;
            Category = category;
            LevelDb = levelDb;
        }

        /// <summary>File name without folder or extension.</summary>
        public string Name
        {
            get
            {
                int slash = Path.LastIndexOf('/');
                string n = slash >= 0 ? Path.Substring(slash + 1) : Path;
                return n.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? n.Substring(0, n.Length - 4) : n;
            }
        }

        /// <summary>The project path of the file.</summary>
        public string AssetPath => AmbienceCatalog.SoundsFolder + "/" + Path;
    }

    /// <summary>
    /// The always-on ambience's clips as data (2026-10-06), pure: the palette menu measures and imports them, the director plays them. The
    /// sound packs under <c>Assets/Sounds</c> are third-party and only referenced. Every ambience loop of these packs is 60 s stereo
    /// 44.1 kHz 16-bit. Bump <see cref="Version"/> whenever the table changes: the palette rebuilds itself on editor load.
    /// </summary>
    public static class AmbienceCatalog
    {
        /// <summary>Version of the table; a palette built from an older one is rebuilt.</summary>
        public const int Version = 2;   // 2 (2026-10-06): mono clips imported without Unity's normalise and trimmed from their mono level

        /// <summary>Project folder of the third-party sound packs.</summary>
        public const string SoundsFolder = "Assets/Sounds";

        private const string V4 = "Environmental Ambiences Sound FX Pack Vol. 4/Environmental Ambiences Sound FX Pack Vol. 4/";
        private const string V5 = "Environmental Ambiences Sound FX Pack Vol. 5/Environmental Ambiences Sound FX Pack Vol. 5/";
        private const string E2 = "Environments 2/Environments 2/";
        private const string AN = "Animals Sound FX Pack/Non Loops/";

        private static AmbienceClipSpec Bed(AmbienceRole role, string path, float levelDb = 0f) => new AmbienceClipSpec(role, path + ".wav", AmbienceCategory.Bed, levelDb);
        private static AmbienceClipSpec At(AmbienceRole role, string path, float levelDb = 0f) => new AmbienceClipSpec(role, path + ".wav", AmbienceCategory.Positioned, levelDb);
        private static AmbienceClipSpec People(AmbienceRole role, string path, float levelDb) => new AmbienceClipSpec(role, path + ".wav", AmbienceCategory.People, levelDb);
        private static AmbienceClipSpec Spot(AmbienceRole role, string path, float levelDb = 0f) => new AmbienceClipSpec(role, path + ".wav", AmbienceCategory.Spot, levelDb);

        /// <summary>Every clip.</summary>
        public static readonly AmbienceClipSpec[] All =
        {
            // Beds (2D). Level offsets are relative to SoundMix.BedTargetDb; the model's own weights (deep forest −8 dB, wind) come on top.
            Bed(AmbienceRole.OpenDay, V5 + "Seasons/Summer"),
            Bed(AmbienceRole.OpenDay, V5 + "Seasons/Spring Wind"),
            Bed(AmbienceRole.OpenDay, V4 + "Nature/Grass Rustling"),
            Bed(AmbienceRole.ForestDay, V5 + "Biome/Forest Trees Birds"),
            Bed(AmbienceRole.DeepForest, E2 + "Fairy Forest/Fairy Forest Calm Loop A"),
            Bed(AmbienceRole.Pines, V5 + "Biome/Pine Forest"),
            Bed(AmbienceRole.Night, V5 + "Biome/Woods Night"),
            Bed(AmbienceRole.WindLight, V5 + "Biome/Wind"),
            Bed(AmbienceRole.WindLight, V4 + "Nature/Windy Place 001"),
            Bed(AmbienceRole.WindLight, V4 + "Nature/Windy Place 002"),
            Bed(AmbienceRole.WindHigh, V5 + "Biome/Windy Mountain Plains"),
            Bed(AmbienceRole.WindHigh, V5 + "Biome/Mountain Wind Gusts"),
            Bed(AmbienceRole.WindStrong, V5 + "Biome/Winter Strong Wind"),
            Bed(AmbienceRole.WindStrong, V5 + "Biome/Winter Cold Wind"),
            Bed(AmbienceRole.Farm, E2 + "Farm/Farm Windy Loop"),

            // Positioned loops (3D, mono). Offsets relative to SoundMix.EmitterTargetDb at the reference distance.
            At(AmbienceRole.Stream, V5 + "Biome/River Close A"),
            At(AmbienceRole.Stream, V5 + "Biome/River Close B"),
            At(AmbienceRole.Stream, V5 + "Biome/River Close Streaming"),
            At(AmbienceRole.Stream, E2 + "Forest Water/Forest Secluded River Loop"),
            At(AmbienceRole.Lake, V5 + "Seasons/Summer Lake"),
            At(AmbienceRole.Lake, E2 + "Forest Water/Forest Big Pond Loop"),
            At(AmbienceRole.Lake, E2 + "Forest Water/Forest Small Pond Loop"),
            At(AmbienceRole.FarmAnimals, E2 + "Farm/Farm Animals Loop A", -4f),
            At(AmbienceRole.FarmAnimals, E2 + "Farm/Farm Animals Loop B", -4f),
            People(AmbienceRole.Market, V5 + "Public/Busy Market", -9f),
            People(AmbienceRole.InnYard, V5 + "Public/Populated Outdoor Restaurant", -9f),
            At(AmbienceRole.Windmill, E2 + "Farm/Farm Windmill Loop", -2f),
            At(AmbienceRole.Church, V5 + "Biome/Windy Church Creaking", -6f),
            At(AmbienceRole.Stones, V4 + "Fantasy/Sacred Place 001", -10f),

            // Spot sounds (3D one-shots), measured on their loudest second.
            Spot(AmbienceRole.SpotBirds, AN + "Birds General/Birds Group A", -3f),
            Spot(AmbienceRole.SpotBirds, AN + "Birds General/Birds Group B", -3f),
            Spot(AmbienceRole.SpotBirds, AN + "Birds General/Birds Group C", -3f),
            Spot(AmbienceRole.SpotBirds, AN + "Birds General/Birds Group D", -3f),
            Spot(AmbienceRole.SpotBirds, AN + "Birds General/Birds Group E", -3f),
            Spot(AmbienceRole.SpotBirds, AN + "Birds General/Birds Group F", -3f),
            Spot(AmbienceRole.SpotBirds, AN + "Birds General/Birds Group G", -3f),
            Spot(AmbienceRole.SpotBirds, AN + "Birds General/Birds Group H", -3f),
            Spot(AmbienceRole.SpotPigeon, AN + "Birds Single/Pigeon", -6f),
            Spot(AmbienceRole.SpotGull, AN + "Birds Single/Seagull Call A", -8f),
            Spot(AmbienceRole.SpotGull, AN + "Birds Single/Seagull Call B", -8f),
            Spot(AmbienceRole.SpotGull, AN + "Birds Single/Seagull Call C", -8f),
        };

        /// <summary>The bed roles the model weights, in order (index = <see cref="BedIndex"/>).</summary>
        public static readonly AmbienceRole[] Beds =
        {
            AmbienceRole.OpenDay, AmbienceRole.ForestDay, AmbienceRole.DeepForest, AmbienceRole.Pines, AmbienceRole.Night,
            AmbienceRole.WindLight, AmbienceRole.WindHigh, AmbienceRole.WindStrong, AmbienceRole.Farm, AmbienceRole.Rain,
        };

        /// <summary>Number of bed roles.</summary>
        public const int BedCount = 10;

        /// <summary>Index of a bed role in <see cref="Beds"/>, or −1.</summary>
        public static int BedIndex(AmbienceRole role) => (int)role >= 0 && (int)role < BedCount ? (int)role : -1;

        /// <summary>True for the bed roles.</summary>
        public static bool IsBed(AmbienceRole role) => BedIndex(role) >= 0;

        /// <summary>True for a bed played from clips (rain is synthesised).</summary>
        public static bool HasClips(AmbienceRole role) => role != AmbienceRole.Rain;

        /// <summary>The clips of a role.</summary>
        public static List<AmbienceClipSpec> Of(AmbienceRole role)
        {
            var list = new List<AmbienceClipSpec>();
            foreach (AmbienceClipSpec c in All)
            {
                if (c.Role == role) list.Add(c);
            }

            return list;
        }

        /// <summary>The loudness target (dBFS RMS) of a clip, its offset included: what the palette trims it to.</summary>
        public static float TargetDb(in AmbienceClipSpec c) =>
            (c.Category == AmbienceCategory.Bed ? SoundMix.BedTargetDb : SoundMix.EmitterTargetDb) + c.LevelDb;
    }
}
