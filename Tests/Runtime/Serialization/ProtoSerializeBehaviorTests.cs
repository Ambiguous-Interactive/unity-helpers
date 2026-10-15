// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ProtoSerializeBehaviorTests
    {
        [Test]
        public void GenericBasePrefersRuntimeTypeWhenDifferent()
        {
            DerivedMsg original = new() { A = 123, B = "hello" };
            byte[] data = Serializer.ProtoSerialize<BaseMsg>(original);
            DerivedMsg round = Serializer.ProtoDeserialize<DerivedMsg>(data);

            Assert.IsTrue(round != null, "Deserialized instance should not be null");
            Assert.AreEqual(123, round.A, "Base field A should match");
            Assert.AreEqual("hello", round.B, "Derived field B should be preserved");
        }

        [Test]
        public void GenericBasePreservesDerivedFieldsWithForce()
        {
            DerivedMsg original = new() { A = 7, B = "world" };
            byte[] data = Serializer.ProtoSerialize<BaseMsg>(original, forceRuntimeType: true);

            DerivedMsg round = Serializer.ProtoDeserialize<DerivedMsg>(data);

            Assert.IsTrue(round != null, "Deserialized instance should not be null");
            Assert.AreEqual(7, round.A, "Base field A should match");
            Assert.AreEqual("world", round.B, "Derived field B should be preserved");
        }

        [Test]
        public void ObjectDeclaredUsesRuntimeTypeByDefault()
        {
            object original = new DerivedMsg { A = 42, B = "obj" };
            byte[] data = Serializer.ProtoSerialize(original);
            DerivedMsg round = Serializer.ProtoDeserialize<DerivedMsg>(data);

            Assert.IsTrue(round != null, "Deserialized instance should not be null");
            Assert.AreEqual(42, round.A, "Base field A should match");
            Assert.AreEqual("obj", round.B, "Derived field B should be preserved");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void DecliningRuntimeFormatterFallsBackForBothWriteOverloads(
            bool callerBuffer,
            bool forceRuntimeType
        )
        {
            DerivedMsg original = new DerivedMsg { A = -1, B = "fallback/café" };
            byte[] expected = Serializer.ProtoSerialize(original);
            IWProtoFormatter<DerivedMsg> previous = WProtoFormatterProvider.TryGet(
                out IWProtoFormatter<DerivedMsg> current
            )
                ? current
                : null;
            try
            {
                WProtoFormatterProvider.Register(new DecliningProtoFormatter<DerivedMsg>());
                Assert.IsTrue(
                    WProtoFormatterProvider.TryGetRuntimeCodec(
                        original,
                        out IWProtoRuntimeCodec writer
                    )
                );
                Assert.IsFalse(writer.TrySerialize(original, out byte[] declined));
                Assert.IsTrue(declined == null);
                if (callerBuffer)
                {
                    byte[] buffer = new byte[expected.Length + 16];
                    int written = Serializer.ProtoSerialize<object>(
                        original,
                        ref buffer,
                        forceRuntimeType
                    );
                    Assert.AreEqual(expected.Length, written);
                    CollectionAssert.AreEqual(expected, buffer.AsSpan(0, written).ToArray());
                }
                else
                {
                    CollectionAssert.AreEqual(
                        expected,
                        Serializer.ProtoSerialize<object>(original, forceRuntimeType)
                    );
                }
            }
            finally
            {
                WProtoFormatterProvider.Register(previous);
            }
        }

        [ProtoContract]
        [ProtoInclude(100, typeof(DerivedMsg))]
        private class BaseMsg
        {
            [ProtoMember(1)]
            public int A { get; set; }
        }

        [ProtoContract]
        private class DerivedMsg : BaseMsg
        {
            [ProtoMember(2)]
            public string B { get; set; }
        }
    }
}
