// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    public sealed class ValueDictionaryEnumerableTests
    {
        private static int _observedSum;

        private static IEnumerable<KeyValuePair<int, int>> ToPairs(EnumerationProbe source)
        {
            foreach (int value in source)
            {
                yield return new KeyValuePair<int, int>(value, -value);
            }
        }

        private static void Consume(IEnumerable<KeyValuePair<int, int>> source, bool wrapped)
        {
            int sum = 0;
            if (wrapped)
            {
                foreach (KeyValuePair<int, int> entry in source.AsValueDictionaryEnumerable())
                {
                    sum += entry.Value;
                }
            }
            else
            {
                foreach (KeyValuePair<int, int> entry in source)
                {
                    sum += entry.Value;
                }
            }
            _observedSum = sum;
        }

        private static long Measure(Action operation)
        {
#if ENABLE_IL2CPP && !UNITY_6000_2_OR_NEWER
            return -1;
#else
            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            for (int repetition = 0; repetition < 64; ++repetition)
            {
                operation();
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int repetition = 0; repetition < 128; ++repetition)
            {
                operation();
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
#endif
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(17)]
        [TestCase(10001)]
        public void DictionaryAndFallbackSourcesPreserveOrderAndReenumeration(int count)
        {
            Dictionary<int, int> dictionary = new Dictionary<int, int>();
            for (int index = 0; index < count; ++index)
            {
                dictionary.Add(index, -index);
            }
            KeyValuePair<int, int>[] array = new KeyValuePair<int, int>[count];
            ((ICollection<KeyValuePair<int, int>>)dictionary).CopyTo(array, 0);
            IEnumerable<KeyValuePair<int, int>>[] sources =
            {
                dictionary,
                array,
                new List<KeyValuePair<int, int>>(array),
                new HashSet<KeyValuePair<int, int>>(array),
            };
            foreach (IEnumerable<KeyValuePair<int, int>> source in sources)
            {
                List<KeyValuePair<int, int>> expected = new List<KeyValuePair<int, int>>(source);
                ValueDictionaryEnumerable<int, int> view = source.AsValueDictionaryEnumerable();
                for (int repetition = 0; repetition < 2; ++repetition)
                {
                    List<KeyValuePair<int, int>> actual = new List<KeyValuePair<int, int>>();
                    foreach (KeyValuePair<int, int> entry in view)
                    {
                        actual.Add(entry);
                    }
                    CollectionAssert.AreEqual(expected, actual);
                }
            }
        }

        [Test]
        public void NullAndDefaultViewsAreEmptyAndRequiredNullPreservesFailure()
        {
            IEnumerable<KeyValuePair<int, int>> source = null;
            ValueDictionaryEnumerable<int, int>[] views =
            {
                source.AsValueDictionaryEnumerable(),
                default,
            };
            foreach (ValueDictionaryEnumerable<int, int> view in views)
            {
                using ValueDictionaryEnumerator<int, int> enumerator = view.GetEnumerator();
                Assert.IsFalse(enumerator.MoveNext());
                Assert.AreEqual(default(KeyValuePair<int, int>), enumerator.Current);
            }
            Assert.Throws<NullReferenceException>(() =>
                source.AsValueDictionaryEnumerableRequired().GetEnumerator()
            );
        }

        [Test]
        public void DictionaryMutationMatchesConcreteEnumerator()
        {
            Dictionary<int, int> source = new Dictionary<int, int> { { 1, 2 } };
            using Dictionary<int, int>.Enumerator original = source.GetEnumerator();
            using ValueDictionaryEnumerator<int, int> wrapped = source
                .AsValueDictionaryEnumerable()
                .GetEnumerator();
            Assert.IsTrue(original.MoveNext());
            Assert.IsTrue(wrapped.MoveNext());
            source.Add(3, 4);
            Assert.Throws<InvalidOperationException>(() => original.MoveNext());
            Assert.Throws<InvalidOperationException>(() => wrapped.MoveNext());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DerivedDictionaryKeepsInterfaceProtocolAndDisposal(bool failOnMove)
        {
            EnumerationDerivedDictionary source = new EnumerationDerivedDictionary
            {
                FailOnMove = failOnMove,
            };
            source.Add(1, 2);
            ValueDictionaryEnumerable<int, int> view = source.AsValueDictionaryEnumerable();
            Assert.AreEqual(0, source.Acquisitions);
            if (failOnMove)
            {
                Assert.Throws<InvalidOperationException>(() =>
                {
                    foreach (KeyValuePair<int, int> entry in view)
                    {
                        Assert.Fail(entry.ToString());
                    }
                });
            }
            else
            {
                foreach (KeyValuePair<int, int> entry in view)
                {
                    Assert.AreEqual(new KeyValuePair<int, int>(42, 24), entry);
                    break;
                }
            }
            Assert.AreEqual(1, source.Acquisitions);
            Assert.AreEqual(1, source.Disposals);
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
                foreach (KeyValuePair<int, int> entry in ToPairs(direct))
                {
                    Assert.AreEqual(1, entry.Key);
                    break;
                }
            });
            Assert.Throws<InvalidOperationException>(() =>
            {
                foreach (
                    KeyValuePair<int, int> entry in ToPairs(wrapped).AsValueDictionaryEnumerable()
                )
                {
                    Assert.AreEqual(1, entry.Key);
                    break;
                }
            });
            CollectionAssert.AreEqual(direct.Events, wrapped.Events);
        }

        [Test]
        public void AcquisitionFailureDoesNotInventCleanup()
        {
            EnumerationDerivedDictionary source = new EnumerationDerivedDictionary
            {
                FailOnAcquire = true,
            };
            ValueDictionaryEnumerable<int, int> view = source.AsValueDictionaryEnumerable();
            Assert.AreEqual(0, source.Acquisitions);
            Assert.Throws<InvalidOperationException>(() => view.GetEnumerator());
            Assert.AreEqual(1, source.Acquisitions);
            Assert.AreEqual(0, source.Disposals);
        }

        [Test]
        public void DictionaryAvoidsEnumeratorBoxingWithPositiveControl()
        {
#if ENABLE_IL2CPP && !UNITY_6000_2_OR_NEWER
            Assert.Ignore("The allocation counter is unsafe on older IL2CPP players.");
#else
            Dictionary<int, int> dictionary = new Dictionary<int, int> { { 1, 2 }, { 3, 4 } };
            IEnumerable<KeyValuePair<int, int>> source = dictionary;
            long control = Measure(() => Consume(source, false));
            if (control <= 0)
            {
                Assert.Ignore("The control did not observe interface enumerator allocation.");
            }
            Assert.Greater(control, 0);
            Assert.AreEqual(0, Measure(() => Consume(source, true)));
            Assert.AreEqual(6, _observedSum);
#endif
        }
    }
}
