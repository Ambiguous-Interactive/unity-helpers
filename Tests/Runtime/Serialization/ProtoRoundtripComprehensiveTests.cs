// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Math;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
    public sealed class ProtoRoundtripComprehensiveTests
    {
        [ProtoContract]
        private sealed class WGuidCollection
        {
            [ProtoMember(1)]
            public WGuid single;

            [ProtoMember(2)]
            public WGuid[] array;

            [ProtoMember(3)]
            public List<WGuid> list;
        }

        private static T RoundTrip<T>(T value)
        {
            byte[] bytes = Serializer.ProtoSerialize(value);
            Assert.IsTrue(bytes != null, "Protobuf should produce bytes");
            Assert.Greater(bytes.Length, 0, "Protobuf should produce non-empty bytes");
            return Serializer.ProtoDeserialize<T>(bytes);
        }

        [Test]
        public void WGuidRoundTrips()
        {
            WGuid id = WGuid.NewGuid();
            WGuid again = RoundTrip(id);
            Assert.AreEqual(id, again, "WGuid should round-trip by value");
        }

        [Test]
        public void WGuidCollectionsRoundTrip()
        {
            WGuidCollection payload = new()
            {
                single = WGuid.NewGuid(),
                array = new[] { WGuid.NewGuid(), WGuid.NewGuid() },
                list = new List<WGuid> { WGuid.NewGuid(), WGuid.NewGuid(), WGuid.NewGuid() },
            };

            WGuidCollection again = RoundTrip(payload);

            Assert.AreEqual(payload.single, again.single, "Single WGuid should round-trip");
            CollectionAssert.AreEqual(
                payload.array,
                again.array,
                "Array of WGuid should round-trip"
            );
            CollectionAssert.AreEqual(payload.list, again.list, "List of WGuid should round-trip");

            Assert.IsTrue(again.single.IsVersion4, "Single WGuid should remain v4");
            foreach (WGuid value in again.array)
            {
                Assert.IsTrue(value.IsVersion4, "Array WGuid entries should remain v4");
            }

            foreach (WGuid value in again.list)
            {
                Assert.IsTrue(value.IsVersion4, "List WGuid entries should remain v4");
            }
        }

        [Test]
        public void Vector2RoundTrip()
        {
            Vector2 v = new(1.5f, -2.25f);
            Vector2 again = RoundTrip(v);
            Assert.AreEqual(v.x, again.x, 0f, "Vector2 x should match");
            Assert.AreEqual(v.y, again.y, 0f, "Vector2 y should match");
        }

        [Test]
        public void Vector3RoundTrip()
        {
            Vector3 v = new(-1.5f, 2.75f, 3.25f);
            Vector3 again = RoundTrip(v);
            Assert.AreEqual(v.x, again.x, 0f, "Vector3 x should match");
            Assert.AreEqual(v.y, again.y, 0f, "Vector3 y should match");
            Assert.AreEqual(v.z, again.z, 0f, "Vector3 z should match");
        }

        [Test]
        public void Vector2IntAndVector3IntRoundTrip()
        {
            Vector2Int v2i = new(int.MinValue + 1, int.MaxValue - 1);
            Vector2Int v2iAgain = RoundTrip(v2i);
            Assert.AreEqual(v2i.x, v2iAgain.x, "Vector2Int x should match");
            Assert.AreEqual(v2i.y, v2iAgain.y, "Vector2Int y should match");

            Vector3Int v3i = new(1, -2, 3);
            Vector3Int v3iAgain = RoundTrip(v3i);
            Assert.AreEqual(v3i.x, v3iAgain.x, "Vector3Int x should match");
            Assert.AreEqual(v3i.y, v3iAgain.y, "Vector3Int y should match");
            Assert.AreEqual(v3i.z, v3iAgain.z, "Vector3Int z should match");
        }

        [Test]
        public void QuaternionRoundTrip()
        {
            Quaternion q = new(0.1f, 0.2f, 0.3f, 0.9f);
            Quaternion again = RoundTrip(q);
            Assert.AreEqual(q.x, again.x, 0f, "Quaternion x should match");
            Assert.AreEqual(q.y, again.y, 0f, "Quaternion y should match");
            Assert.AreEqual(q.z, again.z, 0f, "Quaternion z should match");
            Assert.AreEqual(q.w, again.w, 0f, "Quaternion w should match");
        }

        [Test]
        public void ColorAndColor32RoundTrip()
        {
            Color c = new(0.1f, 0.2f, 0.3f, 0.4f);
            Color cAgain = RoundTrip(c);
            Assert.AreEqual(c.r, cAgain.r, 0f, "Color r should match");
            Assert.AreEqual(c.g, cAgain.g, 0f, "Color g should match");
            Assert.AreEqual(c.b, cAgain.b, 0f, "Color b should match");
            Assert.AreEqual(c.a, cAgain.a, 0f, "Color a should match");

            Color32 c32 = new(10, 20, 30, 40);
            Color32 c32Again = RoundTrip(c32);
            Assert.AreEqual(c32.r, c32Again.r, "Color32 r should match");
            Assert.AreEqual(c32.g, c32Again.g, "Color32 g should match");
            Assert.AreEqual(c32.b, c32Again.b, "Color32 b should match");
            Assert.AreEqual(c32.a, c32Again.a, "Color32 a should match");
        }

        [Test]
        public void RangeIntAndFloatRoundTrips()
        {
            Range<int> ri = new(1, 10, true, false);
            Range<int> riAgain = RoundTrip(ri);
            Assert.AreEqual(ri, riAgain, "Range<int> should round-trip");

            Range<float> rf = new(-1.25f, 3.5f, false, true);
            Range<float> rfAgain = RoundTrip(rf);
            Assert.AreEqual(rf, rfAgain, "Range<float> should round-trip");
        }

        [Test]
        public void RectAndRectIntRoundTrip()
        {
            Rect r = new(1.5f, 2.5f, 10f, 20f);
            Rect rAgain = RoundTrip(r);
            Assert.AreEqual(r, rAgain, "Rect should round-trip by value");

            RectInt ri = new(1, 2, 3, 4);
            RectInt riAgain = RoundTrip(ri);
            Assert.AreEqual(ri, riAgain, "RectInt should round-trip by value");
        }

        [Test]
        public void BoundsAndBoundsIntRoundTrip()
        {
            Bounds b = new(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
            Bounds bAgain = RoundTrip(b);
            Assert.AreEqual(b.center, bAgain.center, "Bounds center should round-trip");
            Assert.AreEqual(b.size, bAgain.size, "Bounds size should round-trip");

            BoundsInt bi = new(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6));
            BoundsInt biAgain = RoundTrip(bi);
            Assert.AreEqual(bi.position, biAgain.position, "BoundsInt position should round-trip");
            Assert.AreEqual(bi.size, biAgain.size, "BoundsInt size should round-trip");
        }

        [Test]
        public void ResolutionRoundTrip()
        {
            Resolution r = new() { width = 800, height = 600 };
            Resolution rAgain = RoundTrip(r);
            Assert.AreEqual(r.width, rAgain.width, "Resolution width should round-trip");
            Assert.AreEqual(r.height, rAgain.height, "Resolution height should round-trip");
        }

        [Test]
        public void Line2DAndLine3DRoundTrip()
        {
            Line2D l2 = new(new Vector2(0, 1), new Vector2(2, 3));
            Line2D l2Again = RoundTrip(l2);
            Assert.AreEqual(l2, l2Again, "Line2D should round-trip by value");

            Line3D l3 = new(new Vector3(0, 1, 2), new Vector3(3, 4, 5));
            Line3D l3Again = RoundTrip(l3);
            Assert.AreEqual(l3, l3Again, "Line3D should round-trip by value");
        }

        [Test]
        public void DisjointSetRoundTrips()
        {
            DisjointSet ds = new(6);
            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(2, 3));
            Assert.IsTrue(ds.TryUnion(3, 4));
            Assert.IsFalse(ds.TryUnion(2, 4), "Already in same set after unions");

            DisjointSet clone = RoundTrip(ds);

            Assert.AreEqual(ds.Count, clone.Count, "Element count should match after round-trip");
            Assert.AreEqual(ds.SetCount, clone.SetCount, "Set count should match after round-trip");

            Assert.IsTrue(
                clone.TryIsConnected(0, 1, out bool c01) && c01,
                "0 and 1 should be connected"
            );
            Assert.IsTrue(
                clone.TryIsConnected(2, 4, out bool c24) && c24,
                "2 and 4 should be connected"
            );
            Assert.IsTrue(
                clone.TryIsConnected(0, 5, out bool c05) && !c05,
                "0 and 5 should not be connected"
            );
        }

        [ProtoContract]
        private sealed class Composite
        {
            [ProtoMember(1)]
            public FastVector2Int fv2;

            [ProtoMember(2)]
            public FastVector3Int fv3;

            [ProtoMember(4)]
            public Line2D l2;

            [ProtoMember(5)]
            public Line3D l3;

            [ProtoMember(6)]
            public Range<int> ri;

            [ProtoMember(7)]
            public List<FastVector3Int> list;

            [ProtoMember(8)]
            public Dictionary<string, FastVector2Int> map;

            [ProtoMember(9)]
            public Range<float> rf;
        }

        [Test]
        public void CompositePayloadWithCollectionsRoundTrips()
        {
            Composite payload = new()
            {
                fv2 = new FastVector2Int(-3, 7),
                fv3 = new FastVector3Int(1, -2, 3),
                l2 = new Line2D(new Vector2(0, 0), new Vector2(1, 1)),
                l3 = new Line3D(new Vector3(0, 0, 0), new Vector3(1, 2, 3)),
                ri = Range<int>.InclusiveExclusive(0, 5),
                list = new List<FastVector3Int> { new(8, 9, 10) },
                map = new Dictionary<string, FastVector2Int> { ["k"] = new(2, 4) },
                rf = Range<float>.ExclusiveInclusive(-1f, 1f),
            };

            Composite again = RoundTrip(payload);

            Assert.AreEqual(
                payload.fv2,
                again.fv2,
                "FastVector2Int should round-trip in composite"
            );
            Assert.AreEqual(
                payload.fv3,
                again.fv3,
                "FastVector3Int should round-trip in composite"
            );
            Assert.AreEqual(payload.l2, again.l2, "Line2D should round-trip in composite");
            Assert.AreEqual(payload.l3, again.l3, "Line3D should round-trip in composite");
            Assert.AreEqual(payload.ri, again.ri, "Range<int> should round-trip in composite");
            Assert.AreEqual(payload.rf, again.rf, "Range<float> should round-trip in composite");
            Assert.AreEqual(payload.list.Count, again.list.Count, "List count should match");
            Assert.AreEqual(payload.list[0], again.list[0], "List element should match");
            Assert.AreEqual(payload.map["k"], again.map["k"], "Dictionary element should match");
        }
    }
}
