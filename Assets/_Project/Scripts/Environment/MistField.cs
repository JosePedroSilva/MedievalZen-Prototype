using System;
using MedievalZen.World;
using UnityEngine;

namespace MedievalZen.Environment
{
    /// <summary>
    /// Low-lying mist patches (pooled soft particle sheets, Synty <c>FX_Fog_01</c>) over the lakes and in the lowest valley
    /// hollows near the player. Every few seconds it gathers candidate spots (lake landmarks, and local height minima of a
    /// coarse terrain sample close to the fog base altitude), keeps up to <c>AtmosphereController.MistAmount × maxActive</c>
    /// patches alive, and fades each one in/out with distance and the mist amount (strongest at dawn/dusk and in Misty
    /// weather). Patches fade by emission and particle alpha only, so there is never pop-in.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MistField : MonoBehaviour, IOriginShiftable
    {
        private sealed class Patch
        {
            public GameObject Instance;
            public ParticleSystem[] Systems;
            public float[] BaseRates;
            public Color[] BaseColors;
            public Vector3 Anchor;
            public float Level;
            public bool Wanted;
            public bool InUse;
        }

        [Header("References")]
        [SerializeField, Tooltip("Mist sheet effect (Synty PolygonGeneric FX_Fog_01).")]
        private GameObject mistPrefab = null;

        [SerializeField, Tooltip("Transform the mist gathers around (the player rig). Falls back to the main camera.")]
        private Transform target;

        [Header("Budget")]
        [SerializeField, Range(1, 16), Tooltip("Most mist patches alive at full mist.")]
        private int maxActive = 10;

        [SerializeField, Min(50f), Tooltip("Patches are placed within this distance of the player (metres).")]
        private float radius = 260f;

        [SerializeField, Min(10f), Tooltip("Patches fade out between 80 % of the radius and the radius.")]
        private float fadeBand = 60f;

        [SerializeField, Min(10f), Tooltip("Minimum distance between two patches (metres).")]
        private float spacing = 70f;

        [SerializeField, Min(0.5f), Tooltip("Seconds between placement passes.")]
        private float checkInterval = 2.5f;

        [SerializeField, Min(0.5f), Tooltip("Seconds a patch takes to fade fully in or out.")]
        private float fadeSeconds = 10f;

        [Header("Placement")]
        [SerializeField, Min(10f), Tooltip("Spacing of the coarse terrain sample used to find valley hollows (metres).")]
        private float sampleSpacing = 45f;

        [SerializeField, Min(0f), Tooltip("Hollows higher than this above the fog base altitude get no patch (metres).")]
        private float maxHeightAboveBase = 30f;

        [SerializeField, Tooltip("Height of a patch above the ground or water (metres).")]
        private float lift = 0.8f;

        [SerializeField, Tooltip("Random scale range of a patch.")]
        private Vector2 scaleRange = new Vector2(1f, 1.6f);

        private const int MaxCandidates = 96;

        private readonly Vector3[] candidates = new Vector3[MaxCandidates];
        private readonly float[] candidateScores = new float[MaxCandidates];
        private Patch[] patches = Array.Empty<Patch>();
        private float[] grid = Array.Empty<float>();
        private IWorldSurface surface;
        private float nextCheck;
        private System.Random random;

        private void Start()
        {
            random = new System.Random(System.Environment.TickCount);
            if (GameWorld.Current != null) surface = GameWorld.Current.Surface;
            patches = new Patch[Mathf.Max(1, maxActive)];
            for (int i = 0; i < patches.Length; i++) patches[i] = new Patch();
        }

        private void OnDisable()
        {
            for (int i = 0; i < patches.Length; i++)
            {
                if (patches[i].Instance != null) patches[i].Instance.SetActive(false);
                patches[i].InUse = false;
                patches[i].Level = 0f;
            }
        }

        private void Update()
        {
            if (mistPrefab == null) return;
            if (target == null)
            {
                Camera main = Camera.main;
                if (main == null) return;
                target = main.transform;
            }

            AtmosphereController atmosphere = AtmosphereController.Current;
            float amount = atmosphere != null ? atmosphere.MistAmount : 0.3f;
            Vector3 centre = target.position;

            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + checkInterval;
                Place(centre, amount, atmosphere != null ? atmosphere.BaseAltitude : float.NegativeInfinity);
            }

            float step = Time.deltaTime / Mathf.Max(0.1f, fadeSeconds);
            for (int i = 0; i < patches.Length; i++)
            {
                Patch p = patches[i];
                if (!p.InUse) continue;
                float dx = p.Anchor.x - centre.x;
                float dz = p.Anchor.z - centre.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                float distanceFade = 1f - AtmosphereMath.SmoothRange(radius - fadeBand, radius, distance);
                float goal = p.Wanted ? Mathf.Clamp01(amount * 1.3f) * distanceFade : 0f;
                p.Level = Mathf.MoveTowards(p.Level, goal, step);
                ApplyLevel(p);
                if (!p.Wanted && p.Level <= 0f && !AnyAlive(p))
                {
                    p.Instance.SetActive(false);
                    p.InUse = false;
                }
            }
        }

        private void Place(Vector3 centre, float amount, float baseAltitude)
        {
            int count = GatherCandidates(centre, baseAltitude);
            int wanted = Mathf.Clamp(Mathf.RoundToInt(amount * patches.Length), 0, patches.Length);

            // Keep patches whose anchor is still a candidate, best-scored first; drop the rest.
            for (int i = 0; i < patches.Length; i++) patches[i].Wanted = false;
            int kept = 0;
            for (int c = 0; c < count && kept < wanted; c++)
            {
                int best = BestCandidate(count);
                if (best < 0) break;
                Vector3 spot = candidates[best];
                candidateScores[best] = float.NegativeInfinity;

                Patch existing = FindPatchNear(spot, spacing * 0.5f);
                if (existing != null)
                {
                    existing.Wanted = true;
                    kept++;
                    continue;
                }

                if (TooClose(spot)) continue;
                Patch free = FreePatch();
                if (free == null) break;
                Activate(free, spot);
                kept++;
            }
        }

        private int GatherCandidates(Vector3 centre, float baseAltitude)
        {
            int count = 0;
            GameWorld world = GameWorld.Current;
            float water = world != null ? world.WaterHeight : -1f;

            // Lakes: the lake centre and points around it.
            if (world != null && water > 0f)
            {
                var landmarks = world.Landmarks;
                for (int i = 0; i < landmarks.Count && count < MaxCandidates - 6; i++)
                {
                    Landmark lake = landmarks[i];
                    if (lake.kind != LandmarkKind.Lake) continue;
                    float r = Mathf.Max(20f, lake.radius);
                    Vector3 c = lake.position;
                    float dx = c.x - centre.x;
                    float dz = c.z - centre.z;
                    if (dx * dx + dz * dz > (radius + r) * (radius + r)) continue;
                    count = AddCandidate(count, new Vector3(c.x, water + lift, c.z), centre, 1.2f);
                    for (int k = 0; k < 5 && count < MaxCandidates; k++)
                    {
                        float angle = k * (Mathf.PI * 2f / 5f) + i;
                        var p = new Vector3(c.x + Mathf.Cos(angle) * r * 0.6f, water + lift, c.z + Mathf.Sin(angle) * r * 0.6f);
                        if (HasGround && GroundHeight(p) > water + 0.5f) continue;   // lake is not round there
                        count = AddCandidate(count, p, centre, 1.1f);
                    }
                }
            }

            // Valley hollows: local minima of a coarse height grid around the player.
            if (!HasGround) return count;
            int n = Mathf.Clamp(Mathf.CeilToInt(radius * 2f / sampleSpacing) + 1, 3, 17);
            if (grid.Length != n * n) grid = new float[n * n];
            float half = (n - 1) * 0.5f * sampleSpacing;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector3(Snap(centre.x) - half + x * sampleSpacing, 0f, Snap(centre.z) - half + z * sampleSpacing);
                    grid[z * n + x] = GroundHeight(p);
                }
            }

            float limit = float.IsNegativeInfinity(baseAltitude) ? float.PositiveInfinity : baseAltitude + maxHeightAboveBase;
            for (int z = 1; z < n - 1 && count < MaxCandidates; z++)
            {
                for (int x = 1; x < n - 1 && count < MaxCandidates; x++)
                {
                    float h = grid[z * n + x];
                    if (h > limit || (water > 0f && h < water + 0.2f)) continue;
                    bool minimum = true;
                    for (int dz = -1; dz <= 1 && minimum; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if ((dx != 0 || dz != 0) && grid[(z + dz) * n + x + dx] < h)
                            {
                                minimum = false;
                                break;
                            }
                        }
                    }

                    if (!minimum) continue;
                    var p = new Vector3(Snap(centre.x) - half + x * sampleSpacing, h + lift, Snap(centre.z) - half + z * sampleSpacing);
                    float lowness = float.IsInfinity(limit) ? 0.5f : 1f - Mathf.Clamp01((h - (limit - maxHeightAboveBase)) / Mathf.Max(1f, maxHeightAboveBase));
                    count = AddCandidate(count, p, centre, 0.6f + 0.5f * lowness);
                }
            }

            return count;
        }

        private float Snap(float v) => Mathf.Round(v / sampleSpacing) * sampleSpacing;

        private int AddCandidate(int count, Vector3 p, Vector3 centre, float weight)
        {
            if (count >= MaxCandidates) return count;
            float dx = p.x - centre.x;
            float dz = p.z - centre.z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            if (distance > radius) return count;

            // Prefer the middle distance (mist is best seen from 40–200 m), but allow walking through it.
            float score = weight * (1f - Mathf.Abs(distance - 120f) / (radius + 120f));
            candidates[count] = p;
            candidateScores[count] = score;
            return count + 1;
        }

        private int BestCandidate(int count)
        {
            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (candidateScores[i] > bestScore)
                {
                    bestScore = candidateScores[i];
                    best = i;
                }
            }

            return float.IsNegativeInfinity(bestScore) ? -1 : best;
        }

        private Patch FindPatchNear(Vector3 p, float distance)
        {
            float sqr = distance * distance;
            for (int i = 0; i < patches.Length; i++)
            {
                Patch patch = patches[i];
                if (!patch.InUse || patch.Wanted) continue;
                float dx = patch.Anchor.x - p.x;
                float dz = patch.Anchor.z - p.z;
                if (dx * dx + dz * dz <= sqr) return patch;
            }

            return null;
        }

        private bool TooClose(Vector3 p)
        {
            float sqr = spacing * spacing;
            for (int i = 0; i < patches.Length; i++)
            {
                Patch patch = patches[i];
                if (!patch.InUse || !patch.Wanted) continue;
                float dx = patch.Anchor.x - p.x;
                float dz = patch.Anchor.z - p.z;
                if (dx * dx + dz * dz < sqr) return true;
            }

            return false;
        }

        private Patch FreePatch()
        {
            for (int i = 0; i < patches.Length; i++)
            {
                if (!patches[i].InUse) return patches[i];
            }

            return null;
        }

        private void Activate(Patch patch, Vector3 spot)
        {
            if (patch.Instance == null)
            {
                patch.Instance = Instantiate(mistPrefab, transform);
                patch.Instance.name = mistPrefab.name;
                patch.Systems = patch.Instance.GetComponentsInChildren<ParticleSystem>(true);
                patch.BaseRates = new float[patch.Systems.Length];
                patch.BaseColors = new Color[patch.Systems.Length];
                for (int i = 0; i < patch.Systems.Length; i++)
                {
                    patch.BaseRates[i] = patch.Systems[i].emission.rateOverTimeMultiplier;
                    patch.BaseColors[i] = patch.Systems[i].main.startColor.color;
                }
            }

            float scale = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)random.NextDouble());
            patch.Instance.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
            patch.Instance.transform.localScale = new Vector3(scale, 1f, scale);
            patch.Anchor = spot;
            patch.Level = 0f;
            patch.Wanted = true;
            patch.InUse = true;
            ApplyLevel(patch);
            patch.Instance.SetActive(true);
            for (int i = 0; i < patch.Systems.Length; i++) patch.Systems[i].Play(false);
        }

        private static void ApplyLevel(Patch patch)
        {
            for (int i = 0; i < patch.Systems.Length; i++)
            {
                ParticleSystem ps = patch.Systems[i];
                if (ps == null) continue;
                ParticleSystem.EmissionModule emission = ps.emission;
                emission.rateOverTimeMultiplier = patch.BaseRates[i] * patch.Level;
                ParticleSystem.MainModule main = ps.main;
                Color c = patch.BaseColors[i];
                c.a *= Mathf.Lerp(0.35f, 1f, patch.Level);
                main.startColor = c;

                // Fading out: let the sheets drift away faster instead of lingering for their full 30 s life.
                main.simulationSpeed = patch.Wanted ? 1f : 2.5f;
            }
        }

        private static bool AnyAlive(Patch patch)
        {
            for (int i = 0; i < patch.Systems.Length; i++)
            {
                if (patch.Systems[i] != null && patch.Systems[i].particleCount > 0) return true;
            }

            return false;
        }

        private bool HasGround => surface != null && surface.HasGround;

        private float GroundHeight(Vector3 p) => surface.GroundHeight(p);

        /// <inheritdoc />
        public void ShiftOrigin(Vector3 delta)
        {
            for (int i = 0; i < patches.Length; i++)
            {
                Patch patch = patches[i];
                if (!patch.InUse) continue;
                patch.Anchor += delta;
                if (patch.Instance != null) patch.Instance.transform.position += delta;
            }
        }
    }
}
