// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

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

        [Test, Timeout(0)]
        public void Benchmark()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(1);

            UnityEngine.Random.State originalUnityRandomState = UnityEngine.Random.state;
            try
            {
                List<RandomBenchmarkResult> results = new();
                foreach (IRandom random in CreateDeterministicGenerators())
                {
                    results.Add(RunBenchmark(random, timeout));
                }

                ApplySpeedBuckets(results);

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

        private static void ApplySpeedBuckets(List<RandomBenchmarkResult> results)
        {
            if (results == null || results.Count == 0)
            {
                return;
            }

            double maxNextUint = 0;
            foreach (RandomBenchmarkResult result in results)
            {
                if (result.NextUintPerSecond > maxNextUint)
                {
                    maxNextUint = result.NextUintPerSecond;
                }
            }

            if (maxNextUint <= 0)
            {
                return;
            }

            foreach (RandomBenchmarkResult result in results)
            {
                double ratio = result.NextUintPerSecond / maxNextUint;
                result.SpeedRatio = ratio;
                result.SpeedBucket = RandomSpeedBucketExtensions.FromRatio(ratio);
            }
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
