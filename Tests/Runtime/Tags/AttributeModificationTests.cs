// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System;
    using System.Text.Json;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core;

    public abstract class AttributeTagsTestBase : CommonTestBase
    {
        protected static void ResetEffectHandleId(long value = 0)
        {
            EffectHandle.Id = value;
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AttributeModificationTests
    {
        [Test]
        public void EqualityOperatorsRespectFields()
        {
            AttributeModification baseline = new()
            {
                attribute = "health",
                action = ModificationAction.Multiplication,
                value = 1.5f,
            };

            AttributeModification clone = baseline;
            Assert.IsTrue(baseline == clone);
            Assert.IsFalse(baseline != clone);
            Assert.AreEqual(baseline, clone);
            Assert.AreEqual(baseline.GetHashCode(), clone.GetHashCode());

            AttributeModification differentAttribute = baseline;
            differentAttribute.attribute = "armor";
            Assert.IsFalse(baseline == differentAttribute);
            Assert.IsTrue(baseline != differentAttribute);

            AttributeModification differentAction = baseline;
            differentAction.action = ModificationAction.Addition;
            Assert.IsFalse(baseline.Equals(differentAction));

            AttributeModification differentValue = baseline;
            differentValue.value = 2f;
            Assert.IsFalse(baseline.Equals(differentValue));
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ToStringSerializesAllFields()
        {
            AttributeModification modification = new()
            {
                attribute = "health",
                action = ModificationAction.Override,
                value = 42.5f,
            };

            using JsonDocument document = JsonDocument.Parse(modification.ToString());
            JsonElement root = document.RootElement;
            Assert.AreEqual(
                "health",
                root.GetProperty(nameof(AttributeModification.attribute)).GetString()
            );
            Assert.AreEqual(
                "Override",
                root.GetProperty(nameof(AttributeModification.action)).GetString()
            );
            Assert.AreEqual(
                42.5f,
                root.GetProperty(nameof(AttributeModification.value)).GetSingle()
            );
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void SystemTextJsonRoundtripPreservesFields()
        {
            AttributeModification modification = new(
                "armor",
                ModificationAction.Multiplication,
                1.25f
            );
            string json = Serializer.JsonStringify(modification);
            AttributeModification clone = Serializer.JsonDeserialize<AttributeModification>(json);

            Assert.AreEqual(modification, clone);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ProtoBufRoundtripPreservesFields()
        {
            AttributeModification modification = new("speed", ModificationAction.Addition, -3f);
            byte[] payload = Serializer.ProtoSerialize(modification);
            AttributeModification clone = Serializer.ProtoDeserialize<AttributeModification>(
                payload
            );
            Assert.AreEqual(modification, clone);
        }

        [Test]
        public void CompareToOrdersBasedOnAction()
        {
            AttributeModification addition = new("health", ModificationAction.Addition, 10f);
            AttributeModification multiplication = new(
                "health",
                ModificationAction.Multiplication,
                2f
            );
            AttributeModification overrideValue = new("health", ModificationAction.Override, 0f);

            AttributeModification[] unsorted = { overrideValue, multiplication, addition };

            Array.Sort(unsorted);

            Assert.AreEqual(addition, unsorted[0], "Addition should be applied first when sorted.");
            Assert.AreEqual(
                multiplication,
                unsorted[1],
                "Multiplication should appear after additions when sorted."
            );
            Assert.AreEqual(
                overrideValue,
                unsorted[2],
                "Override should be processed last when sorted."
            );
        }

        [Test]
        public void CompareToObjectReturnsMinusOneForNonAttributeModification()
        {
            AttributeModification addition = new("health", ModificationAction.Addition, 5f);
            Assert.AreEqual(-1, addition.CompareTo("not a modification"));
        }
    }
}
