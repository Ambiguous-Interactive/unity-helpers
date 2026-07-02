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

        [Test]
        public void LifecycleCallbacksProvideContextData()
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

            EffectHandle handle = handler.ApplyEffectForTesting(effect, currentTime: 10f).Value;
            Assert.AreEqual(1, RecordingEffectBehavior.ApplyCount);
            Assert.AreEqual(
                1,
                RecordingEffectBehavior.ApplyContexts.Count,
                "OnApply should fire immediately."
            );

            int tickCount = handler.ProcessBehaviorTicksForTesting(deltaTime: 0.02f);
            Assert.AreEqual(1, tickCount);
            Assert.IsNotEmpty(
                RecordingEffectBehavior.TickContexts,
                "OnTick should run when behavior ticks are processed."
            );

            int periodicTicks = handler.ProcessPeriodicEffectsForTesting(
                currentTime: 10f,
                deltaTime: 0.02f
            );
            Assert.AreEqual(1, periodicTicks);

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
            Assert.AreEqual(0.02f, tickContext.deltaTime);

            RecordingEffectBehavior.PeriodicInvocation periodicInvocation =
                RecordingEffectBehavior.PeriodicInvocations[0];
            Assert.AreSame(handler, periodicInvocation.Context.handler);
            Assert.AreSame(entity, periodicInvocation.Context.Target);
            Assert.AreEqual(effect, periodicInvocation.Context.Effect);
            Assert.AreEqual(0.02f, periodicInvocation.Context.deltaTime);
            Assert.AreSame(periodicDefinition, periodicInvocation.TickContext.definition);
            Assert.AreEqual(1, periodicInvocation.TickContext.executedTicks);
            Assert.AreEqual(10f, periodicInvocation.TickContext.currentTime);

            EffectBehaviorContext removeContext = RecordingEffectBehavior.RemoveContexts[0];
            Assert.AreSame(handler, removeContext.handler);
            Assert.AreSame(entity, removeContext.Target);
            Assert.AreEqual(effect, removeContext.Effect);
            Assert.AreEqual(0f, removeContext.deltaTime);
        }

        [Test]
        public void PeriodicTickContextTracksExecutedTicksAndTime()
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

            EffectHandle handle = handler.ApplyEffectForTesting(effect, currentTime: 20f).Value;

            int ticks = handler.ProcessPeriodicEffectsForTesting(
                currentTime: 20.11f,
                deltaTime: 0.11f
            );
            Assert.AreEqual(3, ticks);

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
                Assert.AreEqual(0.11f, invocation.Context.deltaTime);

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

        [UnityTest]
        public IEnumerator UpdateProcessesBehaviorAndPeriodicTicks()
        {
            (_, EffectHandler handler, _, _) = CreateEntity();

            PeriodicEffectDefinition periodicDefinition = new()
            {
                name = "Update Pulse",
                initialDelay = 0f,
                interval = 0.05f,
                maxTicks = 1,
            };

            AttributeEffect effect = CreateEffect(
                "Update Lifecycle",
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
            Assert.AreEqual(0, RecordingEffectBehavior.TickCount);
            Assert.AreEqual(0, RecordingEffectBehavior.PeriodicTickCount);

            yield return null;

            Assert.Greater(
                RecordingEffectBehavior.TickCount,
                0,
                "Update should process behavior ticks without a direct test seam call."
            );
            Assert.AreEqual(
                1,
                RecordingEffectBehavior.PeriodicTickCount,
                "Update should process the zero-delay periodic tick without a real-time wait."
            );

            handler.RemoveEffect(handle);
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
