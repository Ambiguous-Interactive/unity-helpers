// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;

    /// <summary>
    /// Decides what <see cref="WallstopStudios.UnityHelpers.Utils.RuntimeSingleton{T}.Instance"/> does
    /// when no instance of the singleton exists in any loaded scene.
    /// </summary>
    public enum SingletonCreationPolicy : byte
    {
        /// <summary>
        /// Default uninitialized value - should never be used.
        /// </summary>
        [Obsolete("Use a specific SingletonCreationPolicy value instead of Unknown.")]
        Unknown = 0,

        /// <summary>
        /// Create a new <see cref="UnityEngine.GameObject"/> holding the component on first access.
        /// This is the behavior a singleton carrying no
        /// <see cref="WallstopStudios.UnityHelpers.Core.Attributes.SingletonCreationAttribute"/> gets.
        /// </summary>
        CreateOnDemand = 1,

        /// <summary>
        /// Never create one. <c>Instance</c> returns the existing instance when there is one and
        /// <c>null</c> otherwise, logging a single warning naming the type.
        /// </summary>
        NeverCreate = 2,
    }
}
