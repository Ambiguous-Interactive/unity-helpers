// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Pins nullable map presence and compatibility with both protobuf-net majors.</summary>
    [TestFixture]
    public sealed class NullableMapDifferentialTests
    {
        private static byte[] Encode(NullableMapContract value)
        {
            IWProtoFormatter<NullableMapContract> formatter =
                WProtoFormatterProvider.Get<NullableMapContract>();
            byte[] bytes = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(bytes);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(bytes.Length, writer.Position);
            return bytes;
        }

        private static NullableMapContract Decode(byte[] bytes)
        {
            WProtoReader reader = new WProtoReader(bytes);
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<NullableMapContract>()
                    .TryRead(ref reader, out NullableMapContract value)
            );
            return value;
        }

        private static byte[] OracleEncode(NullableMapContract value)
        {
            using MemoryStream stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, value);
            return stream.ToArray();
        }

        private static NullableMapContract OracleDecode(byte[] bytes)
        {
            using MemoryStream stream = new MemoryStream(bytes);
            return ProtoBuf.Serializer.Deserialize<NullableMapContract>(stream);
        }

        private static void AssertMap<TKey, TValue>(
            IEnumerable<KeyValuePair<TKey, TValue>> expected,
            IEnumerable<KeyValuePair<TKey, TValue>> actual
        )
        {
            if (expected == null)
            {
                Assert.That(actual, Is.Null);
                return;
            }
            CollectionAssert.AreEquivalent(expected, actual);
        }

        private static void AssertMaps(NullableMapContract expected, NullableMapContract actual)
        {
            Assert.Multiple(() =>
            {
                AssertMap(expected.Values, actual.Values);
                AssertMap(expected.Enums, actual.Enums);
                AssertMap(expected.Points, actual.Points);
                AssertMap(expected.Keys, actual.Keys);
                AssertMap(expected.EnumKeys, actual.EnumKeys);
                AssertMap(expected.PointKeys, actual.PointKeys);
                AssertMap(expected.Interface, actual.Interface);
                AssertMap(expected.ReadOnly, actual.ReadOnly);
                AssertMap(expected.Sorted, actual.Sorted);
                AssertMap(expected.Frozen, actual.Frozen);
            });
        }

        [TestCase(0, TestName = "NullableMap.Differential.Zero")]
        [TestCase(7, TestName = "NullableMap.Differential.Positive")]
        [TestCase(-7, TestName = "NullableMap.Differential.Negative")]
        [TestCase(int.MinValue, TestName = "NullableMap.Differential.Minimum")]
        [TestCase(int.MaxValue, TestName = "NullableMap.Differential.Maximum")]
        public void NullableHalvesMatchBothOracleWriters(int number)
        {
            NullableMapContract value = new NullableMapContract
            {
                Values = new Dictionary<int, int?>
                {
                    { 1, null },
                    { 2, 0 },
                    { 3, number },
                },
                Enums = new Dictionary<int, ZeroMapKeyKind?>
                {
                    { 1, null },
                    { 2, default(ZeroMapKeyKind) },
                    { 3, ZeroMapKeyKind.Other },
                },
                Points = new Dictionary<int, Outer.Point?>
                {
                    { 1, null },
                    { 2, default(Outer.Point) },
                    {
                        3,
                        new Outer.Point { X = number, Y = 17 }
                    },
                },
                Keys = new Dictionary<int?, int> { { 0, 7 }, { 2, 17 } },
                EnumKeys = new Dictionary<ZeroMapKeyKind?, int>
                {
                    { default(ZeroMapKeyKind), 7 },
                    { ZeroMapKeyKind.Other, 17 },
                },
                PointKeys = new Dictionary<Outer.Point?, int>
                {
                    { default(Outer.Point), 7 },
                    {
                        new Outer.Point { X = 3, Y = 5 },
                        17
                    },
                },
                Interface = new Dictionary<int?, int?>
                {
                    { 0, 0 },
                    { 1, null },
                    { 2, number },
                },
                Sorted = new SortedDictionary<int, int?>
                {
                    { 1, null },
                    { 2, 0 },
                    { 3, number },
                },
            };
            byte[] generated = Encode(value);
            byte[] oracle = OracleEncode(value);
            CollectionAssert.AreEqual(oracle, generated);
            Assert.Multiple(() =>
            {
                AssertMaps(value, Decode(generated));
                AssertMaps(value, Decode(oracle));
#if !PROTOBUF_NET_ORACLE_V2
                AssertMaps(value, OracleDecode(generated));
#endif
            });
        }

        [TestCase(null, "0A020803", TestName = "NullableMap.Golden.Null")]
        [TestCase(0, "0A0408031000", TestName = "NullableMap.Golden.PresentZero")]
        [TestCase(7, "0A0408031007", TestName = "NullableMap.Golden.Nondefault")]
        public void NullableScalarValuesMatchLiteralBytes(int? value, string hex)
        {
            NullableMapContract original = new NullableMapContract
            {
                Values = new Dictionary<int, int?> { { 3, value } },
            };
            byte[] expected = Convert.FromHexString(hex);
            CollectionAssert.AreEqual(expected, Encode(original));
            AssertMaps(original, Decode(expected));
            AssertMaps(original, OracleDecode(expected));
        }

        [Test]
        public void PresentDefaultMessageRetainsEmptyField()
        {
            NullableMapContract original = new NullableMapContract
            {
                Points = new Dictionary<int, Outer.Point?> { { 3, default(Outer.Point) } },
            };
            byte[] expected = Convert.FromHexString("1A0408031200");
            CollectionAssert.AreEqual(expected, Encode(original));
            AssertMaps(original, Decode(expected));
            CollectionAssert.AreEqual(expected, OracleEncode(original));
#if PROTOBUF_NET_ORACLE_V2
            NullableMapContract legacyRead = OracleDecode(expected);
            Assert.IsTrue(legacyRead.Points.TryGetValue(3, out Outer.Point? point));
            Assert.IsFalse(point.HasValue);
#else
            AssertMaps(original, OracleDecode(expected));
#endif
        }

        [Test]
        public void DefaultNullableKeysRemainPresent()
        {
            NullableMapContract original = new NullableMapContract
            {
                Keys = new Dictionary<int?, int> { { 0, 7 } },
            };
            byte[] expected = Convert.FromHexString("220408001007");
            CollectionAssert.AreEqual(expected, Encode(original));
            AssertMaps(original, Decode(expected));
            AssertMaps(original, OracleDecode(expected));
        }

        [Test]
        public void ReadOnlyMapsPreserveBothNullableHalves()
        {
            Dictionary<int?, int?> entries = new Dictionary<int?, int?>
            {
                { 0, 0 },
                { 1, null },
                { 2, -7 },
            };
            NullableMapContract original = new NullableMapContract
            {
                ReadOnly = entries,
                Frozen = new ReadOnlyDictionary<int?, int?>(entries),
            };
            AssertMaps(original, Decode(Encode(original)));
        }

        [TestCase(null, false, TestName = "NullableMap.CustomKey.NullRefused")]
        [TestCase(0, true, TestName = "NullableMap.CustomKey.ZeroAccepted")]
        [TestCase(3, true, TestName = "NullableMap.CustomKey.NondefaultAccepted")]
        public void CustomMapsRequirePresentNullableKeys(int? key, bool expected)
        {
            NullableMapContract original = new NullableMapContract
            {
                ReadOnly = new NullableKeyReadOnlyMap(key),
            };
            IWProtoFormatter<NullableMapContract> formatter =
                WProtoFormatterProvider.Get<NullableMapContract>();
            byte[] bytes = new byte[formatter.Measure(original)];
            WProtoWriter writer = new WProtoWriter(bytes);
            Assert.AreEqual(expected, formatter.Write(ref writer, original));
            if (expected)
            {
                AssertMaps(original, Decode(bytes));
            }
            else
            {
                Assert.AreEqual(0, writer.Position);
            }
        }

        [Test]
        public void DuplicateNullableMessagesMergeWithinTheEntry()
        {
            byte[] bytes = Convert.FromHexString("1A0C080312020807120210091200");
            NullableMapContract value = Decode(bytes);
            Assert.IsTrue(value.Points.TryGetValue(3, out Outer.Point? point));
            Assert.IsTrue(point.HasValue);
            Assert.AreEqual(7, point.Value.X);
            Assert.AreEqual(9, point.Value.Y);
            NullableMapContract oracle = OracleDecode(bytes);
            Assert.IsTrue(oracle.Points.TryGetValue(3, out Outer.Point? observed));
#if PROTOBUF_NET_ORACLE_V2
            Assert.AreEqual(9, observed.GetValueOrDefault().X);
            Assert.AreEqual(0, observed.GetValueOrDefault().Y);
#else
            AssertMaps(oracle, value);
#endif
        }

#if PROTOBUF_NET_ORACLE_V2
        [Test]
        public void VersionTwoReaderLosesEnumPresenceFromItsOwnMatchingBytes()
        {
            NullableMapContract original = new NullableMapContract
            {
                Enums = new Dictionary<int, ZeroMapKeyKind?>
                {
                    { 1, null },
                    { 2, default(ZeroMapKeyKind) },
                    { 3, ZeroMapKeyKind.Other },
                },
            };
            byte[] oracle = OracleEncode(original);
            CollectionAssert.AreEqual(oracle, Encode(original));
            AssertMaps(original, Decode(oracle));
            NullableMapContract legacyRead = OracleDecode(oracle);
            Assert.IsTrue(legacyRead.Enums.TryGetValue(1, out ZeroMapKeyKind? missing));
            Assert.IsFalse(missing.HasValue);
            Assert.IsTrue(legacyRead.Enums.TryGetValue(2, out ZeroMapKeyKind? zero));
            Assert.IsFalse(zero.HasValue);
            Assert.IsTrue(legacyRead.Enums.TryGetValue(3, out ZeroMapKeyKind? nonzero));
#if DEBUG
            Assert.AreEqual(ZeroMapKeyKind.Other, nonzero);
#else
            Assert.AreEqual(default(ZeroMapKeyKind), nonzero);
#endif
            ProtoBuf.Meta.RuntimeTypeModel interpreted = ProtoBuf.Meta.RuntimeTypeModel.Create();
            interpreted.AutoCompile = false;
            using MemoryStream stream = new MemoryStream(oracle);
            NullableMapContract interpretedRead = (NullableMapContract)
                interpreted.Deserialize(stream, null, typeof(NullableMapContract));
            AssertMaps(original, interpretedRead);
        }
#endif

        [TestCase("0A03080310", TestName = "NullableMap.Malformed.TruncatedScalar")]
        [TestCase("1A050803120208", TestName = "NullableMap.Malformed.TruncatedMessage")]
        [TestCase("22021007", TestName = "NullableMap.Malformed.AbsentNullableKey")]
        public void MalformedNullableEntriesReturnFailure(string hex)
        {
            WProtoReader reader = new WProtoReader(Convert.FromHexString(hex));
            Assert.IsFalse(
                WProtoFormatterProvider.Get<NullableMapContract>().TryRead(ref reader, out _)
            );
        }
    }
}
