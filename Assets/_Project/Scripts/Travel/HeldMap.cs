using MedievalZen.Player;
using MedievalZen.Roads;
using MedievalZen.UI;
using MedievalZen.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedievalZen.Travel
{
    /// <summary>
    /// The physical map (<c>M</c>): a gently curled parchment sheet parented to the player camera that rises from below
    /// into the lower half of the view (in third person the camera also eases in over the shoulder). The parchment is
    /// painted once per world by <see cref="MapTextureRenderer"/> at scene start; a <see cref="MapOverlay"/> adds names and
    /// live markers. While open the map takes over look input (<see cref="IPlayerInputOverride"/>): look moves a pointer
    /// over the paper, hovering a settlement shows its road distance, click / Interact sets it as the destination and the
    /// route appears dotted. Walking is allowed at half pace; auto-travel and time keep running. <c>M</c> / <c>Esc</c> fold it away.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-45)]
    public sealed class HeldMap : MonoBehaviour, IPlayerInputOverride
    {
        [Header("References (found on this GameObject when empty)")]
        [SerializeField] private PlayerController controller;
        [SerializeField] private PlayerCameraRig cameraRig;
        [SerializeField] private PlayerTravel travel;

        [SerializeField, Tooltip("Shader for the parchment (URP Unlit; assigned by the demo-scene setup so builds include it).")]
        private Shader sheetShader = null;

        [Header("Sheet")]
        [SerializeField, Range(256, 2048), Tooltip("Parchment texture size in pixels.")]
        private int textureSize = 1024;

        [SerializeField, Min(0.05f), Tooltip("Width (and height) of the sheet in metres.")]
        private float sheetSize = 0.34f;

        [SerializeField, Min(0f), Tooltip("How far the sheet edges curl away from the viewer, in metres.")]
        private float curl = 0.01f;

        [SerializeField, Tooltip("Sheet position in camera space when held in first person.")]
        private Vector3 heldPositionFirstPerson = new Vector3(0f, -0.1f, 0.42f);

        [SerializeField, Tooltip("Sheet position in camera space when held in third person (right of the character's head).")]
        private Vector3 heldPositionThirdPerson = new Vector3(0.08f, -0.09f, 0.45f);

        [SerializeField, Range(0f, 80f), Tooltip("Tilt of the held sheet in degrees (top edge leaning away).")]
        private float heldTilt = 30f;

        [SerializeField, Tooltip("Sheet position in camera space when lowered (start/end of the animation).")]
        private Vector3 loweredPosition = new Vector3(0f, -0.45f, 0.32f);

        [SerializeField, Range(0f, 90f), Tooltip("Tilt of the lowered sheet in degrees.")]
        private float loweredTilt = 75f;

        [SerializeField, Min(0.05f), Tooltip("Seconds to raise or lower the map.")]
        private float animationSeconds = 0.4f;

        [Header("Third-person framing while reading")]
        [SerializeField, Min(0.3f)] private float readingDistance = 1.25f;
        [SerializeField] private float readingShoulderOffset = 0.55f;
        [SerializeField] private float readingPivotRaise = 0.1f;

        [Header("Pointer")]
        [SerializeField, Min(0f), Tooltip("Sheet UV per pixel of mouse movement.")]
        private float pointerSensitivity = 0.0009f;

        [SerializeField, Min(0f), Tooltip("Sheet UV per second at full stick deflection.")]
        private float stickPointerSpeed = 0.35f;

        [SerializeField, Range(0.005f, 0.1f), Tooltip("A settlement is hovered when the pointer is within this UV distance.")]
        private float hoverRadius = 0.028f;

        [SerializeField, Range(0f, 1f), Tooltip("Walking pace while the map is open.")]
        private float walkSpeedScale = 0.5f;

        private Transform sheet;
        private Material sheetMaterial;
        private MapOverlay overlay;
        private MapProjection projection;
        private Texture2D texture;
        private RoadNetwork roads;
        private Vector2 pointer;
        private float raise;               // 0 = lowered, 1 = held
        private float modeBlend;           // 0 = first person pose, 1 = third person pose
        private int hovered = -1;
        private string hoverPrompt;
        private float nextRouteRefresh;

        /// <summary>True while the map is held up (or rising).</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The world ↔ sheet projection.</summary>
        public MapProjection Projection => projection;

        /// <inheritdoc />
        public float MoveSpeedScale => walkSpeedScale;

        /// <summary>World position → sheet UV.</summary>
        public Vector2 WorldToMapUV(Vector3 world) => projection.WorldToMapUV(world);

        /// <summary>Sheet UV → world position (height 0).</summary>
        public Vector3 MapUVToWorld(Vector2 uv) => projection.MapUVToWorld(uv);

        /// <summary>Raises the map.</summary>
        public void Open()
        {
            if (IsOpen || sheet == null) return;
            if (controller != null && controller.InputOverride != null && !ReferenceEquals(controller.InputOverride, this)) return;
            IsOpen = true;
            if (controller != null) controller.InputOverride = this;
            if (cameraRig != null) cameraRig.SetFramingOverride(readingDistance, readingShoulderOffset, readingPivotRaise, animationSeconds * 1.5f);
            sheet.gameObject.SetActive(true);
            pointer = ClampPointer(projection.WorldToMapUV(transform.position));
            hovered = -1;
            overlay.SetHover(-1, Vector2.zero, null);
            nextRouteRefresh = 0f;
            HudController hud = HudController.Instance;
            if (hud != null) hud.MapMode = true;
            SetPrompt(null);
        }

        /// <summary>Folds the map away.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (controller != null && ReferenceEquals(controller.InputOverride, this)) controller.InputOverride = null;
            if (cameraRig != null) cameraRig.ClearFramingOverride(animationSeconds * 1.5f);
            HudController hud = HudController.Instance;
            if (hud != null)
            {
                hud.MapMode = false;
                hud.HidePrompt();
            }
        }

        /// <summary>Opens or closes the map.</summary>
        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        // ------------------------------------------------------------------ input override

        /// <inheritdoc />
        public bool OnLook(Vector2 input, bool isPointerDelta)
        {
            Vector2 delta = isPointerDelta ? input * pointerSensitivity : input * (stickPointerSpeed * Time.deltaTime);
            pointer = ClampPointer(pointer + delta);
            return true;
        }

        /// <inheritdoc />
        public bool OnMove(Vector2 move) => false;

        /// <inheritdoc />
        public bool OnInteract()
        {
            SelectHovered();
            return true;
        }

        /// <inheritdoc />
        public bool OnMap()
        {
            Close();
            return true;
        }

        /// <inheritdoc />
        public bool OnAutoTravel() => false;

        /// <inheritdoc />
        public bool OnPause()
        {
            Close();
            return true;
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            if (cameraRig == null) cameraRig = GetComponent<PlayerCameraRig>();
            if (travel == null) travel = GetComponent<PlayerTravel>();
        }

        private void OnEnable()
        {
            if (controller != null) controller.MapRequested += Toggle;
            if (travel != null) travel.DestinationChanged += OnDestinationChanged;
        }

        private void OnDisable()
        {
            if (controller != null) controller.MapRequested -= Toggle;
            if (travel != null) travel.DestinationChanged -= OnDestinationChanged;
            Close();
        }

        private void Start()
        {
            GameWorld world = GameWorld.Current;
            Camera view = cameraRig != null ? cameraRig.Camera : GetComponentInChildren<Camera>();
            if (world == null || view == null)
            {
                Debug.LogWarning("HeldMap: needs a GameWorld in the scene and a camera on the player rig; the map is disabled.", this);
                enabled = false;
                return;
            }

            roads = world.Roads;
            projection = MapTextureRenderer.ProjectionFor(world);
            texture = MapTextureRenderer.Render(world, projection, textureSize);
            BuildSheet(view.transform, world);
            modeBlend = cameraRig != null && cameraRig.Mode == CameraMode.ThirdPerson ? 1f : 0f;
        }

        private void OnDestroy()
        {
            if (texture != null) Destroy(texture);
            if (sheetMaterial != null) Destroy(sheetMaterial);
        }

        private void Update()
        {
            if (sheet == null) return;
            float dt = Time.deltaTime;
            raise = Mathf.MoveTowards(raise, IsOpen ? 1f : 0f, dt / animationSeconds);
            float targetMode = cameraRig != null && cameraRig.Mode == CameraMode.ThirdPerson ? 1f : 0f;
            modeBlend = Mathf.MoveTowards(modeBlend, targetMode, dt / 0.35f);

            if (!IsOpen && raise <= 0f)
            {
                if (sheet.gameObject.activeSelf) sheet.gameObject.SetActive(false);
                return;
            }

            float k = Mathf.SmoothStep(0f, 1f, raise);
            Vector3 held = Vector3.Lerp(heldPositionFirstPerson, heldPositionThirdPerson, Mathf.SmoothStep(0f, 1f, modeBlend));
            sheet.localPosition = Vector3.LerpUnclamped(loweredPosition, held, k);
            sheet.localRotation = Quaternion.Euler(Mathf.Lerp(loweredTilt, heldTilt, k), 0f, 0f);
            UpdateSheetTint();

            overlay.SetPlayer(projection.WorldToMapUV(transform.position), transform.eulerAngles.y);
            overlay.Animate(Time.time);
            if (!IsOpen) return;

            overlay.SetPointer(pointer);
            UpdateHover();
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) SelectHovered();
            if (Time.time >= nextRouteRefresh) RefreshRoute();
        }

        // ------------------------------------------------------------------ behaviour

        private void UpdateHover()
        {
            int best = -1;
            if (roads != null)
            {
                float bestSqr = hoverRadius * hoverRadius;
                var settlements = roads.Settlements;
                for (int i = 0; i < settlements.Count; i++)
                {
                    float sqr = (projection.WorldToMapUV(settlements[i].Position) - pointer).sqrMagnitude;
                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        best = i;
                    }
                }
            }

            if (best == hovered) return;
            hovered = best;
            if (best < 0)
            {
                overlay.SetHover(-1, Vector2.zero, null);
                SetPrompt(null);
                return;
            }

            Settlement s = roads.Settlements[best];
            float distance = RoadDistanceTo(s, out _);
            string kind = s.Type == SettlementType.Town ? "town" : s.Type == SettlementType.Village ? "village" : "inn";
            string text = distance >= 0f ? $"{s.Name}, {kind}\n{UiKit.FormatDistance(distance)} by road" : $"{s.Name}, {kind}";
            overlay.SetHover(best, projection.WorldToMapUV(s.Position), text);
            SetPrompt($"Click / E — travel to {s.Name}   ·   M — fold the map");
        }

        private void SelectHovered()
        {
            if (hovered < 0 || roads == null || travel == null) return;
            Settlement s = roads.Settlements[hovered];
            HudController hud = HudController.Instance;
            if (travel.Destination == s)
            {
                if (hud != null) hud.ShowToast(travel.IsTravelling ? $"On the way to {s.Name}" : $"{s.Name} is marked. Press T to set off.");
                return;
            }

            travel.SetDestination(s);
            if (hud != null) hud.ShowToast(travel.IsTravelling ? $"New destination: {s.Name}" : $"Destination: {s.Name}. Press T to set off.");
        }

        private void OnDestinationChanged(Settlement settlement)
        {
            nextRouteRefresh = 0f;
            if (IsOpen) RefreshRoute();
        }

        private void RefreshRoute()
        {
            nextRouteRefresh = Time.time + 1.5f;
            Settlement target = travel != null ? travel.Destination : null;
            overlay.SetDestination(target != null, target != null ? projection.WorldToMapUV(target.Position) : Vector2.zero);
            if (target == null)
            {
                overlay.SetRoute(null, 0f, projection);
                return;
            }

            AutoTraveller traveller = travel.Traveller;
            if (traveller != null && traveller.IsActive && traveller.Route != null && traveller.Route.Destination == target)
            {
                overlay.SetRoute(traveller.Route, traveller.Follower.Distance, projection);
                return;
            }

            RoadDistanceTo(target, out RoadRoute route);
            overlay.SetRoute(route, 0f, projection);
        }

        /// <summary>Road distance from the player to <paramref name="settlement"/> (including the walk to the road), or −1.</summary>
        private float RoadDistanceTo(Settlement settlement, out RoadRoute route)
        {
            route = null;
            if (roads == null || roads.Segments.Count == 0) return -1f;
            Vector3 position = transform.position;
            RoadLocation start = roads.Project(position);
            if (!roads.TryFindRoute(start, settlement, out route)) return -1f;
            Vector3 onRoad = start.Position;
            return route.TotalLength + new Vector2(onRoad.x - position.x, onRoad.z - position.z).magnitude;
        }

        private void SetPrompt(string text)
        {
            HudController hud = HudController.Instance;
            if (hud == null) return;
            string prompt = text ?? "Look to move the pointer   ·   M — fold the map";
            if (prompt == hoverPrompt && IsOpen) return;
            hoverPrompt = prompt;
            hud.ShowPrompt(prompt);
        }

        private Vector2 ClampPointer(Vector2 uv)
        {
            float lo = projection.Margin * 0.5f;
            return new Vector2(Mathf.Clamp(uv.x, lo, 1f - lo), Mathf.Clamp(uv.y, lo, 1f - lo));
        }

        private void UpdateSheetTint()
        {
            // Unlit parchment, dimmed a little with the ambient light so it does not glow at night.
            Color ambient = RenderSettings.ambientSkyColor;
            float light = Mathf.Clamp(0.4f + 1.1f * (0.3f * ambient.r + 0.55f * ambient.g + 0.15f * ambient.b), 0.55f, 1f);
            sheetMaterial.SetColor(BaseColorId, new Color(light, light, light * 0.97f, 1f));
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        private void BuildSheet(Transform cameraTransform, GameWorld world)
        {
            var go = new GameObject("HeldMap");
            go.layer = cameraTransform.gameObject.layer;
            sheet = go.transform;
            sheet.SetParent(cameraTransform, false);
            sheet.localPosition = loweredPosition;
            sheet.localRotation = Quaternion.Euler(loweredTilt, 0f, 0f);

            go.AddComponent<MeshFilter>().sharedMesh = BuildMesh(sheetSize, curl, 10);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Shader shader = sheetShader != null ? sheetShader : Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            sheetMaterial = new Material(shader) { name = "HeldMap" };
            sheetMaterial.mainTexture = texture;
            if (sheetMaterial.HasProperty(BaseMapId)) sheetMaterial.SetTexture(BaseMapId, texture);
            if (sheetMaterial.HasProperty("_Cull")) sheetMaterial.SetFloat("_Cull", 0f);
            renderer.sharedMaterial = sheetMaterial;

            overlay = go.AddComponent<MapOverlay>();
            overlay.Build(projection, sheetSize, 0.002f, roads, world.Landmarks);
            go.SetActive(false);
        }

        /// <summary>A (segments+1)² grid facing −Z (the camera), UV 0..1, edges curling away along +Z.</summary>
        private static Mesh BuildMesh(float size, float curlDepth, int segments)
        {
            int n = segments + 1;
            var vertices = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)segments;
                    float v = y / (float)segments;
                    float cx = u * 2f - 1f;
                    float cy = v * 2f - 1f;
                    float z = curlDepth * (cx * cx * 0.8f + cy * cy * cy * cy * 0.35f);
                    vertices[y * n + x] = new Vector3((u - 0.5f) * size, (v - 0.5f) * size, z);
                    uvs[y * n + x] = new Vector2(u, v);
                }
            }

            var triangles = new int[segments * segments * 6];
            int t = 0;
            for (int y = 0; y < segments; y++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int bl = y * n + x;
                    int br = bl + 1;
                    int tl = bl + n;
                    int tr = tl + 1;
                    triangles[t++] = bl; triangles[t++] = tl; triangles[t++] = br;
                    triangles[t++] = br; triangles[t++] = tl; triangles[t++] = tr;
                }
            }

            var mesh = new Mesh { name = "HeldMapSheet" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
