using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MedievalZen.UI
{
    /// <summary>
    /// A one-line choice "‹  Normal  ›": left/right (keys, d-pad, stick) or a click on either end steps through
    /// <see cref="Options"/>; Submit or a click in the middle steps forward (wrapping). Raises <see cref="Changed"/>.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class JourneyStepper : Selectable, IPointerClickHandler, ISubmitHandler
    {
        /// <summary>Displayed option labels.</summary>
        public string[] Options = Array.Empty<string>();

        /// <summary>Text showing the current option.</summary>
        public Text Label;

        /// <summary>Wrap around at the ends (true for on/off style choices).</summary>
        public bool Wrap;

        private int index;

        /// <summary>Raised with the new index when the player changes the choice.</summary>
        public event Action<int> Changed;

        /// <summary>The current option index.</summary>
        public int Index => index;

        /// <summary>Shows <paramref name="newIndex"/> without raising <see cref="Changed"/>.</summary>
        public void SetIndexWithoutNotify(int newIndex)
        {
            if (Options.Length == 0) return;
            index = Mathf.Clamp(newIndex, 0, Options.Length - 1);
            Refresh();
        }

        /// <summary>Steps by <paramref name="delta"/> (clamped, or wrapped when <see cref="Wrap"/>) and raises <see cref="Changed"/>.</summary>
        public void Step(int delta)
        {
            int count = Options.Length;
            if (count == 0 || !IsInteractable()) return;
            int next = Wrap ? ((index + delta) % count + count) % count : Mathf.Clamp(index + delta, 0, count - 1);
            if (next == index) return;
            index = next;
            Refresh();
            Changed?.Invoke(index);
        }

        /// <inheritdoc />
        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left && IsInteractable())
            {
                Step(-1);
                eventData.Use();
                return;
            }

            if (eventData.moveDir == MoveDirection.Right && IsInteractable())
            {
                Step(1);
                eventData.Use();
                return;
            }

            base.OnMove(eventData);
        }

        /// <inheritdoc />
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !IsInteractable()) return;
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out Vector2 local))
            {
                Step(1);
                return;
            }

            // The left quarter steps back; the rest (the right arrow and the label) steps forward.
            float x = Rect.PointToNormalized(rect.rect, local).x;
            Step(x < 0.25f ? -1 : 1);
        }

        /// <inheritdoc />
        public void OnSubmit(BaseEventData eventData)
        {
            if (!IsInteractable()) return;
            int before = index;
            Step(1);
            if (index == before && !Wrap && Options.Length > 1)
            {
                // At the last option Submit goes back to the first, so a single button can always cycle.
                index = 0;
                Refresh();
                Changed?.Invoke(index);
            }
        }

        private void Refresh()
        {
            if (Label == null || Options.Length == 0) return;
            bool canLeft = Wrap || index > 0;
            bool canRight = Wrap || index < Options.Length - 1;
            Label.text = (canLeft ? "‹" : " ") + "    " + Options[index] + "    " + (canRight ? "›" : " ");
        }
    }
}
