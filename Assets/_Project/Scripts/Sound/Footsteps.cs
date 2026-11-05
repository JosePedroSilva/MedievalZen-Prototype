using System;
using MedievalZen.Endless;
using MedievalZen.Endless.Generation;
using MedievalZen.Player;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Sound
{
    /// <summary>
    /// The traveller's footsteps (placeholder: synthesised by <see cref="FootstepSynth"/> until a sampled pack replaces it). Each
    /// frame after the <see cref="PlayerAnimator"/> (order 110) and before <c>SeatedPose</c> (120) it reads the motor, the seat,
    /// the world's hold and the animated feet into <see cref="FootstepModel"/>; when a step lands it asks the world what is
    /// underfoot (one cheap query per step) and queues the sound. It attaches itself to the player in Play mode, with no scene or
    /// prefab change, in the endless road and the finite world (there: road and grass only, and water from the surface).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(115)]
    public sealed class Footsteps : MonoBehaviour, IOriginShiftable
    {
        /// <summary>A body that moves further than its own step by this much in one frame was teleported (metres).</summary>
        private const float JumpDistance = 1f;

        private readonly FootstepModel model = new FootstepModel();
        private PlayerMotor motor;
        private PlayerSeat seat;
        private PlayerAnimator playerAnimator;
        private Transform leftFoot, rightFoot;
        private FootstepSynth synth;
        private Vector3 lastPosition;
        private bool hasLast;
        private FootstepSurface lastSurface = FootstepSurface.Road;
        private float rate;
        private float lastStepTime = -10f;

        /// <summary>The surface of the last step.</summary>
        public FootstepSurface LastSurface => lastSurface;

        /// <summary>Steps per second lately (smoothed).</summary>
        public float StepsPerSecond => rate;

        /// <summary>Puts the footsteps on the scene's player if it has none (Play mode only).</summary>
        public static void Ensure()
        {
            if (!Application.isPlaying) return;
            PlayerMotor player = FindFirstObjectByType<PlayerMotor>();
            if (player != null && player.GetComponent<Footsteps>() == null) player.gameObject.AddComponent<Footsteps>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            Ensure();
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Ensure();

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            seat = GetComponent<PlayerSeat>();
            playerAnimator = GetComponent<PlayerAnimator>();

            var child = new GameObject("Footsteps Audio");
            child.transform.SetParent(transform, false);
            child.AddComponent<AudioSource>();
            synth = child.AddComponent<FootstepSynth>();
        }

        private void OnEnable()
        {
            SoundMix.FootstepStatus = Status;
            hasLast = false;
            model.Reset();
        }

        private void OnDisable()
        {
            if (SoundMix.FootstepStatus == (Func<string>)Status) SoundMix.FootstepStatus = null;
        }

        /// <inheritdoc />
        public void ShiftOrigin(Vector3 delta)
        {
            lastPosition += delta;
        }

        private string Status()
        {
            string mode = model.Mode == FootstepSource.Bones ? "bones" : model.Mode == FootstepSource.Stride ? "stride" : "-";
            return $"Footsteps (placeholder): {rate:0.0}/s on {lastSurface}, {mode}{(leftFoot != null ? string.Empty : " (no foot bones)")}";
        }

        // The Animator has posed the body by LateUpdate; the seated pose (120) bends it after this.
        private static readonly int FrameCostId = Review.FrameCost.Register("Footsteps");

        private void LateUpdate()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            float dt = Time.deltaTime;
            if (motor == null || dt <= 0f) return;
            FindFeet();

            Vector3 position = transform.position;
            Vector3 moved = position - lastPosition;
            Vector3 expected = motor.Velocity * dt;
            bool jumped = hasLast && (moved - expected).sqrMagnitude > JumpDistance * JumpDistance;
            lastPosition = position;
            hasLast = true;

            EndlessWorld endless = EndlessWorld.Current;
            bool holding = endless != null && endless.IsHolding;
            bool seated = seat != null && (seat.Phase == PlayerSeat.SeatPhase.Turning || seat.Phase == PlayerSeat.SeatPhase.Seated ||
                                           seat.Phase == PlayerSeat.SeatPhase.Standing);

            // On horseback the traveller's feet are still and the hooves sound instead, in the gait's rhythm (HorseMount counted the
            // beats after the motor moved; it runs its LateUpdate before this one).
            HorseMount mount = HorseMount.Current;
            if (mount != null && mount.gameObject == gameObject && mount.Mounted)
            {
                seated = true;
                if (!holding && !jumped && mount.BeatsThisFrame > 0)
                {
                    lastSurface = SurfaceAt(mount.HorsePosition);
                    for (int b = 0; b < mount.BeatsThisFrame && b < 3; b++) synth.PlayHoof(lastSurface, mount.LastBeat);
                }
            }
            var frame = new FootstepFrame
            {
                Dt = dt,
                Grounded = motor.IsGrounded,
                Seated = seated,
                Hold = holding || jumped,
                Speed = motor.PlanarSpeed,
                HasFeet = leftFoot != null && rightFoot != null,
            };
            if (frame.HasFeet)
            {
                float root = position.y;
                frame.LeftFoot = leftFoot.position.y - root;
                frame.RightFoot = rightFoot.position.y - root;
            }

            if (Time.time - lastStepTime > 1.5f) rate = 0f;
            if (!model.Update(frame, out FootstepEvent step)) return;

            float interval = Time.time - lastStepTime;
            lastStepTime = Time.time;
            if (interval > 0f && interval < 1.5f) rate = rate <= 0f ? 1f / interval : Mathf.Lerp(rate, 1f / interval, 0.3f);

            Transform footBone = step.Foot == 0 ? leftFoot : rightFoot;
            Vector3 at = footBone != null ? footBone.position : position;
            lastSurface = SurfaceAt(at);
            synth.Play(lastSurface, step.Foot, step.Sprint);
        }

        private void FindFeet()
        {
            if (leftFoot != null && rightFoot != null) return;
            Animator animator = playerAnimator != null ? playerAnimator.Animator : GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman || !animator.isActiveAndEnabled) return;
            leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        // ------------------------------------------------------------------ what is underfoot

        private FootstepSurface SurfaceAt(Vector3 scene)
        {
            var ground = new FootstepGround { RoadDistance = float.PositiveInfinity, RoadHalfWidth = 2.5f };
            try
            {
                EndlessWorld endless = EndlessWorld.Current;
                if (endless != null && endless.Generator != null) EndlessGround(endless, scene, ref ground);
                else FiniteGround(scene, ref ground);
            }
            catch (Exception e)
            {
                // A sound must never take the frame down; the road's dirt is the safe answer.
                Debug.LogWarning($"Footsteps: surface query failed ({e.GetType().Name}: {e.Message})", this);
                return FootstepSurface.Road;
            }

            return FootstepSurfaces.Classify(ground);
        }

        private static void EndlessGround(EndlessWorld endless, Vector3 scene, ref FootstepGround ground)
        {
            IEndlessGenerator generator = endless.Generator;
            (double x, double z) = endless.ToAbsolute(scene);
            double s = generator.NearestS(x, z, endless.S);
            RoadPoint p = generator.Road(s);
            float roadDistance = (float)Math.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z));
            float roadWidth = generator.Params != null ? generator.Params.roadWidth : 5f;
            ground.RoadDistance = roadDistance;
            ground.RoadHalfWidth = roadWidth * 0.5f;

            float h = generator.Height(x, z);
            float water = generator.WaterHeight(x, z);
            if (water > 0f) ground.WaterDepth = water - h;

            RegionKind kind = generator.Region(s).Kind;
            if (generator is EndlessGenerator real)
            {
                Crossing crossing = real.Streams.CrossingAt(s);
                if (crossing != null)
                {
                    double along = Math.Abs(s - crossing.S);
                    if (crossing.Kind == CrossingKind.Bridge) ground.Bridge = along <= StreamNetwork.BridgeSpanHalf + 0.5;
                    else ground.Ford = along <= Math.Max(1.5, crossing.BedWidth * 0.5 + 0.3);
                }

                TerrainField.Row row = real.Terrain.RowAt(z);
                float hx = real.Terrain.Height(row, x + 2.0);
                float hz = real.Terrain.Height(x, z + 2.0);
                float slope = (float)Math.Sqrt((hx - h) * (hx - h) + (hz - h) * (hz - h)) / 2f;
                real.Terrain.Cover(row, x, h, slope, out float forest, out float highland, out _, out _, out float mountain);
                ground.Forest = forest;
                // Pass rock: the rock skin's steep rock, or the bare ground of a pass above the tree line (scree and rock).
                float skin = RockSkinField.Rockiness(mountain, slope, 0f, roadDistance, roadWidth);
                float bare = kind == RegionKind.HighlandPass ? Mathf.Clamp01(mountain) * Mathf.Clamp01((highland - 0.6f) / 0.3f) : 0f;
                ground.Rock = Math.Max(skin, bare);
            }
            else
            {
                ground.Forest = kind == RegionKind.Forest ? 0.8f : 0.1f;
                ground.Rock = kind == RegionKind.HighlandPass ? 0.3f : 0f;
            }
        }

        private static void FiniteGround(Vector3 scene, ref FootstepGround ground)
        {
            GameWorld world = GameWorld.Current;
            if (world == null) return;
            if (world.Settings != null) ground.RoadHalfWidth = world.Settings.roadWidth * 0.5f;
            Roads.RoadLocation road = world.Roads.Project(scene);
            if (road.IsValid)
            {
                Vector3 p = road.Position;
                ground.RoadDistance = Mathf.Sqrt((p.x - scene.x) * (p.x - scene.x) + (p.z - scene.z) * (p.z - scene.z));
            }

            IWorldSurface surface = world.Surface;
            if (surface != null && surface.HasGround)
            {
                float water = surface.WaterHeight(scene);
                float h = surface.GroundHeight(scene);
                if (water >= 0f && water > h) ground.WaterDepth = water - h;   // negative: no lake there
            }
        }
    }
}
