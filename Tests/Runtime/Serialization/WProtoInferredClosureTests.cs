// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Serialization
{
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Exercises factory closures without spelling their constructed contract types.</summary>
    [TestFixture]
    public sealed class WProtoInferredClosureTests
    {
        private static WProtoInferredResultContract<T> Create<T>(T value)
        {
            return new WProtoInferredResultContract<T> { Value = value };
        }

        private static void AssertCompatible<T>(T value)
        {
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<T>());
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] generatedBytes));
            using (MemoryStream oracleStream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(oracleStream, value);
                byte[] oracleBytes = oracleStream.ToArray();
                CollectionAssert.AreEqual(oracleBytes, generatedBytes);
                Assert.IsTrue(WProtoFacade.TryDeserialize(oracleBytes, out T restored));
                Assert.AreEqual(value, restored);
            }
            using (MemoryStream generatedStream = new MemoryStream(generatedBytes))
            {
                Assert.AreEqual(value, ProtoBuf.Serializer.Deserialize<T>(generatedStream));
            }
        }

        [TestCase(0, TestName = "InferredFactory.Integer.Zero")]
        [TestCase(-1, TestName = "InferredFactory.Integer.Negative")]
        [TestCase(150, TestName = "InferredFactory.Integer.Positive")]
        [TestCase(int.MaxValue, TestName = "InferredFactory.Integer.Maximum")]
        public void InferredIntegerFactoryRoundTripsAgainstOracle(int value)
        {
            AssertCompatible(Create(value));
        }

        [TestCase(null, TestName = "InferredFactory.String.Null")]
        [TestCase("", TestName = "InferredFactory.String.Empty")]
        [TestCase("payload", TestName = "InferredFactory.String.Text")]
        public void InferredStringFactoryRoundTripsAgainstOracle(string value)
        {
            AssertCompatible(Create(value));
        }

        [TestCase(0.0, TestName = "InferredFactory.Double.Zero")]
        [TestCase(-1.5, TestName = "InferredFactory.Double.Negative")]
        [TestCase(double.PositiveInfinity, TestName = "InferredFactory.Double.Infinity")]
        public void InferredDoubleFactoryRoundTripsAgainstOracle(double value)
        {
            AssertCompatible(Create(value));
        }
    }
}
