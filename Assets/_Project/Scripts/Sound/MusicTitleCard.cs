using MedievalZen.UI;
using UnityEngine;
using UnityEngine.UI;

namespace MedievalZen.Sound
{
    /// <summary>
    /// "♪ Golden Clouds", lower left, when a piece starts: fades in over 1.5 s, holds 7 s, fades out over 2 s. Drawn like the HUD's
    /// toast (parchment panel, italic ink, <see cref="UiKit"/>) on its own overlay canvas. Honours <see cref="SoundMix.ShowMusicTitles"/>;
    /// hidden while a capture runs.
    /// </summary>
    public sealed class MusicTitleCard : MonoBehaviour
    {
        public const float FadeIn = 1.5f, Hold = 7f, FadeOut = 2f;

        private CanvasGroup group;
        private Text text;
        private float age = float.PositiveInfinity;

        /// <summary>Creates the card under <paramref name="parent"/>.</summary>
        public static MusicTitleCard Create(Transform parent)
        {
            var go = new GameObject("Music Title", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var card = go.AddComponent<MusicTitleCard>();
            Image panel = UiKit.CreatePanel("Card", go.transform, UiKit.Parchment);
            UiKit.Place(panel.rectTransform, new Vector2(0f, 0f), new Vector2(40f, 48f), new Vector2(420f, 50f));
            card.group = panel.gameObject.AddComponent<CanvasGroup>();
            card.group.alpha = 0f;
            card.group.blocksRaycasts = false;
            card.group.interactable = false;
            card.text = UiKit.CreateText("Text", panel.transform, 24, TextAnchor.MiddleCenter, UiKit.Ink, FontStyle.Italic);
            UiKit.Stretch(card.text.rectTransform);
            return card;
        }

        /// <summary>Shows a title (ignored when titles are off).</summary>
        public void Show(string title)
        {
            if (!SoundMix.ShowMusicTitles || string.IsNullOrEmpty(title)) return;
            text.text = "♪ " + title;
            age = 0f;
        }

        private static readonly int FrameCostId = Review.FrameCost.Register("MusicTitleCard");

        private void Update()
        {
            using Review.FrameCost.Scope frameCost = Review.FrameCost.Measure(FrameCostId);   // stall hunt: the per-frame culprit
            if (group == null) return;
            age += Time.unscaledDeltaTime;
            float a;
            if (age < FadeIn) a = age / FadeIn;
            else if (age < FadeIn + Hold) a = 1f;
            else a = 1f - (age - FadeIn - Hold) / FadeOut;
            if (!SoundMix.ShowMusicTitles || SoundMix.CaptureRunning) a = 0f;
            group.alpha = Mathf.Clamp01(a);
        }
    }
}
