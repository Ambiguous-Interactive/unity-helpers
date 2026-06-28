// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tags;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
    public sealed class PeriodicEffectDefinitionSerializationTests
    {
        [Test]
        public void JsonRoundtripPreservesFieldValues()
        {
            PeriodicEffectDefinition definition = CreateDefinition();

            string json = Serializer.JsonStringify(definition);
            PeriodicEffectDefinition deserialized =
                Serializer.JsonDeserialize<PeriodicEffectDefinition>(json);

            AssertEquivalent(definition, deserialized);
        }

        [Test]
        public void ProtoRoundtripPreservesFieldValues()
        {
            PeriodicEffectDefinition definition = CreateDefinition();

            byte[] serialized = Serializer.ProtoSerialize(definition);
            Assert.IsTrue(serialized != null, "Serialized bytes should not be null");
            Assert.Greater(serialized.Length, 0);

            PeriodicEffectDefinition deserialized =
                Serializer.ProtoDeserialize<PeriodicEffectDefinition>(serialized);

            AssertEquivalent(definition, deserialized);
        }

        private static PeriodicEffectDefinition CreateDefinition()
        {
            PeriodicEffectDefinition definition = new()
            {
                name = "Damage Pulse",
                initialDelay = 0.35f,
                interval = 0.8f,
                maxTicks = 6,
                modifications = new List<AttributeModification>
                {
                    new()
                    {
                        attribute = "health",
                        action = ModificationAction.Addition,
                        value = -7.5f,
                    },
                    new()
                    {
                        attribute = "armor",
                        action = ModificationAction.Multiplication,
                        value = 0.85f,
                    },
                },
            };

            return definition;
        }

        private static void AssertEquivalent(
            PeriodicEffectDefinition expected,
            PeriodicEffectDefinition actual
        )
        {
            Assert.IsTrue(actual != null, "Deserialized definition should not be null");
            Assert.AreNotSame(expected, actual);
            Assert.AreEqual(expected.name, actual.name);
            Assert.AreEqual(expected.initialDelay, actual.initialDelay);
            Assert.AreEqual(expected.interval, actual.interval);
            Assert.AreEqual(expected.maxTicks, actual.maxTicks);

            Assert.IsTrue(
                actual.modifications != null,
                "Modifications list should not be null after deserialization"
            );
            Assert.AreNotSame(expected.modifications, actual.modifications);
            Assert.AreEqual(expected.modifications.Count, actual.modifications.Count);

            for (int i = 0; i < expected.modifications.Count; ++i)
            {
                AttributeModification expectedModification = expected.modifications[i];
                AttributeModification actualModification = actual.modifications[i];
                Assert.AreEqual(expectedModification.attribute, actualModification.attribute);
                Assert.AreEqual(expectedModification.action, actualModification.action);
                Assert.AreEqual(expectedModification.value, actualModification.value);
            }
        }
    }
}
