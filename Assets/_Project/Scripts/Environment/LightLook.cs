namespace MedievalZen.Environment
{
    /// <summary>
    /// The player's "Rich light" choice (Journey menu → Sky → Light), read every frame by <see cref="DayCycle"/> (sun and
    /// ambient balance), <see cref="AtmosphereController"/> (grade) and the endless road's instanced trees (canopy shading),
    /// so the order things start in does not matter and switching it compares the two looks at once. The look itself is
    /// <see cref="LightLookMath"/>. On by default.
    /// </summary>
    public static class LightLook
    {
        /// <summary>Default of <see cref="Rich"/>.</summary>
        public const bool DefaultRich = true;

        private static bool rich = DefaultRich;

        /// <summary>True for the rich look, false for the look before it (2026-10-02).</summary>
        public static bool Rich
        {
            get => rich;
            set
            {
                if (rich == value) return;
                rich = value;
                Version++;
            }
        }

        /// <summary>1 for the rich look, 0 for the old one.</summary>
        public static float Weight => rich ? 1f : 0f;

        /// <summary>Changes every time <see cref="Rich"/> changes (consumers that cache material state compare it).</summary>
        public static int Version { get; private set; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            rich = DefaultRich;
            Version++;
        }
    }
}
