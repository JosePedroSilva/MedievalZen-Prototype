using System;
using System.Collections.Generic;
using MedievalZen.World;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MedievalZen.Environment
{
    /// <summary>
    /// Keeps a handful of pooled Synty ambient particle prefabs alive around the player: butterflies and petals over open
    /// meadow (by day), drifting leaves under forest, and the odd sun beam under trees around noon. Forest vs meadow comes
    /// from a coarse <see cref="TreeDensityGrid"/> built once from the Terrain's tree instances. Instances further than
    /// <see cref="despawnDistance"/> are deactivated and reused. Cosmetic only, so it uses <c>UnityEngine.Random</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AmbientFxSpawner : MonoBehaviour
    {
        private sealed class Slot
        {
            public GameObject Prefab;
            public GameObject Instance;
            public bool IsSunBeam;
        }

        [Header("References")]
        [SerializeField, Tooltip("Transform the effects gather around (the player rig). Falls back to the main camera.")]
        private Transform target;

        [SerializeField, Tooltip("Terrain to read tree density and heights from. Defaults to GameWorld.Current.Terrain.")]
        private Terrain terrain;

        [Header("Prefabs")]
        [SerializeField, Tooltip("Effects for open meadow by day (butterflies, petals).")]
        private GameObject[] meadowFx = Array.Empty<GameObject>();

        [SerializeField, Tooltip("Effects under forest (falling / blowing leaves).")]
        private GameObject[] forestFx = Array.Empty<GameObject>();

        [SerializeField, Tooltip("Sun beam effect placed under trees around noon.")]
        private GameObject sunBeamFx = null;

        [Header("Placement")]
        [SerializeField, Range(0, 12), Tooltip("Fewest effects to keep alive.")]
        private int minActive = 3;

        [SerializeField, Range(1, 12), Tooltip("Most effects alive at once.")]
        private int maxActive = 6;

        [SerializeField, Min(1f), Tooltip("Closest spawn distance from the player, in metres.")]
        private float minSpawnDistance = 8f;

        [SerializeField, Min(2f), Tooltip("Furthest spawn distance from the player, in metres.")]
        private float maxSpawnDistance = 28f;

        [SerializeField, Min(5f), Tooltip("Effects further than this from the player are recycled.")]
        private float despawnDistance = 40f;

        [SerializeField, Min(0.1f), Tooltip("Seconds between spawn/despawn checks (one spawn per check keeps it gentle).")]
        private float checkInterval = 1.2f;

        [SerializeField, Min(0f), Tooltip("Trees per 100 m² at or below which the ground counts as meadow.")]
        private float meadowMaxDensity = 0.15f;

        [SerializeField, Min(0f), Tooltip("Trees per 100 m² at or above which the ground counts as forest.")]
        private float forestMinDensity = 0.8f;

        [SerializeField, Tooltip("Hours of the day during which sun beams may appear under trees.")]
        private Vector2 sunBeamHours = new Vector2(10f, 15f);

        [SerializeField, Min(1f), Tooltip("Tree density grid cell size in metres.")]
        private float gridCellSize = 16f;

        private readonly List<Slot> active = new List<Slot>(12);
        private readonly Dictionary<GameObject, Stack<GameObject>> pool = new Dictionary<GameObject, Stack<GameObject>>();
        private TreeDensityGrid grid;
        private float nextCheck;
        private int wanted;

        private void Start()
        {
            if (terrain == null && GameWorld.Current != null) terrain = GameWorld.Current.Terrain;
            grid = TreeDensityGrid.FromTerrain(terrain, gridCellSize);
            wanted = Random.Range(minActive, Mathf.Max(minActive, maxActive) + 1);
        }

        private void Update()
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;
            if (target == null)
            {
                Camera main = Camera.main;
                if (main == null) return;
                target = main.transform;
            }

            Vector3 centre = target.position;
            float despawnSqr = despawnDistance * despawnDistance;
            bool sunBeamAllowed = IsSunBeamTime();
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Slot slot = active[i];
                Vector3 offset = slot.Instance != null ? slot.Instance.transform.position - centre : Vector3.zero;
                offset.y = 0f;
                if (slot.Instance == null || offset.sqrMagnitude > despawnSqr || (slot.IsSunBeam && !sunBeamAllowed))
                {
                    Recycle(slot);
                    active.RemoveAt(i);
                }
            }

            if (active.Count < wanted) TrySpawn(centre, sunBeamAllowed);
            if (Random.value < 0.05f) wanted = Random.Range(minActive, Mathf.Max(minActive, maxActive) + 1);
        }

        private bool IsSunBeamTime()
        {
            DayCycle cycle = DayCycle.Current;
            if (cycle == null) return true;
            return cycle.Hour >= sunBeamHours.x && cycle.Hour <= sunBeamHours.y;
        }

        private void TrySpawn(Vector3 centre, bool sunBeamAllowed)
        {
            if (terrain == null || grid == null) return;
            Vector2 dir = Random.insideUnitCircle;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector2.up;
            dir.Normalize();
            float distance = Random.Range(minSpawnDistance, Mathf.Max(minSpawnDistance, maxSpawnDistance));
            var position = new Vector3(centre.x + dir.x * distance, 0f, centre.z + dir.y * distance);

            float ground = terrain.SampleHeight(position) + terrain.GetPosition().y;
            float water = GameWorld.Current != null ? GameWorld.Current.WaterHeight : -1f;
            if (water > 0f && ground < water + 0.2f) return;

            float density = grid.Sample(position.x, position.z);
            bool day = DayCycle.Current == null || DayCycle.Current.IsDay;
            GameObject prefab = null;
            bool isSunBeam = false;
            float lift = 0f;
            if (density >= forestMinDensity)
            {
                if (sunBeamFx != null && sunBeamAllowed && !HasSunBeam() && Random.value < 0.4f)
                {
                    prefab = sunBeamFx;
                    isSunBeam = true;
                }
                else
                {
                    prefab = Pick(forestFx);
                    lift = 2.5f;
                }
            }
            else if (density <= meadowMaxDensity && day)
            {
                prefab = Pick(meadowFx);
                lift = 0.8f;
            }

            if (prefab == null) return;
            position.y = ground + lift;
            GameObject instance = Take(prefab);
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            instance.SetActive(true);
            active.Add(new Slot { Prefab = prefab, Instance = instance, IsSunBeam = isSunBeam });
        }

        private bool HasSunBeam()
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].IsSunBeam) return true;
            }

            return false;
        }

        private static GameObject Pick(GameObject[] prefabs)
        {
            if (prefabs == null || prefabs.Length == 0) return null;
            return prefabs[Random.Range(0, prefabs.Length)];
        }

        private GameObject Take(GameObject prefab)
        {
            if (pool.TryGetValue(prefab, out Stack<GameObject> stack))
            {
                while (stack.Count > 0)
                {
                    GameObject pooled = stack.Pop();
                    if (pooled != null) return pooled;
                }
            }

            GameObject instance = Instantiate(prefab, transform);
            instance.SetActive(false);
            return instance;
        }

        private void Recycle(Slot slot)
        {
            if (slot.Instance == null) return;
            slot.Instance.SetActive(false);
            if (!pool.TryGetValue(slot.Prefab, out Stack<GameObject> stack))
            {
                stack = new Stack<GameObject>(2);
                pool[slot.Prefab] = stack;
            }

            stack.Push(slot.Instance);
        }
    }
}
