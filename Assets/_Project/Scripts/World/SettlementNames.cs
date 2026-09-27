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

        private string Unique(string stem)
        {
            string name = stem;
            for (int i = 2; !used.Add(name); i++) name = stem + " " + i;
            return name;
        }
    }
}
