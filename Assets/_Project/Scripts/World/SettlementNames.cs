using System.Collections.Generic;

namespace MedievalZen.World
{
    /// <summary>Deterministic medieval-English place and inn names from syllable tables; unique per instance.</summary>
    public sealed class SettlementNames
    {
        private static readonly string[] Heads =
        {
            "Ash", "Wex", "Brin", "Thorn", "Oak", "Els", "Mar", "Hol", "Stan", "Red", "Ald", "Brack", "Wick", "Hart",
            "Kel", "Lang", "Pen", "Ros", "Sel", "Whit", "Bram", "Cold", "Fern", "Gold", "Har", "Nether", "Kings",
            "Mill", "Wood", "Bar", "Crow", "Dun", "Ey", "Fox", "Glen", "Hazel", "Ling", "Rye", "Sand", "Wil",
        };

        private static readonly string[] Middles = { "", "", "", "", "le", "ing", "er", "den", "s" };

        private static readonly string[] Tails =
        {
            "ford", "ley", "moor", "ton", "by", "wick", "field", "bury", "ham", "stead", "dale", "combe", "hurst",
            "well", "brook", "thorpe", "worth", "mere", "cott", "bridge",
        };

        private static readonly string[] InnAdjectives =
        {
            "Crooked", "Sleeping", "Golden", "Wandering", "Drowsy", "Merry", "Rusty", "Silver", "Laughing", "Old",
            "Green", "Tired", "Hollow", "Painted", "Patient", "Muddy",
        };

        private static readonly string[] InnNouns =
        {
            "Pike", "Fox", "Goose", "Lantern", "Plough", "Hare", "Boar", "Kettle", "Stag", "Heron", "Barrel",
            "Wheel", "Crown", "Oak", "Pilgrim", "Badger",
        };

        private static readonly string[] PeakTails = { "fell", "pike", "tor", "crag", "howe", "knott", " Beacon", " Edge", "scar" };

        private static readonly string[] LakeForms = { "{0}mere", "{0} Water", "Lake {0}", "{0} Tarn", "{0} Pool" };

        private readonly System.Random rng;
        private readonly HashSet<string> used = new HashSet<string>();

        /// <summary>Creates a name source seeded with <paramref name="seed"/>.</summary>
        public SettlementNames(int seed)
        {
            rng = new System.Random(seed);
        }

        /// <summary>Next unique place name, e.g. "Ashford", "Wexley", "Brindlemoor".</summary>
        public string NextPlace()
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                string name = Heads[rng.Next(Heads.Length)] + Middles[rng.Next(Middles.Length)] + Tails[rng.Next(Tails.Length)];
                if (used.Add(name)) return name;
            }

            return Unique("Newton");
        }

        /// <summary>Next unique inn name, e.g. "The Crooked Pike".</summary>
        public string NextInn()
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                string name = "The " + InnAdjectives[rng.Next(InnAdjectives.Length)] + " " + InnNouns[rng.Next(InnNouns.Length)];
                if (used.Add(name)) return name;
            }

            return Unique("The Wayside Inn");
        }

        /// <summary>Next unique hill/peak name, e.g. "Ashfell", "Thornpike", "Crow Beacon".</summary>
        public string NextPeak()
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                string name = Heads[rng.Next(Heads.Length)] + PeakTails[rng.Next(PeakTails.Length)];
                if (used.Add(name)) return name;
            }

            return Unique("High Fell");
        }

        /// <summary>Next unique lake name, e.g. "Elsmere", "Lake Hazel", "Brack Water".</summary>
        public string NextLake()
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                string name = string.Format(LakeForms[rng.Next(LakeForms.Length)], Heads[rng.Next(Heads.Length)]);
                if (used.Add(name)) return name;
            }

            return Unique("Still Water");
        }

        private static readonly string[] MountainInns =
        {
            "The High Pass Inn", "Cloudrest", "The Drover's Rest", "The Eagle's Rest", "The Shepherd's Bothy", "The Last Hearth",
            "The Windy Gap", "The Fell Refuge", "Stonecot", "The Snowgate Inn", "The Cairn Inn", "The Wayfarer's Refuge",
        };

        private static readonly string[] PassForms = { "{0} Pass", "{0} Gap", "{0} Col", "{0} Hause", "{0} Neck" };

        private static readonly string[] ViewpointForms = { "{0} Lookout", "{0} Stones", "{0} Brow", "The {0} View", "{0} Watch" };

        /// <summary>Next unique name for an inn high in the mountains, e.g. "Cloudrest", "The Drover's Rest".</summary>
        public string NextMountainInn()
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                string name = MountainInns[rng.Next(MountainInns.Length)];
                if (used.Add(name)) return name;
            }

            return Unique("The Mountain Inn");
        }

        /// <summary>
        /// Next unique pass name. When <paramref name="nearPeak"/> is given (the named summit beside the col) the pass is
        /// named after it, e.g. "Oakpike Pass"; otherwise from the syllable tables, e.g. "Crow Gap".
        /// </summary>
        public string NextPass(string nearPeak = null)
        {
            if (!string.IsNullOrEmpty(nearPeak) && used.Add(nearPeak + " Pass")) return nearPeak + " Pass";
            for (int attempt = 0; attempt < 200; attempt++)
            {
                string name = string.Format(PassForms[rng.Next(PassForms.Length)], Heads[rng.Next(Heads.Length)]);
                if (used.Add(name)) return name;
            }

            return Unique("High Pass");
        }

        /// <summary>Next unique viewpoint name, e.g. "Hart Lookout", "The Crow View".</summary>
        public string NextViewpoint()
        {
            for (int attempt = 0; attempt < 200; attempt++)
            {
                string name = string.Format(ViewpointForms[rng.Next(ViewpointForms.Length)], Heads[rng.Next(Heads.Length)]);
                if (used.Add(name)) return name;
            }

            return Unique("The Lookout");
        }

        private string Unique(string stem)
        {
            string name = stem;
            for (int i = 2; !used.Add(name); i++) name = stem + " " + i;
            return name;
        }

        // ------------------------------------------------------------------ indexed names (endless world)
        // Pure functions of (seed, index), safe from any thread: index k of a kind maps through a seeded bijection of the kind's
        // name table, so consecutive indices never repeat a name within the table size (256 inns, 4800 places, ...).

        private static readonly string[] PlaceMiddles = { "", "le", "ing", "er", "den", "s" };

        /// <summary>The <paramref name="index"/>-th roadside inn name of a world, e.g. "The Sleeping Heron".</summary>
        public static string InnAt(int seed, long index)
        {
            int k = Permute(seed, 1, index, InnAdjectives.Length * InnNouns.Length);
            return "The " + InnAdjectives[k % InnAdjectives.Length] + " " + InnNouns[k / InnAdjectives.Length];
        }

        /// <summary>The <paramref name="index"/>-th mountain refuge name of a world, e.g. "Cloudrest".</summary>
        public static string MountainInnAt(int seed, long index) => MountainInns[Permute(seed, 2, index, MountainInns.Length)];

        /// <summary>The <paramref name="index"/>-th place (village, region) name of a world, e.g. "Brindlemoor".</summary>
        public static string PlaceAt(int seed, long index)
        {
            int k = Permute(seed, 3, index, Heads.Length * PlaceMiddles.Length * Tails.Length);
            int head = k % Heads.Length;
            k /= Heads.Length;
            return Heads[head] + PlaceMiddles[k % PlaceMiddles.Length] + Tails[k / PlaceMiddles.Length];
        }

        /// <summary>The <paramref name="index"/>-th pass name of a world, e.g. "Crow Gap".</summary>
        public static string PassAt(int seed, long index)
        {
            int k = Permute(seed, 4, index, PassForms.Length * Heads.Length);
            return string.Format(PassForms[k % PassForms.Length], Heads[k / PassForms.Length]);
        }

        /// <summary>The <paramref name="index"/>-th lake name of a world, e.g. "Elsmere".</summary>
        public static string LakeAt(int seed, long index)
        {
            int k = Permute(seed, 5, index, LakeForms.Length * Heads.Length);
            return string.Format(LakeForms[k % LakeForms.Length], Heads[k / LakeForms.Length]);
        }

        /// <summary>The <paramref name="index"/>-th viewpoint name of a world, e.g. "Hart Lookout".</summary>
        public static string ViewpointAt(int seed, long index)
        {
            int k = Permute(seed, 6, index, ViewpointForms.Length * Heads.Length);
            return string.Format(ViewpointForms[k % ViewpointForms.Length], Heads[k / ViewpointForms.Length]);
        }

        /// <summary>The <paramref name="index"/>-th peak name of a world, e.g. "Thornpike".</summary>
        public static string PeakAt(int seed, long index)
        {
            int k = Permute(seed, 7, index, PeakTails.Length * Heads.Length);
            return Heads[k / PeakTails.Length] + PeakTails[k % PeakTails.Length];
        }

        /// <summary>Seeded bijection of Z/n applied to <paramref name="index"/> (an affine map with a multiplier coprime to n).</summary>
        private static int Permute(int seed, int kind, long index, int n)
        {
            unchecked
            {
                ulong h = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)kind * 0xC2B2AE3D27D4EB4FUL;
                h ^= h >> 29;
                h *= 0xBF58476D1CE4E5B9UL;
                h ^= h >> 32;
                long a = 1 + (long)(h % (ulong)n);
                while (Gcd(a, n) != 1) a++;
                long b = (long)((h >> 20) % (ulong)n);
                long m = (index % n + n) % n;
                return (int)((a * m + b) % n);
            }
        }

        private static long Gcd(long a, long b)
        {
            while (b != 0)
            {
                long t = a % b;
                a = b;
                b = t;
            }

            return a;
        }
    }
}
