// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Grouping declared through <c>[field:]</c>, which is where the attribute lands when the data
    /// lives on an auto-property's backing field.
    /// </summary>
    internal sealed class BackingFieldGroupAsset : ScriptableObject
    {
        [field: SerializeField]
        [field: WGroup("Stats", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
        public int Primary { get; private set; }

        [field: SerializeField]
        public int Secondary { get; private set; }

        [field: SerializeField]
        [field: WGroupEnd]
        public int Tertiary { get; private set; }

        [SerializeField]
        private int _outside;
    }
}
