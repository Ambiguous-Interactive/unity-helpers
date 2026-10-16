// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Serialization
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Runs nullable-map wire and corruption controls on Mono and IL2CPP.</summary>
    [TestFixture]
    [Category("Fast")]
    public sealed class WProtoNullableMapTests
    {
        private static byte[] Bytes(string hex)
        {
            byte[] result = new byte[hex.Length / 2];
            for (int index = 0; index < result.Length; ++index)
            {
                result[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }
            return result;
        }

        [SetUp]
        public void RegisterBuiltInFormatters()
        {
            WProtoBcl.RegisterAll();
        }

        [TestCase(null, "0A020803", TestName = "NullableMap.Native.Null")]
        [TestCase(0, "0A0408031000", TestName = "NullableMap.Native.PresentZero")]
        [TestCase(7, "0A0408031007", TestName = "NullableMap.Native.Nondefault")]
        public void GeneratedScalarMapsRetainPresence(int? value, string hex)
        {
            WProtoNullableMapContract original = new WProtoNullableMapContract
            {
                Values = new Dictionary<int, int?> { { 3, value } },
            };
            Assert.IsTrue(WProtoFormatterProvider.TryGet<WProtoNullableMapContract>(out _));
            Assert.IsTrue(WProtoFacade.TrySerialize(original, out byte[] bytes));
            CollectionAssert.AreEqual(Bytes(hex), bytes);
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(bytes, out WProtoNullableMapContract restored)
            );
            Assert.IsTrue(restored.Values.TryGetValue(3, out int? actual));
            Assert.AreEqual(value, actual);
        }

        [Test]
        public void GeneratedNullableKeysAndValuesMatchLiteralBytes()
        {
            WProtoNullableMapContract original = new WProtoNullableMapContract
            {
                Keys = new Dictionary<int?, int?>
                {
                    { 0, 0 },
                    { 1, null },
                    { 2, 7 },
                },
            };
            Assert.IsTrue(WProtoFacade.TrySerialize(original, out byte[] bytes));
            CollectionAssert.AreEqual(Bytes("12040800100012020801120408021007"), bytes);
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(bytes, out WProtoNullableMapContract restored)
            );
            CollectionAssert.AreEquivalent(original.Keys, restored.Keys);
        }

        [Test]
        public void NullableMessageValuesDistinguishNullAndPresentDefaults()
        {
            WProtoNullableMapContract original = new WProtoNullableMapContract
            {
                Dates = new Dictionary<int, DateTime?>
                {
                    { 1, null },
                    { 2, default(DateTime) },
                    { 3, new DateTime(2000, 1, 2, 3, 4, 5, DateTimeKind.Utc) },
                },
            };
            Assert.IsTrue(WProtoFacade.TrySerialize(original, out byte[] bytes));
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(bytes, out WProtoNullableMapContract restored)
            );
            CollectionAssert.AreEquivalent(original.Dates, restored.Dates);
        }

        [TestCase("0A03080310", TestName = "NullableMap.Native.TruncatedScalar")]
        [TestCase("1A050803120208", TestName = "NullableMap.Native.TruncatedMessage")]
        [TestCase("12021007", TestName = "NullableMap.Native.AbsentKey")]
        public void InvalidNullableMapEntriesAreRefused(string hex)
        {
            WProtoReader reader = new WProtoReader(Bytes(hex));
            Assert.IsFalse(
                WProtoFormatterProvider.Get<WProtoNullableMapContract>().TryRead(ref reader, out _)
            );
        }
    }
}
