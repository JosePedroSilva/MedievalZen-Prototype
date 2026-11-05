using System;
using System.Collections.Generic;
using MedievalZen.UI;
using UnityEngine;
using UnityEngine.UI;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The live layer of the endless road's scroll map, like <see cref="MedievalZen.Travel.MapOverlay"/> for the finite sheet: a
    /// world-space canvas just in front of the held scroll with a clipped viewport holding everything that lies on the strip
    /// (place names, milestone distances, region lettering, "you are here", the destination ring, the hover ring) and, fixed to
    /// the sheet, the pointer, its tooltip, a small compass rose and a scale bar. Positions are strip metres (u, v); the strip
    /// content slides under the viewport as the scroll rolls. Labels come from a fixed pool; nothing allocates per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EndlessMapOverlay : MonoBehaviour
    {
        private const float Units = 1000f;               // canvas units across the sheet
        private const int LabelPool = 64;
        private const int RegionPool = 6;

        private static readonly Color WaterInk = new Color(0.18f, 0.3f, 0.38f, 1f);
        private static readonly Color HaloColour = new Color(0.93f, 0.87f, 0.72f, 0.75f);

        private readonly List<Text> labels = new List<Text>(LabelPool);
        private readonly List<Text> regionLabels = new List<Text>(RegionPool);
        private readonly int[] labelFeature = new int[LabelPool];
        private readonly float[] labelMinAlpha = new float[LabelPool];
        private readonly Color[] labelInk = new Color[LabelPool];
        private readonly int[] regionMark = new int[RegionPool];
        private int labelCount, regionCount;

        private float unitsPerMetre;
        private float heightUnits;
        private double vRef;
        private RectTransform content;
        private RectTransform player;
        private RectTransform destination;
        private RectTransform highlight;
        private RectTransform pointer;
        private RectTransform compass;
        private Text tooltip;
        private RectTransform tooltipRect;
        private StripMapContent current;
        private int highlightedLabel = -1;

        /// <summary>
        /// Builds the canvas for a sheet <paramref name="sheetWidth"/> × <paramref name="sheetHeight"/> metres showing
        /// <paramref name="stripWidthMetres"/> of country across, lifted <paramref name="lift"/> metres towards the viewer.
        /// </summary>
        public void Build(float sheetWidth, float sheetHeight, float stripWidthMetres, float lift)
        {
            unitsPerMetre = Units / stripWidthMetres;
            heightUnits = Units * sheetHeight / sheetWidth;

            var canvasObject = new GameObject("ScrollCanvas", typeof(RectTransform));
            canvasObject.layer = gameObject.layer;
            var canvasRect = (RectTransform)canvasObject.transform;
            canvasRect.SetParent(transform, false);
            canvasRect.sizeDelta = new Vector2(Units, heightUnits);
            canvasRect.localPosition = new Vector3(0f, 0f, -lift);
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * (sheetWidth / Units);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;

            // The viewport clips the strip content at the rollers.
            RectTransform viewport = UiKit.CreateRect("Viewport", canvasRect);
            UiKit.Stretch(viewport);
            viewport.offsetMin = new Vector2(0f, 14f);
            viewport.offsetMax = new Vector2(0f, -14f);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = UiKit.CreateRect("Strip", viewport);
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
            content.pivot = new Vector2(0.5f, 0.5f);
            content.sizeDelta = Vector2.zero;

            for (int i = 0; i < RegionPool; i++)
            {
                Text t = MakeLabel("Region", 22, FontStyle.Italic);
                t.rectTransform.pivot = new Vector2(0.5f, 0f);
                regionLabels.Add(t);
            }

            for (int i = 0; i < LabelPool; i++) labels.Add(MakeLabel("Label", 28, FontStyle.Italic));

            Image ring = UiKit.CreateImage("Destination", content, UiKit.Ring, UiKit.Accent, new Vector2(40f, 40f));
            destination = ring.rectTransform;
            destination.gameObject.SetActive(false);
            highlight = UiKit.CreateImage("Highlight", content, UiKit.Ring, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.7f), new Vector2(48f, 48f)).rectTransform;
            highlight.gameObject.SetActive(false);

            // "You are here": a red dot with a short heading tick (as on the finite sheet).
            player = UiKit.CreateImage("YouAreHere", content, UiKit.Circle, UiKit.Accent, new Vector2(17f, 17f)).rectTransform;
            Image tick = UiKit.CreateImage("Heading", player, null, UiKit.Accent, new Vector2(4f, 19f));
            tick.rectTransform.pivot = new Vector2(0.5f, 0f);
            tick.rectTransform.anchoredPosition = Vector2.zero;

            BuildCompass(canvasRect);
            BuildScaleBar(canvasRect);

            pointer = UiKit.CreateImage("Pointer", canvasRect, UiKit.Ring, UiKit.Ink, new Vector2(24f, 24f)).rectTransform;
            UiKit.CreateImage("Dot", pointer, UiKit.Circle, UiKit.Ink, new Vector2(4.5f, 4.5f));

            tooltip = UiKit.CreateText("Tooltip", canvasRect, 30, TextAnchor.LowerLeft, UiKit.Ink, FontStyle.Italic);
            tooltipRect = tooltip.rectTransform;
            tooltipRect.pivot = new Vector2(0f, 0f);
            tooltipRect.sizeDelta = new Vector2(420f, 60f);
            var outline = tooltip.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.93f, 0.87f, 0.72f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            tooltip.gameObject.SetActive(false);
        }

        private Text MakeLabel(string objectName, int size, FontStyle style)
        {
            Text label = UiKit.CreateText(objectName, content, size, TextAnchor.MiddleCenter, UiKit.Ink, style);
            label.rectTransform.sizeDelta = new Vector2(520f, size * 1.4f);
            var halo = label.gameObject.AddComponent<Outline>();
            halo.effectColor = HaloColour;
            halo.effectDistance = new Vector2(1.2f, -1.2f);
            label.enabled = false;
            return label;
        }

        private void BuildCompass(RectTransform canvasRect)
        {
            // Top right of the sheet: a paper disc, a ring, and a needle with its "N" that turns to true north.
            Image disc = UiKit.CreateImage("CompassDisc", canvasRect, UiKit.Circle, new Color(0.93f, 0.87f, 0.72f, 0.6f), new Vector2(118f, 118f));
            disc.rectTransform.anchoredPosition = new Vector2(Units * 0.5f - 92f, heightUnits * 0.5f - 104f);
            UiKit.CreateImage("CompassRing", disc.rectTransform, UiKit.Ring, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.8f), new Vector2(86f, 86f));
            compass = UiKit.CreateRect("Needle", disc.rectTransform);
            compass.sizeDelta = Vector2.zero;
            Image north = UiKit.CreateImage("North", compass, null, UiKit.Ink, new Vector2(6f, 50f));
            north.rectTransform.pivot = new Vector2(0.5f, 0f);
            north.rectTransform.anchoredPosition = Vector2.zero;
            Image south = UiKit.CreateImage("South", compass, null, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.45f), new Vector2(3f, 36f));
            south.rectTransform.pivot = new Vector2(0.5f, 1f);
            south.rectTransform.anchoredPosition = Vector2.zero;
            Text n = UiKit.CreateText("N", compass, 26, TextAnchor.MiddleCenter, UiKit.Ink, FontStyle.Bold);
            n.rectTransform.sizeDelta = new Vector2(40f, 34f);
            n.rectTransform.anchoredPosition = new Vector2(0f, 66f);
            n.text = "N";
        }

        private void BuildScaleBar(RectTransform canvasRect)
        {
            // Bottom left: 200 m in four alternating blocks, lettered.
            const float metres = 200f;
            float length = metres * unitsPerMetre;
            var origin = new Vector2(-Units * 0.5f + 60f, -heightUnits * 0.5f + 62f);
            for (int i = 0; i < 4; i++)
            {
                bool ink = i % 2 == 0;
                Image block = UiKit.CreateImage("Scale", canvasRect, null, ink ? UiKit.Ink : new Color(0.95f, 0.9f, 0.77f, 0.95f), new Vector2(length / 4f, 9f));
                block.rectTransform.pivot = new Vector2(0f, 0.5f);
                block.rectTransform.anchoredPosition = origin + new Vector2(i * length / 4f, 0f);
                var edge = block.gameObject.AddComponent<Outline>();
                edge.effectColor = UiKit.Ink;
                edge.effectDistance = new Vector2(1f, -1f);
            }

            Text label = UiKit.CreateText("ScaleLabel", canvasRect, 24, TextAnchor.MiddleLeft, UiKit.FadedInk, FontStyle.Italic);
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            label.rectTransform.sizeDelta = new Vector2(200f, 30f);
            label.rectTransform.anchoredPosition = origin + new Vector2(length + 14f, 0f);
            label.text = "200 m";
        }

        // ------------------------------------------------------------------ content

        /// <summary>
        /// Letters <paramref name="newContent"/> (null clears): one pooled label per named feature, region names across the strip.
        /// <paramref name="roadU"/> gives the road's strip offset at a feature's road distance (for which side to letter on).
        /// </summary>
        public void SetContent(StripMapContent newContent, Func<double, double> roadU)
        {
            current = newContent;
            SetHover(-1, null);
            labelCount = 0;
            regionCount = 0;
            if (newContent != null)
            {
                vRef = newContent.Features.Count > 0 ? newContent.Features[0].V : 0.0;
                for (int i = 0; i < newContent.Features.Count && labelCount < LabelPool; i++)
                {
                    StripFeature f = newContent.Features[i];
                    if (string.IsNullOrEmpty(f.Label)) continue;
                    StripLabelStyle style = StripMapText.StyleFor(f, roadU(f.Waypoint.S));
                    Text t = labels[labelCount];
                    labelFeature[labelCount] = i;
                    labelMinAlpha[labelCount] = style.MinAlpha;
                    labelInk[labelCount] = style.Ink == StripInk.Water ? WaterInk : style.Ink == StripInk.Faded ? UiKit.FadedInk : UiKit.Ink;
                    float px = Units / StripMapLayout.Width;                         // canvas units per sheet pixel
                    t.fontSize = Mathf.RoundToInt(style.Size * px);
                    t.fontStyle = style.Bold ? FontStyle.BoldAndItalic : FontStyle.Italic;
                    t.text = style.Bold ? f.Label.ToUpperInvariant() : f.Label;
                    RectTransform rect = t.rectTransform;
                    rect.sizeDelta = new Vector2(520f, t.fontSize * 1.4f);
                    rect.pivot = new Vector2(style.PivotX, style.PivotY);
                    t.alignment = style.PivotX <= 0f ? TextAnchor.MiddleLeft : style.PivotX >= 1f ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
                    rect.anchoredPosition = ToContent(f.U, f.V) + new Vector2(style.OffsetX, style.OffsetY) * px;
                    rect.localRotation = Quaternion.Euler(0f, 0f, style.Tilt);
                    t.enabled = true;
                    labelCount++;
                }

                for (int i = 0; i < newContent.Regions.Count && regionCount < RegionPool; i++)
                {
                    StripRegionMark mark = newContent.Regions[i];
                    StripMapText.RegionLabelAt(mark, out double u, out double v);
                    Text t = regionLabels[regionCount];
                    regionMark[regionCount] = i;
                    t.text = mark.Lettering;
                    t.color = UiKit.FadedInk;
                    t.rectTransform.anchoredPosition = ToContent(u, v);
                    t.enabled = true;
                    regionCount++;
                }
            }

            for (int i = labelCount; i < labels.Count; i++) labels[i].enabled = false;
            for (int i = regionCount; i < regionLabels.Count; i++) regionLabels[i].enabled = false;
        }

        /// <summary>Fades the lettering of unseen country: <paramref name="weightAt"/> gives the ink weight at a road distance.</summary>
        public void SetReveal(Func<double, float> weightAt)
        {
            if (current == null) return;
            for (int i = 0; i < labelCount; i++)
            {
                StripFeature f = current.Features[labelFeature[i]];
                float alpha = Mathf.Max(labelMinAlpha[i], weightAt(f.Waypoint.S));
                Color c = i == highlightedLabel ? UiKit.Accent : labelInk[i];
                c.a = alpha;
                labels[i].color = c;
                labels[i].enabled = alpha > 0.02f;
            }

            for (int i = 0; i < regionCount; i++)
            {
                float alpha = weightAt(current.Regions[regionMark[i]].Region.SStart) * 0.85f;
                Color c = UiKit.FadedInk;
                c.a = alpha;
                regionLabels[i].color = c;
                regionLabels[i].enabled = alpha > 0.02f;
            }
        }

        // ------------------------------------------------------------------ live elements

        /// <summary>Rolls the strip so strip height <paramref name="windowCentreV"/> sits in the middle of the sheet.</summary>
        public void SetWindow(double windowCentreV)
        {
            if (content != null) content.anchoredPosition = new Vector2(0f, (float)(-(windowCentreV - vRef) * unitsPerMetre));
        }

        /// <summary>Moves "you are here" to strip (<paramref name="u"/>, <paramref name="v"/>), its tick turned <paramref name="relativeYaw"/> degrees from the sheet's up.</summary>
        public void SetPlayer(double u, double v, float relativeYaw)
        {
            if (player == null) return;
            player.anchoredPosition = ToContent(u, v);
            player.localRotation = Quaternion.Euler(0f, 0f, -relativeYaw);
        }

        /// <summary>Turns the compass needle: true north lies <paramref name="northDegrees"/> counter-clockwise from the sheet's up.</summary>
        public void SetCompass(float northDegrees)
        {
            if (compass != null) compass.localRotation = Quaternion.Euler(0f, 0f, northDegrees);
        }

        /// <summary>Moves the pointer (sheet UV, 0..1 both ways).</summary>
        public void SetPointer(Vector2 uv)
        {
            if (pointer == null) return;
            Vector2 p = new Vector2((uv.x - 0.5f) * Units, (uv.y - 0.5f) * heightUnits);
            pointer.anchoredPosition = p;
            if (tooltip.gameObject.activeSelf)
            {
                // Keep the tooltip on the sheet: flip it to the pointer's left near the right edge.
                bool left = uv.x > 0.6f;
                tooltipRect.pivot = new Vector2(left ? 1f : 0f, 0f);
                tooltip.alignment = left ? TextAnchor.LowerRight : TextAnchor.LowerLeft;
                tooltipRect.anchoredPosition = p + new Vector2(left ? -18f : 18f, 14f);
            }
        }

        /// <summary>Highlights feature <paramref name="featureIndex"/> of the current content (−1 = none) and shows <paramref name="text"/> by the pointer.</summary>
        public void SetHover(int featureIndex, string text)
        {
            if (highlightedLabel >= 0 && highlightedLabel < labelCount)
            {
                Color c = labelInk[highlightedLabel];
                c.a = labels[highlightedLabel].color.a;
                labels[highlightedLabel].color = c;
            }

            highlightedLabel = -1;
            bool on = featureIndex >= 0 && current != null && featureIndex < current.Features.Count;
            if (highlight == null) return;
            highlight.gameObject.SetActive(on);
            tooltip.gameObject.SetActive(on);
            if (!on) return;
            StripFeature f = current.Features[featureIndex];
            highlight.anchoredPosition = ToContent(f.U, f.V);
            tooltip.text = text;
            for (int i = 0; i < labelCount; i++)
            {
                if (labelFeature[i] != featureIndex) continue;
                highlightedLabel = i;
                Color c = UiKit.Accent;
                c.a = Mathf.Max(0.75f, labels[i].color.a);
                labels[i].color = c;
                break;
            }
        }

        /// <summary>Shows the destination ring at strip (<paramref name="u"/>, <paramref name="v"/>), or hides it.</summary>
        public void SetDestination(bool visible, double u, double v)
        {
            if (destination == null) return;
            destination.gameObject.SetActive(visible);
            if (visible) destination.anchoredPosition = ToContent(u, v);
        }

        /// <summary>Gently pulses the destination ring.</summary>
        public void Animate(float time)
        {
            if (destination == null || !destination.gameObject.activeSelf) return;
            float s = 1f + 0.12f * Mathf.Sin(time * 2.4f);
            destination.localScale = new Vector3(s, s, 1f);
        }

        private Vector2 ToContent(double u, double v) => new Vector2((float)(u * unitsPerMetre), (float)((v - vRef) * unitsPerMetre));
    }
}
