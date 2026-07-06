// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ValueDropDown
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;

    /// <summary>
    /// Test target for WValueDropDown with enum options from static provider.
    /// </summary>
    internal sealed class OdinValueDropDownEnumTarget : SerializedScriptableObject
    {
        [WValueDropDown(
            typeof(TestEnumOptionsProvider),
            nameof(TestEnumOptionsProvider.GetEnumOptions)
        )]
        public TestDropDownMode selectedMode;

        public static class TestEnumOptionsProvider
        {
            public static IEnumerable<TestDropDownMode> GetEnumOptions()
            {
                yield return TestDropDownMode.ModeA;
                yield return TestDropDownMode.ModeB;
                yield return TestDropDownMode.ModeC;
            }
        }
    }
#endif
}
