// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.StringInList
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for StringInList attribute with empty options list.
    /// </summary>
    internal sealed class OdinStringInListEmptyOptionsTarget : SerializedScriptableObject
    {
        [StringInList(
            typeof(TestEmptyStringProvider),
            nameof(TestEmptyStringProvider.GetEmptyOptions)
        )]
        public string emptyDropdown;

        public static class TestEmptyStringProvider
        {
            public static IEnumerable<string> GetEmptyOptions()
            {
                return Array.Empty<string>();
            }
        }
    }
#endif
}
