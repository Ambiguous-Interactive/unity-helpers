// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Extension;

    [TestFixture]
    public sealed class ValueEnumerableTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(17)]
        [TestCase(10001)]
        public void ConcreteSourcesPreserveOrderAndReenumeration(int count)
        {
            int[] values = new int[count];
            for (int index = 0; index < count; ++index)
            {
                values[index] = index;
            }
            IEnumerable<int>[] sources =
            {
                values,
                new List<int>(values),
                new HashSet<int>(values),
            };
            foreach (IEnumerable<int> source in sources)
            {
                List<int> expected = new List<int>();
                foreach (int value in source)
                {
                    expected.Add(value);
                }
                ValueEnumerable<int> view = source.AsValueEnumerable();
                for (int repetition = 0; repetition < 2; ++repetition)
                {
                    List<int> actual = new List<int>();
                    foreach (int value in view)
                    {
                        actual.Add(value);
                    }
                    CollectionAssert.AreEqual(expected, actual);
                }
            }
        }

        [Test]
        public void NullAndDefaultViewsAreEmpty()
        {
            IEnumerable<int> source = null;
            ValueEnumerable<int>[] views = { source.AsValueEnumerable(), default };
            foreach (ValueEnumerable<int> view in views)
            {
                using ValueEnumerator<int> enumerator = view.GetEnumerator();
                Assert.That(enumerator.MoveNext(), Is.False);
                Assert.That(enumerator.Current, Is.EqualTo(0));
            }
            ValueEnumerator<int> empty = default;
            Assert.That(empty.MoveNext(), Is.False);
            empty.Dispose();
        }

        [Test]
        public void CovariantArraysAndListsRetainNullsAndReferenceIdentity()
        {
            string[] values = { "first", null, "last" };
            IEnumerable<object>[] sources = { values, new List<string>(values) };
            foreach (IEnumerable<object> source in sources)
            {
                int index = 0;
                foreach (object value in source.AsValueEnumerable())
                {
                    Assert.That(value, Is.SameAs(values[index]));
                    ++index;
                }
                Assert.That(index, Is.EqualTo(values.Length));
            }
        }

        [Test]
        public void ViewAcquiresLazilyAndFallbackDoesNotUseIndexers()
        {
            EnumerationProbe source = new EnumerationProbe();
            ValueEnumerable<int> view = source.AsValueEnumerable();
            Assert.That(source.Events, Is.Empty);
            List<int> actual = new List<int>();
            foreach (int value in view)
            {
                actual.Add(value);
            }
            CollectionAssert.AreEqual(source.Values, actual);
            CollectionAssert.AreEqual(
                new[]
                {
                    nameof(EnumerationProbe.GetEnumerator),
                    nameof(EnumerationProbe.MoveNext),
                    nameof(EnumerationProbe.Current),
                    nameof(EnumerationProbe.MoveNext),
                    nameof(EnumerationProbe.Current),
                    nameof(EnumerationProbe.MoveNext),
                    nameof(EnumerationProbe.Current),
                    nameof(EnumerationProbe.MoveNext),
                    nameof(EnumerationProbe.Dispose),
                },
                source.Events
            );
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void FallbackExceptionsAndCleanupMatchDirectEnumeration(int failure)
        {
            EnumerationProbe direct = new EnumerationProbe { Failure = failure };
            EnumerationProbe wrapped = new EnumerationProbe { Failure = failure };
            Assert.Throws<InvalidOperationException>(() =>
            {
                foreach (int value in direct)
                {
                    Assert.That(value, Is.EqualTo(1));
                    break;
                }
            });
            Assert.Throws<InvalidOperationException>(() =>
            {
                foreach (int value in wrapped.AsValueEnumerable())
                {
                    Assert.That(value, Is.EqualTo(1));
                    break;
                }
            });
            CollectionAssert.AreEqual(direct.Events, wrapped.Events);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FallbackDisposesOnEarlyExitAndBodyException(bool fail)
        {
            EnumerationProbe source = new EnumerationProbe();
            TestDelegate run = () =>
            {
                foreach (int value in source.AsValueEnumerable())
                {
                    Assert.That(value, Is.EqualTo(1));
                    if (fail)
                    {
                        throw new InvalidOperationException();
                    }
                    break;
                }
            };
            if (fail)
            {
                Assert.Throws<InvalidOperationException>(run);
            }
            else
            {
                run();
            }
            CollectionAssert.AreEqual(
                new[]
                {
                    nameof(EnumerationProbe.GetEnumerator),
                    nameof(EnumerationProbe.MoveNext),
                    nameof(EnumerationProbe.Current),
                    nameof(EnumerationProbe.Dispose),
                },
                source.Events
            );
        }

        [Test]
        public void DerivedCollectionsRetainReimplementedInterfaces()
        {
            EnumerationDerivedList list = new EnumerationDerivedList { 99 };
            EnumerationDerivedHashSet set = new EnumerationDerivedHashSet { 99 };
            IEnumerable<int>[] sources = { list, set };
            foreach (IEnumerable<int> source in sources)
            {
                List<int> actual = new List<int>();
                foreach (int value in source.AsValueEnumerable())
                {
                    actual.Add(value);
                }
                CollectionAssert.AreEqual(new[] { 1, 2, 3 }, actual);
            }
            Assert.That(
                list.Probe.Events[list.Probe.Events.Count - 1],
                Is.EqualTo(nameof(EnumerationProbe.Dispose))
            );
            Assert.That(
                set.Probe.Events[set.Probe.Events.Count - 1],
                Is.EqualTo(nameof(EnumerationProbe.Dispose))
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ListMutationRetainsFailFastBehavior(bool clear)
        {
            List<int> values = new List<int> { 1, 2, 3 };
            using ValueEnumerator<int> enumerator = ((IEnumerable<int>)values)
                .AsValueEnumerable()
                .GetEnumerator();
            Assert.That(enumerator.MoveNext(), Is.True);
            if (clear)
            {
                values.Clear();
            }
            else
            {
                values.Add(4);
            }
            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        }

        [Test]
        public void HashSetMutationRetainsFailFastBehavior()
        {
            HashSet<int> values = new HashSet<int> { 1, 2, 3 };
            using ValueEnumerator<int> enumerator = ((IEnumerable<int>)values)
                .AsValueEnumerable()
                .GetEnumerator();
            Assert.That(enumerator.MoveNext(), Is.True);
            values.Add(4);
            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        }

        [Test]
        public void ArrayMutationIsObservedAndEndIsStable()
        {
            int[] values = { 1, 2 };
            using ValueEnumerator<int> enumerator = ((IEnumerable<int>)values)
                .AsValueEnumerable()
                .GetEnumerator();
            Assert.That(enumerator.MoveNext(), Is.True);
            values[1] = 7;
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(7));
            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(enumerator.MoveNext(), Is.False);
        }

        [Test]
        public void RequiredViewPreservesNullSourceFailure()
        {
            IEnumerable<int> source = null;
            Assert.Throws<NullReferenceException>(() => source.GetEnumerator());
            ValueEnumerable<int> view = source.AsValueEnumerableRequired();
            Assert.Throws<NullReferenceException>(() => view.GetEnumerator());
        }

        [Test]
        public void FallbackCopiesRetainTheOriginalSharedLifetime()
        {
            EnumerationProbe source = new EnumerationProbe();
            ValueEnumerator<int> enumerator = source.AsValueEnumerable().GetEnumerator();
            ValueEnumerator<int> copy = enumerator;
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(copy.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(2));
            enumerator.Dispose();
            copy.Dispose();
            Assert.That(
                source.Events[source.Events.Count - 1],
                Is.EqualTo(nameof(EnumerationProbe.Dispose))
            );
            Assert.That(
                source.Events[source.Events.Count - 2],
                Is.EqualTo(nameof(EnumerationProbe.Dispose))
            );
        }

        [Test]
        public void NestedEnumeratorsRemainIndependent()
        {
            IEnumerable<int> source = new List<int> { 1, 2, 3 };
            ValueEnumerable<int> view = source.AsValueEnumerable();
            int pairs = 0;
            foreach (int outer in view)
            {
                foreach (int inner in view)
                {
                    Assert.That(outer * inner, Is.InRange(1, 9));
                    ++pairs;
                }
            }
            Assert.That(pairs, Is.EqualTo(9));
        }
    }
}
