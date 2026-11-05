using System.Collections.Generic;
using System.Text;
using MedievalZen.Endless;
using MedievalZen.Player;
using MedievalZen.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MedievalZen.Travel
{
    /// <summary>
    /// The traveller's journal as a book: J opens and closes it (Esc closes), two facing parchment pages show the entries in
    /// order under day headings, A/D, the arrow keys or the mouse wheel turn the pages, and the last page holds the tallies.
    /// It opens on the newest page. Built in code on its own screen canvas (no prefab, no art assets: the paper is painted
    /// like the map scroll's); takes input through <see cref="PlayerController.InputOverride"/> and does not pause the world.
    /// </summary>
    [DefaultExecutionOrder(-43)]
    [DisallowMultipleComponent]
    public sealed class JournalBook : MonoBehaviour, IPlayerInputOverride
    {
        private const float OpenSeconds = 0.25f;
        private static readonly Vector2 BookSize = new Vector2(1360f, 860f);
        private static readonly Vector2 PageSize = new Vector2(650f, 810f);
        private const float PageMargin = 58f;
        private const int FontSize = 23;
        private static readonly Color Leather = new Color(0.31f, 0.18f, 0.1f, 1f);

        private PlayerController controller;
        private JournalKeeper keeper;
        private RectTransform root;
        private CanvasGroup group;
        private RectTransform book;
        private readonly Text[] pageText = new Text[2];
        private readonly Text[] pageNumber = new Text[2];
        private readonly List<string> pages = new List<string>();
        private readonly TextGenerator measure = new TextGenerator();
        private int spread;
        private float alpha;
        private bool moveArmed = true;
        private float wheelReadyAt;
        private bool stale;

        /// <summary>True while the book is open (or opening).</summary>
        public bool IsOpen { get; private set; }

        /// <inheritdoc />
        public float MoveSpeedScale => 0f;

        // ------------------------------------------------------------------ open / close

        /// <summary>Opens the book on its newest page (no-op when another overlay holds the input).</summary>
        public bool Open()
        {
            if (IsOpen) return true;
            if (controller == null) controller = FindFirstObjectByType<PlayerController>();
            if (root == null || controller == null || !controller.isActiveAndEnabled) return false;
            if (controller.InputOverride != null && !ReferenceEquals(controller.InputOverride, this)) return false;
            if (Sound.SoundMix.CaptureRunning) return false;
            IsOpen = true;
            controller.InputOverride = this;
            root.gameObject.SetActive(true);
            HudController hud = HudController.Instance;
            if (hud != null) hud.HidePrompt();
            Paginate();
            spread = NewestSpread();
            ShowSpread();
            moveArmed = false;   // the key that opened it, or a held direction, does not turn a page at once
            return true;
        }

        /// <summary>Closes the book and gives the input back.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (controller != null && ReferenceEquals(controller.InputOverride, this)) controller.InputOverride = null;
        }

        // ------------------------------------------------------------------ input override

        /// <inheritdoc />
        public bool OnLook(Vector2 input, bool isPointerDelta) => true;

        /// <inheritdoc />
        public bool OnMove(Vector2 move)
        {
            if (Mathf.Abs(move.x) < 0.3f) moveArmed = true;
            else if (moveArmed && Mathf.Abs(move.x) > 0.5f)
            {
                moveArmed = false;
                Turn(move.x > 0f ? 1 : -1);
            }

            return true;
        }

        /// <inheritdoc />
        public bool OnInteract() => true;

        /// <inheritdoc />
        public bool OnMap()
        {
            Close();
            return false;   // straight on to the map
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
            keeper = GetComponent<JournalKeeper>();
            Build();
        }

        private void OnEnable()
        {
            if (keeper != null) keeper.EntryAdded += OnEntryAdded;
        }

        private void OnDisable()
        {
            if (keeper != null) keeper.EntryAdded -= OnEntryAdded;
            Close();
            alpha = 0f;
            if (root != null) root.gameObject.SetActive(false);
        }

        private void OnEntryAdded(JournalEntry entry) => stale = true;

        private static readonly int FrameCostId = Review.FrameCost.Register("JournalBook");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.jKey.wasPressedThisFrame)
            {
                if (IsOpen) Close();
                else Open();
            }

            if (IsOpen && (controller == null || !controller.isActiveAndEnabled || Sound.SoundMix.CaptureRunning)) Close();
            if (!IsOpen && alpha <= 0f) return;

            alpha = Mathf.MoveTowards(alpha, IsOpen ? 1f : 0f, Time.unscaledDeltaTime / OpenSeconds);
            float eased = Mathf.SmoothStep(0f, 1f, alpha);
            group.alpha = eased;
            book.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, eased);
            book.anchoredPosition = new Vector2(0f, (1f - eased) * -30f);
            if (!IsOpen)
            {
                if (alpha <= 0f) root.gameObject.SetActive(false);
                return;
            }

            if (stale)
            {
                bool atNewest = spread >= NewestSpread();
                Paginate();
                spread = atNewest ? NewestSpread() : Mathf.Min(spread, LastSpread());
                ShowSpread();
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                // One page per notch whatever the wheel's units (±120 or ±1 per notch), at most one every 0.18 s for smooth wheels.
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f && Time.unscaledTime >= wheelReadyAt)
                {
                    Turn(wheel > 0f ? -1 : 1);   // wheel up leafs back
                    wheelReadyAt = Time.unscaledTime + 0.18f;
                }
            }
        }

        // ------------------------------------------------------------------ pages

        private int LastSpread() => Mathf.Max(0, (pages.Count - 1) / 2);

        /// <summary>The spread with the newest entries (the page before the tallies).</summary>
        private int NewestSpread() => Mathf.Max(0, (pages.Count - 2) / 2);

        private void Turn(int direction)
        {
            int next = Mathf.Clamp(spread + direction, 0, LastSpread());
            if (next == spread) return;
            spread = next;
            ShowSpread();
        }

        private void ShowSpread()
        {
            for (int side = 0; side < 2; side++)
            {
                int index = spread * 2 + side;
                bool has = index < pages.Count;
                pageText[side].text = has ? pages[index] : string.Empty;
                pageNumber[side].text = has ? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
            }
        }

        private float Height(string text)
        {
            Text t = pageText[0];
            TextGenerationSettings settings = t.GetGenerationSettings(new Vector2(PageSize.x - 2f * PageMargin, 0f));
            return measure.GetPreferredHeight(text, settings) / Mathf.Max(0.0001f, t.pixelsPerUnit);
        }

        /// <summary>Lays the entries out on pages (greedy by measured height; a day carried over a page says so), then the tallies.</summary>
        private void Paginate()
        {
            stale = false;
            pages.Clear();
            Journal journal = keeper != null ? keeper.Journal : null;
            float limit = PageSize.y - 2f * PageMargin - 24f;
            // Blocks are measured once each (cached by entry and text) and their heights summed, so opening a full journal
            // does not lay out every page again.
            if (measured.Count > 2 * (journal != null ? journal.Entries.Count : 0) + 64) measured.Clear();   // another road's, or folded days
            if (gapHeight < 0f) gapHeight = Mathf.Max(0f, Height("A" + Gap + "A") - 2f * Height("A"));
            string page = string.Empty;
            float used = 0f;
            JournalEntry previous = null;
            if (journal != null)
            {
                foreach (JournalEntry e in journal.Entries)
                {
                    bool sameDay = previous != null && previous.Day == e.Day;
                    bool folded = e.Kind == JournalKind.Folded;
                    string block = (sameDay || folded ? string.Empty : Heading(e.Day, false)) + JournalWriter.Line(e, false, sameDay ? previous : null);
                    float h = BlockHeight(e, block);
                    if (page.Length > 0 && used + gapHeight + h > limit)
                    {
                        // A new page: the day is named again, and so are the time and the weather.
                        pages.Add(page);
                        page = string.Empty;
                        block = (folded ? string.Empty : Heading(e.Day, sameDay)) + JournalWriter.Line(e, false, null);
                        h = BlockHeight(e, block);
                    }

                    if (page.Length == 0)
                    {
                        page = block;
                        used = h;
                    }
                    else
                    {
                        page += Gap + block;
                        used += gapHeight + h;
                    }

                    previous = e;
                }
            }
            if (page.Length > 0) pages.Add(page);
            if (pages.Count == 0) pages.Add("<i>Nothing written yet. The road is waiting.</i>");
            pages.Add(TalliesPage(journal));
        }

        private readonly Dictionary<JournalEntry, KeyValuePair<string, float>> measured = new Dictionary<JournalEntry, KeyValuePair<string, float>>();
        private float gapHeight = -1f;

        private float BlockHeight(JournalEntry e, string block)
        {
            if (measured.TryGetValue(e, out KeyValuePair<string, float> m) && m.Key == block) return m.Value;
            float h = Height(block);
            measured[e] = new KeyValuePair<string, float>(block, h);
            return h;
        }

        private const string Gap = "\n<size=9> </size>\n";

        private static string Heading(int day, bool continued) =>
            continued
                ? "<color=#5C4527><i>" + JournalWriter.DayHeading(day) + ", continued</i></color>" + Gap
                : "<size=30><b>" + JournalWriter.DayHeading(day) + "</b></size>" + Gap;

        private static string TalliesPage(Journal journal)
        {
            var sb = new StringBuilder();
            sb.Append("<size=30><b>The road so far</b></size>").Append(Gap).Append(Gap);
            int today = MedievalZen.Environment.DayCycle.Current != null ? MedievalZen.Environment.DayCycle.Current.Day : 0;
            JournalTotals totals = journal != null ? journal.Totals(today) : default;
            foreach (string line in JournalWriter.TotalsLines(totals)) sb.Append(line).Append(Gap);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            var canvasObject = new GameObject("Journal Book (UI)", typeof(RectTransform));
            int layer = LayerMask.NameToLayer("UI");
            canvasObject.layer = layer >= 0 ? layer : 0;
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;   // over the HUD (10)
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root = (RectTransform)canvasObject.transform;
            group = canvasObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            Image backdrop = UiKit.CreatePanel("Backdrop", root, new Color(0f, 0f, 0f, 0.38f));
            backdrop.sprite = null;
            backdrop.type = Image.Type.Simple;
            UiKit.Stretch(backdrop.rectTransform);

            Image cover = UiKit.CreatePanel("Cover", root, Leather);
            book = cover.rectTransform;
            UiKit.Place(book, new Vector2(0.5f, 0.5f), Vector2.zero, BookSize);

            for (int side = 0; side < 2; side++)
            {
                var image = UiKit.CreateImage(side == 0 ? "Left page" : "Right page", book, PageSprite(side == 0), Color.white, PageSize);
                RectTransform rect = image.rectTransform;
                UiKit.Place(rect, new Vector2(0.5f, 0.5f), new Vector2((side == 0 ? -0.5f : 0.5f) * (PageSize.x + 6f), 0f), PageSize);

                Text text = UiKit.CreateText("Text", rect, FontSize, TextAnchor.UpperLeft, UiKit.Ink);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.lineSpacing = 1.08f;
                RectTransform tr = text.rectTransform;
                UiKit.Stretch(tr);
                tr.offsetMin = new Vector2(PageMargin, PageMargin);
                tr.offsetMax = new Vector2(-PageMargin, -PageMargin);
                pageText[side] = text;

                Text number = UiKit.CreateText("Number", rect, 17, TextAnchor.LowerCenter, UiKit.FadedInk, FontStyle.Italic);
                UiKit.Place(number.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(120f, 24f));
                pageNumber[side] = number;
            }

            Image spine = UiKit.CreateImage("Spine", book, null, new Color(0.18f, 0.1f, 0.05f, 0.55f), new Vector2(5f, PageSize.y));
            UiKit.Place(spine.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5f, PageSize.y));

            Image tag = UiKit.CreatePanel("Hint", root, UiKit.Parchment);
            UiKit.Place(tag.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(560f, 40f));
            Text hint = UiKit.CreateText("Text", tag.rectTransform, 17, TextAnchor.MiddleCenter, UiKit.FadedInk, FontStyle.Italic);
            UiKit.Stretch(hint.rectTransform);
            hint.text = "A / D or the wheel   turn the page      ·      J or Esc   close";

            canvasObject.SetActive(false);
        }

        /// <summary>A parchment page painted like the map scroll's paper: blotches, grain and fibres, darker ragged edges and a shaded gutter at the spine.</summary>
        private static Sprite PageSprite(bool left)
        {
            const int w = 260, h = 324;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = left ? "JournalPageLeft" : "JournalPageRight", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[w * h];
            int salt = left ? 31 : 37;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float blotch = StripNoise.Value(x / 70.0, y / 70.0, salt);
                    float grain = StripNoise.Value(x / 6.0, y / 6.0, salt + 1);
                    float fibre = StripNoise.Value(x / 1.6, y / 18.0, salt + 2);
                    float shade = 0.92f + 0.08f * blotch + 0.05f * (grain - 0.5f) + 0.03f * (fibre - 0.5f);
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    float outer = left ? u : 1f - u;   // distance from the fore-edge
                    float gutter = 1f - outer;          // distance from the spine
                    float ragged = 0.012f * StripNoise.Value(x / 9.0, y / 9.0, salt + 3);
                    float edge = Mathf.Min(Mathf.Min(outer, v), 1f - v);
                    shade *= Mathf.Lerp(0.7f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((edge - ragged) / 0.035f)));
                    shade *= Mathf.Lerp(0.78f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(gutter / 0.09f)));
                    float stain = StripNoise.Value(x / 55.0, y / 55.0, salt + 4);
                    if (stain > 0.76f) shade *= 1f - (stain - 0.76f) * 0.3f;
                    Color c = MapInk.Paper * shade;
                    c.a = 1f;
                    pixels[y * w + x] = c;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
