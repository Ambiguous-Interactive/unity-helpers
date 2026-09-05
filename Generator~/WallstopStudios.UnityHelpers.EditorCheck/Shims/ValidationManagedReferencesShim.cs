// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
    using System;
    using UnityEditor;

    internal static class ValidationManagedReferences
    {
        internal static long GetId(SerializedProperty property)
        {
            throw new NotSupportedException(
                "Host Unity3D.SDK 2021.1 references lack SerializedProperty.managedReferenceId. Native validation requires Unity 2021.3 or newer; this compile-only shim cannot return managed-reference identities."
            );
        }
    }
}
