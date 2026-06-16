// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.EditorFramework
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using UnityEditor;
    using UnityEngine;

    internal sealed class TestIMGUIExecutor : EditorWindow
    {
        private Action _action;
        private bool _hasRun;

        internal static IEnumerator Run(Action action)
        {
            return Run(action, TestIMGUIExecutorBudget.Default);
        }

        // The terminating signal is an EditorWindow Repaint event. Under
        // -batchmode -nographics that event can be delayed indefinitely or never
        // arrive (and a flaky Unity Accelerator reconnect can stall each editor
        // tick for minutes), so an un-bounded wait here can hang the WHOLE test
        // run -- which is exactly the 2.5h CI timeout this budget exists to
        // prevent. The budget converts that hang into a fast, descriptive
        // per-test failure; a healthy editor repaints within a few frames so the
        // budget is never reached on the success path.
        internal static IEnumerator Run(Action action, TestIMGUIExecutorBudget budget)
        {
            if (action == null)
            {
                yield break;
            }

            TestIMGUIExecutor window = CreateInstance<TestIMGUIExecutor>();

            // try/finally (not catch -- C# allows yield only with finally) guarantees
            // the window is closed on EVERY exit: normal completion, the budget throw,
            // a throw during show/focus, and enumerator Dispose() when the test runner
            // aborts the coroutine after the action throws inside OnGUI. The
            // action-exception path previously leaked the window.
            try
            {
                window.hideFlags = HideFlags.HideAndDontSave;
                window.minSize = new Vector2(100f, 50f);
                window._action = action;
                window._hasRun = false;
                window.ShowUtility();
                window.Focus();

                int framesPumped = 0;
                double startTime = EditorApplication.timeSinceStartup;
                while (!window._hasRun)
                {
                    double elapsedSeconds = EditorApplication.timeSinceStartup - startTime;
                    if (framesPumped >= budget.MaxFrames || elapsedSeconds >= budget.MaxSeconds)
                    {
                        throw new TestIMGUIExecutorTimeoutException(
                            framesPumped,
                            elapsedSeconds,
                            budget
                        );
                    }

                    window.Repaint();
                    framesPumped++;
                    yield return null;
                }
            }
            finally
            {
                if (window != null)
                {
                    window.Close();
                }
            }
        }

        private void OnGUI()
        {
            if (_action == null)
            {
                return;
            }

            EventType eventType = Event.current.type;
            if (eventType != EventType.Layout && eventType != EventType.Repaint)
            {
                return;
            }

            try
            {
                _action.Invoke();
            }
            catch
            {
                _action = null;
                _hasRun = true;
                throw;
            }

            if (eventType == EventType.Repaint)
            {
                _action = null;
                _hasRun = true;
            }
            else
            {
                Repaint();
            }
        }
    }
#endif
}
