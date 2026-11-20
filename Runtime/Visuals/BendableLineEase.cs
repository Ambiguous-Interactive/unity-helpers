// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Visuals
{
    using System;

    /// <summary>Selects the interpolation curve for a bendable line animation.</summary>
    public enum BendableLineEase
    {
        /// <summary>Uses the default interpolation when no easing is specified.</summary>
        [Obsolete("Use Linear to select the default interpolation explicitly.")]
        Unset = 0,

        /// <summary>Moves at a constant rate.</summary>
        Linear = 1,

        /// <summary>Starts quickly and slows to a stop.</summary>
        OutQuad = 2,
    }
}
