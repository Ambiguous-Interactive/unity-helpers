// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [Category("Performance")]
    public sealed class BitOpsLookupTimingTests
    {
        private static int Reference(ulong value, int operation)
        {
            if (operation == 2)
            {
                if (value == 0)
                {
                    return 64;
                }
                int count = 0;
                while ((value & 1) == 0)
                {
                    ++count;
                    value >>= 1;
                }
                return count;
            }
            int result = 0;
            while (1 < value)
            {
                ++result;
                value >>= 1;
            }
            return result;
        }

        private static long Measure(ulong[] values, int operation, int batches, long expected)
        {
            long checksum = 0;
            long start = Stopwatch.GetTimestamp();
            switch (operation)
            {
                case 0:
                    for (int batch = 0; batch < batches; ++batch)
                    {
                        foreach (ulong value in values)
                        {
                            checksum += BitOps.Log2((uint)value);
                        }
                    }
                    break;
                case 1:
                    for (int batch = 0; batch < batches; ++batch)
                    {
                        foreach (ulong value in values)
                        {
                            checksum += BitOps.Log2(value);
                        }
                    }
                    break;
                default:
                    for (int batch = 0; batch < batches; ++batch)
                    {
                        foreach (ulong value in values)
                        {
                            checksum += BitOps.TrailingZeroCount(value);
                        }
                    }
                    break;
            }
            long elapsed = Stopwatch.GetTimestamp() - start;
            Assert.That(checksum, Is.EqualTo(expected * batches));
            return elapsed;
        }

        [TestCase(0, 0)]
        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 0)]
        [TestCase(2, 1)]
        public void CaptureLookup(int operation, int pattern)
        {
            if (!Stopwatch.IsHighResolution)
            {
                Assert.Ignore("A high-resolution timer is required.");
            }
            long first = Stopwatch.GetTimestamp();
            long next = first;
            for (int spin = 0; spin < 10000 && next == first; ++spin)
            {
                next = Stopwatch.GetTimestamp();
            }
            Assert.That(first, Is.LessThan(next));
            ulong[] values = new ulong[4096];
            long expected = 0;
            for (int index = 0; index < values.Length; ++index)
            {
                ulong mixed = unchecked((ulong)index * 0x9E3779B97F4A7C15UL);
                ulong value =
                    pattern == 1 ? (ulong)(index % 40 + 1)
                    : operation == 0 ? (uint)mixed >> (index & 31)
                    : mixed >> (index & 63);
                values[index] = value;
                expected += Reference(value, operation);
            }
            PcgRandom random = new(1170L);
            for (int index = values.Length - 1; 0 < index; --index)
            {
                int other = random.Next(index + 1);
                (values[index], values[other]) = (values[other], values[index]);
            }
            for (int warm = 0; warm < 64; ++warm)
            {
                Measure(values, operation, 1, expected);
            }
            int batches = 1;
            long elapsed = Measure(values, operation, batches, expected);
            while (elapsed < Stopwatch.Frequency / 50 && batches < 1048576)
            {
                batches *= 2;
                elapsed = Measure(values, operation, batches, expected);
            }
            Assert.That(elapsed, Is.GreaterThanOrEqualTo(Stopwatch.Frequency / 50));
            string[] samples = new string[21];
            for (int round = 0; round < samples.Length; ++round)
            {
                long ticks = Measure(values, operation, batches, expected);
                double nanoseconds =
                    ticks * (1000000000.0 / Stopwatch.Frequency) / batches / values.Length;
                samples[round] = nanoseconds.ToString("R", CultureInfo.InvariantCulture);
            }
            Guid identity = typeof(BitOps).Assembly.ManifestModule.ModuleVersionId;
            TestContext.WriteLine(
                $"BIT-ROWS operation={operation} pattern={pattern} batches={batches} mvid={identity} nanoseconds={string.Join(",", samples)}"
            );
        }
    }
}
