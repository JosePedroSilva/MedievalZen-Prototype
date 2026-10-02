using System.Collections.Generic;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>
    /// Scene-level owner of the generated world: its settings, palette, terrain, road network and landmarks.
    /// The road network is stored as <see cref="RoadNetworkData"/> and rebuilt lazily at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class GameWorld : MonoBehaviour, IWorldSurface
    {
        private const int GizmoCircleSegments = 48;

        [SerializeField, Tooltip("Settings the world was (or will be) generated from.")]
        private WorldSettings settings;

        [SerializeField, Tooltip("Prefabs, terrain layers and materials the world is dressed with.")]
        private WorldPalette palette;

        [SerializeField, Tooltip("Generate and build the world at Start when no roads have been generated yet (the editor menu MedievalZen/Generate World bakes it into the scene instead).")]
        private bool generateOnStart;

        [SerializeField, HideInInspector]
        private List<Landmark> landmarks = new List<Landmark>();

        [SerializeField, HideInInspector]
        private float waterHeight = -1f;

        [SerializeField, Tooltip("Terrain created by world generation.")]
        private Terrain terrain;

        [SerializeField, HideInInspector]
        private RoadNetworkData roadData = new RoadNetworkData();

        private RoadNetwork roads;
        private IWorldSurface surfaceOverride;

        /// <summary>The active world in the loaded scene (set in Awake, cleared in OnDestroy).</summary>
        public static GameWorld Current { get; private set; }

        /// <summary>Settings the world was generated from.</summary>
        public WorldSettings Settings
        {
            get => settings;
            set => settings = value;
        }

        /// <summary>Palette the world is (or will be) dressed with.</summary>
        public WorldPalette Palette
        {
            get => palette;
            set => palette = value;
        }

        /// <summary>If true, <see cref="Start"/> generates the world when the scene has no baked roads.</summary>
        public bool GenerateOnStart
        {
            get => generateOnStart;
            set => generateOnStart = value;
        }

        /// <summary>Named natural landmarks (tallest peak, lakes) of the generated world.</summary>
        public IReadOnlyList<Landmark> Landmarks => landmarks;

        /// <summary>Lake level in metres, or a negative value when the world has no lakes.</summary>
        public float WaterHeight => waterHeight;

        /// <summary>Stores the natural landmarks and lake level of a generated world (editor callers mark dirty).</summary>
        public void SetNature(IReadOnlyList<Landmark> newLandmarks, float newWaterHeight)
        {
            landmarks = newLandmarks != null ? new List<Landmark>(newLandmarks) : new List<Landmark>();
            waterHeight = newWaterHeight;
        }

        /// <summary>The world terrain.</summary>
        public Terrain Terrain
        {
            get => terrain;
            set => terrain = value;
        }

        /// <summary>The road network, rebuilt from the serialised data on first access.</summary>
        public RoadNetwork Roads => roads ??= (roadData ?? new RoadNetworkData()).ToNetwork();

        /// <summary>
        /// Replaces the road network and stores its serialisable snapshot. Editor callers must record
        /// undo / mark this component dirty themselves so the change is saved with the scene.
        /// </summary>
        public void SetRoads(RoadNetwork network)
        {
            roadData = network != null ? RoadNetworkData.From(network) : new RoadNetworkData();
            roads = network;
        }

        /// <summary>
        /// What the atmosphere and ambience systems treat as the ground: this world's own terrain (the finite world), or the
        /// surface a streamer supplied with <see cref="SetSurface"/> (the endless road).
        /// </summary>
        public IWorldSurface Surface => surfaceOverride ?? this;

        /// <summary>Replaces <see cref="Surface"/> (null: back to this world's terrain). Runtime only, not saved.</summary>
        public void SetSurface(IWorldSurface surface)
        {
            surfaceOverride = ReferenceEquals(surface, this) ? null : surface;
        }

        // ------------------------------------------------------------------ IWorldSurface over the baked terrain

        /// <inheritdoc />
        bool IWorldSurface.HasGround => terrain != null && terrain.terrainData != null;

        /// <inheritdoc />
        bool IWorldSurface.IsDynamic => false;

        /// <inheritdoc />
        float IWorldSurface.GroundHeight(Vector3 scenePosition) => TerrainSurface.Height(terrain, scenePosition);

        /// <inheritdoc />
        float IWorldSurface.WaterHeight(Vector3 scenePosition) => waterHeight;

        /// <inheritdoc />
        ITreeDensity IWorldSurface.CreateTreeDensity(float cellSize) => MedievalZen.Environment.TreeDensityGrid.FromTerrain(terrain, cellSize);

        /// <inheritdoc />
        bool IWorldSurface.TryGetFogBase(Vector3 scenePosition, out float altitude) => TerrainSurface.FogBase(terrain, waterHeight, out altitude);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Supports "Enter Play Mode" without domain reload.
            Current = null;
        }

        private void Awake()
        {
            if (Current != null && Current != this)
            {
                Debug.LogWarning($"Multiple GameWorld components found; '{name}' replaces '{Current.name}' as GameWorld.Current.", this);
            }

            Current = this;
        }

        private void Start()
        {
            if (!generateOnStart || Roads.Segments.Count > 0) return;
            if (settings == null || palette == null)
            {
                Debug.LogWarning("GameWorld: Generate On Start needs both WorldSettings and a WorldPalette.", this);
                return;
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            WorldBlueprint blueprint = WorldGenerator.Generate(settings, palette.GetKeys());
            WorldBuilder.Build(this, settings, palette, blueprint);
            Debug.Log($"GameWorld: generated world seed {settings.seed} at Start in {clock.ElapsedMilliseconds} ms.", this);
        }

        private void OnDestroy()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        private void OnValidate()
        {
            // Serialised data may have changed (undo, inspector, re-import): rebuild on next access.
            roads = null;
        }

        private void OnDrawGizmosSelected()
        {
            RoadNetwork network = Roads;

            Gizmos.color = new Color(0.85f, 0.65f, 0.35f);
            foreach (RoadSegment segment in network.Segments)
            {
                var points = segment.Points;
                for (int i = 1; i < points.Count; i++)
                {
                    Gizmos.DrawLine(points[i - 1], points[i]);
                }
            }

            foreach (RoadNode node in network.Nodes)
            {
                Gizmos.color = node.IsJunction ? Color.yellow : Color.white;
                Gizmos.DrawSphere(node.Position, 1.5f);
            }

            foreach (Settlement settlement in network.Settlements)
            {
                Gizmos.color = ColorFor(settlement.Type);
                DrawWireDisc(settlement.Position, settlement.Radius);
            }
        }

        private static Color ColorFor(SettlementType type)
        {
            switch (type)
            {
                case SettlementType.Town: return new Color(0.9f, 0.3f, 0.25f);
                case SettlementType.Village: return new Color(0.35f, 0.75f, 0.3f);
                case SettlementType.Inn: return new Color(0.3f, 0.55f, 0.95f);
                default: return Color.gray;
            }
        }

        private static void DrawWireDisc(Vector3 center, float radius)
        {
            Vector3 previous = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= GizmoCircleSegments; i++)
            {
                float angle = i * (2f * Mathf.PI / GizmoCircleSegments);
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
