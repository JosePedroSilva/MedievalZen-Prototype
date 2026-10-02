using System.Collections.Generic;
using MedievalZen.Roads;
using MedievalZen.UI;
using MedievalZen.World;
using UnityEngine;
using UnityEngine.UI;

namespace MedievalZen.Travel
{
    /// <summary>
    /// The live layer drawn on top of the held parchment: a world-space canvas lying just in front of the sheet with the
    /// place names (hand-lettered look: italic ink, slight tilt), the "you are here" marker, the destination ring, the
    /// dotted route, the pointer, the hover highlight and its tooltip. Positions are given as sheet UVs (0..1).
    /// Built once by <see cref="HeldMap"/>; updating it does not allocate (the route dots are pooled).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapOverlay : MonoBehaviour
    {
        private const float Units = 1000f;       // canvas units across the sheet
        private const int MaxRouteDots = 180;

        private readonly List<Image> routeDots = new List<Image>(MaxRouteDots);
        private RectTransform canvasRect;
        private RectTransform player;
        private RectTransform destination;
        private Image destinationImage;
        private RectTransform pointer;
        private RectTransform highlight;
        private Text tooltip;
        private RectTransform tooltipRect;
        private Text[] settlementLabels = System.Array.Empty<Text>();
        private int highlightedLabel = -1;
        private int routeDotCount;

        /// <summary>
        /// Creates the canvas (<paramref name="sheetWidth"/> metres wide, lifted <paramref name="lift"/> metres towards the viewer)
        /// and the static labels for <paramref name="roads"/> and <paramref name="landmarks"/>.
        /// </summary>
        public void Build(MapProjection projection, float sheetWidth, float lift, RoadNetwork roads, IReadOnlyList<Landmark> landmarks)
        {
            var canvasObject = new GameObject("MapCanvas", typeof(RectTransform));
            canvasObject.layer = gameObject.layer;
            canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.SetParent(transform, false);
            canvasRect.sizeDelta = new Vector2(Units, Units);
            canvasRect.localPosition = new Vector3(0f, 0f, -lift);
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * (sheetWidth / Units);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;

            if (roads != null)
            {
                var settlements = roads.Settlements;
                settlementLabels = new Text[settlements.Count];
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement s = settlements[i];
                    int size = s.Type == SettlementType.Town ? 30 : s.Type == SettlementType.Village ? 25 : 21;
                    FontStyle style = s.Type == SettlementType.Town ? FontStyle.BoldAndItalic : FontStyle.Italic;
                    string name = s.Type == SettlementType.Town ? s.Name.ToUpperInvariant() : s.Name;
                    float icon = MapTextureRenderer.IconRadius(s.Type) / 1024f * Units;
                    Text label = Label($"Label {s.Name}", name, size, style, UiKit.Ink, projection.WorldToMapUV(s.Position),
                        new Vector2(0f, -(icon + 6f)), new Vector2(0.5f, 1f), (s.Id * 37 % 7 - 3) * 0.8f);
                    settlementLabels[i] = label;
                }
            }

            if (landmarks != null)
            {
                foreach (Landmark landmark in landmarks)
                {
                    Vector2 uv = projection.WorldToMapUV(landmark.position);
                    switch (landmark.kind)
                    {
                        case LandmarkKind.Peak:
                            Label($"Peak {landmark.name}", landmark.name, 20, FontStyle.Italic, UiKit.FadedInk, uv, new Vector2(12f, 4f), new Vector2(0f, 0.5f), 0f);
                            break;
                        case LandmarkKind.Pass:
                            // Above the ")(" sign, so it does not collide with the mountain inn's label below its icon.
                            Label($"Pass {landmark.name}", landmark.name, 19, FontStyle.Italic, UiKit.FadedInk, uv, new Vector2(0f, 14f), new Vector2(0.5f, 0f), 0f);
                            break;
                        case LandmarkKind.Viewpoint:
                            Label($"Viewpoint {landmark.name}", landmark.name, 17, FontStyle.Italic, UiKit.FadedInk, uv, new Vector2(12f, 0f), new Vector2(0f, 0.5f), 0f);
                            break;
                        default:
                            Label($"Lake {landmark.name}", landmark.name, 20, FontStyle.Italic, new Color(0.18f, 0.3f, 0.38f), uv, Vector2.zero, new Vector2(0.5f, 0.5f), -4f);
                            break;
                    }
                }
            }

            Label("North", "N", 22, FontStyle.Bold, UiKit.Ink, new Vector2(0.9f, 0.86f), new Vector2(0f, 30f), new Vector2(0.5f, 0f), 0f);

            for (int i = 0; i < MaxRouteDots; i++)
            {
                Image dot = UiKit.CreateImage("RouteDot", canvasRect, UiKit.Circle, new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.85f), new Vector2(6f, 6f));
                dot.enabled = false;
                routeDots.Add(dot);
            }

            destinationImage = UiKit.CreateImage("Destination", canvasRect, UiKit.Ring, UiKit.Accent, new Vector2(38f, 38f));
            destination = destinationImage.rectTransform;
            destination.gameObject.SetActive(false);

            highlight = UiKit.CreateImage("Highlight", canvasRect, UiKit.Ring, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.7f), new Vector2(46f, 46f)).rectTransform;
            highlight.gameObject.SetActive(false);

            // "You are here": a red dot with a short heading tick.
            player = UiKit.CreateImage("YouAreHere", canvasRect, UiKit.Circle, UiKit.Accent, new Vector2(15f, 15f)).rectTransform;
            Image tick = UiKit.CreateImage("Heading", player, null, UiKit.Accent, new Vector2(3.5f, 16f));
            tick.rectTransform.pivot = new Vector2(0.5f, 0f);
            tick.rectTransform.anchoredPosition = Vector2.zero;

            pointer = UiKit.CreateImage("Pointer", canvasRect, UiKit.Ring, UiKit.Ink, new Vector2(22f, 22f)).rectTransform;
            UiKit.CreateImage("Dot", pointer, UiKit.Circle, UiKit.Ink, new Vector2(4f, 4f));

            tooltip = UiKit.CreateText("Tooltip", canvasRect, 22, TextAnchor.LowerLeft, UiKit.Ink, FontStyle.Italic);
            tooltipRect = tooltip.rectTransform;
            tooltipRect.pivot = new Vector2(0f, 0f);
            tooltipRect.sizeDelta = new Vector2(300f, 60f);
            var outline = tooltip.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.93f, 0.87f, 0.72f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            tooltip.gameObject.SetActive(false);
        }

        /// <summary>Moves the "you are here" marker (UV) and turns its heading tick to <paramref name="yawDegrees"/>.</summary>
        public void SetPlayer(Vector2 uv, float yawDegrees)
        {
            if (player == null) return;
            player.anchoredPosition = ToCanvas(uv);
            player.localRotation = Quaternion.Euler(0f, 0f, -yawDegrees);
        }

        /// <summary>Moves the pointer.</summary>
        public void SetPointer(Vector2 uv)
        {
            if (pointer == null) return;
            pointer.anchoredPosition = ToCanvas(uv);
            if (tooltip.gameObject.activeSelf) tooltipRect.anchoredPosition = ToCanvas(uv) + new Vector2(18f, 14f);
        }

        /// <summary>Highlights settlement <paramref name="index"/> (−1 = none) and shows <paramref name="text"/> beside the pointer.</summary>
        public void SetHover(int index, Vector2 uv, string text)
        {
            if (highlightedLabel >= 0 && highlightedLabel < settlementLabels.Length) settlementLabels[highlightedLabel].color = UiKit.Ink;
            highlightedLabel = index;
            bool on = index >= 0;
            highlight.gameObject.SetActive(on);
            tooltip.gameObject.SetActive(on);
            if (!on) return;
            if (index < settlementLabels.Length) settlementLabels[index].color = UiKit.Accent;
            highlight.anchoredPosition = ToCanvas(uv);
            tooltip.text = text;
        }

        /// <summary>Shows the destination ring at <paramref name="uv"/>, or hides it.</summary>
        public void SetDestination(bool visible, Vector2 uv)
        {
            if (destination == null) return;
            destination.gameObject.SetActive(visible);
            if (visible) destination.anchoredPosition = ToCanvas(uv);
        }

        /// <summary>Gently pulses the destination ring (call every frame while the map is open).</summary>
        public void Animate(float time)
        {
            if (destination == null || !destination.gameObject.activeSelf) return;
            float s = 1f + 0.12f * Mathf.Sin(time * 2.4f);
            destination.localScale = new Vector3(s, s, 1f);
        }

        /// <summary>Lays dots along <paramref name="route"/> from <paramref name="fromDistance"/> to its end (null hides them).</summary>
        public void SetRoute(RoadRoute route, float fromDistance, MapProjection projection)
        {
            int count = 0;
            if (route != null)
            {
                float remaining = Mathf.Max(0f, route.TotalLength - fromDistance);
                float spacing = Mathf.Max(16f, remaining / MaxRouteDots);
                for (float d = fromDistance + spacing * 0.5f; d < route.TotalLength && count < MaxRouteDots; d += spacing)
                {
                    Image dot = routeDots[count++];
                    dot.rectTransform.anchoredPosition = ToCanvas(projection.WorldToMapUV(route.Evaluate(d)));
                    dot.enabled = true;
                }
            }

            for (int i = count; i < routeDotCount; i++) routeDots[i].enabled = false;
            routeDotCount = count;
        }

        private Text Label(string objectName, string text, int size, FontStyle style, Color color, Vector2 uv, Vector2 offset, Vector2 pivot, float tilt)
        {
            Text label = UiKit.CreateText(objectName, canvasRect, size, TextAnchor.MiddleCenter, color, style);
            RectTransform rect = label.rectTransform;
            rect.pivot = pivot;
            rect.sizeDelta = new Vector2(360f, size * 1.4f);
            rect.anchoredPosition = ToCanvas(uv) + offset;
            rect.localRotation = Quaternion.Euler(0f, 0f, tilt);
            if (pivot.x <= 0f) label.alignment = TextAnchor.MiddleLeft;
            label.text = text;
            return label;
        }

        private static Vector2 ToCanvas(Vector2 uv) => (uv - new Vector2(0.5f, 0.5f)) * Units;
    }
}
