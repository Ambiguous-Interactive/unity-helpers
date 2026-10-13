// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf.Meta;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Serialization;

    [TestFixture]
    public sealed class RuntimeRootDifferentialTests
    {
        private static void AssertErasedCollection<T>(
            T value,
            byte[] expected,
            bool compareBytes = true
        )
            where T : IEnumerable
        {
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            using MemoryStream oracle = new MemoryStream();
            model.Serialize(oracle, value);
            if (compareBytes)
            {
                CollectionAssert.AreEqual(oracle.ToArray(), expected);
            }
            Assert.IsTrue(
                WProtoFormatterProvider.TryGetRuntimeWriter(value, out IWProtoRuntimeWriter writer)
            );
            Assert.IsTrue(writer.TrySerialize(value, out byte[] generated));
            CollectionAssert.AreEqual(expected, generated);
            byte[] buffer = new byte[expected.Length + 16];
            byte[] original = buffer;
            WProtoWriteResult written = writer.Serialize(value, ref buffer);
            Assert.IsTrue(written.Served);
            Assert.AreSame(original, buffer);
            Assert.AreEqual(expected.Length, written.Length);
            Assert.IsTrue(buffer.AsSpan(0, written.Length).SequenceEqual(expected));
            using MemoryStream input = new MemoryStream(generated);
            CollectionAssert.AreEqual(
                value,
                (IEnumerable)model.Deserialize(input, null, typeof(T))
            );
            CollectionAssert.AreEqual(value, WProtoFacade.Deserialize<T>(oracle.ToArray()));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ErasedArrayRootsMatchBothOracleReadDirections(bool empty)
        {
            int[] value = empty ? Array.Empty<int>() : new[] { 0, -1, int.MinValue, int.MaxValue };
            AssertErasedCollection(value, WProtoFacade.Serialize(value));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ErasedListRootsMatchBothOracleReadDirections(bool empty)
        {
            List<int> value = empty
                ? new List<int>()
                : new List<int> { 0, -1, int.MinValue, int.MaxValue };
            AssertErasedCollection(value, WProtoFacade.Serialize(value));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ErasedDictionaryRootsMatchBothOracleReadDirections(bool empty)
        {
            Dictionary<int, string> value = empty
                ? new Dictionary<int, string>()
                : new Dictionary<int, string> { { -1, "station/café" } };
            AssertErasedCollection(value, WProtoFacade.Serialize(value));
        }

        [Test]
        public void NestedArrayPackingDoesNotChangeTheArrayRootEncoding()
        {
            List<int[]> value = new List<int[]> { new[] { 1, 2 } };
            byte[] generated = WProtoFacade.Serialize(value);
            CollectionAssert.AreEqual(new byte[] { 10, 4, 10, 2, 1, 2 }, generated);
            List<int[]> restored = WProtoFacade.Deserialize<List<int[]>>(generated);
            Assert.AreEqual(1, restored.Count);
            CollectionAssert.AreEqual(value[0], restored[0]);
            CollectionAssert.AreEqual(new byte[] { 8, 1, 8, 2 }, WProtoFacade.Serialize(value[0]));
        }

        [Test]
        public void ErasedEmptyStringMapRootsPreserveBothOracleReadDirections()
        {
            Dictionary<string, string> value = new Dictionary<string, string>
            {
                { "b", string.Empty },
            };
            byte[] expected = WProtoFacade.Serialize(value);
            CollectionAssert.AreEqual(new byte[] { 10, 5, 10, 1, 98, 18, 0 }, expected);
#if PROTOBUF_NET_ORACLE_V2
            AssertErasedCollection(value, expected, false);
#else
            AssertErasedCollection(value, expected);
#endif
        }

        [TestCase(0, false)]
        [TestCase(-1, false)]
        [TestCase(int.MinValue, false)]
        [TestCase(int.MaxValue, false)]
        [TestCase(0, true)]
        [TestCase(-1, true)]
        [TestCase(int.MinValue, true)]
        [TestCase(int.MaxValue, true)]
        public void InterfaceRootsMatchRuntimeOracleBytesAndBothReadDirections(
            int edge,
            bool otherRoot
        )
        {
            RuntimeTypeModel model = RuntimeTypeModel.Create();
#pragma warning disable WPROTO048
            model
                .Add(typeof(WProtoRuntimeSave), false)
                .Add(1, nameof(WProtoRuntimeSave.Health))
                .Add(2, nameof(WProtoRuntimeSave.Station));
            model
                .Add(typeof(WProtoRuntimeOtherSave), false)
                .Add(7, nameof(WProtoRuntimeOtherSave.Currency));
            model[typeof(WProtoRuntimeSave)][1].DefaultValue = 0;
            model[typeof(WProtoRuntimeOtherSave)][7].DefaultValue = 0L;
#pragma warning restore WPROTO048
            IWProtoRuntimeSave value = otherRoot
                ? new WProtoRuntimeOtherSave { Currency = edge }
                : new WProtoRuntimeSave
                {
                    Health = edge,
                    Station = edge == 0 ? null : "station/café",
                };
            Assert.IsFalse(WProtoFacade.TrySerialize(value, out byte[] declined));
            Assert.IsTrue(declined == null);
            Assert.IsTrue(
                WProtoFormatterProvider.TryGetRuntimeWriter(value, out IWProtoRuntimeWriter writer)
            );
            Assert.IsTrue(writer.TrySerialize(value, out byte[] generated));
            using MemoryStream oracle = new MemoryStream();
            model.Serialize(oracle, value);
            CollectionAssert.AreEqual(oracle.ToArray(), generated);
            byte[] reused = new byte[generated.Length + 8];
            WProtoWriteResult result = writer.Serialize(value, ref reused);
            Assert.IsTrue(result.Served);
            Assert.AreEqual(generated.Length, result.Length);
            Assert.IsTrue(reused.AsSpan(0, result.Length).SequenceEqual(generated));
            using MemoryStream input = new MemoryStream(generated);
            object restored = model.Deserialize(input, null, value.GetType());
            if (otherRoot)
            {
                Assert.AreEqual(edge, ((WProtoRuntimeOtherSave)restored).Currency);
                Assert.AreEqual(
                    edge,
                    WProtoFacade.Deserialize<WProtoRuntimeOtherSave>(oracle.ToArray()).Currency
                );
            }
            else
            {
                Assert.AreEqual(edge, ((WProtoRuntimeSave)restored).Health);
                Assert.AreEqual(
                    ((WProtoRuntimeSave)value).Station,
                    ((WProtoRuntimeSave)restored).Station
                );
                WProtoRuntimeSave migrated = WProtoFacade.Deserialize<WProtoRuntimeSave>(
                    oracle.ToArray()
                );
                Assert.AreEqual(edge, migrated.Health);
                Assert.AreEqual(((WProtoRuntimeSave)value).Station, migrated.Station);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void WarmErasedWritesReuseTheBufferWithoutAllocating(int rootKind)
        {
            object value;
            int expectedLength;
            switch (rootKind)
            {
                case 1:
                    int[] array = new[] { 0, -1, int.MaxValue };
                    value = array;
                    expectedLength = WProtoFacade.Serialize(array).Length;
                    break;
                case 2:
                    List<int> list = new List<int> { 0, -1, int.MaxValue };
                    value = list;
                    expectedLength = WProtoFacade.Serialize(list).Length;
                    break;
                case 3:
                    Dictionary<int, string> dictionary = new Dictionary<int, string>
                    {
                        { -1, "station/café" },
                    };
                    value = dictionary;
                    expectedLength = WProtoFacade.Serialize(dictionary).Length;
                    break;
                default:
                    WProtoRuntimeSave save = new WProtoRuntimeSave
                    {
                        Health = 7,
                        Station = "station",
                    };
                    value = save;
                    expectedLength = WProtoFacade.Serialize(save).Length;
                    break;
            }
            byte[] buffer = new byte[128];
            for (int index = 0; index < 32; ++index)
            {
                Assert.IsTrue(
                    WProtoFormatterProvider.TryGetRuntimeWriter(
                        value,
                        out IWProtoRuntimeWriter writer
                    )
                );
                writer.Serialize(value, ref buffer);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            int totalWritten = 0;
            for (int index = 0; index < 128; ++index)
            {
                if (
                    WProtoFormatterProvider.TryGetRuntimeWriter(
                        value,
                        out IWProtoRuntimeWriter writer
                    )
                )
                {
                    totalWritten += writer.Serialize(value, ref buffer).Length;
                }
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(128 * expectedLength, totalWritten);
            Assert.AreEqual(0, allocated);
        }
    }
}
