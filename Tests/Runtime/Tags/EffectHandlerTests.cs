// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Tags.Helpers;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EffectHandlerTests : TagsTestBase
    {
        private const float RemainingDurationEpsilon = 1e-3f;

        [SetUp]
        public void SetUp()
        {
            ResetEffectHandleId();
            RecordingCosmeticComponent.ResetCounters();
            RecordingEffectBehavior.ResetForTests();
        }

        [UnityTest]
        public IEnumerator ApplyEffectWithDurationInvokesEventsAndAppliesChanges()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Buff",
                e =>
                {
                    e.effectTags.Add("Buff");
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = 5f,
                        }
                    );
                }
            );

            int appliedCount = 0;
            handler.OnEffectApplied += _ => ++appliedCount;

            EffectHandle handle = entity.ApplyEffect(effect).Value;
            Assert.AreEqual(1, appliedCount);
            Assert.AreEqual(105f, attributes.health.CurrentValue);
            Assert.IsTrue(tags.HasTag("Buff"));

            handler.RemoveEffect(handle);
            Assert.IsFalse(tags.HasTag("Buff"));
            Assert.AreEqual(100f, attributes.health.CurrentValue);
        }

        [UnityTest]
        public IEnumerator ApplyEffectWithInstantDurationAppliesImmediately()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Instant",
                e =>
                {
                    e.durationType = ModifierDurationType.Instant;
                    e.effectTags.Add("Flash");
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = 10f,
                        }
                    );
                }
            );

            EffectHandle? handle = entity.ApplyEffect(effect);
            Assert.IsFalse(handle.HasValue);
            Assert.AreEqual(110f, attributes.health.CurrentValue);
            Assert.IsTrue(tags.HasTag("Flash"));
        }

        [UnityTest]
        public IEnumerator RemoveAllEffectsClearsAppliedHandles()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            AttributeEffect effectA = CreateEffect(
                "BuffA",
                e =>
                {
                    e.effectTags.Add("BuffA");
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = 5f,
                        }
                    );
                }
            );
            AttributeEffect effectB = CreateEffect(
                "BuffB",
                e =>
                {
                    e.effectTags.Add("BuffB");
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.armor),
                            action = ModificationAction.Addition,
                            value = 10f,
                        }
                    );
                }
            );

            _ = entity.ApplyEffect(effectA);
            _ = entity.ApplyEffect(effectB);

            handler.RemoveAllEffects();
            Assert.AreEqual(100f, attributes.health.CurrentValue);
            Assert.AreEqual(50f, attributes.armor.CurrentValue);
            Assert.IsFalse(tags.HasTag("BuffA"));
            Assert.IsFalse(tags.HasTag("BuffB"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator DurationEffectExpiresAutomatically()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            AttributeEffect effect = CreateEffect(
                "Temporary",
                e =>
                {
                    e.duration = 0f;
                    e.effectTags.Add("Temp");
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = 5f,
                        }
                    );
                }
            );

            int removedCount = 0;
            handler.OnEffectRemoved += _ => ++removedCount;

            _ = entity.ApplyEffect(effect);
            Assert.IsTrue(tags.HasTag("Temp"));

            yield return null;
            yield return null;

            Assert.IsFalse(tags.HasTag("Temp"));
            Assert.AreEqual(100f, attributes.health.CurrentValue);
            Assert.AreEqual(1, removedCount);
        }

        [UnityTest]
        public IEnumerator ApplyEffectAppliesNonInstancedCosmetics()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            CosmeticEffectData cosmetic = CreateCosmeticTemplate("Glow");
            AttributeEffect effect = CreateEffect(
                "Cosmetic",
                e =>
                {
                    e.cosmeticEffects.Add(cosmetic);
                }
            );

            EffectHandle handle = entity.ApplyEffect(effect).Value;
            Assert.AreEqual(1, RecordingCosmeticComponent.AppliedCount);

            handler.RemoveEffect(handle);
            Assert.AreEqual(1, RecordingCosmeticComponent.RemovedCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ApplyEffectInstantiatesAndDestroysCosmeticInstances()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            CosmeticEffectData template = CreateCosmeticTemplate("Aura", requiresInstance: true);
            AttributeEffect effect = CreateEffect(
                "Aura",
                e =>
                {
                    e.cosmeticEffects.Add(template);
                }
            );

            int initialChildCount = entity.transform.childCount;
            EffectHandle handle = entity.ApplyEffect(effect).Value;
            Assert.Greater(entity.transform.childCount, initialChildCount);
            Assert.AreEqual(1, RecordingCosmeticComponent.AppliedCount);

            handler.RemoveEffect(handle);
            yield return null;

            Assert.LessOrEqual(entity.transform.childCount, initialChildCount);
            Assert.AreEqual(1, RecordingCosmeticComponent.RemovedCount);
        }

        [UnityTest]
        public IEnumerator IsEffectActiveReflectsState()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect("Buff");
            Assert.IsFalse(handler.IsEffectActive(effect));

            EffectHandle handle = handler.ApplyEffect(effect).Value;
            Assert.IsTrue(handler.IsEffectActive(effect));

            handler.RemoveEffect(handle);
            Assert.IsFalse(handler.IsEffectActive(effect));
        }

        [UnityTest]
        public IEnumerator GetEffectStackCountSupportsMultipleHandles()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Stacking",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackingMode = EffectStackingMode.Stack;
                }
            );

            EffectHandle first = handler.ApplyEffect(effect).Value;
            EffectHandle second = handler.ApplyEffect(effect).Value;

            Assert.AreEqual(2, handler.GetEffectStackCount(effect));

            handler.RemoveEffect(first);
            Assert.AreEqual(1, handler.GetEffectStackCount(effect));

            handler.RemoveEffect(second);
            Assert.AreEqual(0, handler.GetEffectStackCount(effect));
        }

        [UnityTest]
        public IEnumerator StackingModeIgnoreReturnsExistingHandle()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Ignore",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackingMode = EffectStackingMode.Ignore;
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = 5f,
                        }
                    );
                }
            );

            EffectHandle first = handler.ApplyEffect(effect).Value;
            float afterFirst = attributes.health.CurrentValue;

            EffectHandle? second = handler.ApplyEffect(effect);
            Assert.IsTrue(second.HasValue);
            Assert.AreEqual(first, second.Value);
            Assert.AreEqual(afterFirst, attributes.health.CurrentValue);

            handler.RemoveEffect(first);
        }

        [UnityTest]
        public IEnumerator RefreshModeWithoutResetDurationPreservesTimer()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "RefreshNoReset",
                e =>
                {
                    e.duration = 0.3f;
                    e.stackingMode = EffectStackingMode.Refresh;
                    e.resetDurationOnReapplication = false;
                }
            );

            EffectHandle handle = handler.ApplyEffect(effect).Value;
            yield return new WaitForSeconds(0.05f);
            Assert.IsTrue(handler.TryGetRemainingDuration(handle, out float beforeReapply));

            EffectHandle? reapplied = handler.ApplyEffect(effect);
            Assert.IsTrue(reapplied.HasValue);
            Assert.AreEqual(handle, reapplied.Value);

            Assert.IsTrue(handler.TryGetRemainingDuration(handle, out float afterReapply));
            Assert.LessOrEqual(afterReapply, beforeReapply + 0.01f);

            handler.RemoveEffect(handle);
        }

        [UnityTest]
        public IEnumerator CustomStackGroupStackAcrossAssets()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effectA = CreateEffect(
                "GroupA",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackGroup = EffectStackGroup.CustomKey;
                    e.stackGroupKey = "shared";
                    e.stackingMode = EffectStackingMode.Stack;
                }
            );

            AttributeEffect effectB = CreateEffect(
                "GroupB",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackGroup = EffectStackGroup.CustomKey;
                    e.stackGroupKey = "shared";
                    e.stackingMode = EffectStackingMode.Stack;
                }
            );

            EffectHandle a1 = handler.ApplyEffect(effectA).Value;
            EffectHandle b1 = handler.ApplyEffect(effectB).Value;
            EffectHandle a2 = handler.ApplyEffect(effectA).Value;

            List<EffectHandle> active = handler.GetActiveEffects();
            Assert.AreEqual(3, active.Count);
            Assert.AreEqual(2, handler.GetEffectStackCount(effectA));
            Assert.AreEqual(1, handler.GetEffectStackCount(effectB));

            handler.RemoveAllEffects();
        }

        [UnityTest]
        public IEnumerator StackedTagsPersistUntilFinalStackRemoved()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Tagged",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackingMode = EffectStackingMode.Stack;
                    e.effectTags.Add("Shielded");
                }
            );

            EffectHandle first = handler.ApplyEffect(effect).Value;
            EffectHandle second = handler.ApplyEffect(effect).Value;

            Assert.IsTrue(tags.HasTag("Shielded"));

            handler.RemoveEffect(first);
            Assert.IsTrue(tags.HasTag("Shielded"));

            handler.RemoveEffect(second);
            Assert.IsFalse(tags.HasTag("Shielded"));
        }

        [UnityTest]
        public IEnumerator GetActiveEffectsPopulatesBuffer()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Active",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackingMode = EffectStackingMode.Stack;
                }
            );

            EffectHandle first = handler.ApplyEffect(effect).Value;
            EffectHandle second = handler.ApplyEffect(effect).Value;

            List<EffectHandle> buffer = new();
            handler.GetActiveEffects(buffer);
            CollectionAssert.AreEquivalent(new[] { first, second }, buffer);

            handler.RemoveEffect(first);
            buffer.Clear();
            handler.GetActiveEffects(buffer);
            CollectionAssert.AreEqual(new[] { second }, buffer);

            handler.RemoveEffect(second);
        }

        [UnityTest]
        public IEnumerator PeriodicEffectHonorsInitialDelayAndMaxTicks()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "PeriodicLimited",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    PeriodicEffectDefinition definition = new()
                    {
                        initialDelay = 0.05f,
                        interval = 0.05f,
                        maxTicks = 2,
                    };
                    definition.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = -10f,
                        }
                    );
                    e.periodicEffects.Add(definition);
                }
            );

            EffectHandle handle = handler.ApplyEffect(effect).Value;
            yield return null;
            Assert.AreEqual(100f, attributes.health.CurrentValue, 0.01f);

            yield return new WaitForSeconds(0.03f);
            Assert.Zero(
                attributes.notifications.Count,
                "No periodic ticks should occur before the initial delay elapses."
            );

            yield return WaitForAttributeNotifications(
                attributes,
                expectedCount: 1,
                timeout: 0.5f,
                checkpoint: "first periodic tick"
            );
            Assert.AreEqual(90f, attributes.health.CurrentValue, 0.01f);

            yield return WaitForAttributeNotifications(
                attributes,
                expectedCount: 2,
                timeout: 0.5f,
                checkpoint: "second periodic tick"
            );
            Assert.AreEqual(80f, attributes.health.CurrentValue, 0.01f);

            yield return AssertNoAdditionalAttributeNotifications(
                attributes,
                expectedCount: 2,
                holdDuration: 0.2f,
                checkpoint: "max tick enforcement"
            );
            Assert.AreEqual(80f, attributes.health.CurrentValue, 0.01f);

            handler.RemoveEffect(handle);
        }

        private static IEnumerator WaitForAttributeNotifications(
            TestAttributesComponent attributes,
            int expectedCount,
            float timeout,
            string checkpoint
        )
        {
            float elapsed = 0f;
            while (attributes.notifications.Count < expectedCount && elapsed < timeout)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            Assert.That(
                attributes.notifications.Count,
                Is.GreaterThanOrEqualTo(expectedCount),
                $"{checkpoint} not reached within {timeout:F2}s (saw {attributes.notifications.Count})."
            );
        }

        private static IEnumerator AssertNoAdditionalAttributeNotifications(
            TestAttributesComponent attributes,
            int expectedCount,
            float holdDuration,
            string checkpoint
        )
        {
            float elapsed = 0f;
            while (elapsed < holdDuration)
            {
                Assert.AreEqual(
                    expectedCount,
                    attributes.notifications.Count,
                    $"{checkpoint}: detected unexpected periodic ticks after reaching the expected count."
                );
                yield return null;
                elapsed += Time.deltaTime;
            }
        }

        [UnityTest]
        public IEnumerator PeriodicEffectUnlimitedTicksStopOnRemoval()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "PeriodicUnlimited",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    PeriodicEffectDefinition definition = new() { interval = 0.05f, maxTicks = 0 };
                    definition.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = -5f,
                        }
                    );
                    e.periodicEffects.Add(definition);
                }
            );

            EffectHandle handle = handler.ApplyEffect(effect).Value;
            yield return new WaitForSeconds(0.16f);
            float afterTicks = attributes.health.CurrentValue;
            Assert.Less(afterTicks, 100f);

            handler.RemoveEffect(handle);
            float afterRemoval = attributes.health.CurrentValue;
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(afterRemoval, attributes.health.CurrentValue, 0.01f);
        }

        [UnityTest]
        public IEnumerator PeriodicEffectCatchUpIsBoundedPerUpdate()
        {
            (_, EffectHandler handler, TestAttributesComponent attributes, _) = CreateEntity();

            AttributeEffect effect = CreateEffect(
                "PeriodicCatchUpCap",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    PeriodicEffectDefinition definition = new() { interval = 0.01f, maxTicks = 0 };
                    definition.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = -1f,
                        }
                    );
                    e.periodicEffects.Add(definition);
                }
            );

            EffectHandle handle = handler.ApplyEffect(effect).Value;
            float overdueTime = Time.time + 10f;

            int firstCatchUpTicks = handler.ProcessPeriodicEffectsForTesting(
                overdueTime,
                deltaTime: 10f
            );
            Assert.AreEqual(32, firstCatchUpTicks);
            Assert.AreEqual(32, attributes.notifications.Count);
            Assert.AreEqual(68f, attributes.health.CurrentValue, 0.01f);

            int secondCatchUpTicks = handler.ProcessPeriodicEffectsForTesting(
                overdueTime,
                deltaTime: 0f
            );
            Assert.AreEqual(32, secondCatchUpTicks);
            Assert.AreEqual(64, attributes.notifications.Count);
            Assert.AreEqual(36f, attributes.health.CurrentValue, 0.01f);

            handler.RemoveEffect(handle);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MultiplePeriodicDefinitionsAffectAttributesIndependently()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "PeriodicMulti",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;

                    PeriodicEffectDefinition damage = new()
                    {
                        initialDelay = 0.05f,
                        interval = 0.05f,
                        maxTicks = 2,
                    };
                    damage.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = -5f,
                        }
                    );

                    PeriodicEffectDefinition armorGain = new()
                    {
                        initialDelay = 0.02f,
                        interval = 0.1f,
                        maxTicks = 3,
                    };
                    armorGain.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.armor),
                            action = ModificationAction.Addition,
                            value = 1f,
                        }
                    );

                    e.periodicEffects.Add(damage);
                    e.periodicEffects.Add(armorGain);
                }
            );

            EffectHandle handle = handler.ApplyEffect(effect).Value;

            yield return WaitForAttributeNotifications(
                attributes,
                expectedCount: 5,
                timeout: 0.75f,
                checkpoint: "all independent periodic ticks"
            );

            int healthTicks = 0;
            int armorTicks = 0;
            foreach ((string attribute, _, _) in attributes.notifications)
            {
                if (attribute == nameof(TestAttributesComponent.health))
                {
                    healthTicks++;
                }
                else if (attribute == nameof(TestAttributesComponent.armor))
                {
                    armorTicks++;
                }
            }

            Assert.AreEqual(2, healthTicks);
            Assert.AreEqual(3, armorTicks);
            Assert.AreEqual(90f, attributes.health.CurrentValue, 0.01f);
            Assert.AreEqual(53f, attributes.armor.CurrentValue, 0.01f);

            handler.RemoveEffect(handle);
        }

        [UnityTest]
        public IEnumerator TryGetRemainingDurationReportsTime()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Timed",
                e =>
                {
                    e.duration = 0.5f;
                }
            );

            EffectHandle handle = handler.ApplyEffect(effect).Value;
            Assert.IsTrue(handler.TryGetRemainingDuration(handle, out float remaining));
            Assert.Greater(remaining, 0f);
            Assert.LessOrEqual(
                remaining,
                effect.duration + RemainingDurationEpsilon,
                $"Remaining duration {remaining} should not exceed declared duration {effect.duration} beyond epsilon."
            );

            handler.RemoveEffect(handle);
            Assert.IsFalse(handler.TryGetRemainingDuration(handle, out float afterRemoval));
            Assert.AreEqual(0f, afterRemoval);
        }

        [UnityTest]
        public IEnumerator EnsureHandleRefreshesDurationWhenRequested()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Refreshable",
                e =>
                {
                    e.duration = 0.2f;
                    e.resetDurationOnReapplication = true;
                }
            );

            EffectHandle handle = handler.ApplyEffect(effect).Value;
            Assert.IsTrue(handler.TryGetRemainingDuration(handle, out float initialRemaining));
            yield return null;
            Assert.IsTrue(handler.TryGetRemainingDuration(handle, out float beforeRefresh));
            Assert.Less(beforeRefresh, initialRemaining);

            EffectHandle? ensured = handler.EnsureHandle(effect);
            Assert.IsTrue(ensured.HasValue);
            Assert.AreEqual(handle, ensured.Value);
            Assert.IsTrue(handler.TryGetRemainingDuration(handle, out float afterRefresh));
            Assert.Greater(afterRefresh, beforeRefresh);

            yield return null;
            Assert.IsTrue(handler.TryGetRemainingDuration(handle, out float beforeNoRefresh));
            EffectHandle? ensuredNoRefresh = handler.EnsureHandle(effect, refreshDuration: false);
            Assert.IsTrue(ensuredNoRefresh.HasValue);
            Assert.AreEqual(handle, ensuredNoRefresh.Value);
            Assert.IsTrue(handler.TryGetRemainingDuration(handle, out float afterNoRefresh));
            Assert.LessOrEqual(afterNoRefresh, beforeNoRefresh);

            handler.RemoveEffect(handle);
        }

        [UnityTest]
        public IEnumerator RefreshEffectHonorsReapplicationPolicy()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Policy",
                e =>
                {
                    e.duration = 0.3f;
                    e.resetDurationOnReapplication = false;
                }
            );

            EffectHandle handle = handler.ApplyEffect(effect).Value;
            yield return null;
            Assert.IsTrue(handler.TryGetRemainingDuration(handle, out float beforeRefresh));

            Assert.IsFalse(handler.RefreshEffect(handle));
            Assert.IsTrue(handler.RefreshEffect(handle, ignoreReapplicationPolicy: true));
            Assert.IsTrue(handler.TryGetRemainingDuration(handle, out float afterRefresh));
            Assert.Greater(afterRefresh, beforeRefresh);

            handler.RemoveEffect(handle);
        }

        [UnityTest]
        public IEnumerator PeriodicEffectAppliesTicksAndStops()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Periodic",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    PeriodicEffectDefinition periodic = new() { interval = 0.1f, maxTicks = 3 };
                    periodic.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = -10f,
                        }
                    );
                    e.periodicEffects.Add(periodic);
                }
            );

            EffectHandle handle = handler.ApplyEffect(effect).Value;
            yield return new WaitForSeconds(0.35f);
            Assert.AreEqual(70f, attributes.health.CurrentValue, 0.01f);

            yield return new WaitForSeconds(0.2f);
            handler.RemoveEffect(handle);
            Assert.AreEqual(70f, attributes.health.CurrentValue, 0.01f);
        }

        [UnityTest]
        public IEnumerator EffectBehaviorReceivesCallbacks()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Behavior",
                e =>
                {
                    e.duration = 0.25f;
                    e.periodicEffects.Add(
                        new PeriodicEffectDefinition { interval = 0.05f, maxTicks = 2 }
                    );
                }
            );

            RecordingEffectBehavior behavior = Track(
                ScriptableObject.CreateInstance<RecordingEffectBehavior>()
            );
            effect.behaviors.Add(behavior);

            EffectHandle handle = handler.ApplyEffect(effect).Value;
            Assert.AreEqual(1, RecordingEffectBehavior.ApplyCount);

            yield return null;
            Assert.Greater(RecordingEffectBehavior.TickCount, 0);

            yield return new WaitForSeconds(0.12f);
            Assert.GreaterOrEqual(RecordingEffectBehavior.PeriodicTickCount, 1);

            handler.RemoveEffect(handle);
            Assert.AreEqual(1, RecordingEffectBehavior.RemoveCount);
        }

        [UnityTest]
        public IEnumerator EffectBehaviorClonesPerHandle()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "BehaviorStacks",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackingMode = EffectStackingMode.Stack;
                }
            );

            RecordingEffectBehavior behavior = Track(
                ScriptableObject.CreateInstance<RecordingEffectBehavior>()
            );
            effect.behaviors.Add(behavior);

            int startingInstances = RecordingEffectBehavior.InstanceCount;

            EffectHandle first = handler.ApplyEffect(effect).Value;
            Assert.AreEqual(startingInstances + 1, RecordingEffectBehavior.InstanceCount);

            EffectHandle second = handler.ApplyEffect(effect).Value;
            Assert.AreEqual(startingInstances + 2, RecordingEffectBehavior.InstanceCount);
            Assert.AreEqual(2, RecordingEffectBehavior.ApplyCount);

            handler.RemoveEffect(first);
            handler.RemoveEffect(second);
            Assert.AreEqual(2, RecordingEffectBehavior.RemoveCount);
        }

        [UnityTest]
        public IEnumerator EffectBehaviorWithoutPeriodicSkipsPeriodicCallbacks()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "BehaviorNoPeriodic",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                }
            );

            RecordingEffectBehavior behavior = Track(
                ScriptableObject.CreateInstance<RecordingEffectBehavior>()
            );
            effect.behaviors.Add(behavior);

            EffectHandle handle = handler.ApplyEffect(effect).Value;
            yield return new WaitForSeconds(0.1f);

            Assert.AreEqual(0, RecordingEffectBehavior.PeriodicTickCount);

            handler.RemoveEffect(handle);
        }

        [UnityTest]
        public IEnumerator StackingModeStackRespectsMaximumStacks()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Stacking",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackingMode = EffectStackingMode.Stack;
                    e.maximumStacks = 2;
                }
            );

            EffectHandle first = handler.ApplyEffect(effect).Value;
            EffectHandle second = handler.ApplyEffect(effect).Value;
            EffectHandle third = handler.ApplyEffect(effect).Value;

            List<EffectHandle> active = handler.GetActiveEffects();
            Assert.AreEqual(2, active.Count);
            CollectionAssert.DoesNotContain(active, first);
            CollectionAssert.Contains(active, second);
            CollectionAssert.Contains(active, third);

            handler.RemoveAllEffects();
        }

        [UnityTest]
        public IEnumerator InstantEffectWithPeriodicLogsWarning()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "InstantPeriodic",
                e =>
                {
                    e.durationType = ModifierDurationType.Instant;
                    PeriodicEffectDefinition definition = new() { interval = 0.05f, maxTicks = 1 };
                    definition.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = -10f,
                        }
                    );
                    e.periodicEffects.Add(definition);
                }
            );

            LogAssert.Expect(
                LogType.Warning,
                new Regex("defines periodic or behaviour data but is Instant")
            );

            EffectHandle? handle = handler.ApplyEffect(effect);
            Assert.IsFalse(handle.HasValue);

            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(100f, attributes.health.CurrentValue, 0.01f);
        }

        [UnityTest]
        public IEnumerator StackingModeReplaceSwapsHandles()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Replace",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackingMode = EffectStackingMode.Replace;
                }
            );

            EffectHandle first = handler.ApplyEffect(effect).Value;
            EffectHandle second = handler.ApplyEffect(effect).Value;

            List<EffectHandle> active = handler.GetActiveEffects();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual(second, active[0]);
            Assert.AreNotEqual(first, second);

            handler.RemoveAllEffects();
        }

        [UnityTest]
        public IEnumerator CustomStackGroupSharesAcrossEffects()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effectA = CreateEffect(
                "GroupA",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackGroup = EffectStackGroup.CustomKey;
                    e.stackGroupKey = "shared";
                    e.stackingMode = EffectStackingMode.Replace;
                }
            );
            AttributeEffect effectB = CreateEffect(
                "GroupB",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackGroup = EffectStackGroup.CustomKey;
                    e.stackGroupKey = "shared";
                    e.stackingMode = EffectStackingMode.Replace;
                }
            );

            EffectHandle first = handler.ApplyEffect(effectA).Value;
            EffectHandle second = handler.ApplyEffect(effectB).Value;

            List<EffectHandle> active = handler.GetActiveEffects();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual(second, active[0]);
            Assert.IsFalse(handler.IsEffectActive(effectA));
            Assert.IsTrue(handler.IsEffectActive(effectB));
            Assert.AreNotEqual(first, second);

            handler.RemoveAllEffects();
        }

        private (
            GameObject entity,
            EffectHandler handler,
            TestAttributesComponent attributes,
            TagHandler tags
        ) CreateEntity()
        {
            GameObject entity = CreateTrackedGameObject("Entity", typeof(TestAttributesComponent));
            return (
                entity,
                entity.GetComponent<EffectHandler>(),
                entity.GetComponent<TestAttributesComponent>(),
                entity.GetComponent<TagHandler>()
            );
        }

        private CosmeticEffectData CreateCosmeticTemplate(
            string name,
            bool requiresInstance = false
        )
        {
            GameObject template = CreateTrackedGameObject(name, typeof(CosmeticEffectData));
            RecordingCosmeticComponent component =
                template.AddComponent<RecordingCosmeticComponent>();
            component.requireInstance = requiresInstance;
            component.cleansSelf = false;
            return template.GetComponent<CosmeticEffectData>();
        }
    }
}
