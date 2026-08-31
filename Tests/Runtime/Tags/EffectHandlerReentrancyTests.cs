// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Tags.Helpers;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EffectHandlerReentrancyTests : TagsTestBase
    {
        private const string FailureMessage = "Reentrancy fixture teardown failure";
        private const string LifecycleTag = "Lifecycle";
        private const string SecondaryTag = "Secondary";

        [SetUp]
        public void SetUp()
        {
            ResetEffectHandleId();
            EffectLifecycleLog.ResetForTests();
            ReentrantEffectBehavior.ResetForTests();
            ReentrantCosmeticComponent.ResetForTests();
            RecordingEffectBehavior.ResetForTests();
            RecordingCosmeticComponent.ResetCounters();
        }

        [TearDown]
        public void TearDownHooks()
        {
            // Destroying the tracked entity fires CosmeticEffectComponent.OnDestroy, which can
            // re-enter a hook. Clear them before the base teardown reaches the objects.
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

            // Disarms itself after firing once, so the re-application at the end of this test and
            // the handler's own OnDestroy teardown do not throw a second time.
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
        }

        [UnityTest]
        public IEnumerator SelfRemovalFromBehaviorApplyAppliesNothing()
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
                requiresInstance: true
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
            Assert.IsFalse(handler.IsEffectActive(effect));
            Assert.AreEqual(0, handler.GetActiveEffects().Count);
            Assert.IsFalse(tags.HasTag(LifecycleTag));
            Assert.AreEqual(100f, attributes.health.CurrentValue);
            Assert.AreEqual(0, handler.ProcessBehaviorTicksForTesting(deltaTime: 0.1f));
            Assert.AreEqual(initialChildCount, entity.transform.childCount);
        }

        [UnityTest]
        public IEnumerator ReplaceEvictionToleratesReentrantApplication()
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
            EffectHandle bystanderHandle = default;
            handler.OnEffectRemoved += _ =>
            {
                if (applied)
                {
                    return;
                }

                applied = true;
                bystanderHandle = handler.ApplyEffect(bystander).Value;
            };

            EffectHandle second = handler.ApplyEffect(replaced).Value;

            Assert.IsTrue(applied);
            Assert.AreNotEqual(first.id, second.id);
            Assert.AreEqual(1, handler.GetEffectStackCount(replaced));
            Assert.AreEqual(1, handler.GetEffectStackCount(bystander));
            List<EffectHandle> active = handler.GetActiveEffects();
            Assert.AreEqual(2, active.Count);
            CollectionAssert.Contains(active, second);
            CollectionAssert.Contains(active, bystanderHandle);
            CollectionAssert.DoesNotContain(active, first);
        }

        [UnityTest]
        public IEnumerator CapEvictionToleratesReentrantRemoval()
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
            handler.OnEffectRemoved += _ =>
            {
                if (evicted)
                {
                    return;
                }

                evicted = true;
                handler.RemoveEffect(second);
            };

            EffectHandle third = handler.ApplyEffect(effect).Value;

            Assert.IsTrue(evicted);
            Assert.AreEqual(1, handler.GetEffectStackCount(effect));
            CollectionAssert.AreEqual(new[] { third }, handler.GetActiveEffects());
            Assert.AreNotEqual(first.id, third.id);
            Assert.AreNotEqual(second.id, third.id);
        }

        [UnityTest]
        public IEnumerator ExpirationTeardownSupportsReentrantApplication()
        {
            (
                GameObject entity,
                EffectHandler handler,
                TestAttributesComponent attributes,
                TagHandler tags
            ) = CreateEntity();
            yield return null;

            AttributeEffect expiring = CreateEffect(
                "Expiring",
                e =>
                {
                    e.duration = 0f;
                    e.effectTags.Add(LifecycleTag);
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
            EffectHandle survivorHandle = default;
            handler.OnEffectRemoved += removed =>
            {
                if (applied)
                {
                    return;
                }

                applied = true;
                handler.RemoveEffect(removed);
                survivorHandle = handler.ApplyEffect(survivor).Value;
            };

            _ = handler.ApplyEffect(expiring).Value;
            Assert.IsTrue(tags.HasTag(LifecycleTag));

            yield return null;
            yield return null;

            Assert.IsTrue(applied);
            Assert.IsFalse(tags.HasTag(LifecycleTag));
            Assert.IsTrue(tags.HasTag(SecondaryTag));
            Assert.IsFalse(handler.IsEffectActive(expiring));
            CollectionAssert.AreEqual(new[] { survivorHandle }, handler.GetActiveEffects());
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

        // Renting the same pooled type the handler is enumerating is the whole hazard: with the
        // handler's list returned to the LIFO pool mid-traversal, this Get hands back that very
        // instance and the mutation invalidates the suspended enumerator.
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

        private AttributeEffect CreateLifecycleEffect(
            string name,
            string effectTag,
            bool requiresInstance
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
