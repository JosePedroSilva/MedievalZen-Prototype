using System.Collections.Generic;
using UnityEngine;

namespace MedievalZen.Endless.Generation
{
    /// <summary>
    /// One prop of a set piece, in the set piece's own frame: metres from its origin on the ground, X to its right, Y up, Z to
    /// its front; yaw in degrees about Y (0 = facing the front); a uniform scale.
    /// </summary>
    public readonly struct SetPart
    {
        /// <summary>The prop family (<c>PropRole</c>).</summary>
        public readonly string Role;

        /// <summary>Index in the family's prefab list, in <c>SyntyCatalog</c>'s order.</summary>
        public readonly int Variant;

        /// <summary>The prefab's name, so a test can check <see cref="Variant"/> against the catalog.</summary>
        public readonly string Prefab;

        /// <summary>Position in the set piece's frame, metres.</summary>
        public readonly float X, Y, Z;

        /// <summary>Yaw in degrees; uniform scale.</summary>
        public readonly float Yaw, Scale;

        /// <summary>Drawn by the instanced renderer (wall modules, battlements, fences) rather than as a GameObject.</summary>
        public readonly bool Instanced;

        /// <summary>
        /// What the part stands on or hangs from (vignettes, mined 2026-10-09; castle pieces are <see cref="SetMount.Ground"/>): see
        /// <see cref="SetMount"/>. For <see cref="SetMount.OnPart"/> and <see cref="SetMount.Hung"/>, <see cref="Support"/> is the index of
        /// that part in the same vignette (always lower than this part's) and the offset from it is (X, Y, Z) minus the support's.
        /// </summary>
        public readonly SetMount Mount;

        /// <summary>Index of the part this one stands on or hangs from, in the same vignette; -1 for none.</summary>
        public readonly int Support;

        /// <summary>Largest horizontal reach of the mesh from the pivot, metres at this part's scale (its footprint for occupancy).</summary>
        public readonly float Radius;

        /// <summary>Lowest and highest point of the mesh above the pivot, metres at this part's scale (negative Low: it reaches below).</summary>
        public readonly float Low, High;

        /// <summary>
        /// For a <see cref="SetMount.Wall"/> part: the lowest its pivot may be mounted above the street (2.2 m, more when what hangs from it
        /// must keep 2 m of headroom); <see cref="Y"/> is the height it had in the demo, never lower than this. Zero otherwise.
        /// </summary>
        public readonly float MountMin;

        /// <summary>
        /// Vertical scale (a piece the demo stretched non-uniformly; equal to <see cref="Scale"/> otherwise) and pitch and roll in degrees
        /// (zero for every vignette part, which are yaw-only). Carried for the castle plan's follow-up; consumers that cannot draw them
        /// must leave such a piece out rather than draw it distorted or upright.
        /// </summary>
        public readonly float ScaleY, Pitch, Roll;

        /// <summary>True when the piece is not a uniform, yaw-only placement.</summary>
        public bool Distorted => Mathf.Abs(ScaleY - Scale) > 1e-3f || Pitch != 0f || Roll != 0f;

        public SetPart(string role, int variant, string prefab, float x, float y, float z, float yaw, float scale = 1f, bool instanced = false)
            : this(role, variant, prefab, x, y, z, yaw, scale, instanced, SetMount.Ground, -1, 0f, 0f, 0f, 0f)
        {
        }

        public SetPart(string role, int variant, string prefab, float x, float y, float z, float yaw, float scale, bool instanced, SetMount mount, int support,
            float radius, float low, float high, float mountMin, float scaleY = -1f, float pitch = 0f, float roll = 0f)
        {
            ScaleY = scaleY < 0f ? scale : scaleY;
            Pitch = pitch;
            Roll = roll;
            Role = role;
            Variant = variant;
            Prefab = prefab;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
            Scale = scale;
            Instanced = instanced;
            Mount = mount;
            Support = support;
            Radius = radius;
            Low = low;
            High = high;
            MountMin = mountMin;
        }

        /// <summary>Height of the point where the part meets what it stands on, above its pivot: 0, or the mesh's bottom when it starts above the pivot.</summary>
        public float Contact => Low > 0f ? Low : 0f;
    }

    /// <summary>One vignette part as the city composer placed it (window-local), for tests and the log.</summary>
    public struct PlacedSetPart
    {
        public string Vignette, Role, Prefab;
        public VignetteKind Slot;
        /// <summary>Which placement of a vignette (parts of one placement share it) and which of its parts.</summary>
        public int Placement, Part;
        public SetMount Mount;
        /// <summary>Index in <c>CityStats.SetParts</c> of what it stands on or hangs from; -1 for none.</summary>
        public int Support;
        public Vector3 Position;
        public float Yaw, Radius;
        /// <summary>Height where it meets what it stands on (pivot + <see cref="SetPart.Contact"/>).</summary>
        public float Contact;
        /// <summary>Lowest and highest ground the composer found under its footprint (its root's, for a part on or hung from another).</summary>
        public float GroundLow, GroundHigh;
        /// <summary>Fixed to the slot's face (a wall part, or hung from one).</summary>
        public bool OnFace;
        /// <summary>The slot's face: found, a point on it (y = the street at its foot), its outward normal, its top and the street's height.</summary>
        public bool HasFace;
        public Vector3 FacePoint, FaceNormal;
        public float FaceTop, Street;
        /// <summary>How far the slot's edge (the vignette's origin) stands in front of its face (a porch or door steps between them).</summary>
        public float FaceBack;
    }

    /// <summary>How a vignette's part is held (mined from what it stood on in the demo city, 2026-10-09).</summary>
    public enum SetMount
    {
        /// <summary>Stands on the ground: Y is 0, or minus <see cref="SetPart.Low"/> for a mesh that starts above its pivot (it meets the ground there).</summary>
        Ground = 0,

        /// <summary>Stands on another part of the vignette (pots on a bench, a statue on its plinth, sacks on crates).</summary>
        OnPart = 1,

        /// <summary>Fixed to the wall or house front the vignette stands against (sign brackets, hanging pots, banners, ivy): its back at Z = 0.</summary>
        Wall = 2,

        /// <summary>Hangs from another part (a chain from a bracket, a sign from its chain, clothes from a stall's frame).</summary>
        Hung = 3,
    }

    /// <summary>Where a vignette belongs.</summary>
    public enum VignetteKind
    {
        /// <summary>A market stall with its goods, standing free on a square or against a frontage.</summary>
        Stall = 0,

        /// <summary>Clutter beside a house door: barrels, crates, a bench, flower pots.</summary>
        DoorSide = 1,

        /// <summary>The corner where two streets meet: a lamp, a cart, a sign, stacked goods.</summary>
        Corner = 2,

        /// <summary>The foot of a wall or retaining wall: banners, ivy, a lean-to, stacked wood.</summary>
        WallFoot = 3,

        /// <summary>The middle of a small square: a well or fountain, a tree in a stone ring, benches.</summary>
        Plaza = 4,

        /// <summary>A working yard: a smith's, a carter's, a stable's.</summary>
        Yard = 5,

        /// <summary>By water: a quay, a mill wheel, a jetty.</summary>
        Quay = 6,
    }

    /// <summary>
    /// A small arrangement of props taken from the Fantasy Kingdom pack's own demo city, where its artists composed them: the
    /// origin is on the ground at the middle of the edge it stands against (a house front, a wall), +Z points out into the street.
    /// Every part is yaw-only (within 5 degrees in the demo); a part's Y is its height above the vignette's ground (the demo's ground
    /// under the origin), or above the street for a wall-mounted one.
    /// </summary>
    public sealed class CityVignette
    {
        /// <summary>A name for logs and tests.</summary>
        public string Name;

        /// <summary>Where it belongs.</summary>
        public VignetteKind Kind;

        /// <summary>Room it needs along the edge and out from it, metres.</summary>
        public float Width, Depth;

        /// <summary>Its props.</summary>
        public SetPart[] Parts;
    }

    /// <summary>
    /// A castle as a set piece: curtain walls, towers, a keep, taken from the demo city's castle. The origin is the middle of its
    /// footprint on its own ground, +Z the side its gate faces.
    /// </summary>
    public sealed class CastlePlan
    {
        /// <summary>A name for logs and tests.</summary>
        public string Name;

        /// <summary>Half its footprint across (X) and deep (Z), and its height above its ground, metres.</summary>
        public float HalfWidth, HalfDepth, Height;

        /// <summary>Where its gate opens, in its own frame.</summary>
        public float GateX, GateZ;

        /// <summary>Its pieces.</summary>
        public SetPart[] Parts;
    }

    /// <summary>
    /// The set pieces a city is dressed with (2026-10-06): the user wants cities as dense and rich as the pack's demo scene, so
    /// the composer keeps its own layout (every city differs) and takes its detail from arrangements the pack's artists made.
    /// The tables are filled by the mined data file (<c>CitySetPieces.Data.cs</c>); with none, the lists are empty and the
    /// composer must still build a city.
    /// </summary>
    public static partial class CitySetPieces
    {
        private static readonly List<CityVignette> vignettes = new List<CityVignette>();
        private static readonly List<CastlePlan> castles = new List<CastlePlan>();

        static CitySetPieces()
        {
            Fill(vignettes, castles);
        }

        /// <summary>Every vignette.</summary>
        public static IReadOnlyList<CityVignette> Vignettes => vignettes;

        /// <summary>Every castle.</summary>
        public static IReadOnlyList<CastlePlan> Castles => castles;

        /// <summary>The vignettes of one kind, appended to <paramref name="results"/>.</summary>
        public static void Of(VignetteKind kind, List<CityVignette> results)
        {
            foreach (CityVignette v in vignettes)
            {
                if (v != null && v.Kind == kind) results.Add(v);
            }
        }

        /// <summary>Implemented by the mined data file; absent, the lists stay empty.</summary>
        static partial void Fill(List<CityVignette> vignettes, List<CastlePlan> castles);
    }
}
