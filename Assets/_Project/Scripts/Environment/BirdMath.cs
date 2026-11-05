using System;

namespace MedievalZen.Environment
{
    /// <summary>
    /// Pure maths for the small birds that cross the sky (<see cref="SkyBirds"/>): wing beat with glides, loose flock
    /// formations and the crossing path. No engine types, so it is tested offline.
    /// </summary>
    public static class BirdMath
    {
        /// <summary>Wing angle when gliding (degrees above level; a shallow V).</summary>
        public const float GlideAngle = 9f;

        /// <summary>Half the wing-beat sweep in degrees (the wing goes GlideAngle ± this, scaled by the flap envelope).</summary>
        public const float FlapAmplitude = 34f;

        /// <summary>Lowest wing angle any pose shows.</summary>
        public const float MinAngle = GlideAngle - FlapAmplitude;

        /// <summary>Highest wing angle any pose shows.</summary>
        public const float MaxAngle = GlideAngle + FlapAmplitude;

        /// <summary>
        /// 0 (gliding) .. 1 (flapping) envelope over a bird's flap–glide cycle of <paramref name="cycleSeconds"/>: it flaps for
        /// <paramref name="flapFraction"/> of the cycle, eases over 0.25 s at each end and glides the rest. <paramref name="time"/>
        /// already includes the bird's own phase offset.
        /// </summary>
        public static float FlapEnvelope(double time, float cycleSeconds, float flapFraction)
        {
            float cycle = Math.Max(0.5f, cycleSeconds);
            float flap = Math.Min(0.95f, Math.Max(0.05f, flapFraction)) * cycle;
            float t = (float)(time - Math.Floor(time / cycle) * cycle);
            const float ease = 0.25f;
            if (t < flap) return Smooth(Math.Min(t, flap - t) / ease);
            return 0f;
        }

        /// <summary>
        /// Wing angle in degrees at <paramref name="time"/> for a bird beating at <paramref name="beatHz"/>: the glide angle
        /// plus a sine beat scaled by the flap envelope. Always within [<see cref="MinAngle"/>, <see cref="MaxAngle"/>].
        /// </summary>
        public static float WingAngle(double time, float beatHz, float cycleSeconds, float flapFraction)
        {
            float envelope = FlapEnvelope(time, cycleSeconds, flapFraction);
            double beat = Math.Sin(time * 2.0 * Math.PI * Math.Max(0.1f, beatHz));
            return GlideAngle + (float)beat * FlapAmplitude * envelope;
        }

        /// <summary>Index of the nearest of <paramref name="poseCount"/> evenly spaced poses from <see cref="MinAngle"/> to <see cref="MaxAngle"/>.</summary>
        public static int PoseIndex(float angle, int poseCount)
        {
            if (poseCount <= 1) return 0;
            float t = (angle - MinAngle) / (MaxAngle - MinAngle);
            int i = (int)Math.Round(t * (poseCount - 1));
            return Math.Min(poseCount - 1, Math.Max(0, i));
        }

        /// <summary>The wing angle pose <paramref name="index"/> of <paramref name="poseCount"/> shows.</summary>
        public static float PoseAngle(int index, int poseCount)
        {
            if (poseCount <= 1) return GlideAngle;
            return MinAngle + (MaxAngle - MinAngle) * index / (poseCount - 1f);
        }

        /// <summary>
        /// Offset of bird <paramref name="index"/> from the flock centre in flock space (x right, z forward), metres.
        /// A V: the leader in front, the others alternating left and right behind at <paramref name="spacing"/>; otherwise a
        /// loose cluster on a sunflower spiral. <paramref name="jitterX"/>/<paramref name="jitterZ"/> (−1..1) loosen it.
        /// </summary>
        public static (float x, float z) FormationOffset(int index, int count, bool vee, float spacing, float jitterX, float jitterZ)
        {
            spacing = Math.Max(0.5f, spacing);
            float x, z;
            if (vee)
            {
                int rank = (index + 1) / 2;
                int side = index == 0 ? 0 : (index % 2 == 1 ? -1 : 1);
                x = side * rank * spacing * 0.9f;
                z = -rank * spacing * 0.75f + (count - 1) * spacing * 0.25f;
            }
            else
            {
                const double golden = 2.399963229728653;
                double r = spacing * Math.Sqrt(index + 0.5);
                double a = index * golden;
                x = (float)(r * Math.Cos(a));
                z = (float)(r * Math.Sin(a));
            }

            return (x + jitterX * spacing * 0.3f, z + jitterZ * spacing * 0.3f);
        }

        /// <summary>
        /// Start and end of a crossing: a straight line along (<paramref name="headingX"/>, <paramref name="headingZ"/>) that passes
        /// <paramref name="sideOffset"/> metres to the side of (<paramref name="centreX"/>, <paramref name="centreZ"/>), running
        /// <paramref name="halfLength"/> metres either side of its closest point.
        /// </summary>
        public static (float sx, float sz, float ex, float ez) CrossingPath(float centreX, float centreZ, float headingX, float headingZ, float sideOffset, float halfLength)
        {
            double len = Math.Sqrt(headingX * headingX + headingZ * headingZ);
            float hx = len > 1e-6 ? (float)(headingX / len) : 0f;
            float hz = len > 1e-6 ? (float)(headingZ / len) : 1f;
            float px = centreX + hz * sideOffset, pz = centreZ - hx * sideOffset;   // right of the heading
            return (px - hx * halfLength, pz - hz * halfLength, px + hx * halfLength, pz + hz * halfLength);
        }

        private static float Smooth(float x)
        {
            x = Math.Min(1f, Math.Max(0f, x));
            return x * x * (3f - 2f * x);
        }
    }
}
