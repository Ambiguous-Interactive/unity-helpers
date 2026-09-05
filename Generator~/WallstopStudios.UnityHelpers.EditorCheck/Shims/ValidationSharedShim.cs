// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*

 * The 2021.1 references lack the managedReferenceValue getter, excluding ValidationShared. This

 * signature-only shim keeps its callers compilable.

 */
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils
{
    using UnityEditor;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public static class ValidationShared
    {
        public const float HelpBoxPadding = 2f;
        public const string ValidateAssignmentMessageFormat = "{0} is not assigned or is empty";
        public const string ValidateAssignmentFallbackMessage = "Field is not assigned or is empty";
        public const string NotNullMessageFormat = "{0} must not be null";
        public const string NotNullFallbackMessage = "Field is null or unassigned";

        public static void ClearHeightCache() { }

        public static float GetHelpBoxHeight(string message) => default;

        public static MessageType ToMessageType(ValidateAssignmentMessageType messageType) =>
            default;

        public static HelpBoxMessageType ToHelpBoxMessageType(
            ValidateAssignmentMessageType messageType
        ) => default;

        public static MessageType GetMessageType(ValidateAssignmentAttribute validateAttribute) =>
            default;

        public static HelpBoxMessageType GetHelpBoxMessageType(
            ValidateAssignmentAttribute validateAttribute
        ) => default;

        public static MessageType ToMessageType(WNotNullMessageType messageType) => default;

        public static HelpBoxMessageType ToHelpBoxMessageType(WNotNullMessageType messageType) =>
            default;

        public static MessageType GetMessageType(WNotNullAttribute notNullAttribute) => default;

        public static HelpBoxMessageType GetHelpBoxMessageType(
            WNotNullAttribute notNullAttribute
        ) => default;

        public static string GetValidateAssignmentMessage(
            SerializedProperty property,
            ValidateAssignmentAttribute validateAttribute
        ) => default;

        public static string GetValidateAssignmentMessage(
            string fieldName,
            ValidateAssignmentAttribute validateAttribute
        ) => default;

        public static string GetNotNullMessage(
            SerializedProperty property,
            WNotNullAttribute notNullAttribute
        ) => default;

        public static string GetNotNullMessage(
            string fieldName,
            WNotNullAttribute notNullAttribute
        ) => default;

        public static bool IsValueNull(object value) => default;

        public static bool IsValueInvalid(object value) => default;

        public static bool IsPropertyNull(SerializedProperty property) => default;

        public static bool IsPropertyInvalid(SerializedProperty property) => default;

        public static bool IsGenericPropertyInvalid(SerializedProperty property) => default;

        public static bool DrawValidateAssignmentHelpBoxIfNeeded(
            SerializedProperty property,
            ValidateAssignmentAttribute validateAttribute
        ) => default;

        public static bool DrawNotNullHelpBoxIfNeeded(
            SerializedProperty property,
            WNotNullAttribute notNullAttribute
        ) => default;
    }
}
