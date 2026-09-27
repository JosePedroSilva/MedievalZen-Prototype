namespace MedievalZen.Travel
{
    /// <summary>What <see cref="StuckDetector.Update"/> asks the auto-traveller to do.</summary>
    public enum StuckAction
    {
        /// <summary>Keep going.</summary>
        None = 0,

        /// <summary>First time stuck: resync to the nearest point of the route and try again.</summary>
        Reproject = 1,

        /// <summary>Stuck again after reprojecting: give up ("The way is blocked").</summary>
        GiveUp = 2,
    }

    /// <summary>
    /// Pure state machine that notices when auto-travel stops making progress: planar speed below <see cref="MinSpeed"/> for
    /// <see cref="StuckSeconds"/> first asks for one reprojection, and if the body is still stuck after another
    /// <see cref="StuckSeconds"/>, for giving up. Moving normally for <see cref="RecoverSeconds"/> forgets the earlier attempt.
    /// </summary>
    public sealed class StuckDetector
    {
        private float slowTime;
        private float movingTime;

        /// <summary>Creates a detector.</summary>
        public StuckDetector(float minSpeed = 0.2f, float stuckSeconds = 3f, float recoverSeconds = 3f)
        {
            MinSpeed = minSpeed;
            StuckSeconds = stuckSeconds;
            RecoverSeconds = recoverSeconds;
        }

        /// <summary>Planar speed in m/s below which the traveller counts as not moving.</summary>
        public float MinSpeed { get; set; }

        /// <summary>Seconds of not moving before acting.</summary>
        public float StuckSeconds { get; set; }

        /// <summary>Seconds of moving normally after which a previous reprojection is forgotten.</summary>
        public float RecoverSeconds { get; set; }

        /// <summary>True once a reprojection has been requested and not yet forgotten.</summary>
        public bool HasReprojected { get; private set; }

        /// <summary>Seconds spent below <see cref="MinSpeed"/> so far.</summary>
        public float SlowTime => slowTime;

        /// <summary>Forgets all history (call when travel starts).</summary>
        public void Reset()
        {
            slowTime = 0f;
            movingTime = 0f;
            HasReprojected = false;
        }

        /// <summary>Feeds one frame of <paramref name="deltaTime"/> seconds at <paramref name="planarSpeed"/> m/s.</summary>
        public StuckAction Update(float deltaTime, float planarSpeed)
        {
            if (deltaTime <= 0f) return StuckAction.None;

            if (planarSpeed >= MinSpeed)
            {
                slowTime = 0f;
                movingTime += deltaTime;
                if (movingTime >= RecoverSeconds) HasReprojected = false;
                return StuckAction.None;
            }

            movingTime = 0f;
            slowTime += deltaTime;
            if (slowTime < StuckSeconds) return StuckAction.None;

            slowTime = 0f;
            if (!HasReprojected)
            {
                HasReprojected = true;
                return StuckAction.Reproject;
            }

            HasReprojected = false;
            return StuckAction.GiveUp;
        }
    }
}
