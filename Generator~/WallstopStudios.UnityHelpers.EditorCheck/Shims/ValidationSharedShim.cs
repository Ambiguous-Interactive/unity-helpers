// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Reference-only stand-in for the package's OWN `ValidationShared`, and the one shim here that does
// not stand in for a third party. Read the reason before touching it.
//
// `Editor/CustomDrawers/Utils/ValidationShared.cs` reads `SerializedProperty.managedReferenceValue`.
// Unity added that property's GETTER in 2021.2; the newest community UnityEditor reference assembly
// on nuget.org is `Unity3D.SDK` 2021.1.14, which ships the setter only, so the real file cannot
// compile here at all and is excluded (see the csproj's enumerated exclusion list).
//
// Excluding it alone would take EIGHT more real files with it, because they only NAME it:
//
//     Editor/CustomDrawers/ValidateAssignmentPropertyDrawer.cs
//     Editor/CustomDrawers/WNotNullPropertyDrawer.cs
//     Editor/CustomDrawers/Odin/ValidateAssignmentOdinDrawer.cs
//     Editor/CustomDrawers/Odin/WNotNullOdinDrawer.cs
//     Editor/CustomEditors/WButtonInspector.cs
//     Editor/CustomEditors/WButtonOdinInspectorHelper.cs
//     Editor/CustomEditors/WButtonOdinMonoBehaviourInspector.cs
//     Editor/CustomEditors/WButtonOdinScriptableObjectInspector.cs
//
// The last five are exactly the surface #347 exists to compile, so the cascade would cost the gate
// its point. One stand-in file buys eight real ones back.
//
// The drift failure mode is a FALSE RED, not a false green, and that is why this is acceptable: the
// consumers compiled here are the real files. Rename or re-sign a member on the real
// `ValidationShared` and the real callers move with it, this file does not, and the gate reports
// `ValidationShared does not contain a definition for ...` naming this file. A stale member left
// here that nobody calls any more is inert. Mirror the real signatures EXACTLY -- bodies are
// deliberately empty, because a type-checker asserts surface, never behaviour.
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
