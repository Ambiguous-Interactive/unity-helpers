// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ShowIf
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WShowIf method-based condition tests with Odin Inspector.
    /// </summary>
    internal sealed class OdinShowIfMethodTarget : SerializedScriptableObject
    {
        public int value;

        public bool IsPositive()
        {
            return value > 0;
        }

        [WShowIf(nameof(IsPositive))]
        public int dependentField;
    }
#endif
}
