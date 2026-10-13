// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf.Meta;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Serialization;

    [TestFixture]
    public sealed class RuntimeRootDifferentialTests
    {
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

        [Test]
        public void WarmErasedWritesReuseTheBufferWithoutAllocating()
        {
            object value = new WProtoRuntimeSave { Health = 7, Station = "station" };
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
            Assert.AreEqual(128 * 11, totalWritten);
            Assert.AreEqual(0, allocated);
        }
    }
}
