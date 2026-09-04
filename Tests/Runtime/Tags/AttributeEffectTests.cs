// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// UNH-SUPPRESS UNH003: AttributeEffectTests inherits from AttributeTagsTestBase which inherits from CommonTestBase
namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Tags.Helpers;
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

        [Test]
        [TestCaseSource(nameof(AuthoredFieldMutations))]
        public void EqualsComparesEveryAuthoredField(Action<AttributeEffect> mutate)
        {
            AttributeEffect left = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            AttributeEffect right = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            ConfigureBaseline(left);
            ConfigureBaseline(right);

            Assert.IsTrue(left.Equals(right), "Two identically authored effects must be equal");
            Assert.AreEqual(
                left.GetHashCode(),
                right.GetHashCode(),
                "Two identically authored effects must share a hash code"
            );

            mutate(right);

            /*
                Every one of these fields changes how the effect behaves, and the XML doc has always
                promised "all fields". Six of them -- periodicEffects, behaviors and the four
                stacking fields -- were never read, so two effects that stacked differently
                compared equal.
            */
            Assert.IsFalse(left.Equals(right), "A change to an authored field must break equality");
            Assert.IsFalse(right.Equals(left), "Inequality must hold in both directions");
        }

        [Test]
        public void EqualsComparesBehaviourReferences()
        {
            AttributeEffect left = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            AttributeEffect right = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            ConfigureBaseline(left);
            ConfigureBaseline(right);

            EffectBehavior behavior = Track(
                ScriptableObject.CreateInstance<RecordingEffectBehavior>()
            );
            left.behaviors.Add(behavior);

            Assert.IsFalse(left.Equals(right), "A behaviour only one effect carries must differ");

            right.behaviors.Add(behavior);

            Assert.IsTrue(left.Equals(right));
            Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
        }

        [Test]
        public void EqualsComparesCosmeticEffects()
        {
            AttributeEffect left = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            AttributeEffect right = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            ConfigureBaseline(left);
            ConfigureBaseline(right);

            GameObject glowHolder = Track(new GameObject("Glow", typeof(CosmeticEffectData)));
            CosmeticEffectData glow = glowHolder.GetComponent<CosmeticEffectData>();
            left.cosmeticEffects.Add(glow);

            Assert.IsFalse(left.Equals(right), "A cosmetic only one effect carries must differ");
            Assert.IsFalse(right.Equals(left), "Inequality must hold in both directions");

            right.cosmeticEffects.Add(glow);

            Assert.IsTrue(left.Equals(right));
            Assert.AreEqual(
                left.GetHashCode(),
                right.GetHashCode(),
                "Effects carrying the same cosmetics must share a hash code"
            );

            GameObject sparkHolder = Track(new GameObject("Spark", typeof(CosmeticEffectData)));
            right.cosmeticEffects[0] = sparkHolder.GetComponent<CosmeticEffectData>();

            Assert.IsFalse(left.Equals(right), "A different cosmetic asset must break equality");
        }

        [Test]
        public void GetHashCodeReadsNoNativeStateOnADestroyedEffect()
        {
            AttributeEffect effect = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            ConfigureBaseline(effect);

            GameObject glowHolder = Track(new GameObject("Glow", typeof(CosmeticEffectData)));
            effect.cosmeticEffects.Add(glowHolder.GetComponent<CosmeticEffectData>());
            effect.behaviors.Add(Track(ScriptableObject.CreateInstance<RecordingEffectBehavior>()));

            UnityEngine.Object.DestroyImmediate(effect); // UNH-SUPPRESS: the state under test

            /*
                A hash is computed on every probe of every set and dictionary the effect is in, so
                it may never touch native state: name raises MissingReferenceException once the
                asset is gone, and hashing a cosmetic walks its components.
            */
            Assert.DoesNotThrow(() => effect.GetHashCode());
        }

        [Test]
        public void EqualsReadsNoNativeStateOnADestroyedEffect()
        {
            AttributeEffect destroyed = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            AttributeEffect live = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            ConfigureBaseline(destroyed);
            ConfigureBaseline(live);

            Assert.IsTrue(destroyed.Equals(live));

            UnityEngine.Object.DestroyImmediate(destroyed); // UNH-SUPPRESS: the state under test

            /*
                Every probe of a set or dictionary calls Equals on the key it stored, which may be
                the destroyed one, so reading name there raises MissingReferenceException from a
                public member.
            */
            Assert.IsFalse(destroyed.Equals(live));
            Assert.IsFalse(live.Equals(destroyed), "Inequality must hold in both directions");
            Assert.IsTrue(
                destroyed.Equals(destroyed),
                "A destroyed effect must still be equal to itself"
            );
            Assert.IsFalse(new List<AttributeEffect> { destroyed }.Contains(live));
        }

        [Test]
        public void EqualsReadsNoNativeStateOnADestroyedCosmeticEffect()
        {
            AttributeEffect left = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            AttributeEffect right = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            ConfigureBaseline(left);
            ConfigureBaseline(right);

            GameObject glowHolder = Track(new GameObject("Glow", typeof(CosmeticEffectData)));
            GameObject otherGlowHolder = Track(new GameObject("Glow", typeof(CosmeticEffectData)));
            CosmeticEffectData glow = glowHolder.GetComponent<CosmeticEffectData>();
            left.cosmeticEffects.Add(glow);
            right.cosmeticEffects.Add(otherGlowHolder.GetComponent<CosmeticEffectData>());

            Assert.IsTrue(left.Equals(right));

            UnityEngine.Object.DestroyImmediate(glow); // UNH-SUPPRESS: the state under test

            /*
                object.Equals(object, object) null-checks the managed reference, which a destroyed
                component passes, so the comparison reaches CosmeticEffectData and its GetComponents.
            */
            Assert.IsFalse(left.Equals(right));
            Assert.IsFalse(right.Equals(left), "Inequality must hold in both directions");
        }

        [Test]
        public void EqualsComparesPeriodicEffectContentRatherThanIdentity()
        {
            AttributeEffect left = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            AttributeEffect right = Track(ScriptableObject.CreateInstance<AttributeEffect>());
            ConfigureBaseline(left);
            ConfigureBaseline(right);

            left.periodicEffects.Add(CreatePeriodicEffect(2f));
            right.periodicEffects.Add(CreatePeriodicEffect(2f));

            /*
                Deserialization hands back fresh instances, which is the whole reason this type
                compares by value at all.
            */
            Assert.IsTrue(left.Equals(right));
            Assert.AreEqual(left.GetHashCode(), right.GetHashCode());

            right.periodicEffects[0] = CreatePeriodicEffect(3f);

            Assert.IsFalse(left.Equals(right));
        }

        private static void ConfigureBaseline(AttributeEffect effect)
        {
            effect.name = "Stack";
            effect.durationType = ModifierDurationType.Duration;
            effect.duration = 2f;
            effect.resetDurationOnReapplication = false;
            effect.stackGroup = EffectStackGroup.Reference;
            effect.stackGroupKey = null;
            effect.stackingMode = EffectStackingMode.Refresh;
            effect.maximumStacks = 0;
        }

        private static PeriodicEffectDefinition CreatePeriodicEffect(float interval)
        {
            return new PeriodicEffectDefinition
            {
                name = "Tick",
                initialDelay = 0.5f,
                interval = interval,
                maxTicks = 4,
            };
        }

        private static IEnumerable<TestCaseData> AuthoredFieldMutations()
        {
            yield return new TestCaseData(
                (Action<AttributeEffect>)(effect => effect.name = "Renamed")
            ).SetName("Field.Name");

            yield return new TestCaseData(
                (Action<AttributeEffect>)(
                    effect => effect.durationType = ModifierDurationType.Infinite
                )
            ).SetName("Field.DurationType");

            yield return new TestCaseData(
                (Action<AttributeEffect>)(effect => effect.duration = 99f)
            ).SetName("Field.Duration");

            yield return new TestCaseData(
                (Action<AttributeEffect>)(effect => effect.resetDurationOnReapplication = true)
            ).SetName("Field.ResetDurationOnReapplication");

            yield return new TestCaseData(
                (Action<AttributeEffect>)(
                    effect =>
                        effect.modifications.Add(
                            new AttributeModification
                            {
                                attribute = "speed",
                                action = ModificationAction.Addition,
                                value = 1f,
                            }
                        )
                )
            ).SetName("Field.Modifications");

            yield return new TestCaseData(
                (Action<AttributeEffect>)(
                    effect => effect.periodicEffects.Add(CreatePeriodicEffect(2f))
                )
            ).SetName("Field.PeriodicEffects");

            yield return new TestCaseData(
                (Action<AttributeEffect>)(effect => effect.effectTags.Add("Burning"))
            ).SetName("Field.EffectTags");

            yield return new TestCaseData(
                (Action<AttributeEffect>)(effect => effect.stackGroup = EffectStackGroup.CustomKey)
            ).SetName("Field.StackGroup");

            yield return new TestCaseData(
                (Action<AttributeEffect>)(effect => effect.stackGroupKey = "DamageOverTime")
            ).SetName("Field.StackGroupKey");

            yield return new TestCaseData(
                (Action<AttributeEffect>)(effect => effect.stackingMode = EffectStackingMode.Stack)
            ).SetName("Field.StackingMode");

            yield return new TestCaseData(
                (Action<AttributeEffect>)(effect => effect.maximumStacks = 3)
            ).SetName("Field.MaximumStacks");
        }
    }
}
