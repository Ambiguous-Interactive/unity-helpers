// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoUnityStructMemberTests
    {
        private static WProtoUnityStructHolder Sample()
        {
            return new WProtoUnityStructHolder
            {
                vector = new Vector4(1f, 2f, 3f, 4f),
                matrices = new[]
                {
                    Matrix4x4.identity,
                    Matrix4x4.Translate(new Vector3(1f, 2f, 3f)),
                },
                keyframes = new[]
                {
                    new Keyframe(1f, 2f, 3f, 4f, 0.25f, 0.75f) { weightedMode = WeightedMode.Both },
                    default,
                },
                boneWeights = new[]
                {
                    new BoneWeight
                    {
                        weight0 = 0.25f,
                        weight1 = 0.75f,
                        boneIndex0 = 2,
                        boneIndex1 = 3,
                    },
                    default,
                },
            };
        }

        private static void AssertEquivalent(
            WProtoUnityStructHolder expected,
            WProtoUnityStructHolder actual
        )
        {
            Assert.IsTrue(actual != null);
            Assert.AreEqual(expected.vector, actual.vector);
            CollectionAssert.AreEqual(expected.matrices, actual.matrices);
            Assert.IsTrue(actual.keyframes != null);
            Assert.AreEqual(expected.keyframes.Length, actual.keyframes.Length);
            for (int index = 0; index < expected.keyframes.Length; index++)
            {
                KeyframeSurrogate left = expected.keyframes[index];
                KeyframeSurrogate right = actual.keyframes[index];
                Assert.AreEqual(left, right);
            }
            CollectionAssert.AreEqual(expected.boneWeights, actual.boneWeights);
        }

        [Test]
        public void UnityStructMembersAndArraysRoundTripThroughWallstopProto()
        {
            WProtoUnityStructHolder expected = Sample();
            Assert.IsTrue(WProtoFacade.TrySerialize(expected, out byte[] bytes));
            Assert.IsTrue(WProtoFacade.TryDeserialize(bytes, out WProtoUnityStructHolder actual));
            AssertEquivalent(expected, actual);
        }

        [Test]
        [SkipUnderIL2CPP]
        public void UnityStructMembersAndArraysMatchAndCrossReadProtobufNet()
        {
            ProtobufUnityModel.EnsureInitialized();
            WProtoUnityStructHolder expected = Sample();
            Assert.IsTrue(WProtoFacade.TrySerialize(expected, out byte[] mine));
            using System.IO.MemoryStream stream = new();
            ProtoBuf.Serializer.Serialize(stream, expected);
            byte[] theirs = stream.ToArray();
            CollectionAssert.AreEqual(theirs, mine);
            Assert.IsTrue(WProtoFacade.TryDeserialize(theirs, out WProtoUnityStructHolder actual));
            AssertEquivalent(expected, actual);
            using System.IO.MemoryStream input = new(mine);
            AssertEquivalent(
                expected,
                ProtoBuf.Serializer.Deserialize<WProtoUnityStructHolder>(input)
            );
        }
    }
}
