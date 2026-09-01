// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    /// <summary>
    /// The byte budget a single <c>stackalloc</c> may spend, so a caller-sized buffer has a ceiling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>stackalloc</c> sized from an argument is bounded by whatever the caller passes, and
    /// overrunning the stack raises <c>StackOverflowException</c>, which no <c>catch</c> can
    /// intercept: the process dies. That is strictly worse than the exception this package's public
    /// APIs are already forbidden from throwing, so every caller-sized span allocation stops at this
    /// budget and rents from <c>SystemArrayPool</c> above it.
    /// </para>
    /// <para>
    /// 8 KiB against Unity's 1 MiB main thread and 512 KiB worker threads leaves the frame the
    /// allocation sits in, and every frame above it, room to finish -- while still covering a
    /// 1024-vertex polygon or a 1024-object selection on the stack.
    /// </para>
    /// </remarks>
    public static class StackAllocation
    {
        /// <summary>The largest number of bytes one <c>stackalloc</c> may take.</summary>
        public const int MaxByteBudget = 8192;
    }
}
