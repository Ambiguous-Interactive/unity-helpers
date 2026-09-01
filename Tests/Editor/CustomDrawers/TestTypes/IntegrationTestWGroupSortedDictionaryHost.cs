// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class IntegrationTestWGroupSortedDictionaryHost : ScriptableObject
    {
        [WGroup("SortedGroup", displayName: "Sorted Group", collapsible: true, autoIncludeCount: 1)]
        public IntegrationTestSortedStringIntDictionary sortedDictionary = new();
    }
}
