// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WGroup
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for display name resolution testing.
    /// Tests that display names are correctly preserved when a group is defined
    /// by multiple fields, with and without explicit display names.
    /// </summary>
    public sealed class WGroupDisplayNameTestTarget : ScriptableObject
    {
        [WGroup("GroupA", displayName: "Custom Display A")]
        public int groupAField1;

        [WGroup("GroupA")]
        public int groupAField2;

        [WGroup("GroupA")]
        public int groupAField3;

        [WGroup("GroupB")]
        public int groupBField1;

        [WGroup("GroupB", displayName: "Custom Display B")]
        public int groupBField2;

        [WGroup("GroupB")]
        public int groupBField3;

        [WGroup("GroupC")]
        public int groupCField1;

        [WGroup("GroupC")]
        public int groupCField2;

        [WGroup("GroupC", displayName: "Custom Display C")]
        public int groupCField3;

        [WGroup("GroupD")]
        public int groupDField1;

        [WGroup("GroupD")]
        public int groupDField2;

        [WGroup("GroupE", displayName: "First Display E")]
        public int groupEField1;

        [WGroup("GroupE", displayName: "Second Display E")]
        public int groupEField2;
    }
}
#endif
