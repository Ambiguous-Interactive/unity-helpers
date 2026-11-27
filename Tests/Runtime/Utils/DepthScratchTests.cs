// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class DepthScratchTests
    {
        [Test]
        public void ReusesStorageForTheSameDepth()
        {
            DepthScratch<int> scratch = new();
            Span<int> first = scratch.Get(2, 4);
            first[0] = 17;

            Span<int> second = scratch.Get(2, 4);

            Assert.AreEqual(17, second[0]);
        }

        [Test]
        public void GrowsStorageAndPreservesExistingValues()
        {
            DepthScratch<int> scratch = new();
            Span<int> first = scratch.Get(0, 2);
            first[0] = 1;
            first[1] = 2;

            Span<int> grown = scratch.Get(0, 5);

            Assert.AreEqual(5, grown.Length);
            Assert.AreEqual(1, grown[0]);
            Assert.AreEqual(2, grown[1]);
        }

        [Test]
        public void SeparateDepthsKeepSeparateArrays()
        {
            DepthScratch<int> scratch = new();
            Span<int> first = scratch.Get(0, 3);
            first[0] = 9;

            Span<int> second = scratch.Get(1, 3);

            Assert.AreEqual(0, second[0]);
        }

        [Test]
        public void DeepDepthsGrowTheDepthTable()
        {
            const int DepthCount = 64;
            DepthScratch<int> scratch = new();
            for (int depth = 0; depth < DepthCount; ++depth)
            {
                Span<int> span = scratch.Get(depth, 2);
                span[0] = depth;
            }

            for (int depth = 0; depth < DepthCount; ++depth)
            {
                Assert.AreEqual(depth, scratch.Get(depth, 2)[0], $"depth {depth}");
            }
        }

        [Test]
        public void ZeroLengthReturnsAnEmptySpan()
        {
            DepthScratch<int> scratch = new();

            Assert.IsTrue(scratch.Get(5, 0).IsEmpty);
        }

        [Test]
        public void NegativeDepthIsRejected()
        {
            DepthScratch<int> scratch = new();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                scratch.Get(-1, 1);
            });
        }

        [Test]
        public void NegativeLengthIsRejected()
        {
            DepthScratch<int> scratch = new();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                scratch.Get(0, -1);
            });
        }
    }
}
