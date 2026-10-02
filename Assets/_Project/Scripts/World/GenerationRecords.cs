using System.Collections.Generic;
using MedievalZen.Roads;
using UnityEngine;

namespace MedievalZen.World
{
    /// <summary>A planned settlement (plain data; becomes a <see cref="Settlement"/> in the road network).</summary>
    public sealed class SettlementSite
    {
        /// <summary>Footprint radius of a town in metres.</summary>
        public const float TownRadius = 90f;

        /// <summary>Footprint radius of a village in metres.</summary>
        public const float VillageRadius = 60f;

        /// <summary>Footprint radius of an inn in metres.</summary>
        public const float InnRadius = 35f;

        /// <summary>Creates a site.</summary>
        public SettlementSite(string name, SettlementType type, Vector3 position)
        {
            Name = name;
            Type = type;
            Position = position;
            Radius = RadiusFor(type);
        }

        /// <summary>Display name (unique per world).</summary>
        public string Name { get; }

        /// <summary>Town, village or inn.</summary>
        public SettlementType Type { get; }

        /// <summary>Centre; y is filled in once the disc has been flattened.</summary>
        public Vector3 Position { get; set; }

        /// <summary>Footprint radius in metres.</summary>
        public float Radius { get; }

        /// <summary>Default radius for a settlement type.</summary>
        public static float RadiusFor(SettlementType type)
        {
            switch (type)
            {
                case SettlementType.Town: return TownRadius;
                case SettlementType.Village: return VillageRadius;
                default: return InnRadius;
            }
        }

        /// <inheritdoc />
        public override string ToString() => $"{Type} '{Name}' at ({Position.x:0.0}, {Position.z:0.0})";
    }

    /// <summary>Terrain detail density map (grass/flowers) for one prototype: family + variant.</summary>
    public sealed class DetailLayer
    {
        /// <summary>Detail family id (<see cref="PropRole"/> detail constants).</summary>
        public string family;
        /// <summary>Variant within the family (index into the palette's prefab list).</summary>
        public int variant;
        /// <summary>Instances per detail cell, <c>[z * resolution + x]</c>.</summary>
        public byte[] density;
    }

    /// <summary>A background hill model placed outside the playable area.</summary>
    public struct HorizonPlacement
    {
        /// <summary><see cref="PropRole.HorizonHill"/> or <see cref="PropRole.HorizonMountain"/>.</summary>
        public string family;
        /// <summary>Variant index within the family.</summary>
        public int variant;
        /// <summary>Base centre of the hill.</summary>
        public Vector3 position;
        /// <summary>Rotation about Y in degrees.</summary>
        public float yaw;
        /// <summary>Target footprint width in metres (the builder scales the mesh to it).</summary>
        public float width;
        /// <summary>Target height in metres.</summary>
        public float height;
    }

    /// <summary>A terrain tree instance to create.</summary>
    public struct TreePlacement
    {
        /// <summary>Tree family: <see cref="PropRole.ForestTree"/>, <see cref="PropRole.MeadowTree"/> or <see cref="PropRole.FruitTree"/>.</summary>
        public string role;
        /// <summary>Index into the palette list of the family (wrapped by the palette).</summary>
        public int variant;
        /// <summary>World position (y on the terrain).</summary>
        public Vector3 position;
        /// <summary>Rotation about Y in degrees.</summary>
        public float yaw;
        /// <summary>Uniform scale.</summary>
        public float scale;
    }

    /// <summary>A prefab instance to create.</summary>
    public struct PropPlacement
    {
        /// <summary>Family id (<see cref="PropRole"/>).</summary>
        public string role;
        /// <summary>Index into the palette list of the family (wrapped by the palette).</summary>
        public int variant;
        /// <summary>World position (y on the terrain).</summary>
        public Vector3 position;
        /// <summary>Rotation about Y in degrees. For line pieces (fences, walls, arches) this is the line direction;
        /// the palette adds a per-role yaw offset to match the mesh's axis.</summary>
        public float yaw;
        /// <summary>Uniform scale.</summary>
        public float scale;
        /// <summary>Owning settlement id, or -1 for countryside scatter.</summary>
        public int settlementId;
        /// <summary>
        /// Tilt about the local X axis in degrees (rocks leaning with the ground; 0 for everything upright). The builder rotates
        /// by <c>Quaternion.Euler(pitch, yaw + yawOffset, roll)</c>, i.e. roll about Z, then pitch about X, then yaw about Y
        /// (see <see cref="RockPlacer.Rotate"/>).
        /// </summary>
        public float pitch;
        /// <summary>Tilt about the local Z axis in degrees (see <see cref="pitch"/>).</summary>
        public float roll;
    }

    /// <summary>A rectangular crop field next to a settlement.</summary>
    public struct FieldPatch
    {
        /// <summary>Centre (y on the terrain).</summary>
        public Vector3 center;
        /// <summary>Half size along the field's local X (right) and Z (forward) axes.</summary>
        public Vector2 halfExtents;
        /// <summary>Rotation about Y in degrees.</summary>
        public float yaw;
        /// <summary>Settlement the field belongs to.</summary>
        public int settlementId;
        /// <summary>Crop family planted in rows (<see cref="PropRole.CropField"/>, <see cref="PropRole.Rapeseed"/>, <see cref="PropRole.Sunflower"/>).</summary>
        public string crop;

        /// <summary>True if (x, z) lies inside the field grown by <paramref name="margin"/>.</summary>
        public bool Contains(float x, float z, float margin)
        {
            float dx = x - center.x;
            float dz = z - center.z;
            float reachX = halfExtents.x + margin;
            float reachZ = halfExtents.y + margin;
            if (dx * dx + dz * dz > reachX * reachX + reachZ * reachZ) return false;
            float rad = yaw * Mathf.Deg2Rad;
            float s = Mathf.Sin(rad);
            float c = Mathf.Cos(rad);
            // Local axes: right = (cos, 0, -sin), forward = (sin, 0, cos).
            float lx = dx * c - dz * s;
            float lz = dx * s + dz * c;
            return Mathf.Abs(lx) <= halfExtents.x + margin && Mathf.Abs(lz) <= halfExtents.y + margin;
        }
    }

    /// <summary>One arm of a junction signpost.</summary>
    public sealed class SignpostArmRecord
    {
        /// <summary>Segment leaving the junction.</summary>
        public int segmentId;
        /// <summary>Node at the far end of that segment.</summary>
        public int towardNodeId;
        /// <summary>Horizontal unit direction of the road leaving the junction.</summary>
        public Vector3 direction;
        /// <summary>Settlements whose shortest route from the junction starts on this arm, nearest first.</summary>
        public readonly List<int> settlementIds = new List<int>();
        /// <summary>Road distance to each entry of <see cref="settlementIds"/>.</summary>
        public readonly List<float> distances = new List<float>();
    }

    /// <summary>A signpost at a junction with three or more roads.</summary>
    public sealed class SignpostRecord
    {
        /// <summary>Junction node id.</summary>
        public int nodeId;
        /// <summary>Where the post stands (beside the junction, off the road).</summary>
        public Vector3 position;
        /// <summary>Rotation about Y in degrees (faces the junction).</summary>
        public float yaw;
        /// <summary>Sign prefab variant.</summary>
        public int variant;
        /// <summary>One entry per road leaving the junction.</summary>
        public readonly List<SignpostArmRecord> arms = new List<SignpostArmRecord>();
    }
}
