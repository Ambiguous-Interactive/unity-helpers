// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.IntDropDown
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for IntDropDown attribute with an empty options provider.
    /// </summary>
    internal sealed class OdinIntDropDownEmptyOptionsTarget : SerializedScriptableObject
    {
        [IntDropDown(typeof(TestEmptyIntProvider), nameof(TestEmptyIntProvider.GetEmptyOptions))]
        public int emptyDropdown;

        public static class TestEmptyIntProvider
        {
            public static IEnumerable<int> GetEmptyOptions()
            {
                return Array.Empty<int>();
            }
        }
    }
#endif
}
