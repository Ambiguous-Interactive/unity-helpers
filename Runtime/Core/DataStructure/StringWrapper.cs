// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
#if !SINGLE_THREADED
    using System.Collections.Concurrent;
#else
    using WallstopStudios.UnityHelpers.Core.Extension;
    using System.Collections.Generic;
#endif

    /// <summary>
    /// Flyweight cache that interns frequently reused strings to reduce allocations and dictionary lookups.
    /// Useful when you have a known set of keys and want reference equality semantics without hitting <see cref="string.Intern(string)"/>.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// StringWrapper key = StringWrapper.Get("Enemy/State/Alert");
    /// dictionary[key] = value;
    /// key.Dispose(); // optional – returns wrapper to cache when no longer needed
    /// ]]></code>
    /// </example>
    [Serializable]
    public sealed class StringWrapper
        : IEquatable<StringWrapper>,
            IComparable<StringWrapper>,
            IDisposable
    {
#if SINGLE_THREADED
        private static readonly Dictionary<string, StringWrapper> Cache = new();
#else
        private static readonly ConcurrentDictionary<string, StringWrapper> Cache = new();
#endif

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
        public static StringWrapper Get(string value)
        {
            if (value == null)
            {
                return null;
            }

            return Cache.GetOrAdd(value, key => new StringWrapper(key));
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

        public static int Clear()
        {
            int count = Cache.Count;
            Cache.Clear();
            return count;
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

        public void Dispose()
        {
            Remove(value);
        }
    }
}
