// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
#if !SINGLE_THREADED
    using System.Threading;
#endif
    /// <summary>
    /// Provides a per-thread singleton instance for a given <see cref="IRandom"/> implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Many PRNGs are stateful and not thread-safe to share. This utility gives each thread its own instance,
    /// removing locking and state contention. Prefer accessing PRNGs via <c>ThreadLocalRandom&lt;T&gt;.Instance</c>
    /// or <see cref="PRNG.Instance"/> (which already uses a thread-local default) in multithreaded systems.
    /// </para>
    /// </remarks>
    public static class ThreadLocalRandom<T>
        where T : IRandom, new()
    {
#if SINGLE_THREADED
        public static readonly T Instance = new();
#else
        public static T Instance => RandomCache.Value;
        private static readonly ThreadLocal<T> RandomCache = new(() => new T());
#endif
    }
}
