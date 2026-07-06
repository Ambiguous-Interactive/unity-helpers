// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.IntDropDown
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for IntDropDown attribute with a large list of options (uses popup dropdown).
    /// </summary>
    internal sealed class OdinIntDropDownLargeListTarget : SerializedScriptableObject
    {
        [IntDropDown(
            typeof(TestLargeIntListProvider),
            nameof(TestLargeIntListProvider.GetLargeList)
        )]
        public int largeListSelection;

        public static class TestLargeIntListProvider
        {
            public static IEnumerable<int> GetLargeList()
            {
                for (int i = 0; i < 150; i++)
                {
                    yield return i * 10;
                }
            }
        }
    }
#endif
}
