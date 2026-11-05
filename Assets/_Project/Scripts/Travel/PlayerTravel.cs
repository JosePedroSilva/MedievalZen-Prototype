using System;
using MedievalZen.Roads;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Travel
{
    /// <summary>
    /// Chooses where auto-travel goes when the player presses the auto-travel key, instead of the finite world's rule
    /// (the destination picked on the map or a signpost). The endless road supplies one: the next inn in the direction the
    /// traveller faces.
    /// </summary>
    public interface ITravelDestinationPicker
    {
        /// <summary>
        /// The settlement to travel to from where <paramref name="travel"/>'s player stands (in the network of
        /// <c>GameWorld.Current</c>, which the picker may refresh first), or null with a player-facing
        /// <paramref name="notice"/> explaining why there is none.
        /// </summary>
        Settlement PickDestination(PlayerTravel travel, out string notice);
    }

    /// <summary>
    /// The player's travel state: the chosen destination and starting/stopping auto-travel towards it
    /// along the roads of <see cref="GameWorld.Current"/>.
    /// </summary>
    [RequireComponent(typeof(AutoTraveller))]
    [DisallowMultipleComponent]
    public sealed class PlayerTravel : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("Auto-travel starts within this many metres of a road; further than the approach threshold, the player first walks straight to the road.")]
        private float maxApproachDistance = 60f;

        [SerializeField, Min(0f), Tooltip("Off-road distance above which auto-travel begins with a straight approach leg to the nearest road point.")]
        private float approachThreshold = 3f;

        [SerializeField, Tooltip("Clear the destination after arriving, so the next auto-travel asks for (or picks) a new one.")]
        private bool clearDestinationOnArrival = true;

        [SerializeField, Tooltip("Log travel events to the console.")]
        private bool logEvents = true;

        private AutoTraveller traveller;

        /// <summary>Raised when <see cref="Destination"/> changes (argument may be <c>null</c>).</summary>
        public event Action<Settlement> DestinationChanged;

        /// <summary>Raised when auto-travel reaches a settlement.</summary>
        public event Action<Settlement> Arrived;

        /// <summary>Short player-facing messages about travel ("Setting out for …", "The way is blocked"); the HUD shows them as toasts.</summary>
        public event Action<string> Notice;

        /// <summary>The settlement auto-travel heads for, or <c>null</c>.</summary>
        public Settlement Destination { get; private set; }

        /// <summary>True while auto-travelling.</summary>
        public bool IsTravelling => traveller != null && traveller.IsActive;

        /// <summary>The auto-traveller this component drives.</summary>
        public AutoTraveller Traveller => traveller;

        /// <summary>
        /// When set (the endless road), every start of auto-travel asks it for the destination; when null (the finite world)
        /// the chosen <see cref="Destination"/> is used as before.
        /// </summary>
        public ITravelDestinationPicker DestinationPicker { get; set; }

        private static readonly int CostContinue = Review.FrameCost.Register("PlayerTravel.ContinueTravel (re-route)");

        /// <summary>
        /// Keeps an ongoing trip going after the road network was replaced (the endless road's sliding window, or a floating-
        /// origin shift that moved everything by <paramref name="originDelta"/>): re-routes to <paramref name="destination"/>
        /// (the same place in the new network) and hands the route to the auto-traveller without stopping or announcing
        /// anything. When not travelling, only the destination reference is updated. Returns false (and stops) when the new
        /// network has no route.
        /// </summary>
        public bool ContinueTravel(Settlement destination, Vector3 originDelta)
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(CostContinue);   // stall hunt: the per-frame culprit
            if (destination != Destination)
            {
                Destination = destination;
                DestinationChanged?.Invoke(destination);
            }

            if (!IsTravelling || destination == null) return destination != null;
            RoadNetwork roads = GameWorld.Current != null ? GameWorld.Current.Roads : null;
            if (roads == null || roads.Segments.Count == 0)
            {
                Cancel();
                return false;
            }

            RoadLocation start = roads.Project(transform.position);
            if (!roads.TryFindRoute(start, destination, out RoadRoute route))
            {
                if (logEvents) Debug.LogWarning($"Auto-travel: lost the way to {destination.Name} when the road ahead was refreshed.", this);
                Cancel();
                return false;
            }

            traveller.ContinueOn(route, originDelta);
            return true;
        }

        /// <summary>
        /// Chooses where to travel. While auto-travelling, the route is recomputed towards the new
        /// destination (or travel stops when <paramref name="settlement"/> is <c>null</c>).
        /// </summary>
        public void SetDestination(Settlement settlement)
        {
            if (settlement == Destination) return;
            Destination = settlement;
            DestinationChanged?.Invoke(settlement);

            if (IsTravelling)
            {
                if (settlement == null) Cancel();
                else StartTravel();
            }
        }

        /// <summary>Starts auto-travel towards <see cref="Destination"/>, or stops it if already travelling.</summary>
        public void ToggleAutoTravel()
        {
            if (IsTravelling)
            {
                Cancel();
                return;
            }

            StartTravel();
        }

        /// <summary>Stops auto-travel (keeps the destination).</summary>
        public void Cancel()
        {
            if (traveller != null) traveller.Cancel();
        }

        private void Awake()
        {
            traveller = GetComponent<AutoTraveller>();
        }

        private void OnEnable()
        {
            traveller.Arrived += OnArrived;
            traveller.Cancelled += OnCancelled;
            traveller.Blocked += OnBlocked;
        }

        private void OnDisable()
        {
            traveller.Arrived -= OnArrived;
            traveller.Cancelled -= OnCancelled;
            traveller.Blocked -= OnBlocked;
        }

        private static readonly int CostStart = Review.FrameCost.Register("PlayerTravel.StartTravel (pick + route)");

        private bool StartTravel()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(CostStart);   // stall hunt: the per-frame culprit
            if (DestinationPicker != null)
            {
                Settlement picked = DestinationPicker.PickDestination(this, out string notice);
                if (picked == null)
                {
                    if (!string.IsNullOrEmpty(notice)) Notice?.Invoke(notice);
                    return false;
                }

                if (picked != Destination)
                {
                    Destination = picked;
                    DestinationChanged?.Invoke(picked);
                }
            }

            RoadNetwork roads = GameWorld.Current != null ? GameWorld.Current.Roads : null;
            if (roads == null || roads.Segments.Count == 0)
            {
                Debug.LogWarning("Auto-travel: there is no GameWorld with roads in this scene.", this);
                return false;
            }

            Vector3 position = transform.position;
            RoadLocation start = roads.Project(position);
            Vector3 onRoad = start.Position;
            float offRoad = new Vector2(onRoad.x - position.x, onRoad.z - position.z).magnitude;
            if (offRoad > maxApproachDistance)
            {
                Debug.LogWarning($"Auto-travel: the nearest road is {offRoad:0} m away (limit {maxApproachDistance:0} m). Walk closer to a road first.", this);
                Notice?.Invoke("The road is too far away. Walk closer to it first.");
                return false;
            }

            if (Destination == null)
            {
                Settlement farthest = FarthestReachable(roads, start);
                if (farthest == null)
                {
                    Debug.LogWarning("Auto-travel: no destination is set and no settlement can be reached from here.", this);
                    Notice?.Invoke("No destination. Open the map (M) or read a signpost.");
                    return false;
                }

                if (logEvents) Debug.Log($"Auto-travel (debug): no destination set, picked the farthest reachable settlement: {farthest.Name}.", this);
                Destination = farthest;
                DestinationChanged?.Invoke(farthest);
            }

            if (!roads.TryFindRoute(start, Destination, out RoadRoute route))
            {
                Debug.LogWarning($"Auto-travel: {Destination.Name} cannot be reached by road from here.", this);
                Notice?.Invoke($"{Destination.Name} cannot be reached by road from here.");
                return false;
            }

            if (route.TotalLength < 1f && Vector3.Distance(position, Destination.Position) < Mathf.Max(2f, Destination.Radius))
            {
                if (logEvents) Debug.Log($"Auto-travel: you are already at {Destination.Name}.", this);
                Notice?.Invoke($"You are already at {Destination.Name}.");
                return false;
            }

            if (offRoad > approachThreshold) traveller.Begin(route, onRoad);
            else traveller.Begin(route);
            Notice?.Invoke($"Setting out for {Destination.Name}");
            if (logEvents) Debug.Log($"Auto-travel: setting out for {Destination.Name} ({route.TotalLength:0} m by road).", this);
            return true;
        }

        private static Settlement FarthestReachable(RoadNetwork roads, RoadLocation start)
        {
            Settlement best = null;
            float bestLength = -1f;
            var settlements = roads.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                if (roads.TryFindRoute(start, settlements[i], out RoadRoute route) && route.TotalLength > bestLength)
                {
                    bestLength = route.TotalLength;
                    best = settlements[i];
                }
            }

            return best;
        }

        private static readonly int CostArrived = Review.FrameCost.Register("PlayerTravel.OnArrived (arrival handlers)");

        private void OnArrived(RoadRoute route)
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(CostArrived);   // stall hunt: the per-frame culprit
            Settlement reached = route.Destination;
            if (logEvents) Debug.Log($"You arrive at {reached.Name}.", this);
            Arrived?.Invoke(reached);
            if (clearDestinationOnArrival && Destination == reached) SetDestination(null);
        }

        private void OnBlocked(RoadRoute route)
        {
            if (logEvents) Debug.Log("Auto-travel: the way is blocked.", this);
            Notice?.Invoke("The way is blocked");
        }

        private void OnCancelled(RoadRoute route)
        {
            if (logEvents) Debug.Log("Auto-travel stopped.", this);
        }
    }
}
