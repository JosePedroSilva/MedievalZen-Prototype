using System;
using UnityEngine;

namespace MedievalZen.Endless
{
    /// <summary>
    /// The endless road's floating origin: scene position = absolute position − origin (horizontally; heights are never
    /// shifted). The origin always sits on a tile corner, so tiles land on whole-metre scene positions and a shift moves
    /// everything by whole tiles. Pure maths (unit tested); <see cref="EndlessWorld"/> owns the instance and performs shifts.
    /// </summary>
    public sealed class FloatingOrigin
    {
        /// <summary>Creates an origin at the tile corner nearest to absolute (<paramref name="x"/>, <paramref name="z"/>).</summary>
        public FloatingOrigin(float tileSize, double x = 0.0, double z = 0.0)
        {
            if (!(tileSize > 0f)) throw new ArgumentOutOfRangeException(nameof(tileSize));
            TileSize = tileSize;
            X = Quantise(x, tileSize);
            Z = Quantise(z, tileSize);
        }

        /// <summary>Tile edge length the origin is quantised to.</summary>
        public float TileSize { get; }

        /// <summary>Absolute X of the scene origin (a multiple of <see cref="TileSize"/>).</summary>
        public double X { get; private set; }

        /// <summary>Absolute Z of the scene origin (a multiple of <see cref="TileSize"/>).</summary>
        public double Z { get; private set; }

        /// <summary>Scene position of an absolute point (y passes through unchanged).</summary>
        public Vector3 ToScene(double x, double y, double z) => new Vector3((float)(x - X), (float)y, (float)(z - Z));

        /// <summary>Scene position of an absolute point on the ground plane (y = 0).</summary>
        public Vector3 ToScene(double x, double z) => new Vector3((float)(x - X), 0f, (float)(z - Z));

        /// <summary>Absolute X of a scene position.</summary>
        public double AbsoluteX(Vector3 scene) => X + scene.x;

        /// <summary>Absolute Z of a scene position.</summary>
        public double AbsoluteZ(Vector3 scene) => Z + scene.z;

        /// <summary>Absolute (x, z) of a scene position.</summary>
        public (double x, double z) ToAbsolute(Vector3 scene) => (X + scene.x, Z + scene.z);

        /// <summary>The multiple of <paramref name="tileSize"/> nearest to <paramref name="value"/>.</summary>
        public static double Quantise(double value, float tileSize) => Math.Round(value / tileSize) * tileSize;

        /// <summary>True when a scene position is farther than <paramref name="threshold"/> metres (horizontally) from the scene origin.</summary>
        public static bool NeedsShift(Vector3 scene, float threshold) => scene.x * scene.x + scene.z * scene.z > threshold * threshold;

        /// <summary>
        /// The scene-space delta a shift that brings the origin to the tile corner nearest the scene position
        /// <paramref name="traveller"/> would apply (every scene position moves by it); zero when already there.
        /// Whole tiles only, horizontal only.
        /// </summary>
        public Vector3 DeltaToward(Vector3 traveller)
        {
            double nx = Quantise(X + traveller.x, TileSize);
            double nz = Quantise(Z + traveller.z, TileSize);
            return new Vector3((float)(X - nx), 0f, (float)(Z - nz));
        }

        /// <summary>
        /// Moves the origin by <paramref name="delta"/>'s opposite (scene positions move by <paramref name="delta"/>), rounded to
        /// whole tiles. Returns the delta actually applied.
        /// </summary>
        public Vector3 Shift(Vector3 delta)
        {
            double dx = Quantise(-delta.x, TileSize);
            double dz = Quantise(-delta.z, TileSize);
            X += dx;
            Z += dz;
            return new Vector3((float)-dx, 0f, (float)-dz);
        }

        /// <summary>Puts the origin at the tile corner nearest absolute (<paramref name="x"/>, <paramref name="z"/>) and returns the scene delta.</summary>
        public Vector3 MoveTo(double x, double z)
        {
            double nx = Quantise(x, TileSize);
            double nz = Quantise(z, TileSize);
            var delta = new Vector3((float)(X - nx), 0f, (float)(Z - nz));
            X = nx;
            Z = nz;
            return delta;
        }

        /// <inheritdoc />
        public override string ToString() => $"({X:0}, {Z:0})";
    }
}
