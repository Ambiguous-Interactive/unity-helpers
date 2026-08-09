// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tags;
    using Attribute = WallstopStudios.UnityHelpers.Tags.Attribute;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    /// <summary>
    /// Pins what deserialization does to <see cref="Attribute"/>'s cached current value. The class
    /// used to carry an <c>[OnDeserialized]</c> and a <c>[ProtoAfterDeserialization]</c> hook that
    /// cleared the cache; neither was ever called by any serializer the package uses, and either
    /// one working would change the behavior asserted here.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
    public sealed class AttributeSerializationTests : AttributeTagsTestBase
    {
        [SetUp]
        public void SetUp()
        {
            ResetEffectHandleId();
        }

        [Test]
        public void JsonRoundTripKeepsTheModifiedValueRatherThanRecalculating()
        {
            Attribute attribute = new(100f);
            attribute.Add(25f);
            Assert.AreEqual(125f, attribute.CurrentValue);

            string json = Serializer.JsonStringify(attribute);
            Attribute deserialized = Serializer.JsonDeserialize<Attribute>(json);

            Assert.IsTrue(deserialized != null);
            Assert.AreEqual(100f, deserialized.BaseValue);

            // Modifications are not serialized. Recalculating on load would report 100 for an
            // attribute that was written while buffed, so the written value is kept instead.
            Assert.AreEqual(125f, deserialized.CurrentValue);
        }

        [Test]
        public void ClearCacheAfterJsonRoundTripFallsBackToTheBaseValue()
        {
            Attribute attribute = new(100f);
            attribute.Add(25f);

            string json = Serializer.JsonStringify(attribute);
            Attribute deserialized = Serializer.JsonDeserialize<Attribute>(json);

            Assert.IsTrue(deserialized != null);
            Assert.AreEqual(125f, deserialized.CurrentValue);

            deserialized.ClearCache();

            // The escape hatch documented on ClearCache: a caller that intends to rebuild
            // modifications opts into recalculation explicitly.
            Assert.AreEqual(100f, deserialized.CurrentValue);
        }

        [Test]
        public void JsonRoundTripOfAnUnmodifiedAttributePreservesBothValues()
        {
            Attribute attribute = new(42.5f);

            string json = Serializer.JsonStringify(attribute);
            Attribute deserialized = Serializer.JsonDeserialize<Attribute>(json);

            Assert.IsTrue(deserialized != null);
            Assert.AreEqual(42.5f, deserialized.BaseValue);
            Assert.AreEqual(42.5f, deserialized.CurrentValue);
        }
    }
}
