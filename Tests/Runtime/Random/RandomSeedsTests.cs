// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RandomSeedsTests
    {
        private const ulong Fnv64OffsetBasis = 14695981039346656037UL;
        private const ulong Fnv64Prime = 1099511628211UL;

        private static ulong _sink;

        private static void AssertDoesNotAllocate(Action action)
        {
#if ENABLE_IL2CPP && !UNITY_6000_0_OR_NEWER
            Assert.Ignore(
                "GC.GetAllocatedBytesForCurrentThread is unavailable before Unity 6 IL2CPP"
            );
#else
            _ = GC.GetAllocatedBytesForCurrentThread();
            long controlBefore = GC.GetAllocatedBytesForCurrentThread();
            byte[] forcedAllocation = new byte[64];
            long controlAfter = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(forcedAllocation);
            if (controlAfter <= controlBefore)
            {
                Assert.Ignore("GC.GetAllocatedBytesForCurrentThread is inert on this runtime");
            }

            for (int iteration = 0; iteration < 5; ++iteration)
            {
                action();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 10; ++iteration)
            {
                action();
            }

            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0L, after - before);
#endif
        }

        [TestCase(0UL, 0UL)]
        [TestCase(1UL, 6238072747940578789UL)]
        [TestCase(0x9E3779B97F4A7C15UL, 16294208416658607535UL)]
        [TestCase(ulong.MaxValue, 13029008266876403067UL)]
        public void Mix64MatchesGoldenValues(ulong value, ulong expected)
        {
            Assert.AreEqual(expected, RandomSeeds.Mix64(value));
        }

        [Test]
        public void DeriveMatchesGoldenValuesAndKeyOrder()
        {
            const ulong root = 0x0123456789ABCDEFUL;

            Assert.AreEqual(0x8931545F4F9EA651UL, RandomSeeds.Derive(root, 7UL));
            Assert.AreEqual(0x8EBD6F8DEC338D74UL, RandomSeeds.Derive(root, 7UL, 11UL));
            Assert.AreEqual(0x2671CD6FF2D97882UL, RandomSeeds.Derive(root, 7UL, 11UL, 13UL));
            Assert.AreNotEqual(
                RandomSeeds.Derive(root, 7UL, 11UL),
                RandomSeeds.Derive(root, 11UL, 7UL)
            );
        }

        [Test]
        public void DeriveIsDeterministicAcrossOverflowEdges()
        {
            const ulong expected = 0xE985075607043784UL;

            Assert.AreEqual(expected, RandomSeeds.Derive(ulong.MaxValue - 15UL, ulong.MaxValue));
            Assert.AreEqual(expected, RandomSeeds.Derive(ulong.MaxValue - 15UL, ulong.MaxValue));
        }

        [Test]
        public void DeriveHasNoCollisionsAcrossKeyGrid()
        {
            const int width = 1000;
            HashSet<ulong> seeds = new(width * width);
            for (ulong first = 0; first < width; ++first)
            {
                for (ulong second = 0; second < width; ++second)
                {
                    ulong seed = RandomSeeds.Derive(123UL, first, second);
                    if (!seeds.Add(seed))
                    {
                        Assert.Fail($"Collision for ({first}, {second}): {seed}");
                    }
                }
            }
        }

        [TestCase(null, 0xAA67D882ACF0FA67UL)]
        [TestCase("", 0xAA67D882ACF0FA67UL)]
        [TestCase("gameplay", 0x7F07EC1F040D748AUL)]
        [TestCase("é水😀", 0x51DAC6FCDC98D75DUL)]
        [TestCase("e\u0301", 0x0AF06206F6BC5FC8UL)]
        public void DomainDerivationUsesStableUtf8(string domain, ulong expected)
        {
            string normalized = domain ?? string.Empty;
            ulong hash = Fnv64OffsetBasis;
            foreach (byte value in Encoding.UTF8.GetBytes(normalized))
            {
                hash = unchecked((hash ^ value) * Fnv64Prime);
            }

            Assert.AreEqual(expected, RandomSeeds.Derive(456UL, domain));
            Assert.AreEqual(RandomSeeds.Derive(456UL, hash), RandomSeeds.Derive(456UL, domain));
        }

        [Test]
        public void DomainDerivationReplacesUnpairedSurrogates()
        {
            string high = new('\uD800', 1);
            string low = new('\uDC00', 1);

            Assert.AreEqual(0x931ECB49C4233398UL, RandomSeeds.Derive(456UL, high));
            Assert.AreEqual(0x931ECB49C4233398UL, RandomSeeds.Derive(456UL, low));
        }

        [Test]
        public void NumericDerivationAllocatesNothing()
        {
            AllocationProbe.IgnoreWhenUnmeasurable();
            AssertDoesNotAllocate(() =>
            {
                for (ulong key = 0; key < (ulong)AllocationProbe.Iterations; ++key)
                {
                    _sink ^= RandomSeeds.Derive(123UL, key, key + 1UL, key + 2UL);
                }
            });
        }

        [Test]
        public void DomainDerivationAllocatesNothing()
        {
            AllocationProbe.IgnoreWhenUnmeasurable();
            AssertDoesNotAllocate(() =>
            {
                for (int index = 0; index < AllocationProbe.Iterations; ++index)
                {
                    _sink ^= RandomSeeds.Derive(123UL, "gameplay");
                }
            });
        }
    }
}
