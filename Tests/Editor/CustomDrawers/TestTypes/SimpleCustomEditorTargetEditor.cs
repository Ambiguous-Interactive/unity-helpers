// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers
{
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Internal;

    /// <summary>
    /// Custom editor for SimpleCustomEditorTarget used to test inline editor behavior with custom editors.
    /// </summary>
    [CustomEditor(typeof(SimpleCustomEditorTarget))]
    internal sealed class SimpleCustomEditorTargetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty scriptProperty = serializedObject.FindProperty("m_Script");
            if (scriptProperty != null && !InlineInspectorContext.IsActive)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(scriptProperty, true);
                }
                EditorGUILayout.Space();
            }

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                if (
                    string.Equals(
                        iterator.propertyPath,
                        "m_Script",
                        System.StringComparison.Ordinal
                    )
                )
                {
                    enterChildren = false;
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
                enterChildren = false;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
