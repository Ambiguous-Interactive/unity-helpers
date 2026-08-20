// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Threading;
    using Core.Extension;

    /// <summary>
    /// Opaque identifier for a specific effect application instance.
    /// Use to remove or refresh one instance without affecting others.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lifecycle: Handles are only created for <see cref="ModifierDurationType.Duration"/> and
    /// <see cref="ModifierDurationType.Infinite"/> effects. <see cref="ModifierDurationType.Instant"/> effects
    /// apply permanently and do not produce a handle.
    /// </para>
    /// <para>
    /// Problem solved: Distinguishes concurrent applications of the same <see cref="AttributeEffect"/>.
    /// You can remove a single stack while leaving others intact, and systems can track/refresh durations
    /// per instance using the handle ID.
    /// </para>
    /// <para>
    /// Contains:
    /// - A monotonically increasing unique <see cref="id"/>
    /// - A reference to the applied <see cref="effect"/>
    /// Equality and ordering are based solely on the ID.
    /// </para>
    /// <para>
    /// Usage patterns:
    /// <code>
    /// // Apply and store for later removal
    /// EffectHandle? maybe = target.ApplyEffect(slow);
    /// if (maybe.HasValue) _activeSlows.Add(maybe.Value);
    ///
    /// // Remove one instance (e.g., dispel one stack)
    /// if (_activeSlows.Count > 0) {
    ///     target.RemoveEffect(_activeSlows[0]);
    ///     _activeSlows.RemoveAt(0);
    /// }
    ///
    /// // Refreshing timed effects (EffectHandler already supports resetDurationOnReapplication)
    /// target.ApplyEffect(slow); // Reapplying can reset duration (if enabled)
    /// </code>
    /// </para>
    /// </remarks>
    [Serializable]
    public readonly struct EffectHandle
        : IEquatable<EffectHandle>,
            IComparable<EffectHandle>,
            IComparable
    {
        internal static long Id;

        /// <summary>
        /// The AttributeEffect associated with this handle.
        /// </summary>
        public readonly AttributeEffect effect;

        /// <summary>
        /// The unique identifier for this effect instance.
        /// This ID is globally unique and monotonically increasing.
        /// </summary>
        public readonly long id;

        /// <summary>
        /// Creates a new EffectHandle instance with a unique ID for the specified effect.
        /// </summary>
        /// <param name="effect">The AttributeEffect to create a handle for.</param>
        /// <returns>A new EffectHandle with a unique ID.</returns>
        public static EffectHandle CreateInstance(AttributeEffect effect)
        {
            return new EffectHandle(Interlocked.Increment(ref Id), effect);
        }

        internal static EffectHandle CreateInstanceInternal()
        {
            return new EffectHandle(Interlocked.Increment(ref Id), null);
        }

        private EffectHandle(long id, AttributeEffect effect)
        {
            this.id = id;
            this.effect = effect;
        }

        /// <summary>
        /// Compares this handle to another handle based on their IDs.
        /// </summary>
        /// <param name="other">The handle to compare with.</param>
        /// <returns>
        /// A value less than 0 if this handle's ID is less than <paramref name="other"/>;
        /// 0 if they are equal;
        /// a value greater than 0 if this handle's ID is greater than <paramref name="other"/>.
        /// </returns>
        public int CompareTo(EffectHandle other)
        {
            return id.CompareTo(other.id);
        }

        /// <summary>
        /// Compares this handle to an object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>
        /// The comparison result if <paramref name="obj"/> is an EffectHandle; otherwise, -1.
        /// </returns>
        public int CompareTo(object obj)
        {
            if (obj is EffectHandle other)
            {
                return CompareTo(other);
            }

            return obj == null ? 1 : -1;
        }

        /// <summary>
        /// Determines whether this handle equals the specified object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns><c>true</c> if the object is an EffectHandle with the same ID; otherwise, <c>false</c>.</returns>
        public override bool Equals(object obj)
        {
            return obj is EffectHandle other && Equals(other);
        }

        /// <summary>
        /// Determines whether this handle equals another handle by comparing their IDs.
        /// </summary>
        /// <param name="other">The handle to compare with.</param>
        /// <returns><c>true</c> if the IDs are equal; otherwise, <c>false</c>.</returns>
        public bool Equals(EffectHandle other)
        {
            return id == other.id;
        }

        /// <summary>
        /// Returns the hash code for this handle based on its ID.
        /// </summary>
        /// <returns>The hash code of the ID.</returns>
        public override int GetHashCode()
        {
            return id.GetHashCode();
        }

        /// <summary>
        /// Converts this handle to a JSON string representation.
        /// </summary>
        /// <returns>A JSON string representing this handle.</returns>
        public override string ToString()
        {
            return this.ToJson();
        }
    }
}
