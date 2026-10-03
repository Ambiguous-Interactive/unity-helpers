// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// The committed measurement behind the published <see cref="IntMap{TValue}"/> margins.
    /// </summary>
    /// <remarks>
    /// The Dictionary comparison reports the input to issue #578's ship-or-retire decision.
    /// The shared-low-bit comparison gates issue #926's limit of twice the dense lookup time.
    /// Both comparisons require calibrated, stable measurements before publishing a ratio.
    /// </remarks>
    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class IntMapPerformanceTests
    {
        private const int BenchmarkTimeoutMilliseconds = 600_000;

        private const int ProbeCount = 500_000;

        /*
            Leave removed keys absent: reinserting them consumes their tombstones and hides the degraded lookup
            path.
        */
        private const int RemovedShare = 10;

        private const ulong KeySeed = 0x6C8E9CF5709321D5UL;
        private const ulong ProbeSeed = 0x9E3779B97F4A7C15UL;
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong Increment = 1442695040888963407UL;

        private static readonly int[] EntryCounts = new int[] { 1_000, 10_000 };
        private static readonly int[] MissPercents = new int[] { 0, 50 };

        /*
            Written by both lookup loops so neither can be eliminated as dead code. It says nothing about the
            two sides agreeing; AssertBothAgreeOnEveryProbe is what checks that.
        */
        private static int _sink;

        private static PairedMeasurement MeasureWorkload(int entries, int missPercent)
        {
            int[] keys = BuildKeys(entries);
            int[] surviving = KeysThatSurviveRemoval(keys);
            int[] probes = BuildProbes(keys, surviving, missPercent);
            Dictionary<int, int> reference = BuildDictionary(keys);
            IntMap<int> subject = BuildIntMap(keys);

            CalibratedBenchmarkMeasurement samples = BenchmarkProtocol.MeasureCalibrated(
                iterations => RunDictionary(reference, probes, iterations),
                iterations => RunIntMap(subject, probes, iterations),
                () => AssertBothAgreeOnEveryProbe(reference, subject, probes, surviving.Length),
                unchecked((int)ProbeSeed)
            );
            if (samples == null)
            {
                return PairedMeasurement.Unusable;
            }

            UnityEngine.Debug.Log(
                $"INTMAP_PAIRED_SAMPLES {entries} {missPercent} {samples.ToJson()}"
            );
            return samples.HasSufficientTiming ? samples.Comparison : PairedMeasurement.Unusable;
        }

        private static void AssertBothAgreeOnEveryProbe(
            Dictionary<int, int> reference,
            IntMap<int> subject,
            int[] probes,
            int expectedCount
        )
        {
            Assert.AreEqual(expectedCount, reference.Count, "The oracle holds the surviving keys.");
            Assert.AreEqual(expectedCount, subject.Count, "The subject holds the surviving keys.");
            foreach (int probe in probes)
            {
                bool referenceFound = reference.TryGetValue(probe, out int referenceValue);
                bool subjectFound = subject.TryGet(probe, out int subjectValue);
                if (referenceFound != subjectFound || referenceValue != subjectValue)
                {
                    Assert.Fail(
                        $"Key {probe}: Dictionary answered ({referenceFound}, {referenceValue}) "
                            + $"and IntMap answered ({subjectFound}, {subjectValue})."
                    );
                }
            }
        }

        private static long RunDictionary(Dictionary<int, int> map, int[] probes, int iterations)
        {
            int accumulated = 0;
            for (int iteration = 0; iteration < iterations; ++iteration)
            {
                foreach (int probe in probes)
                {
                    if (map.TryGetValue(probe, out int value))
                    {
                        accumulated = unchecked(accumulated + value);
                    }
                }
            }
            _sink = accumulated;
            return accumulated;
        }

        private static long RunIntMap(IntMap<int> map, int[] probes, int iterations)
        {
            int accumulated = 0;
            for (int iteration = 0; iteration < iterations; ++iteration)
            {
                foreach (int probe in probes)
                {
                    if (map.TryGet(probe, out int value))
                    {
                        accumulated = unchecked(accumulated + value);
                    }
                }
            }
            _sink = accumulated;
            return accumulated;
        }

        private static Dictionary<int, int> BuildDictionary(int[] keys)
        {
            // Use default capacity and comparer to match ordinary caller construction.
            Dictionary<int, int> map = new Dictionary<int, int>();
            foreach (int key in keys)
            {
                map[key] = key;
            }

            for (int index = 0; index < keys.Length; index += RemovedShare)
            {
                map.Remove(keys[index]);
            }

            return map;
        }

        private static IntMap<int> BuildIntMap(int[] keys)
        {
            IntMap<int> map = new IntMap<int>();
            foreach (int key in keys)
            {
                map.TrySet(key, key);
            }

            for (int index = 0; index < keys.Length; index += RemovedShare)
            {
                map.Remove(keys[index], out int _);
            }

            return map;
        }

        private static int[] KeysThatSurviveRemoval(int[] keys)
        {
            List<int> surviving = new List<int>(keys.Length);
            for (int index = 0; index < keys.Length; ++index)
            {
                if (index % RemovedShare != 0)
                {
                    surviving.Add(keys[index]);
                }
            }

            return surviving.ToArray();
        }

        private static int[] BuildKeys(int entries)
        {
            HashSet<int> unique = new HashSet<int>(entries);
            int[] keys = new int[entries];
            ulong state = KeySeed;
            int written = 0;
            while (written < entries)
            {
                int candidate = NextKey(ref state);
                if (unique.Add(candidate))
                {
                    keys[written] = candidate;
                    ++written;
                }
            }

            return keys;
        }

        private static int[] BuildProbes(int[] keys, int[] surviving, int missPercent)
        {
            HashSet<int> everInserted = new HashSet<int>(keys);
            int[] probes = new int[ProbeCount];
            ulong state = ProbeSeed;
            for (int index = 0; index < probes.Length; ++index)
            {
                bool wantMiss = NextBounded(ref state, 100) < missPercent;
                if (!wantMiss)
                {
                    probes[index] = surviving[NextBounded(ref state, surviving.Length)];
                    continue;
                }

                int candidate = NextKey(ref state);
                while (everInserted.Contains(candidate))
                {
                    candidate = NextKey(ref state);
                }

                probes[index] = candidate;
            }

            return probes;
        }

        // A key the map is allowed to hold: the two lowest int values name slot states.
        private static int NextKey(ref ulong state)
        {
            int candidate = (int)(Next(ref state) >> 32);
            return candidate < IntMap<int>.MinimumAllowedKey
                ? IntMap<int>.MinimumAllowedKey
                : candidate;
        }

        // Use the LCG high bits; low-bit periods previously restricted probes to half the key set.
        private static int NextBounded(ref ulong state, int exclusiveUpperBound)
        {
            return (int)((Next(ref state) >> 32) % (ulong)exclusiveUpperBound);
        }

        /*
            An LCG rather than one of the package generators: the key set has to be identical on every runtime
            this runs on, and it must not be the thing being measured.
        */
        private static ulong Next(ref ulong state)
        {
            state = (state * Multiplier) + Increment;
            return state;
        }

        [Test]
        public void MillionInsertionsWithEightLiveKeysRetainTheDefaultCapacity()
        {
            IntMap<int> map = new();
            int startingCapacity = map.Capacity;
            for (int key = 0; key < 1_000_000; ++key)
            {
                map[key] = key;
                if (8 <= key)
                {
                    if (!map.Remove(key - 8, out int removed) || removed != key - 8)
                    {
                        Assert.Fail($"The sliding window lost key {key - 8}.");
                    }
                }
            }

            Assert.AreEqual(64, startingCapacity);
            Assert.AreEqual(startingCapacity, map.Capacity);
            Assert.AreEqual(8, map.Count);
            foreach (KeyValuePair<int, int> entry in map)
            {
                Assert.AreEqual(entry.Key, entry.Value);
                Assert.IsTrue(999_992 <= entry.Key && entry.Key < 1_000_000);
            }
        }

        [TestCase(64, false)]
        [TestCase(64, true)]
        [TestCase(1024, false)]
        [TestCase(1024, true)]
        [Timeout(BenchmarkTimeoutMilliseconds)]
        public void SharedLowBitLookupsStayWithinTwiceDenseLookupTime(int entries, bool misses)
        {
            IntMap<int> dense = new();
            IntMap<int> sparse = new();
            for (int key = 0; key < entries; ++key)
            {
                dense.TrySet(key, key);
                sparse.TrySet(key * 1024, key);
            }

            int[] denseProbes = new int[ProbeCount];
            int[] sparseProbes = new int[ProbeCount];
            for (int index = 0; index < ProbeCount; ++index)
            {
                int ordinal = index % entries;
                int key = misses ? ordinal + entries : ordinal;
                denseProbes[index] = key;
                sparseProbes[index] = key * 1024;
            }

            CalibratedBenchmarkMeasurement samples = BenchmarkProtocol.MeasureCalibrated(
                iterations => RunIntMap(dense, denseProbes, iterations),
                iterations => RunIntMap(sparse, sparseProbes, iterations),
                () =>
                {
                    Assert.AreEqual(entries, dense.Count);
                    Assert.AreEqual(entries, sparse.Count);
                    foreach (int ordinal in denseProbes)
                    {
                        bool denseFound = dense.TryGet(ordinal, out int denseValue);
                        bool sparseFound = sparse.TryGet(ordinal * 1024, out int sparseValue);
                        Assert.AreEqual(!misses, denseFound);
                        Assert.AreEqual(denseFound, sparseFound);
                        Assert.AreEqual(denseValue, sparseValue);
                    }
                },
                unchecked((int)ProbeSeed)
            );
            if (samples == null)
            {
                Assert.Ignore("The dense/strided lookup comparison could not calibrate.");
                return;
            }

            UnityEngine.Debug.Log($"INTMAP_SHARED_LOW_BITS {entries} {misses} {samples.ToJson()}");
            if (
                !samples.HasSufficientTiming
                || !samples.Comparison.IsStable(BenchmarkProtocol.DefaultSpreadLimit)
            )
            {
                Assert.Ignore("The dense/strided lookup comparison has unstable timings.");
                return;
            }

            Assert.LessOrEqual(
                0.5,
                samples.Comparison.Ratio,
                "Keys sharing low bits must cost at most twice the equivalent dense lookups."
            );
        }

        [Test]
        [Timeout(BenchmarkTimeoutMilliseconds)]
        public void IntMapLookupsComparedAgainstDictionary()
        {
            UnityEngine.Debug.Log("| Workload | Ratio | Reference Spread | Subject Spread |");
            UnityEngine.Debug.Log("| -------- | -----:| ----------------:| --------------:|");

            List<string> unstable = new List<string>();
            int stableWorkloads = 0;
            foreach (int entries in EntryCounts)
            {
                foreach (int missPercent in MissPercents)
                {
                    string workload = $"{entries} entries / {missPercent}% miss";
                    PairedMeasurement measurement = MeasureWorkload(entries, missPercent);
                    if (!measurement.IsStable(BenchmarkProtocol.DefaultSpreadLimit))
                    {
                        unstable.Add($"{workload} ({measurement})");
                        continue;
                    }

                    ++stableWorkloads;
                    UnityEngine.Debug.Log(
                        $"| {workload} | {measurement.Ratio:F2} | "
                            + $"{measurement.ReferenceSpread:F4} | {measurement.SubjectSpread:F4} |"
                    );
                }
            }

            foreach (string workload in unstable)
            {
                UnityEngine.Debug.Log($"unstable, not published: {workload}");
            }

            if (stableWorkloads == 0)
            {
                Assert.Ignore(
                    "Every workload read the machine rather than the code: none came inside the "
                        + $"{BenchmarkProtocol.DefaultSpreadLimit:P0} spread limit on "
                        + $"{Application.platform}."
                );
            }
        }
    }
}
