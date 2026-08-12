// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections;
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Stands in for the package's wrapper-marshalled collections: a collection whose ROOT encoding
    /// is a wrapper of items-plus-capacity and whose MEMBER encoding is an ordinary repeated field.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <remarks>
    /// The two encodings are the whole point of a root marshal, and they are not a quirk of this
    /// fixture: <c>Serializer.ProtoSerialize(aDeque)</c> has always written
    /// <c>DequeProtoWrapper</c>, while the same deque held as a member of another contract is
    /// written by protobuf-net's list handling. This type is <see cref="ICollection{T}"/> so
    /// protobuf-net and the generator both reach the member case the same way the real ones do.
    /// </remarks>
    public sealed class StandInRing<T> : ICollection<T>
    {
        private readonly List<T> _items = new List<T>();

        /// <summary>The capacity carried in the wrapper and dropped by the member encoding.</summary>
        public int Capacity { get; set; }

        /// <inheritdoc />
        public int Count => _items.Count;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public void Add(T item)
        {
            _items.Add(item);
        }

        /// <inheritdoc />
        public void Clear()
        {
            _items.Clear();
        }

        /// <inheritdoc />
        public bool Contains(T item)
        {
            return _items.Contains(item);
        }

        /// <inheritdoc />
        public void CopyTo(T[] array, int arrayIndex)
        {
            _items.CopyTo(array, arrayIndex);
        }

        /// <inheritdoc />
        public bool Remove(T item)
        {
            return _items.Remove(item);
        }

        /// <inheritdoc />
        public IEnumerator<T> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        internal T[] ToArray()
        {
            return 0 < _items.Count ? _items.ToArray() : null;
        }

        /// <summary>How many times <see cref="OnBeforeSerialize"/> has run.</summary>
        /// <remarks>
        /// The real collections stage their serialized array in a callback, so the marshal has to
        /// run it once -- from Measure, per <c>IWProtoFormatter&lt;T&gt;</c>'s hook contract -- and
        /// not again from Write. A count is the only way to tell "ran once" from "ran twice with an
        /// idempotent body", and twice is what leaks a rental in a hook that pools.
        /// </remarks>
        public int StageCount { get; private set; }

        /// <summary>Stages the elements the wrapper is written from.</summary>
        public void OnBeforeSerialize()
        {
            StageCount++;
            Staged = ToArray();
        }

        /// <summary>The staged elements, or <c>null</c> before staging.</summary>
        internal T[] Staged { get; private set; }
    }

    /// <summary>The wire shape a <see cref="StandInRing{T}"/> root is written as.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class StandInRingWrapper<T>
    {
        /// <summary>The elements, in order.</summary>
        [ProtoMember(1, OverwriteList = true)]
        [WProtoMember(1, OverwriteList = true)]
        public T[] Items;

        /// <summary>The capacity the collection was saved with.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public int Capacity;
    }

    /// <summary>Writes a <see cref="StandInRing{T}"/> root as its wrapper.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    public sealed class StandInRingMarshalFormatter<T>
        : IWProtoFormatter<StandInRing<T>>,
            IWProtoConditionalFormatter
    {
        /// <inheritdoc />
        public bool CanServe()
        {
            return WProtoGeneric<T>.CanEncode;
        }

        /// <inheritdoc />
        public int Measure(in StandInRing<T> value)
        {
            value.OnBeforeSerialize();
            return StandInRingWrapper<T>.WProtoFormatter.Instance.Measure(Wrap(value));
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in StandInRing<T> value)
        {
            return StandInRingWrapper<T>.WProtoFormatter.Instance.Write(ref writer, Wrap(value));
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out StandInRing<T> value)
        {
            if (
                !StandInRingWrapper<T>.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out StandInRingWrapper<T> wrapper
                )
            )
            {
                value = default;
                return false;
            }

            value = new StandInRing<T> { Capacity = wrapper.Capacity };
            if (wrapper.Items != null)
            {
                foreach (T item in wrapper.Items)
                {
                    value.Add(item);
                }
            }

            return true;
        }

        private static StandInRingWrapper<T> Wrap(StandInRing<T> value)
        {
            return new StandInRingWrapper<T> { Items = value.Staged, Capacity = value.Capacity };
        }
    }

    /// <summary>Stands in for the one non-generic marshal the package ships, <c>SparseSet</c>.</summary>
    public sealed class StandInBag
    {
        /// <summary>The elements, in order.</summary>
        public List<int> Elements { get; } = new List<int>();
    }

    /// <summary>The wire shape a <see cref="StandInBag"/> root is written as.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class StandInBagWrapper
    {
        /// <summary>The elements, in order.</summary>
        [ProtoMember(1, OverwriteList = true)]
        [WProtoMember(1, OverwriteList = true)]
        public int[] Elements;
    }

    /// <summary>Writes a <see cref="StandInBag"/> root as its wrapper.</summary>
    public sealed class StandInBagMarshalFormatter : IWProtoFormatter<StandInBag>
    {
        /// <inheritdoc />
        public int Measure(in StandInBag value)
        {
            return StandInBagWrapper.WProtoFormatter.Instance.Measure(Wrap(value));
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in StandInBag value)
        {
            return StandInBagWrapper.WProtoFormatter.Instance.Write(ref writer, Wrap(value));
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out StandInBag value)
        {
            if (
                !StandInBagWrapper.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out StandInBagWrapper wrapper
                )
            )
            {
                value = default;
                return false;
            }

            value = new StandInBag();
            if (wrapper.Elements != null)
            {
                value.Elements.AddRange(wrapper.Elements);
            }

            return true;
        }

        private static StandInBagWrapper Wrap(StandInBag value)
        {
            return new StandInBagWrapper
            {
                Elements = 0 < value.Elements.Count ? value.Elements.ToArray() : null,
            };
        }
    }

    /// <summary>
    /// An element type WallstopProto cannot encode: no scalar formatter, no contract.
    /// </summary>
    /// <remarks>
    /// Stands in for two real cases. A type protobuf-net reaches through a <b>surrogate</b> —
    /// <c>UnityEngine.Vector2</c> — has no runtime formatter of its own, because a surrogate is
    /// substituted at generate time and a type parameter is not known then. And an <b>enum</b> is
    /// resolved the same way, so a closure over one has nothing to resolve either. Both appear in
    /// this package's own sources as <c>Deque&lt;Vector2&gt;</c> and dictionaries keyed by enums.
    /// </remarks>
    public struct Unserviceable
    {
        /// <summary>Ordinary data; the point is that nothing can encode it.</summary>
        public int Value;
    }

    /// <summary>A contract whose SUBTYPE is the one carrying a type parameter.</summary>
    /// <remarks>
    /// The entry point registered for <c>ConditionalSub&lt;T&gt;</c> is its root formatter, which
    /// delegates to this type. Asking only this type whether it can serve would miss the subtype's
    /// own parameter entirely.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    [WProtoInclude(100, typeof(ConditionalSub<Unserviceable>))]
    public partial class ConditionalBase
    {
        /// <summary>An ordinary member, always encodable.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Id;
    }

    /// <summary>A generic subtype whose own member is the one that may not be encodable.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    [ProtoContract]
    [WProtoContract]
    public partial class ConditionalSub<T> : ConditionalBase
    {
        /// <summary>The member whose closure decides whether this can be served.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public T Value;
    }

    /// <summary>Holds a marshalled collection as a MEMBER, where the marshal must not apply.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class RingHolder
    {
        /// <summary>The collection, written as an ordinary repeated field.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public StandInRing<int> Ring;

        /// <summary>A scalar after it, so a length mistake in the member shows up as drift.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public int Trailer;
    }
}
