// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// Clean-room. An "index-into-array" random number generator, inspired by id's original DOOM
// technique. No third-party code or data is used: the table is this package's own permutation of
// 0-255, shuffled by SplitMix64 from a fixed seed and built at type load.

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
    /// An index-into-array generator: a 256-entry table read one value at a time by an index that
    /// wraps at 256.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a novelty, and a useful one: its whole state is a single byte index, so a saved game
    /// restores the exact sequence, replays are trivially reproducible, and the cost of a draw is one
    /// array read. That is also its limit -- it returns each of 256 table entries in a fixed order and
    /// repeats forever, so it is not random in any statistical sense.
    /// </para>
    /// <para>
    /// The table is this package's own: a permutation of 0-255 shuffled by <see cref="SplitMix64"/>
    /// from a fixed seed and built once at type load, so a full cycle emits every byte value exactly
    /// once.
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
    /// int damage = 5 * ((rng.Next(256) % 10) + 1); // a table-driven damage roll
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Poor,
        "Index-into-array generator over a fixed 256-entry table. The table holds bytes, so the period is 256 byte draws -- measured as exactly 64 draws of NextUint, which consumes four. Deterministic by design, not statistically random.",
        "",
        ""
    )]
    [Serializable]
    [DataContract]
    // SkipConstructor for the same reason the other generators carry it: the parameterless
    // constructor seeds from a fresh Guid, and index 0 -- the one state proto omits, because it
    // equals the type's default -- would otherwise come back as whatever that Guid invented.
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
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

        public override RandomState InternalState => BuildState((ulong)_index);

        // An index into a 256-entry table, and nothing else. A wider field would claim state this
        // generator does not have.
        [ProtoMember(6)]
        [WProtoMember(6)]
        internal int _index;

        public WDoomRandom()
            : this(Guid.NewGuid()) { }

        public WDoomRandom(Guid guid)
        {
            (ulong a, ulong b) = RandomUtilities.GuidToUInt64Pair(guid);
            _index = (int)((a ^ b) % TableSize);
        }

        public WDoomRandom(int seedIndex)
        {
            _index = seedIndex.PositiveMod(TableSize);
        }

        [JsonConstructor]
        public WDoomRandom(RandomState internalState)
        {
            _index = (int)(internalState.State1 % TableSize);
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
                    _index = _index.WrappedIncrement(TableSize);
                    value = (value << 8) | Table[_index];
                }

                return value;
            }
        }

        /// <summary>
        /// Draws a single table entry, which is this generator's natural unit.
        /// </summary>
        /// <returns>The next byte in the table, 0 through 255.</returns>
        public byte NextTableByte()
        {
            // WrappedIncrement, not a mask: the wrap stays correct if the table ever stops being a
            // power of two in length, and it is a comparison either way.
            _index = _index.WrappedIncrement(TableSize);
            return Table[_index];
        }

        public override IRandom Copy()
        {
            return new WDoomRandom(InternalState);
        }

        // A permutation rather than 256 arbitrary bytes, so a full cycle emits every value exactly once.
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
                return 1;
            }

            return _index.CompareTo(other._index);
        }
    }
}
