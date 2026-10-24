// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
#if UNITY_5_3_OR_NEWER
    using WallstopStudios.UnityHelpers.Tests.DataStructures;
#endif

    [TestFixture]
    [Category("Fast")]
    public sealed class ListMutationBoundaryTests
    {
        private static readonly int[] Counts = { 0, 1, 2, 3, 5, 8, 17, 10_001 };
        private static readonly int[] Offsets =
        {
            int.MinValue,
            int.MinValue + 1,
            -17,
            -1,
            0,
            1,
            17,
            int.MaxValue - 1,
            int.MaxValue,
        };
        private static readonly string[] Shapes =
        {
            nameof(Array),
            nameof(List<int>),
            nameof(SerializableList<int>),
            nameof(Collection<int>),
        };

        private static IEnumerable<TestCaseData> RotationCases()
        {
            foreach (string shape in Shapes)
            {
                foreach (int count in Counts)
                {
                    foreach (int offset in Offsets)
                    {
                        yield return new TestCaseData(shape, count, offset).SetName(
                            $"RotateLeft.{shape}.Count{count}.Offset{offset}"
                        );
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> AppendCases()
        {
            foreach (string shape in Shapes)
            {
                if (string.Equals(shape, nameof(Array), StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (int count in Counts)
                {
                    yield return new TestCaseData(shape, count).SetName(
                        $"SelfAppend.{shape}.Count{count}"
                    );
                }
            }
        }

        private static int[] CreateValues(int count)
        {
            int[] values = new int[count];
            for (int index = 0; index < count; ++index)
            {
                values[index] = index;
            }
            return values;
        }

        private static IList<int> CreateList(string shape, int[] values)
        {
            switch (shape)
            {
                case nameof(List<int>):
                    return new List<int>(values);
                case nameof(SerializableList<int>):
                    return new SerializableList<int>(values);
                case nameof(Collection<int>):
                    return new Collection<int>(new List<int>(values));
                default:
                    return (int[])values.Clone();
            }
        }

        private static int[] RotateLeftOracle(int[] source, int positions)
        {
            int count = source.Length;
            int[] expected = new int[count];
            if (count == 0)
            {
                return expected;
            }
            long offset = ((long)positions % count + count) % count;
            for (int index = 0; index < count; ++index)
            {
                expected[index] = source[(int)((index + offset) % count)];
            }
            return expected;
        }

        private static IEnumerable<int> EnumerateOnce(Action started)
        {
            started();
            yield return 7;
            yield return 8;
            yield return 9;
        }

        private static IEnumerable<int> FailAfterFirst()
        {
            yield return 7;
            throw new InvalidOperationException();
        }

        [TestCaseSource(nameof(RotationCases))]
        public void RotateLeftMatchesWidenedOracle(string shape, int count, int positions)
        {
            int[] source = CreateValues(count);
            IList<int> actual = CreateList(shape, source);
            actual.RotateLeft(positions);
            Assert.That(actual, Is.EqualTo(RotateLeftOracle(source, positions)));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(8)]
        [TestCase(17)]
        [TestCase(10_001)]
        public void SpanRotateLeftMatchesWidenedOracle(int count)
        {
            foreach (int positions in Offsets)
            {
                int[] source = CreateValues(count);
                int[] actual = (int[])source.Clone();
                actual.AsSpan().RotateLeft(positions);
                Assert.That(
                    actual,
                    Is.EqualTo(RotateLeftOracle(source, positions)),
                    $"Offset {positions}"
                );
            }
        }

        [TestCaseSource(nameof(AppendCases))]
        public void SelfAppendDuplicatesOriginalSequenceOnce(string shape, int count)
        {
            int[] source = CreateValues(count);
            IList<int> actual = CreateList(shape, source);
            int[] expected = new int[count * 2];
            Array.Copy(source, 0, expected, 0, count);
            Array.Copy(source, 0, expected, count, count);
            IListExtensions.AddRange(actual, actual);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase(nameof(List<int>))]
        [TestCase(nameof(SerializableList<int>))]
        [TestCase(nameof(Collection<int>))]
        public void AppendEnumeratesSinglePassSourceOnce(string shape)
        {
            IList<int> actual = CreateList(shape, new[] { 1, 2 });
            int starts = 0;
            IEnumerable<int> source = EnumerateOnce(() => ++starts);
            IListExtensions.AddRange(actual, source);
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(actual, Is.EqualTo(new[] { 1, 2, 7, 8, 9 }));
        }

        [TestCase(nameof(List<int>))]
        [TestCase(nameof(SerializableList<int>))]
        [TestCase(nameof(Collection<int>))]
        public void AppendPreservesYieldedPrefixWhenSourceThrows(string shape)
        {
            IList<int> actual = CreateList(shape, new[] { 1, 2 });
            Assert.Throws<InvalidOperationException>(() =>
                IListExtensions.AddRange(actual, FailAfterFirst())
            );
            Assert.That(actual, Is.EqualTo(new[] { 1, 2, 7 }));
        }

        [TestCase(nameof(List<int>))]
        [TestCase(nameof(SerializableList<int>))]
        [TestCase(nameof(Collection<int>))]
        public void NullSourceThrowsBeforeMutation(string shape)
        {
            IList<int> actual = CreateList(shape, new[] { 1, 2 });
            Assert.Throws<ArgumentNullException>(() => IListExtensions.AddRange(actual, null));
            Assert.That(actual, Is.EqualTo(new[] { 1, 2 }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SerializableAppendAcceptsSharedBackingStorage(bool sourceIsWrapper)
        {
            List<int> backing = new List<int> { 1, 2, 3 };
            SerializableList<int> target = backing;
            IEnumerable<int> source = sourceIsWrapper ? (SerializableList<int>)backing : backing;
            IListExtensions.AddRange((IList<int>)target, source);
            Assert.That(backing, Is.EqualTo(new[] { 1, 2, 3, 1, 2, 3 }));
            Assert.That(target, Is.EqualTo(backing));
        }

        [Test]
        public void SelfAppendPreservesObservableCollectionNotifications()
        {
            ObservableCollection<int> target = new ObservableCollection<int> { 1, 2, 3 };
            int changes = 0;
            target.CollectionChanged += (sender, change) => ++changes;
            IListExtensions.AddRange((IList<int>)target, target);
            Assert.That(target, Is.EqualTo(new[] { 1, 2, 3, 1, 2, 3 }));
            Assert.That(changes, Is.EqualTo(3));
        }

        [Test]
        public void SelfAppendRetainsNullsAndObjectIdentity()
        {
            object item = new object();
            IList<object> actual = new Collection<object> { item, null, item };
            IListExtensions.AddRange(actual, actual);
            Assert.That(actual.Count, Is.EqualTo(6));
            Assert.That(actual[0], Is.SameAs(item));
            Assert.That(actual[2], Is.SameAs(item));
            Assert.That(actual[3], Is.SameAs(item));
            Assert.That(actual[5], Is.SameAs(item));
            Assert.That(actual[1] == null, Is.True);
            Assert.That(actual[4] == null, Is.True);
        }

        [Test]
        public void ReadOnlySelfAppendRejectsMutationWithoutChangingValues()
        {
            IList<int> actual = Array.AsReadOnly(new[] { 1, 2 });
            Assert.Throws<NotSupportedException>(() => IListExtensions.AddRange(actual, actual));
            Assert.That(actual, Is.EqualTo(new[] { 1, 2 }));
        }

#if UNITY_5_3_OR_NEWER
        [TestCase(1)]
        [TestCase(16)]
        [TestCase(4096)]
        public void SerializableBulkAppendAvoidsObservableEnumeratorAllocations(int count)
        {
            object[] retained = new object[1];
            object preboxed = 42;
            Action allocate = () => retained[0] = (object)42;
            Action reuse = () => retained[0] = preboxed;
            long positive = ValueEnumerationAllocationTests.MeasureProfilerAllocationCount(
                allocate
            );
            if (positive == 0)
            {
                Assert.Ignore("The GC.Alloc marker did not observe fresh retained boxes.");
            }
            Assert.That(positive, Is.EqualTo(ValueEnumerationAllocationTests.ProfilerCalls));
            Assert.That(
                ValueEnumerationAllocationTests.MeasureProfilerAllocationCount(reuse),
                Is.Zero
            );

            SerializableList<int> source = new SerializableList<int>(CreateValues(count));
            SerializableList<int> target = new SerializableList<int>(count);
            IList<int> view = target;
            Action enumerated = () =>
            {
                target.Clear();
                foreach (int value in source.AsValueEnumerable())
                {
                    view.Add(value);
                }
            };
            Action bulk = () =>
            {
                target.Clear();
                IListExtensions.AddRange(view, source);
            };
            for (int trial = 0; trial < 3; ++trial)
            {
                Assert.That(
                    ValueEnumerationAllocationTests.MeasureProfilerAllocationCount(enumerated),
                    Is.EqualTo(ValueEnumerationAllocationTests.ProfilerCalls)
                );
                Assert.That(
                    ValueEnumerationAllocationTests.MeasureProfilerAllocationCount(bulk),
                    Is.Zero
                );
                Assert.That(target, Is.EqualTo(source));
            }
        }
#endif

        [Test]
        public void NullRotationTargetRemainsANoOp()
        {
            IList<int> actual = null;
            Assert.DoesNotThrow(() => actual.RotateLeft(int.MinValue));
        }
    }
}
