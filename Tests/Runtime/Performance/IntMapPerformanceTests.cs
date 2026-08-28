// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// The committed measurement behind the published <see cref="IntMap{TValue}"/> margins.
    /// </summary>
    /// <remarks>
    /// The numbers in the data-structures guide were quoted from a session's ad-hoc run, and the
    /// guide said they "come from the protocol in the repository's benchmarks" while no such
    /// benchmark was committed -- so nobody could reproduce them, and no CI leg produced them on
    /// any other runtime. This fixture is that benchmark. It reports rather than gates: the ratio
    /// on an IL2CPP player is the input to issue #578's ship-or-retire decision, not a build
    /// result. Only a workload whose spread is inside the protocol's limit reaches the table,
    /// because a wider one is a reading of the machine.
    /// </remarks>
    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class IntMapPerformanceTests
    {
        private const int ProbeCount = 500_000;
        private const int MeasurementBatches = 3;

        // Every tenth key is removed and NOT re-added. Re-adding the same keys is what the issue's
        // recipe says, and on the shipped map it leaves a pristine table: the insert path prefers
        // the first tombstone on the probe chain, so re-adding N removed keys consumes exactly the
        // N tombstones the removal made.
        private const int RemovedShare = 10;

        private const ulong KeySeed = 0x6C8E9CF5709321D5UL;
        private const ulong ProbeSeed = 0x9E3779B97F4A7C15UL;
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong Increment = 1442695040888963407UL;

        private static readonly int[] EntryCounts = new int[] { 1_000, 10_000 };
        private static readonly int[] MissPercents = new int[] { 0, 50 };

        // Written by both lookup loops so neither can be eliminated as dead code. It says nothing
        // about the two sides agreeing; AssertBothAgreeOnEveryProbe is what checks that.
        private static int _sink;

        [Test]
        [Timeout(0)]
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

                    stableWorkloads++;
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

        private static PairedMeasurement MeasureWorkload(int entries, int missPercent)
        {
            int[] keys = BuildKeys(entries);
            int[] surviving = KeysThatSurviveRemoval(keys);
            int[] probes = BuildProbes(keys, surviving, missPercent);
            Dictionary<int, int> reference = BuildDictionary(keys);
            IntMap<int> subject = BuildIntMap(keys);

            AssertBothAgreeOnEveryProbe(reference, subject, probes, surviving.Length);

            // Warm both sides so the first measured slot is not also the first execution.
            MeasureDictionary(reference, probes);
            MeasureIntMap(subject, probes);

            return BenchmarkProtocol.MeasurePaired(
                () => MeasureDictionary(reference, probes),
                () => MeasureIntMap(subject, probes),
                MeasurementBatches
            );
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

        private static double MeasureDictionary(Dictionary<int, int> map, int[] probes)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int accumulated = 0;
            for (int index = 0; index < probes.Length; index++)
            {
                if (map.TryGetValue(probes[index], out int value))
                {
                    accumulated += value;
                }
            }

            stopwatch.Stop();
            _sink = accumulated;
            return Throughput(probes.Length, stopwatch);
        }

        private static double MeasureIntMap(IntMap<int> map, int[] probes)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int accumulated = 0;
            for (int index = 0; index < probes.Length; index++)
            {
                if (map.TryGet(probes[index], out int value))
                {
                    accumulated += value;
                }
            }

            stopwatch.Stop();
            _sink = accumulated;
            return Throughput(probes.Length, stopwatch);
        }

        private static double Throughput(int operations, Stopwatch stopwatch)
        {
            double seconds = stopwatch.Elapsed.TotalSeconds;
            return seconds <= 0 ? 0 : operations / seconds;
        }

        private static Dictionary<int, int> BuildDictionary(int[] keys)
        {
            // No capacity hint and no comparer: the default comparer is half of what is being
            // beaten, and a pre-sized table would not be the shape a caller builds by hand.
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
            for (int index = 0; index < keys.Length; index++)
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
                    written++;
                }
            }

            return keys;
        }

        private static int[] BuildProbes(int[] keys, int[] surviving, int missPercent)
        {
            HashSet<int> everInserted = new HashSet<int>(keys);
            int[] probes = new int[ProbeCount];
            ulong state = ProbeSeed;
            for (int index = 0; index < probes.Length; index++)
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

        // The HIGH bits, always. An LCG's low bits have a short period -- bit 0 alternates every
        // draw -- and each probe advances the state a fixed number of times, so a `% length` taken
        // from the low bits pins the index to one parity, and half of an even-sized key set is
        // never probed at all. Measured before this was fixed: 500 of 1000, and 5000 of 10000.
        private static int NextBounded(ref ulong state, int exclusiveUpperBound)
        {
            return (int)((Next(ref state) >> 32) % (ulong)exclusiveUpperBound);
        }

        // An LCG rather than one of the package generators: the key set has to be identical on
        // every runtime this runs on, and it must not be the thing being measured.
        private static ulong Next(ref ulong state)
        {
            state = (state * Multiplier) + Increment;
            return state;
        }
    }
}
