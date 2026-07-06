// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.IntDropDown
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for IntDropDown attribute using a static method provider for options.
    /// </summary>
    internal sealed class OdinIntDropDownStaticProviderTarget : SerializedScriptableObject
    {
        [IntDropDown(typeof(TestStaticIntProvider), nameof(TestStaticIntProvider.GetStaticOptions))]
        public int staticProviderSelection;

        public static class TestStaticIntProvider
        {
            public static IEnumerable<int> GetStaticOptions()
            {
                yield return 100;
                yield return 200;
                yield return 300;
            }
        }
    }
#endif
}
