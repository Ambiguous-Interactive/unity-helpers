// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomEditors
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;
    using UnityHelpers.Utils;

    [CustomEditor(typeof(PolygonCollider2DOptimizer))]
    public sealed class PolygonCollider2DOptimizerEditor : Editor
    {
        private SerializedProperty _tolerance;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(_tolerance);

            if (GUILayout.Button("Optimize"))
            {
                PolygonCollider2DOptimizer optimizer = target as PolygonCollider2DOptimizer;
                if (optimizer != null)
                {
                    PolygonCollider2D collider = optimizer.GetComponent<PolygonCollider2D>();
                    if (collider != null)
                    {
                        Undo.RecordObject(collider, "Optimize Polygon Collider");
                    }
                    Undo.RecordObject(optimizer, "Optimize Polygon Collider");
                    optimizer.Refresh();
                    EditorUtility.SetDirty(optimizer);
                    if (collider != null)
                    {
                        EditorUtility.SetDirty(collider);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void OnEnable()
        {
            _tolerance = serializedObject.FindProperty(
                nameof(PolygonCollider2DOptimizer.tolerance)
            );
        }
    }
#endif
}
