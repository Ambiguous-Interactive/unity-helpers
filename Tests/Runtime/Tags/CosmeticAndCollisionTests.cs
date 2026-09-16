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
    public sealed class CosmeticEffectComponentTests : TagsTestBase
    {
        [Test]
        public void OnApplyAndRemoveEffectMaintainTargetList()
        {
            GameObject cosmetic = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            SpyCosmeticComponent.ResetForTests();
            SpyCosmeticComponent component = cosmetic.AddComponent<SpyCosmeticComponent>();
            GameObject target = CreateTrackedGameObject("Target");

            component.OnApplyEffect(target);
            Assert.AreEqual(1, component.AppliedCount);

            component.OnRemoveEffect(target);
            Assert.AreEqual(0, component.AppliedCount);
        }

        [Test]
        public void OnDestroyInvokesRemoveEffectForTrackedTargets()
        {
            GameObject cosmetic = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            SpyCosmeticComponent.ResetForTests();
            SpyCosmeticComponent component = cosmetic.AddComponent<SpyCosmeticComponent>();
            GameObject target = CreateTrackedGameObject("Target");

            component.OnApplyEffect(target);
            Object.DestroyImmediate(cosmetic); // UNH-SUPPRESS: Test verifies OnDestroy callback
            Assert.AreEqual(1, SpyCosmeticComponent.RemoveInvocationCount);
        }

        [Test]
        public void OnDestroyKeepsTargetSnapshotWhenRemovalMutatesTargets()
        {
            GameObject cosmetic = CreateTrackedGameObject("Cosmetic", typeof(CosmeticEffectData));
            ReentrantCosmeticComponent.ResetForTests();
            ReentrantCosmeticComponent component =
                cosmetic.AddComponent<ReentrantCosmeticComponent>();
            GameObject first = CreateTrackedGameObject("First");
            GameObject second = CreateTrackedGameObject("Second");
            GameObject third = CreateTrackedGameObject("Third");
            component.OnApplyEffect(first);
            component.OnApplyEffect(second);
            component.OnApplyEffect(third);
            ReentrantCosmeticComponent.RemoveHook = target =>
            {
                if (target == first)
                {
                    component.OnRemoveEffect(second);
                }
            };

            try
            {
                Object.DestroyImmediate(cosmetic); // UNH-SUPPRESS: Test verifies OnDestroy callback
                Assert.AreEqual(4, ReentrantCosmeticComponent.RemovedCount);
            }
            finally
            {
                ReentrantCosmeticComponent.ResetForTests();
            }
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CollisionSensesTests : TagsTestBase
    {
        [UnityTest]
        public IEnumerator ApplyingDisableTagDisablesChildColliders()
        {
            GameObject entity = CreateTrackedGameObject(
                "Entity",
                typeof(TagHandler),
                typeof(CollisionSenses)
            );
            GameObject child = CreateTrackedGameObject("Collider", typeof(BoxCollider2D));
            child.transform.SetParent(entity.transform);
            BoxCollider2D collider = child.GetComponent<BoxCollider2D>();
            collider.enabled = true;

            yield return null;

            TagHandler handler = entity.GetComponent<TagHandler>();
            handler.ApplyTag(CollisionSenses.CollisionDisabledTag);
            yield return null;
            Assert.IsFalse(collider.enabled);

            handler.RemoveTag(CollisionSenses.CollisionDisabledTag);
            yield return null;
            Assert.IsTrue(collider.enabled);
        }

        [UnityTest]
        public IEnumerator DisabledCollidersAreIgnored()
        {
            GameObject entity = CreateTrackedGameObject(
                "Entity",
                typeof(TagHandler),
                typeof(CollisionSenses)
            );

            GameObject activeChild = CreateTrackedGameObject("Active", typeof(BoxCollider2D));
            activeChild.transform.SetParent(entity.transform);
            BoxCollider2D active = activeChild.GetComponent<BoxCollider2D>();
            active.enabled = true;

            GameObject disabledChild = CreateTrackedGameObject("Disabled", typeof(BoxCollider2D));
            disabledChild.transform.SetParent(entity.transform);
            BoxCollider2D disabled = disabledChild.GetComponent<BoxCollider2D>();
            disabled.enabled = false;

            yield return null;

            entity.GetComponent<TagHandler>().ApplyTag(CollisionSenses.CollisionDisabledTag);
            yield return null;

            Assert.IsFalse(active.enabled);
            Assert.IsFalse(disabled.enabled);
        }
    }
}
