// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ShowIf
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WShowIf integer condition tests with Odin Inspector.
    /// </summary>
    internal sealed class OdinShowIfIntTarget : SerializedScriptableObject
    {
        public int intCondition;

        [WShowIf(nameof(intCondition), WShowIfComparison.GreaterThan, 0)]
        public int dependentField;
    }
#endif
}
