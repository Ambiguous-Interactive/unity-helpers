// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [Category("Fast")]
    public sealed class BitOpsLookupTests
    {
        private static int ReferenceLog2(ulong value)
        {
            int result = 0;
            while (1 < value)
            {
                value >>= 1;
                ++result;
            }
            return result;
        }

        private static int ReferenceTrailingZeros(ulong value, int width)
        {
            if (value == 0)
            {
                return width;
            }
            int result = 0;
            while ((value & 1) == 0)
            {
                value >>= 1;
                ++result;
            }
            return result;
        }

        [TestCase(0)]
        [TestCase(16)]
        [TestCase(32)]
        [TestCase(48)]
        public void LookupsMatchEverySixteenBitWindow(int shift)
        {
            for (ulong window = 0; window < 65536; ++window) // UNH-SUPPRESS: exhaustive scalar checks avoid per-value assertion allocations.
            {
                ulong value = window << shift;
                int logarithm = ReferenceLog2(value);
                int trailing = ReferenceTrailingZeros(value, 64);
                if (
                    BitOps.Log2(value) != logarithm
                    || BitOps.Log2(unchecked((long)value)) != logarithm
                    || BitOps.TrailingZeroCount(value) != trailing
                    || BitOps.TrailingZeroCount(unchecked((long)value)) != trailing
                )
                {
                    Assert.Fail($"The 64-bit lookup disagreed for {value} at shift {shift}.");
                }
                if (shift < 32)
                {
                    uint narrow = (uint)value;
                    int narrowTrailing = ReferenceTrailingZeros(narrow, 32);
                    if (
                        BitOps.Log2(narrow) != logarithm
                        || BitOps.Log2(unchecked((int)narrow)) != logarithm
                        || BitOps.TrailingZeroCount(narrow) != narrowTrailing
                        || BitOps.TrailingZeroCount(unchecked((int)narrow)) != narrowTrailing
                    )
                    {
                        Assert.Fail($"The 32-bit lookup disagreed for {narrow} at shift {shift}.");
                    }
                }
            }
        }
    }
}
