// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Editor.Utils
{
    using System;
    using System.Reflection;
    using UnityEditor;
    using UnityEditorInternal;
    using UnityEngine;

    /// <summary>
    /// Tracks whether at least one Project window tab is both open and currently visible so we can
    /// suppress actions (like Ping buttons) that would have no effect otherwise.
    /// </summary>
    internal static class ProjectBrowserVisibilityUtility
    {
        private const string ProjectBrowserTypeName = "UnityEditor.ProjectBrowser, UnityEditor";
        private const string HostViewTypeName = "UnityEditor.HostView";
        private const double PollIntervalSeconds = 0.25d;

        internal static bool _cachedVisibility;

        internal static double _nextPollTime;

        private static readonly Type ProjectBrowserType = Type.GetType(ProjectBrowserTypeName);
        private static readonly FieldInfo EditorWindowParentField = typeof(EditorWindow).GetField(
            "m_Parent",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        private static readonly Type HostViewType = typeof(EditorWindow).Assembly.GetType(
            HostViewTypeName
        );
        private static readonly PropertyInfo HostViewActualViewProperty = HostViewType?.GetProperty(
            "actualView",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );

        static ProjectBrowserVisibilityUtility()
        {
            _cachedVisibility = EvaluateProjectBrowserVisibility();
            _nextPollTime = EditorApplication.timeSinceStartup + PollIntervalSeconds;
            EditorApplication.update += HandleEditorApplicationUpdate;
        }

        internal static bool IsProjectBrowserVisible()
        {
            return _cachedVisibility;
        }

        private static void HandleEditorApplicationUpdate()
        {
            double time = EditorApplication.timeSinceStartup;
            if (time < _nextPollTime)
            {
                return;
            }

            _nextPollTime = time + PollIntervalSeconds;
            bool visibility = EvaluateProjectBrowserVisibility();
            UpdateCachedVisibility(visibility);
        }

        private static void UpdateCachedVisibility(bool newValue)
        {
            if (_cachedVisibility == newValue)
            {
                return;
            }

            _cachedVisibility = newValue;
            InternalEditorUtility.RepaintAllViews();
        }

        private static bool EvaluateProjectBrowserVisibility()
        {
            if (ProjectBrowserType == null)
            {
                return true;
            }

            UnityEngine.Object[] projectBrowsers = Resources.FindObjectsOfTypeAll(
                ProjectBrowserType
            );
            if (projectBrowsers == null || projectBrowsers.Length == 0)
            {
                return false;
            }

            foreach (UnityEngine.Object projectBrowsersElement in projectBrowsers)
            {
                EditorWindow window = projectBrowsersElement as EditorWindow;
                if (IsEditorWindowVisible(window))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsEditorWindowVisible(EditorWindow window)
        {
            if (window == null || !window)
            {
                return false;
            }

            if (EditorWindowParentField == null || HostViewActualViewProperty == null)
            {
                return true;
            }

            object hostView = EditorWindowParentField.GetValue(window);
            if (hostView == null)
            {
                return false;
            }

            object currentView = HostViewActualViewProperty.GetValue(hostView);
            return ReferenceEquals(currentView, window);
        }
    }
}
#endif
