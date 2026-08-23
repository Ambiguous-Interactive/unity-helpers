// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEditor.Animations;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Covers <see cref="AnimatorExtensions.ResetTriggers"/> against a real controller, which needs
    /// <c>UnityEditor.Animations</c> to build and so cannot live in the runtime suite.
    /// </summary>
    /// <remarks>
    /// <c>Animator.parameters</c> builds a new array and new element objects on every read --
    /// 80.4 bytes per call for a three-parameter controller on Unity 6000.4.6f1, against a control
    /// that moved 9.6 MB -- so the hashes are read once per controller. These fixtures pin the
    /// behaviour that caching must not change, the second call in particular.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AnimatorExtensionsEditorTests : CommonTestBase
    {
        [Test]
        public void ResetTriggersClearsEveryTriggerAndLeavesOtherParametersAlone()
        {
            Animator animator = BuildAnimator();

            animator.SetTrigger("Jump");
            animator.SetTrigger("Land");
            animator.SetBool("Grounded", true);
            animator.SetFloat("Speed", 4.5f);

            Assert.IsTrue(animator.GetBool("Jump"), "a set trigger reads as true before the reset");

            animator.ResetTriggers();

            Assert.IsFalse(animator.GetBool("Jump"));
            Assert.IsFalse(animator.GetBool("Land"));
            Assert.IsTrue(animator.GetBool("Grounded"), "a bool is not a trigger");
            Assert.AreEqual(4.5f, animator.GetFloat("Speed"), 0.0001f);
        }

        /// <summary>
        /// The second call is the one the cache serves, so it is the one worth asserting.
        /// </summary>
        [Test]
        public void ResetTriggersKeepsWorkingOnEveryLaterCall()
        {
            Animator animator = BuildAnimator();

            for (int i = 0; i < 4; ++i)
            {
                animator.SetTrigger("Jump");
                Assert.IsTrue(animator.GetBool("Jump"));

                animator.ResetTriggers();

                Assert.IsFalse(animator.GetBool("Jump"), $"call {i} left the trigger set");
            }
        }

        [Test]
        public void ResetTriggersHandlesAnAnimatorWithNoTriggers()
        {
            GameObject host = Track(
                new GameObject(nameof(ResetTriggersHandlesAnAnimatorWithNoTriggers))
            );
            AnimatorController controller = Track(new AnimatorController { name = "NoTriggers" });
            controller.AddLayer("Base");
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            Animator animator = host.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            Assert.DoesNotThrow(() => animator.ResetTriggers());
            Assert.DoesNotThrow(() => animator.ResetTriggers());
        }

        [Test]
        public void ResetTriggersHandlesAnAnimatorWithNoController()
        {
            GameObject host = Track(
                new GameObject(nameof(ResetTriggersHandlesAnAnimatorWithNoController))
            );
            Animator animator = host.AddComponent<Animator>();

            Assert.IsTrue(animator.runtimeAnimatorController == null);
            Assert.DoesNotThrow(() => animator.ResetTriggers());
        }

        /// <summary>
        /// The cache is keyed on the controller, so swapping one must not answer with the other's
        /// triggers.
        /// </summary>
        [Test]
        public void ResetTriggersFollowsAControllerSwap()
        {
            Animator animator = BuildAnimator();
            animator.ResetTriggers();

            AnimatorController replacement = Track(new AnimatorController { name = "Replacement" });
            replacement.AddLayer("Base");
            replacement.AddParameter("Fire", AnimatorControllerParameterType.Trigger);
            animator.runtimeAnimatorController = replacement;

            animator.SetTrigger("Fire");
            Assert.IsTrue(animator.GetBool("Fire"));

            animator.ResetTriggers();

            Assert.IsFalse(animator.GetBool("Fire"));
        }

        private Animator BuildAnimator()
        {
            GameObject host = Track(new GameObject("AnimatorExtensionsHost"));
            AnimatorController controller = Track(new AnimatorController { name = "Triggers" });
            controller.AddLayer("Base");
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Land", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            Animator animator = host.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            return animator;
        }
    }
#endif
}
