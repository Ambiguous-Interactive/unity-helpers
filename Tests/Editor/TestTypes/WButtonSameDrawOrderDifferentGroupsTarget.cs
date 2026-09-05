// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target specifically for testing multiple groups at same draw order.
    /// </summary>
    public sealed class WButtonSameDrawOrderDifferentGroupsTarget : ScriptableObject
    {
        [WButton("Setup A", drawOrder: 0, groupName: "Setup")]
        public void SetupA() { }

        [WButton("Setup B", drawOrder: 0, groupName: "Setup")]
        public void SetupB() { }

        [WButton("Config A", drawOrder: 0, groupName: "Configuration")]
        public void ConfigA() { }

        [WButton("Config B", drawOrder: 0, groupName: "Configuration")]
        public void ConfigB() { }

        [WButton("Validate A", drawOrder: 0, groupName: "Validation")]
        public void ValidateA() { }

        [WButton("No Group 1", drawOrder: 0)]
        public void NoGroup1() { }

        [WButton("No Group 2", drawOrder: 0)]
        public void NoGroup2() { }
    }
}
