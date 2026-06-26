// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Tags.Helpers;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class EffectBehaviorTests : TagsTestBase
    {
        [SetUp]
        public void SetUp()
        {
            ResetEffectHandleId();
            RecordingEffectBehavior.ResetForTests();
        }

        [UnityTest]
        public IEnumerator LifecycleCallbacksProvideContextData()
        {
            (GameObject entity, EffectHandler handler, _, _) = CreateEntity();

            PeriodicEffectDefinition periodicDefinition = new()
            {
                name = "Pulse",
                initialDelay = 0f,
                interval = 0.05f,
                maxTicks = 1,
            };

            AttributeEffect effect = CreateEffect(
                "Lifecycle",
                e =>
                {
                    e.periodicEffects.Add(periodicDefinition);
                }
            );

            RecordingEffectBehavior behavior = Track(
                ScriptableObject.CreateInstance<RecordingEffectBehavior>()
            );
            effect.behaviors.Add(behavior);

            EffectHandle handle = handler.ApplyEffect(effect).Value;
            Assert.AreEqual(1, RecordingEffectBehavior.ApplyCount);
            Assert.AreEqual(
                1,
                RecordingEffectBehavior.ApplyContexts.Count,
                "OnApply should fire immediately."
            );

            yield return null;
            yield return null;

            Assert.IsNotEmpty(
                RecordingEffectBehavior.TickContexts,
                "OnTick should run after Update."
            );

            yield return WaitForPeriodicInvocations(
                expectedCount: 1,
                timeout: 0.5f,
                checkpoint: "lifecycle periodic tick"
            );

            Assert.AreEqual(
                1,
                RecordingEffectBehavior.PeriodicInvocations.Count,
                "Expected one periodic tick."
            );

            int removeCountBefore = RecordingEffectBehavior.RemoveCount;
            handler.RemoveEffect(handle);
            Assert.AreEqual(removeCountBefore + 1, RecordingEffectBehavior.RemoveCount);
            Assert.AreEqual(
                1,
                RecordingEffectBehavior.RemoveContexts.Count,
                "OnRemove should fire once."
            );

            EffectBehaviorContext applyContext = RecordingEffectBehavior.ApplyContexts[0];
            Assert.AreSame(handler, applyContext.handler);
            Assert.AreSame(entity, applyContext.Target);
            Assert.AreEqual(effect, applyContext.Effect);
            Assert.AreEqual(0f, applyContext.deltaTime);

            EffectBehaviorContext tickContext = RecordingEffectBehavior.TickContexts[0];
            Assert.AreSame(handler, tickContext.handler);
            Assert.AreSame(entity, tickContext.Target);
            Assert.AreEqual(effect, tickContext.Effect);
            Assert.Greater(tickContext.deltaTime, 0f);

            RecordingEffectBehavior.PeriodicInvocation periodicInvocation =
                RecordingEffectBehavior.PeriodicInvocations[0];
            Assert.AreSame(handler, periodicInvocation.Context.handler);
            Assert.AreSame(entity, periodicInvocation.Context.Target);
            Assert.AreEqual(effect, periodicInvocation.Context.Effect);
            Assert.Greater(periodicInvocation.Context.deltaTime, 0f);
            Assert.AreSame(periodicDefinition, periodicInvocation.TickContext.definition);
            Assert.AreEqual(1, periodicInvocation.TickContext.executedTicks);
            Assert.GreaterOrEqual(periodicInvocation.TickContext.currentTime, 0f);

            EffectBehaviorContext removeContext = RecordingEffectBehavior.RemoveContexts[0];
            Assert.AreSame(handler, removeContext.handler);
            Assert.AreSame(entity, removeContext.Target);
            Assert.AreEqual(effect, removeContext.Effect);
            Assert.AreEqual(0f, removeContext.deltaTime);
        }

        [UnityTest]
        public IEnumerator PeriodicTickContextTracksExecutedTicksAndTime()
        {
            (GameObject entity, EffectHandler handler, _, _) = CreateEntity();

            PeriodicEffectDefinition periodicDefinition = new()
            {
                name = "Stacking Pulse",
                initialDelay = 0f,
                interval = 0.05f,
                maxTicks = 3,
            };

            AttributeEffect effect = CreateEffect(
                "Periodic",
                e =>
                {
                    e.periodicEffects.Add(periodicDefinition);
                }
            );

            RecordingEffectBehavior behavior = Track(
                ScriptableObject.CreateInstance<RecordingEffectBehavior>()
            );
            effect.behaviors.Add(behavior);

            EffectHandle handle = handler.ApplyEffect(effect).Value;

            yield return WaitForPeriodicInvocations(
                expectedCount: 3,
                timeout: 0.75f,
                checkpoint: "three periodic behavior callbacks"
            );

            Assert.AreEqual(
                3,
                RecordingEffectBehavior.PeriodicInvocations.Count,
                "Expected periodic callbacks for each executed tick."
            );

            for (int i = 0; i < RecordingEffectBehavior.PeriodicInvocations.Count; ++i)
            {
                RecordingEffectBehavior.PeriodicInvocation invocation =
                    RecordingEffectBehavior.PeriodicInvocations[i];
                Assert.AreSame(periodicDefinition, invocation.TickContext.definition);
                Assert.AreEqual(i + 1, invocation.TickContext.executedTicks);
                Assert.Greater(invocation.Context.deltaTime, 0f);

                if (i > 0)
                {
                    float previousTime = RecordingEffectBehavior
                        .PeriodicInvocations[i - 1]
                        .TickContext
                        .currentTime;
                    Assert.GreaterOrEqual(invocation.TickContext.currentTime, previousTime);
                }
            }

            handler.RemoveEffect(handle);
        }

        private static IEnumerator WaitForPeriodicInvocations(
            int expectedCount,
            float timeout,
            string checkpoint
        )
        {
            float elapsed = 0f;
            while (
                RecordingEffectBehavior.PeriodicInvocations.Count < expectedCount
                && elapsed < timeout
            )
            {
                yield return null;
                elapsed += Time.deltaTime;
            }

            Assert.That(
                RecordingEffectBehavior.PeriodicInvocations.Count,
                Is.GreaterThanOrEqualTo(expectedCount),
                $"{checkpoint} not reached within {timeout:F2}s (saw {RecordingEffectBehavior.PeriodicInvocations.Count})."
            );
        }

        private (
            GameObject entity,
            EffectHandler handler,
            TestAttributesComponent attributes,
            TagHandler tags
        ) CreateEntity()
        {
            GameObject entity = CreateTrackedGameObject(
                "EffectBehaviorEntity",
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
