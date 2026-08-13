// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    /// <summary>
    /// Bounds the capacity a deserializer will allocate on behalf of a payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A length prefix is safe to allocate from because a reader refuses one longer than the bytes
    /// it actually holds: claiming a billion elements costs the sender a billion bytes. A
    /// <b>capacity</b> has no such backing. It is a bare number, and a payload of ten bytes can ask
    /// for a two-billion-element buffer -- 8 GB for a <c>Deque&lt;T&gt;</c>, 16 GB for a
    /// <c>SparseSet</c>, which is either an out-of-memory crash in a shipped player or a machine
    /// brought to its knees by a save file.
    /// </para>
    /// <para>
    /// So capacity is treated as a claim rather than an instruction: honored up to what the payload
    /// actually delivered, and beyond that only up to <see cref="MaximumRestoredCapacity"/>. The two
    /// verbs differ because the consequence of ignoring the claim differs. Where capacity is a
    /// performance hint the structure will re-derive by growing -- a deque's buffer, a bit set's
    /// word array -- <see cref="Clamp"/> keeps every delivered element and simply starts smaller.
    /// Where it is semantic -- a <c>SparseSet</c>'s universe decides which elements may be added
    /// later -- <see cref="TryAccept"/> refuses, because silently shrinking it would change how the
    /// restored object behaves rather than how it was allocated.
    /// </para>
    /// <para>
    /// The bound is deliberately generous and adjustable rather than clever: a game that genuinely
    /// persists a larger structure raises <see cref="MaximumRestoredCapacity"/> once at startup,
    /// which is a decision its own code makes about its own data, not one a payload makes.
    /// </para>
    /// </remarks>
    public static class SerializationCapacityLimits
    {
        /// <summary>
        /// The default ceiling on a capacity a payload may ask for beyond what it delivers.
        /// </summary>
        /// <remarks>
        /// 1,048,576 elements: roughly 8 MB for a <c>SparseSet</c>'s two index arrays and 8 MB for a
        /// reference-typed deque, which a shipped game can absorb, while a payload asking for a
        /// thousand times that no longer can.
        /// </remarks>
        public const int DefaultMaximumRestoredCapacity = 1 << 20;

        private static int _maximumRestoredCapacity = DefaultMaximumRestoredCapacity;

        /// <summary>
        /// The largest capacity a payload may ask for beyond the elements it carries.
        /// </summary>
        /// <value>
        /// Defaults to <see cref="DefaultMaximumRestoredCapacity"/>. A value below 1 is treated as 1,
        /// because a capacity of zero can hold nothing.
        /// </value>
        public static int MaximumRestoredCapacity
        {
            get => _maximumRestoredCapacity;
            set => _maximumRestoredCapacity = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Restores <see cref="MaximumRestoredCapacity"/> to its default.
        /// </summary>
        public static void ResetMaximumRestoredCapacity()
        {
            _maximumRestoredCapacity = DefaultMaximumRestoredCapacity;
        }

        /// <summary>
        /// Bounds a stated capacity for a structure that can grow later.
        /// </summary>
        /// <param name="statedCapacity">The capacity the payload asked for.</param>
        /// <param name="deliveredCount">How many elements the payload actually carried.</param>
        /// <returns>
        /// A capacity that holds every delivered element and never exceeds
        /// <see cref="MaximumRestoredCapacity"/> unless the delivered elements themselves do.
        /// </returns>
        /// <remarks>
        /// The delivered count is the floor rather than the cap, because those elements exist and
        /// have to fit; a payload carrying two million elements has already paid for them in bytes.
        /// </remarks>
        public static int Clamp(int statedCapacity, int deliveredCount)
        {
            int floor = deliveredCount < 0 ? 0 : deliveredCount;
            int ceiling = _maximumRestoredCapacity < floor ? floor : _maximumRestoredCapacity;
            if (statedCapacity < floor)
            {
                return floor;
            }

            return statedCapacity > ceiling ? ceiling : statedCapacity;
        }

        /// <summary>
        /// Decides whether a stated capacity may be honored for a structure whose capacity is part
        /// of its behavior.
        /// </summary>
        /// <param name="statedCapacity">The capacity the payload asked for.</param>
        /// <param name="deliveredCount">How many elements the payload actually carried.</param>
        /// <param name="capacity">Receives the capacity to use, or 0 when the claim is refused.</param>
        /// <returns><c>false</c> when the claim exceeds what this process will allocate for it.</returns>
        public static bool TryAccept(int statedCapacity, int deliveredCount, out int capacity)
        {
            int floor = deliveredCount < 0 ? 0 : deliveredCount;
            int ceiling = _maximumRestoredCapacity < floor ? floor : _maximumRestoredCapacity;
            if (statedCapacity > ceiling)
            {
                capacity = 0;
                return false;
            }

            capacity = statedCapacity < floor ? floor : statedCapacity;
            return true;
        }

        /// <summary>
        /// Builds the message reported when a payload's capacity claim is refused.
        /// </summary>
        /// <param name="type">The type being restored, for the message.</param>
        /// <param name="statedCapacity">The capacity the payload asked for.</param>
        /// <returns>The message.</returns>
        public static string Refusal(string type, int statedCapacity)
        {
            return $"A payload asked to restore a '{type}' with a capacity of {statedCapacity}, "
                + $"beyond the {MaximumRestoredCapacity} this process will allocate for a value it "
                + "has not received. A capacity is a claim rather than data -- allocating it would "
                + "let a few bytes ask for gigabytes. Raise "
                + $"'{nameof(SerializationCapacityLimits)}.{nameof(MaximumRestoredCapacity)}' if "
                + "this payload is trusted and genuinely that large.";
        }
    }
}
