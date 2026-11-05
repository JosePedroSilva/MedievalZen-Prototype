using System.Text;

namespace MedievalZen.Wildlife
{
    /// <summary>
    /// The player's wildlife choices (Journey menu → Sky: "Animals", "Animal sounds"), set by the menu and read by
    /// <see cref="WildlifeManager"/> every frame, so the order the two start in does not matter.
    /// </summary>
    public static class WildlifePreferences
    {
        /// <summary>Default of <see cref="AnimalsShown"/>.</summary>
        public const bool DefaultShown = true;

        /// <summary>Default of <see cref="SoundVolume"/>.</summary>
        public const float DefaultVolume = 0.5f;

        /// <summary>Animals are shown.</summary>
        public static bool AnimalsShown = DefaultShown;

        /// <summary>Volume of the animals' calls, 0..1.</summary>
        public static float SoundVolume = DefaultVolume;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            AnimalsShown = DefaultShown;
            SoundVolume = DefaultVolume;
        }
    }

    /// <summary>
    /// Logs one line each time the number of animals or groups first passes a milestone (pure; the caller logs the line),
    /// like the endless road's load milestones.
    /// </summary>
    public sealed class WildlifeMilestones
    {
        public static readonly int[] AnimalSteps = { 10, 20, 40, 60 };
        public static readonly int[] GroupSteps = { 3, 6, 10 };
        private int animalIndex, groupIndex;

        /// <summary>True (with a line) when the counts pass a milestone not passed before.</summary>
        public bool Update(int animals, int groups, out string line)
        {
            int a0 = animalIndex, g0 = groupIndex;
            while (animalIndex < AnimalSteps.Length && animals >= AnimalSteps[animalIndex]) animalIndex++;
            while (groupIndex < GroupSteps.Length && groups >= GroupSteps[groupIndex]) groupIndex++;
            if (animalIndex == a0 && groupIndex == g0)
            {
                line = null;   // the usual case: nothing allocated
                return false;
            }

            var crossed = new StringBuilder();
            if (animalIndex != a0) crossed.Append("animals ").Append(AnimalSteps[animalIndex - 1]);
            if (groupIndex != g0)
            {
                if (crossed.Length > 0) crossed.Append(", ");
                crossed.Append("groups ").Append(GroupSteps[groupIndex - 1]);
            }

            line = $"Wildlife load: {animals} animals in {groups} groups (passed {crossed}).";
            return true;
        }
    }
}
