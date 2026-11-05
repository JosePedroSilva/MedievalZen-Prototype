using System;
using System.Collections.Generic;

namespace MedievalZen.People
{
    /// <summary>What a person does in a place.</summary>
    public enum PersonRole
    {
        /// <summary>Keeps the inn: the porch, the door, the gate, a bench.</summary>
        Innkeeper = 0,

        /// <summary>Lives in a house: door, garden gate, the well, a bench, the green.</summary>
        Peasant = 1,

        /// <summary>Works at the smithy's forge.</summary>
        Smith = 2,

        /// <summary>Minds a market stall.</summary>
        Merchant = 3,

        /// <summary>A traveller resting at the inn: a bench, the gate.</summary>
        Guest = 4,

        /// <summary>Looks after the stables.</summary>
        Groom = 5,

        /// <summary>A monk or a nun at a chapel near the place.</summary>
        Religious = 6,
    }

    /// <summary>One Fantasy Kingdom character (prefab name and which clip set its posture takes).</summary>
    public readonly struct PersonCharacter
    {
        /// <summary>Creates a character entry.</summary>
        public PersonCharacter(string name, bool feminine)
        {
            Name = name;
            Feminine = feminine;
        }

        /// <summary>Synty prefab name (<c>Characters/SM_Chr_*_01</c>); the people palette's variant is named after it.</summary>
        public string Name { get; }

        /// <summary>True for the feminine idle and walk clips.</summary>
        public bool Feminine { get; }
    }

    /// <summary>
    /// The people of the endless road's inns and villages (2026-10-05): which Fantasy Kingdom characters play which role (the catalogue's
    /// fitting ones: peasants, the bartender, smiths, the merchant, a rider, the hermit, a monk, a nun and a priest; a soldier only now and
    /// then as a guest; no mages, headsmen or court), the randomness streams (1301 cast, 1302 per person) and the caps. Pure.
    /// </summary>
    public static class PeopleCatalog
    {
        /// <summary>Randomness stream of a place's cast.</summary>
        public const int CastStream = 1301;

        /// <summary>Randomness stream of one person's day (start stop, waits).</summary>
        public const int PersonStream = 1302;

        /// <summary>Most people a place has.</summary>
        public const int MaxPerPlace = 8;

        /// <summary>Most people shown at once (the manager's hard cap).</summary>
        public const int MaxActive = 12;

        /// <summary>People are shown within this distance of the traveller, metres.</summary>
        public const float ShowWithin = 200f;

        /// <summary>Animators run within this distance, metres.</summary>
        public const float AnimateWithin = 80f;

        /// <summary>Folder of the first-party character prefabs built by <c>MedievalZen/People/Build People Palette</c>.</summary>
        public const string PrefabFolder = "Assets/_Project/Prefabs/People";

        /// <summary>Synty folder of the characters.</summary>
        public const string SourceFolder = "Assets/Synty/PolygonFantasyKingdom/Prefabs/Characters";

        /// <summary>Every character the people palette builds.</summary>
        public static readonly PersonCharacter[] All =
        {
            new PersonCharacter("SM_Chr_Peasant_Male_01", false),
            new PersonCharacter("SM_Chr_Peasant_Female_01", true),
            new PersonCharacter("SM_Chr_Bartender_01", false),
            new PersonCharacter("SM_Chr_Blacksmith_Male_01", false),
            new PersonCharacter("SM_Chr_Blacksmith_Female_01", true),
            new PersonCharacter("SM_Chr_Merchant_01", false),
            new PersonCharacter("SM_Chr_Rider_01", false),
            new PersonCharacter("SM_Chr_Hermit_01", false),
            new PersonCharacter("SM_Chr_Monk_01", false),
            new PersonCharacter("SM_Chr_Nun_01", true),
            new PersonCharacter("SM_Chr_Priest_01", false),
            new PersonCharacter("SM_Chr_Soldier_Male_01", false),
        };

        private static readonly Dictionary<PersonRole, string[]> Casting = new Dictionary<PersonRole, string[]>
        {
            [PersonRole.Innkeeper] = new[] { "SM_Chr_Bartender_01", "SM_Chr_Bartender_01", "SM_Chr_Peasant_Female_01" },
            [PersonRole.Peasant] = new[] { "SM_Chr_Peasant_Male_01", "SM_Chr_Peasant_Female_01", "SM_Chr_Peasant_Male_01", "SM_Chr_Peasant_Female_01", "SM_Chr_Hermit_01" },
            [PersonRole.Smith] = new[] { "SM_Chr_Blacksmith_Male_01", "SM_Chr_Blacksmith_Male_01", "SM_Chr_Blacksmith_Female_01" },
            [PersonRole.Merchant] = new[] { "SM_Chr_Merchant_01", "SM_Chr_Peasant_Female_01" },
            [PersonRole.Guest] = new[] { "SM_Chr_Rider_01", "SM_Chr_Merchant_01", "SM_Chr_Hermit_01", "SM_Chr_Peasant_Male_01", "SM_Chr_Rider_01", "SM_Chr_Soldier_Male_01" },
            [PersonRole.Groom] = new[] { "SM_Chr_Peasant_Male_01", "SM_Chr_Rider_01" },
            [PersonRole.Religious] = new[] { "SM_Chr_Monk_01", "SM_Chr_Nun_01", "SM_Chr_Priest_01" },
        };

        /// <summary>A character for <paramref name="role"/>, picked with <paramref name="rng"/>.</summary>
        public static PersonCharacter Pick(PersonRole role, Random rng)
        {
            string[] names = Casting[role];
            string name = names[rng.Next(names.Length)];
            return Find(name);
        }

        /// <summary>The catalogue entry of a prefab name (feminine false when unknown).</summary>
        public static PersonCharacter Find(string name)
        {
            foreach (PersonCharacter c in All)
            {
                if (c.Name == name) return c;
            }

            return new PersonCharacter(name, false);
        }

        /// <summary>The hours a role is out and about (from, to), before each person's own jitter.</summary>
        public static (float from, float to) Hours(PersonRole role)
        {
            switch (role)
            {
                case PersonRole.Innkeeper: return (6f, 23.5f);
                case PersonRole.Guest: return (8.5f, 22f);
                case PersonRole.Smith: return (7f, 19f);
                case PersonRole.Merchant: return (8f, 18f);
                case PersonRole.Religious: return (6f, 19.5f);
                case PersonRole.Groom: return (6f, 20.5f);
                default: return (6.5f, 20.5f);
            }
        }

        /// <summary>A short hash of the catalogue (the people palette rebuilds when it changes).</summary>
        public static string Signature
        {
            get
            {
                var parts = new List<string>();
                foreach (PersonCharacter c in All) parts.Add(c.Name + (c.Feminine ? "F" : "M"));
                return string.Join(",", parts);
            }
        }
    }
}
