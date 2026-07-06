// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector.Editor;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;

    /// <summary>
    /// Odin Inspector attribute drawer for <see cref="ValidateAssignmentAttribute"/>.
    /// Displays a warning or error HelpBox when the field value is invalid
    /// (null, empty string, or empty collection).
    /// </summary>
    /// <remarks>
    /// This drawer ensures ValidateAssignment works correctly when Odin Inspector is installed
    /// and classes derive from SerializedMonoBehaviour or SerializedScriptableObject,
    /// where Unity's standard PropertyDrawer system is bypassed.
    /// </remarks>
    public sealed class ValidateAssignmentOdinDrawer
        : OdinAttributeDrawer<ValidateAssignmentAttribute>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            CallNextDrawer(label);

            object value = Property.ValueEntry?.WeakSmartValue;
            if (ValidationShared.IsValueInvalid(value))
            {
                string message = ValidationShared.GetValidateAssignmentMessage(
                    Property.NiceName,
                    Attribute
                );
                MessageType messageType = ValidationShared.GetMessageType(Attribute);
                EditorGUILayout.HelpBox(message, messageType);
            }
        }
    }
#endif
}
