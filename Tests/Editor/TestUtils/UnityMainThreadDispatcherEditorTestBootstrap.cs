// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.TestUtils
{
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Disables automatic UnityMainThreadDispatcher creation during test runs.
    /// This prevents the dispatcher from being created unexpectedly during tests
    /// and ensures clean test isolation.
    /// </summary>
    /// <remarks>
    /// The dispatcher cleanup is deferred via EditorApplication.delayCall to avoid
    /// blocking during Unity's early initialization phase (e.g., during "Open Project: Open Scene").
    /// Calling Resources.FindObjectsOfTypeAll during static initialization can cause
    /// Unity Editor hangs.
    /// </remarks>
    [InitializeOnLoad]
    internal static class UnityMainThreadDispatcherEditorTestBootstrap
    {
        static UnityMainThreadDispatcherEditorTestBootstrap()
        {
            // Safe during static initialization: no Unity API call is required.
            UnityMainThreadDispatcher.SetAutoCreationEnabled(false);

            // Delay cleanup until Unity has loaded; FindObjectsOfTypeAll can hang during static initialization.
            EditorApplication.delayCall += CleanupExistingDispatchers;
        }

        private static void CleanupExistingDispatchers()
        {
            UnityMainThreadDispatcher[] allDispatchers =
                Resources.FindObjectsOfTypeAll<UnityMainThreadDispatcher>();

            if (allDispatchers == null || allDispatchers.Length == 0)
            {
                return;
            }

            UnityMainThreadDispatcher dispatcher = allDispatchers[0];
            if (dispatcher == null)
            {
                return;
            }

            GameObject dispatcherObject = dispatcher.gameObject;
            if (dispatcherObject == null)
            {
                return;
            }

            Object.DestroyImmediate(dispatcherObject); // UNH-SUPPRESS: Cleanup dispatcher in test teardown
        }
    }
}
