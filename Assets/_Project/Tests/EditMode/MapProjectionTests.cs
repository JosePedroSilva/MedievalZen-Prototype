using MedievalZen.Travel;
using NUnit.Framework;
using UnityEngine;

namespace MedievalZen.Tests
{
    public sealed class MapProjectionTests
    {
        private const float Tolerance = 1e-3f;

        [Test]
        public void CornersOfTheWorldLandOnTheInnerFrame()
        {
            var projection = new MapProjection(0f, 0f, 2000f, 0.1f);
            Vector2 sw = projection.WorldToMapUV(new Vector3(0f, 50f, 0f));
            Vector2 ne = projection.WorldToMapUV(new Vector3(2000f, 0f, 2000f));
            Vector2 centre = projection.WorldToMapUV(new Vector3(1000f, 0f, 1000f));
            Assert.That(sw.x, Is.EqualTo(0.1f).Within(Tolerance));
            Assert.That(sw.y, Is.EqualTo(0.1f).Within(Tolerance));
            Assert.That(ne.x, Is.EqualTo(0.9f).Within(Tolerance));
            Assert.That(ne.y, Is.EqualTo(0.9f).Within(Tolerance));
            Assert.That(centre.x, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(centre.y, Is.EqualTo(0.5f).Within(Tolerance));
        }

        [Test]
        public void NorthIsUpAndEastIsRight()
        {
            var projection = new MapProjection(-500f, -500f, 1000f, 0.05f);
            Vector2 origin = projection.WorldToMapUV(Vector3.zero);
            Assert.That(projection.WorldToMapUV(new Vector3(0f, 0f, 100f)).y, Is.GreaterThan(origin.y));
            Assert.That(projection.WorldToMapUV(new Vector3(100f, 0f, 0f)).x, Is.GreaterThan(origin.x));
        }

        [TestCase(0f, 0f)]
        [TestCase(123.4f, 1876.5f)]
        [TestCase(2000f, 17f)]
        [TestCase(-40f, 2100f)]
        public void RoundTripsWorldToUvAndBack(float x, float z)
        {
            var projection = new MapProjection(0f, 0f, 2000f, 0.07f);
            var world = new Vector3(x, 12f, z);
            Vector3 back = projection.MapUVToWorld(projection.WorldToMapUV(world), 12f);
            Assert.That(back.x, Is.EqualTo(x).Within(1e-2f));
            Assert.That(back.z, Is.EqualTo(z).Within(1e-2f));
            Assert.That(back.y, Is.EqualTo(12f).Within(Tolerance));
        }

        [Test]
        public void RoundTripsUvToWorldAndBackWithOffsetOrigin()
        {
            var projection = new MapProjection(-300f, 250f, 800f, 0.07f);
            var uv = new Vector2(0.31f, 0.77f);
            Vector2 back = projection.WorldToMapUV(projection.MapUVToWorld(uv));
            Assert.That(back.x, Is.EqualTo(uv.x).Within(1e-5f));
            Assert.That(back.y, Is.EqualTo(uv.y).Within(1e-5f));
        }

        [Test]
        public void MarginIsNotOnTheWorld()
        {
            var projection = new MapProjection(0f, 0f, 2000f, 0.07f);
            Assert.That(projection.IsOnWorld(new Vector2(0.5f, 0.5f)), Is.True);
            Assert.That(projection.IsOnWorld(new Vector2(0.03f, 0.5f)), Is.False);
            Assert.That(projection.MetresPerUv, Is.EqualTo(2000f / 0.86f).Within(1e-2f));
        }
    }
}
