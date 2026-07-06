// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ShowIf
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WShowIf reference/null condition tests with Odin Inspector.
    /// </summary>
    internal sealed class OdinShowIfReferenceTarget : SerializedScriptableObject
    {
        public GameObject objectReference;

        [WShowIf(nameof(objectReference), WShowIfComparison.IsNotNull)]
        public int dependentField;
    }
#endif
}
