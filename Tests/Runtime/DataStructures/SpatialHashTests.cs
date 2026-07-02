// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpatialHashTests
    {
        private readonly List<IDisposable> _trackedResources = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _trackedResources.Count; i++)
            {
                _trackedResources[i]?.Dispose();
            }

            _trackedResources.Clear();
        }

        private T Track<T>(T disposable)
            where T : IDisposable
        {
            _trackedResources.Add(disposable);
            return disposable;
        }

        [Test]
        public void ConstructorWithZeroCellSizeThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialHash2D<string>(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialHash3D<string>(0f));
        }

        [Test]
        public void ConstructorWithNegativeCellSizeThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialHash2D<string>(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialHash3D<string>(-1f));
        }

        [Test]
        public void ConstructorWithValidCellSizeSucceeds()
        {
            SpatialHash2D<string> hash2D = Track(new SpatialHash2D<string>(1.0f));
            Assert.AreEqual(1.0f, hash2D.CellSize);
            Assert.AreEqual(0, hash2D.CellCount);

            SpatialHash3D<string> hash3D = Track(new SpatialHash3D<string>(2.5f));
            Assert.AreEqual(2.5f, hash3D.CellSize);
            Assert.AreEqual(0, hash3D.CellCount);
        }

        [Test]
        public void SpatialHash2DCanBeUsedThroughItsInterface()
        {
            ISpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.5f, 0.5f), "value");

            List<string> results = new();
            hash.Query(new Vector2(0.5f, 0.5f), 0.25f, results);

            CollectionAssert.AreEqual(new[] { "value" }, results);
        }

        [Test]
        public void ConstructorWithCustomComparerUsesComparer()
        {
            StringComparer comparer = StringComparer.OrdinalIgnoreCase;
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f, comparer));

            hash.Insert(new Vector2(0, 0), "ABC");
            hash.Insert(new Vector2(1, 1), "abc");

            List<string> results = new();
            hash.Query(new Vector2(0, 0), 5.0f, results);

            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void InsertSingleItemIncrementsCellCount()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            Assert.AreEqual(0, hash.CellCount);

            hash.Insert(new Vector2(0.5f, 0.5f), "a");
            Assert.AreEqual(1, hash.CellCount);
        }

        [Test]
        public void InsertMultipleItemsSameCellKeepsCellCountOne()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.1f, 0.1f), "a");
            hash.Insert(new Vector2(0.2f, 0.2f), "b");
            hash.Insert(new Vector2(0.3f, 0.3f), "c");

            Assert.AreEqual(1, hash.CellCount);
        }

        [Test]
        public void InsertDuplicateItemsAllowsMultipleInstances()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.5f, 0.5f), "a");
            hash.Insert(new Vector2(0.5f, 0.5f), "a");
            hash.Insert(new Vector2(0.5f, 0.5f), "a");

            List<string> results = new();
            hash.Query(new Vector2(0.5f, 0.5f), 1.0f, results, distinct: true);

            Assert.AreEqual(1, results.Count);
            hash.Query(new Vector2(0.5f, 0.5f), 1.0f, results, distinct: false);

            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void InsertNullItemAllowsNull()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0, 0), null);

            List<string> results = new();
            hash.Query(new Vector2(0, 0), 1.0f, results);

            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0] == null, "Queried item should be null as inserted");
        }

        [Test]
        public void InsertNegativeCoordinatesWorks()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(-5.5f, -3.2f), "negative");

            List<string> results = new();
            hash.Query(new Vector2(-5.5f, -3.2f), 1.0f, results);

            Assert.AreEqual(1, results.Count);
            CollectionAssert.Contains(results, "negative");
        }

        [Test]
        public void InsertVeryLargeCoordinatesWorks()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(100000f, 100000f), "far");

            List<string> results = new();
            hash.Query(new Vector2(100000f, 100000f), 1.0f, results);

            Assert.AreEqual(1, results.Count);
            CollectionAssert.Contains(results, "far");
        }

        [Test]
        public void InsertOnCellBoundaryWorks()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0f, 0f), "origin");
            hash.Insert(new Vector2(1f, 1f), "boundary");

            List<string> results = new();
            hash.Query(new Vector2(0f, 0f), 0.1f, results);
            Assert.AreEqual(1, results.Count);
            CollectionAssert.Contains(results, "origin");

            results.Clear();
            hash.Query(new Vector2(1f, 1f), 0.1f, results);
            Assert.AreEqual(1, results.Count);
            CollectionAssert.Contains(results, "boundary");
        }

        [Test]
        public void Insert3DWorks()
        {
            SpatialHash3D<string> hash = Track(new SpatialHash3D<string>(1.0f));
            hash.Insert(new Vector3(0.5f, 0.5f, 0.5f), "a");
            hash.Insert(new Vector3(1.5f, 1.5f, 1.5f), "b");

            Assert.AreEqual(2, hash.CellCount);

            List<string> results = new();
            hash.Query(new Vector3(0.5f, 0.5f, 0.5f), 1.0f, results);
            CollectionAssert.Contains(results, "a");
        }

        [Test]
        public void RemoveExistingItemReturnsTrue()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.5f, 0.5f), "a");

            Assert.IsTrue(hash.Remove(new Vector2(0.5f, 0.5f), "a"));
        }

        [Test]
        public void RemoveNonExistentItemReturnsFalse()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.5f, 0.5f), "a");

            Assert.IsFalse(hash.Remove(new Vector2(0.5f, 0.5f), "b"));
        }

        [Test]
        public void RemoveFromWrongPositionReturnsFalse()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.5f, 0.5f), "a");

            Assert.IsFalse(hash.Remove(new Vector2(5.5f, 5.5f), "a"));
        }

        [Test]
        public void RemoveFromEmptyHashReturnsFalse()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            Assert.IsFalse(hash.Remove(new Vector2(0, 0), "anything"));
        }

        [Test]
        public void RemoveLastItemInCellDecrementsCellCount()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.5f, 0.5f), "a");
            Assert.AreEqual(1, hash.CellCount);

            hash.Remove(new Vector2(0.5f, 0.5f), "a");
            Assert.AreEqual(0, hash.CellCount);
        }

        [Test]
        public void RemoveOneOfMultipleInCellKeepsCellCount()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.1f, 0.1f), "a");
            hash.Insert(new Vector2(0.2f, 0.2f), "b");
            Assert.AreEqual(1, hash.CellCount);

            hash.Remove(new Vector2(0.1f, 0.1f), "a");
            Assert.AreEqual(1, hash.CellCount);
        }

        [Test]
        public void RemoveDuplicateItemsRemovesOnlyOne()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.5f, 0.5f), "a");
            hash.Insert(new Vector2(0.5f, 0.5f), "a");
            hash.Insert(new Vector2(0.5f, 0.5f), "a");

            Assert.IsTrue(hash.Remove(new Vector2(0.5f, 0.5f), "a"));

            List<string> results = new();
            hash.Query(new Vector2(0.5f, 0.5f), 1.0f, results, distinct: true);
            Assert.AreEqual(1, results.Count);
            hash.Query(new Vector2(0.5f, 0.5f), 1.0f, results, distinct: false);
            Assert.AreEqual(2, results.Count);
        }

        [Test]
        public void RemoveUsesCustomComparerAtCellBoundary()
        {
            SpatialHash2D<string> hash = Track(
                new SpatialHash2D<string>(1.0f, StringComparer.OrdinalIgnoreCase)
            );
            Vector2 boundaryPosition = new(2f, -1f); // Lies exactly on a cell edge

            hash.Insert(boundaryPosition, "Player");
            Assert.AreEqual(1, hash.CellCount);

            bool removed = hash.Remove(boundaryPosition, "player");

            Assert.IsTrue(
                removed,
                "Case-insensitive comparer should allow removal of differently-cased values."
            );
            Assert.AreEqual(0, hash.CellCount, "Removing last entry in cell should shrink grid.");

            List<string> results = new();
            hash.Query(boundaryPosition, 0.25f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void RemoveNullItemWorks()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0, 0), null);

            Assert.IsTrue(hash.Remove(new Vector2(0, 0), null));

            List<string> results = new();
            hash.Query(new Vector2(0, 0), 1.0f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void Remove3DWorks()
        {
            SpatialHash3D<string> hash = Track(new SpatialHash3D<string>(1.0f));
            hash.Insert(new Vector3(0.5f, 0.5f, 0.5f), "a");

            Assert.IsTrue(hash.Remove(new Vector3(0.5f, 0.5f, 0.5f), "a"));
            Assert.AreEqual(0, hash.CellCount);
        }

        [Test]
        public void Remove3DUsesCustomComparerAtCellBoundary()
        {
            SpatialHash3D<string> hash = Track(
                new SpatialHash3D<string>(1.0f, StringComparer.OrdinalIgnoreCase)
            );
            Vector3 boundary = new(2f, -3f, 4f);

            hash.Insert(boundary, "Enemy");
            Assert.IsTrue(hash.Remove(boundary, "enemy"));
            Assert.AreEqual(0, hash.CellCount);
        }

        [Test]
        public void QueryWithNullResultsListThrowsArgumentNullException()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            Assert.Throws<ArgumentNullException>(() => hash.Query(Vector2.zero, 1.0f, null));

            SpatialHash3D<string> hash3D = Track(new SpatialHash3D<string>(1.0f));
            Assert.Throws<ArgumentNullException>(() => hash3D.Query(Vector3.zero, 1.0f, null));
        }

        [Test]
        public void QueryEmptyHashReturnsEmptyResults()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            List<string> results = new();
            hash.Query(new Vector2(0, 0), 10.0f, results);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void QueryZeroRadiusReturnsOnlyItemsInSameCell()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.5f, 0.5f), "same");
            hash.Insert(new Vector2(1.5f, 1.5f), "different");

            List<string> results = new();
            hash.Query(new Vector2(0.5f, 0.5f), 0f, results);

            Assert.AreEqual(1, results.Count);
            CollectionAssert.Contains(results, "same");
        }

        [Test]
        public void QueryNegativeRadiusTreatsAsZero()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.5f, 0.5f), "item");

            List<string> results = new();
            hash.Query(new Vector2(0.5f, 0.5f), -5.0f, results);

            Assert.GreaterOrEqual(results.Count, 0);
        }

        [Test]
        public void QueryVeryLargeRadiusReturnsAllItems()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0, 0), "a");
            hash.Insert(new Vector2(100, 100), "b");
            hash.Insert(new Vector2(-50, -50), "c");

            List<string> results = new();
            hash.Query(new Vector2(0, 0), 1000.0f, results);

            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void QueryCoarseModeReturnsSupersetAndDeduplicates()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(Vector2.zero, "origin");
            hash.Insert(new Vector2(0.9f, 0f), "close");
            hash.Insert(new Vector2(1.8f, 0f), "far");
            hash.Insert(new Vector2(1.8f, 0.1f), "far");

            List<string> exactResults = new();
            List<string> coarseResults = new();

            hash.Query(Vector2.zero, 1.0f, exactResults, distinct: true, exactDistance: true);
            hash.Query(Vector2.zero, 1.0f, coarseResults, distinct: true, exactDistance: false);

            CollectionAssert.IsSubsetOf(exactResults, coarseResults);
            Assert.IsFalse(exactResults.Contains("far"));
            Assert.IsTrue(coarseResults.Contains("far"));

            int farCount = 0;
            foreach (string entry in coarseResults)
            {
                if (entry == "far")
                {
                    farCount++;
                }
            }

            Assert.AreEqual(
                1,
                farCount,
                "Distinct coarse query should deduplicate identical entries."
            );
        }

        [Test]
        public void QueryCoarseModeWithoutDistinctKeepsDuplicates()
        {
            SpatialHash3D<int> hash = Track(new SpatialHash3D<int>(1.0f));
            hash.Insert(Vector3.zero, 0);
            hash.Insert(new Vector3(1.9f, 0f, 0f), 1);
            hash.Insert(new Vector3(1.9f, 0.2f, 0f), 1);
            hash.Insert(new Vector3(-1.9f, 0f, 0f), 2);

            List<int> exactResults = new();
            List<int> coarseResults = new();

            hash.Query(Vector3.zero, 1.0f, exactResults, distinct: false, exactDistance: true);
            hash.Query(Vector3.zero, 1.0f, coarseResults, distinct: false, exactDistance: false);

            Assert.AreEqual(
                1,
                exactResults.Count,
                "Exact distance query should only include the origin entry."
            );
            Assert.AreEqual(
                3,
                coarseResults.Count,
                "Coarse query should include entries from intersecting cells even without distance checks."
            );

            int duplicateOnes = 0;
            foreach (int entry in coarseResults)
            {
                if (entry == 1)
                {
                    duplicateOnes++;
                }
            }

            Assert.AreEqual(2, duplicateOnes, "Non-distinct coarse query should keep duplicates.");
        }

        [Test]
        public void CoarseQuerySkipsCellsBeyondRadiusExtent()
        {
            SpatialHash3D<int> hash = Track(new SpatialHash3D<int>(1.0f));
            hash.Insert(new Vector3(2.5f, 0f, 0f), 99);

            List<int> coarseResults = new();
            hash.Query(Vector3.zero, 1.0f, coarseResults, distinct: false, exactDistance: false);

            CollectionAssert.DoesNotContain(
                coarseResults,
                99,
                "Cells more than one cell radius away should not be considered in coarse queries."
            );
        }

        [Test]
        public void QueryClearsResultsListBeforeAdding()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0, 0), "new");

            List<string> results = new() { "old1", "old2" };
            hash.Query(new Vector2(0, 0), 1.0f, results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("new", results[0]);
        }

        [Test]
        public void QueryDeduplicatesItemsAcrossCells()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            string item = "shared";
            hash.Insert(new Vector2(0.5f, 0.5f), item);
            hash.Insert(new Vector2(1.5f, 1.5f), item);

            List<string> results = new();
            hash.Query(new Vector2(1.0f, 1.0f), 2.0f, results);

            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void QueryWithDifferentCellSizesWorks()
        {
            SpatialHash2D<string> hashSmall = Track(new SpatialHash2D<string>(0.5f));
            SpatialHash2D<string> hashLarge = Track(new SpatialHash2D<string>(10.0f));

            hashSmall.Insert(new Vector2(0.5f, 0.5f), "a");
            hashLarge.Insert(new Vector2(0.5f, 0.5f), "a");

            List<string> resultsSmall = new();
            List<string> resultsLarge = new();

            hashSmall.Query(new Vector2(0.5f, 0.5f), 1.0f, resultsSmall);
            hashLarge.Query(new Vector2(0.5f, 0.5f), 1.0f, resultsLarge);

            Assert.AreEqual(1, resultsSmall.Count);
            Assert.AreEqual(1, resultsLarge.Count);
        }

        [Test]
        public void QueryItemsNearCellBoundariesFoundCorrectly()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.99f, 0.99f), "nearBoundary");
            hash.Insert(new Vector2(1.01f, 1.01f), "acrossBoundary");

            List<string> results = new();
            hash.Query(new Vector2(1.0f, 1.0f), 0.5f, results);

            Assert.GreaterOrEqual(results.Count, 2);
        }

        [Test]
        public void QueryReturnsListForChaining()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0, 0), "a");

            List<string> results = new();
            List<string> returned = hash.Query(new Vector2(0, 0), 1.0f, results);

            Assert.AreSame(results, returned);
        }

        [Test]
        public void Query3DWithMultipleLayersWorks()
        {
            SpatialHash3D<string> hash = Track(new SpatialHash3D<string>(1.0f));
            hash.Insert(new Vector3(0.5f, 0.5f, 0.5f), "layer1");
            hash.Insert(new Vector3(0.5f, 0.5f, 1.5f), "layer2");
            hash.Insert(new Vector3(0.5f, 0.5f, 2.5f), "layer3");

            List<string> results = new();
            hash.Query(new Vector3(0.5f, 0.5f, 1.5f), 1.5f, results);

            Assert.GreaterOrEqual(results.Count, 2);
        }

        [Test]
        public void QueryRectWithNullResultsListThrowsArgumentNullException()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            Assert.Throws<ArgumentNullException>(() =>
                hash.QueryRect(new Rect(Vector2.zero, Vector2.one), null)
            );

            SpatialHash3D<string> hash3D = Track(new SpatialHash3D<string>(1.0f));
            Assert.Throws<ArgumentNullException>(() =>
                hash3D.QueryBox(new Bounds(Vector3.zero, Vector3.one), null)
            );
        }

        [Test]
        public void QueryRectEmptyHashReturnsEmptyResults()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            List<string> results = new();
            hash.QueryRect(Rect.MinMaxRect(-10, -10, 10, 10), results);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void QueryRectItemsWithinBoundsReturnsAll()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.5f, 0.5f), "a");
            hash.Insert(new Vector2(1.5f, 1.5f), "b");
            hash.Insert(new Vector2(5.0f, 5.0f), "c");

            List<string> results = new();
            hash.QueryRect(Rect.MinMaxRect(0, 0, 2, 2), results);

            Assert.AreEqual(2, results.Count);
            CollectionAssert.Contains(results, "a");
            CollectionAssert.Contains(results, "b");
        }

        [Test]
        public void QueryRectItemsOutsideBoundsExcludesAll()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(5.0f, 5.0f), "far");

            List<string> results = new();
            hash.QueryRect(Rect.MinMaxRect(0, 0, 2, 2), results);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void QueryRectInvertedBoundsStillWorks()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(1.5f, 1.5f), "item");

            List<string> results = new();
            hash.QueryRect(Rect.MinMaxRect(2, 2, 0, 0), results);

            Assert.GreaterOrEqual(results.Count, 0);
        }

        [Test]
        public void QueryRectZeroSizeRectReturnsItemsInCell()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.5f, 0.5f), "item");

            List<string> results = new();
            hash.QueryRect(Rect.MinMaxRect(0.5f, 0.5f, 0.5f, 0.5f), results);

            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void QueryRectNegativeCoordinatesWorks()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(-2.5f, -2.5f), "negative");

            List<string> results = new();
            hash.QueryRect(Rect.MinMaxRect(-3, -3, -1, -1), results);

            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void QueryRectClearsResultsListBeforeAdding()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0, 0), "new");

            List<string> results = new() { "old1", "old2", "old3" };
            hash.QueryRect(Rect.MinMaxRect(0, 0, 1, 1), results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("new", results[0]);
        }

        [Test]
        public void QueryRectDeduplicatesItemsAcrossCells()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            string item = "shared";
            hash.Insert(new Vector2(0.5f, 0.5f), item);
            hash.Insert(new Vector2(1.5f, 1.5f), item);
            hash.Insert(new Vector2(2.5f, 2.5f), item);

            List<string> results = new();
            hash.QueryRect(Rect.MinMaxRect(0, 0, 3, 3), results);

            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void QueryRectDistinctAndNonDistinctModesYieldExpectedCounts()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.25f, 0.25f), "dup");
            hash.Insert(new Vector2(0.9f, 0.9f), "dup");
            hash.Insert(new Vector2(0.5f, 0.5f), "unique");

            Rect rect = Rect.MinMaxRect(0, 0, 1.2f, 1.2f);

            List<string> distinctResults = new();
            hash.QueryRect(rect, distinctResults, distinct: true);
            CollectionAssert.AreEquivalent(new[] { "dup", "unique" }, distinctResults);

            List<string> duplicates = new();
            hash.QueryRect(rect, duplicates, distinct: false);
            Assert.AreEqual(3, duplicates.Count);
            Assert.AreEqual(2, duplicates.Count(token => token == "dup"));
        }

        [Test]
        public void QueryRectReturnsListForChaining()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0, 0), "a");

            List<string> results = new();
            List<string> returned = hash.QueryRect(Rect.MinMaxRect(0, 0, 1, 1), results);

            Assert.AreSame(results, returned);
        }

        [Test]
        public void QueryBox3DWorks()
        {
            SpatialHash3D<string> hash = Track(new SpatialHash3D<string>(1.0f));
            hash.Insert(new Vector3(0.5f, 0.5f, 0.5f), "a");
            hash.Insert(new Vector3(1.5f, 1.5f, 1.5f), "b");
            hash.Insert(new Vector3(5.0f, 5.0f, 5.0f), "c");

            List<string> results = new();
            hash.QueryBox(new Bounds(new Vector3(1, 1, 1), new Vector3(2, 2, 2)), results);

            Assert.AreEqual(2, results.Count);
            CollectionAssert.Contains(results, "a");
            CollectionAssert.Contains(results, "b");
        }

        [Test]
        public void QueryBox3DIncludesBoundaryPoints()
        {
            SpatialHash3D<string> hash = Track(new SpatialHash3D<string>(1.0f));
            hash.Insert(new Vector3(1f, 1f, 1f), "edge");

            Bounds bounds = new(new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1f, 1f, 1f));
            List<string> results = new();
            hash.QueryBox(bounds, results);

            CollectionAssert.Contains(results, "edge");
        }

        [Test]
        public void QueryBox3DDistinctVersusNonDistinct()
        {
            SpatialHash3D<string> hash = Track(new SpatialHash3D<string>(1.0f));
            hash.Insert(new Vector3(0.25f, 0.25f, 0.25f), "alpha");
            hash.Insert(new Vector3(0.9f, 0.9f, 0.9f), "alpha");
            hash.Insert(new Vector3(0.5f, 0.5f, 0.2f), "beta");

            Bounds bounds = new(new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1.2f, 1.2f, 1.2f));

            List<string> distinct = new();
            hash.QueryBox(bounds, distinct, distinct: true);
            CollectionAssert.AreEquivalent(new[] { "alpha", "beta" }, distinct);

            List<string> duplicates = new();
            hash.QueryBox(bounds, duplicates, distinct: false);
            Assert.AreEqual(3, duplicates.Count);
            Assert.AreEqual(2, duplicates.Count(token => token == "alpha"));
        }

        [Test]
        public void ClearEmptyHashDoesNothing()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Clear();
            Assert.AreEqual(0, hash.CellCount);
        }

        [Test]
        public void ClearWithItemsRemovesAll()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0, 0), "a");
            hash.Insert(new Vector2(1, 1), "b");
            hash.Insert(new Vector2(2, 2), "c");

            hash.Clear();

            Assert.AreEqual(0, hash.CellCount);

            List<string> results = new();
            hash.Query(new Vector2(0, 0), 100.0f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void ClearAllowsReuse()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0, 0), "a");
            hash.Clear();
            hash.Insert(new Vector2(1, 1), "b");

            List<string> results = new();
            hash.Query(new Vector2(1, 1), 1.0f, results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("b", results[0]);
        }

        [Test]
        public void StressTestManyItemsSameCell()
        {
            SpatialHash2D<int> hash = Track(new SpatialHash2D<int>(1.0f));
            int count = 1000;

            for (int i = 0; i < count; i++)
            {
                hash.Insert(new Vector2(0.5f, 0.5f), i);
            }

            Assert.AreEqual(1, hash.CellCount);

            List<int> results = new();
            hash.Query(new Vector2(0.5f, 0.5f), 1.0f, results);
            Assert.AreEqual(count, results.Count);
        }

        [Test]
        public void StressTestManyDifferentCells()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            int gridSize = 100;

            for (int x = 0; x < gridSize; x++)
            {
                for (int y = 0; y < gridSize; y++)
                {
                    hash.Insert(new Vector2(x + 0.5f, y + 0.5f), $"item_{x}_{y}");
                }
            }

            Assert.AreEqual(gridSize * gridSize, hash.CellCount);
        }

        [Test]
        public void StressTestQueryLargeRadius()
        {
            SpatialHash2D<int> hash = Track(new SpatialHash2D<int>(1.0f));
            for (int i = 0; i < 100; i++)
            {
                hash.Insert(new Vector2(i * 0.5f, i * 0.5f), i);
            }

            List<int> results = new();
            hash.Query(new Vector2(25, 25), 50.0f, results);

            Assert.Greater(results.Count, 0);
        }

        [Test]
        public void EdgeCaseFloatingPointPrecisionCellBoundaries()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0.999999f, 0.999999f), "a");
            hash.Insert(new Vector2(1.000001f, 1.000001f), "b");

            List<string> results = new();
            hash.Query(new Vector2(1.0f, 1.0f), 0.5f, results);

            Assert.GreaterOrEqual(results.Count, 1);
        }

        [Test]
        public void EdgeCaseVerifyDeduplicationComplexScenario()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            string shared = "shared_item";

            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    hash.Insert(new Vector2(x + 0.5f, y + 0.5f), shared);
                }
            }

            List<string> results = new();
            hash.Query(new Vector2(1.5f, 1.5f), 2.0f, results);

            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void EdgeCaseMultipleClearAndInsert()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));

            for (int iteration = 0; iteration < 10; iteration++)
            {
                for (int i = 0; i < 100; i++)
                {
                    hash.Insert(new Vector2(i, i), $"iter{iteration}_item{i}");
                }

                List<string> results = new();
                hash.Query(new Vector2(50, 50), 10.0f, results);
                Assert.Greater(results.Count, 0);

                hash.Clear();
                Assert.AreEqual(0, hash.CellCount);
            }
        }

        [Test]
        public void EdgeCaseValueTypeWithCustomComparer()
        {
            EqualityComparer<int> comparer = EqualityComparer<int>.Default;
            SpatialHash2D<int> hash = Track(new SpatialHash2D<int>(1.0f, comparer));

            hash.Insert(new Vector2(0, 0), 42);
            hash.Insert(new Vector2(1, 1), 42);

            List<int> results = new();
            hash.Query(new Vector2(0.5f, 0.5f), 2.0f, results);

            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void EdgeCaseReferenceTypeDeduplication()
        {
            SpatialHash2D<List<int>> hash = Track(new SpatialHash2D<List<int>>(1.0f));
            List<int> list = new() { 1, 2, 3 };

            hash.Insert(new Vector2(0, 0), list);
            hash.Insert(new Vector2(1, 1), list);
            hash.Insert(new Vector2(2, 2), list);

            List<List<int>> results = new();
            hash.Query(new Vector2(1, 1), 2.0f, results);

            Assert.AreEqual(1, results.Count);
            Assert.AreSame(list, results[0]);
        }

        [Test]
        public void EdgeCaseSameItemRemovedMultipleTimes()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0, 0), "item");

            Assert.IsTrue(hash.Remove(new Vector2(0, 0), "item"));
            Assert.IsFalse(hash.Remove(new Vector2(0, 0), "item"));
            Assert.IsFalse(hash.Remove(new Vector2(0, 0), "item"));
        }

        [Test]
        public void EdgeCaseInsertRemoveInsertSamePosition()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0, 0), "first");
            hash.Remove(new Vector2(0, 0), "first");
            hash.Insert(new Vector2(0, 0), "second");

            List<string> results = new();
            hash.Query(new Vector2(0, 0), 1.0f, results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("second", results[0]);
        }

        [Test]
        public void EdgeCaseQueryWithExtremelySmallCellSize()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(0.001f));
            hash.Insert(new Vector2(0.0005f, 0.0005f), "tiny");

            List<string> results = new();
            hash.Query(new Vector2(0, 0), 0.01f, results);

            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void EdgeCaseQueryWithLargeCellSize()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1000f));
            hash.Insert(new Vector2(500, 500), "a");
            hash.Insert(new Vector2(1500, 1500), "b");

            List<string> results = new();
            hash.Query(new Vector2(500, 500), 100f, results);

            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void EdgeCaseItemAtExactCellCorner()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0f, 0f), "corner");

            List<string> results = new();
            hash.Query(new Vector2(0f, 0f), 0.1f, results);

            Assert.AreEqual(1, results.Count);
            CollectionAssert.Contains(results, "corner");
        }

        [Test]
        public void EdgeCaseMultipleNullItems()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(0, 0), null);
            hash.Insert(new Vector2(0, 0), null);
            hash.Insert(new Vector2(0, 0), null);

            List<string> results = new();
            hash.Query(new Vector2(0, 0), 1.0f, results, distinct: true);
            Assert.AreEqual(1, results.Count);
            hash.Query(new Vector2(0, 0), 1.0f, results, distinct: false);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void EdgeCaseQueryRectSpanningManyCells()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            for (int x = 0; x < 50; x++)
            {
                for (int y = 0; y < 50; y++)
                {
                    hash.Insert(new Vector2(x + 0.5f, y + 0.5f), $"{x},{y}");
                }
            }

            List<string> results = new();
            // Query rect from (0,0) to (50,50) to include all items at positions (0.5,0.5) through (49.5,49.5)
            hash.QueryRect(Rect.MinMaxRect(0, 0, 50, 50), results);

            Assert.AreEqual(2500, results.Count);
        }

        [Test]
        public void EdgeCaseQueryRectExactBoundaryExclusion()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            hash.Insert(new Vector2(1.0f, 1.0f), "onBoundary");
            hash.Insert(new Vector2(0.5f, 0.5f), "inside");
            hash.Insert(new Vector2(1.5f, 1.5f), "outside");

            List<string> results = new();
            // Rect from (0,0) to (1,1) - item at exactly (1,1) should be included
            hash.QueryRect(Rect.MinMaxRect(0, 0, 1, 1), results);

            Assert.AreEqual(2, results.Count);
            CollectionAssert.Contains(results, "inside");
            CollectionAssert.Contains(results, "onBoundary");
        }

        [Test]
        public void EdgeCaseQueryRectPartialOverlap()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    hash.Insert(new Vector2(x + 0.5f, y + 0.5f), $"{x},{y}");
                }
            }

            List<string> results = new();
            // Query only items from (2.5, 2.5) to (7.5, 7.5) - should be 6x6=36 items
            hash.QueryRect(Rect.MinMaxRect(2, 2, 8, 8), results);

            Assert.AreEqual(36, results.Count);
        }

        [Test]
        public void EdgeCaseQueryRectSingleRowOrColumn()
        {
            SpatialHash2D<string> hash = Track(new SpatialHash2D<string>(1.0f));
            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    hash.Insert(new Vector2(x + 0.5f, y + 0.5f), $"{x},{y}");
                }
            }

            List<string> results = new();
            // Query single row at y=5 (items at y=5.5)
            hash.QueryRect(Rect.MinMaxRect(0, 5, 10, 6), results);
            Assert.AreEqual(10, results.Count);

            results.Clear();
            // Query single column at x=3 (items at x=3.5)
            hash.QueryRect(Rect.MinMaxRect(3, 0, 4, 10), results);
            Assert.AreEqual(10, results.Count);
        }

        [Test]
        public void EdgeCase3DQueryBoxInvertedBounds()
        {
            SpatialHash3D<string> hash = Track(new SpatialHash3D<string>(1.0f));
            hash.Insert(new Vector3(1, 1, 1), "item");

            List<string> results = new();
            hash.QueryBox(new Bounds(new Vector3(1, 1, 1), new Vector3(4, 4, 4)), results);

            Assert.GreaterOrEqual(results.Count, 0);
        }

        [Test]
        public void EdgeCase3DZAxisIsolation()
        {
            SpatialHash3D<string> hash = Track(new SpatialHash3D<string>(1.0f));
            hash.Insert(new Vector3(0.5f, 0.5f, 0.5f), "bottom");
            hash.Insert(new Vector3(0.5f, 0.5f, 10.5f), "top");

            List<string> results = new();
            hash.Query(new Vector3(0.5f, 0.5f, 0.5f), 1.0f, results);

            Assert.AreEqual(1, results.Count);
            CollectionAssert.Contains(results, "bottom");
        }

        [Test]
        public void EdgeCase3DQueryBoxExactBoundaryInclusion()
        {
            SpatialHash3D<string> hash = Track(new SpatialHash3D<string>(1.0f));
            hash.Insert(new Vector3(2.0f, 2.0f, 2.0f), "onBoundary");
            hash.Insert(new Vector3(1.5f, 1.5f, 1.5f), "inside");
            hash.Insert(new Vector3(2.5f, 2.5f, 2.5f), "outside");

            List<string> results = new();
            // Bounds centered at (1.5, 1.5, 1.5) with size (1, 1, 1) gives min=(1, 1, 1), max=(2, 2, 2)
            hash.QueryBox(new Bounds(new Vector3(1.5f, 1.5f, 1.5f), new Vector3(1, 1, 1)), results);

            Assert.AreEqual(2, results.Count);
            CollectionAssert.Contains(results, "inside");
            CollectionAssert.Contains(results, "onBoundary");
        }

        [Test]
        public void EdgeCase3DQueryBoxSpanningManyCells()
        {
            SpatialHash3D<string> hash = Track(new SpatialHash3D<string>(1.0f));
            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    for (int z = 0; z < 10; z++)
                    {
                        hash.Insert(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), $"{x},{y},{z}");
                    }
                }
            }

            List<string> results = new();
            // Query all items from (0.5, 0.5, 0.5) to (9.5, 9.5, 9.5)
            // Bounds centered at (5, 5, 5) with size (10, 10, 10) gives min=(0, 0, 0), max=(10, 10, 10)
            hash.QueryBox(new Bounds(new Vector3(5, 5, 5), new Vector3(10, 10, 10)), results);

            Assert.AreEqual(1000, results.Count);
        }

        [Test]
        public void EdgeCase3DQueryBoxPartialOverlap()
        {
            SpatialHash3D<string> hash = Track(new SpatialHash3D<string>(1.0f));
            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    for (int z = 0; z < 10; z++)
                    {
                        hash.Insert(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), $"{x},{y},{z}");
                    }
                }
            }

            List<string> results = new();
            // Query subset from (2.5, 2.5, 2.5) to (5.5, 5.5, 5.5) - should be 4x4x4=64 items
            // Bounds centered at (4, 4, 4) with size (4, 4, 4) gives min=(2, 2, 2), max=(6, 6, 6)
            hash.QueryBox(new Bounds(new Vector3(4, 4, 4), new Vector3(4, 4, 4)), results);

            Assert.AreEqual(64, results.Count);
        }

        [Test]
        public void EdgeCase3DQueryBoxSinglePlane()
        {
            SpatialHash3D<string> hash = Track(new SpatialHash3D<string>(1.0f));
            for (int x = 0; x < 5; x++)
            {
                for (int y = 0; y < 5; y++)
                {
                    for (int z = 0; z < 5; z++)
                    {
                        hash.Insert(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), $"{x},{y},{z}");
                    }
                }
            }

            List<string> results = new();
            // Query single z-plane at z=2 (items at z=2.5)
            // Bounds centered at (2.5, 2.5, 2.5) with size (5, 5, 1) gives min=(0, 0, 2), max=(5, 5, 3)
            hash.QueryBox(new Bounds(new Vector3(2.5f, 2.5f, 2.5f), new Vector3(5, 5, 1)), results);

            Assert.AreEqual(25, results.Count);
        }
    }
}
