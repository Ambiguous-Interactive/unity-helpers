// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ShowIf
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WShowIf string condition tests with Odin Inspector.
    /// </summary>
    internal sealed class OdinShowIfStringTarget : SerializedScriptableObject
    {
        public string stringCondition;

        [WShowIf(nameof(stringCondition), WShowIfComparison.IsNotNullOrEmpty)]
        public int dependentField;
    }
#endif
}
