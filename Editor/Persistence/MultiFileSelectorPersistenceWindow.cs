// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Editor.Persistence
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Simple UI to manage MultiFileSelector persistence options and run cleanup.
    /// </summary>
    public sealed class MultiFileSelectorPersistenceWindow : EditorWindow
    {
        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Multi-File Selector Persistence")]
        private static void Open()
        {
            MultiFileSelectorPersistenceWindow window =
                GetWindow<MultiFileSelectorPersistenceWindow>(
                    true,
                    "File Selector Persistence",
                    true
                );
            window.minSize = new Vector2(380, 160);
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Cleanup Settings", EditorStyles.boldLabel);

            bool auto = MultiFileSelectorPersistenceManager.IsAutoCleanupEnabled();
            bool newAuto = EditorGUILayout.ToggleLeft("Run cleanup on Editor startup", auto);
            if (newAuto != auto)
            {
                MultiFileSelectorPersistenceManager.SetAutoCleanupEnabled(newAuto);
            }

            int days = MultiFileSelectorPersistenceManager.GetMaxAgeDays();
            int newDays = EditorGUILayout.IntField(
                new GUIContent(
                    "Max age (days)",
                    "Scopes not used within this many days are cleaned."
                ),
                days
            );
            if (newDays != days)
            {
                MultiFileSelectorPersistenceManager.SetMaxAgeDays(newDays);
            }

            GUILayout.Space(10);
            if (GUILayout.Button("Run Cleanup Now", GUILayout.Height(28)))
            {
                MultiFileSelectorPersistenceManager.RunCleanupNow();
                ShowNotification(new GUIContent("Cleanup completed"));
            }

            GUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Persistence is opt-in per MultiFileSelectorElement via persistenceKey. Only scoped keys are managed.",
                MessageType.Info
            );
        }
    }
}
#endif
