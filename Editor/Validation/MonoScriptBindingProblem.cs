// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;

    /// <summary>
    /// Why a type cannot be authored onto a scene, a prefab or an asset.
    /// </summary>
    public enum MonoScriptBindingProblem
    {
        /// <summary>Unused.</summary>
        [Obsolete("A finding always names a problem.")]
        Unknown = 0,

        /// <summary>
        /// No <c>MonoScript</c> binds the type, so nothing can reference it from an asset.
        /// </summary>
        NoBoundScript = 1,

        /// <summary>
        /// A script asset binds a type that is not the one its file name names, so the binding is
        /// decided by declaration order and one added type moves it.
        /// </summary>
        FileNameMismatch = 2,
    }
#endif
}
