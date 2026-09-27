using System.Collections.Generic;
using MedievalZen.Roads;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    public sealed class PolylineTests
    {
        private const float Tolerance = 1e-3f;

        // L-shape: 10 m along +X, then 5 m along +Z.
        private static Polyline CreateLShape() => new Polyline(new[]
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(10f, 0f, 0f),
            new Vector3(10f, 0f, 5f),
        });

        [Test]
        public void Length_IsSumOfSpans()
        {
            Polyline line = CreateLShape();

            Assert.That(line.Length, Is.EqualTo(15f).Within(Tolerance));
            Assert.That(line.DistanceAt(1), Is.EqualTo(10f).Within(Tolerance));
        }

        [Test]
        public void Evaluate_IsArcLengthParameterised()
        {
            Polyline line = CreateLShape();

            AssertVector(new Vector3(0f, 0f, 0f), line.Evaluate(0f));
            AssertVector(new Vector3(4f, 0f, 0f), line.Evaluate(4f));
            AssertVector(new Vector3(10f, 0f, 0f), line.Evaluate(10f));
            AssertVector(new Vector3(10f, 0f, 2f), line.Evaluate(12f));
            AssertVector(new Vector3(10f, 0f, 5f), line.Evaluate(15f));
        }

        [Test]
        public void Evaluate_ClampsOutOfRangeDistances()
        {
            Polyline line = CreateLShape();

            AssertVector(line.Start, line.Evaluate(-3f));
            AssertVector(line.End, line.Evaluate(99f));
        }

        [Test]
        public void Tangent_FollowsEachSpan()
        {
            Polyline line = CreateLShape();

            AssertVector(Vector3.right, line.Tangent(5f));
            AssertVector(Vector3.forward, line.Tangent(12f));
            AssertVector(Vector3.forward, line.Tangent(15f));
        }

        [Test]
        public void ClosestPoint_ProjectsOntoNearestSpan()
        {
            Polyline line = CreateLShape();

            AssertVector(new Vector3(4f, 0f, 0f), line.ClosestPoint(new Vector3(4f, 3f, -2f), out float d1));
            Assert.That(d1, Is.EqualTo(4f).Within(Tolerance));

            AssertVector(new Vector3(10f, 0f, 3f), line.ClosestPoint(new Vector3(12f, 0f, 3f), out float d2));
            Assert.That(d2, Is.EqualTo(13f).Within(Tolerance));

            AssertVector(new Vector3(10f, 0f, 5f), line.ClosestPoint(new Vector3(11f, 0f, 9f), out float d3));
            Assert.That(d3, Is.EqualTo(15f).Within(Tolerance));
        }

        [Test]
        public void CatmullRom_ResamplesAtRequestedSpacing()
        {
            var control = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(20f, 1f, 6f),
                new Vector3(40f, 0f, 0f),
                new Vector3(60f, -1f, -6f),
                new Vector3(75f, 0f, 4f),
            };
            const float spacing = 2f;

            Polyline curve = Polyline.CatmullRom(control, spacing);

            AssertVector(control[0], curve.Start);
            AssertVector(control[control.Length - 1], curve.End);
            for (int i = 1; i < curve.Count; i++)
            {
                float span = Vector3.Distance(curve.Points[i - 1], curve.Points[i]);
                Assert.That(span, Is.InRange(spacing * 0.9f, spacing * 1.1f), $"span {i}");
            }

            // The curve interpolates its control points (up to the resampling chord error).
            foreach (Vector3 point in control)
            {
                Vector3 closest = curve.ClosestPoint(point, out _);
                Assert.That(Vector3.Distance(point, closest), Is.LessThan(0.1f), $"control point {point}");
            }
        }

        [Test]
        public void Simplify_DropsCollinearPointsAndKeepsCorners()
        {
            var points = new List<Vector3>();
            for (int x = 0; x <= 10; x++) points.Add(new Vector3(x, 0f, 0f));
            for (int z = 1; z <= 5; z++) points.Add(new Vector3(10f, 0f, z));

            List<Vector3> simplified = Polyline.Simplify(points, 0.01f);

            Assert.That(simplified.Count, Is.EqualTo(3));
            AssertVector(new Vector3(10f, 0f, 0f), simplified[1]);
        }

        [Test]
        public void Densify_KeepsVerticesAndLimitsSpacing()
        {
            Polyline dense = CreateLShape().Densify(2f);

            Assert.That(dense.Length, Is.EqualTo(15f).Within(Tolerance));
            CollectionAssert.Contains(dense.Points, new Vector3(10f, 0f, 0f));
            for (int i = 1; i < dense.Count; i++)
            {
                Assert.That(Vector3.Distance(dense.Points[i - 1], dense.Points[i]), Is.LessThanOrEqualTo(2f + Tolerance));
            }
        }

        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(Tolerance), $"expected {expected} but was {actual}");
        }
    }
}
