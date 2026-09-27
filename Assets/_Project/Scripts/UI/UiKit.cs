using UnityEngine;
using UnityEngine.UI;

namespace MedievalZen.UI
{
    /// <summary>
    /// Shared look and small factory helpers for the code-built uGUI (legacy <see cref="Text"/> with the built-in
    /// LegacyRuntime font, parchment panels, ink colours, procedurally generated round sprites).
    /// </summary>
    public static class UiKit
    {
        /// <summary>Parchment panel colour (slightly transparent).</summary>
        public static readonly Color Parchment = new Color(0.93f, 0.87f, 0.72f, 0.82f);

        /// <summary>Dark brown ink for text.</summary>
        public static readonly Color Ink = new Color(0.22f, 0.15f, 0.08f, 1f);

        /// <summary>Faded ink for secondary text.</summary>
        public static readonly Color FadedInk = new Color(0.36f, 0.27f, 0.17f, 1f);

        /// <summary>Red-brown accent (highlights, selected entries, destination).</summary>
        public static readonly Color Accent = new Color(0.55f, 0.16f, 0.08f, 1f);

        private static Font font;
        private static Sprite circle;
        private static Sprite ring;
        private static Sprite softRect;

        /// <summary>The built-in legacy runtime font.</summary>
        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        /// <summary>A filled anti-aliased circle sprite (64 px).</summary>
        public static Sprite Circle => circle != null ? circle : circle = MakeRound(64, 0f);

        /// <summary>A ring sprite (64 px, ~12 % line width).</summary>
        public static Sprite Ring => ring != null ? ring : ring = MakeRound(64, 0.76f);

        /// <summary>A rounded-rectangle sprite with 9-slice borders, for panels.</summary>
        public static Sprite SoftRect => softRect != null ? softRect : softRect = MakeSoftRect();

        /// <summary>Creates a child <see cref="RectTransform"/>.</summary>
        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>Anchors <paramref name="rect"/> at <paramref name="anchor"/> (also its pivot) with an offset and size.</summary>
        public static void Place(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        /// <summary>Stretches <paramref name="rect"/> over its parent.</summary>
        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Creates a parchment panel image.</summary>
        public static Image CreatePanel(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SoftRect;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Creates a text element.</summary>
        public static Text CreateText(string name, Transform parent, int size, TextAnchor alignment, Color color, FontStyle style = FontStyle.Normal)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.supportRichText = true;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>Creates an image element with <paramref name="sprite"/>.</summary>
        public static Image CreateImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.sizeDelta = size;
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>"850 m" below a kilometre, "1.2 km" above (distance in metres).</summary>
        public static string FormatDistance(float metres)
        {
            if (metres < 995f) return $"{Mathf.Max(10f, Mathf.Round(metres / 10f) * 10f):0} m";
            return $"{metres / 1000f:0.0} km";
        }

        private static Sprite MakeRound(int size, float innerFraction)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = innerFraction > 0f ? "UiRing" : "UiCircle", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float outer = Mathf.Clamp01(r - d);
                    float inner = innerFraction > 0f ? Mathf.Clamp01(d - r * innerFraction) : 1f;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * outer * inner));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite MakeSoftRect()
        {
            const int size = 32;
            const float radius = 10f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "UiSoftRect", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - size * 0.5f) - (size * 0.5f - radius));
                    float dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - size * 0.5f) - (size * 0.5f - radius));
                    float a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * a));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
        }
    }
}
