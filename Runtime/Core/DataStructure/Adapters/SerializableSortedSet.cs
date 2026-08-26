// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    /// <summary>
    /// Sorted set wrapper that keeps ordering intact across Unity, ProtoBuf, and JSON serialization.
    /// Ideal for deterministic gameplay systems that need sorted iteration but still want to save or inspect data.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// SerializableSortedSet<int> highscores = new SerializableSortedSet<int>(new[] { 100, 250, 420 });
    /// highscores.Add(360);
    /// foreach (int score in highscores)
    /// {
    ///     Debug.Log(score);
    /// }
    /// ]]></code>
    /// </example>
    /// <remarks>
    /// The <see cref="IComparable{T}"/> constraint is load-bearing and must not be relaxed to match
    /// <see cref="SerializableHashSet{T}"/>. It is what turns a set of collections
    /// (<c>SerializableSortedSet&lt;List&lt;float&gt;&gt;</c>) into a compile error at the field
    /// declaration rather than a set that cannot order its own elements: <c>List&lt;T&gt;</c> is not
    /// <c>IComparable&lt;List&lt;T&gt;&gt;</c>, and without the constraint the failure would surface
    /// at runtime, far from the declaration that caused it.
    /// </remarks>
    [Serializable]
    public class SerializableSortedSet<T> : SerializableSetBase<T, SortedSet<T>>
        where T : IComparable<T>
    {
        /// <summary>
        /// Initializes an empty sorted set that can participate in Unity and ProtoBuf serialization.
        /// </summary>
        public SerializableSortedSet()
            : base(new StorageSet()) { }

        /// <summary>
        /// Initializes the set with the provided items.
        /// </summary>
        /// <param name="collection">Sequence whose elements are copied into the set.</param>
        public SerializableSortedSet(IEnumerable<T> collection)
            : base(new StorageSet(collection)) { }

        /// <summary>
        /// Initializes the set during custom serialization scenarios.
        /// </summary>
        protected SerializableSortedSet(SerializationInfo info, StreamingContext context)
            : base(
                info,
                context,
                (serializationInfo, streamingContext) =>
                    new StorageSet(serializationInfo, streamingContext)
            ) { }

        /// <summary>
        /// Creates a new <see cref="global::System.Collections.Generic.SortedSet{T}"/> populated with this set's contents.
        /// </summary>
        /// <returns>A copy of the sorted set's current state.</returns>
        public SortedSet<T> ToSortedSet()
        {
            SortedSet<T> copy = new(Set, Set.Comparer);
            return copy;
        }

        /// <summary>
        /// Returns an enumerator that iterates through the set in sorted order.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedSet<int> highscores = new SerializableSortedSet<int>();
        /// SortedSet<int>.Enumerator enumerator = highscores.GetEnumerator();
        /// while (enumerator.MoveNext())
        /// {
        ///     Debug.Log(enumerator.Current);
        /// }
        /// ]]></code>
        /// </example>
        public SortedSet<T>.Enumerator GetEnumerator()
        {
            return Set.GetEnumerator();
        }

        /// <summary>
        /// Gets the smallest value stored in the set.
        /// </summary>
        public T Min => Set.Min;

        /// <summary>
        /// Gets the largest value stored in the set.
        /// </summary>
        public T Max => Set.Max;

        /// <summary>
        /// Enumerates the set in descending order without allocating a copy.
        /// </summary>
        /// <returns>Lazy enumerable that yields items from greatest to least.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedSet<int> highscores = new SerializableSortedSet<int>();
        /// foreach (int score in highscores.Reverse())
        /// {
        ///     Debug.Log(score);
        /// }
        /// ]]></code>
        /// </example>
        public IEnumerable<T> Reverse()
        {
            return Set.Reverse();
        }

        /// <summary>
        /// Produces a view of the set constrained between the provided lower and upper bounds.
        /// </summary>
        /// <param name="lowerValue">Inclusive lower bound.</param>
        /// <param name="upperValue">Inclusive upper bound.</param>
        /// <returns>A view that reflects mutations made to the underlying set.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableSortedSet<int> highscores = new SerializableSortedSet<int>(new int[] { 100, 250, 420 });
        /// SortedSet<int> topScores = highscores.GetViewBetween(200, 500);
        /// ]]></code>
        /// </example>
        public SortedSet<T> GetViewBetween(T lowerValue, T upperValue)
        {
            return Set.GetViewBetween(lowerValue, upperValue);
        }

        /// <inheritdoc/>
        protected override int RemoveWhereInternal(Predicate<T> match)
        {
            return Set.RemoveWhere(match);
        }

        /// <inheritdoc/>
        protected override bool TryGetValueCore(T equalValue, out T actualValue)
        {
            return Set.TryGetValue(equalValue, out actualValue);
        }

        protected override bool SupportsSorting => true;

        private sealed class StorageSet : SortedSet<T>
        {
            /// <summary>
            /// Initializes an empty storage set using the default comparer.
            /// </summary>
            public StorageSet() { }

            /// <summary>
            /// Initializes the storage set with the provided collection.
            /// </summary>
            /// <param name="collection">Elements copied into the sorted set.</param>
            public StorageSet(IEnumerable<T> collection)
                : base(collection ?? Array.Empty<T>()) { }

            /// <summary>
            /// Deserialization constructor used by <see cref="ISerializable"/>.
            /// </summary>
            /// <param name="info">Serialized data describing the set.</param>
            /// <param name="context">Context describing the serialization source.</param>
            public StorageSet(SerializationInfo info, StreamingContext context)
                : base(info, context) { }
        }
    }
}
