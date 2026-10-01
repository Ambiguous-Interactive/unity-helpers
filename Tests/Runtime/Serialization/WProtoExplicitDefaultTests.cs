// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Serialization
{
    using System;
    using System.Globalization;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Exercises explicit default generation without a runtime protobuf-net oracle.</summary>
    [TestFixture]
    public sealed class WProtoExplicitDefaultTests
    {
        [Test]
        public void ScalarDefaultsOmitOnlyMatchingValuesAndRequiredValuesRemainPresent()
        {
            WProtoBcl.RegisterAll();
            WProtoExplicitDefaultContract value = new WProtoExplicitDefaultContract();
            value.NaN = 0;
            value.Nullable = null;
            value.Plain = 5;
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] encoded));
            CollectionAssert.AreEqual(
                new byte[] { 0xB8, 0x01, 5, 0xD5, 0x01, 0, 0, 0, 0 },
                encoded
            );
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(encoded, out WProtoExplicitDefaultContract restored)
            );
            Assert.AreEqual(-123, restored.Int32);
            Assert.AreEqual("café", restored.Text);
            Assert.AreEqual(0, restored.Plain);
            Assert.AreEqual(5, restored.Required);
            Assert.AreEqual(
                DateTime.Parse("2000-01-01T00:00:00.1234567Z", CultureInfo.InvariantCulture),
                restored.DirectZonedTimestamp
            );
            Assert.AreEqual(
                DateTime.Parse("2001-02-03T00:00:00.7654321+05:30", CultureInfo.InvariantCulture),
                restored.TypedZonedTimestamp
            );
            Assert.IsTrue(float.IsNaN(restored.NaN));
        }

        [Test]
        public void AnAbsentFieldKeepsConstructionStateAndDoesNotAdoptItsAttributeDefault()
        {
            WProtoReader reader = new WProtoReader(Array.Empty<byte>());
            IWProtoFormatter<WProtoExplicitDefaultContract> formatter =
                WProtoFormatterProvider.Get<WProtoExplicitDefaultContract>();
            Assert.IsTrue(formatter.TryRead(ref reader, out WProtoExplicitDefaultContract value));
            Assert.AreEqual(0, value.Plain);
            Assert.AreEqual(-123, value.Int32);
            Assert.AreEqual(5, value.Nullable);
            Assert.AreEqual(1.25M, value.Decimal);
            Assert.AreEqual(TimeSpan.FromSeconds(5), value.Duration);
        }
    }
}
