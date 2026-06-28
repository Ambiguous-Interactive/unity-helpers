// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Math;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
    public sealed class MathJsonTests
    {
        [Test]
        public void CircleRoundTrips()
        {
            Circle c = new(new Vector2(1.5f, -2.25f), 3.75f);
            string json = Serializer.JsonStringify(c);
            Circle again = Serializer.JsonDeserialize<Circle>(json);
            Assert.AreEqual(c, again, "Circle should round-trip by value");
        }

        [Test]
        public void SphereRoundTrips()
        {
            Sphere s = new(new Vector3(2f, 3f, 4f), 5f);
            string json = Serializer.JsonStringify(s);
            Sphere again = Serializer.JsonDeserialize<Sphere>(json);
            Assert.AreEqual(s, again, "Sphere should round-trip by value");
        }

        [Test]
        public void Line2DRoundTrips()
        {
            Line2D l = new(new Vector2(0, 1), new Vector2(2, 3));
            string json = Serializer.JsonStringify(l);
            Line2D again = Serializer.JsonDeserialize<Line2D>(json);
            Assert.AreEqual(l, again, "Line2D should round-trip by value");
        }

        [Test]
        public void Line3DRoundTrips()
        {
            Line3D l = new(new Vector3(0, 1, 2), new Vector3(3, 4, 5));
            string json = Serializer.JsonStringify(l);
            Line3D again = Serializer.JsonDeserialize<Line3D>(json);
            Assert.AreEqual(l, again, "Line3D should round-trip by value");
        }

        [Test]
        public void BoundingBox3DRoundTrips()
        {
            BoundingBox3D b = new(new Vector3(-1, -2, -3), new Vector3(4, 5, 6));
            string json = Serializer.JsonStringify(b);
            BoundingBox3D again = Serializer.JsonDeserialize<BoundingBox3D>(json);
            Assert.AreEqual(b, again, "BoundingBox3D should round-trip by value");
        }

        [Test]
        public void ParabolaRoundTrips()
        {
            Parabola p = new(maxHeight: 5f, length: 10f);
            string json = Serializer.JsonStringify(p);
            Parabola again = Serializer.JsonDeserialize<Parabola>(json);
            Assert.AreEqual(p, again, "Parabola should round-trip by value");
        }

        [Test]
        public void RangeIntAndFloatRoundTrips()
        {
            Range<int> ri = new(1, 10, true, false);
            string riJson = Serializer.JsonStringify(ri);
            Range<int> riAgain = Serializer.JsonDeserialize<Range<int>>(riJson);
            Assert.AreEqual(ri, riAgain, "Range<int> should round-trip");

            Range<float> rf = new(-1.25f, 3.5f, false, true);
            string rfJson = Serializer.JsonStringify(rf);
            Range<float> rfAgain = Serializer.JsonDeserialize<Range<float>>(rfJson);
            Assert.AreEqual(rf, rfAgain, "Range<float> should round-trip");
        }
    }
}
