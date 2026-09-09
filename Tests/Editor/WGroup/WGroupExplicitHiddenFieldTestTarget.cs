// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WGroup
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target where a [HideInInspector] field has an explicit [WGroup] attribute.
    /// This tests that hidden fields can still be explicitly grouped when needed.
    /// </summary>
    public sealed class WGroupExplicitHiddenFieldTestTarget : ScriptableObject
    {
        [WGroup("Explicit Group")]
        public int groupAnchor;

        public int visibleField;

        [WGroup("Explicit Group")]
        [HideInInspector]
        [SerializeField]
#pragma warning disable CS0649 // Field is never assigned to
        internal int _explicitlyGroupedHiddenField;
#pragma warning restore CS0649 // Field is never assigned to
    }
}
#endif
