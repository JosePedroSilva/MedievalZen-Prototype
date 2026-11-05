using UnityEngine;

namespace MedievalZen.People
{
    /// <summary>What a spot in a place is for, as the composers record it for the people who live there.</summary>
    public enum AnchorKind
    {
        /// <summary>The foot of a house's door steps.</summary>
        Door = 0,

        /// <summary>The inn's door (its rest point).</summary>
        InnDoor = 1,

        /// <summary>Beside the well.</summary>
        Well = 2,

        /// <summary>A bench: the seat's middle on the ground, facing the way a sitter looks.</summary>
        Bench = 3,

        /// <summary>Where a house's path meets the road's verge: the garden gate.</summary>
        Gate = 4,

        /// <summary>Where someone works: the forge, the stable door, the inn's porch.</summary>
        Work = 5,

        /// <summary>In front of a market stall.</summary>
        Stall = 6,

        /// <summary>The middle of the green.</summary>
        Green = 7,

        /// <summary>In front of a chapel (a landmark near the place).</summary>
        Chapel = 8,
    }

    /// <summary>
    /// One spot of a place for its people: kind, position (the dressing window's local coordinates; the planner moves it to absolute),
    /// the yaw someone there faces (degrees, Unity's convention: 0 = +Z), the side of the road it lies on (+1 right, -1 left of the
    /// road's direction of travel, 0 = not known) and the building it belongs to (index into the dressing's props, -1 = none).
    /// Pure data.
    /// </summary>
    public struct PlaceAnchor
    {
        /// <summary>What the spot is.</summary>
        public AnchorKind Kind;

        /// <summary>Position on the ground (y = ground height).</summary>
        public Vector3 Position;

        /// <summary>Yaw a person standing or sitting here faces, degrees.</summary>
        public float Facing;

        /// <summary>Side of the road (+1 right, -1 left, 0 unknown).</summary>
        public int Side;

        /// <summary>Index of the building prop the spot belongs to in the dressing's props, or -1.</summary>
        public int Building;

        /// <summary>Seat height above the ground (benches), metres.</summary>
        public float SeatHeight;
    }
}
