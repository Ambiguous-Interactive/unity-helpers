// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    public sealed class ValueArrayEnumerableTests
    {
        private static int _observedCount;

        private static void Consume(Array source, bool wrapped)
        {
            int count = 0;
            if (wrapped)
            {
                foreach (object value in new ValueArrayEnumerable(source))
                {
                    if (value != null)
                    {
                        ++count;
                    }
                }
            }
            else
            {
                foreach (object value in source)
                {
                    if (value != null)
                    {
                        ++count;
                    }
                }
            }
            _observedCount = count;
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(17)]
        [TestCase(10001)]
        public void VectorsPreserveOrderAndReenumeration(int count)
        {
            string[] array = new string[count];
            for (int index = 0; index < count; ++index)
            {
                array[index] = index.ToString();
            }
            Array source = array;
            ValueArrayEnumerable view = new ValueArrayEnumerable(source);
            for (int repetition = 0; repetition < 2; ++repetition)
            {
                List<object> actual = new List<object>();
                foreach (object value in view)
                {
                    actual.Add(value);
                }
                CollectionAssert.AreEqual(array, actual);
            }
        }

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, true)]
        public void RanksAndLowerBoundsPreserveTheArrayProtocol(int rank, bool nonzeroBounds)
        {
            int[] lengths = new int[rank];
            int[] lowerBounds = new int[rank];
            int[] indices = new int[rank];
            int total = 1;
            for (int dimension = 0; dimension < rank; ++dimension)
            {
                lengths[dimension] = dimension + 2;
                lowerBounds[dimension] = nonzeroBounds ? dimension - 2 : 0;
                indices[dimension] = lowerBounds[dimension];
                total *= lengths[dimension];
            }
            Array source;
            try
            {
                source = Array.CreateInstance(typeof(int), lengths, lowerBounds);
            }
            catch (NotSupportedException)
            {
                Assert.Ignore("This runtime cannot create the array shape used by the control.");
                return;
            }
            for (int index = 0; index < total; ++index)
            {
                source.SetValue(index, indices);
                for (int dimension = rank - 1; 0 <= dimension; --dimension)
                {
                    ++indices[dimension];
                    if (indices[dimension] < lowerBounds[dimension] + lengths[dimension])
                    {
                        break;
                    }
                    indices[dimension] = lowerBounds[dimension];
                }
            }
            List<object> expected = new List<object>();
            foreach (object value in source)
            {
                expected.Add(value);
            }
            List<object> actual = new List<object>();
            foreach (object value in new ValueArrayEnumerable(source))
            {
                actual.Add(value);
            }
            CollectionAssert.AreEqual(expected, actual);
            Assert.AreEqual(total, actual.Count);
        }

        [Test]
        public void DefaultAndNullViewsAreEmpty()
        {
            ValueArrayEnumerable[] views = { default, new ValueArrayEnumerable(null) };
            foreach (ValueArrayEnumerable view in views)
            {
                using ValueArrayEnumerator enumerator = view.GetEnumerator();
                Assert.IsFalse(enumerator.MoveNext());
                Assert.IsFalse(enumerator.MoveNext());
            }
        }

        [TestCase(2, 0)]
        [TestCase(2, 1)]
        [TestCase(3, 0)]
        [TestCase(3, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 3)]
        public void EmptyDimensionsMatchTheArrayEnumerator(int rank, int emptyDimension)
        {
            int[] lengths = new int[rank];
            for (int dimension = 0; dimension < rank; ++dimension)
            {
                lengths[dimension] = dimension == emptyDimension ? 0 : 2;
            }
            Array source = Array.CreateInstance(typeof(int), lengths);
            IEnumerator original = source.GetEnumerator();
            using ValueArrayEnumerator wrapped = new ValueArrayEnumerable(source).GetEnumerator();
            Assert.IsFalse(original.MoveNext());
            Assert.IsFalse(wrapped.MoveNext());
            Assert.IsFalse(wrapped.MoveNext());
        }

        [Test]
        public void ElementMutationAndRepeatedEndMatchTheArrayEnumerator()
        {
            Array source = new[] { 1, 2 };
            IEnumerator original = source.GetEnumerator();
            using ValueArrayEnumerator wrapped = new ValueArrayEnumerable(source).GetEnumerator();
            Assert.IsTrue(original.MoveNext());
            Assert.IsTrue(wrapped.MoveNext());
            source.SetValue(42, 0);
            Assert.AreEqual(original.Current, wrapped.Current);
            Assert.IsTrue(wrapped.MoveNext());
            Assert.AreEqual(2, wrapped.Current);
            Assert.IsFalse(wrapped.MoveNext());
            Assert.IsFalse(wrapped.MoveNext());
        }

        [Test]
        public void ReferenceVectorsAvoidEnumeratorAllocationWithPositiveControl()
        {
#if ENABLE_IL2CPP && !UNITY_6000_2_OR_NEWER
            Assert.Ignore("The allocation counter is unsafe on older IL2CPP players.");
#else
            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            Array source = new[] { "first", "second" };
            Consume(source, false);
            Consume(source, true);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int repetition = 0; repetition < 128; ++repetition)
            {
                Consume(source, false);
            }
            long control = GC.GetAllocatedBytesForCurrentThread() - before;
            if (control <= 0)
            {
                Assert.Ignore("The control did not observe Array enumerator allocation.");
            }
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int repetition = 0; repetition < 128; ++repetition)
            {
                Consume(source, true);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated);
            Assert.AreEqual(2, _observedCount);
#endif
        }
    }
}
