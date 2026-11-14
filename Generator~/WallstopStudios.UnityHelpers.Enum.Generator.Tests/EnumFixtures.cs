// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Enum.Generator.Tests
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    [WEnumHelpers]
    internal enum SmallEnum
    {
        None = 0,
        First = 1,
        Second = 2,
        Third = 3,
    }

    [WEnumHelpers]
    internal enum ByteEnum : byte
    {
        None = 0,
        Low = 1,
        High = byte.MaxValue,
    }

    [WEnumHelpers]
    internal enum SignedByteEnum : sbyte
    {
        Minimum = sbyte.MinValue,
        Negative = -1,
        Zero = 0,
        Maximum = sbyte.MaxValue,
    }

    [WEnumHelpers]
    internal enum ShortEnum : short
    {
        Minimum = short.MinValue,
        Zero = 0,
        Maximum = short.MaxValue,
    }

    [WEnumHelpers]
    internal enum UnsignedShortEnum : ushort
    {
        Zero = 0,
        Maximum = ushort.MaxValue,
    }

    [WEnumHelpers]
    internal enum UnsignedIntEnum : uint
    {
        Zero = 0,
        High = 1U << 31,
        Maximum = uint.MaxValue,
    }

    [WEnumHelpers]
    internal enum LongEnum : long
    {
        Minimum = long.MinValue,
        Negative = -1,
        Zero = 0,
        Maximum = long.MaxValue,
    }

    [WEnumHelpers]
    internal enum UnsignedLongEnum : ulong
    {
        Zero = 0,
        High = 1UL << 63,
        Maximum = ulong.MaxValue,
    }

    [Flags]
    [WEnumHelpers]
    internal enum DenseFlags
    {
        None = 0,
        A = 1 << 0,
        B = 1 << 1,
        C = 1 << 2,
        D = 1 << 3,
        E = 1 << 4,
        F = 1 << 5,
        G = 1 << 6,
        H = 1 << 7,
        All = A | B | C | D | E | F | G | H,
        Composite = A | B,
    }

    [Flags]
    [WEnumHelpers]
    internal enum WideFlags : ulong
    {
        None = 0,
        A = 1UL << 0,
        B = 1UL << 8,
        C = 1UL << 16,
        D = 1UL << 24,
        E = 1UL << 32,
        F = 1UL << 40,
        G = 1UL << 48,
        H = 1UL << 63,
        All = A | B | C | D | E | F | G | H,
    }

    [Flags]
    [WEnumHelpers]
    internal enum SignedFlags : sbyte
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4,
        D = 8,
        E = 16,
        F = 32,
        G = 64,
        All = -1,
        Minimum = sbyte.MinValue,
    }

    [WEnumHelpers]
    internal enum AliasedEnum
    {
        None = 0,
        First = 1,
        Alias = 1,
        Last = 2,
    }

    [WEnumHelpers]
    internal enum EmptyEnum { }

    [WEnumHelpers]
    internal enum DisplayEnum
    {
        None = 0,

        [EnumDisplayName("Alpha")]
        A = 1,

        [EnumDisplayName("Beta Value")]
        B = 2,

        [EnumDisplayName(null)]
        C = 3,

        D = 4,
    }

    [WEnumHelpers]
    internal enum ObsoleteMemberEnum
    {
        None = 0,

        [Obsolete("Generated code must not reference this member.", true)]
        Retired = 1,

        Current = 2,
    }

    [WEnumHelpers]
    internal enum InternalVisibilityEnum
    {
        None = 0,
        Value = 1,
    }

    [WEnumHelpers]
    public enum PublicVisibilityEnum
    {
        None = 0,
        Value = 1,
    }

    internal static class NestedEnumHolder
    {
        [WEnumHelpers]
        internal enum NestedEnum
        {
            None = 0,
            Value = 1,
        }
    }
}
