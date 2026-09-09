// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WGroup
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for infinite auto-include mode with hidden fields.
    /// </summary>
    public sealed class WGroupHideInInspectorInfiniteTestTarget : ScriptableObject
    {
        [WGroup("Infinite Group")]
        public int groupAnchor;

        public int visibleField1;
        public int visibleField2;

        [HideInInspector]
        [SerializeField]
#pragma warning disable CS0649 // Field is never assigned to
        internal int _hiddenField;
#pragma warning restore CS0649 // Field is never assigned to
    }
}
#endif
