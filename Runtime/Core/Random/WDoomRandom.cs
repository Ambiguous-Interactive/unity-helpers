// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// Clean-room. This file is inspired by the fixed-table generator DOOM (id Software, 1993) made
// famous -- draw from a constant table, advance a wrapping index -- which is a technique, not
// expression, and so is nobody's to license. No DOOM source was read or copied while writing it,
// and none of DOOM's data appears in it: the table is this package's own blend, a permutation of
// 0-255 shuffled by SplitMix64 from a fixed seed and built at type load, and it is a permutation
// where DOOM's table is not. Every line here is MIT under this package's own license.
// See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using Extension;
    using Helper;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A table-walk generator in the style DOOM made famous: a 256-byte table read one entry at a
    /// time by an index that wraps at 256.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a novelty, and a useful one: its whole state is a single byte index, so a saved game
    /// restores the exact sequence, replays are trivially reproducible, and the cost of a draw is one
    /// array read. That is also its limit -- it returns each of 256 table entries in a fixed order and
    /// repeats forever, so it is not random in any statistical sense.
    /// </para>
    /// <para>
    /// Clean-room, and MIT throughout. DOOM's table is GPL-2.0 data and does not appear here: the 256
    /// entries are this package's own blend, a permutation of 0-255 shuffled by
    /// <see cref="SplitMix64"/> from a fixed seed and built once at type load. A full cycle therefore
    /// emits every byte value exactly once, which DOOM's table does not do. Only the technique is
    /// borrowed, and a technique is not something anyone licenses.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Fastest draw in the package: one masked increment and one array read.</description></item>
    /// <item><description>One byte of state, so a save file records the whole generator.</description></item>
    /// <item><description>Exactly reproducible, which is what a replay or a deterministic test wants.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Period of 256 bytes. Every distribution it produces repeats.</description></item>
    /// <item><description>Fails any statistical test worth running; do not use it for sampling or simulation.</description></item>
    /// <item><description>Not cryptographically secure, not close.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>Deliberate retro feel, deterministic replays, teaching, or a fixed jitter table.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Anything that needs randomness rather than variety. Reach for <see cref="PcgRandom"/> or <see cref="RomuDuo"/>.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// WDoomRandom rng = new(seedIndex: 0);
    /// int damage = 5 * ((rng.Next(256) % 10) + 1); // the shape of DOOM's damage rolls
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Poor,
        "Fixed 256-entry lookup table walked in order. Period 256; deterministic by design, not statistically random. Clean-room: the table is this package's own permutation of 0-255.",
        "Technique popularized by DOOM (1993)",
        "https://doomwiki.org/wiki/Random_number_generator"
    )]
    [Serializable]
    [DataContract]
    [ProtoContract]
    [WProtoContract]
    public sealed partial class WDoomRandom
        : AbstractRandom,
            IEquatable<WDoomRandom>,
            IComparable,
            IComparable<WDoomRandom>
    {
        private const int TableSize = 256;
        private const ulong TableSeed = 0x1D00_0000_1D00_0000UL;

        private static readonly byte[] Table = BuildTable();

        public static WDoomRandom Instance => ThreadLocalRandom<WDoomRandom>.Instance;

        public override RandomState InternalState => BuildState(_index);

        [ProtoMember(6)]
        [WProtoMember(6)]
        internal ulong _index;

        public WDoomRandom()
            : this(Guid.NewGuid()) { }

        public WDoomRandom(Guid guid)
        {
            (ulong a, ulong b) = RandomUtilities.GuidToUInt64Pair(guid);
            _index = (a ^ b) & (TableSize - 1);
        }

        public WDoomRandom(int seedIndex)
        {
            _index = (ulong)(seedIndex & (TableSize - 1));
        }

        [JsonConstructor]
        public WDoomRandom(RandomState internalState)
        {
            _index = internalState.State1 & (TableSize - 1);
            RestoreCommonState(internalState);
        }

        /// <summary>
        /// The 256 table entries, in the order the generator walks them.
        /// </summary>
        public static ReadOnlySpan<byte> LookupTable => Table;

        public override uint NextUint()
        {
            unchecked
            {
                // The table holds bytes, so a uint is four draws and the index advances four times.
                uint value = 0;
                for (int i = 0; i < 4; ++i)
                {
                    _index = (_index + 1) & (TableSize - 1);
                    value = (value << 8) | Table[(int)_index];
                }

                return value;
            }
        }

        /// <summary>
        /// Draws a single table entry, the byte-at-a-time draw this generator's style is named for.
        /// </summary>
        /// <returns>The next byte in the table, 0 through 255.</returns>
        public byte NextTableByte()
        {
            unchecked
            {
                _index = (_index + 1) & (TableSize - 1);
                return Table[(int)_index];
            }
        }

        public override IRandom Copy()
        {
            return new WDoomRandom(InternalState);
        }

        // A permutation rather than 256 arbitrary bytes: over a full cycle every byte value comes out
        // exactly once, which DOOM's table does not do and nothing here needs to copy it to get.
        private static byte[] BuildTable()
        {
            byte[] table = new byte[TableSize];
            for (int i = 0; i < TableSize; ++i)
            {
                table[i] = (byte)i;
            }

            SplitMix64 source = new(TableSeed);
            for (int i = TableSize - 1; i > 0; --i)
            {
                int swap = (int)(source.NextUint() % (uint)(i + 1));
                (table[i], table[swap]) = (table[swap], table[i]);
            }

            return table;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as WDoomRandom);
        }

        public bool Equals(WDoomRandom other)
        {
            if (other == null)
            {
                return false;
            }

            return _index == other._index;
        }

        public override int GetHashCode()
        {
            return Objects.HashCode(_index);
        }

        public override string ToString()
        {
            return this.ToJson();
        }

        public int CompareTo(object obj)
        {
            return CompareTo(obj as WDoomRandom);
        }

        public int CompareTo(WDoomRandom other)
        {
            if (other == null)
            {
                return -1;
            }

            return _index.CompareTo(other._index);
        }
    }
}
