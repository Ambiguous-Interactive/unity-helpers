// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Tags.Helpers;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EffectHandlerReentrancyTests : TagsTestBase
    {
        private const string FailureMessage = "Reentrancy fixture teardown failure";
        private const string LifecycleTag = "Lifecycle";
        private const string SecondaryTag = "Secondary";
        private const string TertiaryTag = "Tertiary";

        private static readonly Regex StackCapRefusalPattern = new(
            "made no progress because a removal callback re-applied it"
        );

        [SetUp]
        public void SetUp()
        {
            ResetEffectHandleId();
            EffectLifecycleLog.ResetForTests();
            ReentrantEffectBehavior.ResetForTests();
            ReentrantCosmeticComponent.ResetForTests();
            RecordingEffectBehavior.ResetForTests();
            RecordingCosmeticComponent.ResetCounters();
            SiblingDestroyingCosmeticComponent.ResetForTests();
        }

        [TearDown]
        public void TearDownHooks()
        {
            /*
                Destroying the tracked entity fires CosmeticEffectComponent.OnDestroy, which can re-enter a
                hook. Clear them before the base teardown reaches the objects.
            */
            ReentrantEffectBehavior.ResetForTests();
            ReentrantCosmeticComponent.ResetForTests();
        }

        [UnityTest]
        public IEnumerator RemoveEffectDetachesHandleBeforeAnyTeardownCallback()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateLifecycleEffect(
                "Detach",
                LifecycleTag,
                requiresInstance: false
            );
            EffectHandle handle = handler.ApplyEffect(effect).Value;
            Assert.IsTrue(handler.IsEffectActive(effect));

            List<bool> activeObservations = new();
            List<int> stackObservations = new();
            List<int> listedObservations = new();

            void Observe()
            {
                activeObservations.Add(handler.IsEffectActive(effect));
                stackObservations.Add(handler.GetEffectStackCount(effect));
                listedObservations.Add(handler.GetActiveEffects().Count);
            }

            attributes.OnAttributeModified += (_, _, _) =>
            {
                EffectLifecycleLog.Record(EffectLifecycleLog.AttributeModified);
                Observe();
            };
            tags.OnTagRemoved += _ =>
            {
                EffectLifecycleLog.Record(EffectLifecycleLog.TagRemoved);
                Observe();
            };
            handler.OnEffectRemoved += _ =>
            {
                EffectLifecycleLog.Record(EffectLifecycleLog.EffectRemoved);
                Observe();
            };
            ReentrantCosmeticComponent.RemoveHook = _ => Observe();
            ReentrantEffectBehavior.RemoveHook = _ => Observe();

            EffectLifecycleLog.ResetForTests();
            handler.RemoveEffect(handle);

            CollectionAssert.AreEqual(
                new[]
                {
                    EffectLifecycleLog.AttributeModified,
                    EffectLifecycleLog.TagRemoved,
                    EffectLifecycleLog.CosmeticRemoved,
                    EffectLifecycleLog.EffectRemoved,
                    EffectLifecycleLog.BehaviorRemoved,
                },
                EffectLifecycleLog.Entries
            );

            Assert.AreEqual(5, activeObservations.Count);
            foreach (bool observedActive in activeObservations)
            {
                Assert.IsFalse(observedActive);
            }

            foreach (int observedStack in stackObservations)
            {
                Assert.AreEqual(0, observedStack);
            }

            foreach (int observedListed in listedObservations)
            {
                Assert.AreEqual(0, observedListed);
            }

            Assert.IsFalse(tags.HasTag(LifecycleTag));
            Assert.AreEqual(100f, attributes.health.CurrentValue);
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        public IEnumerator RecursiveRemovalDuringTeardownIsANoOp()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateLifecycleEffect(
                "Recursive",
                LifecycleTag,
                requiresInstance: false
            );
            EffectHandle handle = handler.ApplyEffect(effect).Value;

            int tagRemovals = 0;
            int attributeNotifications = 0;
            attributes.OnAttributeModified += (_, _, _) => ++attributeNotifications;
            tags.OnTagRemoved += _ => ++tagRemovals;
            handler.OnEffectRemoved += removed =>
            {
                EffectLifecycleLog.Record(EffectLifecycleLog.EffectRemoved);
                handler.RemoveEffect(removed);
                handler.RemoveEffect(handle);
            };
            ReentrantEffectBehavior.RemoveHook = context =>
                context.handler.RemoveEffect(context.handle);
            ReentrantCosmeticComponent.RemoveHook = _ => handler.RemoveEffect(handle);

            EffectLifecycleLog.ResetForTests();
            handler.RemoveEffect(handle);

            Assert.AreEqual(1, EffectLifecycleLog.CountOf(EffectLifecycleLog.EffectRemoved));
            Assert.AreEqual(1, EffectLifecycleLog.CountOf(EffectLifecycleLog.CosmeticRemoved));
            Assert.AreEqual(1, EffectLifecycleLog.CountOf(EffectLifecycleLog.BehaviorRemoved));
            Assert.AreEqual(1, ReentrantEffectBehavior.RemoveCount);
            Assert.AreEqual(1, ReentrantCosmeticComponent.RemovedCount);
            Assert.AreEqual(1, tagRemovals);
            Assert.AreEqual(1, attributeNotifications);

            int entriesAfterFirstRemoval = EffectLifecycleLog.Entries.Count;
            handler.RemoveEffect(handle);
            Assert.AreEqual(entriesAfterFirstRemoval, EffectLifecycleLog.Entries.Count);

            handler.RemoveEffect(EffectHandle.CreateInstance(effect));
            Assert.AreEqual(entriesAfterFirstRemoval, EffectLifecycleLog.Entries.Count);
            Assert.AreEqual(0, handler.GetActiveEffects().Count);
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        public IEnumerator SelfRemovalFromBehaviorTickStopsLaterBehaviors()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            ReentrantEffectBehavior reentrant = Track(
                ScriptableObject.CreateInstance<ReentrantEffectBehavior>()
            );
            RecordingEffectBehavior recording = Track(
                ScriptableObject.CreateInstance<RecordingEffectBehavior>()
            );
            AttributeEffect effect = CreateEffect(
                "TickSelfRemoval",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.behaviors.Add(reentrant);
                    e.behaviors.Add(recording);
                    e.behaviors.Add(recording);
                }
            );

            EffectHandle handle = handler.ApplyEffect(effect).Value;
            ReentrantEffectBehavior.TickHook = context =>
            {
                context.handler.RemoveEffect(context.handle);
                RentAndMutateBehaviorBuffer();
            };

            int processedTicks = handler.ProcessBehaviorTicksForTesting(deltaTime: 0.1f);

            Assert.AreEqual(1, processedTicks);
            Assert.AreEqual(1, ReentrantEffectBehavior.TickCount);
            Assert.AreEqual(0, RecordingEffectBehavior.TickCount);
            Assert.AreEqual(1, ReentrantEffectBehavior.RemoveCount);
            Assert.AreEqual(2, RecordingEffectBehavior.RemoveCount);
            Assert.IsFalse(handler.IsEffectActive(effect));
            Assert.AreEqual(0, handler.ProcessBehaviorTicksForTesting(deltaTime: 0.1f));
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        public IEnumerator SelfRemovalFromPeriodicTickStopsLaterCallbacks()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            ReentrantEffectBehavior reentrant = Track(
                ScriptableObject.CreateInstance<ReentrantEffectBehavior>()
            );
            RecordingEffectBehavior recording = Track(
                ScriptableObject.CreateInstance<RecordingEffectBehavior>()
            );
            AttributeEffect effect = CreateEffect(
                "PeriodicSelfRemoval",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.periodicEffects.Add(new PeriodicEffectDefinition { interval = 0.05f });
                    e.periodicEffects.Add(new PeriodicEffectDefinition { interval = 0.05f });
                    e.behaviors.Add(reentrant);
                    e.behaviors.Add(recording);
                }
            );

            EffectHandle handle = handler.ApplyEffectForTesting(effect, currentTime: 600f).Value;
            ReentrantEffectBehavior.PeriodicTickHook = (context, _) =>
            {
                context.handler.RemoveEffect(context.handle);
                RentAndMutateBehaviorBuffer();
                RentAndMutatePeriodicBuffer();
            };

            int consumedTicks = handler.ProcessPeriodicEffectsForTesting(
                currentTime: 600.6f,
                deltaTime: 0.6f
            );

            Assert.AreEqual(1, consumedTicks);
            Assert.AreEqual(1, ReentrantEffectBehavior.PeriodicTickCount);
            Assert.AreEqual(0, RecordingEffectBehavior.PeriodicTickCount);
            Assert.AreEqual(1, ReentrantEffectBehavior.RemoveCount);
            Assert.AreEqual(1, RecordingEffectBehavior.RemoveCount);
            Assert.IsFalse(handler.IsEffectActive(effect));
            Assert.AreEqual(
                0,
                handler.ProcessPeriodicEffectsForTesting(currentTime: 601.6f, deltaTime: 1f)
            );
            Assert.AreEqual(0, handler.ProcessBehaviorTicksForTesting(deltaTime: 0.1f));
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        public IEnumerator ReapplyDuringTeardownProducesIndependentHandle()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateLifecycleEffect(
                "Reapply",
                LifecycleTag,
                requiresInstance: false
            );
            EffectHandle first = handler.ApplyEffect(effect).Value;

            bool reapplied = false;
            EffectHandle second = default;
            handler.OnEffectRemoved += _ =>
            {
                if (reapplied)
                {
                    return;
                }

                reapplied = true;
                second = handler.ApplyEffect(effect).Value;
            };

            handler.RemoveEffect(first);

            Assert.IsTrue(reapplied);
            Assert.AreNotEqual(first.id, second.id);
            Assert.IsTrue(handler.IsEffectActive(effect));
            Assert.AreEqual(1, handler.GetEffectStackCount(effect));
            CollectionAssert.AreEqual(new[] { second }, handler.GetActiveEffects());
            Assert.IsTrue(tags.HasTag(LifecycleTag));
            Assert.AreEqual(105f, attributes.health.CurrentValue);

            handler.RemoveEffect(second);
            Assert.AreEqual(0, handler.GetActiveEffects().Count);
            Assert.IsFalse(tags.HasTag(LifecycleTag));
            Assert.AreEqual(100f, attributes.health.CurrentValue);
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        public IEnumerator RemoveAllEffectsKeepsAnEffectAppliedDuringTeardown()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect first = CreateLifecycleEffect(
                "BulkFirst",
                LifecycleTag,
                requiresInstance: false
            );
            AttributeEffect second = CreateLifecycleEffect(
                "BulkSecond",
                SecondaryTag,
                requiresInstance: false
            );
            AttributeEffect survivor = CreateEffect(
                "Survivor",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.effectTags.Add(nameof(survivor));
                }
            );

            _ = handler.ApplyEffect(first).Value;
            _ = handler.ApplyEffect(second).Value;

            bool applied = false;
            EffectHandle survivorHandle = default;
            handler.OnEffectRemoved += _ =>
            {
                if (applied)
                {
                    return;
                }

                applied = true;
                survivorHandle = handler.ApplyEffect(survivor).Value;
            };

            handler.RemoveAllEffects();

            Assert.IsTrue(applied);
            CollectionAssert.AreEqual(new[] { survivorHandle }, handler.GetActiveEffects());
            Assert.IsTrue(handler.IsEffectActive(survivor));
            Assert.IsFalse(handler.IsEffectActive(first));
            Assert.IsFalse(handler.IsEffectActive(second));
            Assert.IsTrue(tags.HasTag(nameof(survivor)));
            Assert.IsFalse(tags.HasTag(LifecycleTag));
            Assert.IsFalse(tags.HasTag(SecondaryTag));
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        [TestCaseSource(nameof(TeardownFailureCases))]
        public IEnumerator TeardownFailurePropagatesWithConsistentState(EffectTeardownPhase phase)
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            int initialChildCount = entity.transform.childCount;
            AttributeEffect effect = CreateLifecycleEffect(
                "Failure",
                LifecycleTag,
                requiresInstance: true
            );
            EffectHandle handle = handler.ApplyEffect(effect).Value;
            Assert.AreEqual(initialChildCount + 1, entity.transform.childCount);

            /*
                Disarms itself after firing once, so the re-application at the end of this test and the
                handler's own OnDestroy teardown do not throw a second time.
            */
            bool armed = true;
            void FailOnce()
            {
                if (!armed)
                {
                    return;
                }

                armed = false;
                throw new InvalidOperationException(FailureMessage);
            }

            switch (phase)
            {
                case EffectTeardownPhase.AttributeModification:
                {
                    attributes.OnAttributeModified += (_, _, _) => FailOnce();
                    break;
                }
                case EffectTeardownPhase.Tag:
                {
                    tags.OnTagRemoved += _ => FailOnce();
                    break;
                }
                case EffectTeardownPhase.Cosmetic:
                {
                    ReentrantCosmeticComponent.RemoveHook = _ => FailOnce();
                    break;
                }
                case EffectTeardownPhase.EffectRemovedEvent:
                {
                    handler.OnEffectRemoved += _ => FailOnce();
                    break;
                }
                case EffectTeardownPhase.BehaviorRemove:
                {
                    ReentrantEffectBehavior.RemoveHook = _ => FailOnce();
                    break;
                }
                default:
                {
                    Assert.Fail($"Unhandled teardown phase {phase}.");
                    break;
                }
            }

            EffectLifecycleLog.ResetForTests();
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
                handler.RemoveEffect(handle)
            );
            Assert.AreEqual(FailureMessage, failure.Message);

            Assert.AreEqual(1, EffectLifecycleLog.CountOf(EffectLifecycleLog.CosmeticRemoved));
            Assert.AreEqual(1, EffectLifecycleLog.CountOf(EffectLifecycleLog.BehaviorRemoved));
            Assert.IsFalse(handler.IsEffectActive(effect));
            Assert.AreEqual(0, handler.GetActiveEffects().Count);
            Assert.AreEqual(0, handler.GetEffectStackCount(effect));
            Assert.IsFalse(tags.HasTag(LifecycleTag));
            Assert.AreEqual(100f, attributes.health.CurrentValue);
            Assert.AreEqual(1, ReentrantEffectBehavior.RemoveCount);
            Assert.AreEqual(0, handler.ProcessBehaviorTicksForTesting(deltaTime: 0.1f));

            int entriesAfterFailure = EffectLifecycleLog.Entries.Count;
            handler.RemoveEffect(handle);
            Assert.AreEqual(entriesAfterFailure, EffectLifecycleLog.Entries.Count);

            yield return null;

            Assert.AreEqual(initialChildCount, entity.transform.childCount);
            foreach (EffectBehavior clone in ReentrantEffectBehavior.Clones)
            {
                Assert.IsTrue(clone == null);
            }

            ReentrantEffectBehavior.ResetForTests();
            ReentrantCosmeticComponent.ResetForTests();
            EffectHandle replacement = handler.ApplyEffect(effect).Value;
            Assert.AreNotEqual(handle.id, replacement.id);
            Assert.IsTrue(handler.IsEffectActive(effect));
            Assert.AreEqual(1, ReentrantEffectBehavior.ApplyCount);
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        public IEnumerator ApplyFailureFromEffectAppliedRollsBackEverything()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            int initialChildCount = entity.transform.childCount;
            AttributeEffect effect = CreateLifecycleEffect(
                "ApplyFailure",
                LifecycleTag,
                requiresInstance: true
            );
            handler.OnEffectApplied += _ => throw new InvalidOperationException(FailureMessage);

            EffectHandle? applied = null;
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            {
                applied = handler.ApplyEffect(effect);
            });
            Assert.AreEqual(FailureMessage, failure.Message);

            Assert.IsFalse(applied.HasValue);
            Assert.IsFalse(handler.IsEffectActive(effect));
            Assert.AreEqual(0, handler.GetActiveEffects().Count);
            Assert.IsFalse(tags.HasTag(LifecycleTag));
            Assert.AreEqual(100f, attributes.health.CurrentValue);
            Assert.AreEqual(1, ReentrantEffectBehavior.RemoveCount);
            Assert.AreEqual(1, ReentrantCosmeticComponent.RemovedCount);
            Assert.AreEqual(0, handler.ProcessBehaviorTicksForTesting(deltaTime: 0.1f));

            yield return null;

            Assert.AreEqual(initialChildCount, entity.transform.childCount);
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        [TestCaseSource(nameof(CosmeticInstancingCases))]
        public IEnumerator SelfRemovalFromBehaviorApplyAppliesNothing(bool requiresInstance)
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            int initialChildCount = entity.transform.childCount;
            AttributeEffect effect = CreateLifecycleEffect(
                "ApplySelfRemoval",
                LifecycleTag,
                requiresInstance
            );
            int appliedEvents = 0;
            handler.OnEffectApplied += _ => ++appliedEvents;
            ReentrantEffectBehavior.ApplyHook = context =>
                context.handler.RemoveEffect(context.handle);

            _ = handler.ApplyEffect(effect).Value;

            Assert.AreEqual(1, ReentrantEffectBehavior.ApplyCount);
            Assert.AreEqual(1, ReentrantEffectBehavior.RemoveCount);
            Assert.AreEqual(0, appliedEvents);
            Assert.AreEqual(0, ReentrantCosmeticComponent.AppliedCount);
            Assert.AreEqual(0, ReentrantCosmeticComponent.RemovedCount);
            Assert.IsFalse(handler.IsEffectActive(effect));
            Assert.AreEqual(0, handler.GetActiveEffects().Count);
            Assert.IsFalse(tags.HasTag(LifecycleTag));
            Assert.AreEqual(100f, attributes.health.CurrentValue);
            Assert.AreEqual(0, handler.ProcessBehaviorTicksForTesting(deltaTime: 0.1f));
            Assert.AreEqual(initialChildCount, entity.transform.childCount);
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        [TestCaseSource(nameof(CosmeticInstancingCases))]
        public IEnumerator SelfRemovalFromTheFirstCosmeticComponentSkipsTheRest(
            bool requiresInstance
        )
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            int initialChildCount = entity.transform.childCount;
            GameObject template = CreateTrackedGameObject(
                "MultiComponentCosmetic",
                typeof(CosmeticEffectData)
            );
            ReentrantCosmeticComponent first = template.AddComponent<ReentrantCosmeticComponent>();
            first.requireInstance = requiresInstance;
            RecordingCosmeticComponent second = template.AddComponent<RecordingCosmeticComponent>();
            second.requireInstance = false;
            AttributeEffect effect = CreateEffect(
                "CosmeticSelfRemoval",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.effectTags.Add(LifecycleTag);
                    e.cosmeticEffects.Add(template.GetComponent<CosmeticEffectData>());
                }
            );

            ReentrantCosmeticComponent.ApplyHook = _ => handler.RemoveAllEffects();

            _ = handler.ApplyEffect(effect).Value;

            Assert.AreEqual(1, ReentrantCosmeticComponent.AppliedCount);
            Assert.AreEqual(0, RecordingCosmeticComponent.AppliedCount);
            Assert.AreEqual(1, ReentrantCosmeticComponent.RemovedCount);
            Assert.IsFalse(handler.IsEffectActive(effect));
            Assert.AreEqual(0, handler.GetActiveEffects().Count);
            Assert.IsFalse(tags.HasTag(LifecycleTag));
            AssertHandlerIsIdle(handler);

            yield return null;

            Assert.AreEqual(initialChildCount, entity.transform.childCount);
        }

        [UnityTest]
        public IEnumerator ReplaceEvictionDetachesBeforeAReentrantApplication()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect replaced = CreateEffect(
                "Replaced",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackingMode = EffectStackingMode.Replace;
                }
            );
            AttributeEffect bystander = CreateEffect(
                "Bystander",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                }
            );

            EffectHandle first = handler.ApplyEffect(replaced).Value;

            bool applied = false;
            bool observedActive = true;
            int observedStackCount = -1;
            bool observedListed = true;
            EffectHandle bystanderHandle = default;
            handler.OnEffectRemoved += _ =>
            {
                if (applied)
                {
                    return;
                }

                applied = true;
                observedActive = handler.IsEffectActive(replaced);
                observedStackCount = handler.GetEffectStackCount(replaced);
                observedListed = handler.GetActiveEffects().Contains(first);
                bystanderHandle = handler.ApplyEffect(bystander).Value;
            };

            EffectHandle second = handler.ApplyEffect(replaced).Value;

            Assert.IsTrue(applied);
            Assert.IsFalse(observedActive);
            Assert.AreEqual(0, observedStackCount);
            Assert.IsFalse(observedListed);
            Assert.AreNotEqual(first.id, second.id);
            Assert.AreEqual(1, handler.GetEffectStackCount(replaced));
            Assert.AreEqual(1, handler.GetEffectStackCount(bystander));
            List<EffectHandle> active = handler.GetActiveEffects();
            Assert.AreEqual(2, active.Count);
            CollectionAssert.Contains(active, second);
            CollectionAssert.Contains(active, bystanderHandle);
            CollectionAssert.DoesNotContain(active, first);
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        public IEnumerator CapEvictionRefusesAnApplicationThatWouldExceedMaximumStacks()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "Capped",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackingMode = EffectStackingMode.Stack;
                    e.maximumStacks = 2;
                }
            );

            EffectHandle first = handler.ApplyEffect(effect).Value;
            EffectHandle second = handler.ApplyEffect(effect).Value;

            bool evicted = false;
            EffectHandle reapplied = default;
            handler.OnEffectRemoved += _ =>
            {
                if (evicted)
                {
                    return;
                }

                evicted = true;
                reapplied = handler.ApplyEffect(effect).Value;
            };

            ExpectWallstopLog(LogType.Warning, StackCapRefusalPattern);
            EffectHandle? third = handler.ApplyEffect(effect);

            Assert.IsTrue(evicted);
            Assert.IsFalse(third.HasValue);
            Assert.AreEqual(2, handler.GetEffectStackCount(effect));
            List<EffectHandle> active = handler.GetActiveEffects();
            Assert.AreEqual(2, active.Count);
            CollectionAssert.DoesNotContain(active, first);
            CollectionAssert.Contains(active, second);
            CollectionAssert.Contains(active, reapplied);
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        public IEnumerator ExpirationTeardownRunsOnceUnderAReentrantRemoval()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect expiring = CreateLifecycleEffect(
                "Expiring",
                LifecycleTag,
                requiresInstance: false,
                e =>
                {
                    e.durationType = ModifierDurationType.Duration;
                    e.duration = 0f;
                }
            );
            AttributeEffect survivor = CreateEffect(
                "ExpirySurvivor",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.effectTags.Add(SecondaryTag);
                }
            );

            bool applied = false;
            bool observedActive = true;
            EffectHandle survivorHandle = default;
            handler.OnEffectRemoved += removed =>
            {
                EffectLifecycleLog.Record(EffectLifecycleLog.EffectRemoved);
                if (applied)
                {
                    return;
                }

                applied = true;
                observedActive = handler.IsEffectActive(expiring);
                handler.RemoveEffect(removed);
                survivorHandle = handler.ApplyEffect(survivor).Value;
            };

            _ = handler.ApplyEffect(expiring).Value;
            Assert.IsTrue(tags.HasTag(LifecycleTag));
            EffectLifecycleLog.ResetForTests();

            yield return null;
            yield return null;

            Assert.IsTrue(applied);
            Assert.IsFalse(observedActive);
            Assert.AreEqual(1, EffectLifecycleLog.CountOf(EffectLifecycleLog.EffectRemoved));
            Assert.AreEqual(1, EffectLifecycleLog.CountOf(EffectLifecycleLog.CosmeticRemoved));
            Assert.AreEqual(1, EffectLifecycleLog.CountOf(EffectLifecycleLog.BehaviorRemoved));
            Assert.AreEqual(1, ReentrantEffectBehavior.RemoveCount);
            Assert.AreEqual(1, ReentrantCosmeticComponent.RemovedCount);
            Assert.IsFalse(tags.HasTag(LifecycleTag));
            Assert.IsTrue(tags.HasTag(SecondaryTag));
            Assert.IsFalse(handler.IsEffectActive(expiring));
            Assert.AreEqual(100f, attributes.health.CurrentValue);
            CollectionAssert.AreEqual(new[] { survivorHandle }, handler.GetActiveEffects());
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        public IEnumerator NestedRemovalsThreeLevelsDeepEachRunExactlyOnce()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect outer = CreateLifecycleEffect(
                "NestedOuter",
                LifecycleTag,
                requiresInstance: false
            );
            AttributeEffect middle = CreateLifecycleEffect(
                "NestedMiddle",
                SecondaryTag,
                requiresInstance: false
            );
            AttributeEffect inner = CreateLifecycleEffect(
                "NestedInner",
                TertiaryTag,
                requiresInstance: false
            );
            AttributeEffect survivor = CreateEffect(
                "NestedSurvivor",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                }
            );

            EffectHandle outerHandle = handler.ApplyEffect(outer).Value;
            EffectHandle middleHandle = handler.ApplyEffect(middle).Value;
            EffectHandle innerHandle = handler.ApplyEffect(inner).Value;
            Assert.AreEqual(115f, attributes.health.CurrentValue);

            int nesting = 0;
            int deepestNesting = 0;
            List<int> observedActiveCounts = new();
            handler.OnEffectRemoved += _ =>
            {
                EffectLifecycleLog.Record(EffectLifecycleLog.EffectRemoved);
                ++nesting;
                deepestNesting = Math.Max(deepestNesting, nesting);
                observedActiveCounts.Add(handler.GetActiveEffects().Count);
                if (nesting == 1)
                {
                    handler.RemoveEffect(middleHandle);
                }
                else if (nesting == 2)
                {
                    handler.RemoveEffect(innerHandle);
                }
                else if (nesting == 3)
                {
                    _ = handler.ApplyEffect(survivor).Value;
                }

                --nesting;
            };

            EffectLifecycleLog.ResetForTests();
            handler.RemoveEffect(outerHandle);

            Assert.AreEqual(3, deepestNesting);
            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, observedActiveCounts);
            Assert.AreEqual(3, EffectLifecycleLog.CountOf(EffectLifecycleLog.EffectRemoved));
            Assert.AreEqual(3, EffectLifecycleLog.CountOf(EffectLifecycleLog.CosmeticRemoved));
            Assert.AreEqual(3, EffectLifecycleLog.CountOf(EffectLifecycleLog.BehaviorRemoved));
            Assert.AreEqual(3, ReentrantEffectBehavior.RemoveCount);
            Assert.AreEqual(3, ReentrantCosmeticComponent.RemovedCount);
            Assert.IsFalse(tags.HasTag(LifecycleTag));
            Assert.IsFalse(tags.HasTag(SecondaryTag));
            Assert.IsFalse(tags.HasTag(TertiaryTag));
            Assert.AreEqual(100f, attributes.health.CurrentValue);
            Assert.AreEqual(1, handler.GetActiveEffects().Count);
            Assert.IsTrue(handler.IsEffectActive(survivor));
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        public IEnumerator DestroyingTheHandlerRunsTeardownForEveryEffect()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateLifecycleEffect(
                "Destroyed",
                LifecycleTag,
                requiresInstance: false
            );
            handler.OnEffectRemoved += _ =>
                EffectLifecycleLog.Record(EffectLifecycleLog.EffectRemoved);
            _ = handler.ApplyEffect(effect).Value;
            Assert.IsTrue(handler.IsEffectActive(effect));

            EffectLifecycleLog.ResetForTests();
            Object.DestroyImmediate(entity); // UNH-SUPPRESS: the destroy path is the subject

            Assert.AreEqual(1, EffectLifecycleLog.CountOf(EffectLifecycleLog.EffectRemoved));
            Assert.AreEqual(1, EffectLifecycleLog.CountOf(EffectLifecycleLog.CosmeticRemoved));
            Assert.AreEqual(1, EffectLifecycleLog.CountOf(EffectLifecycleLog.BehaviorRemoved));
            Assert.AreEqual(1, ReentrantEffectBehavior.RemoveCount);
            Assert.AreEqual(1, ReentrantCosmeticComponent.RemovedCount);
            Assert.AreEqual(0, handler.GetActiveEffects().Count);
            Assert.IsFalse(handler.IsEffectActive(effect));
            AssertHandlerIsIdle(handler);
        }

        [UnityTest]
        public IEnumerator DestroyingTheHandlerFromATickKeepsTheTraversalCounterBalanced()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            ReentrantEffectBehavior reentrant = Track(
                ScriptableObject.CreateInstance<ReentrantEffectBehavior>()
            );
            RecordingEffectBehavior recording = Track(
                ScriptableObject.CreateInstance<RecordingEffectBehavior>()
            );
            AttributeEffect effect = CreateEffect(
                "TickDestroysHandler",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.behaviors.Add(reentrant);
                    e.behaviors.Add(recording);
                }
            );

            _ = handler.ApplyEffect(effect).Value;
            ReentrantEffectBehavior.TickHook = context =>
            {
                Object.DestroyImmediate(context.handler.gameObject); // UNH-SUPPRESS: the subject
                RentAndMutateBehaviorBuffer();
            };

            int processedTicks = handler.ProcessBehaviorTicksForTesting(deltaTime: 0.1f);

            Assert.AreEqual(1, processedTicks);
            Assert.AreEqual(1, ReentrantEffectBehavior.TickCount);
            Assert.AreEqual(0, RecordingEffectBehavior.TickCount);
            Assert.AreEqual(1, ReentrantEffectBehavior.RemoveCount);
            Assert.AreEqual(1, RecordingEffectBehavior.RemoveCount);
            Assert.IsTrue(entity == null);
            Assert.AreEqual(0, handler.GetActiveEffects().Count);
            AssertHandlerIsIdle(handler);
        }

        /// <summary>
        /// A cosmetic component that cleans up a one-shot sibling leaves a destroyed entry in the
        /// snapshot the handler is still walking, and Destroy runs OnDestroy immediately outside
        /// play mode. Every one of the four callback loops has to step over it.
        /// </summary>
        [UnityTest]
        [TestCaseSource(nameof(DestroyedCosmeticSiblingCases))]
        public IEnumerator ADestroyedCosmeticSiblingDoesNotAbortTheEffectPhase(
            ModifierDurationType durationType,
            bool requiresInstance,
            bool destroysDuringRemoval
        )
        {
            (_, EffectHandler handler, TestAttributesComponent attributes, TagHandler tags) =
                CreateEntity();
            yield return null;

            GameObject template = CreateTrackedGameObject(
                "SiblingDestroyingCosmetic",
                typeof(CosmeticEffectData)
            );
            SiblingDestroyingCosmeticComponent destroyer =
                template.AddComponent<SiblingDestroyingCosmeticComponent>();
            SiblingDestroyingCosmeticComponent doomed =
                template.AddComponent<SiblingDestroyingCosmeticComponent>();
            destroyer.requireInstance = requiresInstance;
            destroyer.destroysSibling = true;
            destroyer.destroysDuringRemoval = destroysDuringRemoval;
            doomed.requireInstance = requiresInstance;
            doomed.destroysDuringRemoval = destroysDuringRemoval;

            AttributeEffect effect = CreateEffect(
                "SiblingDestroyingEffect",
                e =>
                {
                    e.durationType = durationType;
                    e.effectTags.Add(LifecycleTag);
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = -30f,
                        }
                    );
                    e.cosmeticEffects.Add(template.GetComponent<CosmeticEffectData>());
                }
            );

            EffectHandle? handle = handler.ApplyEffect(effect);

            Assert.AreEqual(
                70f,
                attributes.health.CurrentValue,
                "the modification phase runs after the cosmetic phase and must still be reached"
            );
            Assert.IsTrue(tags.HasTag(LifecycleTag));

            if (!destroysDuringRemoval)
            {
                AssertHandlerIsIdle(handler);
                yield break;
            }

            Assert.IsTrue(handle.HasValue, "a durational effect returns a handle");
            handler.RemoveEffect(handle.Value);

            Assert.AreEqual(100f, attributes.health.CurrentValue);
            Assert.IsFalse(tags.HasTag(LifecycleTag));
            Assert.IsFalse(handler.IsEffectActive(effect));
            AssertHandlerIsIdle(handler);
        }

        private static IEnumerable<TestCaseData> DestroyedCosmeticSiblingCases()
        {
            yield return new TestCaseData(ModifierDurationType.Instant, false, false)
                .Returns(null)
                .SetName("DestroyedSibling.Instant.Shared.DuringApply");
            yield return new TestCaseData(ModifierDurationType.Infinite, false, false)
                .Returns(null)
                .SetName("DestroyedSibling.Infinite.Shared.DuringApply");
            yield return new TestCaseData(ModifierDurationType.Infinite, false, true)
                .Returns(null)
                .SetName("DestroyedSibling.Infinite.Shared.DuringRemoval");
            yield return new TestCaseData(ModifierDurationType.Infinite, true, true)
                .Returns(null)
                .SetName("DestroyedSibling.Infinite.Instanced.DuringRemoval");
        }

        private static void AssertHandlerIsIdle(EffectHandler handler)
        {
            Assert.AreEqual(
                0,
                handler.TraversalDepthForTesting,
                "The traversal counter must return to zero once every callback has unwound."
            );
            Assert.AreEqual(
                0,
                handler.DeferredLeaseCountForTesting,
                "Every deferred pooled lease must be released once the outermost traversal exits."
            );
        }

        private static IEnumerable<TestCaseData> CosmeticInstancingCases()
        {
            yield return new TestCaseData(false).Returns(null);
            yield return new TestCaseData(true).Returns(null);
        }

        private static IEnumerable<TestCaseData> TeardownFailureCases()
        {
            yield return new TestCaseData(EffectTeardownPhase.AttributeModification)
                .Returns(null)
                .SetName("Teardown.AttributeModification.Throws");
            yield return new TestCaseData(EffectTeardownPhase.Tag)
                .Returns(null)
                .SetName("Teardown.Tag.Throws");
            yield return new TestCaseData(EffectTeardownPhase.Cosmetic)
                .Returns(null)
                .SetName("Teardown.Cosmetic.Throws");
            yield return new TestCaseData(EffectTeardownPhase.EffectRemovedEvent)
                .Returns(null)
                .SetName("Teardown.EffectRemovedEvent.Throws");
            yield return new TestCaseData(EffectTeardownPhase.BehaviorRemove)
                .Returns(null)
                .SetName("Teardown.BehaviorRemove.Throws");
        }

        // Rent the same pooled type to expose early return of the handler's active traversal list.
        private static void RentAndMutateBehaviorBuffer()
        {
            using PooledResource<List<EffectBehavior>> lease = Buffers<EffectBehavior>.List.Get(
                out List<EffectBehavior> stolen
            );
            stolen.Add(null);
            stolen.Add(null);
        }

        private static void RentAndMutatePeriodicBuffer()
        {
            using PooledResource<List<PeriodicEffectRuntimeState>> lease =
                Buffers<PeriodicEffectRuntimeState>.List.Get(
                    out List<PeriodicEffectRuntimeState> stolen
                );
            stolen.Add(null);
            stolen.Add(null);
        }

        /// <summary>
        /// An OnAttributeModified subscriber that removes the effect leaves the handle detached
        /// from every index. A modifier applied after that point has no handle to remove it, so the
        /// attribute stays changed with no active effect and no API that can undo it.
        /// </summary>
        [UnityTest]
        public IEnumerator AttributeApplicationStopsWhenACallbackTearsTheEffectDown()
        {
            (_, EffectHandler handler, TestAttributesComponent attributes, _) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "TwoModifications",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = 5f,
                        }
                    );
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.armor),
                            action = ModificationAction.Addition,
                            value = 7f,
                        }
                    );
                }
            );

            float baselineArmor = attributes.armor.CurrentValue;

            /*
                A non-tearing control proves the attribute still resolves, preventing an inert effect from
                falsely passing.
            */
            EffectHandle? control = handler.ApplyEffect(effect);
            yield return null;
            Assert.AreNotEqual(
                baselineArmor,
                attributes.armor.CurrentValue,
                "control: the second modification reaches armor when nothing interferes"
            );
            Assert.IsTrue(control.HasValue, "control: the application returned a handle");
            handler.RemoveEffect(control.Value);
            yield return null;
            Assert.AreEqual(
                baselineArmor,
                attributes.armor.CurrentValue,
                "control: removing the effect puts armor back"
            );

            bool torn = false;
            attributes.OnAttributeModified += (_, _, _) =>
            {
                if (torn)
                {
                    return;
                }

                torn = true;
                handler.RemoveAllEffects();
            };

            EffectHandle? handle = handler.ApplyEffect(effect);
            yield return null;

            Assert.IsTrue(torn, "the fixture must actually reach the removal callback");
            Assert.IsFalse(
                handler.IsEffectActive(effect),
                "the effect was removed by the callback"
            );
            Assert.AreEqual(
                baselineArmor,
                attributes.armor.CurrentValue,
                "the second modification ran against a handle that no longer exists, so nothing "
                    + "can ever remove it"
            );
            Assert.IsTrue(handle.HasValue, "the application returned a handle");
            handler.RemoveEffect(handle.Value);
            Assert.AreEqual(
                baselineArmor,
                attributes.armor.CurrentValue,
                "removing through the returned handle is a no-op once it has been detached, which "
                    + "is what would have made a surviving modifier permanent"
            );
        }

        /// <summary>
        /// The tag half of the same defect: an OnTagAdded subscriber that removes the effect leaves
        /// later tags raised by a handle that no longer exists, and no effect-level removal can
        /// clear them.
        /// </summary>
        [UnityTest]
        public IEnumerator TagApplicationStopsWhenACallbackTearsTheEffectDown()
        {
            (_, EffectHandler handler, _, TagHandler tags) = CreateEntity();
            yield return null;

            AttributeEffect effect = CreateEffect(
                "TwoTags",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.effectTags.Add(LifecycleTag);
                    e.effectTags.Add(SecondaryTag);
                }
            );

            bool torn = false;
            tags.OnTagAdded += tag =>
            {
                if (torn || tag != LifecycleTag)
                {
                    return;
                }

                torn = true;
                handler.RemoveAllEffects();
            };

            EffectHandle? applied = handler.ApplyEffect(effect);
            yield return null;

            Assert.IsTrue(applied.HasValue, "the application returned a handle");
            Assert.IsTrue(torn, "the fixture must actually reach the removal callback");
            Assert.IsFalse(
                handler.IsEffectActive(effect),
                "the effect was removed by the callback"
            );
            Assert.IsFalse(
                tags.HasTag(SecondaryTag),
                $"{SecondaryTag} was raised after the handle was detached, so nothing owns it and "
                    + "nothing can remove it"
            );
            Assert.IsFalse(tags.HasTag(LifecycleTag), "the removal must clear what it did raise");
        }

        /// <summary>
        /// A throwing OnTagAdded stops application part-way. Removal must decrement only the tags
        /// this handle actually raised: decrementing the rest takes a count that belongs to another
        /// effect, which then reports active while the tag it owns has been cleared.
        /// </summary>
        [UnityTest]
        public IEnumerator AThrowingTagSubscriberLeavesAnotherEffectsTagAlone()
        {
            (_, EffectHandler handler, _, TagHandler tags) = CreateEntity();
            yield return null;

            AttributeEffect owner = CreateEffect(
                "TagOwner",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.effectTags.Add(SecondaryTag);
                }
            );
            AttributeEffect thrower = CreateEffect(
                "TagThrower",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.effectTags.Add(LifecycleTag);
                    e.effectTags.Add(SecondaryTag);
                }
            );

            EffectHandle? ownerHandle = handler.ApplyEffect(owner);
            yield return null;
            Assert.IsTrue(tags.HasTag(SecondaryTag), "the owning effect raised its tag");

            tags.OnTagAdded += tag =>
            {
                if (tag == LifecycleTag)
                {
                    throw new InvalidOperationException(FailureMessage);
                }
            };

            EffectHandle? throwerHandle = null;
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            {
                throwerHandle = handler.ApplyEffect(thrower);
            });
            Assert.AreEqual(FailureMessage, failure.Message);
            Assert.IsFalse(throwerHandle.HasValue, "the failed application returned no handle");

            Assert.IsTrue(
                handler.IsEffectActive(owner),
                "the owning effect was never touched by the failed application"
            );
            Assert.IsTrue(
                tags.HasTag(SecondaryTag),
                $"{SecondaryTag} belongs to the owning effect; the failed application never raised "
                    + "it, so its rollback must not decrement it"
            );
            Assert.IsFalse(tags.HasTag(LifecycleTag), "the tag the failed application did raise");
            Assert.IsTrue(ownerHandle.HasValue, "the owning application returned a handle");
            handler.RemoveEffect(ownerHandle.Value);
            Assert.IsFalse(tags.HasTag(SecondaryTag), "and removing it clears the tag it owns");
        }

        /// <summary>
        /// Refresh is the default stacking mode and re-enters the cosmetic application for an
        /// effect that is already applied. A shared cosmetic gets one OnRemoveEffect however many
        /// times it was applied, so a second apply is a start with no stop.
        /// </summary>
        [UnityTest]
        public IEnumerator RefreshingAnEffectDoesNotReapplyASharedCosmetic()
        {
            (_, EffectHandler handler, _, _) = CreateEntity();
            yield return null;

            GameObject template = CreateTrackedGameObject(
                "SharedCosmetic",
                typeof(CosmeticEffectData)
            );
            RecordingCosmeticComponent recording =
                template.AddComponent<RecordingCosmeticComponent>();
            recording.requireInstance = false;
            CosmeticEffectData cosmetic = template.GetComponent<CosmeticEffectData>();

            AttributeEffect effect = CreateEffect(
                "RefreshedCosmetic",
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.stackingMode = EffectStackingMode.Refresh;
                    e.cosmeticEffects.Add(cosmetic);
                }
            );

            EffectHandle? first = handler.ApplyEffect(effect);
            yield return null;
            Assert.AreEqual(1, RecordingCosmeticComponent.AppliedCount, "the first application");

            EffectHandle? refreshed = handler.ApplyEffect(effect);
            yield return null;
            Assert.AreEqual(
                1,
                RecordingCosmeticComponent.AppliedCount,
                "a refresh of an already-applied effect must not start the cosmetic a second time"
            );
            Assert.AreEqual(first, refreshed, "a refresh reuses the existing handle");

            Assert.IsTrue(first.HasValue, "the first application returned a handle");
            handler.RemoveEffect(first.Value);
            yield return null;
            Assert.AreEqual(
                RecordingCosmeticComponent.AppliedCount,
                RecordingCosmeticComponent.RemovedCount,
                "every apply must be balanced by exactly one remove"
            );
        }

        private AttributeEffect CreateLifecycleEffect(
            string name,
            string effectTag,
            bool requiresInstance,
            Action<AttributeEffect> configure = null
        )
        {
            CosmeticEffectData cosmetic = CreateReentrantCosmetic(
                $"{name}Cosmetic",
                requiresInstance
            );
            ReentrantEffectBehavior behavior = Track(
                ScriptableObject.CreateInstance<ReentrantEffectBehavior>()
            );
            return CreateEffect(
                name,
                e =>
                {
                    e.durationType = ModifierDurationType.Infinite;
                    e.effectTags.Add(effectTag);
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Addition,
                            value = 5f,
                        }
                    );
                    e.cosmeticEffects.Add(cosmetic);
                    e.behaviors.Add(behavior);
                    configure?.Invoke(e);
                }
            );
        }

        private CosmeticEffectData CreateReentrantCosmetic(string name, bool requiresInstance)
        {
            GameObject template = CreateTrackedGameObject(name, typeof(CosmeticEffectData));
            ReentrantCosmeticComponent component =
                template.AddComponent<ReentrantCosmeticComponent>();
            component.requireInstance = requiresInstance;
            return template.GetComponent<CosmeticEffectData>();
        }

        private (
            GameObject entity,
            EffectHandler handler,
            TestAttributesComponent attributes,
            TagHandler tags
        ) CreateEntity()
        {
            GameObject entity = CreateTrackedGameObject(
                "ReentrancyEntity",
                typeof(TestAttributesComponent)
            );
            return (
                entity,
                entity.GetComponent<EffectHandler>(),
                entity.GetComponent<TestAttributesComponent>(),
                entity.GetComponent<TagHandler>()
            );
        }
    }
}
