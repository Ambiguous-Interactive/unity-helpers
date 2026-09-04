// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    public sealed class WGuidDrawerStateEvictionTests : CommonTestBase
    {
        private const int MaxDrawerStateEntries = 512;
        private const int EvictionChurnPathCount = MaxDrawerStateEntries + 128;
        private const string PendingInvalidText = "not-a-guid";

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            WGuidPropertyDrawer.ClearCachedStates();
        }

        [TearDown]
        public void ClearDrawerStatesAfterTest()
        {
            WGuidPropertyDrawer.ClearCachedStates();
        }

        /// <summary>
        /// Pins the one eviction-safety claim the bound rests on: a user-typed invalid GUID awaiting
        /// correction is the only drawer state the serialized value cannot rebuild, and it outlives
        /// every other property path an inspector session realistically touches.
        /// </summary>
        /// <remarks>
        /// The pinned path is created FIRST and never touched again, which is the worst case for a
        /// least-recently-used bound; a user typing into the field keeps that entry most recently
        /// used instead.
        /// </remarks>
        [Test]
        [TestCase(2, TestName = "Churn.TwoOtherPaths")]
        [TestCase(64, TestName = "Churn.OneInspectorWorth")]
        [TestCase(MaxDrawerStateEntries - 1, TestName = "Churn.OnePathShortOfTheBound")]
        public void PendingInvalidTextSurvivesTypicalSelectionChurn(int churnedPathCount)
        {
            BoundedDrawerCacheChurnHost host =
                CreateScriptableObject<BoundedDrawerCacheChurnHost>();
            using SerializedObject serializedObject = new(host);
            SerializedProperty guids = ResizeGuidArray(serializedObject, churnedPathCount + 1);

            SerializedProperty pending = guids.GetArrayElementAtIndex(0);
            WGuidPropertyDrawer.DrawerState pendingState = WGuidPropertyDrawer.GetState(pending);
            WGuidPropertyDrawer.HandleTextChange(
                pending,
                pending.FindPropertyRelative(WGuid.LowFieldName),
                pending.FindPropertyRelative(WGuid.HighFieldName),
                pendingState,
                PendingInvalidText
            );
            Assert.IsTrue(
                pendingState.hasPendingInvalid,
                "The invalid text was never accepted, so this fixture has no pending state to lose."
            );
            Assert.AreEqual(PendingInvalidText, pendingState.displayText);

            List<WGuidPropertyDrawer.DrawerState> churnedStates = new(churnedPathCount);
            for (int pathIndex = 1; pathIndex <= churnedPathCount; pathIndex++)
            {
                churnedStates.Add(
                    WGuidPropertyDrawer.GetState(guids.GetArrayElementAtIndex(pathIndex))
                );
            }

            HashSet<WGuidPropertyDrawer.DrawerState> distinctChurnedStates = new(churnedStates);
            Assert.AreEqual(
                churnedPathCount,
                distinctChurnedStates.Count,
                "The churn did not produce one state per property path, so it never populated the "
                    + "cache the way a selection change does."
            );
            Assert.AreSame(
                churnedStates[0],
                WGuidPropertyDrawer.GetState(guids.GetArrayElementAtIndex(1)),
                "The cache dropped the first churned path while still below its bound."
            );

            WGuidPropertyDrawer.DrawerState pendingStateAfterChurn = WGuidPropertyDrawer.GetState(
                guids.GetArrayElementAtIndex(0)
            );
            Assert.AreSame(
                pendingState,
                pendingStateAfterChurn,
                $"{churnedPathCount} other property paths evicted the entry holding a user-typed "
                    + "invalid GUID."
            );
            Assert.IsTrue(pendingStateAfterChurn.hasPendingInvalid);
            Assert.AreEqual(PendingInvalidText, pendingStateAfterChurn.displayText);
        }

        /// <summary>
        /// Pins that the drawer state cache is bounded rather than merely large: past the bound the
        /// path touched longest ago is rebuilt from scratch while the most recent path is still
        /// served from the cache.
        /// </summary>
        [Test]
        public void DrawerStateCacheEvictsThePathTouchedLongestAgo()
        {
            BoundedDrawerCacheChurnHost host =
                CreateScriptableObject<BoundedDrawerCacheChurnHost>();
            using SerializedObject serializedObject = new(host);
            SerializedProperty guids = ResizeGuidArray(
                serializedObject,
                EvictionChurnPathCount + 1
            );

            SerializedProperty oldest = guids.GetArrayElementAtIndex(0);
            WGuidPropertyDrawer.DrawerState oldestState = WGuidPropertyDrawer.GetState(oldest);
            WGuidPropertyDrawer.HandleTextChange(
                oldest,
                oldest.FindPropertyRelative(WGuid.LowFieldName),
                oldest.FindPropertyRelative(WGuid.HighFieldName),
                oldestState,
                PendingInvalidText
            );
            Assert.IsTrue(oldestState.hasPendingInvalid);

            List<WGuidPropertyDrawer.DrawerState> churnedStates = new(EvictionChurnPathCount);
            for (int pathIndex = 1; pathIndex <= EvictionChurnPathCount; pathIndex++)
            {
                churnedStates.Add(
                    WGuidPropertyDrawer.GetState(guids.GetArrayElementAtIndex(pathIndex))
                );
            }

            HashSet<WGuidPropertyDrawer.DrawerState> distinctChurnedStates = new(churnedStates);
            Assert.AreEqual(
                EvictionChurnPathCount,
                distinctChurnedStates.Count,
                "The churn did not produce one state per property path, so nothing crossed the bound."
            );
            Assert.AreSame(
                churnedStates[EvictionChurnPathCount - 1],
                WGuidPropertyDrawer.GetState(guids.GetArrayElementAtIndex(EvictionChurnPathCount)),
                "The most recent path was not served from the cache, so the cache stopped caching "
                    + "rather than evicting."
            );

            WGuidPropertyDrawer.DrawerState oldestStateAfterChurn = WGuidPropertyDrawer.GetState(
                guids.GetArrayElementAtIndex(0)
            );
            Assert.AreNotSame(
                oldestState,
                oldestStateAfterChurn,
                $"{EvictionChurnPathCount} other property paths left the oldest entry in place, so "
                    + "the cache still grows without limit."
            );
            Assert.IsFalse(
                oldestStateAfterChurn.hasPendingInvalid,
                "An evicted path must be rebuilt as a clean state rather than a stale one."
            );
        }

        private static SerializedProperty ResizeGuidArray(
            SerializedObject serializedObject,
            int length
        )
        {
            serializedObject.Update();
            SerializedProperty guids = serializedObject.FindProperty(
                nameof(BoundedDrawerCacheChurnHost.guids)
            );
            Assert.NotNull(guids);
            guids.arraySize = length;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();

            SerializedProperty resized = serializedObject.FindProperty(
                nameof(BoundedDrawerCacheChurnHost.guids)
            );
            Assert.AreEqual(length, resized.arraySize);
            return resized;
        }
    }
}
