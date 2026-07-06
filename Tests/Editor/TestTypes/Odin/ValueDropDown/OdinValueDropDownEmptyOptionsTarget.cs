// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ValueDropDown
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WValueDropDown with empty options to test empty state handling.
    /// </summary>
    internal sealed class OdinValueDropDownEmptyOptionsTarget : SerializedScriptableObject
    {
        [WValueDropDown(
            typeof(TestEmptyOptionsProvider),
            nameof(TestEmptyOptionsProvider.GetEmptyOptions)
        )]
        public string emptyDropdown;

        public static class TestEmptyOptionsProvider
        {
            public static IEnumerable<string> GetEmptyOptions()
            {
                return Array.Empty<string>();
            }
        }
    }
#endif
}
