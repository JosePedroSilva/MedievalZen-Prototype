using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MedievalZen.Roads;
using MedievalZen.Travel;
using MedievalZen.UI;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The endless road's scroll map behind <see cref="HeldMap"/>: a strip of country along the road (<see cref="StripProjection"/>)
    /// painted by <see cref="StripMapRenderer"/> in segments on worker threads, kept in a bounded cache, and copied slice by slice
    /// into a ring texture that the held sheet shows through a sliding UV window — so rolling the scroll costs nothing and opening
    /// it never waits. Segments around the traveller are prepared quietly while the map is folded (one job at a time, below
    /// normal thread priority, one upload slice every few frames); when it is opened the sheet shows at once with what is ready and
    /// fills in the rest within a moment.
    /// <para>
    /// Plain class owned by <see cref="HeldMap"/> (which calls <see cref="Tick"/> every frame, so the execution order is the held
    /// map's). Everything is computed in absolute coordinates; only "you are here" reads the traveller through
    /// <see cref="EndlessWorld.ToAbsolute"/>, so floating-origin shifts do not disturb it. A new road (another generator) resets
    /// every cache. The stretch of road reached is saved under <see cref="EndlessMapReach.PrefsKey"/>.
    /// </para>
    /// </summary>
    public sealed class EndlessScrollMap : IDisposable
    {
        /// <summary>Sheet width in metres (the held quad).</summary>
        public const float SheetWidth = 0.27f;

        /// <summary>Rows of the strip visible on the sheet.</summary>
        public const int WindowRows = 768;

        /// <summary>Strip metres visible on the sheet.</summary>
        public const double WindowLength = WindowRows * StripMapLayout.MetresPerPixel;

        /// <summary>Sheet height in metres (the strip's aspect).</summary>
        public const float SheetHeight = SheetWidth * WindowRows / StripMapLayout.Width;

        /// <summary>The scroll reaches this far behind the traveller, metres of road.</summary>
        public const double ExtentBehind = 1200.0;

        /// <summary>The scroll reaches at least this far ahead (and always past the next inn).</summary>
        public const double ExtentAhead = 2500.0;

        private const int RingSegments = 16;
        private const int RingRows = RingSegments * StripMapLayout.SegmentRows;
        private const int SliceRows = 64;
        private const int SlicesPerSegment = StripMapLayout.SegmentRows / SliceRows;
        private const int W = StripMapLayout.Width;
        private const double Mpp = StripMapLayout.MetresPerPixel;
        private const float EdgeZone = 0.09f;          // pointer within this UV of the top/bottom rolls the scroll
        private const float EdgeSpeed = 420f;          // metres per second at the very edge
        private const float WheelStep = 140f;          // metres per wheel notch
        private const float HoverMetres = 24f;

        private readonly Material material;
        private readonly EndlessMapOverlay overlay;
        private readonly Transform traveller;
        private readonly PlayerTravel travel;
        private readonly object resultGate = new object();
        private readonly ConcurrentQueue<JobResult> results = new ConcurrentQueue<JobResult>();
        private readonly HashSet<int> inFlight = new HashSet<int>();
        private readonly List<Want> wanted = new List<Want>(32);
        private readonly WantOrder order = new WantOrder();
        private readonly int[] slotSegment = new int[RingSegments];
        private readonly long[] slotKey = new long[RingSegments];
        private readonly Pending[] pending = new Pending[RingSegments];
        private readonly Dictionary<int, (double low, double high)> segmentRoad = new Dictionary<int, (double low, double high)>();
        private readonly Color32[] blank = new Color32[W * SliceRows];
        private readonly Color32[] sliceBuffer = new Color32[W * SliceRows];
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        private EndlessWorld world;
        private IEndlessGenerator generator;
        private StripProjection projection;
        private StripMapRenderer renderer;
        private StripSegmentCache cache;
        private EndlessMapReach reach;
        private bool reachDirty;
        private float nextReachSave;
        private int generation;
        private int lastErrorGeneration = -1;

        private RenderTexture ring;
        private Texture2D staging;
        private Texture2D fallback;
        private bool mipsDirty;

        private StripMapContent content;
        private Task<StripMapContent> contentJob;
        private int contentJobGeneration;
        private double contentS = double.NaN;
        private double extentLowV, extentHighV;
        private double plannedS = double.NaN;
        private long plannedReach;
        private float nextPlan;
        private float nextReveal;
        private int closedUploadFrame;

        private double windowBottom, windowTarget;
        private bool following = true;
        private Vector2 pointer = new Vector2(0.5f, 0.3f);
        private int hovered = -1;
        private double travellerV;
        private float nextDestination;
        private MapPicker picker;
        private int chosenWaypoint = -1;

        private struct Pending
        {
            public bool Active;
            public int Segment;
            public long Key;
            public Color32[] Pixels;   // null = blank paper
            public int Slice;
        }

        private struct Want
        {
            public int Segment;
            public long Key;
            public StripReveal.State State;
        }

        /// <summary>Nearest the focus first (a reusable comparer: planning does not allocate).</summary>
        private sealed class WantOrder : IComparer<Want>
        {
            public double Focus;

            public int Compare(Want a, Want b) => Math.Abs(Centre(a.Segment) - Focus).CompareTo(Math.Abs(Centre(b.Segment) - Focus));
        }

        private struct JobResult
        {
            public int Generation;
            public int Segment;
            public long Key;
            public Color32[] Pixels;
        }

        /// <summary>
        /// Creates the scroll for the held sheet's <paramref name="sheetMaterial"/> and <paramref name="sheetOverlay"/>, for the
        /// traveller <paramref name="travellerTransform"/> and their <paramref name="playerTravel"/> (may be null).
        /// </summary>
        public EndlessScrollMap(Material sheetMaterial, EndlessMapOverlay sheetOverlay, Transform travellerTransform, PlayerTravel playerTravel)
        {
            material = sheetMaterial;
            overlay = sheetOverlay;
            traveller = travellerTransform;
            travel = playerTravel;
            Color32 paper = MapInk.Paper * 0.97f;
            paper.a = 255;
            for (int i = 0; i < blank.Length; i++) blank[i] = paper;
            CreateTextures();
            for (int i = 0; i < RingSegments; i++) slotSegment[i] = int.MinValue;
            if (travel != null)
            {
                picker = new MapPicker(this, travel.DestinationPicker);
                travel.DestinationPicker = picker;
                travel.DestinationChanged += OnDestinationChanged;
                travel.Arrived += OnArrived;
            }
        }

        /// <summary>The ring texture shown by the sheet.</summary>
        public Texture Texture => ring != null ? (Texture)ring : fallback;

        /// <summary>Sheet UV of the pointer.</summary>
        public Vector2 Pointer => pointer;

        /// <summary>True once a generator is known (the map can be opened).</summary>
        public bool Ready => projection != null;

        /// <summary>The settlement under the pointer can be chosen (for the prompt).</summary>
        public bool HoveringSettlement => hovered >= 0 && content != null && content.Features[hovered].Class == StripFeatureClass.Settlement;

        // ------------------------------------------------------------------ frame

        /// <summary>Per-frame work: follows the road, schedules and uploads segments, and (open) rolls, points and letters.</summary>
        public void Tick(bool open, float dt)
        {
            if (!Bind()) return;
            if (ring != null && !ring.IsCreated())
            {
                // The GPU dropped the texture (device reset): recreate and fill again.
                ring.Create();
                for (int i = 0; i < RingSegments; i++) slotSegment[i] = int.MinValue;
            }

            double s = world.S;
            if (reach.Extend(s)) reachDirty = true;
            if (reachDirty && Time.unscaledTime >= nextReachSave) SaveReach(false);

            DrainResults();
            if (Time.unscaledTime >= nextPlan || Math.Abs(s - plannedS) > 8.0 || StripReveal.Key(reach.MinS, reach.MaxS) != plannedReach && open)
            {
                Plan(s, open);
            }

            Launch(open);
            Upload(open);
            UpdateContent(s, open);
            if (!open) return;

            UpdateWindow(dt);
            UpdateTraveller();
            if (Time.unscaledTime >= nextReveal)
            {
                nextReveal = Time.unscaledTime + 0.75f;
                overlay.SetReveal(WeightAt);
            }

            if (Time.unscaledTime >= nextDestination) RefreshDestination();
            overlay.SetPointer(pointer);
            overlay.SetCompass((float)projection.HeadingAt(windowBottom + WindowLength * 0.5));
            overlay.Animate(Time.time);
            UpdateHover();
        }

        /// <summary>Resets the view for opening: the scroll centred on the traveller, the pointer on them. Returns the pointer.</summary>
        public Vector2 Recentre()
        {
            following = true;
            if (!Bind()) return pointer;
            Plan(world.S, true);
            UpdateTraveller();
            windowTarget = windowBottom = FollowBottom();
            ClampWindow();
            windowBottom = windowTarget;
            pointer = ClampPointer(new Vector2(0.5f + (float)(TravellerU / StripMapLayout.HalfWidth * 0.5), (float)((travellerV - windowBottom) / WindowLength)));
            hovered = -1;
            overlay.SetHover(-1, null);
            nextReveal = 0f;
            nextDestination = 0f;
            nextPlan = 0f;
            ApplyWindow();
            return pointer;
        }

        /// <summary>Moves the pointer by <paramref name="deltaUv"/>; at the top or bottom edge it rolls the scroll instead.</summary>
        public void MovePointer(Vector2 deltaUv)
        {
            pointer = ClampPointer(pointer + new Vector2(deltaUv.x, deltaUv.y * StripMapLayout.Width / (float)WindowRows));
        }

        /// <summary>Rolls the scroll by <paramref name="metres"/> along the road (positive = ahead).</summary>
        public void Roll(float metres)
        {
            if (Math.Abs(metres) < 1e-3f) return;
            following = false;
            windowTarget += metres;
            ClampWindow();
        }

        /// <summary>Picks the hovered place: a settlement becomes the destination; anything else is only named.</summary>
        public void Select()
        {
            if (hovered < 0 || content == null || travel == null) return;
            StripFeature f = content.Features[hovered];
            HudController hud = HudController.Instance;
            if (f.Class != StripFeatureClass.Settlement) return;
            RoadWindow window = world != null ? world.Window : null;
            Settlement settlement = window != null ? window.SettlementFor(f.Waypoint.Id) : null;
            string name = f.Waypoint.Name ?? "the inn";
            if (settlement == null)
            {
                if (hud != null) hud.ShowToast("Too far to set out for from here");
                return;
            }

            chosenWaypoint = f.Waypoint.Id;
            if (travel.Destination == settlement)
            {
                if (hud != null) hud.ShowToast(travel.IsTravelling ? $"On the way to {name}" : $"{name} is marked. Press T to set off.");
                return;
            }

            travel.SetDestination(settlement);
            chosenWaypoint = f.Waypoint.Id;
            if (hud != null) hud.ShowToast(travel.IsTravelling ? $"New destination: {name}" : $"Destination: {name}. Press T to set off.");
        }

        /// <summary>Text for the HUD prompt while the map is open.</summary>
        public string Prompt()
        {
            if (HoveringSettlement) return $"Click / E — set out for {content.Features[hovered].Waypoint.Name}   ·   M — fold the map";
            return "Look to move the pointer   ·   edges or wheel to unroll   ·   M — fold the map";
        }

        /// <inheritdoc />
        public void Dispose()
        {
            generation++;
            lifetime.Cancel();
            if (reach != null) SaveReach(true);
            if (travel != null)
            {
                travel.DestinationChanged -= OnDestinationChanged;
                travel.Arrived -= OnArrived;
                if (ReferenceEquals(travel.DestinationPicker, picker)) travel.DestinationPicker = picker.Inner;
            }

            if (ring != null)
            {
                ring.Release();
                UnityEngine.Object.Destroy(ring);
            }

            if (staging != null) UnityEngine.Object.Destroy(staging);
            if (fallback != null) UnityEngine.Object.Destroy(fallback);
        }

        // ------------------------------------------------------------------ the road and its generator

        /// <summary>Finds the endless world and (re)starts for its generator. False while there is none.</summary>
        private bool Bind()
        {
            EndlessWorld current = EndlessWorld.Current;
            if (current == null || current.Generator == null) return false;
            if (ReferenceEquals(current, world) && ReferenceEquals(current.Generator, generator)) return true;
            if (reach != null) SaveReach(true);
            world = current;
            generator = current.Generator;
            generation++;
            projection = new StripProjection(generator, current.S);
            renderer = new StripMapRenderer(projection);
            cache = new StripSegmentCache(28);
            reach = EndlessMapReach.Resume(LoadReach(), current.Seed, current.S);
            reachDirty = true;
            lock (resultGate) inFlight.Clear();
            segmentRoad.Clear();
            while (results.TryDequeue(out _))
            {
            }

            for (int i = 0; i < RingSegments; i++)
            {
                slotSegment[i] = int.MinValue;
                pending[i] = default;
            }

            content = null;
            contentJob = null;
            contentS = double.NaN;
            plannedS = double.NaN;
            nextPlan = 0f;
            hovered = -1;
            chosenWaypoint = -1;
            overlay.SetContent(null, null);
            travellerV = projection.VOfS(current.S);
            windowBottom = windowTarget = FollowBottom();
            return true;
        }

        private static EndlessMapReach LoadReach()
        {
            try
            {
                string json = PlayerPrefs.GetString(EndlessMapReach.PrefsKey, string.Empty);
                return EndlessMapReach.TryFromJson(json, out EndlessMapReach saved) ? saved : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Map: could not read how far this road was walked ({e.Message}).");
                return null;
            }
        }

        private void SaveReach(bool flush)
        {
            nextReachSave = Time.unscaledTime + 20f;
            if (!reachDirty && !flush) return;
            reachDirty = false;
            try
            {
                PlayerPrefs.SetString(EndlessMapReach.PrefsKey, reach.ToJson());
                if (flush) PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Map: could not save how far this road was walked ({e.Message}).");
            }
        }

        // ------------------------------------------------------------------ planning and jobs

        private void Plan(double s, bool open)
        {
            nextPlan = Time.unscaledTime + (open ? 0.25f : 1f);
            plannedS = s;
            plannedReach = StripReveal.Key(reach.MinS, reach.MaxS);
            double sLow = s - ExtentBehind, sHigh = s + ExtentAhead;
            Waypoint inn = world.NextInn;
            if (inn != null && inn.S + 250.0 > sHigh) sHigh = inn.S + 250.0;
            sHigh = Math.Min(sHigh, s + 4200.0);                         // never more than the ring can hold
            extentLowV = projection.VOfS(sLow);
            extentHighV = projection.VOfS(sHigh);
            travellerV = projection.VOfS(s);

            wanted.Clear();
            int k0 = StripMapLayout.SegmentAt(extentLowV), k1 = StripMapLayout.SegmentAt(extentHighV);
            for (int k = k0; k <= k1 && wanted.Count < RingSegments - 1; k++)
            {
                long key = KeyFor(k, out StripReveal.State state);
                wanted.Add(new Want { Segment = k, Key = key, State = state });
            }

            order.Focus = open ? windowBottom + WindowLength * 0.5 : travellerV;
            wanted.Sort(order);

            foreach (Want want in wanted)
            {
                int k = want.Segment;
                long key = want.Key;
                int slot = Slot(k);
                if (slotSegment[slot] == k && slotKey[slot] == key) continue;
                if (pending[slot].Active && pending[slot].Segment == k && pending[slot].Key == key && pending[slot].Pixels != null) continue;

                // The layer may already be cached (a fully inked or sketch segment seen before).
                if (cache.TryGet(key, out Color32[] cached))
                {
                    pending[slot] = new Pending { Active = true, Segment = k, Key = key, Pixels = cached, Slice = 0 };
                    continue;
                }

                // Stale content of another segment in the slot: paper over it until the real thing arrives.
                if (slotSegment[slot] != k && !(pending[slot].Active && pending[slot].Segment == k))
                {
                    pending[slot] = new Pending { Active = true, Segment = k, Key = long.MinValue, Pixels = null, Slice = 0 };
                }
            }
        }

        private static double Centre(int k) => StripMapLayout.SegmentBottom(k) + StripMapLayout.SegmentLength * 0.5;

        private static int Slot(int k) => ((k % RingSegments) + RingSegments) % RingSegments;

        /// <summary>Content key of segment <paramref name="k"/> for the current reach.</summary>
        private long KeyFor(int k, out StripReveal.State state)
        {
            if (!segmentRoad.TryGetValue(k, out (double low, double high) range))
            {
                // Fixed for a projection: solved once per segment.
                double vLow = StripMapLayout.SegmentBottom(k);
                range = (projection.SAtV(vLow), projection.SAtV(vLow + StripMapLayout.SegmentLength));
                if (segmentRoad.Count > 256) segmentRoad.Clear();
                segmentRoad[k] = range;
            }

            double sLow = range.low, sHigh = range.high;
            state = StripReveal.Classify(sLow, sHigh, reach.MinS, reach.MaxS);
            switch (state)
            {
                case StripReveal.State.Inked: return StripSegmentCache.Key(k, StripSegmentCache.Inked);
                case StripReveal.State.Sketch: return StripSegmentCache.Key(k, StripSegmentCache.Sketch);
                default: return unchecked(StripSegmentCache.Key(k, StripSegmentCache.Blended) * 31L + StripReveal.Key(reach.MinS, reach.MaxS)) | 3L;
            }
        }

        private void Launch(bool open)
        {
            int limit = open ? 2 : 1;
            lock (resultGate)
            {
                if (inFlight.Count >= limit) return;
            }

            foreach (Want want in wanted)
            {
                int k = want.Segment;
                long key = want.Key;
                StripReveal.State state = want.State;
                int slot = Slot(k);
                if (slotSegment[slot] == k && slotKey[slot] == key) continue;
                if (pending[slot].Active && pending[slot].Segment == k && pending[slot].Key == key && pending[slot].Pixels != null) continue;
                if (cache.Contains(key)) continue;
                lock (resultGate)
                {
                    if (inFlight.Contains(k)) continue;
                    if (inFlight.Count >= limit) return;
                    inFlight.Add(k);
                }

                int gen = generation;
                StripMapRenderer r = renderer;
                StripSegmentCache c = cache;
                StripProjection p = projection;
                double minS = reach.MinS, maxS = reach.MaxS;
                bool background = !open;
                int segment = k;
                Task.Run(() => Produce(gen, segment, key, state, minS, maxS, r, c, p, background), lifetime.Token);
            }
        }

        private void Produce(int gen, int k, long key, StripReveal.State state, double minS, double maxS, StripMapRenderer r, StripSegmentCache c, StripProjection p, bool background)
        {
            System.Threading.ThreadPriority before = System.Threading.ThreadPriority.Normal;
            bool lowered = false;
            try
            {
                if (background)
                {
                    try
                    {
                        before = Thread.CurrentThread.Priority;
                        Thread.CurrentThread.Priority = System.Threading.ThreadPriority.BelowNormal;
                        lowered = true;
                    }
                    catch (Exception)
                    {
                        // Not allowed on this platform: run at normal priority.
                    }
                }

                Color32[] pixels;
                if (state == StripReveal.State.Inked) pixels = Layer(r, c, k, StripSegmentCache.Inked);
                else if (state == StripReveal.State.Sketch) pixels = Layer(r, c, k, StripSegmentCache.Sketch);
                else
                {
                    Color32[] inked = Layer(r, c, k, StripSegmentCache.Inked);
                    Color32[] sketch = Layer(r, c, k, StripSegmentCache.Sketch);
                    var rowS = new double[StripMapLayout.SegmentRows];
                    int firstRow = k * StripMapLayout.SegmentRows;
                    for (int y = 0; y < rowS.Length; y++) rowS[y] = p.SAtV((firstRow + y + 0.5) * Mpp);
                    pixels = new Color32[inked.Length];
                    StripReveal.Compose(inked, sketch, rowS, W, firstRow, minS, maxS, pixels);
                    c.Put(key, pixels);
                }

                results.Enqueue(new JobResult { Generation = gen, Segment = k, Key = key, Pixels = pixels });
            }
            catch (Exception e)
            {
                results.Enqueue(new JobResult { Generation = gen, Segment = k, Key = key, Pixels = null });
                if (lastErrorGeneration != gen)
                {
                    lastErrorGeneration = gen;
                    Debug.LogError($"Map: painting a stretch of the scroll failed: {e}");
                }
            }
            finally
            {
                if (lowered)
                {
                    try
                    {
                        Thread.CurrentThread.Priority = before;
                    }
                    catch (Exception)
                    {
                        // Ignore.
                    }
                }
            }
        }

        private static Color32[] Layer(StripMapRenderer r, StripSegmentCache c, int k, int layer)
        {
            long key = StripSegmentCache.Key(k, layer);
            if (c.TryGet(key, out Color32[] pixels)) return pixels;
            pixels = layer == StripSegmentCache.Inked ? r.RenderInked(k) : r.RenderSketch(k);
            c.Put(key, pixels);
            return pixels;
        }

        private void DrainResults()
        {
            while (results.TryDequeue(out JobResult result))
            {
                if (result.Generation != generation) continue;
                lock (resultGate) inFlight.Remove(result.Segment);
                if (result.Pixels == null) continue;
                int slot = Slot(result.Segment);
                long key = long.MinValue;
                foreach (Want want in wanted)
                {
                    if (want.Segment == result.Segment) key = want.Key;
                }

                if (key == long.MinValue) continue;                              // no longer on the scroll
                if (key != result.Key && ((key & 3L) != 3L || (result.Key & 3L) != 3L)) continue;   // a blend of an older reach is fine until the newer one arrives
                pending[slot] = new Pending { Active = true, Segment = result.Segment, Key = result.Key, Pixels = result.Pixels, Slice = 0 };
            }
        }

        // ------------------------------------------------------------------ the ring texture

        private void CreateTextures()
        {
            staging = new Texture2D(W, SliceRows, TextureFormat.RGBA32, false, false) { name = "ScrollMapSlice", wrapMode = TextureWrapMode.Clamp };
            bool copy = (SystemInfo.copyTextureSupport & CopyTextureSupport.TextureToRT) != 0;
            if (copy)
            {
                var descriptor = new RenderTextureDescriptor(W, RingRows, staging.graphicsFormat, 0)
                {
                    useMipMap = true,
                    autoGenerateMips = false,
                    msaaSamples = 1,
                };
                ring = new RenderTexture(descriptor)
                {
                    name = "ScrollMap",
                    wrapModeU = TextureWrapMode.Clamp,
                    wrapModeV = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4,
                };
                ring.Create();
                // Start as plain paper.
                for (int row = 0; row < RingRows; row += SliceRows) CopySlice(blank, 0, row);
                ring.GenerateMips();
            }
            else
            {
                fallback = new Texture2D(W, RingRows, TextureFormat.RGBA32, false, false)
                {
                    name = "ScrollMap",
                    wrapModeU = TextureWrapMode.Clamp,
                    wrapModeV = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    anisoLevel = 4,
                };
                for (int row = 0; row < RingRows; row += SliceRows) fallback.SetPixels32(0, row, W, SliceRows, blank);
                fallback.Apply(false, false);
            }
        }

        private void CopySlice(Color32[] source, int sourceStart, int row)
        {
            if (ring != null)
            {
                staging.SetPixelData(source, 0, sourceStart);
                staging.Apply(false, false);
                Graphics.CopyTexture(staging, 0, 0, 0, 0, W, SliceRows, ring, 0, 0, 0, row);
                mipsDirty = true;
            }
            else
            {
                Array.Copy(source, sourceStart, sliceBuffer, 0, sliceBuffer.Length);
                fallback.SetPixels32(0, row, W, SliceRows, sliceBuffer);
                fallback.Apply(false, false);
            }
        }

        /// <summary>Copies a few slices of pending segments into the ring: two per frame when open, one every fourth frame when folded.</summary>
        private void Upload(bool open)
        {
            int budget = open ? 2 : 0;
            if (!open && Time.frameCount - closedUploadFrame >= 4)
            {
                budget = 1;
                closedUploadFrame = Time.frameCount;
            }

            double focus = open ? windowBottom + WindowLength * 0.5 : travellerV;
            while (budget > 0)
            {
                int best = -1;
                double bestDistance = double.PositiveInfinity;
                for (int i = 0; i < RingSegments; i++)
                {
                    if (!pending[i].Active) continue;
                    double d = Math.Abs(Centre(pending[i].Segment) - focus) - (pending[i].Pixels == null ? 1e5 : 0.0);   // paper over stale slots first
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = i;
                    }
                }

                if (best < 0) break;
                ref Pending p = ref pending[best];
                int row = best * StripMapLayout.SegmentRows + p.Slice * SliceRows;
                if (p.Pixels == null) CopySlice(blank, 0, row);
                else CopySlice(p.Pixels, p.Slice * SliceRows * W, row);
                p.Slice++;
                budget--;
                if (p.Slice < SlicesPerSegment) continue;
                slotSegment[best] = p.Segment;
                slotKey[best] = p.Pixels == null ? long.MinValue : p.Key;
                p.Active = false;
                p.Pixels = null;
            }

            if (mipsDirty && ring != null)
            {
                ring.GenerateMips();
                mipsDirty = false;
            }
        }

        // ------------------------------------------------------------------ the window, the traveller, the lettering

        private double TravellerU { get; set; }

        private void UpdateTraveller()
        {
            if (traveller == null || world == null) return;
            (double x, double z) = world.ToAbsolute(traveller.position);
            projection.ToStrip(x, z, travellerV, out double u, out double v);
            TravellerU = u;
            travellerV = v;
            float relative = Mathf.DeltaAngle((float)projection.HeadingAt(v), traveller.eulerAngles.y);
            overlay.SetPlayer(u, v, relative);
        }

        private double FollowBottom()
        {
            // Facing along the road: more of what lies ahead; facing back: more of what lies behind.
            bool back = traveller != null && world != null && InnFinder.DirectionFor(world.Facing(), (float)projection.HeadingAt(travellerV)) < 0;
            return travellerV - WindowLength * (back ? 0.7 : 0.3);
        }

        private void ClampWindow()
        {
            double lo = Math.Min(extentLowV, travellerV - WindowLength * 0.7);
            double hi = Math.Max(extentHighV, travellerV + WindowLength * 0.7) - WindowLength;
            if (double.IsNaN(lo) || hi < lo) return;
            windowTarget = Math.Max(lo, Math.Min(hi, windowTarget));
        }

        private void UpdateWindow(float dt)
        {
            // Pointer at the top or bottom edge, the wheel, or the gamepad shoulders roll the scroll.
            float roll = 0f;
            if (pointer.y > 1f - EdgeZone) roll += (pointer.y - (1f - EdgeZone)) / EdgeZone * EdgeSpeed * dt;
            if (pointer.y < EdgeZone) roll -= (EdgeZone - pointer.y) / EdgeZone * EdgeSpeed * dt;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f) roll += Mathf.Sign(wheel) * WheelStep * Mathf.Max(1f, Mathf.Abs(wheel) / 120f);
            }

            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null)
            {
                if (pad.rightShoulder.isPressed) roll += EdgeSpeed * 0.7f * dt;
                if (pad.leftShoulder.isPressed) roll -= EdgeSpeed * 0.7f * dt;
            }

            if (Mathf.Abs(roll) > 1e-4f) Roll(roll);
            else if (following) windowTarget = FollowBottom();
            ClampWindow();
            windowBottom += (windowTarget - windowBottom) * (1.0 - Math.Exp(-dt * 7.0));
            ApplyWindow();
        }

        private void ApplyWindow()
        {
            double rowBottom = windowBottom / Mpp;
            double offset = rowBottom / RingRows;
            offset -= Math.Floor(offset);
            var scale = new Vector2(1f, WindowRows / (float)RingRows);
            var shift = new Vector2(0f, (float)offset);
            if (material.HasProperty(BaseMapId))
            {
                material.SetTextureScale(BaseMapId, scale);
                material.SetTextureOffset(BaseMapId, shift);
            }

            material.mainTextureScale = scale;
            material.mainTextureOffset = shift;
            overlay.SetWindow(windowBottom + WindowLength * 0.5);
        }

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        private void UpdateContent(double s, bool open)
        {
            if (contentJob != null)
            {
                if (!contentJob.IsCompleted) return;
                Task<StripMapContent> job = contentJob;
                contentJob = null;
                if (job.Status == TaskStatus.RanToCompletion && contentJobGeneration == generation && job.Result != null)
                {
                    content = job.Result;
                    hovered = -1;
                    StripProjection p = projection;
                    overlay.SetContent(content, sx => RoadU(p, sx));
                    overlay.SetReveal(WeightAt);
                    nextDestination = 0f;
                }
            }

            if (!open && content != null) return;
            if (!double.IsNaN(contentS) && Math.Abs(s - contentS) < 150.0 && content != null) return;
            if (contentJob != null) return;
            contentS = s;
            StripProjection proj = projection;
            double sLow = s - ExtentBehind - 400.0, sHigh = s + 4600.0;
            contentJobGeneration = generation;
            contentJob = Task.Run(() => StripMapContent.Build(proj, sLow, sHigh));
        }

        private static double RoadU(StripProjection p, double s)
        {
            p.RoadToStrip(s, out double u, out _);
            return u;
        }

        private float WeightAt(double s) => reach != null ? StripReveal.Weight(s, reach.MinS, reach.MaxS) : 1f;

        private void UpdateHover()
        {
            int best = -1;
            if (content != null)
            {
                double u = (pointer.x - 0.5) * 2.0 * StripMapLayout.HalfWidth;
                double v = windowBottom + pointer.y * WindowLength;
                best = StripMapPicker.Pick(content.Features, u, v, HoverMetres, IsVisible);
            }

            if (best == hovered) return;
            hovered = best;
            if (best < 0)
            {
                overlay.SetHover(-1, null);
                return;
            }

            StripFeature f = content.Features[best];
            overlay.SetHover(best, StripMapText.Describe(f.Label, f.Waypoint.S, world.S));
        }

        private bool IsVisible(StripFeature f) => f.Class == StripFeatureClass.Settlement || WeightAt(f.Waypoint.S) > 0.25f;

        private void RefreshDestination()
        {
            nextDestination = Time.unscaledTime + 1f;
            RoadWindow window = world.Window;
            Waypoint w = travel != null && window != null ? window.WaypointOf(travel.Destination) : null;
            if (w == null)
            {
                overlay.SetDestination(false, 0, 0);
                return;
            }

            projection.ToStrip(w.X, w.Z, projection.VOfS(w.S), out double u, out double v);
            overlay.SetDestination(true, u, v);
        }

        private void OnDestinationChanged(Settlement settlement)
        {
            nextDestination = 0f;
            if (settlement == null)
            {
                chosenWaypoint = -1;
                return;
            }

            // A window rebuild hands over the same place as a new Settlement: keep the choice; anything else replaces it.
            Waypoint w = world != null && world.Window != null ? world.Window.WaypointOf(settlement) : null;
            if (w == null || w.Id != chosenWaypoint) chosenWaypoint = -1;
        }

        private void OnArrived(Settlement settlement) => chosenWaypoint = -1;

        private Vector2 ClampPointer(Vector2 uv)
        {
            return new Vector2(Mathf.Clamp(uv.x, 0.03f, 0.97f), Mathf.Clamp(uv.y, 0.012f, 0.988f));
        }

        // ------------------------------------------------------------------ destination picking for T

        /// <summary>
        /// Wraps the endless world's picker (the next inn in the facing direction) so that a place chosen on the map is where
        /// T goes, until it is reached or replaced.
        /// </summary>
        private sealed class MapPicker : ITravelDestinationPicker
        {
            private readonly EndlessScrollMap map;

            public MapPicker(EndlessScrollMap map, ITravelDestinationPicker inner)
            {
                this.map = map;
                Inner = inner;
            }

            public ITravelDestinationPicker Inner { get; }

            public Settlement PickDestination(PlayerTravel travel, out string notice)
            {
                notice = null;
                EndlessWorld w = EndlessWorld.Current;
                if (map.chosenWaypoint >= 0 && w != null && !w.IsBusy && w.Window != null)
                {
                    Settlement chosen = w.Window.SettlementFor(map.chosenWaypoint);
                    if (chosen != null) return chosen;
                    map.chosenWaypoint = -1;
                }

                return Inner != null ? Inner.PickDestination(travel, out notice) : travel.Destination;
            }
        }
    }
}
