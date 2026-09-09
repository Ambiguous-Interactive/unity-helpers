// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Controls whether SerializableDictionary and SerializableSet inspectors start expanded.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class WSerializableCollectionFoldoutAttribute : PropertyAttribute
    {
        /// <summary>
        /// Requested default foldout behavior for the decorated collection field.
        /// </summary>
        public WSerializableCollectionFoldoutBehavior Behavior { get; }

        /// <summary>
        /// Convenience accessor for inspectors that only need an expanded flag.
        /// </summary>
        public bool StartExpanded =>
            Behavior == WSerializableCollectionFoldoutBehavior.StartExpanded;

        public WSerializableCollectionFoldoutAttribute(
            WSerializableCollectionFoldoutBehavior behavior =
                WSerializableCollectionFoldoutBehavior.StartCollapsed
        )
        {
            Behavior = behavior;
        }
    }

    /// <summary>
    /// Available foldout states for SerializableDictionary/SerializableSet inspectors.
    /// </summary>
    public enum WSerializableCollectionFoldoutBehavior
    {
        StartCollapsed = 0,
        StartExpanded = 1,
    }
}
