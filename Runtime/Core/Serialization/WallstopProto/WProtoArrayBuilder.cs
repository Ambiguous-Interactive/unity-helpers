// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Collects the elements of an array-typed repeated member and hands back the array.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <remarks>
    /// <para>
    /// An array cannot be filled in place -- its length is fixed at construction and a repeated
    /// field's length is not known until the run ends -- so the read path collects elements and
    /// converts once. Doing that with a <c>List&lt;T&gt;</c> costs two allocations the returned
    /// array is not: every doubling of the list's buffer, and the copy <c>ToArray</c> makes out of
    /// the final one. Measured on <c>int[128]</c>: 1,744 bytes allocated to return a 560-byte
    /// graph, against protobuf-net's 560.
    /// </para>
    /// <para>
    /// This type removes both. <see cref="Reserve"/> takes the element count
    /// <see cref="WProtoReader.CountPackedElements"/> read off the encoded bytes, so the buffer is
    /// allocated once at exactly the size needed, and <see cref="ToArray"/> hands that buffer over
    /// rather than copying it when it came out exactly full. A run whose count is not known in
    /// advance -- an unpacked one, which is what protobuf-net writes -- still works, by doubling and
    /// copying, which is what the list did.
    /// </para>
    /// <para>
    /// A struct, and used only as a local in generated code, so building an array costs no object of
    /// its own. That makes <see cref="ToArray"/> handing over the internal buffer safe: nothing else
    /// holds a reference to the builder to observe it through.
    /// </para>
    /// </remarks>
    public struct WProtoArrayBuilder<T>
    {
        private const int MinimumGrowth = 4;

        private T[] _items;
        private int _count;

        /// <summary>
        /// Initializes a builder holding a copy of <paramref name="seed"/>.
        /// </summary>
        /// <param name="seed">The elements to start from; <c>null</c> starts empty.</param>
        /// <remarks>
        /// Reading a repeated field appends to what the contract's constructor left in the member,
        /// which for an array means starting from its current elements.
        /// </remarks>
        public WProtoArrayBuilder(T[] seed)
        {
            if (seed == null || seed.Length == 0)
            {
                _items = null;
                _count = 0;
                return;
            }

            _items = new T[seed.Length];
            Array.Copy(seed, _items, seed.Length);
            _count = seed.Length;
        }

        /// <summary>How many elements have been collected.</summary>
        public int Count => _count;

        /// <summary>
        /// Makes room for <paramref name="additional"/> more elements in one allocation.
        /// </summary>
        /// <param name="additional">How many elements are about to be added.</param>
        /// <remarks>
        /// A hint, never a requirement: a wrong or absent count costs the growth it would have cost
        /// anyway and changes nothing about what the builder produces.
        /// </remarks>
        public void Reserve(int additional)
        {
            if (additional <= 0)
            {
                return;
            }

            int required = _count + additional;
            if (_items != null && required <= _items.Length)
            {
                return;
            }

            Resize(required);
        }

        /// <summary>
        /// Appends one element.
        /// </summary>
        /// <param name="item">The element.</param>
        public void Add(T item)
        {
            if (_items == null || _count == _items.Length)
            {
                Resize(_count == 0 ? MinimumGrowth : _count * 2);
            }

            _items[_count] = item;
            _count++;
        }

        /// <summary>
        /// Appends every element of <paramref name="items"/>.
        /// </summary>
        /// <param name="items">The elements; <c>null</c> appends nothing.</param>
        public void AddRange(T[] items)
        {
            if (items == null || items.Length == 0)
            {
                return;
            }

            Reserve(items.Length);
            Array.Copy(items, 0, _items, _count, items.Length);
            _count += items.Length;
        }

        /// <summary>
        /// Appends every element of <paramref name="items"/>.
        /// </summary>
        /// <param name="items">The elements; <c>null</c> appends nothing.</param>
        /// <remarks>
        /// Typed as the concrete list rather than <see cref="IEnumerable{T}"/> because the one
        /// caller is a deferred read's pending list, and enumerating it through the interface would
        /// box the enumerator on every deserialization.
        /// </remarks>
        public void AddRange(List<T> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            Reserve(items.Count);
            /*
                CopyTo rather than an indexer loop: it is one memmove against a bounds-checked read
                and a bounds-checked write per element. Measured on 256 elements, 11.06 ns/op against
                76.33 -- 6.9x, and the cost scales with the collection this exists to size exactly.
            */
            items.CopyTo(_items, _count);
            _count += items.Count;
        }

        /// <summary>
        /// Produces the array of everything collected.
        /// </summary>
        /// <returns>The elements, in the order they were added.</returns>
        /// <remarks>
        /// Returns the internal buffer when it came out exactly full, which is the whole point of
        /// reserving an exact count, so this is the last call a builder should receive.
        /// </remarks>
        public T[] ToArray()
        {
            if (_count == 0)
            {
                return Array.Empty<T>();
            }

            if (_count == _items.Length)
            {
                return _items;
            }

            T[] exact = new T[_count];
            Array.Copy(_items, exact, _count);
            return exact;
        }

        /*
            Array.Resize is the shorter spelling and was measured against this one: the copy
            primitives are equivalent (2.85 ns/op against Span.CopyTo's 2.60 at 64 elements, 80.12
            against 79.55 at 4096 -- the same memmove either way), so the only difference left is HOW
            MUCH each copies. Array.Resize copies min(old.Length, newSize), the whole old buffer;
            this copies the live prefix, which is never more and is less whenever a reservation was
            not filled. Same speed for the same work, less work when they differ.
        */
        private void Resize(int capacity)
        {
            T[] grown = new T[capacity];
            if (_count != 0)
            {
                Array.Copy(_items, grown, _count);
            }

            _items = grown;
        }
    }
}
