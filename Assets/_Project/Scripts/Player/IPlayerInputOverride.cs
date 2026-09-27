using UnityEngine;

namespace MedievalZen.Player
{
    /// <summary>
    /// Something that temporarily takes over part of the player's input (the held map, the signpost list).
    /// Assigned to <see cref="PlayerController.InputOverride"/>; each hook returns true when it consumed the input,
    /// in which case the controller skips its default handling.
    /// </summary>
    public interface IPlayerInputOverride
    {
        /// <summary>Multiplier on the walking speed while this override is active (1 = normal, 0.5 = half pace).</summary>
        float MoveSpeedScale { get; }

        /// <summary>Look input (pointer delta in pixels or stick deflection). Return true to keep the camera still.</summary>
        bool OnLook(Vector2 input, bool isPointerDelta);

        /// <summary>Movement input (-1..1 per axis). Return true to stop the player from walking with it.</summary>
        bool OnMove(Vector2 move);

        /// <summary>Interact was pressed. Return true to suppress <see cref="PlayerController.InteractRequested"/>.</summary>
        bool OnInteract();

        /// <summary>Map was pressed. Return true to suppress <see cref="PlayerController.MapRequested"/>.</summary>
        bool OnMap();

        /// <summary>Auto-travel was pressed. Return true to skip the default toggle.</summary>
        bool OnAutoTravel();

        /// <summary>Pause was pressed. Return true to keep the cursor locked (e.g. the override closed itself instead).</summary>
        bool OnPause();
    }
}
