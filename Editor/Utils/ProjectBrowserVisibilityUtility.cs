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

        private static bool? _visibilityOverride;
        private static bool _cachedVisibility;
        private static double _nextPollTime;

        static ProjectBrowserVisibilityUtility()
        {
            _cachedVisibility = EvaluateProjectBrowserVisibility();
            _nextPollTime = EditorApplication.timeSinceStartup + PollIntervalSeconds;
            EditorApplication.update += HandleEditorApplicationUpdate;
        }

        internal static void SetProjectBrowserVisibilityForTesting(bool? visible)
        {
            _visibilityOverride = visible;
            if (visible.HasValue)
            {
                UpdateCachedVisibility(visible.Value);
            }
            else
            {
                ForceVisibilityPoll();
            }
        }

        internal static bool IsProjectBrowserVisible()
        {
            if (_visibilityOverride.HasValue)
            {
                return _visibilityOverride.Value;
            }

            return _cachedVisibility;
        }

        private static void HandleEditorApplicationUpdate()
        {
            if (_visibilityOverride.HasValue)
            {
                UpdateCachedVisibility(_visibilityOverride.Value);
                return;
            }

            double time = EditorApplication.timeSinceStartup;
            if (time < _nextPollTime)
            {
                return;
            }

            _nextPollTime = time + PollIntervalSeconds;
            bool visibility = EvaluateProjectBrowserVisibility();
            UpdateCachedVisibility(visibility);
        }

        private static void ForceVisibilityPoll()
        {
            _nextPollTime = 0d;
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
