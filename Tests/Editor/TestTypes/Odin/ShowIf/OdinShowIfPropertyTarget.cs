// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ShowIf
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WShowIf property-based condition tests with Odin Inspector.
    /// </summary>
    internal sealed class OdinShowIfPropertyTarget : SerializedScriptableObject
    {
        public bool boolField;
        public int intField;

        public bool ComputedProperty => boolField && intField > 0;

        [WShowIf(nameof(ComputedProperty))]
        public int dependentField;
    }
#endif
}
