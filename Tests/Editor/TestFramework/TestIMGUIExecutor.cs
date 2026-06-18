// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.EditorFramework
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Reflection;
    using System.Runtime.ExceptionServices;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    // Executes an IMGUI Action inside a valid OnGUI context WITHOUT creating a real
    // EditorWindow / OS window.
    //
    // The previous implementation called EditorWindow.ShowUtility() and pumped editor
    // frames until a Repaint arrived. On non-interactive CI runners (a session-0 service
    // or a disconnected RDP session) the host view's D3D swapchain HWND cannot be created,
    // and that fails CATASTROPHICALLY and DIFFERENTLY per Unity version:
    //   * 2021.3 / 2022.3: a native "[Assert] Assertion failed on expression: 'SUCCEEDED(hr)'"
    //     log -- and the Unity Test Framework fails the test on ANY unexpected log, so EVERY
    //     IMGUI test (~440 across the suite) failed.
    //   * Unity 6 (6000.x): a hard editor CRASH in D3D12SwapChain::CreateHWND, which produces
    //     no results.xml and loses the ENTIRE editmode leg.
    // A non-null graphics device does NOT prevent either failure (the device exists; the
    // top-level WINDOW is what cannot be created), so the old graphicsDeviceType==Null guard
    // could not help, and there is no reliable API to detect an interactive desktop session.
    //
    // This drives an OFFSCREEN UIElements Panel + IMGUIContainer instead. A standalone
    // IMGUIContainer (useOwnerObjectGUIState == false) opens the native GUI context via
    // GUIUtility.BeginContainer(ObjectGUIState), which does not depend on a GUIView/window or
    // a render device -- the panel is built from a throwaway ScriptableObject via the internal
    // Panel.CreateEditorPanel, and the IMGUI handler is driven synchronously by Layout then
    // Repaint events through IMGUIContainer.HandleIMGUIEvent. No window, no swapchain, no GPU
    // render surface: it can never crash, and it emits no benign window-init logs, so full
    // unexpected-log strictness is retained. The internal members are reflection-bound and
    // their signatures are stable across 2021.3 / 2022.3 / 6000.x; if a future Unity renames
    // one, Run throws a precise, non-crashing diagnostic naming the missing member.
    //
    // UNH-SUPPRESS UNH003: this is IMGUI test INFRASTRUCTURE (the executor that hosts other
    // fixtures' OnGUI), not a test fixture, so it does not inherit CommonTestBase.
    internal static class TestIMGUIExecutor
    {
        internal static IEnumerator Run(Action action)
        {
            return Run(action, TestIMGUIExecutorBudget.Default);
        }

        // Execution is synchronous (no waiting on a real Repaint), so the whole pass runs on
        // the first MoveNext and any failure surfaces there -- which is exactly what the
        // [UnityTest]/Assert.Throws callers expect. The method stays an iterator so the public
        // IEnumerator contract (and the 380+ `yield return TestIMGUIExecutor.Run(...)` call
        // sites) is unchanged.
        internal static IEnumerator Run(Action action, TestIMGUIExecutorBudget budget)
        {
            if (action != null)
            {
                Execute(
                    action,
                    budget,
                    CreateLayoutRepaintEvents(),
                    overrideMouseDownPosition: false,
                    mouseDownPosition: default
                );
            }

            yield break;
        }

        internal static IEnumerator RunMouseDown(Action action, Vector2 mousePosition)
        {
            return RunMouseDown(action, mousePosition, TestIMGUIExecutorBudget.Default);
        }

        internal static IEnumerator RunMouseDown(
            Action action,
            Vector2 mousePosition,
            TestIMGUIExecutorBudget budget
        )
        {
            if (action != null)
            {
                Execute(
                    action,
                    budget,
                    CreateLayoutMouseDownRepaintEvents(mousePosition),
                    overrideMouseDownPosition: true,
                    mouseDownPosition: mousePosition
                );
            }

            yield break;
        }

        private static void Execute(
            Action action,
            TestIMGUIExecutorBudget budget,
            Event[] events,
            bool overrideMouseDownPosition,
            Vector2 mouseDownPosition
        )
        {
            if (!TryResolveMechanism(out string resolveError))
            {
                throw new InvalidOperationException(
                    "TestIMGUIExecutor could not resolve the window-free IMGUI mechanism on Unity "
                        + Application.unityVersion
                        + " ("
                        + resolveError
                        + "). The internal UIElements API likely changed; update the reflection "
                        + "targets in TestIMGUIExecutor."
                );
            }

            // Capture the action's own exception (a genuine test failure) so it propagates with
            // its original stack even if IMGUIContainer swallows or wraps exceptions thrown from
            // the OnGUI handler.
            Exception actionError = null;
            bool handlerRan = false;
            Action wrapped = () =>
            {
                handlerRan = true;
                try
                {
                    Event currentEvent = Event.current;
                    if (
                        overrideMouseDownPosition
                        && currentEvent != null
                        && (
                            currentEvent.type == EventType.MouseDown
                            || currentEvent.rawType == EventType.MouseDown
                        )
                    )
                    {
                        currentEvent.mousePosition = mouseDownPosition;
                    }

                    action();
                }
                catch (Exception e)
                {
                    actionError = e;
                    throw;
                }
            };

            // The panel owner is internal IMGUI-pump infrastructure, not a test-tracked object;
            // it is explicitly destroyed in the finally below.
            ScriptableObject owner = ScriptableObject.CreateInstance<ScriptableObject>(); // UNH-SUPPRESS UNH002
            owner.hideFlags = HideFlags.HideAndDontSave;
            object panel = null;
            IMGUIContainer container = null;
            try
            {
                // Fill the owner and Type.Missing for any optional trailing parameters so the
                // call works whether CreateEditorPanel takes just (ScriptableObject) or has
                // gained optional siblings in a newer Unity.
                ParameterInfo[] panelParams = _createEditorPanel.GetParameters();
                object[] panelArgs = new object[panelParams.Length];
                panelArgs[0] = owner;
                for (int i = 1; i < panelArgs.Length; i++)
                {
                    panelArgs[i] = Type.Missing;
                }

                panel = _createEditorPanel.Invoke(
                    null,
                    BindingFlags.OptionalParamBinding | BindingFlags.InvokeMethod,
                    null,
                    panelArgs,
                    null
                );
                if (panel == null)
                {
                    throw new InvalidOperationException(
                        "Panel.CreateEditorPanel returned null on Unity "
                            + Application.unityVersion
                            + "."
                    );
                }

                // The same wrapped handler is the container's stored onGUIHandler AND the handler
                // passed to HandleIMGUIEvent below: the explicit-handler overload runs the passed
                // one, and if the panel ever drives an internal repaint it runs the stored one --
                // either way it is the same idempotent action (drawer render + asserts), exactly
                // as the previous multi-frame executor already invoked it more than once per Run.
                container = new IMGUIContainer(wrapped);
                // A finite layout rect so GUIClip / layout math inside the handler is valid.
                container.style.width = 400f;
                container.style.height = 300f;

                VisualElement visualTree = (VisualElement)_visualTreeProperty.GetValue(panel);
                visualTree.Add(container);
                _validateLayout.Invoke(panel, null);

                // Layout then Repaint is the standard IMGUI contract a drawer's OnGUI expects.
                // Focused interaction tests may insert a single MouseDown between those phases.
                // A healthy run never approaches the budget; the budget only bounds a pathological pump.
                int passes = 0;
                double start = EditorApplication.timeSinceStartup;
                foreach (Event evt in events)
                {
                    double elapsed = EditorApplication.timeSinceStartup - start;
                    if (passes >= budget.MaxFrames || elapsed >= budget.MaxSeconds)
                    {
                        throw new TestIMGUIExecutorTimeoutException(passes, elapsed, budget);
                    }

                    try
                    {
                        _handleIMGUIEvent.Invoke(container, new object[] { evt, wrapped, true });
                    }
                    catch (TargetInvocationException tie)
                    {
                        // If the action itself threw we captured it in actionError and rethrow it
                        // below with a faithful stack; anything else is a mechanism failure.
                        if (actionError == null)
                        {
                            throw new InvalidOperationException(
                                "TestIMGUIExecutor's IMGUI event pump failed on Unity "
                                    + Application.unityVersion
                                    + ".",
                                tie.InnerException ?? tie
                            );
                        }
                    }

                    passes++;
                    if (actionError != null)
                    {
                        break;
                    }
                }

                if (actionError != null)
                {
                    ExceptionDispatchInfo.Capture(actionError).Throw();
                }

                if (!handlerRan)
                {
                    throw new InvalidOperationException(
                        "TestIMGUIExecutor pumped Layout+Repaint but the IMGUI handler never ran "
                            + "(the offscreen panel produced no OnGUI pass) on Unity "
                            + Application.unityVersion
                            + "."
                    );
                }
            }
            finally
            {
                try
                {
                    container?.RemoveFromHierarchy();
                }
                catch
                {
                    // Best-effort teardown; never mask the real result.
                }

                try
                {
                    (panel as IDisposable)?.Dispose();
                }
                catch
                {
                    // Best-effort teardown; never mask the real result.
                }

                if (owner != null)
                {
                    // Deterministic teardown of the pump's own throwaway owner (HideAndDontSave);
                    // it is never a test-tracked object.
                    UnityEngine.Object.DestroyImmediate(owner); // UNH-SUPPRESS UNH001
                }
            }
        }

        private static Event[] CreateLayoutRepaintEvents()
        {
            return new[]
            {
                new Event { type = EventType.Layout },
                new Event { type = EventType.Repaint },
            };
        }

        private static Event[] CreateLayoutMouseDownRepaintEvents(Vector2 mousePosition)
        {
            return new[]
            {
                new Event { type = EventType.Layout },
                new Event
                {
                    type = EventType.MouseDown,
                    mousePosition = mousePosition,
                    button = 0,
                    clickCount = 1,
                },
                new Event { type = EventType.Repaint },
            };
        }

        // ---- Reflection mechanism (resolved once) ----------------------------------------
        // Every target below is stable across 2021.3 / 2022.3 / 6000.x. The Panel type itself
        // is internal, so all of its members are reflected; IMGUIContainer's ctor / styling /
        // RemoveFromHierarchy are public and only HandleIMGUIEvent is internal.
        private static bool _resolved;
        private static bool _available;
        private static string _resolveError;
        private static MethodInfo _createEditorPanel;
        private static PropertyInfo _visualTreeProperty;
        private static MethodInfo _validateLayout;
        private static MethodInfo _handleIMGUIEvent;

        private static bool TryResolveMechanism(out string error)
        {
            if (!_resolved)
            {
                _resolved = true;
                _available = Resolve(out _resolveError);
            }

            error = _resolveError;
            return _available;
        }

        private static bool Resolve(out string error)
        {
            error = null;

            Type panelType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.Panel");
            if (panelType == null)
            {
                error = "type UnityEngine.UIElements.Panel not found";
                return false;
            }

            // CreateEditorPanel(ScriptableObject) -- pick the static overload satisfiable with
            // only the owner (across versions it has gained sibling overloads / optional params).
            _createEditorPanel = SelectOwnerOnlyMethod(
                panelType,
                "CreateEditorPanel",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (_createEditorPanel == null)
            {
                error = "Panel.CreateEditorPanel(ScriptableObject) not found";
                return false;
            }

            _visualTreeProperty = panelType.GetProperty(
                "visualTree",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (_visualTreeProperty == null)
            {
                error = "Panel.visualTree not found";
                return false;
            }

            _validateLayout = FindMethodUpHierarchy(panelType, "ValidateLayout", Type.EmptyTypes);
            if (_validateLayout == null)
            {
                error = "Panel.ValidateLayout() not found";
                return false;
            }

            _handleIMGUIEvent = typeof(IMGUIContainer).GetMethod(
                "HandleIMGUIEvent",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Event), typeof(Action), typeof(bool) },
                null
            );
            if (_handleIMGUIEvent == null)
            {
                error = "IMGUIContainer.HandleIMGUIEvent(Event, Action, bool) not found";
                return false;
            }

            return true;
        }

        // Finds a static method whose single required parameter accepts a ScriptableObject and
        // whose remaining parameters (if any) are all optional, so it can be invoked with just
        // the owner via OptionalParamBinding.
        private static MethodInfo SelectOwnerOnlyMethod(Type type, string name, BindingFlags flags)
        {
            MethodInfo best = null;
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.Name != name)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0)
                {
                    continue;
                }

                if (!parameters[0].ParameterType.IsAssignableFrom(typeof(ScriptableObject)))
                {
                    continue;
                }

                bool restOptional = true;
                for (int i = 1; i < parameters.Length; i++)
                {
                    if (!parameters[i].IsOptional)
                    {
                        restOptional = false;
                        break;
                    }
                }

                if (!restOptional)
                {
                    continue;
                }

                // Prefer the simplest (fewest-parameter) overload.
                if (best == null || parameters.Length < best.GetParameters().Length)
                {
                    best = method;
                }
            }

            return best;
        }

        private static MethodInfo FindMethodUpHierarchy(Type type, string name, Type[] paramTypes)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    paramTypes,
                    null
                );
                if (method != null)
                {
                    return method;
                }
            }

            return null;
        }
    }
#endif
}
