// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Threading;

    /// <summary>
    /// Flyweight cache that interns frequently reused strings to reduce allocations and dictionary lookups.
    /// Useful when you have a known set of keys and want reference equality semantics without hitting <see cref="string.Intern(string)"/>.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// StringWrapper key = StringWrapper.Get("Enemy/State/Alert");
    /// dictionary[key] = value;
    /// StringWrapper.Remove("Enemy/State/Alert"); // explicit administration, not per-borrower
    /// ]]></code>
    /// </example>
    /// <remarks>
    /// <para><b>Wrappers are shared.</b> Every caller asking for the same string gets the same
    /// instance, so dropping one is an administrative act on behalf of all of them. That is why
    /// <see cref="Dispose"/> is an obsolete no-op rather than an eviction: a <c>using</c> block
    /// around one borrower used to invalidate the entry every other borrower was reading.</para>
    /// <para><b>The cache is bounded, and eviction is silent.</b> It holds at most
    /// <see cref="MaxCachedWrappers"/> strings, evicting the least recently requested one to stay
    /// there, because a caller that wraps a value derived from gameplay -- an entity id, a save
    /// slot name, a <c>$"{prefix}:{index}"</c> -- would otherwise keep every string it ever built
    /// alive for the life of the process. Eviction costs one allocation on the next
    /// <see cref="Get"/> for that string and nothing else: equality and hashing are by ordinal
    /// value, so a wrapper handed out after an eviction still equals one handed out before it, and
    /// nothing in this type's contract depends on reference identity. Set
    /// <see cref="MaxCachedWrappers"/> to 0 or less to restore unbounded retention.</para>
    /// </remarks>
    [Serializable]
    public sealed class StringWrapper
        : IEquatable<StringWrapper>,
            IComparable<StringWrapper>,
            IDisposable
    {
        /// <summary>
        /// The default bound on distinct strings the cache retains.
        /// </summary>
        /// <remarks>
        /// Sized above the "known set of keys" this type documents itself for -- animator state
        /// names, tags, layer names, localization ids -- so an intended use never reaches the
        /// cliff, and far below the point where retaining wrappers costs real memory. Bounded is
        /// the requirement; this number only decides where the cliff is.
        /// </remarks>
        public const int DefaultMaxCachedWrappers = 4096;

        private static readonly Cache<string, StringWrapper> Cache = CacheBuilder<
            string,
            StringWrapper
        >
            .NewBuilder()
            .MaximumSize(DefaultMaxCachedWrappers)
            .InitialCapacity(16)
            .Build();

        private static int _maxCachedWrappers = DefaultMaxCachedWrappers;
        private static readonly object CacheResizeLock = new();

        /// <summary>
        /// Gets or sets how many distinct strings the cache retains. A value of 0 or less removes
        /// the bound.
        /// </summary>
        /// <remarks>
        /// Changing the value resizes the live cache. Lowering it evicts least-recently-used
        /// entries immediately; a value of 0 or less removes the bound without clearing entries.
        /// </remarks>
        public static int MaxCachedWrappers
        {
            get => Volatile.Read(ref _maxCachedWrappers);
            set
            {
                lock (CacheResizeLock)
                {
                    Cache.Resize(value);
                    Volatile.Write(ref _maxCachedWrappers, value);
                }
            }
        }

        /// <summary>
        /// The number of strings the cache currently holds.
        /// </summary>
        public static int CachedCount => Cache.Count;

        public readonly string value;

        private readonly int _hashCode;

        private StringWrapper(string value)
        {
            this.value = value;
            _hashCode = value.GetHashCode();
        }

        /// <summary>
        /// Returns the cached wrapper for a string, creating it on first use.
        /// </summary>
        /// <param name="value">The string to wrap.</param>
        /// <returns>The shared wrapper, or <c>null</c> when <paramref name="value"/> is null.</returns>
        /// <remarks>
        /// Caching is bounded by <see cref="MaxCachedWrappers"/>; a string evicted since the last
        /// call is wrapped again rather than returned from the cache.
        /// </remarks>
        public static StringWrapper Get(string value)
        {
            if (value == null)
            {
                return null;
            }

            return Cache.GetOrAdd(value, static key => new StringWrapper(key));
        }

        /// <summary>
        /// Drops a string's wrapper from the cache.
        /// </summary>
        /// <param name="value">The string whose wrapper should be dropped.</param>
        /// <returns><c>true</c> when a wrapper was removed.</returns>
        public static bool Remove(string value)
        {
            if (value == null)
            {
                return false;
            }

            return Cache.TryRemove(value, out _);
        }

        /// <summary>
        /// Drops every cached wrapper.
        /// </summary>
        /// <returns>The number of wrappers dropped.</returns>
        public static int Clear()
        {
            int count = Cache.Count;
            Cache.Clear();
            return count;
        }

        internal static bool IsCachedForTesting(string value)
        {
            return value != null && Cache.ContainsKey(value);
        }

        public bool Equals(StringWrapper other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (ReferenceEquals(other, null))
            {
                return false;
            }

            if (_hashCode != other._hashCode)
            {
                return false;
            }

            return string.Equals(value, other.value, StringComparison.Ordinal);
        }

        /// <summary>
        /// Orders wrappers by their wrapped string, using ordinal comparison.
        /// </summary>
        /// <param name="other">The wrapper to compare against.</param>
        /// <returns>A negative value when this wrapper orders first, positive when it orders last, zero when neither does.</returns>
        /// <remarks>
        /// Ordering the hash first would be a valid total order but an arbitrary one, and hash codes
        /// are not required to be stable across processes, so the order would not be either.
        /// Null orders first, matching every <see cref="IComparable{T}"/> in the framework.
        /// </remarks>
        public int CompareTo(StringWrapper other)
        {
            if (ReferenceEquals(this, other))
            {
                return 0;
            }

            if (ReferenceEquals(other, null))
            {
                return 1;
            }

            return string.CompareOrdinal(value, other.value);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object other)
        {
            return Equals(other as StringWrapper);
        }

        public override string ToString()
        {
            return value;
        }

        /// <summary>
        /// Does nothing. The wrapper it would have dropped is shared with every other holder of the
        /// same string, so one borrower's <c>using</c> block used to evict an entry the rest were
        /// still reading through. Administer the cache explicitly with <see cref="Remove"/> or
        /// <see cref="Clear"/>.
        /// </summary>
        [Obsolete(
            "StringWrapper.Dispose does nothing: the wrapper is shared, so disposing one borrower's handle evicted an entry others still held. Use StringWrapper.Remove or StringWrapper.Clear. Removed in 4.0."
        )]
        public void Dispose() { }
    }
}
