using System;

namespace MedievalZen.Environment
{
    /// <summary>
    /// The mood under the trees (forest pass, 2026-10-05; endless road only). <see cref="Allowed"/> is set every frame by the endless
    /// world (its <c>forestExtras</c> feature, which the safety ladder sheds first); the finite world never sets it, so its look is
    /// unchanged. <see cref="Canopy"/> (0 in the open, 1 deep in a wood) is kept by the <see cref="AtmosphereController"/> from the
    /// tree density around the camera and read by the haze, the grade (<see cref="LightLookMath.AddForest"/>), the sun shafts and dust
    /// (<see cref="AmbientFxSpawner"/>) and the mist among the trunks (<see cref="MistField"/>).
    /// </summary>
    public static class ForestMood
    {
        /// <summary>True while the endless road allows the forest extras.</summary>
        public static bool Allowed { get; set; }

        /// <summary>0 in the open, 1 deep in a wood (smoothed over a few seconds); 0 whenever <see cref="Allowed"/> is false.</summary>
        public static float Canopy { get; internal set; }

        /// <summary>Tree density (trees per 100 m²) at which the canopy term starts.</summary>
        public const float OpenDensity = 0.35f;

        /// <summary>Tree density at which the canopy term is full.</summary>
        public const float WoodDensity = 0.9f;

        /// <summary>Extra haze inside a wood: the haze density is multiplied by 1 + this × canopy (trunks layer back in depth).</summary>
        public const float HazeGain = 0.9f;

        /// <summary>Seconds over which <see cref="Canopy"/> follows the ground (walking in and out of a wood).</summary>
        public const float FollowSeconds = 4f;

        /// <summary>
        /// The canopy target from the mean tree density around the camera (trees per 100 m²): 0 below <see cref="OpenDensity"/>,
        /// 1 from <see cref="WoodDensity"/>, smooth between. Pure.
        /// </summary>
        public static float CanopyFor(float meanDensity)
        {
            float t = (meanDensity - OpenDensity) / (WoodDensity - OpenDensity);
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }

        /// <summary>Mean density of a 3 × 3 sample, 25 m apart, centred on (x, z) (scene coordinates).</summary>
        public static float MeanDensity(Func<float, float, float> density, float x, float z)
        {
            if (density == null) return 0f;
            float sum = 0f;
            for (int j = -1; j <= 1; j++)
            {
                for (int i = -1; i <= 1; i++) sum += density(x + 25f * i, z + 25f * j);
            }

            return sum / 9f;
        }

        /// <summary>Moves <see cref="Canopy"/> toward <paramref name="target"/> over <see cref="FollowSeconds"/>.</summary>
        public static void Follow(float target, float deltaTime)
        {
            float k = 1f - (float)Math.Exp(-Math.Max(0f, deltaTime) / FollowSeconds);
            Canopy += (target - Canopy) * k;
            if (Canopy < 1e-4f) Canopy = 0f;
        }

        /// <summary>
        /// How strong forest sun shafts are, 0..1: by day only, strongest with a low sun (8-25°), fading out by 55°; none in rain or
        /// thick overcast. Pure.
        /// </summary>
        public static float ShaftStrength(float sunElevation, float rain, float overcast, float sunMultiplier)
        {
            if (rain > 0.05f || overcast > 0.65f || sunMultiplier < 0.6f || sunElevation < 3f || sunElevation > 55f) return 0f;
            float rise = Smooth((sunElevation - 3f) / 5f);
            float high = 1f - Smooth((sunElevation - 25f) / 30f);
            float clear = 1f - Math.Max(0f, overcast) / 0.65f;
            return rise * high * Math.Min(1f, clear * 1.2f);
        }

        private static float Smooth(float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Allowed = false;
            Canopy = 0f;
        }
    }
}
