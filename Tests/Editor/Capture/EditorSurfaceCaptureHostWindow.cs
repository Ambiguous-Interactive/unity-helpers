// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Capture
{
#if UNITY_EDITOR
    using System;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Owns the editor panel used for offscreen capture, without a native popup in batch mode.
    /// </summary>
    internal sealed class EditorSurfaceCaptureHostWindow : EditorWindow
    {
        internal const string HostWindowTitle = "Editor Surface Capture Host";
        private const string CreateEditorPanelMethodName = "CreateEditorPanel";
        private IPanel _ownedPanel;

        /// <summary>
        /// How many hosts are alive right now. Tests assert this returns to zero, because a
        /// window that survives a capture is a leaked native panel, not a harmless object.
        /// </summary>
        internal static int LiveHostCount =>
            Resources.FindObjectsOfTypeAll<EditorSurfaceCaptureHostWindow>().Length;

        internal static EditorSurfaceCaptureHostWindow Create(int canvasWidth, int canvasHeight)
        {
            EditorSurfaceCaptureHostWindow window =
                CreateInstance<EditorSurfaceCaptureHostWindow>();
            window.titleContent = new GUIContent(HostWindowTitle);
            window.hideFlags = HideFlags.HideAndDontSave;
            if (Application.isBatchMode)
            {
                try
                {
                    Type panelType = typeof(VisualElement).Assembly.GetType(
                        "UnityEngine.UIElements.Panel",
                        true
                    );
                    MethodInfo createPanel = panelType.GetMethod(
                        CreateEditorPanelMethodName,
                        BindingFlags.Static | BindingFlags.NonPublic
                    );
                    if (createPanel == null)
                        throw new MissingMethodException(
                            panelType.FullName,
                            CreateEditorPanelMethodName
                        );
                    window._ownedPanel = (IPanel)createPanel.Invoke(null, new object[] { window });
                    VisualElement panelRoot = window._ownedPanel.visualTree;
                    panelRoot.style.width = canvasWidth;
                    panelRoot.style.height = canvasHeight;
                    panelRoot.Add(window.rootVisualElement);
                    return window;
                }
                catch
                {
                    CloseHost(window);
                    throw;
                }
            }
            window.minSize = new Vector2(canvasWidth, canvasHeight);
            window.position = new Rect(0f, 0f, canvasWidth, canvasHeight);
            // A docked tab shares the captured panel; popup mode avoids inseparable window chrome.
            window.ShowPopup();
            window.hideFlags = HideFlags.HideAndDontSave;
            return window;
        }

        /// <summary>
        /// Closes a host window and never throws from teardown.
        /// <see cref="EditorWindow.Close"/> dereferences the parent host view unconditionally, so
        /// a host that failed before it was shown is destroyed instead of closed.
        /// </summary>
        internal static void CloseHost(EditorSurfaceCaptureHostWindow window)
        {
            if (window == null)
            {
                return;
            }

            window.rootVisualElement.Clear();
            if (window._ownedPanel != null)
            {
                window._ownedPanel.Dispose();
                window._ownedPanel = null;
                Object.DestroyImmediate(window); // UNH-SUPPRESS: batch host has no native parent to close.
                return;
            }
            try
            {
                window.Close();
            }
            catch (System.Exception)
            {
                Object.DestroyImmediate(window); // UNH-SUPPRESS: last resort when Close() cannot run
            }
        }

        /// <summary>
        /// Closes every host window still alive, including ones an interrupted run left behind,
        /// and reports how many there were.
        /// </summary>
        internal static int CloseLeakedHosts()
        {
            EditorSurfaceCaptureHostWindow[] leaked =
                Resources.FindObjectsOfTypeAll<EditorSurfaceCaptureHostWindow>();
            foreach (
                WallstopStudios.UnityHelpers.Tests.Editor.Capture.EditorSurfaceCaptureHostWindow leakedElement in leaked
            )
            {
                CloseHost(leakedElement);
            }

            return leaked.Length;
        }
    }
#endif
}
