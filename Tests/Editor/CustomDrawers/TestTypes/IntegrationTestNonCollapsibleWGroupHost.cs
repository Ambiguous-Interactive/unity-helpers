// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class IntegrationTestNonCollapsibleWGroupHost : ScriptableObject
    {
        [WGroup(
            "StaticGroup",
            displayName: "Static Group",
            collapsible: false,
            autoIncludeCount: 1
        )]
        public IntegrationTestStringIntDictionary dictionary = new();
    }
}
