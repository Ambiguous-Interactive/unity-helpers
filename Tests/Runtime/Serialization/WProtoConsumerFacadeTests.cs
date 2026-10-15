// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoConsumerFacadeTests
    {
        private static void AssertValue<T>(
            WProtoImmutableConsumerValue<T> expected,
            WProtoImmutableConsumerValue<T> actual
        )
        {
            Assert.AreEqual(expected.Identifier, actual.Identifier);
            Assert.AreEqual(expected.Sequence, actual.Sequence);
        }

        private static void VerifyProtobuf<T>(WProtoImmutableConsumerValue<T> expected)
        {
            Assert.IsTrue(WProtoFacade.TrySerialize(expected, out byte[] bytes));
            foreach (bool forceRuntimeType in new[] { false, true })
            {
                CollectionAssert.AreEqual(
                    bytes,
                    Serializer.ProtoSerialize(expected, forceRuntimeType)
                );
                CollectionAssert.AreEqual(
                    bytes,
                    Serializer.ProtoSerialize<object>(expected, forceRuntimeType)
                );
                byte[] buffer = new byte[256];
                byte[] original = buffer;
                int length = Serializer.ProtoSerialize(expected, ref buffer, forceRuntimeType);
                Assert.AreSame(original, buffer);
                Assert.AreEqual(bytes.Length, length);
                CollectionAssert.AreEqual(bytes, new ArraySegment<byte>(buffer, 0, length));
                length = Serializer.ProtoSerialize<object>(expected, ref buffer, forceRuntimeType);
                Assert.AreSame(original, buffer);
                Assert.AreEqual(bytes.Length, length);
                CollectionAssert.AreEqual(bytes, new ArraySegment<byte>(buffer, 0, length));
            }
            AssertValue(
                expected,
                Serializer.ProtoDeserialize<WProtoImmutableConsumerValue<T>>(bytes)
            );
            AssertValue(
                expected,
                Serializer.ProtoDeserialize<WProtoImmutableConsumerValue<T>>(
                    bytes,
                    typeof(WProtoImmutableConsumerValue<T>)
                )
            );
            AssertValue(
                expected,
                (WProtoImmutableConsumerValue<T>)
                    Serializer.ProtoDeserialize<object>(
                        bytes,
                        typeof(WProtoImmutableConsumerValue<T>)
                    )
            );
            Assert.IsTrue(
                Serializer.TryProtoDeserialize(bytes, out WProtoImmutableConsumerValue<T> restored)
            );
            AssertValue(expected, restored);
            Assert.IsTrue(
                Serializer.TryProtoDeserialize(
                    bytes,
                    typeof(WProtoImmutableConsumerValue<T>),
                    out restored
                )
            );
            AssertValue(expected, restored);
            Assert.IsTrue(
                Serializer.TryProtoDeserialize(
                    bytes,
                    typeof(WProtoImmutableConsumerValue<T>),
                    out object erased
                )
            );
            AssertValue(expected, (WProtoImmutableConsumerValue<T>)erased);
        }

        [TestCase(0, false)]
        [TestCase(-1, false)]
        [TestCase(int.MinValue, false)]
        [TestCase(int.MaxValue, false)]
        [TestCase(0, true)]
        [TestCase(-1, true)]
        [TestCase(int.MinValue, true)]
        [TestCase(int.MaxValue, true)]
        public void ImmutableConsumerClosuresUseEveryProtobufFacadePath(
            int edge,
            bool textIdentifier
        )
        {
            Assert.AreNotSame(
                typeof(WProtoConsumerFacadeTests).Assembly,
                typeof(WProtoImmutableConsumerValue<>).Assembly
            );
            long sequence =
                edge == 0 ? 0
                : edge == -1 ? 9007199254740993L
                : edge == int.MinValue ? long.MinValue
                : long.MaxValue;
            if (textIdentifier)
            {
                string identifier =
                    edge == 0 ? string.Empty
                    : edge == -1 ? null
                    : "café/中/" + edge;
                VerifyProtobuf(new WProtoImmutableConsumerValue<string>(identifier, sequence));
            }
            else
            {
                VerifyProtobuf(new WProtoImmutableConsumerValue<int>(edge, sequence));
            }
        }

        [Test]
        public void ImmutableConsumerReadFailuresKeepThePublicExceptionContract()
        {
            byte[] corrupt = { 0x80 };
            Assert.IsTrue(
                WProtoFormatterProvider.IsRegistered<WProtoImmutableConsumerValue<int>>()
            );
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.ProtoDeserialize<WProtoImmutableConsumerValue<int>>(corrupt)
            );
            Assert.IsFalse(
                Serializer.TryProtoDeserialize(corrupt, out WProtoImmutableConsumerValue<int> _)
            );
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.ProtoDeserialize<object>(
                    corrupt,
                    typeof(WProtoImmutableConsumerValue<int>)
                )
            );
            Assert.IsFalse(
                Serializer.TryProtoDeserialize(
                    corrupt,
                    typeof(WProtoImmutableConsumerValue<int>),
                    out WProtoImmutableConsumerValue<int> _
                )
            );
        }

        [Test]
        public void ExplicitConsumerTypeCannotBeReadIntoAnUnrelatedDeclaration()
        {
            byte[] bytes = WProtoFacade.Serialize(new WProtoImmutableConsumerValue<int>(7, 99));
            Assert.IsFalse(
                WProtoFacade.TryDeserializeAs<string>(
                    bytes,
                    typeof(WProtoImmutableConsumerValue<int>),
                    out _
                )
            );
            Assert.Throws<SerializationTypeException>(() =>
                WProtoFacade.DeserializeAs<string>(bytes, typeof(WProtoImmutableConsumerValue<int>))
            );
            Assert.IsFalse(WProtoFacade.TryDeserializeAs<object>(bytes, null, out _));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GenericCollectionsOfImmutableValuesUseTheJsonFacade(bool empty)
        {
            Deque<FastVector2Int> expected = new(4);
            if (!empty)
            {
                expected.PushBack(new FastVector2Int(int.MinValue, int.MaxValue));
                expected.PushBack(new FastVector2Int(-1, 0));
            }
            byte[] bytes = Serializer.JsonSerialize(expected);
            byte[] buffer = new byte[512];
            byte[] original = buffer;
            int length = Serializer.JsonSerialize(expected, ref buffer);
            Assert.AreSame(original, buffer);
            CollectionAssert.AreEqual(bytes, new ArraySegment<byte>(buffer, 0, length));
            CollectionAssert.AreEqual(
                expected,
                Serializer.JsonDeserialize<Deque<FastVector2Int>>(bytes)
            );
            CollectionAssert.AreEqual(
                expected,
                Serializer.JsonDeserialize<Deque<FastVector2Int>>(
                    Serializer.JsonStringify(expected)
                )
            );
            Assert.IsTrue(Serializer.TryJsonDeserialize(bytes, out Deque<FastVector2Int> restored));
            CollectionAssert.AreEqual(expected, restored);
        }
    }
}
