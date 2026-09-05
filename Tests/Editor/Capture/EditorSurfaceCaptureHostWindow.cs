// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Capture
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Offscreen host for a captured editor surface. A visual element only gets a panel once it
    /// lives in a shown window, and a panel is what the renderer draws, so the harness needs a
    /// window even though it never reads the desktop.
    ///
    /// It is a dedicated type rather than a bare <see cref="EditorWindow"/> so a leaked host from
    /// an interrupted run can be found by type and closed.
    /// </summary>
    internal sealed class EditorSurfaceCaptureHostWindow : EditorWindow
    {
        internal const string HostWindowTitle = "Editor Surface Capture Host";

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
