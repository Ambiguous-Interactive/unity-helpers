// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    public sealed class UnityStructSurrogateDifferentialTests
    {
        private static IEnumerable<TestCaseData> Cases()
        {
            float[] values =
            {
                0f,
                BitConverter.Int32BitsToSingle(int.MinValue),
                float.Epsilon,
                float.MaxValue,
                float.MinValue,
                float.PositiveInfinity,
                float.NegativeInfinity,
                BitConverter.Int32BitsToSingle(0x7fc01234),
                1.25f,
            };
            foreach (float value in values)
            {
                yield return new TestCaseData(
                    (Action)(
                        () =>
                            AssertParity(
                                new Vector4Surrogate
                                {
                                    x = value,
                                    y = value,
                                    z = value,
                                    w = value,
                                }
                            )
                    )
                ).SetName($"Vector4WireParity{BitConverter.SingleToInt32Bits(value):X8}");
                yield return new TestCaseData(
                    (Action)(
                        () =>
                            AssertParity(
                                new Matrix4x4Surrogate
                                {
                                    m00 = value,
                                    m10 = value,
                                    m20 = value,
                                    m30 = value,
                                    m01 = value,
                                    m11 = value,
                                    m21 = value,
                                    m31 = value,
                                    m02 = value,
                                    m12 = value,
                                    m22 = value,
                                    m32 = value,
                                    m03 = value,
                                    m13 = value,
                                    m23 = value,
                                    m33 = value,
                                }
                            )
                    )
                ).SetName($"Matrix4x4WireParity{BitConverter.SingleToInt32Bits(value):X8}");
                yield return new TestCaseData(
                    (Action)(
                        () =>
                            AssertParity(
                                new KeyframeSurrogate
                                {
                                    time = value,
                                    value = value,
                                    inTangent = value,
                                    outTangent = value,
                                    inWeight = value,
                                    outWeight = value,
                                    weightedMode = 3,
                                }
                            )
                    )
                ).SetName($"KeyframeWireParity{BitConverter.SingleToInt32Bits(value):X8}");
                yield return new TestCaseData(
                    (Action)(
                        () =>
                            AssertParity(
                                new BoneWeightSurrogate
                                {
                                    weight0 = value,
                                    weight1 = value,
                                    weight2 = value,
                                    weight3 = value,
                                    boneIndex0 = int.MaxValue,
                                    boneIndex1 = int.MinValue,
                                    boneIndex2 = int.MaxValue,
                                    boneIndex3 = int.MinValue,
                                }
                            )
                    )
                ).SetName($"BoneWeightWireParity{BitConverter.SingleToInt32Bits(value):X8}");
            }
        }

        private static void AssertParity<T>(T original)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            Assert.IsTrue(formatter != null);
            byte[] mine = Encode(formatter, original);
            using MemoryStream oracle = new();
            ProtoBuf.Serializer.Serialize(oracle, original);
            byte[] theirs = oracle.ToArray();
            CollectionAssert.AreEqual(theirs, mine);

            using MemoryStream mineStream = new(mine);
            T readByOracle = ProtoBuf.Serializer.Deserialize<T>(mineStream);
            CollectionAssert.AreEqual(mine, Encode(formatter, readByOracle));

            WProtoReader reader = new(theirs);
            Assert.IsTrue(formatter.TryRead(ref reader, out T readByUs));
            CollectionAssert.AreEqual(theirs, Encode(formatter, readByUs));
        }

        private static byte[] Encode<T>(IWProtoFormatter<T> formatter, T value)
        {
            byte[] bytes = new byte[formatter.Measure(value)];
            WProtoWriter writer = new(bytes);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(bytes.Length, writer.Position);
            return bytes;
        }

        [TestCaseSource(nameof(Cases))]
        public void UnityStructBytesAndCrossReadsAgreeWithTheOracle(Action verify)
        {
            verify();
        }
    }
}
