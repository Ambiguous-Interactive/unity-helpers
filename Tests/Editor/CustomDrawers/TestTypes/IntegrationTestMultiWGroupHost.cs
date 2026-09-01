// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class IntegrationTestMultiWGroupHost : ScriptableObject
    {
        [WGroup("OuterGroup", displayName: "Outer Group", collapsible: true, autoIncludeCount: 3)]
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
        public int outerField;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value

        [WGroup("InnerGroup", displayName: "Inner Group", collapsible: true, autoIncludeCount: 1)]
        public IntegrationTestStringIntDictionary nestedDictionary = new();

        [WGroupEnd("InnerGroup")]
        public IntegrationTestIntSet nestedSet = new();

        [WGroupEnd("OuterGroup")]
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
        public int outerEndField;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value
    }
}
