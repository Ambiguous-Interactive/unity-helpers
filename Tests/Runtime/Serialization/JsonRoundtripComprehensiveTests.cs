// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using System.Text.Json;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Math;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
    public sealed class JsonRoundtripComprehensiveTests
    {
        private sealed class CompositePayload
        {
            public FastVector2Int fv2 { get; set; }
            public FastVector3Int fv3 { get; set; }
            public Vector2 v2 { get; set; }
            public Vector3 v3 { get; set; }
            public Bounds bounds { get; set; }
            public BoundsInt boundsInt { get; set; }
            public Rect rect { get; set; }
            public RectInt rectInt { get; set; }
            public Quaternion q { get; set; }
            public Range<int> ri { get; set; }
            public Range<float> rf { get; set; }
            public List<FastVector3Int> listFv3 { get; set; }
            public Dictionary<string, FastVector2Int> mapFv2 { get; set; }
        }

        [Test]
        public void AdaptersCollectionsRoundTrip()
        {
            List<FastVector3Int> list = new() { new(1, 2, 3), new(-4, 5, -6), new(7, 0, -1) };

            Dictionary<string, FastVector2Int> map = new()
            {
                ["a"] = new(9, -9),
                ["b"] = new(0, 1),
                ["c"] = new(int.MinValue + 1, int.MaxValue - 1),
            };

            string listJson = Serializer.JsonStringify(list);
            string mapJson = Serializer.JsonStringify(map);

            List<FastVector3Int> listAgain = Serializer.JsonDeserialize<List<FastVector3Int>>(
                listJson
            );
            Dictionary<string, FastVector2Int> mapAgain = Serializer.JsonDeserialize<
                Dictionary<string, FastVector2Int>
            >(mapJson);

            Assert.AreEqual(list.Count, listAgain.Count, "List count should round-trip");
            for (int i = 0; i < list.Count; i++)
            {
                Assert.AreEqual(list[i], listAgain[i], $"List element {i} should round-trip");
            }

            Assert.AreEqual(map.Count, mapAgain.Count, "Dictionary count should round-trip");
            foreach (KeyValuePair<string, FastVector2Int> kvp in map)
            {
                Assert.IsTrue(mapAgain.ContainsKey(kvp.Key), $"Key '{kvp.Key}' should exist");
                Assert.AreEqual(
                    kvp.Value,
                    mapAgain[kvp.Key],
                    $"Value for '{kvp.Key}' should match"
                );
            }
        }

        [Test]
        public void CompositePayloadRoundTrips()
        {
            CompositePayload payload = new()
            {
                fv2 = new FastVector2Int(-3, 7),
                fv3 = new FastVector3Int(1, -2, 3),
                v2 = new Vector2(10.5f, -0.25f),
                v3 = new Vector3(-1.5f, 2.75f, 3.25f),
                bounds = new Bounds(new Vector3(1, 2, 3), new Vector3(4, 5, 6)),
                boundsInt = new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6)),
                rect = new Rect(1.5f, 2.5f, 10f, 20f),
                rectInt = new RectInt(1, 2, 3, 4),
                q = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f),
                ri = new Range<int>(1, 10, true, false),
                rf = new Range<float>(-1.25f, 3.5f, false, true),
                listFv3 = new List<FastVector3Int> { new(8, 9, 10) },
                mapFv2 = new Dictionary<string, FastVector2Int> { ["p"] = new(2, 4) },
            };

            string json = Serializer.JsonStringify(payload);
            CompositePayload again = Serializer.JsonDeserialize<CompositePayload>(json);

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
            Assert.AreEqual(payload.v2, again.v2, "Vector2 should round-trip in composite");
            Assert.AreEqual(payload.v3, again.v3, "Vector3 should round-trip in composite");
            Assert.AreEqual(
                payload.bounds.center,
                again.bounds.center,
                "Bounds center should match"
            );
            Assert.AreEqual(payload.bounds.size, again.bounds.size, "Bounds size should match");
            Assert.AreEqual(
                payload.boundsInt.position,
                again.boundsInt.position,
                "BoundsInt position should match"
            );
            Assert.AreEqual(
                payload.boundsInt.size,
                again.boundsInt.size,
                "BoundsInt size should match"
            );
            Assert.AreEqual(payload.rect, again.rect, "Rect should round-trip in composite");
            Assert.AreEqual(
                payload.rectInt,
                again.rectInt,
                "RectInt should round-trip in composite"
            );
            Assert.AreEqual(payload.q, again.q, "Quaternion should round-trip in composite");
            Assert.AreEqual(payload.ri, again.ri, "Range<int> should round-trip in composite");
            Assert.AreEqual(payload.rf, again.rf, "Range<float> should round-trip in composite");
            Assert.AreEqual(payload.listFv3.Count, again.listFv3.Count, "List count should match");
            Assert.AreEqual(payload.listFv3[0], again.listFv3[0], "List element should match");
            Assert.AreEqual(
                payload.mapFv2["p"],
                again.mapFv2["p"],
                "Dictionary element should match"
            );
        }

        [Test]
        public void CompositePayloadRoundTripsWithFastOptions()
        {
            CompositePayload payload = new()
            {
                fv2 = new FastVector2Int(9, -4),
                fv3 = new FastVector3Int(5, 6, -7),
                v2 = new Vector2(-2f, 3f),
                v3 = new Vector3(0.5f, -1.5f, 2.5f),
                bounds = new Bounds(new Vector3(0, 0, 0), new Vector3(1, 1, 1)),
                boundsInt = new BoundsInt(new Vector3Int(0, 0, 0), new Vector3Int(1, 1, 1)),
                rect = new Rect(0, 0, 2, 2),
                rectInt = new RectInt(0, 0, 2, 2),
                q = Quaternion.identity,
                ri = Range<int>.InclusiveExclusive(0, 5),
                rf = Range<float>.ExclusiveInclusive(-1f, 1f),
                listFv3 = new List<FastVector3Int> { new(1, 2, 3), new(3, 2, 1) },
                mapFv2 = new Dictionary<string, FastVector2Int> { ["x"] = new(1, 1) },
            };

            JsonSerializerOptions options = Serializer.CreateFastJsonOptions();
            string json = Serializer.JsonStringify(payload, options);
            CompositePayload again = Serializer.JsonDeserialize<CompositePayload>(
                json,
                null,
                options
            );

            Assert.AreEqual(
                payload.fv2,
                again.fv2,
                "FastVector2Int should round-trip with fast options"
            );
            Assert.AreEqual(
                payload.fv3,
                again.fv3,
                "FastVector3Int should round-trip with fast options"
            );
            Assert.AreEqual(payload.v2, again.v2, "Vector2 should round-trip with fast options");
            Assert.AreEqual(payload.v3, again.v3, "Vector3 should round-trip with fast options");
            Assert.AreEqual(
                payload.bounds.center,
                again.bounds.center,
                "Bounds center should match with fast options"
            );
            Assert.AreEqual(
                payload.bounds.size,
                again.bounds.size,
                "Bounds size should match with fast options"
            );
            Assert.AreEqual(
                payload.boundsInt.position,
                again.boundsInt.position,
                "BoundsInt position should match with fast options"
            );
            Assert.AreEqual(
                payload.boundsInt.size,
                again.boundsInt.size,
                "BoundsInt size should match with fast options"
            );
            Assert.AreEqual(payload.rect, again.rect, "Rect should round-trip with fast options");
            Assert.AreEqual(
                payload.rectInt,
                again.rectInt,
                "RectInt should round-trip with fast options"
            );
            Assert.AreEqual(payload.q, again.q, "Quaternion should round-trip with fast options");
            Assert.AreEqual(payload.ri, again.ri, "Range<int> should round-trip with fast options");
            Assert.AreEqual(
                payload.rf,
                again.rf,
                "Range<float> should round-trip with fast options"
            );
            Assert.AreEqual(
                payload.listFv3.Count,
                again.listFv3.Count,
                "List count should match with fast options"
            );
            Assert.AreEqual(
                payload.listFv3[1],
                again.listFv3[1],
                "Second list element should match with fast options"
            );
            Assert.AreEqual(
                payload.mapFv2["x"],
                again.mapFv2["x"],
                "Dictionary element should match with fast options"
            );
        }
    }
}
