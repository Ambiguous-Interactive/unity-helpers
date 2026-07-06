// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// UNH-SUPPRESS UNH003: AttributeEffectTests inherits from AttributeTagsTestBase which inherits from CommonTestBase
namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System.Text.Json;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tags;
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
#endif

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AttributeEffectTests : AttributeTagsTestBase
    {
        [Test]
        public void AttributeEffectUsesExpectedScriptableObjectBase()
        {
            AttributeEffect effect = Track(ScriptableObject.CreateInstance<AttributeEffect>());

            Assert.IsInstanceOf<ScriptableObject>(effect);
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
            Assert.IsInstanceOf<SerializedScriptableObject>(effect);
#endif
        }

        [Test]
        public void HumanReadableDescriptionFormatsAllModificationTypes()
        {
            AttributeEffect effect = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            effect.name = "Composite";
            effect.modifications.Add(
                new AttributeModification
                {
                    attribute = "health",
                    action = ModificationAction.Addition,
                    value = 5f,
                }
            );
            effect.modifications.Add(
                new AttributeModification
                {
                    attribute = "attack_speed",
                    action = ModificationAction.Multiplication,
                    value = 1.5f,
                }
            );
            effect.modifications.Add(
                new AttributeModification
                {
                    attribute = "armor",
                    action = ModificationAction.Override,
                    value = 10f,
                }
            );

            string description = effect.HumanReadableDescription;
            Assert.AreEqual("+5 Health, +50% Attack Speed, 10 Armor", description);
        }

        [Test]
        public void HumanReadableDescriptionSkipsNeutralModifications()
        {
            AttributeEffect effect = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            effect.modifications.Add(
                new AttributeModification
                {
                    attribute = "health",
                    action = ModificationAction.Addition,
                    value = 0f,
                }
            );
            effect.modifications.Add(
                new AttributeModification
                {
                    attribute = "speed",
                    action = ModificationAction.Multiplication,
                    value = 1f,
                }
            );

            Assert.IsEmpty(effect.HumanReadableDescription);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ToStringSerializesSummaryAndCollections()
        {
            AttributeEffect effect = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            effect.name = "JsonEffect";
            effect.durationType = ModifierDurationType.Duration;
            effect.duration = 3.25f;
            effect.resetDurationOnReapplication = true;
            effect.modifications.Add(
                new AttributeModification
                {
                    attribute = "health",
                    action = ModificationAction.Addition,
                    value = 10f,
                }
            );
            effect.effectTags.Add("Buff");

            GameObject cosmeticHolder = Track(new GameObject("Glow", typeof(CosmeticEffectData)));
            CosmeticEffectData cosmeticData = cosmeticHolder.GetComponent<CosmeticEffectData>();
            effect.cosmeticEffects.Add(cosmeticData);

            using JsonDocument document = JsonDocument.Parse(effect.ToString());
            JsonElement root = document.RootElement;
            Assert.AreEqual(
                effect.HumanReadableDescription,
                root.GetProperty("Description").GetString()
            );
            Assert.AreEqual("Duration", root.GetProperty("durationType").GetString());
            Assert.AreEqual(3.25f, root.GetProperty("duration").GetSingle());
            Assert.AreEqual("Buff", root.GetProperty("tags")[0].GetString());
            Assert.AreEqual("Glow", root.GetProperty("CosmeticEffects")[0].GetString());
            Assert.AreEqual(1, root.GetProperty("modifications").GetArrayLength());
        }

        [Test]
        public void EqualsRequiresMatchingState()
        {
            AttributeEffect left = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            AttributeEffect right = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            left.name = right.name = "Stack";
            left.durationType = right.durationType = ModifierDurationType.Duration;
            left.duration = right.duration = 2f;
            left.resetDurationOnReapplication = right.resetDurationOnReapplication = false;

            AttributeModification modification = new()
            {
                attribute = "health",
                action = ModificationAction.Addition,
                value = 5f,
            };

            left.modifications.Add(modification);
            right.modifications.Add(modification);
            Assert.IsTrue(left.Equals(right));

            right.modifications[0] = new AttributeModification
            {
                attribute = "health",
                action = ModificationAction.Addition,
                value = 10f,
            };

            Assert.IsFalse(left.Equals(right));
        }
    }
}
