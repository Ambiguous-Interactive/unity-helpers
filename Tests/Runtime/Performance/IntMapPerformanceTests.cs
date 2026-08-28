// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
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
    /// result, and a paired comparison whose spread says it read the machine is ignored rather
    /// than asserted.
    /// </remarks>
    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class IntMapPerformanceTests
    {
        private const int ProbeCount = 500_000;
        private const int MeasurementBatches = 3;

        // A tenth of the entries are removed and re-added before measuring, so the table under
        // test carries real tombstones rather than the pristine one a fresh fill produces.
        private const int TombstoneDivisor = 10;

        private const ulong KeySeed = 0x6C8E9CF5709321D5UL;
        private const ulong ProbeSeed = 0x9E3779B97F4A7C15UL;
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong Increment = 1442695040888963407UL;

        // Written by both lookup loops and read by the report, so neither loop can be eliminated
        // as dead code and the two sides can be seen to have done the same work.
        private static int _sink;

        [Test]
        [Timeout(0)]
        [TestCase(1_000, 0)]
        [TestCase(1_000, 50)]
        [TestCase(10_000, 0)]
        [TestCase(10_000, 50)]
        public void IntMapLookupsComparedAgainstDictionary(int entries, int missPercent)
        {
            int[] keys = BuildKeys(entries);
            int[] probes = BuildProbes(keys, missPercent);
            Dictionary<int, int> reference = BuildDictionary(keys);
            IntMap<int> subject = BuildIntMap(keys);

            AssertBothAgreeOnEveryProbe(reference, subject, probes);

            // Warm both sides so the first measured slot is not also the first execution.
            MeasureDictionary(reference, probes);
            MeasureIntMap(subject, probes);

            PairedMeasurement measurement = BenchmarkProtocol.MeasurePaired(
                () => MeasureDictionary(reference, probes),
                () => MeasureIntMap(subject, probes),
                MeasurementBatches
            );

            UnityEngine.Debug.Log(
                $"| {entries} | {missPercent}% | {Describe(measurement)} | "
                    + $"{Application.platform} | checksum {_sink} |"
            );

            if (!measurement.IsStable(BenchmarkProtocol.DefaultSpreadLimit))
            {
                Assert.Ignore(
                    "This run read the machine rather than the code: "
                        + $"{Describe(measurement)} exceeds the "
                        + $"{BenchmarkProtocol.DefaultSpreadLimit:P0} spread limit."
                );
            }

            Assert.IsTrue(measurement.IsUsable, "A stable measurement must also be usable.");
        }

        private static void AssertBothAgreeOnEveryProbe(
            Dictionary<int, int> reference,
            IntMap<int> subject,
            int[] probes
        )
        {
            Assert.AreEqual(
                reference.Count,
                subject.Count,
                "Both maps must hold the same entries."
            );
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

            Churn(keys, key => map.Remove(key), key => map[key] = key);
            return map;
        }

        private static IntMap<int> BuildIntMap(int[] keys)
        {
            IntMap<int> map = new IntMap<int>();
            foreach (int key in keys)
            {
                map.TrySet(key, key);
            }

            Churn(keys, key => map.Remove(key, out int _), key => map.TrySet(key, key));
            return map;
        }

        private static void Churn(int[] keys, Func<int, bool> remove, Action<int> reAdd)
        {
            for (int index = 0; index < keys.Length; index += TombstoneDivisor)
            {
                remove(keys[index]);
            }

            for (int index = 0; index < keys.Length; index += TombstoneDivisor)
            {
                reAdd(keys[index]);
            }
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

        private static int[] BuildProbes(int[] keys, int missPercent)
        {
            HashSet<int> present = new HashSet<int>(keys);
            int[] probes = new int[ProbeCount];
            ulong state = ProbeSeed;
            for (int index = 0; index < probes.Length; index++)
            {
                bool wantMiss = (int)(Next(ref state) % 100UL) < missPercent;
                if (!wantMiss)
                {
                    probes[index] = keys[(int)(Next(ref state) % (ulong)keys.Length)];
                    continue;
                }

                int candidate = NextKey(ref state);
                while (present.Contains(candidate))
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

        // An LCG rather than one of the package generators: the key set has to be identical on
        // every runtime this runs on, and it must not be the thing being measured.
        private static ulong Next(ref ulong state)
        {
            state = (state * Multiplier) + Increment;
            return state;
        }

        private static string Describe(PairedMeasurement measurement)
        {
            return $"{measurement.Ratio:F2}x, spread {measurement.ReferenceSpread:P1} / "
                + $"{measurement.SubjectSpread:P1} over {measurement.Cycles} cycles";
        }
    }
}
