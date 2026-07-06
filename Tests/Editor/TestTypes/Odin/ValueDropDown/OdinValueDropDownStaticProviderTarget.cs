// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ValueDropDown
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WValueDropDown with static method provider on SerializedScriptableObject.
    /// </summary>
    internal sealed class OdinValueDropDownStaticProviderTarget : SerializedScriptableObject
    {
        [WValueDropDown(
            typeof(TestDropDownOptionsProvider),
            nameof(TestDropDownOptionsProvider.GetStringOptions)
        )]
        public string selectedFromProvider;

        public static class TestDropDownOptionsProvider
        {
            public static IEnumerable<string> GetStringOptions()
            {
                yield return "First";
                yield return "Second";
                yield return "Third";
            }
        }
    }
#endif
}
