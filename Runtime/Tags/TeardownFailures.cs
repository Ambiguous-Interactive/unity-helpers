// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using Core.Extension;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Keeps the first exception a teardown loop raised so the loop can finish, which is the only
    /// way a later unit of work still gets torn down.
    /// </summary>
    internal static class TeardownFailures
    {
        /// <summary>
        /// Returns whichever of the two exceptions the caller should keep, logging the one it drops.
        /// </summary>
        /// <param name="context">The object the dropped failure is attributed to.</param>
        /// <param name="firstFailure">The failure already recorded, or <c>null</c>.</param>
        /// <param name="failure">The failure just caught, or <c>null</c>.</param>
        /// <returns>The failure to carry forward, or <c>null</c> when neither is set.</returns>
        internal static Exception KeepFirst(
            Object context,
            Exception firstFailure,
            Exception failure
        )
        {
            if (failure == null)
            {
                return firstFailure;
            }

            if (firstFailure == null)
            {
                return failure;
            }

            context.LogError(
                $"Effect teardown raised a second exception, which is dropped in favour of the first.",
                failure
            );
            return firstFailure;
        }
    }
}
