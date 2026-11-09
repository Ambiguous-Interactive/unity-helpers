// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System.Diagnostics;
    using System.Globalization;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [Category("Performance")]
    [Category("Slow")]
    public sealed class IntMapSparseClearTimingTests
    {
        private static void Refill(IntMap<int> map, int count)
        {
            map.Clear();
            for (int key = 1; key <= count; ++key)
            {
                if (!map.TrySet(key, key))
                {
                    Assert.Fail($"Insertion failed for key {key}.");
                }
            }
        }

        [TestCase(64, 1)]
        [TestCase(64, 7)]
        [TestCase(64, 8)]
        [TestCase(64, 64)]
        [TestCase(1024, 1)]
        [TestCase(1024, 64)]
        [TestCase(1024, 1024)]
        [TestCase(16384, 1)]
        [TestCase(16384, 64)]
        [TestCase(16384, 1024)]
        public void MeasureSparseClearAndRefill(int hint, int count)
        {
            IntMap<int> map = new(hint);
            for (int warmup = 0; warmup < 64; ++warmup)
            {
                Refill(map, count);
            }
            Stopwatch timer = new();
            int batch = 1;
            for (int calibration = 0; calibration < 24; ++calibration)
            {
                timer.Restart();
                for (int iteration = 0; iteration < batch; ++iteration)
                {
                    Refill(map, count);
                }
                timer.Stop();
                if (20 <= timer.Elapsed.TotalMilliseconds)
                {
                    break;
                }
                batch *= 2;
                Assert.That(
                    calibration,
                    Is.LessThan(23),
                    "The sample must exceed the timer floor."
                );
            }
            for (int round = 0; round < 7; ++round)
            {
                timer.Restart();
                for (int iteration = 0; iteration < batch; ++iteration)
                {
                    Refill(map, count);
                }
                timer.Stop();
                double nanoseconds = timer.Elapsed.TotalSeconds * 1_000_000_000 / batch;
                string sample = nanoseconds.ToString("R", CultureInfo.InvariantCulture);
                TestContext.Out.WriteLine(
                    $"intmap-clear-sample hint={hint} count={count} capacity={map.Capacity} round={round} batch={batch} ns={sample}"
                );
            }
            Assert.AreEqual(count, map.Count);
            for (int key = 1; key <= count; ++key)
            {
                Assert.IsTrue(map.TryGet(key, out int value));
                Assert.AreEqual(key, value);
            }
        }
    }
}
