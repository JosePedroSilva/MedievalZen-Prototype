using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MedievalZen.UI
{
    /// <summary>
    /// A horizontal <see cref="Slider"/> whose left/right (keys, d-pad, stick) step is <see cref="KeyStep"/> instead of
    /// uGUI's fixed 10 % of the range, and which reports whether the pointer is dragging it (so live values flowing in from
    /// the world, like the time of day, do not fight the player's hand).
    /// </summary>
    [AddComponentMenu("")]
    public sealed class JourneySlider : Slider
    {
        /// <summary>Value change per left/right press.</summary>
        public float KeyStep = 0.1f;

        /// <summary>True while the pointer holds the slider.</summary>
        public bool IsDragging { get; private set; }

        /// <summary>Time (unscaled) of the last change made by the player (pointer or keys).</summary>
        public float LastTouched { get; private set; } = -100f;

        /// <inheritdoc />
        public override void OnMove(AxisEventData eventData)
        {
            if (!IsActive() || !IsInteractable())
            {
                base.OnMove(eventData);
                return;
            }

            switch (eventData.moveDir)
            {
                case MoveDirection.Left:
                    LastTouched = Time.unscaledTime;
                    value -= KeyStep;
                    eventData.Use();
                    break;
                case MoveDirection.Right:
                    LastTouched = Time.unscaledTime;
                    value += KeyStep;
                    eventData.Use();
                    break;
                default:
                    base.OnMove(eventData);
                    break;
            }
        }

        /// <inheritdoc />
        public override void OnPointerDown(PointerEventData eventData)
        {
            IsDragging = true;
            LastTouched = Time.unscaledTime;
            base.OnPointerDown(eventData);
        }

        /// <inheritdoc />
        public override void OnPointerUp(PointerEventData eventData)
        {
            IsDragging = false;
            LastTouched = Time.unscaledTime;
            base.OnPointerUp(eventData);
        }

        /// <inheritdoc />
        public override void OnDrag(PointerEventData eventData)
        {
            LastTouched = Time.unscaledTime;
            base.OnDrag(eventData);
        }

        /// <inheritdoc />
        protected override void OnDisable()
        {
            IsDragging = false;
            base.OnDisable();
        }
    }
}
