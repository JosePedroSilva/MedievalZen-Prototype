using MedievalZen.Endless;
using MedievalZen.Travel;
using MedievalZen.UI;
using UnityEngine;

namespace MedievalZen.Player
{
    /// <summary>
    /// The traveller on horseback (created by <see cref="PlayerController"/> at Awake; order −41, after <see cref="PlayerSeat"/>). Holds
    /// the mount's state (<see cref="MountStateMachine"/>), the gait the wheel steps (<see cref="HorseGaits"/>) and the ridden
    /// <see cref="HorseBody"/>. It never moves the body itself while riding: the controller and the auto-traveller ask the one
    /// <see cref="PlayerMotor"/> for riding speeds and the riding turn (<see cref="PlayerMotor.MoveRiding"/>), and in LateUpdate (after
    /// the motor) the horse is put under the rider. Getting on and off are short blends during which the body is held and slid to or
    /// from the saddle with <see cref="PlayerMotor.Teleport"/>. The camera only gets <see cref="PlayerCameraRig.AnchorOffset"/>, written
    /// only while the weight is above 0 (and once back to zero), so on foot every camera number is as before. Its hoof rhythm is read
    /// by the footsteps (<see cref="BeatsThisFrame"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-41)]
    public sealed class HorseMount : MonoBehaviour
    {
        /// <summary>The traveller's mount (one player), or null.</summary>
        public static HorseMount Current { get; private set; }

        /// <summary>True while the traveller is on a horse or getting on or off one.</summary>
        public static bool TravellerMounted => Current != null && Current.Mounted;

        private readonly MountStateMachine machine = new MountStateMachine();
        private readonly HoofRhythm rhythm = new HoofRhythm();
        private PlayerMotor motor;
        private PlayerCameraRig cameraRig;
        private PlayerAnimator playerAnimator;
        private PlayerSeat seat;
        private PlayerTravel travel;
        private AutoTraveller traveller;
        private RiderPose pose;
        private HorseStable stable;
        private HorseBody horse;
        private Vector3 blendFrom, blendTo;
        private float yawFrom, yawTo;
        private bool offsetApplied, resumeChecked;
        private float bob;

        /// <summary>The gait the wheel steps (manual riding and auto-travel).</summary>
        public HorseGait Gait { get; private set; } = HorseGait.Walk;

        /// <summary>The riding turn passed to the motor.</summary>
        public RideTurn Turn { get; } = RideTurn.Default;

        /// <summary>Phase of getting on, riding, getting off.</summary>
        public MountPhase Phase => machine.Phase;

        /// <summary>On a horse or getting on/off.</summary>
        public bool Mounted => machine.Mounted;

        /// <summary>Astride with the riding controls.</summary>
        public bool Riding => machine.Riding;

        /// <summary>True while getting on or off: no driver may move the body.</summary>
        public bool HoldsBody => machine.HoldsBody;

        /// <summary>0 on foot … 1 astride.</summary>
        public float Weight => machine.Weight;

        /// <summary>Hoofbeats that fell this frame (read by the footsteps after the horse moved).</summary>
        public int BeatsThisFrame { get; private set; }

        /// <summary>Index of the last beat in its cycle (0 = the accented first beat).</summary>
        public int LastBeat => rhythm.LastBeat;

        /// <summary>True once a saved ride has been picked up (or there was none): from then on the journey saves this mount's state.</summary>
        public bool ResumeChecked => resumeChecked;

        /// <summary>Where the ridden horse stands (scene).</summary>
        public Vector3 HorsePosition => horse != null ? horse.Position : transform.position;

        private void Awake()
        {
            Current = this;
            motor = GetComponent<PlayerMotor>();
            cameraRig = GetComponent<PlayerCameraRig>();
            playerAnimator = GetComponent<PlayerAnimator>();
            travel = GetComponent<PlayerTravel>();
            traveller = GetComponent<AutoTraveller>();
            pose = GetComponent<RiderPose>();
            if (pose == null) pose = gameObject.AddComponent<RiderPose>();
            stable = GetComponent<HorseStable>();
            if (stable == null) stable = gameObject.AddComponent<HorseStable>();
            if (GetComponent<HorseInteractable>() == null) gameObject.AddComponent<HorseInteractable>();
        }

        private void Start()
        {
            seat = GetComponent<PlayerSeat>();
            if (traveller != null) traveller.Started += OnTravelStarted;
        }

        private void OnDestroy()
        {
            if (traveller != null) traveller.Started -= OnTravelStarted;
            if (Current == this) Current = null;
        }

        private void OnTravelStarted(MedievalZen.Roads.RoadRoute route)
        {
            if (Riding) Gait = HorseGaits.TravelDefault;   // auto-travel sets off at a trot; the wheel changes it
        }

        // ------------------------------------------------------------------ getting on and off

        /// <summary>Gets on <paramref name="body"/> (from <see cref="HorseInteractable"/>); false when the rules forbid it now.</summary>
        internal bool Mount(HorseBody body)
        {
            if (body == null || stable == null || MedievalZen.Sound.SoundMix.CaptureRunning) return false;
            float distance = Vector3.Distance(Flat(body.Position), Flat(transform.position));
            bool seated = seat != null && seat.IsActive;
            if (!MountStateMachine.CanMount(machine.Phase, distance, seated, false)) return false;
            if (travel != null && travel.IsTravelling) travel.Cancel();
            stable.Take(body);
            horse = body;
            blendFrom = transform.position;
            yawFrom = motor.Yaw;
            yawTo = body.Yaw;
            blendTo = body.RiderRoot;
            blendTo.y = body.Position.y;
            Gait = HorseGait.Walk;
            rhythm.Reset();
            machine.BeginMount();
            return true;
        }

        /// <summary>Interact with nothing in reach: get down at a walk or standing. Returns true when it began.</summary>
        public bool TryDismount()
        {
            if (!machine.Riding || horse == null) return false;
            if (!MountStateMachine.CanDismount(machine.Phase, motor.PlanarSpeed, MedievalZen.Sound.SoundMix.CaptureRunning))
            {
                Notice("Slow to a walk to get down");
                return false;
            }

            if (travel != null && travel.IsTravelling) travel.Cancel();
            horse.Position = horse.RootUnder(transform.position, motor.Yaw);
            horse.Yaw = motor.Yaw;
            blendFrom = transform.position;
            yawFrom = yawTo = motor.Yaw;
            Vector3 right = Quaternion.Euler(0f, motor.Yaw, 0f) * Vector3.right;
            blendTo = OnGround(SideClear(-right) ? transform.position - right * 1.0f : transform.position + right * 1.0f);   // the near (left) side first
            machine.BeginDismount();
            return true;
        }

        /// <summary>One wheel notch or bumper while riding: the next gait, shown on the HUD as "Trot · 3.6 m/s".</summary>
        public void StepGait(int steps)
        {
            if (steps == 0) return;
            Gait = HorseGaits.Stepped(Gait, steps);
            HudController hud = HudController.Instance;
            if (hud != null) hud.ShowPace(HorseGaits.Label(Gait, HorseGaits.Speed(Gait)));
        }

        /// <summary>A short HUD notice (e.g. "Get down from the horse first").</summary>
        public void Notice(string message)
        {
            HudController hud = HudController.Instance;
            if (hud != null) hud.ShowToast(message);
        }

        private bool SideClear(Vector3 dir)
        {
            Vector3 p = transform.position + dir * 1.0f;
            return !Physics.CheckCapsule(p + Vector3.up * 0.7f, p + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>The point with its height on the ground (unchanged when the ground cannot be asked).</summary>
        private static Vector3 OnGround(Vector3 p)
        {
            MedievalZen.World.IWorldSurface surface = MedievalZen.World.GameWorld.Current != null ? MedievalZen.World.GameWorld.Current.Surface : null;
            if (surface == null || !surface.HasGround) return p;
            float h = surface.GroundHeight(p);
            if (!float.IsNaN(h) && Mathf.Abs(h - p.y) < 1.5f) p.y = h + 0.05f;
            return p;
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            ResumeIfSaved();
            if (machine.Mounted && (horse == null || horse.Go == null || stable == null || stable.Own != horse))
            {
                // The horse went away under the rider (a new journey, a pool reset): back on foot at once.
                machine.SetOnFoot();
                horse = null;
            }

            if (!machine.HoldsBody) return;
            bool ended = machine.Update(Time.deltaTime);
            // Progress of the slide, 0 → 1 both ways (the weight is already eased).
            float progress = ended ? 1f : machine.Phase == MountPhase.Mounting ? machine.Weight : 1f - machine.Weight;
            Vector3 p = Vector3.Lerp(blendFrom, blendTo, progress);
            motor.Teleport(p, Mathf.LerpAngle(yawFrom, yawTo, progress));
        }

        private void ResumeIfSaved()
        {
            if (resumeChecked) return;
            EndlessWorld endless = EndlessWorld.Current;
            if (endless == null || endless.IsHolding) return;
            resumeChecked = true;
            if (!endless.ResumeMounted || stable == null) return;
            HorseBody b = stable.SpawnOwn(transform.position, motor.Yaw);
            if (b == null) return;
            horse = b;
            Gait = HorseGaits.Clamp(endless.ResumeGait);
            machine.SetRiding();
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            BeatsThisFrame = 0;
            bool mounted = machine.Mounted && horse != null;
            if (playerAnimator != null) playerAnimator.StandStill = mounted;
            float rise = 0f;
            if (mounted)
            {
                if (machine.Riding)
                {
                    horse.Yaw = motor.Yaw;
                    horse.Position = horse.RootUnder(transform.position, motor.Yaw);
                    float speed = motor.PlanarSpeed;
                    BeatsThisFrame = rhythm.Update(dt, speed);
                    float target = speed >= HorseGaits.StandSpeed ? HorseGaits.Bob(rhythm.Gait, rhythm.Phase) * Mathf.Clamp01(speed / 1f) : 0f;
                    bob = target;
                    horse.Place(dt);
                    horse.Animate(speed, false, dt);
                }
                else
                {
                    bob = 0f;
                    horse.Place(dt);
                    horse.Animate(0f, false, dt);
                }

                rise = Mathf.Max(0f, horse.SaddleHeight - pose.HipHeight + (horse.Position.y - transform.position.y));
            }
            else
            {
                bob = 0f;
                rhythm.Reset();
            }

            float weight = mounted ? machine.Weight : 0f;
            pose.Weight = weight;
            pose.Rise = rise;
            pose.Bob = bob;

            if (cameraRig == null) return;
            if (weight > 0f)
            {
                cameraRig.AnchorOffset = RideCamera.Offset(weight, rise, bob);
                offsetApplied = true;
            }
            else if (offsetApplied)
            {
                cameraRig.AnchorOffset = Vector3.zero;
                offsetApplied = false;
            }
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
