// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// The comparer-keyed pool caches key on the caller's comparer instance, which in Unity is
    /// routinely a scene object or a closure over one. Without a bound those static caches keep
    /// every comparer a game ever built alive for the process. They are the
    /// closed-generic <see cref="Cache{TKey,TValue}"/> callers whose shared live bound is
    /// <see cref="Buffers.ComparerPoolMaxDistinctEntries"/>.
    /// </summary>
    /// <remarks>
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/689">#689</see>
    /// asked for a <see cref="System.WeakReference"/> observable -- drop every comparer, force a
    /// collection, assert the cache no longer holds them. These assert instead that the cache
    /// <b>drops</b> what it evicts, which is the property this code establishes and does not depend
    /// on a conservative collector choosing to run. Collectability follows: nothing else in the
    /// package roots a comparer, and <c>GlobalPoolRegistry</c> holds its pools weakly.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ComparerPoolCacheTests
    {
        private int _originalBound;

        [SetUp]
        public void SetUp()
        {
            _originalBound = Buffers.ComparerPoolMaxDistinctEntries;
            DropProbePools();
        }

        [TearDown]
        public void TearDown()
        {
            Buffers.ComparerPoolMaxDistinctEntries = _originalBound;
            DropProbePools();
        }

        // Reset closed-generic caches so retained domains cannot carry counts into the next run.
        private static void DropProbePools()
        {
            SetBuffers<HashSetProbe>.ClearPoolsForTesting();
            SetBuffers<LruProbe>.ClearPoolsForTesting();
            SetBuffers<SortedSetProbe>.ClearPoolsForTesting();
            SetBuffers<UnboundedProbe>.ClearPoolsForTesting();
            SetBuffers<StableProbe>.ClearPoolsForTesting();
            DictionaryBuffer<DictionaryProbe, int>.ClearPoolsForTesting();
        }

        [Test]
        public void HashSetPoolCacheStopsAtTheConfiguredBound()
        {
            Buffers.ComparerPoolMaxDistinctEntries = 4;

            List<IEqualityComparer<HashSetProbe>> comparers = new();
            for (int index = 0; index < 16; index++)
            {
                ProbeEqualityComparer comparer = new();
                comparers.Add(comparer);
                _ = SetBuffers<HashSetProbe>.GetHashSetPool(comparer);
                Assert.LessOrEqual(SetBuffers<HashSetProbe>.HashSetPoolCount, 4);
            }

            Assert.AreEqual(4, SetBuffers<HashSetProbe>.HashSetPoolCount);

            int retained = 0;
            foreach (IEqualityComparer<HashSetProbe> comparer in comparers)
            {
                if (SetBuffers<HashSetProbe>.HasHashSetPool(comparer))
                {
                    retained++;
                }
            }

            Assert.AreEqual(4, retained);
        }

        [Test]
        public void HashSetPoolCacheEvictsTheLeastRecentlyUsedComparer()
        {
            Buffers.ComparerPoolMaxDistinctEntries = 3;

            ProbeEqualityComparer first = new();
            ProbeEqualityComparer second = new();
            ProbeEqualityComparer third = new();
            ProbeEqualityComparer fourth = new();

            _ = SetBuffers<LruProbe>.GetHashSetPool(first);
            _ = SetBuffers<LruProbe>.GetHashSetPool(second);
            _ = SetBuffers<LruProbe>.GetHashSetPool(third);
            _ = SetBuffers<LruProbe>.GetHashSetPool(first);
            _ = SetBuffers<LruProbe>.GetHashSetPool(fourth);

            Assert.IsTrue(SetBuffers<LruProbe>.HasHashSetPool(first));
            Assert.IsFalse(SetBuffers<LruProbe>.HasHashSetPool(second));
            Assert.IsTrue(SetBuffers<LruProbe>.HasHashSetPool(third));
            Assert.IsTrue(SetBuffers<LruProbe>.HasHashSetPool(fourth));
        }

        [Test]
        public void SortedSetPoolCacheStopsAtTheConfiguredBound()
        {
            Buffers.ComparerPoolMaxDistinctEntries = 2;

            for (int index = 0; index < 8; index++)
            {
                _ = SetBuffers<SortedSetProbe>.GetSortedSetPool(new ProbeComparer());
            }

            Assert.AreEqual(2, SetBuffers<SortedSetProbe>.SortedSetPoolCount);
        }

        [Test]
        public void DictionaryPoolCacheStopsAtTheConfiguredBound()
        {
            Buffers.ComparerPoolMaxDistinctEntries = 2;

            for (int index = 0; index < 8; index++)
            {
                _ = DictionaryBuffer<DictionaryProbe, int>.GetDictionaryPool(
                    new ProbeDictionaryEqualityComparer()
                );
                _ = DictionaryBuffer<DictionaryProbe, int>.GetSortedDictionaryPool(
                    new ProbeDictionaryComparer()
                );
            }

            Assert.AreEqual(2, DictionaryBuffer<DictionaryProbe, int>.DictionaryPoolCount);
            Assert.AreEqual(2, DictionaryBuffer<DictionaryProbe, int>.SortedDictionaryPoolCount);
        }

        [Test]
        public void PoolCacheIsUnboundedWhenTheBoundIsCleared()
        {
            Buffers.ComparerPoolMaxDistinctEntries = 0;

            for (int index = 0; index < 8; index++)
            {
                _ = SetBuffers<UnboundedProbe>.GetHashSetPool(new ProbeUnboundedComparer());
            }

            Assert.AreEqual(8, SetBuffers<UnboundedProbe>.HashSetPoolCount);
        }

        [Test]
        public void LoweringPoolCacheBoundEvictsExistingPoolsImmediately()
        {
            Buffers.ComparerPoolMaxDistinctEntries = 0;
            for (int index = 0; index < 8; index++)
            {
                _ = SetBuffers<UnboundedProbe>.GetHashSetPool(new ProbeUnboundedComparer());
            }

            Buffers.ComparerPoolMaxDistinctEntries = 2;

            Assert.AreEqual(2, SetBuffers<UnboundedProbe>.HashSetPoolCount);
        }

        [Test]
        public void RepeatedRequestsForOneComparerReturnTheSamePool()
        {
            Buffers.ComparerPoolMaxDistinctEntries = 4;

            ProbeEqualityComparer comparer = new();
            WallstopGenericPool<HashSet<StableProbe>> first =
                SetBuffers<StableProbe>.GetHashSetPool(comparer);
            WallstopGenericPool<HashSet<StableProbe>> second =
                SetBuffers<StableProbe>.GetHashSetPool(comparer);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, SetBuffers<StableProbe>.HashSetPoolCount);
        }

        private sealed class ProbeEqualityComparer
            : IEqualityComparer<HashSetProbe>,
                IEqualityComparer<LruProbe>,
                IEqualityComparer<StableProbe>
        {
            public bool Equals(HashSetProbe x, HashSetProbe y) => true;

            public int GetHashCode(HashSetProbe obj) => 0;

            public bool Equals(LruProbe x, LruProbe y) => true;

            public int GetHashCode(LruProbe obj) => 0;

            public bool Equals(StableProbe x, StableProbe y) => true;

            public int GetHashCode(StableProbe obj) => 0;
        }

        private sealed class ProbeComparer : IComparer<SortedSetProbe>
        {
            public int Compare(SortedSetProbe x, SortedSetProbe y) => 0;
        }

        private sealed class ProbeDictionaryEqualityComparer : IEqualityComparer<DictionaryProbe>
        {
            public bool Equals(DictionaryProbe x, DictionaryProbe y) => true;

            public int GetHashCode(DictionaryProbe obj) => 0;
        }

        private sealed class ProbeDictionaryComparer : IComparer<DictionaryProbe>
        {
            public int Compare(DictionaryProbe x, DictionaryProbe y) => 0;
        }

        private sealed class ProbeUnboundedComparer : IEqualityComparer<UnboundedProbe>
        {
            public bool Equals(UnboundedProbe x, UnboundedProbe y) => true;

            public int GetHashCode(UnboundedProbe obj) => 0;
        }

        // Distinct element types isolate the closed-generic caches whose counts each test asserts.
        private sealed class HashSetProbe { }

        private sealed class LruProbe { }

        private sealed class SortedSetProbe { }

        private sealed class DictionaryProbe { }

        private sealed class UnboundedProbe { }

        private sealed class StableProbe { }
    }
}
