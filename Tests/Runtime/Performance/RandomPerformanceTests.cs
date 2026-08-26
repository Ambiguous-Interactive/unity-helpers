// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class RandomPerformanceTests
    {
        private const int NumInvocationsPerIteration = 100_000;
        private const ulong DeterministicSeedBase = 0x6C8E9CF5709321D5UL;
        private const ulong DeterministicSeedIncrement = 0x9E3779B97F4A7C15UL;
        private const int GuidSeedOffset = 10_000;
        private const int WarmupIterations = 5_000;

        // One slot of the counterbalanced ranking pass. Enough draws that the slot lasts tens of
        // milliseconds on every generator here, so timer resolution is not part of the reading.
        private const int RankingDrawsPerSlot = 20_000_000;

        [Test, Timeout(0)]
        public void Benchmark()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(1);

            UnityEngine.Random.State originalUnityRandomState = UnityEngine.Random.state;
            try
            {
                List<IRandom> generators = new(CreateDeterministicGenerators());
                List<RandomBenchmarkResult> results = new(generators.Count);
                foreach (IRandom random in generators)
                {
                    results.Add(RunBenchmark(random, timeout));
                }

                ApplySpeedBuckets(results, generators);

                List<string> markdown = RandomBenchmarkMarkdownBuilder.BuildTables(results);

                BenchmarkReadmeUpdater.UpdateSection(
                    "RANDOM_BENCHMARKS",
                    markdown,
                    "docs/performance/random-performance.md"
                );

                UnityEngine.Debug.Log("Random benchmark summary generated.");
            }
            finally
            {
                UnityEngine.Random.state = originalUnityRandomState;
            }
        }

        private static IEnumerable<IRandom> CreateDeterministicGenerators()
        {
            int seedIndex = 1;

            yield return new DotNetRandom(CreateGuidSeed(seedIndex++));
            yield return new LinearCongruentialGenerator(CreateGuidSeed(seedIndex++));
            yield return new IllusionFlow(CreateGuidSeed(seedIndex++));
            yield return new PcgRandom(CreateGuidSeed(seedIndex++));
            yield return new RomuDuo(CreateGuidSeed(seedIndex++));
            yield return new SplitMix64(CreateGuidSeed(seedIndex++));
            yield return new FlurryBurstRandom(CreateGuidSeed(seedIndex++));
            yield return new SquirrelRandom(CreateIntSeed(seedIndex++));
            yield return new SystemRandom(CreateIntSeed(seedIndex++));
            yield return new UnityRandom(CreateIntSeed(seedIndex++));
            yield return new WyRandom(CreateGuidSeed(seedIndex++));
            yield return new XorShiftRandom(CreateGuidSeed(seedIndex++));
            yield return new XoroShiroRandom(CreateGuidSeed(seedIndex++));
            yield return new PhotonSpinRandom(CreateGuidSeed(seedIndex++));
            yield return new StormDropRandom(CreateGuidSeed(seedIndex++));
            yield return new BlastCircuitRandom(CreateGuidSeed(seedIndex++));
            yield return new WaveSplatRandom(CreateGuidSeed(seedIndex++));
            yield return new Xoshiro128StarStar(CreateGuidSeed(seedIndex++));
            yield return new Xoshiro256StarStar(CreateGuidSeed(seedIndex++));
            yield return new WDoomRandom(CreateGuidSeed(seedIndex++));
        }

        private static Guid CreateGuidSeed(int index)
        {
            byte[] buffer = new byte[16];
            ulong first = DeriveSeed(index);
            ulong second = DeriveSeed(index + GuidSeedOffset);
            WriteUInt64LittleEndian(buffer, 0, first);
            WriteUInt64LittleEndian(buffer, 8, second);
            return new Guid(buffer);
        }

        private static int CreateIntSeed(int index)
        {
            int value = unchecked((int)(DeriveSeed(index) & int.MaxValue));
            return value == 0 ? 1 : value;
        }

        private static ulong DeriveSeed(int index)
        {
            unchecked
            {
                ulong value = DeterministicSeedBase + ((ulong)index * DeterministicSeedIncrement);
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value;
            }
        }

        private static void WriteUInt64LittleEndian(byte[] buffer, int offset, ulong value)
        {
            buffer[offset + 0] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
            buffer[offset + 4] = (byte)(value >> 32);
            buffer[offset + 5] = (byte)(value >> 40);
            buffer[offset + 6] = (byte)(value >> 48);
            buffer[offset + 7] = (byte)(value >> 56);
        }

        private static RandomBenchmarkResult RunBenchmark<T>(T random, TimeSpan timeout)
            where T : IRandom
        {
            WarmupGenerator(random);

            int nextBool = RunNextBool(timeout, random);
            int nextInt = RunNext(timeout, random);
            int nextUint = RunNextUint(timeout, random);
            int nextFloat = RunNextFloat(timeout, random);
            int nextDouble = RunNextDouble(timeout, random);
            int nextUintRange = RunNextUintRange(timeout, random);
            int nextIntRange = RunNextIntRange(timeout, random);

            double durationSeconds = timeout.TotalSeconds;

            RandomGeneratorMetadata metadata = RandomGeneratorMetadataRegistry.Snapshot(random);

            return new RandomBenchmarkResult(
                random.GetType(),
                nextBool / durationSeconds,
                nextInt / durationSeconds,
                nextUint / durationSeconds,
                nextFloat / durationSeconds,
                nextDouble / durationSeconds,
                nextUintRange / durationSeconds,
                nextIntRange / durationSeconds,
                metadata
            );
        }

        /// <summary>
        /// Ranks the roster from measurements taken against one pivot generator, interleaved.
        /// </summary>
        /// <remarks>
        /// The seven ops/s columns are each generator measured on its own, one after another, over
        /// a couple of minutes. That is fine for "how fast is this" and wrong for "which is
        /// faster": anything that changed between the first generator and the twentieth lands on
        /// the twentieth. #285 recorded the consequence -- the committed table and a fresh run
        /// disagreeing by up to 9x with inverted rankings -- and could not act on it.
        ///
        /// Every ratio here is instead measured against the same pivot in an ABBABAAB batch, so
        /// each pair of readings is adjacent in time (#573). A comparison the machine was too busy
        /// to give falls back to the absolute numbers rather than blocking the run, and says so.
        /// </remarks>
        private static void ApplySpeedBuckets(
            List<RandomBenchmarkResult> results,
            List<IRandom> generators
        )
        {
            if (results == null || results.Count == 0)
            {
                return;
            }

            double[] ratios = new double[results.Count];
            List<string> unstable = new();
            int pivotIndex = FindPivotIndex(generators, results);

            if (0 <= pivotIndex && generators != null && generators.Count == results.Count)
            {
                IRandom pivot = generators[pivotIndex];
                WarmupGenerator(pivot);
                for (int index = 0; index < results.Count; index++)
                {
                    if (index == pivotIndex)
                    {
                        ratios[index] = 1;
                        continue;
                    }

                    IRandom subject = generators[index];
                    WarmupGenerator(subject);
                    PairedMeasurement measurement = BenchmarkProtocol.MeasurePaired(
                        () => MeasureNextUintThroughput(pivot),
                        () => MeasureNextUintThroughput(subject)
                    );

                    if (measurement.IsStable(BenchmarkProtocol.DefaultSpreadLimit))
                    {
                        ratios[index] = measurement.Ratio;
                        continue;
                    }

                    unstable.Add($"{results[index].DisplayName} ({measurement})");
                }
            }

            double pivotThroughput = 0 <= pivotIndex ? results[pivotIndex].NextUintPerSecond : 0;
            for (int index = 0; index < results.Count; index++)
            {
                if (ratios[index] <= 0)
                {
                    // No usable pairing: fall back to the independently measured number, which is
                    // the only thing this table had before.
                    ratios[index] =
                        0 < pivotThroughput
                            ? results[index].NextUintPerSecond / pivotThroughput
                            : 0;
                }
            }

            double best = 0;
            foreach (double ratio in ratios)
            {
                if (best < ratio)
                {
                    best = ratio;
                }
            }

            if (best <= 0)
            {
                return;
            }

            for (int index = 0; index < results.Count; index++)
            {
                double normalized = ratios[index] / best;
                results[index].SpeedRatio = normalized;
                results[index].SpeedBucket = RandomSpeedBucketExtensions.FromRatio(normalized);
            }

            if (0 < unstable.Count)
            {
                UnityEngine.Debug.LogWarning(
                    "Random benchmark ranking fell back to un-paired numbers for "
                        + $"{unstable.Count} of {results.Count} generators; the machine moved more "
                        + $"than {BenchmarkProtocol.DefaultSpreadLimit:P0} between adjacent cycles: "
                        + string.Join(", ", unstable)
                );
            }
        }

        // The package default, so every published ratio reads as "against what PRNG.Instance gives
        // you". Falls back to the first entry if the roster ever stops carrying it.
        private static int FindPivotIndex(
            List<IRandom> generators,
            List<RandomBenchmarkResult> results
        )
        {
            if (generators == null || generators.Count == 0 || generators.Count != results.Count)
            {
                return -1;
            }

            for (int index = 0; index < generators.Count; index++)
            {
                if (generators[index] is IllusionFlow)
                {
                    return index;
                }
            }

            return 0;
        }

        private static double MeasureNextUintThroughput(IRandom random)
        {
            Stopwatch timer = Stopwatch.StartNew();
            for (int draw = 0; draw < RankingDrawsPerSlot; ++draw)
            {
                _ = random.NextUint();
            }

            timer.Stop();
            double seconds = timer.Elapsed.TotalSeconds;
            return seconds <= 0 ? 0 : RankingDrawsPerSlot / seconds;
        }

        // Copy-pasta'd for maximum speed
        private static int RunNext<T>(TimeSpan timeout, T random)
            where T : IRandom
        {
            int count = 0;
            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    _ = random.Next();
                    ++count;
                }
            } while (timer.Elapsed < timeout);

            return count;
        }

        private static void WarmupGenerator<T>(T random)
            where T : IRandom
        {
            for (int i = 0; i < WarmupIterations; ++i)
            {
                _ = random.Next();
                _ = random.NextBool();
                _ = random.NextUint();
                _ = random.NextFloat();
                _ = random.NextDouble();
                _ = random.NextUint(1_000);
                _ = random.Next(1_000);
            }
        }

        private static int RunNextBool<T>(TimeSpan timeout, T random)
            where T : IRandom
        {
            int count = 0;
            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    _ = random.NextBool();
                    ++count;
                }
            } while (timer.Elapsed < timeout);

            return count;
        }

        private static int RunNextUint<T>(TimeSpan timeout, T random)
            where T : IRandom
        {
            int count = 0;
            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    _ = random.NextUint();
                    ++count;
                }
            } while (timer.Elapsed < timeout);

            return count;
        }

        private static int RunNextUintRange<T>(TimeSpan timeout, T random)
            where T : IRandom
        {
            int count = 0;
            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    _ = random.NextUint(1_000);
                    ++count;
                }
            } while (timer.Elapsed < timeout);

            return count;
        }

        private static int RunNextIntRange<T>(TimeSpan timeout, T random)
            where T : IRandom
        {
            int count = 0;
            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    _ = random.Next(1_000);
                    ++count;
                }
            } while (timer.Elapsed < timeout);

            return count;
        }

        private static int RunNextFloat<T>(TimeSpan timeout, T random)
            where T : IRandom
        {
            int count = 0;
            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    _ = random.NextFloat();
                    ++count;
                }
            } while (timer.Elapsed < timeout);

            return count;
        }

        private static int RunNextDouble<T>(TimeSpan timeout, T random)
            where T : IRandom
        {
            int count = 0;
            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    _ = random.NextDouble();
                    ++count;
                }
            } while (timer.Elapsed < timeout);

            return count;
        }
    }
}
