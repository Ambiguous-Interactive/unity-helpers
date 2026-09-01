// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class IntegrationTestWGroupSetHost : ScriptableObject
    {
        [WGroup("TestGroup", displayName: "Test Group", collapsible: true, autoIncludeCount: 1)]
        public IntegrationTestIntSet set = new();
    }
}
