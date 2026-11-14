// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.EditorFramework
{
#if UNITY_EDITOR
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    [TestFixture]
    public sealed class TestIMGUIExecutorTests
    {
        private static void DrainEnumerator(IEnumerator enumerator)
        {
            while (enumerator.MoveNext()) { }
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void DefaultRunsRestoreIncomingPolicy(
            bool incoming,
            bool mouseDown,
            bool budgetFailure
        )
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = incoming;
            try
            {
                TestIMGUIExecutorBudget budget = budgetFailure
                    ? TestIMGUIExecutorBudget.WithFrames(0)
                    : TestIMGUIExecutorBudget.Default;
                IEnumerator runner = mouseDown
                    ? TestIMGUIExecutor.RunMouseDown(() => { }, Vector2.zero, budget)
                    : TestIMGUIExecutor.Run(() => { }, budget);
                if (budgetFailure)
                {
                    Assert.Throws<TestIMGUIExecutorTimeoutException>(() => DrainEnumerator(runner));
                }
                else
                {
                    DrainEnumerator(runner);
                }
                Assert.That(LogAssert.ignoreFailingMessages, Is.EqualTo(incoming));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InitializeEditorStylesPreservesIncomingLogPolicy(bool incoming)
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = incoming;
            try
            {
                TestIMGUIExecutor.InitializeEditorStyles();
                Assert.That(LogAssert.ignoreFailingMessages, Is.EqualTo(incoming));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StrictRunRestoresPolicyWhenTheBudgetFails(bool incoming)
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = incoming;
            try
            {
                IEnumerator runner = TestIMGUIExecutor.Run(
                    () => { },
                    TestIMGUIExecutorBudget.WithFrames(0),
                    strictLogs: true
                );
                Assert.Throws<TestIMGUIExecutorTimeoutException>(() => DrainEnumerator(runner));
                Assert.That(LogAssert.ignoreFailingMessages, Is.EqualTo(incoming));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DefaultRunsExposeUnexpectedErrors(bool mouseDown)
        {
            const string probe = "default-log-policy-probe";
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = false;
            bool emitted = false;
            try
            {
                System.Action action = () =>
                {
                    if (!emitted)
                    {
                        emitted = true;
                        Debug.LogError(probe);
                    }
                };
                IEnumerator runner = mouseDown
                    ? TestIMGUIExecutor.RunMouseDown(action, Vector2.zero)
                    : TestIMGUIExecutor.Run(action);
                DrainEnumerator(runner);
                Assert.That(emitted, Is.True);
                Assert.That(LogAssert.ignoreFailingMessages, Is.False);
                Assert.Catch<System.Exception>(() => LogAssert.NoUnexpectedReceived());
                LogAssert.Expect(LogType.Error, probe);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        [TestCase("strict-log-policy-probe")]
        [TestCase(TestIMGUIExecutor.OffscreenCursorRectError)]
        public void StrictRunExposesUnexpectedErrors(string probe)
        {
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = false;
            bool emitted = false;
            try
            {
                IEnumerator runner = TestIMGUIExecutor.Run(
                    () =>
                    {
                        if (!emitted)
                        {
                            emitted = true;
                            Debug.LogError(probe);
                        }
                    },
                    TestIMGUIExecutorBudget.Default,
                    strictLogs: true
                );
                DrainEnumerator(runner);
                Assert.That(emitted, Is.True);
                Assert.That(LogAssert.ignoreFailingMessages, Is.False);
                Assert.Catch<System.Exception>(() => LogAssert.NoUnexpectedReceived());
                LogAssert.Expect(LogType.Error, probe);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

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
            List<string> receivedEvents = new();
            bool sawLayout = false;
            bool sawMouseDown = false;
            bool sawRepaint = false;
            Vector2 mousePosition = new(12f, 34f);

            yield return TestIMGUIExecutor.RunMouseDown(
                () =>
                {
                    Event currentEvent = Event.current;
                    receivedEvents.Add(
                        $"{currentEvent.type}/{currentEvent.rawType} button={currentEvent.button} position={currentEvent.mousePosition}"
                    );
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

            string eventSnapshot = string.Join(", ", receivedEvents);
            Assert.IsTrue(
                sawLayout,
                "Expected the offscreen pump to run Layout first. Received: " + eventSnapshot
            );
            Assert.IsTrue(
                sawMouseDown,
                "Expected the offscreen pump to run MouseDown. Received: " + eventSnapshot
            );
            Assert.IsTrue(
                sawRepaint,
                "Expected the offscreen pump to run Repaint last. Received: " + eventSnapshot
            );
        }
    }
#endif
}
