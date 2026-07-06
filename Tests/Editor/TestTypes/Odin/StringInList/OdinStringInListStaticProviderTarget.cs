// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.StringInList
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for StringInList attribute with static method provider.
    /// </summary>
    internal sealed class OdinStringInListStaticProviderTarget : SerializedScriptableObject
    {
        [StringInList(
            typeof(TestStaticStringProvider),
            nameof(TestStaticStringProvider.GetStaticOptions)
        )]
        public string staticProviderSelection;

        public static class TestStaticStringProvider
        {
            public static IEnumerable<string> GetStaticOptions()
            {
                yield return "StaticOption1";
                yield return "StaticOption2";
                yield return "StaticOption3";
            }
        }
    }
#endif
}
