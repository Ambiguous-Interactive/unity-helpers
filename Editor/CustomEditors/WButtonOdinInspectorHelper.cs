// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomEditors
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Extensions;
    using WallstopStudios.UnityHelpers.Editor.Internal;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Editor.Utils.WGroup;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Shared logic for Odin-based WButton inspectors.
    /// </summary>
    /// <remarks>
    /// This helper consolidates duplicate code between <see cref="WButtonOdinMonoBehaviourInspector"/>
    /// and <see cref="WButtonOdinScriptableObjectInspector"/> to follow DRY principles.
    /// </remarks>
    internal static class WButtonOdinInspectorHelper
    {
        private const string ScriptPropertyPath = "m_Script";

        internal static readonly WallstopGenericPool<
            Dictionary<string, SerializedProperty>
        > PropertyLookupPool = new(
            () => new Dictionary<string, SerializedProperty>(16, StringComparer.Ordinal),
            onRelease: d => d.Clear()
        );

        /// <summary>
        /// Draws the complete inspector GUI for an Odin-based object with WButton and WGroup support.
        /// </summary>
        /// <param name="editor">The editor instance.</param>
        /// <param name="paginationStates">Per-editor pagination state for button groups.</param>
        /// <param name="foldoutStates">Per-editor foldout state for button groups.</param>
        /// <param name="groupFoldoutStates">Per-editor foldout state for WGroups.</param>
        internal static void DrawInspectorGUI(
            Editor editor,
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates,
            Dictionary<WButtonGroupKey, bool> foldoutStates,
            Dictionary<int, bool> groupFoldoutStates
        )
        {
            // Check editor.target first using Unity's == operator to detect destroyed objects
            // before accessing serializedObject, which would log an error if target is destroyed
            if (editor == null || editor.target == null)
            {
                return;
            }

            SerializedObject serializedObject = editor.serializedObject;
            if (serializedObject == null || serializedObject.targetObject == null)
            {
                return;
            }

            using PooledResource<List<WButtonMethodContext>> triggeredContextsLease =
                Buffers<WButtonMethodContext>.GetList(
                    4,
                    out List<WButtonMethodContext> triggeredContexts
                );

            editor.serializedObject.UpdateIfRequiredOrScript();

            using PooledResource<Dictionary<string, SerializedProperty>> propertyLookupLease =
                PropertyLookupPool.Get(out Dictionary<string, SerializedProperty> propertyLookup);

            SerializedProperty scriptProperty = BuildPropertyLookup(
                editor.serializedObject,
                propertyLookup
            );

            bool drawScriptField = scriptProperty != null && !InlineInspectorContext.IsActive;
            if (drawScriptField)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(scriptProperty, true);
                }
                EditorGUILayout.Space();
            }

            UnityHelpersSettings.WButtonActionsPlacement placement =
                UnityHelpersSettings.GetWButtonActionsPlacement();
            UnityHelpersSettings.WButtonFoldoutBehavior foldoutBehavior =
                UnityHelpersSettings.GetWButtonFoldoutBehavior();
            bool globalPlacementIsTop =
                placement == UnityHelpersSettings.WButtonActionsPlacement.Top;

            if (
                WButtonGUI.DrawButtons(
                    editor,
                    WButtonPlacement.Top,
                    paginationStates,
                    foldoutStates,
                    foldoutBehavior,
                    triggeredContexts,
                    globalPlacementIsTop
                )
            )
            {
                EditorGUILayout.Space();
            }

            string scriptPathOrNull = scriptProperty != null ? scriptProperty.propertyPath : null;
            WGroupLayout layout = WGroupLayoutBuilder.Build(
                editor.serializedObject,
                scriptPathOrNull
            );
            IReadOnlyList<WGroupDrawOperation> operations = layout.Operations;

            for (int index = 0; index < operations.Count; index++)
            {
                WGroupDrawOperation operation = operations[index];
                if (operation.Type == WGroupDrawOperationType.Group)
                {
                    WGroupDefinition definition = operation.Group;
                    if (definition == null)
                    {
                        continue;
                    }

                    WGroupGUI.DrawGroup(
                        definition,
                        editor.serializedObject,
                        groupFoldoutStates,
                        propertyLookup
                    );
                    continue;
                }

                if (operation.IsHiddenInInspector)
                {
                    continue;
                }

                if (
                    !propertyLookup.TryGetValue(
                        operation.PropertyPath,
                        out SerializedProperty property
                    )
                )
                {
                    continue;
                }

                if (!WShowIfPropertyDrawer.ShouldShowProperty(property))
                {
                    continue;
                }

                DrawValidationHelpBoxIfNeeded(property);

                EditorGUILayout.PropertyField(property, true);
            }

            editor.serializedObject.ApplyModifiedProperties();

            if (
                WButtonGUI.DrawButtons(
                    editor,
                    WButtonPlacement.Bottom,
                    paginationStates,
                    foldoutStates,
                    foldoutBehavior,
                    triggeredContexts,
                    globalPlacementIsTop
                )
            )
            {
                EditorGUILayout.Space();
            }

            if (triggeredContexts.Count > 0)
            {
                WButtonInvocationController.ProcessTriggeredMethods(triggeredContexts);
            }
        }

        private static SerializedProperty BuildPropertyLookup(
            SerializedObject serializedObject,
            Dictionary<string, SerializedProperty> propertyLookup
        )
        {
            propertyLookup.Clear();
            SerializedProperty scriptProperty = null;
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                string path = iterator.propertyPath;

                if (string.Equals(path, ScriptPropertyPath, StringComparison.Ordinal))
                {
                    scriptProperty = iterator.Copy();
                    continue;
                }

                propertyLookup[path] = iterator.Copy();
            }

            return scriptProperty;
        }

        private static void DrawValidationHelpBoxIfNeeded(SerializedProperty property)
        {
            if (!property.isArray || property.propertyType == SerializedPropertyType.String)
            {
                return;
            }

            property.GetEnclosingObject(out FieldInfo fieldInfo);
            if (fieldInfo == null)
            {
                return;
            }

            ValidateAssignmentAttribute validateAttribute =
                fieldInfo.GetCustomAttribute<ValidateAssignmentAttribute>();
            if (validateAttribute != null)
            {
                ValidateAssignmentPropertyDrawer.DrawValidationHelpBoxIfNeeded(
                    property,
                    validateAttribute
                );
                return;
            }

            WNotNullAttribute notNullAttribute = fieldInfo.GetCustomAttribute<WNotNullAttribute>();
            if (notNullAttribute != null)
            {
                WNotNullPropertyDrawer.DrawValidationHelpBoxIfNeeded(property, notNullAttribute);
            }
        }
    }
#endif
}
