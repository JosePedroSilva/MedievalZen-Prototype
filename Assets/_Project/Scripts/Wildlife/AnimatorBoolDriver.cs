namespace MedievalZen.Wildlife
{
    /// <summary>Receives the bool changes a <see cref="AnimatorBoolDriver"/> makes (the Animator in the game, a model in tests).</summary>
    public interface IAnimatorBoolSink
    {
        /// <summary>Sets bool <paramref name="boolIndex"/> (index into <see cref="SpeciesInfo.Bools"/>).</summary>
        void SetBool(int boolIndex, bool value);
    }

    /// <summary>
    /// Turns the pose a brain wants into the bool changes the pack's controllers need, honouring their quirks: at most one
    /// bool is true at a time; every change goes through Idle (the old bool is cleared and the new one set only once the
    /// controller is back in Idle, or after the binding's leave time when that cannot be observed); sleep and sit are
    /// chains with a get-up clip; a bark is a pulse (the state returns by exit time, so the bool is only set for a moment);
    /// poses the controller lacks are shown as Idle; death bools are never in a binding. Pure and allocation-free.
    /// </summary>
    public sealed class AnimatorBoolDriver
    {
        private enum Phase
        {
            Steady,
            Leaving,
            PulseOn,
            PulseOut,
        }

        private const float Timeout = 3f;
        private const float MinLeave = 0.05f;

        private SpeciesInfo info;
        private Phase phase;
        private float timer;
        private float leaveSeconds;
        private int activeBool = -1;
        private Pose pendingPulse = Pose.Idle;
        private bool hasPendingPulse;

        /// <summary>Creates a driver for a species (in Idle, every bool false).</summary>
        public AnimatorBoolDriver(SpeciesInfo info)
        {
            this.info = info;
        }

        /// <summary>The pose the brain wants (continuous).</summary>
        public Pose Wanted { get; set; }

        /// <summary>The pose the controller is in or entering (Idle while changing over).</summary>
        public Pose Shown { get; private set; }

        /// <summary>True while a change is under way.</summary>
        public bool Busy => phase != Phase.Steady || Wanted != Shown || hasPendingPulse;

        /// <summary>Index of the bool currently true, or −1.</summary>
        public int ActiveBool => activeBool;

        /// <summary>Back to Idle with every bool false, for a pooled animal (the caller also resets the Animator itself).</summary>
        public void Reset(SpeciesInfo species)
        {
            info = species;
            phase = Phase.Steady;
            timer = 0f;
            activeBool = -1;
            Wanted = Pose.Idle;
            Shown = Pose.Idle;
            hasPendingPulse = false;
        }

        /// <summary>Asks for a one-shot pose (a bark) to play once, then the wanted pose resumes. Ignored if the controller lacks it.</summary>
        public void Pulse(Pose pose)
        {
            if (!info.TryGetBinding(pose, out PoseBinding b) || b.Kind != BindKind.Pulse) return;
            pendingPulse = pose;
            hasPendingPulse = true;
        }

        /// <summary>
        /// Advances by <paramref name="dt"/>. <paramref name="idleKnown"/> says whether <paramref name="inIdle"/> is observed
        /// (the Animator's state); otherwise the binding's leave time is waited instead.
        /// </summary>
        public void Update(float dt, bool idleKnown, bool inIdle, IAnimatorBoolSink sink)
        {
            timer += dt;
            switch (phase)
            {
                case Phase.Leaving:
                {
                    bool back = idleKnown ? inIdle && timer >= MinLeave : timer >= leaveSeconds;
                    if (!back && timer < leaveSeconds + Timeout) return;
                    phase = Phase.Steady;
                    Shown = Pose.Idle;
                    break;
                }

                case Phase.PulseOn:
                {
                    if (!info.TryGetBinding(Shown, out PoseBinding b) || timer >= b.PulseSeconds)
                    {
                        if (activeBool >= 0) sink.SetBool(activeBool, false);
                        activeBool = -1;
                        phase = Phase.PulseOut;
                        leaveSeconds = b.LeaveSeconds;
                        timer = 0f;
                    }

                    return;
                }

                case Phase.PulseOut:
                {
                    bool back = idleKnown ? inIdle && timer >= MinLeave + 0.2f : timer >= leaveSeconds;
                    if (!back && timer < leaveSeconds + Timeout) return;
                    phase = Phase.Steady;
                    Shown = Pose.Idle;
                    break;
                }
            }

            if (phase != Phase.Steady) return;
            Pose next = hasPendingPulse ? pendingPulse : Wanted;
            if (next == Shown)
            {
                hasPendingPulse = false;
                return;
            }

            if (Shown != Pose.Idle)
            {
                // A pose sharing the bool of the one shown (a cow's alert is its second idle) needs no change.
                if (!hasPendingPulse && info.TryGetBinding(next, out PoseBinding same) && same.BoolIndex == activeBool && activeBool >= 0)
                {
                    Shown = next;
                    return;
                }

                Leave(sink);
                return;
            }

            Enter(next, sink);
        }

        private void Leave(IAnimatorBoolSink sink)
        {
            leaveSeconds = info.TryGetBinding(Shown, out PoseBinding b) ? b.LeaveSeconds : MinLeave;
            if (activeBool >= 0) sink.SetBool(activeBool, false);
            activeBool = -1;
            phase = Phase.Leaving;
            timer = 0f;
        }

        private void Enter(Pose pose, IAnimatorBoolSink sink)
        {
            timer = 0f;
            if (hasPendingPulse && pose == pendingPulse) hasPendingPulse = false;
            if (pose == Pose.Idle || !info.TryGetBinding(pose, out PoseBinding b))
            {
                // The controller has no such state: the animal shows Idle (and the driver counts the pose as shown).
                Shown = pose;
                activeBool = -1;
                phase = Phase.Steady;
                return;
            }

            sink.SetBool(b.BoolIndex, true);
            activeBool = b.BoolIndex;
            Shown = pose;
            phase = b.Kind == BindKind.Pulse ? Phase.PulseOn : Phase.Steady;
        }
    }
}
