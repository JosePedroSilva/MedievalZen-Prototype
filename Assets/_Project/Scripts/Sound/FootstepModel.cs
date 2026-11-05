using System;

namespace MedievalZen.Sound
{
    /// <summary>What the traveller's foot lands on.</summary>
    public enum FootstepSurface
    {
        /// <summary>The packed dirt road.</summary>
        Road = 0,

        /// <summary>Meadow, verge and field grass.</summary>
        Grass = 1,

        /// <summary>Leaf litter under the woods.</summary>
        ForestFloor = 2,

        /// <summary>Pass rock, scree and the stone bridges.</summary>
        Rock = 3,

        /// <summary>Shallow water over the ground: a ford, a lake's edge.</summary>
        Water = 4,
    }

    /// <summary>How the steps are being timed.</summary>
    public enum FootstepSource
    {
        /// <summary>No steps (standing, seated, holding).</summary>
        None = 0,

        /// <summary>From the animated body's foot bones.</summary>
        Bones = 1,

        /// <summary>From the distance walked (no animated body, or its feet give nothing usable).</summary>
        Stride = 2,
    }

    /// <summary>One frame of what the footstep model needs to know (filled by <c>Footsteps</c> from the motor, the seat and the body).</summary>
    public struct FootstepFrame
    {
        /// <summary>Seconds since the last frame.</summary>
        public float Dt;

        /// <summary>The capsule touches the ground.</summary>
        public bool Grounded;

        /// <summary>Sitting down, seated or getting up.</summary>
        public bool Seated;

        /// <summary>The world holds the traveller (streaming hold, scripted move) or the body was teleported this frame.</summary>
        public bool Hold;

        /// <summary>Horizontal speed of the body, m/s (from the motor's own step, so a teleport is not a stride).</summary>
        public float Speed;

        /// <summary>The foot heights below are valid this frame.</summary>
        public bool HasFeet;

        /// <summary>Height of each foot bone above the body's root, metres.</summary>
        public float LeftFoot, RightFoot;
    }

    /// <summary>A step that landed this frame.</summary>
    public struct FootstepEvent
    {
        /// <summary>0 left, 1 right.</summary>
        public int Foot;

        /// <summary>Walking speed when it landed, m/s.</summary>
        public float Speed;

        /// <summary>Sprinting (louder, no heel-toe roll).</summary>
        public bool Sprint;

        /// <summary>Where the step came from.</summary>
        public FootstepSource Source;
    }

    /// <summary>
    /// Decides when a step lands (pure, tested). With an animated body a plant is read from the foot bones: a foot that was
    /// descending comes within <see cref="PlantThreshold"/> of its lowest height while its downward speed drops; it must rise
    /// <see cref="RearmHeight"/> above that before it can plant again, feet alternate, and two steps are at least
    /// <see cref="MinInterval"/> apart. When there is no body, or the bones have given nothing for <see cref="BoneTimeout"/> while
    /// walking, steps fall back to the stride: <see cref="WalkStride"/> at walking speed (1.9 steps/s at 1.6 m/s), lengthening to
    /// <see cref="SprintStride"/> at a sprint (2.8 steps/s at 3.5 m/s). No step while airborne, standing, seated or held, and a
    /// hold or a teleport restarts the count (a position jump is not a stride).
    /// </summary>
    public sealed class FootstepModel
    {
        /// <summary>Shortest time between two steps, seconds.</summary>
        public const float MinInterval = 0.18f;

        /// <summary>Stride (distance per step) at walking speed and at a sprint, metres.</summary>
        public const float WalkStride = 0.84f, SprintStride = 1.25f;

        /// <summary>Walking and sprinting speed the strides belong to (the motor's 1.6 m/s and ×2.2).</summary>
        public const float WalkSpeed = 1.6f, SprintSpeed = 3.52f;

        /// <summary>Below this speed the traveller is standing (m/s).</summary>
        public const float MoveSpeed = 0.25f;

        /// <summary>Above this speed the body is being carried faster than feet go (debug travel): no steps.</summary>
        public const float MaxStepSpeed = 6f;

        /// <summary>A step at this speed or more counts as a sprint.</summary>
        public const float SprintFrom = 2.6f;

        /// <summary>Plant when the foot comes this close to its lowest height (metres).</summary>
        public const float PlantThreshold = 0.035f;

        /// <summary>A planted foot must rise this far above its lowest height before it can plant again (metres).</summary>
        public const float RearmHeight = 0.06f;

        /// <summary>A foot counts as descending below this vertical speed (m/s).</summary>
        public const float DescendSpeed = -0.08f;

        /// <summary>The lowest-height estimate creeps up at this rate (1/s) so a slope or a new clip does not leave it behind.</summary>
        public const float LowRelax = 0.2f;

        /// <summary>Seconds of walking without a bone plant after which the stride takes over.</summary>
        public const float BoneTimeout = 1.5f;

        /// <summary>A foot may plant twice in a row only after this long (the other foot's plant was missed).</summary>
        public const float SameFootAfter = 0.9f;

        private struct Foot
        {
            public bool Primed;
            public float Height, Velocity, Low;
            public bool Armed;
        }

        private Foot left, right;
        private float time;
        private float lastStep = -10f;
        private int lastFoot = 1;
        private float strideLeft;
        private float sinceBonePlant;
        private bool bonesWork;

        /// <summary>True while the foot bones give plants (false: no body, or the stride has taken over).</summary>
        public bool BonesWork => bonesWork;

        /// <summary>Where the last step came from (None while not walking).</summary>
        public FootstepSource Mode { get; private set; }

        /// <summary>Steps so far (for the overlay's rate).</summary>
        public int Count { get; private set; }

        /// <summary>Distance per step at <paramref name="speed"/> m/s: 0.84 m at a walk, rising to 1.25 m at a sprint, shorter when slower.</summary>
        public static float StrideAt(float speed)
        {
            if (!(speed > 0f)) return WalkStride;
            if (speed <= WalkSpeed) return WalkStride * Math.Max(0.6f, (float)Math.Sqrt(speed / WalkSpeed));
            float t = Math.Min(1f, (speed - WalkSpeed) / (SprintSpeed - WalkSpeed));
            return WalkStride + (SprintStride - WalkStride) * t;
        }

        /// <summary>Forgets every foot and the stride (after a hold or a teleport).</summary>
        public void Reset()
        {
            left = default;
            right = default;
            strideLeft = 0f;
            sinceBonePlant = 0f;
            bonesWork = false;
            Mode = FootstepSource.None;
        }

        /// <summary>Advances one frame; true when a step lands, described by <paramref name="step"/>.</summary>
        public bool Update(in FootstepFrame frame, out FootstepEvent step)
        {
            step = default;
            float dt = frame.Dt;
            if (!(dt > 0f) || dt > 1f) return false;
            time += dt;

            if (frame.Hold || float.IsNaN(frame.Speed))
            {
                Reset();
                return false;
            }

            bool walking = frame.Grounded && !frame.Seated && frame.Speed >= MoveSpeed && frame.Speed <= MaxStepSpeed;

            // The bones are tracked whenever they exist, so the plant detector is warm when walking starts.
            int planted = -1;
            if (frame.HasFeet && !float.IsNaN(frame.LeftFoot) && !float.IsNaN(frame.RightFoot))
            {
                bool l = Track(ref left, frame.LeftFoot, dt);
                bool r = Track(ref right, frame.RightFoot, dt);
                if (l && r) planted = left.Height <= right.Height ? 0 : 1;
                else if (l) planted = 0;
                else if (r) planted = 1;
            }
            else
            {
                left = default;
                right = default;
                bonesWork = false;
            }

            if (!walking)
            {
                strideLeft = 0f;
                sinceBonePlant = 0f;
                Mode = FootstepSource.None;
                return false;
            }

            sinceBonePlant += dt;
            float stride = StrideAt(frame.Speed);
            strideLeft += frame.Speed * dt;

            bool land = false;
            int foot = 1 - lastFoot;
            FootstepSource source = FootstepSource.Stride;
            if (planted >= 0)
            {
                bool alternates = planted != lastFoot || time - lastStep >= SameFootAfter;
                if (alternates && time - lastStep >= MinInterval)
                {
                    land = true;
                    foot = planted;
                    source = FootstepSource.Bones;
                }

                sinceBonePlant = 0f;
                bonesWork = true;
            }
            else if (!frame.HasFeet || sinceBonePlant > BoneTimeout)
            {
                // No body, or its feet have given nothing for a while (a clip without a plant, a body that does not animate).
                bonesWork = false;
                if (strideLeft >= stride && time - lastStep >= MinInterval) land = true;
            }

            if (!land)
            {
                // While the bones have the say the stride only stands by: never let it build a backlog.
                if (frame.HasFeet && sinceBonePlant <= BoneTimeout) strideLeft = Math.Min(strideLeft, stride);
                return false;
            }

            strideLeft = source == FootstepSource.Stride ? Math.Min(strideLeft - stride, stride) : 0f;
            if (strideLeft < 0f) strideLeft = 0f;
            lastStep = time;
            lastFoot = foot;
            Mode = source;
            Count++;
            step = new FootstepEvent { Foot = foot, Speed = frame.Speed, Sprint = frame.Speed >= SprintFrom, Source = source };
            return true;
        }

        /// <summary>Follows one foot; true on the frame it plants.</summary>
        private static bool Track(ref Foot f, float height, float dt)
        {
            if (!f.Primed)
            {
                f = new Foot { Primed = true, Height = height, Velocity = 0f, Low = height, Armed = false };
                return false;
            }

            float velocity = (height - f.Height) / dt;
            float previousVelocity = f.Velocity;
            f.Height = height;
            f.Velocity = velocity;

            if (height < f.Low) f.Low = height;
            else f.Low += (height - f.Low) * Math.Min(1f, dt * LowRelax);

            if (!f.Armed)
            {
                if (height > f.Low + RearmHeight) f.Armed = true;
                return false;
            }

            // Descending (this frame or the last), near the floor, and the fall is slowing: the foot lands.
            bool descending = previousVelocity < DescendSpeed || velocity < DescendSpeed;
            bool slowing = velocity > previousVelocity || velocity > DescendSpeed;
            if (descending && slowing && height <= f.Low + PlantThreshold)
            {
                f.Armed = false;
                return true;
            }

            return false;
        }
    }

    /// <summary>What the world knows about the ground under a step (filled by <c>Footsteps</c> from the generator or the terrain).</summary>
    public struct FootstepGround
    {
        /// <summary>Horizontal distance from the road's centre line, metres (infinity: no road known).</summary>
        public float RoadDistance;

        /// <summary>Half the road's width, metres.</summary>
        public float RoadHalfWidth;

        /// <summary>On a stone bridge's deck.</summary>
        public bool Bridge;

        /// <summary>In a ford's water on the road.</summary>
        public bool Ford;

        /// <summary>Water surface above the ground, metres (0 or less: dry).</summary>
        public float WaterDepth;

        /// <summary>Woodland cover, 0..1.</summary>
        public float Forest;

        /// <summary>Bare rock or scree, 0..1.</summary>
        public float Rock;
    }

    /// <summary>The surface rules (pure): water, then a bridge's stone, then the road, rock, leaf litter and grass.</summary>
    public static class FootstepSurfaces
    {
        /// <summary>Water deeper than this over the ground is waded (metres).</summary>
        public const float WadeDepth = 0.02f;

        /// <summary>Rock cover from which the ground sounds of rock.</summary>
        public const float RockFrom = 0.45f;

        /// <summary>Wood cover from which the ground is leaf litter.</summary>
        public const float ForestFrom = 0.45f;

        /// <summary>The surface for <paramref name="g"/>.</summary>
        public static FootstepSurface Classify(in FootstepGround g)
        {
            bool onRoad = g.RoadDistance <= g.RoadHalfWidth;
            if (g.WaterDepth > WadeDepth || (g.Ford && onRoad)) return FootstepSurface.Water;
            if (g.Bridge && onRoad) return FootstepSurface.Rock;
            if (onRoad) return FootstepSurface.Road;
            if (g.Rock >= RockFrom) return FootstepSurface.Rock;
            if (g.Forest >= ForestFrom) return FootstepSurface.ForestFloor;
            return FootstepSurface.Grass;
        }
    }
}
