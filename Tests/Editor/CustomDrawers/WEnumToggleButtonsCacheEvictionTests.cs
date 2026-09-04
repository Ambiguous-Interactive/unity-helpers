// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    public sealed class WEnumToggleButtonsCacheEvictionTests : CommonTestBase
    {
        private const int MaxPaginationStateEntries = 1024;
        private const int MaxLayoutCacheEntries = 512;
        private const int PaginationEvictionChurnKeyCount = MaxPaginationStateEntries + 128;
        private const int LayoutEvictionChurnKeyCount = MaxLayoutCacheEntries + 128;
        private const int PaginationTotalItems = 100;
        private const int PaginationPageSize = 10;
        private const int PinnedPageIndex = 7;
        private const int LayoutOptionCount = 8;
        private const int LayoutButtonsPerRow = 4;
        private const float LayoutWidth = 320f;
        private const float PinnedLayoutHeight = 123.5f;
        private const float HeightTolerance = 0.0001f;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            WEnumToggleButtonsPagination.Reset();
        }

        [TearDown]
        public void ResetEnumToggleCachesAfterTest()
        {
            WEnumToggleButtonsPagination.Reset();
        }

        /// <summary>
        /// Pins that the page a user has scrolled to survives a realistic run of other selections.
        /// Pagination state is the second piece of real user state behind a bound, and eviction costs
        /// the user that page rather than a recompute.
        /// </summary>
        /// <remarks>
        /// The pinned key is created FIRST and never touched again, the worst case for a
        /// least-recently-used bound.
        /// </remarks>
        [Test]
        [TestCase(2, TestName = "Churn.TwoOtherKeys")]
        [TestCase(128, TestName = "Churn.OneInspectorWorth")]
        [TestCase(MaxPaginationStateEntries - 1, TestName = "Churn.OneKeyShortOfTheBound")]
        public void PageIndexSurvivesTypicalSelectionChurn(int churnedKeyCount)
        {
            BoundedDrawerCacheChurnHost host =
                CreateScriptableObject<BoundedDrawerCacheChurnHost>();
            using SerializedObject serializedObject = new(host);
            SerializedProperty slots = ResizeSlotArray(serializedObject, churnedKeyCount + 1);

            WEnumToggleButtonsPagination.PaginationState pinnedState = GetPaginationState(slots, 0);
            pinnedState.PageIndex = PinnedPageIndex;
            Assert.AreEqual(
                PinnedPageIndex,
                pinnedState.PageIndex,
                "The page index was rejected, so this fixture has no user state to lose."
            );

            List<WEnumToggleButtonsPagination.PaginationState> churnedStates = new(churnedKeyCount);
            for (int keyIndex = 1; keyIndex <= churnedKeyCount; keyIndex++)
            {
                churnedStates.Add(GetPaginationState(slots, keyIndex));
            }

            HashSet<WEnumToggleButtonsPagination.PaginationState> distinctChurnedStates = new(
                churnedStates
            );
            Assert.AreEqual(
                churnedKeyCount,
                distinctChurnedStates.Count,
                "The churn did not produce one state per key, so it never populated the cache the "
                    + "way a selection change does."
            );
            Assert.AreSame(
                churnedStates[0],
                GetPaginationState(slots, 1),
                "The cache dropped the first churned key while still below its bound."
            );

            WEnumToggleButtonsPagination.PaginationState pinnedStateAfterChurn = GetPaginationState(
                slots,
                0
            );
            Assert.AreSame(
                pinnedState,
                pinnedStateAfterChurn,
                $"{churnedKeyCount} other keys evicted the page the user had scrolled to."
            );
            Assert.AreEqual(PinnedPageIndex, pinnedStateAfterChurn.PageIndex);
        }

        /// <summary>
        /// Pins that the pagination cache is bounded rather than merely large: past the bound the key
        /// touched longest ago is rebuilt on page one while the most recent key is still served from
        /// the cache.
        /// </summary>
        [Test]
        public void PaginationStateCacheEvictsTheKeyTouchedLongestAgo()
        {
            BoundedDrawerCacheChurnHost host =
                CreateScriptableObject<BoundedDrawerCacheChurnHost>();
            using SerializedObject serializedObject = new(host);
            SerializedProperty slots = ResizeSlotArray(
                serializedObject,
                PaginationEvictionChurnKeyCount + 1
            );

            WEnumToggleButtonsPagination.PaginationState oldestState = GetPaginationState(slots, 0);
            oldestState.PageIndex = PinnedPageIndex;

            List<WEnumToggleButtonsPagination.PaginationState> churnedStates = new(
                PaginationEvictionChurnKeyCount
            );
            for (int keyIndex = 1; keyIndex <= PaginationEvictionChurnKeyCount; keyIndex++)
            {
                churnedStates.Add(GetPaginationState(slots, keyIndex));
            }

            HashSet<WEnumToggleButtonsPagination.PaginationState> distinctChurnedStates = new(
                churnedStates
            );
            Assert.AreEqual(
                PaginationEvictionChurnKeyCount,
                distinctChurnedStates.Count,
                "The churn did not produce one state per key, so nothing crossed the bound."
            );
            Assert.AreSame(
                churnedStates[PaginationEvictionChurnKeyCount - 1],
                GetPaginationState(slots, PaginationEvictionChurnKeyCount),
                "The most recent key was not served from the cache, so the cache stopped caching "
                    + "rather than evicting."
            );

            WEnumToggleButtonsPagination.PaginationState oldestStateAfterChurn = GetPaginationState(
                slots,
                0
            );
            Assert.AreNotSame(
                oldestState,
                oldestStateAfterChurn,
                $"{PaginationEvictionChurnKeyCount} other keys left the oldest entry in place, so "
                    + "the cache still grows without limit."
            );
            Assert.AreEqual(
                0,
                oldestStateAfterChurn.PageIndex,
                "An evicted key must be rebuilt on page one rather than served a stale page."
            );
        }

        /// <summary>
        /// Pins that an evicted layout measurement reports a miss, so the drawer re-measures, and that
        /// a measurement below the bound is still answered from the cache.
        /// </summary>
        [Test]
        [TestCase(
            MaxLayoutCacheEntries - 1,
            true,
            TestName = "Churn.OneKeyShortOfTheBound.HeightRetained"
        )]
        [TestCase(
            LayoutEvictionChurnKeyCount,
            false,
            TestName = "Churn.PastTheBound.HeightRemeasured"
        )]
        public void LayoutHeightIsRemeasuredAfterEviction(int churnedKeyCount, bool expectRetained)
        {
            BoundedDrawerCacheChurnHost host =
                CreateScriptableObject<BoundedDrawerCacheChurnHost>();
            using SerializedObject serializedObject = new(host);
            SerializedProperty slots = ResizeSlotArray(serializedObject, churnedKeyCount + 1);

            LayoutSignature signature = WEnumToggleButtonsLayoutCache.CreateSignature(
                LayoutOptionCount,
                LayoutOptionCount,
                LayoutButtonsPerRow,
                supportsMultipleSelection: false,
                showSelectAll: false,
                showSelectNone: false,
                usePagination: false,
                hasSummary: false,
                LayoutWidth
            );

            SerializedProperty pinned = slots.GetArrayElementAtIndex(0);
            WEnumToggleButtonsLayoutCache.Store(pinned, signature, LayoutWidth, PinnedLayoutHeight);
            Assert.IsTrue(
                WEnumToggleButtonsLayoutCache.TryGetHeight(
                    pinned,
                    signature,
                    out float storedHeight
                ),
                "The measurement was never stored, so this fixture has nothing to evict."
            );
            Assert.AreEqual(PinnedLayoutHeight, storedHeight, HeightTolerance);

            for (int keyIndex = 1; keyIndex <= churnedKeyCount; keyIndex++)
            {
                WEnumToggleButtonsLayoutCache.Store(
                    slots.GetArrayElementAtIndex(keyIndex),
                    signature,
                    LayoutWidth,
                    PinnedLayoutHeight + keyIndex
                );
            }

            Assert.IsTrue(
                WEnumToggleButtonsLayoutCache.TryGetHeight(
                    slots.GetArrayElementAtIndex(churnedKeyCount),
                    signature,
                    out float newestHeight
                ),
                "The most recent measurement was not served from the cache, so the cache stopped "
                    + "caching rather than evicting."
            );
            Assert.AreEqual(PinnedLayoutHeight + churnedKeyCount, newestHeight, HeightTolerance);

            bool retained = WEnumToggleButtonsLayoutCache.TryGetHeight(
                pinned,
                signature,
                out float pinnedHeight
            );
            Assert.AreEqual(
                expectRetained,
                retained,
                $"{churnedKeyCount} other keys must "
                    + (
                        expectRetained
                            ? "leave the pinned measurement cached."
                            : "evict the pinned measurement so the drawer re-measures."
                    )
            );
            Assert.AreEqual(
                expectRetained ? PinnedLayoutHeight : 0f,
                pinnedHeight,
                HeightTolerance,
                "A miss must report a zero height so the caller re-measures instead of laying out a "
                    + "stale one."
            );
        }

        private static WEnumToggleButtonsPagination.PaginationState GetPaginationState(
            SerializedProperty slots,
            int slotIndex
        )
        {
            return WEnumToggleButtonsPagination.GetState(
                slots.GetArrayElementAtIndex(slotIndex),
                PaginationTotalItems,
                PaginationPageSize
            );
        }

        private static SerializedProperty ResizeSlotArray(
            SerializedObject serializedObject,
            int length
        )
        {
            serializedObject.Update();
            SerializedProperty slots = serializedObject.FindProperty(
                nameof(BoundedDrawerCacheChurnHost.slots)
            );
            Assert.NotNull(slots);
            slots.arraySize = length;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializedProperty resized = serializedObject.FindProperty(
                nameof(BoundedDrawerCacheChurnHost.slots)
            );
            Assert.AreEqual(length, resized.arraySize);
            return resized;
        }
    }
}
