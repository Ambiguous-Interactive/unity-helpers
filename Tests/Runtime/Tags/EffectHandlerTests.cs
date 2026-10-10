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

            Assert.IsTrue(entity.ApplyEffect(effectA).HasValue);
            Assert.IsTrue(entity.ApplyEffect(effectB).HasValue);

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

            Assert.IsTrue(entity.ApplyEffect(effect).HasValue);
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

            EffectHandle handle = handler.ApplyEffect(effect, currentTime: 10f).Value;
            Assert.IsTrue(
                handler.TryGetRemainingDuration(
                    handle,
                    currentTime: 10.05f,
                    remainingDuration: out float beforeReapply
                )
            );
            Assert.AreEqual(0.25f, beforeReapply, RemainingDurationEpsilon);

            EffectHandle? reapplied = handler.ApplyEffect(effect, currentTime: 10.2f);
            Assert.IsTrue(reapplied.HasValue);
            Assert.AreEqual(handle, reapplied.Value);

            Assert.IsTrue(
                handler.TryGetRemainingDuration(
                    handle,
                    currentTime: 10.2f,
                    remainingDuration: out float afterReapply
                )
            );
            Assert.AreEqual(0.1f, afterReapply, RemainingDurationEpsilon);
            Assert.Less(afterReapply, beforeReapply);

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

        [TestCase(100f, 0f, 1)]
        [TestCase(100f, 1f, 0)]
        [TestCase(16777216f, 0f, 1)]
        [TestCase(16777216f, 1f, 0)]
        [TestCase(float.MaxValue, 0f, 1)]
        [TestCase(float.MaxValue, 1f, 0)]
        public void PeriodicCadenceDoesNotRepeatAtUnchangedClock(
            float startTime,
            float initialDelay,
            int expectedTicks
        )
        {
            (_, EffectHandler handler, TestAttributesComponent attributes, _) = CreateEntity();
            AttributeEffect effect = CreateEffect(
                nameof(PeriodicCadenceDoesNotRepeatAtUnchangedClock),
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    PeriodicEffectDefinition definition = new()
                    {
                        initialDelay = initialDelay,
                        interval = 1f,
                    };
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
            EffectHandle handle = handler.ApplyEffect(effect, startTime).Value;
            Assert.AreEqual(expectedTicks, handler.ProcessPeriodicEffects(startTime, 0f));
            Assert.AreEqual(0, handler.ProcessPeriodicEffects(startTime, 0f));
            Assert.AreEqual(
                100f - expectedTicks,
                attributes.health.CurrentValue,
                RemainingDurationEpsilon
            );
            handler.RemoveEffect(handle);
        }

        [Test]
        public void PeriodicCancellationOriginCatchUpRetainsThePerPassCap()
        {
            (_, EffectHandler handler, _, _) = CreateEntity();
            AttributeEffect effect = CreateEffect(
                nameof(PeriodicCancellationOriginCatchUpRetainsThePerPassCap),
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.periodicEffects.Add(
                        new PeriodicEffectDefinition
                        {
                            initialDelay = float.MaxValue,
                            interval = 0.25f,
                        }
                    );
                }
            );
            EffectHandle handle = handler.ApplyEffect(effect, -float.MaxValue).Value;
            Assert.AreEqual(32, handler.ProcessPeriodicEffects(10f, 0f));
            Assert.AreEqual(9, handler.ProcessPeriodicEffects(10f, 0f));
            Assert.AreEqual(0, handler.ProcessPeriodicEffects(10f, 0f));
            handler.RemoveEffect(handle);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PeriodicPhaseSurvivesDurationRefreshAndReapplication(bool reapply)
        {
            (_, EffectHandler handler, _, _) = CreateEntity();
            AttributeEffect effect = CreateEffect(
                nameof(PeriodicPhaseSurvivesDurationRefreshAndReapplication),
                e =>
                {
                    e.durationType = ModifierDurationType.Duration;
                    e.duration = 10f;
                    e.stackingMode = EffectStackingMode.Refresh;
                    e.resetDurationOnReapplication = true;
                    e.periodicEffects.Add(new PeriodicEffectDefinition { interval = 1f });
                }
            );
            EffectHandle handle = handler.ApplyEffect(effect, 100f).Value;
            Assert.AreEqual(1, handler.ProcessPeriodicEffects(100f, 0f));
            if (reapply)
            {
                Assert.AreEqual(handle, handler.ApplyEffect(effect, 100.5f).Value);
            }
            else
            {
                Assert.IsTrue(handler.RefreshEffect(handle, false, 100.5f));
            }
            Assert.AreEqual(0, handler.ProcessPeriodicEffects(100.5f, 0f));
            Assert.AreEqual(1, handler.ProcessPeriodicEffects(101f, 0f));
            handler.RemoveEffect(handle);
            EffectHandle fresh = handler.ApplyEffect(effect, 101f).Value;
            Assert.AreNotEqual(handle, fresh);
            Assert.AreEqual(1, handler.ProcessPeriodicEffects(101f, 0f));
            Assert.AreEqual(0, handler.ProcessPeriodicEffects(101f, 0f));
            handler.RemoveEffect(fresh);
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

            EffectHandle handle = handler.ApplyEffect(effect, currentTime: 5f).Value;
            Assert.AreEqual(100f, attributes.health.CurrentValue, 0.01f);

            int beforeDelayTicks = handler.ProcessPeriodicEffects(
                currentTime: 5.049f,
                deltaTime: 0.049f
            );
            Assert.AreEqual(0, beforeDelayTicks);
            Assert.Zero(
                attributes.notifications.Count,
                "No periodic ticks should occur before the initial delay elapses."
            );

            int firstTicks = handler.ProcessPeriodicEffects(currentTime: 5.051f, deltaTime: 0.05f);
            Assert.AreEqual(1, firstTicks);
            Assert.AreEqual(90f, attributes.health.CurrentValue, 0.01f);

            int secondTicks = handler.ProcessPeriodicEffects(currentTime: 5.101f, deltaTime: 0.05f);
            Assert.AreEqual(1, secondTicks);
            Assert.AreEqual(80f, attributes.health.CurrentValue, 0.01f);

            int afterMaxTicks = handler.ProcessPeriodicEffects(
                currentTime: 5.201f,
                deltaTime: 0.1f
            );
            Assert.AreEqual(0, afterMaxTicks);
            Assert.AreEqual(2, attributes.notifications.Count);
            Assert.AreEqual(80f, attributes.health.CurrentValue, 0.01f);

            handler.RemoveEffect(handle);
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

            EffectHandle handle = handler.ApplyEffect(effect, currentTime: 20f).Value;
            int ticks = handler.ProcessPeriodicEffects(currentTime: 20.16f, deltaTime: 0.16f);
            Assert.Greater(ticks, 0);
            float afterTicks = attributes.health.CurrentValue;
            Assert.Less(afterTicks, 100f);

            handler.RemoveEffect(handle);
            float afterRemoval = attributes.health.CurrentValue;
            int ticksAfterRemoval = handler.ProcessPeriodicEffects(
                currentTime: 20.26f,
                deltaTime: 0.1f
            );
            Assert.AreEqual(0, ticksAfterRemoval);
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

            EffectHandle handle = handler.ApplyEffect(effect, currentTime: 30f).Value;
            float overdueTime = 40f;

            int firstCatchUpTicks = handler.ProcessPeriodicEffects(overdueTime, deltaTime: 10f);
            Assert.AreEqual(32, firstCatchUpTicks);
            Assert.AreEqual(32, attributes.notifications.Count);
            Assert.AreEqual(68f, attributes.health.CurrentValue, 0.01f);

            int secondCatchUpTicks = handler.ProcessPeriodicEffects(overdueTime, deltaTime: 0f);
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

            EffectHandle handle = handler.ApplyEffect(effect, currentTime: 50f).Value;

            int firstTicks = handler.ProcessPeriodicEffects(currentTime: 50.021f, deltaTime: 0.02f);
            Assert.AreEqual(1, firstTicks);

            int secondTicks = handler.ProcessPeriodicEffects(
                currentTime: 50.051f,
                deltaTime: 0.03f
            );
            Assert.AreEqual(1, secondTicks);

            int finalTicks = handler.ProcessPeriodicEffects(currentTime: 50.221f, deltaTime: 0.17f);
            Assert.AreEqual(3, finalTicks);
            Assert.AreEqual(5, attributes.notifications.Count);

            int healthTicks = 0;
            int armorTicks = 0;
            foreach ((string attribute, _, _) in attributes.notifications)
            {
                if (
                    string.Equals(
                        attribute,
                        nameof(TestAttributesComponent.health),
                        System.StringComparison.Ordinal
                    )
                )
                {
                    ++healthTicks;
                }
                else if (
                    string.Equals(
                        attribute,
                        nameof(TestAttributesComponent.armor),
                        System.StringComparison.Ordinal
                    )
                )
                {
                    ++armorTicks;
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

            EffectHandle handle = handler.ApplyEffect(effect, currentTime: 70f).Value;
            Assert.IsTrue(
                handler.TryGetRemainingDuration(
                    handle,
                    currentTime: 70f,
                    remainingDuration: out float remaining
                )
            );
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

            EffectHandle handle = handler.ApplyEffect(effect, currentTime: 100f).Value;
            Assert.IsTrue(
                handler.TryGetRemainingDuration(
                    handle,
                    currentTime: 100f,
                    remainingDuration: out float initialRemaining
                )
            );
            Assert.IsTrue(
                handler.TryGetRemainingDuration(
                    handle,
                    currentTime: 100.05f,
                    remainingDuration: out float beforeRefresh
                )
            );
            Assert.Less(beforeRefresh, initialRemaining);

            EffectHandle? ensured = handler.EnsureHandle(
                effect,
                refreshDuration: true,
                currentTime: 100.1f
            );
            Assert.IsTrue(ensured.HasValue);
            Assert.AreEqual(handle, ensured.Value);
            Assert.IsTrue(
                handler.TryGetRemainingDuration(
                    handle,
                    currentTime: 100.1f,
                    remainingDuration: out float afterRefresh
                )
            );
            Assert.Greater(afterRefresh, beforeRefresh);

            Assert.IsTrue(
                handler.TryGetRemainingDuration(
                    handle,
                    currentTime: 100.15f,
                    remainingDuration: out float beforeNoRefresh
                )
            );
            EffectHandle? ensuredNoRefresh = handler.EnsureHandle(
                effect,
                refreshDuration: false,
                currentTime: 100.18f
            );
            Assert.IsTrue(ensuredNoRefresh.HasValue);
            Assert.AreEqual(handle, ensuredNoRefresh.Value);
            Assert.IsTrue(
                handler.TryGetRemainingDuration(
                    handle,
                    currentTime: 100.18f,
                    remainingDuration: out float afterNoRefresh
                )
            );
            Assert.Less(afterNoRefresh, beforeNoRefresh);

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

            EffectHandle handle = handler.ApplyEffect(effect, currentTime: 200f).Value;
            Assert.IsTrue(
                handler.TryGetRemainingDuration(
                    handle,
                    currentTime: 200.1f,
                    remainingDuration: out float beforeRefresh
                )
            );

            Assert.IsFalse(handler.RefreshEffect(handle));
            Assert.IsTrue(
                handler.RefreshEffect(handle, ignoreReapplicationPolicy: true, currentTime: 200.15f)
            );
            Assert.IsTrue(
                handler.TryGetRemainingDuration(
                    handle,
                    currentTime: 200.15f,
                    remainingDuration: out float afterRefresh
                )
            );
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

            EffectHandle handle = handler.ApplyEffect(effect, currentTime: 300f).Value;
            int ticks = handler.ProcessPeriodicEffects(currentTime: 300.35f, deltaTime: 0.35f);
            Assert.AreEqual(3, ticks);
            Assert.AreEqual(70f, attributes.health.CurrentValue, 0.01f);

            int afterMaxTicks = handler.ProcessPeriodicEffects(
                currentTime: 300.55f,
                deltaTime: 0.2f
            );
            Assert.AreEqual(0, afterMaxTicks);
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

            EffectHandle handle = handler.ApplyEffect(effect, currentTime: 400f).Value;
            Assert.AreEqual(1, RecordingEffectBehavior.ApplyCount);

            int tickCount = handler.ProcessBehaviorTicks(deltaTime: 0.033f);
            Assert.AreEqual(1, tickCount);
            Assert.Greater(RecordingEffectBehavior.TickCount, 0);
            Assert.AreEqual(0.033f, RecordingEffectBehavior.TickContexts[0].deltaTime);

            int periodicTicks = handler.ProcessPeriodicEffects(
                currentTime: 400.12f,
                deltaTime: 0.12f
            );
            Assert.AreEqual(2, periodicTicks);
            Assert.GreaterOrEqual(RecordingEffectBehavior.PeriodicTickCount, 1);
            Assert.AreEqual(
                0.12f,
                RecordingEffectBehavior.PeriodicInvocations[0].Context.deltaTime
            );

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

            EffectHandle handle = handler.ApplyEffect(effect, currentTime: 500f).Value;
            int tickCount = handler.ProcessBehaviorTicks(deltaTime: 0.1f);
            Assert.AreEqual(1, tickCount);

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

            // Emitted via the package logger, which is compiled out in a non-development player.
            ExpectWallstopLog(
                LogType.Warning,
                new Regex("defines periodic or behaviour data but is Instant")
            );

            /*
                Count every application: LogAssert alone accepts one of many warnings, hiding repeated expensive
                rendering.
            */
            int warnings = 0;
            void CountWarning(string condition, string stackTrace, LogType type)
            {
                if (
                    type == LogType.Warning
                    && condition.Contains("defines periodic or behaviour data but is Instant")
                )
                {
                    ++warnings;
                }
            }

            Application.logMessageReceived += CountWarning;
            EffectHandle? handle = null;
            try
            {
                for (int application = 0; application < 4; ++application)
                {
                    handle = handler.ApplyEffect(effect);
                }
            }
            finally
            {
                Application.logMessageReceived -= CountWarning;
            }

            Assert.IsFalse(handle.HasValue);
            if (WallstopLoggingCompiledIn)
            {
                Assert.AreEqual(
                    1,
                    warnings,
                    "The Instant warning must be emitted once per effect, not once per application."
                );
            }

            int ticks = handler.ProcessPeriodicEffects(currentTime: 600f, deltaTime: 0.1f);
            Assert.AreEqual(0, ticks);
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

        [TestCase(100f, 0.5f, TestName = "EffectDuration.OrdinaryClock")]
        [TestCase(16777216f, 1f, TestName = "EffectDuration.LargeClock")]
        [TestCase(33554432f, 2f, TestName = "EffectDuration.CollapsedDeadline")]
        [TestCase(33554432f, 3f, TestName = "EffectDuration.RoundedDeadline")]
        [TestCase(float.MaxValue, 1f, TestName = "EffectDuration.ExtremeClock")]
        [TestCase(float.MaxValue, float.MaxValue, TestName = "EffectDuration.FiniteOverflow")]
        [TestCase(100f, float.PositiveInfinity, TestName = "EffectDuration.InfinityPolicy")]
        [TestCase(100f, float.NaN, TestName = "EffectDuration.NaNPolicy")]
        public void DurationSurvivesApplyRefreshAndEnsureAtSameClock(
            float currentTime,
            float duration
        )
        {
            EffectHandler handler = CreateEntity().handler;
            AttributeEffect effect = CreateEffect(
                nameof(DurationSurvivesApplyRefreshAndEnsureAtSameClock),
                e =>
                {
                    e.durationType = ModifierDurationType.Duration;
                    e.duration = duration;
                    e.resetDurationOnReapplication = true;
                }
            );
            EffectHandle handle = handler.ApplyEffect(effect, currentTime).Value;
            Assert.IsTrue(
                handler.TryGetRemainingDuration(handle, currentTime, out float remaining)
            );
            Assert.That(remaining, Is.EqualTo(duration));
            Assert.IsTrue(handler.RefreshEffect(handle, false, currentTime));
            Assert.IsTrue(handler.TryGetRemainingDuration(handle, currentTime, out remaining));
            Assert.That(remaining, Is.EqualTo(duration));
            EffectHandle ensured = handler.EnsureHandle(effect, true, currentTime).Value;
            Assert.AreEqual(handle, ensured);
            Assert.IsTrue(handler.TryGetRemainingDuration(ensured, currentTime, out remaining));
            Assert.That(remaining, Is.EqualTo(duration));
        }

        [TestCase(false, TestName = "EffectDuration.Reapply.KeepsStart")]
        [TestCase(true, TestName = "EffectDuration.Reapply.ResetsStart")]
        public void ReapplicationRetainsDurationPolicyAtLargeClock(bool reset)
        {
            EffectHandler handler = CreateEntity().handler;
            AttributeEffect effect = CreateEffect(
                nameof(ReapplicationRetainsDurationPolicyAtLargeClock),
                e =>
                {
                    e.durationType = ModifierDurationType.Duration;
                    e.duration = 3f;
                    e.resetDurationOnReapplication = reset;
                }
            );
            EffectHandle handle = handler.ApplyEffect(effect, 33554432f).Value;
            EffectHandle reapplied = handler.ApplyEffect(effect, 33554436f).Value;
            Assert.AreEqual(handle, reapplied);
            Assert.IsTrue(handler.TryGetRemainingDuration(handle, 33554436f, out float remaining));
            Assert.That(remaining, Is.EqualTo(reset ? 3f : 0f));
        }

        [TestCase(float.Epsilon, 1f, -0.000000059604644775390625f, 1.00000011920928955078125f)]
        [TestCase(
            -7.888609052210118e-31f,
            1f,
            0.0000000298023223876953125f,
            0.999999940395355224609375f
        )]
        [TestCase(7.888609052210118e-31f, 1f, 0.0000000298023223876953125f, 1f)]
        [TestCase(1.0141204801825835e31f, float.MaxValue, float.Epsilon, float.MaxValue)]
        [TestCase(1.0141204801825835e31f, float.MaxValue, -float.Epsilon, float.PositiveInfinity)]
        [TestCase(1.0141204801825835e31f, float.MaxValue, 0f, float.PositiveInfinity)]
        public void RemainingDurationRoundsExactFloatMidpointsAndOverflow(
            float startTime,
            float duration,
            float currentTime,
            float expectedRemaining
        )
        {
            (_, EffectHandler handler, _, _) = CreateEntity();
            AttributeEffect effect = CreateEffect(
                nameof(RemainingDurationRoundsExactFloatMidpointsAndOverflow),
                e =>
                {
                    e.durationType = ModifierDurationType.Duration;
                    e.duration = duration;
                }
            );
            EffectHandle handle = handler.ApplyEffect(effect, startTime).Value;
            Assert.IsTrue(
                handler.TryGetRemainingDuration(handle, currentTime, out float remaining)
            );
            Assert.AreEqual(expectedRemaining, remaining);
        }

        [TestCase(1f, 1f, false)]
        [TestCase(0f, 0f, true)]
        [TestCase(-1f, 0f, true)]
        public void LargeDurationRetainsSmallRemainingTimeAndExpiryResidual(
            float startTime,
            float expectedRemaining,
            bool expired
        )
        {
            (_, EffectHandler handler, _, _) = CreateEntity();
            AttributeEffect effect = CreateEffect(
                nameof(LargeDurationRetainsSmallRemainingTimeAndExpiryResidual),
                e =>
                {
                    e.durationType = ModifierDurationType.Duration;
                    e.duration = float.MaxValue;
                }
            );
            EffectHandle handle = handler.ApplyEffect(effect, startTime).Value;
            Assert.IsTrue(
                handler.TryGetRemainingDuration(handle, float.MaxValue, out float remaining)
            );
            Assert.AreEqual(expectedRemaining, remaining);
            handler.ProcessEffectExpirations(float.MaxValue);
            Assert.AreEqual(
                !expired,
                handler.TryGetRemainingDuration(handle, float.MaxValue, out _)
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DurationRefreshPreservesOrReplacesSmallElapsedResidual(bool reset)
        {
            (_, EffectHandler handler, _, _) = CreateEntity();
            AttributeEffect effect = CreateEffect(
                nameof(DurationRefreshPreservesOrReplacesSmallElapsedResidual),
                e =>
                {
                    e.durationType = ModifierDurationType.Duration;
                    e.duration = float.MaxValue;
                    e.resetDurationOnReapplication = reset;
                }
            );
            EffectHandle handle = handler.ApplyEffect(effect, 1f).Value;
            Assert.AreEqual(reset, handler.RefreshEffect(handle, false, -1f));
            Assert.IsTrue(
                handler.TryGetRemainingDuration(handle, float.MaxValue, out float remaining)
            );
            Assert.AreEqual(reset ? 0f : 1f, remaining);
            handler.ProcessEffectExpirations(float.MaxValue);
            Assert.AreEqual(!reset, handler.TryGetRemainingDuration(handle, float.MaxValue, out _));
        }

        [TestCase(10f, 0.5f, 10.25f, false, TestName = "EffectExpiry.Ordinary.BeforeBoundary")]
        [TestCase(10f, 0.5f, 10.5f, true, TestName = "EffectExpiry.Ordinary.AtBoundary")]
        [TestCase(16777216f, 1f, 16777216f, false, TestName = "EffectExpiry.Large.SameClock")]
        [TestCase(16777216f, 1f, 16777218f, true, TestName = "EffectExpiry.Large.NextClock")]
        [TestCase(16777216f, 2f, 16777218f, true, TestName = "EffectExpiry.Large.AtBoundary")]
        [TestCase(
            float.MaxValue,
            float.Epsilon,
            float.MaxValue,
            false,
            TestName = "EffectExpiry.TinyDuration"
        )]
        [TestCase(
            float.MaxValue,
            float.MaxValue,
            float.MaxValue,
            false,
            TestName = "EffectExpiry.FiniteOverflow"
        )]
        [TestCase(
            -float.MaxValue,
            float.MaxValue,
            float.MaxValue,
            true,
            TestName = "EffectExpiry.OppositeExtremes"
        )]
        [TestCase(10f, 2f, 9f, false, TestName = "EffectExpiry.BackwardClock")]
        [TestCase(10f, 0f, 10f, true, TestName = "EffectExpiry.ZeroDuration")]
        [TestCase(10f, -1f, 10f, true, TestName = "EffectExpiry.NegativeDuration")]
        [TestCase(
            10f,
            float.PositiveInfinity,
            float.MaxValue,
            false,
            TestName = "EffectExpiry.InfinityDuration"
        )]
        [TestCase(10f, float.NaN, float.MaxValue, false, TestName = "EffectExpiry.NaNDuration")]
        public void ExpirationPassPreservesInclusiveDurationBoundary(
            float start,
            float duration,
            float currentTime,
            bool expires
        )
        {
            EffectHandler handler = CreateEntity().handler;
            AttributeEffect effect = CreateEffect(
                nameof(ExpirationPassPreservesInclusiveDurationBoundary),
                e =>
                {
                    e.durationType = ModifierDurationType.Duration;
                    e.duration = duration;
                }
            );
            EffectHandle handle = handler.ApplyEffect(effect, start).Value;
            int removed = 0;
            handler.OnEffectRemoved += _ => ++removed;
            Assert.IsTrue(
                handler.TryGetRemainingDuration(handle, currentTime, out float beforeExpiration)
            );
            if (expires)
            {
                Assert.That(beforeExpiration, Is.EqualTo(0f));
            }
            handler.ProcessEffectExpirations(currentTime);
            Assert.AreEqual(expires ? 1 : 0, removed);
            Assert.AreEqual(expires ? 0 : 1, handler.GetEffectStackCount(effect));
            Assert.AreEqual(
                !expires,
                handler.TryGetRemainingDuration(handle, currentTime, out float remaining)
            );
            if (expires)
            {
                Assert.That(remaining, Is.EqualTo(0f));
            }
            else if (!float.IsNaN(duration))
            {
                double expected = (double)duration - ((double)currentTime - start);
                Assert.That(remaining, Is.EqualTo((float)expected));
            }
            else
            {
                Assert.IsTrue(float.IsNaN(remaining));
            }
            handler.ProcessEffectExpirations(currentTime);
            Assert.AreEqual(expires ? 1 : 0, removed);
        }

        [Test]
        public void DurationSnapshotAndInfinitePolicySurviveAssetChanges()
        {
            EffectHandler handler = CreateEntity().handler;
            AttributeEffect effect = CreateEffect(
                nameof(DurationSnapshotAndInfinitePolicySurviveAssetChanges),
                e =>
                {
                    e.durationType = ModifierDurationType.Duration;
                    e.duration = 2f;
                }
            );
            EffectHandle handle = handler.ApplyEffect(effect, 16777216f).Value;
            effect.duration = 100f;
            Assert.IsTrue(handler.TryGetRemainingDuration(handle, 16777216f, out float remaining));
            Assert.That(remaining, Is.EqualTo(2f));
            handler.ProcessEffectExpirations(16777218f);
            Assert.IsFalse(handler.IsEffectActive(effect));
            effect.durationType = ModifierDurationType.Infinite;
            handle = handler.ApplyEffect(effect, float.MaxValue).Value;
            handler.ProcessEffectExpirations(float.MaxValue);
            Assert.IsTrue(handler.IsEffectActive(effect));
            Assert.IsFalse(handler.TryGetRemainingDuration(handle, float.MaxValue, out remaining));
            Assert.That(remaining, Is.EqualTo(0f));
            Assert.IsFalse(handler.RefreshEffect(handle, true, float.MaxValue));
        }

        [TestCase(
            float.MaxValue,
            float.MaxValue,
            float.PositiveInfinity,
            true,
            TestName = "EffectClock.Infinity.AfterFiniteOverflow"
        )]
        [TestCase(
            -float.MaxValue,
            -float.MaxValue,
            float.NegativeInfinity,
            true,
            TestName = "EffectClock.NegativeInfinity.AfterFiniteOverflow"
        )]
        [TestCase(10f, 1f, float.NaN, false, TestName = "EffectClock.NaN")]
        public void NonfiniteClockRetainsExistingQueryAndExpirationPolicy(
            float start,
            float duration,
            float currentTime,
            bool expires
        )
        {
            EffectHandler handler = CreateEntity().handler;
            AttributeEffect effect = CreateEffect(
                nameof(NonfiniteClockRetainsExistingQueryAndExpirationPolicy),
                e =>
                {
                    e.durationType = ModifierDurationType.Duration;
                    e.duration = duration;
                }
            );
            EffectHandle handle = handler.ApplyEffect(effect, start).Value;
            Assert.IsTrue(
                handler.TryGetRemainingDuration(handle, currentTime, out float remaining)
            );
            Assert.IsTrue(float.IsNaN(remaining));
            handler.ProcessEffectExpirations(currentTime);
            Assert.AreEqual(!expires, handler.IsEffectActive(effect));
        }

        [TestCase(false, TestName = "EffectExpiry.Refresh.RespectsPolicy")]
        [TestCase(true, TestName = "EffectExpiry.Refresh.IgnoresPolicy")]
        public void RefreshResetsActualExpirationOnlyWhenAllowed(bool ignorePolicy)
        {
            EffectHandler handler = CreateEntity().handler;
            AttributeEffect effect = CreateEffect(
                nameof(RefreshResetsActualExpirationOnlyWhenAllowed),
                e =>
                {
                    e.durationType = ModifierDurationType.Duration;
                    e.duration = 4f;
                    e.resetDurationOnReapplication = false;
                }
            );
            EffectHandle handle = handler.ApplyEffect(effect, 16777216f).Value;
            Assert.AreEqual(ignorePolicy, handler.RefreshEffect(handle, ignorePolicy, 16777218f));
            handler.ProcessEffectExpirations(16777220f);
            Assert.AreEqual(ignorePolicy, handler.IsEffectActive(effect));
            handler.ProcessEffectExpirations(16777222f);
            Assert.IsFalse(handler.IsEffectActive(effect));
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
    }
}
