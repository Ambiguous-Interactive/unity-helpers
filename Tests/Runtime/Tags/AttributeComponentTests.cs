// MIT License - Copyright (c) 2025 wallstop
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

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AttributesComponentTests : TagsTestBase
    {
        private static IEnumerable<AttributeModification> EnumerateModifications(
            IEnumerable<AttributeModification> modifications
        )
        {
            foreach (AttributeModification modification in modifications)
            {
                yield return modification;
            }
        }

        [SetUp]
        public void SetUp()
        {
            ResetEffectHandleId();
        }

        [UnityTest]
        public IEnumerator ApplyAttributeModificationsWithoutHandleUpdatesBaseValue()
        {
            GameObject entity = CreateTrackedGameObject(
                "Attributes",
                typeof(TestAttributesComponent)
            );
            TestAttributesComponent component = entity.GetComponent<TestAttributesComponent>();

            AttributeModification modification = new()
            {
                attribute = nameof(TestAttributesComponent.health),
                action = ModificationAction.Addition,
                value = 10f,
            };

            component.ApplyAttributeModifications(new[] { modification }, null);
            Assert.AreEqual(110f, component.health.CurrentValue);
            Assert.AreEqual(1, component.notifications.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ForceApplyAttributeModificationsAppliesOncePerHandle()
        {
            GameObject entity = CreateTrackedGameObject(
                "Attributes",
                typeof(TestAttributesComponent)
            );
            TestAttributesComponent component = entity.GetComponent<TestAttributesComponent>();

            AttributeEffect effect = CreateEffect(
                "Buff",
                e =>
                {
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
            EffectHandle handle = EffectHandle.CreateInstance(effect);

            component.ForceApplyAttributeModifications(handle);
            Assert.AreEqual(105f, component.health.CurrentValue);
            Assert.AreEqual(1, component.notifications.Count);

            component.notifications.Clear();
            component.ForceApplyAttributeModifications(handle);
            Assert.IsEmpty(component.notifications);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ForceRemoveAttributeModificationsRestoresValue()
        {
            GameObject entity = CreateTrackedGameObject(
                "Attributes",
                typeof(TestAttributesComponent)
            );
            TestAttributesComponent component = entity.GetComponent<TestAttributesComponent>();

            AttributeEffect effect = CreateEffect(
                "Buff",
                e =>
                {
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = nameof(TestAttributesComponent.health),
                            action = ModificationAction.Multiplication,
                            value = 2f,
                        }
                    );
                }
            );
            EffectHandle handle = EffectHandle.CreateInstance(effect);

            component.ForceApplyAttributeModifications(handle);
            component.notifications.Clear();
            component.ForceRemoveAttributeModifications(handle);

            Assert.AreEqual(100f, component.health.CurrentValue);
            Assert.AreEqual(1, component.notifications.Count);
            Assert.AreEqual(
                nameof(TestAttributesComponent.health),
                component.notifications[0].attribute
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator ApplyAttributeModificationsWithHandleDelegatesToForce()
        {
            GameObject entity = CreateTrackedGameObject(
                "Attributes",
                typeof(TestAttributesComponent)
            );
            TestAttributesComponent component = entity.GetComponent<TestAttributesComponent>();

            AttributeEffect effect = CreateEffect(
                "Buff",
                e =>
                {
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
            EffectHandle handle = EffectHandle.CreateInstance(effect);

            component.ApplyAttributeModifications(effect.modifications, handle);
            Assert.AreEqual(105f, component.health.CurrentValue);
            yield return null;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RemovingAnAttributeHandleNotifiesOnlyOnce(bool recursive)
        {
            GameObject entity = CreateTrackedGameObject(
                nameof(RemovingAnAttributeHandleNotifiesOnlyOnce),
                typeof(TestAttributesComponent)
            );
            TestAttributesComponent component = entity.GetComponent<TestAttributesComponent>();
            AttributeEffect effect = CreateEffect(
                nameof(RemovingAnAttributeHandleNotifiesOnlyOnce),
                e =>
                {
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
            EffectHandle handle = EffectHandle.CreateInstance(effect);
            component.ForceApplyAttributeModifications(handle);
            component.notifications.Clear();
            int removalNotifications = 0;
            component.OnAttributeModified += (_, _, _) =>
            {
                ++removalNotifications;
                if (recursive && removalNotifications < 5)
                {
                    component.ForceRemoveAttributeModifications(handle);
                }
            };

            component.ForceRemoveAttributeModifications(handle);
            component.ForceRemoveAttributeModifications(handle);

            Assert.AreEqual(1, removalNotifications);
            Assert.AreEqual(1, component.notifications.Count);
            Assert.AreEqual(100f, component.health.CurrentValue);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AttributeRemovalFinishesBeforeNotifyingAndPreservesReapplication(bool reapply)
        {
            GameObject entity = CreateTrackedGameObject(
                nameof(AttributeRemovalFinishesBeforeNotifyingAndPreservesReapplication),
                typeof(TestAttributesComponent)
            );
            TestAttributesComponent component = entity.GetComponent<TestAttributesComponent>();
            AttributeEffect effect = CreateEffect(
                nameof(AttributeRemovalFinishesBeforeNotifyingAndPreservesReapplication),
                e =>
                {
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
            EffectHandle handle = EffectHandle.CreateInstance(effect);
            component.ForceApplyAttributeModifications(handle);
            bool observedRemoval = false;
            float healthAtFirstRemoval = float.NaN;
            float armorAtFirstRemoval = float.NaN;
            component.OnAttributeModified += (_, _, _) =>
            {
                if (observedRemoval)
                {
                    return;
                }
                observedRemoval = true;
                healthAtFirstRemoval = component.health.CurrentValue;
                armorAtFirstRemoval = component.armor.CurrentValue;
                if (reapply)
                {
                    component.ForceApplyAttributeModifications(handle);
                }
            };

            component.ForceRemoveAttributeModifications(handle);

            Assert.IsTrue(observedRemoval);
            Assert.AreEqual(reapply ? 105f : 100f, component.health.CurrentValue);
            Assert.AreEqual(reapply ? 57f : 50f, component.armor.CurrentValue);
            Assert.AreEqual(100f, healthAtFirstRemoval);
            Assert.AreEqual(50f, armorAtFirstRemoval);
            component.ForceRemoveAttributeModifications(handle);
            Assert.AreEqual(100f, component.health.CurrentValue);
            Assert.AreEqual(50f, component.armor.CurrentValue);
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void PermanentPeriodicModificationsRespectCancellationAcrossEnumerableShapes(
            bool useIterator,
            bool cancel,
            bool includeUnknownAttribute
        )
        {
            GameObject entity = CreateTrackedGameObject(
                nameof(PermanentPeriodicModificationsRespectCancellationAcrossEnumerableShapes),
                typeof(TestAttributesComponent)
            );
            TestAttributesComponent component = entity.GetComponent<TestAttributesComponent>();
            EffectHandler handler = entity.GetComponent<EffectHandler>();
            AttributeEffect effect = CreateEffect(
                nameof(PermanentPeriodicModificationsRespectCancellationAcrossEnumerableShapes),
                e => e.durationType = ModifierDurationType.Infinite
            );
            EffectHandle handle = handler.ApplyEffect(effect).Value;
            List<AttributeModification> modifications = new()
            {
                new()
                {
                    attribute = nameof(TestAttributesComponent.health),
                    action = ModificationAction.Addition,
                    value = 5f,
                },
                new()
                {
                    attribute = nameof(TestAttributesComponent.armor),
                    action = ModificationAction.Addition,
                    value = 7f,
                },
                new()
                {
                    attribute = nameof(TestAttributesComponent.health),
                    action = ModificationAction.Addition,
                    value = 11f,
                },
            };
            if (includeUnknownAttribute)
            {
                modifications.Insert(
                    0,
                    new AttributeModification
                    {
                        attribute = nameof(
                            PermanentPeriodicModificationsRespectCancellationAcrossEnumerableShapes
                        ),
                        action = ModificationAction.Addition,
                        value = 100f,
                    }
                );
            }
            component.OnAttributeModified += (attribute, _, _) =>
            {
                if (
                    cancel
                    && string.Equals(
                        attribute,
                        nameof(TestAttributesComponent.health),
                        StringComparison.Ordinal
                    )
                )
                {
                    handler.RemoveEffect(handle);
                }
            };
            IEnumerable<AttributeModification> source = useIterator
                ? EnumerateModifications(modifications)
                : modifications;

            component.ApplyPeriodicAttributeModifications(source, handle);

            Assert.AreEqual(cancel ? 105f : 116f, component.health.CurrentValue);
            Assert.AreEqual(cancel ? 50f : 57f, component.armor.CurrentValue);
            Assert.AreEqual(cancel ? 1 : 3, component.notifications.Count);
            Assert.AreEqual(!cancel, handler.IsEffectActive(effect));
        }

        [UnityTest]
        public IEnumerator ForceApplyAttributeModificationsSkipsUnknownAttributes()
        {
            GameObject entity = CreateTrackedGameObject(
                "Attributes",
                typeof(TestAttributesComponent)
            );
            TestAttributesComponent component = entity.GetComponent<TestAttributesComponent>();

            AttributeEffect effect = CreateEffect(
                "Buff",
                e =>
                {
                    e.modifications.Add(
                        new AttributeModification
                        {
                            attribute = "missing",
                            action = ModificationAction.Addition,
                            value = 5f,
                        }
                    );
                }
            );
            EffectHandle handle = EffectHandle.CreateInstance(effect);

            component.ForceApplyAttributeModifications(handle);
            Assert.IsEmpty(component.notifications);
            yield return null;
        }

        /// <summary>
        /// A default handle carries no effect, and these are public entry points -- reaching the
        /// modification list through it must answer "nothing to do" rather than throw.
        /// </summary>
        [UnityTest]
        public IEnumerator ForceModificationEntryPointsIgnoreAMissingEffect()
        {
            GameObject entity = CreateTrackedGameObject(
                "Attributes",
                typeof(TestAttributesComponent)
            );
            TestAttributesComponent component = entity.GetComponent<TestAttributesComponent>();

            EffectHandle handleWithoutEffect = default;
            Assert.DoesNotThrow(() =>
                component.ForceApplyAttributeModifications(handleWithoutEffect)
            );
            Assert.DoesNotThrow(() =>
                component.ForceRemoveAttributeModifications(handleWithoutEffect)
            );
            Assert.DoesNotThrow(() =>
                component.ForceApplyAttributeModifications((AttributeEffect)null)
            );
            Assert.DoesNotThrow(() =>
                component.ApplyAttributeModifications(null, handleWithoutEffect)
            );

            Assert.AreEqual(100f, component.health.CurrentValue);
            Assert.IsEmpty(component.notifications);
            yield return null;
        }
    }
}
