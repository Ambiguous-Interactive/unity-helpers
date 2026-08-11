// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    // The seven collections Serializer has always marshalled through a wrapper POCO rather than
    // handing to protobuf-net as themselves, written for WallstopProto without reflection.
    //
    // Each one delegates to the wrapper contract's GENERATED formatter rather than emitting the
    // wrapper's fields itself. That is the property worth having: the bytes are the wrapper's by
    // construction, so a change to the wrapper contract cannot leave a hand-written copy of its
    // encoding behind. The only thing written here is the conversion, which is what the reflection
    // path spent MakeGenericType and Activator.CreateInstance on -- neither of which IL2CPP can run.
    //
    // The hook ordering follows IWProtoFormatter<T>'s contract exactly: whatever has to be staged
    // before the value can be measured is staged in Measure, never in Write, because a length prefix
    // is emitted from the measurement and a hook that ran twice would leak whatever it rented.

    /// <summary>
    /// Serializes a <see cref="SerializableHashSet{T}"/> root as its protobuf wrapper.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public sealed class SerializableHashSetMarshalFormatter<T>
        : IWProtoFormatter<SerializableHashSet<T>>
    {
        /// <inheritdoc />
        public int Measure(in SerializableHashSet<T> value)
        {
            value.OnBeforeSerialize();
            return SerializableHashSetProtoWrapper<T>.WProtoFormatter.Instance.Measure(Wrap(value));
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in SerializableHashSet<T> value)
        {
            return SerializableHashSetProtoWrapper<T>.WProtoFormatter.Instance.Write(
                ref writer,
                Wrap(value)
            );
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out SerializableHashSet<T> value)
        {
            if (
                !SerializableHashSetProtoWrapper<T>.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out SerializableHashSetProtoWrapper<T> wrapper
                )
            )
            {
                value = default;
                return false;
            }

            SerializableHashSet<T> restored = new SerializableHashSet<T>();
            restored._items = wrapper.Items;
            restored._preserveSerializedEntries = true;
            restored.OnAfterDeserialize();

            value = restored;
            return true;
        }

        private static SerializableHashSetProtoWrapper<T> Wrap(SerializableHashSet<T> value)
        {
            return new SerializableHashSetProtoWrapper<T> { Items = value.SerializedItems };
        }
    }

    /// <summary>
    /// Serializes a <see cref="SerializableSortedSet{T}"/> root as its protobuf wrapper.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public sealed class SerializableSortedSetMarshalFormatter<T>
        : IWProtoFormatter<SerializableSortedSet<T>>
        where T : IComparable<T>
    {
        /// <inheritdoc />
        public int Measure(in SerializableSortedSet<T> value)
        {
            value.OnBeforeSerialize();
            return SerializableSortedSetProtoWrapper<T>.WProtoFormatter.Instance.Measure(
                Wrap(value)
            );
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in SerializableSortedSet<T> value)
        {
            return SerializableSortedSetProtoWrapper<T>.WProtoFormatter.Instance.Write(
                ref writer,
                Wrap(value)
            );
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out SerializableSortedSet<T> value)
        {
            if (
                !SerializableSortedSetProtoWrapper<T>.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out SerializableSortedSetProtoWrapper<T> wrapper
                )
            )
            {
                value = default;
                return false;
            }

            SerializableSortedSet<T> restored = new SerializableSortedSet<T>();
            restored._items = wrapper.Items;
            restored._preserveSerializedEntries = true;
            restored.OnAfterDeserialize();

            value = restored;
            return true;
        }

        private static SerializableSortedSetProtoWrapper<T> Wrap(SerializableSortedSet<T> value)
        {
            return new SerializableSortedSetProtoWrapper<T> { Items = value.SerializedItems };
        }
    }

    /// <summary>
    /// Serializes a <see cref="SerializableDictionary{TKey, TValue}"/> root as its protobuf wrapper.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    public sealed class SerializableDictionaryMarshalFormatter<TKey, TValue>
        : IWProtoFormatter<SerializableDictionary<TKey, TValue>>
    {
        /// <inheritdoc />
        public int Measure(in SerializableDictionary<TKey, TValue> value)
        {
            value.OnBeforeSerialize();
            return SerializableDictionaryProtoWrapper<
                TKey,
                TValue
            >.WProtoFormatter.Instance.Measure(Wrap(value));
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in SerializableDictionary<TKey, TValue> value)
        {
            return SerializableDictionaryProtoWrapper<TKey, TValue>.WProtoFormatter.Instance.Write(
                ref writer,
                Wrap(value)
            );
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out SerializableDictionary<TKey, TValue> value)
        {
            if (
                !SerializableDictionaryProtoWrapper<TKey, TValue>.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out SerializableDictionaryProtoWrapper<TKey, TValue> wrapper
                )
            )
            {
                value = default;
                return false;
            }

            SerializableDictionary<TKey, TValue> restored =
                new SerializableDictionary<TKey, TValue>();
            restored._keys = wrapper.Keys;
            restored._values = wrapper.Values;
            restored._preserveSerializedEntries = true;
            restored.OnAfterDeserialize();

            value = restored;
            return true;
        }

        private static SerializableDictionaryProtoWrapper<TKey, TValue> Wrap(
            SerializableDictionary<TKey, TValue> value
        )
        {
            return new SerializableDictionaryProtoWrapper<TKey, TValue>
            {
                Keys = value.SerializedKeys,
                Values = value.SerializedValues,
            };
        }
    }

    /// <summary>
    /// Serializes a <see cref="SerializableSortedDictionary{TKey, TValue}"/> root as its protobuf
    /// wrapper.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    public sealed class SerializableSortedDictionaryMarshalFormatter<TKey, TValue>
        : IWProtoFormatter<SerializableSortedDictionary<TKey, TValue>>
        where TKey : IComparable<TKey>
    {
        /// <inheritdoc />
        public int Measure(in SerializableSortedDictionary<TKey, TValue> value)
        {
            value.OnBeforeSerialize();
            return SerializableSortedDictionaryProtoWrapper<
                TKey,
                TValue
            >.WProtoFormatter.Instance.Measure(Wrap(value));
        }

        /// <inheritdoc />
        public bool Write(
            ref WProtoWriter writer,
            in SerializableSortedDictionary<TKey, TValue> value
        )
        {
            return SerializableSortedDictionaryProtoWrapper<
                TKey,
                TValue
            >.WProtoFormatter.Instance.Write(ref writer, Wrap(value));
        }

        /// <inheritdoc />
        public bool TryRead(
            ref WProtoReader reader,
            out SerializableSortedDictionary<TKey, TValue> value
        )
        {
            if (
                !SerializableSortedDictionaryProtoWrapper<
                    TKey,
                    TValue
                >.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out SerializableSortedDictionaryProtoWrapper<TKey, TValue> wrapper
                )
            )
            {
                value = default;
                return false;
            }

            SerializableSortedDictionary<TKey, TValue> restored =
                new SerializableSortedDictionary<TKey, TValue>();
            restored._keys = wrapper.Keys;
            restored._values = wrapper.Values;
            restored._preserveSerializedEntries = true;
            restored.OnAfterDeserialize();

            value = restored;
            return true;
        }

        private static SerializableSortedDictionaryProtoWrapper<TKey, TValue> Wrap(
            SerializableSortedDictionary<TKey, TValue> value
        )
        {
            return new SerializableSortedDictionaryProtoWrapper<TKey, TValue>
            {
                Keys = value.SerializedKeys,
                Values = value.SerializedValues,
            };
        }
    }

    /// <summary>
    /// Serializes a <see cref="Deque{T}"/> root as its protobuf wrapper.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public sealed class DequeMarshalFormatter<T> : IWProtoFormatter<Deque<T>>
    {
        /// <inheritdoc />
        public int Measure(in Deque<T> value)
        {
            return DequeProtoWrapper<T>.WProtoFormatter.Instance.Measure(Wrap(value));
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in Deque<T> value)
        {
            return DequeProtoWrapper<T>.WProtoFormatter.Instance.Write(ref writer, Wrap(value));
        }

        /// <inheritdoc />
        /// <remarks>
        /// The capacity reconciliation mirrors <see cref="Deque{T}"/>'s own
        /// <c>[ProtoAfterDeserialization]</c> hook, so an empty deque keeps the capacity it was
        /// saved with and a non-empty one never under-allocates.
        /// </remarks>
        public bool TryRead(ref WProtoReader reader, out Deque<T> value)
        {
            if (
                !DequeProtoWrapper<T>.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out DequeProtoWrapper<T> wrapper
                )
            )
            {
                value = default;
                return false;
            }

            int itemCount = wrapper.Items?.Length ?? 0;
            int capacity = wrapper.Capacity;
            if (capacity <= 0)
            {
                capacity = 0 < itemCount ? itemCount : Deque<T>.DefaultCapacity;
            }

            if (capacity < itemCount)
            {
                capacity = itemCount;
            }

            Deque<T> restored = new Deque<T>(capacity);
            for (int index = 0; index < itemCount; index++)
            {
                restored.PushBack(wrapper.Items[index]);
            }

            value = restored;
            return true;
        }

        private static DequeProtoWrapper<T> Wrap(Deque<T> value)
        {
            return new DequeProtoWrapper<T> { Items = value.ToArray(), Capacity = value.Capacity };
        }
    }

    /// <summary>
    /// Serializes a <see cref="CyclicBuffer{T}"/> root as its protobuf wrapper.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    public sealed class CyclicBufferMarshalFormatter<T> : IWProtoFormatter<CyclicBuffer<T>>
    {
        /// <inheritdoc />
        public int Measure(in CyclicBuffer<T> value)
        {
            return CyclicBufferProtoWrapper<T>.WProtoFormatter.Instance.Measure(Wrap(value));
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in CyclicBuffer<T> value)
        {
            return CyclicBufferProtoWrapper<T>.WProtoFormatter.Instance.Write(
                ref writer,
                Wrap(value)
            );
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out CyclicBuffer<T> value)
        {
            if (
                !CyclicBufferProtoWrapper<T>.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out CyclicBufferProtoWrapper<T> wrapper
                )
            )
            {
                value = default;
                return false;
            }

            int itemCount = wrapper.Items?.Length ?? 0;
            int capacity = wrapper.Capacity;
            if (capacity < itemCount)
            {
                capacity = itemCount;
            }

            // The constructor fills oldest-to-newest, which is the order Wrap writes them in.
            value = new CyclicBuffer<T>(capacity, wrapper.Items);
            return true;
        }

        private static CyclicBufferProtoWrapper<T> Wrap(CyclicBuffer<T> value)
        {
            int count = value.Count;
            T[] items = null;
            if (0 < count)
            {
                items = new T[count];
                for (int index = 0; index < count; index++)
                {
                    items[index] = value[index];
                }
            }

            return new CyclicBufferProtoWrapper<T> { Items = items, Capacity = value.Capacity };
        }
    }

    /// <summary>
    /// Serializes a <see cref="SparseSet"/> root as its protobuf wrapper.
    /// </summary>
    public sealed class SparseSetMarshalFormatter : IWProtoFormatter<SparseSet>
    {
        /// <inheritdoc />
        public int Measure(in SparseSet value)
        {
            return SparseSetProtoWrapper.WProtoFormatter.Instance.Measure(Wrap(value));
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in SparseSet value)
        {
            return SparseSetProtoWrapper.WProtoFormatter.Instance.Write(ref writer, Wrap(value));
        }

        /// <inheritdoc />
        /// <remarks>
        /// A <see cref="SparseSet"/> needs a positive universe size, so a payload carrying none
        /// falls back to the smallest one that can hold the largest stored element.
        /// </remarks>
        public bool TryRead(ref WProtoReader reader, out SparseSet value)
        {
            if (
                !SparseSetProtoWrapper.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out SparseSetProtoWrapper wrapper
                )
            )
            {
                value = default;
                return false;
            }

            int itemCount = wrapper.Elements?.Length ?? 0;
            int capacity = wrapper.Capacity;
            if (capacity <= 0)
            {
                capacity = 1;
                for (int index = 0; index < itemCount; index++)
                {
                    int candidate = wrapper.Elements[index] + 1;
                    if (capacity < candidate)
                    {
                        capacity = candidate;
                    }
                }
            }

            SparseSet restored = new SparseSet(capacity);
            for (int index = 0; index < itemCount; index++)
            {
                restored.TryAdd(wrapper.Elements[index]);
            }

            value = restored;
            return true;
        }

        private static SparseSetProtoWrapper Wrap(SparseSet value)
        {
            return new SparseSetProtoWrapper
            {
                Elements = value.ToArray(),
                Capacity = value.Capacity,
            };
        }
    }
}
