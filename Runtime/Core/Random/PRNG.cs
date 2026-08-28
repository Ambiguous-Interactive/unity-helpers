// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    /// <summary>
    /// Convenience access to the default high-performance PRNG used by this package.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns a thread-local instance of the current default generator (presently
    /// <see cref="Xoshiro256StarStar"/>). Using this entry point is recommended for most gameplay
    /// scenarios to get strong performance and quality without committing to a specific algorithm at
    /// call sites. If you need specific algorithm guarantees, construct that PRNG type directly
    /// instead.
    /// </para>
    /// <para>
    /// The default is chosen by quality first, then period, then speed: xoshiro256** is rated
    /// <c>Excellent</c> with a published 2^256-1 period and a published reference, and measured
    /// even with the previous default. Threading: The returned instance is thread-local, avoiding
    /// shared state and contention across threads.
    /// </para>
    /// </remarks>
    public static class PRNG
    {
        public static IRandom Instance => Xoshiro256StarStar.Instance;
    }
}
