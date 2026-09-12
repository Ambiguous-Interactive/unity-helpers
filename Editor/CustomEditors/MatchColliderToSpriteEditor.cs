// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomEditors
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Utils;

    [CustomEditor(typeof(MatchColliderToSprite))]
    public sealed class MatchColliderToSpriteEditor : Editor
    {
        private const string ScriptPropertyPath = "m_Script";

        public override void OnInspectorGUI()
        {
            MatchColliderToSprite matchColliderToSprite = target as MatchColliderToSprite;
            if (matchColliderToSprite == null)
            {
                this.LogError(
                    $"Target was of type {(target != null ? target.GetType() : null)}, expected {nameof(MatchColliderToSprite)}."
                );
                return;
            }

            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                using (new EditorGUI.DisabledScope(iterator.propertyPath == ScriptPropertyPath))
                {
                    EditorGUILayout.PropertyField(iterator, true);
                }
                enterChildren = false;
            }
            if (EditorGUI.EndChangeCheck())
            {
                ApplyInspectorProperties();
            }

            if (GUILayout.Button("MatchColliderToSprite"))
            {
                ApplyInspectorProperties();
            }
        }

        internal void ApplyInspectorProperties()
        {
            PolygonCollider2D collider = ResolvePendingCollider();
            RecordCompleteColliderUpdate(collider);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            if (target is MatchColliderToSprite matcher)
            {
                matcher.RebuildCollider();
            }
            EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            if (collider != null)
            {
                EditorUtility.SetDirty(collider);
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            }
        }

        private void RecordCompleteColliderUpdate(PolygonCollider2D collider)
        {
            if (collider != null)
            {
                Undo.RegisterCompleteObjectUndo(
                    new UnityEngine.Object[] { target, collider },
                    "Match Collider To Sprite"
                );
            }
            else
            {
                Undo.RegisterCompleteObjectUndo(target, "Match Collider To Sprite");
            }
        }

        private PolygonCollider2D ResolvePendingCollider()
        {
            SerializedProperty colliderProperty = serializedObject.FindProperty(
                nameof(MatchColliderToSprite.polygonCollider)
            );
            PolygonCollider2D collider = colliderProperty.objectReferenceValue as PolygonCollider2D;
            if (
                collider == null
                && target is MatchColliderToSprite matcher
                && matcher.TryGetComponent(out PolygonCollider2D resolvedCollider)
            )
            {
                collider = resolvedCollider;
            }
            return collider;
        }
    }
#endif
}
