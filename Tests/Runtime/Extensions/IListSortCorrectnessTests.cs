// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class IListSortCorrectnessTests
    {
        private const int SmallAlphabet = 3;
        private const int SmallAlphabetLength = 6;
        private const int BinaryAlphabet = 2;
        private const int BinaryAlphabetLength = 10;
        private const int PermutationLength = 7;
        private const int ReducedAlphabetLength = 5;
        private const int ReducedPermutationLength = 5;
        private const int ChainedBackingMaximumSize = 129;
        private const int ExhaustiveDomainCaseCount = 9054;
        private const int ReducedDomainCaseCount = 518;

        /*
            Stability expectations follow the SortAlgorithm API contract and the algorithm table in
            docs/performance/ilist-sorting-performance.md.
        */
        private static readonly Dictionary<SortAlgorithm, bool> PromisedStability = new()
        {
            [SortAlgorithm.Ghost] = false,
            [SortAlgorithm.Insertion] = true,
            [SortAlgorithm.Meteor] = false,
            [SortAlgorithm.PatternDefeatingQuickSort] = false,
            [SortAlgorithm.Grail] = true,
            [SortAlgorithm.Power] = true,
            [SortAlgorithm.Tim] = true,
            [SortAlgorithm.Jesse] = false,
            [SortAlgorithm.Green] = true,
            [SortAlgorithm.Ska] = false,
            [SortAlgorithm.Ipn] = false,
            [SortAlgorithm.Smooth] = false,
            [SortAlgorithm.Block] = true,
            [SortAlgorithm.Ips4o] = false,
            [SortAlgorithm.PowerPlus] = true,
            [SortAlgorithm.Glide] = true,
            [SortAlgorithm.Flux] = false,
            [SortAlgorithm.Yam] = true,
        };

        private static readonly string[] BackingNames =
        {
            "T[]",
            "List<T>",
            "SerializableList<T>",
            "IndexerOnlyList<T>",
            "NodeChainList<T>",
        };

        private static readonly int[] ShapeSizes =
        {
            0,
            1,
            2,
            3,
            5,
            8,
            13,
            17,
            31,
            32,
            33,
            63,
            64,
            65,
            127,
            128,
            129,
            255,
            256,
            257,
            511,
            512,
            513,
            1000,
        };

        private static readonly (string Name, Func<int, int, int> KeyAt)[] Shapes =
        {
            ("sorted", static (index, _) => index),
            ("reverseSorted", static (index, size) => size - index),
            ("allEqual", static (_, _) => 7),
            ("organPipe", static (index, size) => index < size / 2 ? index : size - index),
            ("sawtooth", static (index, _) => index % 8),
            ("duplicateHeavy", static (index, _) => (index * 7) % 3),
            (
                "extremes",
                static (index, _) =>
                    index % 3 == 0 ? int.MinValue
                    : index % 3 == 1 ? int.MaxValue
                    : 0
            ),
            (
                "adversarialPivot",
                static (index, size) => index % 2 == 0 ? index / 2 : size - 1 - (index / 2)
            ),
            ("plateauThenDescent", static (index, size) => index < size / 2 ? 0 : size - index),
            ("nearlySorted", static (index, _) => index % 16 == 0 ? index + 5 : index),
        };

        private static IEnumerable<TestCaseData> SortAlgorithmCases
        {
            get
            {
                foreach (SortAlgorithm algorithm in EveryAlgorithm())
                {
                    yield return new TestCaseData(algorithm).SetName($"Exhaustive.{algorithm}");
                }
            }
        }

        private static IEnumerable<TestCaseData> SortAlgorithmBackingCases
        {
            get
            {
                foreach (SortAlgorithm algorithm in EveryAlgorithm())
                {
                    for (int backing = 0; backing < BackingNames.Length; ++backing)
                    {
                        yield return new TestCaseData(algorithm, backing).SetName(
                            $"Backing.{algorithm}.{BackingNames[backing].Replace("<T>", string.Empty).Replace("[]", "Array")}"
                        );
                    }
                }
            }
        }

        private static IEnumerable<SortAlgorithm> EveryAlgorithm()
        {
            foreach (SortAlgorithm algorithm in Enum.GetValues(typeof(SortAlgorithm)))
            {
#pragma warning disable CS0618 // Type or member is obsolete
                if (algorithm == SortAlgorithm.None)
#pragma warning restore CS0618 // Type or member is obsolete
                {
                    continue;
                }

                yield return algorithm;
            }
        }

        [Test]
        public void EveryDeclaredAlgorithmCarriesAStabilityPromise()
        {
            List<SortAlgorithm> missing = new();
            int declared = 0;
            foreach (SortAlgorithm algorithm in EveryAlgorithm())
            {
                declared++;
                if (!PromisedStability.ContainsKey(algorithm))
                {
                    missing.Add(algorithm);
                }
            }

            Assert.That(
                0 < declared,
                Is.True,
                "SortAlgorithm declared no members, so this check measured nothing"
            );
            Assert.That(
                missing,
                Is.Empty,
                "Every SortAlgorithm needs a documented stability promise before it can be verified"
            );
            Assert.That(PromisedStability.Count, Is.EqualTo(declared));
        }

        /// <remarks>
        /// Every sequence over a three symbol alphabet up to length six is complete duplicate
        /// coverage for the shapes a sort can branch on, every binary sequence up to length ten
        /// reaches deeper with the same completeness, and every permutation of seven distinct
        /// elements is complete coverage of the orderings. The audit checks the result is a
        /// permutation of the input, not merely that it is sorted.
        /// </remarks>
        [TestCaseSource(nameof(SortAlgorithmCases))]
        public void EveryAlgorithmSortsEverySmallDomainExhaustively(SortAlgorithm algorithm)
        {
            SortAudit audit = new(algorithm, PromisedStabilityFor(algorithm), 0);
            AuditEverySequence(audit, SmallAlphabet, SmallAlphabetLength);
            AuditEverySequence(audit, BinaryAlphabet, BinaryAlphabetLength);
            AuditEveryPermutation(audit, PermutationLength);

            Assert.That(
                audit.Cases,
                Is.EqualTo(ExhaustiveDomainCaseCount),
                $"{algorithm} exhaustive audit enumerated the wrong domain"
            );
            Assert.That(audit.Failure, Is.Null, audit.Failure);
        }

        /// <remarks>
        /// The sorts copy anything that is not a <c>T[]</c> into a pooled array and write it back,
        /// with a bulk path for the two list types that offer one and the interface indexer for
        /// everything else. Both interface-only backings are unsealed, so the indexer cannot be
        /// devirtualized away and the path is genuinely exercised.
        /// </remarks>
        [TestCaseSource(nameof(SortAlgorithmBackingCases))]
        public void EveryAlgorithmSortsEveryShapeInEveryBacking(
            SortAlgorithm algorithm,
            int backing
        )
        {
            SortAudit audit = new(algorithm, PromisedStabilityFor(algorithm), backing);
            AuditEverySequence(audit, SmallAlphabet, ReducedAlphabetLength);
            AuditEveryPermutation(audit, ReducedPermutationLength);

            int sizes = 0;
            foreach (int size in SizesFor(backing))
            {
                sizes++;
                foreach ((string name, Func<int, int, int> keyAt) in Shapes)
                {
                    int[] keys = new int[size];
                    for (int i = 0; i < size; ++i)
                    {
                        keys[i] = keyAt(i, size);
                    }

                    audit.Run(keys, name);
                }
            }

            Assert.That(
                audit.Cases,
                Is.EqualTo(ReducedDomainCaseCount + (sizes * Shapes.Length)),
                $"{algorithm} over {BackingNames[backing]} enumerated the wrong domain"
            );
            Assert.That(audit.Failure, Is.Null, audit.Failure);
        }

        private static bool PromisedStabilityFor(SortAlgorithm algorithm)
        {
            Assert.That(
                PromisedStability.TryGetValue(algorithm, out bool stable),
                Is.True,
                $"{algorithm} has no documented stability promise to verify against"
            );
            return stable;
        }

        private static IEnumerable<int> SizesFor(int backing)
        {
            foreach (int size in ShapeSizes)
            {
                if (backing == BackingNames.Length - 1 && ChainedBackingMaximumSize < size)
                {
                    continue;
                }

                yield return size;
            }
        }

        private static void AuditEverySequence(SortAudit audit, int alphabet, int maximumLength)
        {
            for (int length = 0; length <= maximumLength; ++length)
            {
                int[] keys = new int[length];
                while (true)
                {
                    audit.Run(keys, "sequence");

                    int index = length - 1;
                    while (0 <= index)
                    {
                        keys[index]++;
                        if (keys[index] < alphabet)
                        {
                            break;
                        }

                        keys[index] = 0;
                        index--;
                    }

                    if (index < 0)
                    {
                        break;
                    }
                }
            }
        }

        private static void AuditEveryPermutation(SortAudit audit, int maximumLength)
        {
            for (int length = 0; length <= maximumLength; ++length)
            {
                int[] keys = new int[length];
                for (int i = 0; i < length; ++i)
                {
                    keys[i] = i;
                }

                AuditPermutationsFrom(audit, keys, 0);
            }
        }

        private static void AuditPermutationsFrom(SortAudit audit, int[] keys, int start)
        {
            if (start == keys.Length)
            {
                audit.Run(keys, "permutation");
                return;
            }

            for (int i = start; i < keys.Length; ++i)
            {
                (keys[start], keys[i]) = (keys[i], keys[start]);
                AuditPermutationsFrom(audit, keys, start + 1);
                (keys[start], keys[i]) = (keys[i], keys[start]);
            }
        }

        internal static IList<SortProbe> CreateBacking(int backing, int[] keys)
        {
            SortProbe[] probes = new SortProbe[keys.Length];
            for (int i = 0; i < keys.Length; ++i)
            {
                probes[i] = new SortProbe(keys[i], i);
            }

            switch (backing)
            {
                case 0:
                {
                    return probes;
                }
                case 1:
                {
                    return new List<SortProbe>(probes);
                }
                case 2:
                {
                    SerializableList<SortProbe> serializable = new();
                    serializable.AddRange(probes);
                    return serializable;
                }
                case 3:
                {
                    IndexerOnlyList<SortProbe> indexed = new();
                    indexed.AddRange(probes);
                    return indexed;
                }
                default:
                {
                    NodeChainList<SortProbe> chained = new();
                    chained.AddRange(probes);
                    return chained;
                }
            }
        }

        internal static string Describe(int[] keys)
        {
            if (32 < keys.Length)
            {
                return "length " + keys.Length;
            }

            StringBuilder builder = new("[");
            for (int i = 0; i < keys.Length; ++i)
            {
                if (0 < i)
                {
                    builder.Append(',');
                }

                builder.Append(keys[i]);
            }

            return builder.Append(']').ToString();
        }

        /// <summary>An element carrying its original position so a sort can be audited.</summary>
        public readonly struct SortProbe
        {
            /// <summary>The value the comparer orders by.</summary>
            public int Key { get; }

            /// <summary>The index this element started at.</summary>
            public int Origin { get; }

            /// <summary>Creates a probe.</summary>
            /// <param name="key">The value to order by.</param>
            /// <param name="origin">The index the element started at.</param>
            public SortProbe(int key, int origin)
            {
                Key = key;
                Origin = origin;
            }
        }

        /// <summary>Orders probes by key alone, so equal keys separate stable sorts from the rest.</summary>
        public readonly struct SortProbeKeyComparer : IComparer<SortProbe>
        {
            /// <summary>Compares two probes by key.</summary>
            /// <param name="x">The left probe.</param>
            /// <param name="y">The right probe.</param>
            /// <returns>The sign of the key comparison.</returns>
            public int Compare(SortProbe x, SortProbe y)
            {
                return x.Key.CompareTo(y.Key);
            }
        }

        private sealed class SortAudit
        {
            public int Cases { get; private set; }

            public string Failure { get; private set; }

            private readonly SortAlgorithm _algorithm;
            private readonly bool _stable;
            private readonly int _backing;

            public SortAudit(SortAlgorithm algorithm, bool stable, int backing)
            {
                _algorithm = algorithm;
                _stable = stable;
                _backing = backing;
            }

            public void Run(int[] keys, string label)
            {
                Cases++;
                IList<SortProbe> subject = CreateBacking(_backing, keys);
                subject.Sort(new SortProbeKeyComparer(), _algorithm);
                Verify(subject, keys, label);
            }

            private void Verify(IList<SortProbe> subject, int[] keys, string label)
            {
                if (Failure != null)
                {
                    return;
                }

                int count = keys.Length;
                if (subject.Count != count)
                {
                    Record(label, keys, $"count became {subject.Count}, expected {count}");
                    return;
                }

                bool[] seen = new bool[count];
                for (int i = 0; i < count; ++i)
                {
                    SortProbe probe = subject[i];
                    if (probe.Origin < 0 || count <= probe.Origin)
                    {
                        Record(label, keys, $"index {i} holds an element from outside the input");
                        return;
                    }

                    if (seen[probe.Origin])
                    {
                        Record(
                            label,
                            keys,
                            $"index {i} duplicates the element from {probe.Origin}"
                        );
                        return;
                    }

                    seen[probe.Origin] = true;
                    if (probe.Key != keys[probe.Origin])
                    {
                        Record(label, keys, $"index {i} holds a corrupted element");
                        return;
                    }
                }

                for (int i = 1; i < count; ++i)
                {
                    if (subject[i].Key < subject[i - 1].Key)
                    {
                        Record(label, keys, $"index {i} is out of order");
                        return;
                    }

                    if (
                        _stable
                        && subject[i].Key == subject[i - 1].Key
                        && subject[i].Origin < subject[i - 1].Origin
                    )
                    {
                        Record(label, keys, $"index {i} reordered equal elements");
                        return;
                    }
                }
            }

            private void Record(string label, int[] keys, string reason)
            {
                Failure =
                    $"{_algorithm} over {BackingNames[_backing]}: {reason} for {label} {Describe(keys)}";
            }
        }
    }
}
