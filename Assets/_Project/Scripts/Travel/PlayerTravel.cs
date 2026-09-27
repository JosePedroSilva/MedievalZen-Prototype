using System;
using MedievalZen.Roads;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Travel
{
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

        private bool StartTravel()
        {
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

        private void OnArrived(RoadRoute route)
        {
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
