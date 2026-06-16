// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.EditorFramework
{
#if UNITY_EDITOR
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    [TestFixture]
    public sealed class TestIMGUIExecutorTests
    {
        [UnityTest]
        public IEnumerator RunInvokesActionAndCompletesWithinBudget()
        {
            bool actionRan = false;
            yield return TestIMGUIExecutor.Run(() => actionRan = true);
            Assert.IsTrue(
                actionRan,
                "TestIMGUIExecutor.Run should invoke the action and complete on a repainting editor."
            );
        }

        [Test]
        public void RunThrowsWhenFrameBudgetExhausted()
        {
            IEnumerator runner = TestIMGUIExecutor.Run(
                () => { },
                TestIMGUIExecutorBudget.WithFrames(0)
            );
            Assert.Throws<TestIMGUIExecutorTimeoutException>(() => DrainEnumerator(runner));
        }

        [Test]
        public void RunThrowsWhenTimeBudgetExhausted()
        {
            IEnumerator runner = TestIMGUIExecutor.Run(
                () => { },
                TestIMGUIExecutorBudget.WithSeconds(0d)
            );
            Assert.Throws<TestIMGUIExecutorTimeoutException>(() => DrainEnumerator(runner));
        }

        [Test]
        public void RunIgnoresNullActionWithoutThrowing()
        {
            IEnumerator runner = TestIMGUIExecutor.Run(null);
            Assert.DoesNotThrow(() => DrainEnumerator(runner));
        }

        private static void DrainEnumerator(IEnumerator enumerator)
        {
            while (enumerator.MoveNext()) { }
        }
    }
#endif
}
