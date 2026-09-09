// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WGroup
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for verifying that [HideInInspector] fields are excluded from
    /// WGroup auto-include processing. Hidden fields should never be automatically
    /// included in groups, though they can still be explicitly included via [WGroup].
    /// </summary>
    /// <remarks>
    /// Field layout:
    /// - groupAnchor: [WGroup] with explicit autoIncludeCount: 4
    /// - hiddenField1: [HideInInspector] - should NOT be auto-included
    /// - visibleField1: No attributes - should be auto-included
    /// - hiddenField2: [HideInInspector] - should NOT be auto-included
    /// - visibleField2: No attributes - should be auto-included
    /// - visibleField3: No attributes - should be auto-included
    /// - visibleField4: No attributes - should be auto-included (last in count)
    /// - notIncluded: No attributes - beyond auto-include count, should NOT be included
    ///
    /// Expected: Group should contain groupAnchor + 4 visible fields = 5 total
    /// (hiddenField1 and hiddenField2 are skipped but still count towards auto-include budget)
    /// </remarks>
    public sealed class WGroupHideInInspectorTestTarget : ScriptableObject
    {
        [WGroup("Test Group", autoIncludeCount: 4)]
        public int groupAnchor;

        public int visibleField1;

        public int visibleField2;
        public int visibleField3;
        public int visibleField4;
        public int notIncluded;

        [HideInInspector]
        [SerializeField]
#pragma warning disable CS0649 // Field is never assigned to
        internal int _hiddenField1;
#pragma warning restore CS0649 // Field is never assigned to

        [HideInInspector]
        [SerializeField]
#pragma warning disable CS0649 // Field is never assigned to
        internal int _hiddenField2;
#pragma warning restore CS0649 // Field is never assigned to
    }
}
#endif
