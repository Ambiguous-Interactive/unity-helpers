// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using UnityEditor;

    internal static class ValidationManagedReferences
    {
        internal static long GetId(SerializedProperty property) => property.managedReferenceId;
    }
#endif
}
