// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.IntDropDown
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for IntDropDown attribute with zero as one of the options.
    /// </summary>
    internal sealed class OdinIntDropDownWithZeroTarget : SerializedScriptableObject
    {
        [IntDropDown(0, 1, 2, 3)]
        public int selectedValue;
    }
#endif
}
