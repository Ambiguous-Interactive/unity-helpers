// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.EditorFramework
{
#if UNITY_EDITOR
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
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

        [UnityTest]
        public IEnumerator RunMouseDownPumpsMouseDownBetweenLayoutAndRepaint()
        {
            bool sawLayout = false;
            bool sawMouseDown = false;
            bool sawRepaint = false;
            Vector2 mousePosition = new(12f, 34f);

            yield return TestIMGUIExecutor.RunMouseDown(
                () =>
                {
                    Event currentEvent = Event.current;
                    if (currentEvent.type == EventType.Layout)
                    {
                        sawLayout = true;
                    }
                    else if (currentEvent.type == EventType.MouseDown)
                    {
                        sawMouseDown = true;
                        Assert.AreEqual(mousePosition, currentEvent.mousePosition);
                        Assert.AreEqual(0, currentEvent.button);
                    }
                    else if (currentEvent.type == EventType.Repaint)
                    {
                        sawRepaint = true;
                    }
                },
                mousePosition
            );

            Assert.IsTrue(sawLayout, "Expected the offscreen pump to run Layout first.");
            Assert.IsTrue(sawMouseDown, "Expected the offscreen pump to run MouseDown.");
            Assert.IsTrue(sawRepaint, "Expected the offscreen pump to run Repaint last.");
        }

        private static void DrainEnumerator(IEnumerator enumerator)
        {
            while (enumerator.MoveNext()) { }
        }
    }
#endif
}
