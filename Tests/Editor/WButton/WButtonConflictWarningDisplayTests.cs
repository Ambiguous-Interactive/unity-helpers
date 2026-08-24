// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WButton
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;

    /// <summary>
    /// Comprehensive tests for WButton conflict warning display functionality.
    /// Verifies warning generation, content accuracy, caching behavior,
    /// and handling of all conflict types (placement, priority, draw order).
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WButtonConflictWarningDisplayTests : BatchedEditorTestBase
    {
        [SetUp]
        public void SetUp()
        {
            base.BaseSetUp();
            ClearAllCaches();
        }

        [TearDown]
        public override void TearDown()
        {
            ClearAllCaches();
            base.TearDown();
        }

        private static void ClearAllCaches()
        {
            WButtonGUI.ClearGroupDataForTesting();
            WButtonGUI.ClearConflictingDrawOrderWarningsForTesting();
            WButtonGUI.ClearConflictingGroupPriorityWarningsForTesting();
            WButtonGUI.ClearConflictingGroupPlacementWarningsForTesting();
            WButtonGUI.ClearConflictWarningContentCacheForTesting();
            WButtonGUI.ClearContextCache();
        }

        private static void DrawButtonsWithDefaults(
            Editor editor,
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates,
            Dictionary<WButtonGroupKey, bool> foldoutStates
        )
        {
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );
        }

        private T CreateAssetAndEditor<T>(out Editor editor)
            where T : ScriptableObject
        {
            T asset = Track(ScriptableObject.CreateInstance<T>());
            editor = Track(Editor.CreateEditor(asset));
            return asset;
        }

        private ScriptableObject CreateAssetAndEditor(Type targetType, out Editor editor)
        {
            ScriptableObject asset = Track(
                ScriptableObject.CreateInstance(targetType) as ScriptableObject
            );
            editor = Track(Editor.CreateEditor(asset));
            return asset;
        }

        private static IReadOnlyDictionary<
            string,
            WButtonGUI.GroupPlacementConflictInfo
        > GetPlacementWarnings()
        {
            return WButtonGUI.GetConflictingGroupPlacementWarnings();
        }

        private static IReadOnlyDictionary<
            string,
            WButtonGUI.GroupPriorityConflictInfo
        > GetPriorityWarnings()
        {
            return WButtonGUI.GetConflictingGroupPriorityWarnings();
        }

        private static IReadOnlyDictionary<
            string,
            WButtonGUI.DrawOrderConflictInfo
        > GetDrawOrderWarnings()
        {
            return WButtonGUI.GetConflictingDrawOrderWarnings();
        }

        [Test]
        [TestCaseSource(nameof(PlacementConflictDetectionCases))]
        public void PlacementConflictWarningHandlesAllScenarios(
            Type targetType,
            string groupName,
            bool shouldHaveConflict,
            int expectedConflictCount
        )
        {
            CreateAssetAndEditor(targetType, out Editor editor);
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            DrawButtonsWithDefaults(editor, paginationStates, foldoutStates);
            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> warnings =
                GetPlacementWarnings();

            Assert.AreEqual(
                shouldHaveConflict,
                warnings.ContainsKey(groupName),
                shouldHaveConflict
                    ? $"Expected conflict warning for group '{groupName}'. Available groups: [{string.Join(", ", warnings.Keys)}]"
                    : $"Expected no conflict warning for group '{groupName}'. Available groups: [{string.Join(", ", warnings.Keys)}]"
            );

            if (shouldHaveConflict && 0 < expectedConflictCount)
            {
                Assert.AreEqual(
                    expectedConflictCount,
                    warnings[groupName]._allGroupPlacements.Count,
                    $"Expected {expectedConflictCount} conflicting placement values, but got {warnings[groupName]._allGroupPlacements.Count}: [{string.Join(", ", warnings[groupName]._allGroupPlacements)}]"
                );
            }
        }

        private static IEnumerable<TestCaseData> PlacementConflictDetectionCases()
        {
            yield return new TestCaseData(
                typeof(WButtonGroupPlacementConflictTarget),
                "ConflictGroup",
                true,
                2
            ).SetName("Placement.ConflictingValues.GeneratesWarning");

            yield return new TestCaseData(
                typeof(WButtonGroupPlacementTopTarget),
                "TopGroup",
                false,
                0
            ).SetName("Placement.ConsistentTop.NoWarning");

            yield return new TestCaseData(
                typeof(WButtonMixedExplicitAndDefaultPlacementTarget),
                "Debug Tools",
                false,
                0
            ).SetName("Placement.MixedExplicitAndDefault.NoWarning");

            yield return new TestCaseData(
                typeof(WButtonAllDefaultPlacementTarget),
                "DefaultGroup",
                false,
                0
            ).SetName("Placement.AllDefaults.NoWarning");

            yield return new TestCaseData(
                typeof(WButtonAllSameExplicitPlacementTarget),
                "SameGroup",
                false,
                0
            ).SetName("Placement.AllSameExplicit.NoWarning");
        }

        [Test]
        [TestCaseSource(nameof(PlacementConflictContentCases))]
        public void PlacementConflictWarningContainsExpectedContent(
            WButtonGroupPlacement expectedCanonicalPlacement,
            WButtonGroupPlacement[] expectedConflictingPlacements
        )
        {
            CreateAssetAndEditor<WButtonGroupPlacementConflictTarget>(out Editor editor);
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            DrawButtonsWithDefaults(editor, paginationStates, foldoutStates);
            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> warnings =
                GetPlacementWarnings();
            WButtonGUI.GroupPlacementConflictInfo conflict = warnings["ConflictGroup"];

            Assert.AreEqual(
                expectedCanonicalPlacement,
                conflict._canonicalGroupPlacement,
                "Canonical placement should match first declared button"
            );

            for (int i = 0; i < expectedConflictingPlacements.Length; i++)
            {
                WButtonGroupPlacement expected = expectedConflictingPlacements[i];
                Assert.IsTrue(
                    conflict._allGroupPlacements.Contains(expected),
                    $"Should include {expected} placement"
                );
            }

            Assert.IsFalse(
                conflict._allGroupPlacements.Contains(WButtonGroupPlacement.UseGlobalSetting),
                "Should exclude default UseGlobalSetting value"
            );
        }

        private static IEnumerable<TestCaseData> PlacementConflictContentCases()
        {
            yield return new TestCaseData(
                WButtonGroupPlacement.Top,
                new[] { WButtonGroupPlacement.Top, WButtonGroupPlacement.Bottom }
            ).SetName("Content.ConflictGroup.ContainsTopAndBottom");
        }

        [UnityTest]
        [TestCaseSource(nameof(CollapsedConflictWarningRenderCases))]
        public IEnumerator ConflictWarningsRenderAndCacheInImGuiContext(
            Type targetType,
            string groupName,
            string warningType,
            UnityHelpersSettings.WButtonFoldoutBehavior foldoutBehavior,
            string expectedSummaryFragment,
            string expectedCanonicalFragment
        )
        {
            CreateAssetAndEditor(targetType, out Editor editor);
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            yield return TestIMGUIExecutor.Run(() =>
            {
                WButtonGUI.DrawButtons(
                    editor,
                    WButtonPlacement.Top,
                    paginationStates,
                    foldoutStates,
                    foldoutBehavior,
                    triggeredContexts: null,
                    globalPlacementIsTop: true
                );
            });

            bool hasCachedWarning = TryGetCachedWarningText(
                warningType,
                groupName,
                out string warningText
            );

            LogWarningDiagnostics(warningType, foldoutBehavior, groupName, hasCachedWarning);

            Assert.IsTrue(
                hasCachedWarning,
                $"Expected '{warningType}' warning text for group '{groupName}' to be cached after IMGUI draw with behavior '{foldoutBehavior}'."
            );
            StringAssert.Contains(expectedSummaryFragment, warningText);
            StringAssert.Contains(expectedCanonicalFragment, warningText);
        }

        private static IEnumerable<TestCaseData> CollapsedConflictWarningRenderCases()
        {
            UnityHelpersSettings.WButtonFoldoutBehavior[] behaviors =
            {
                UnityHelpersSettings.WButtonFoldoutBehavior.StartCollapsed,
                UnityHelpersSettings.WButtonFoldoutBehavior.StartExpanded,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
            };

            foreach (UnityHelpersSettings.WButtonFoldoutBehavior behavior in behaviors)
            {
                yield return new TestCaseData(
                    typeof(WButtonGroupPlacementConflictTarget),
                    "ConflictGroup",
                    "placement",
                    behavior,
                    "Conflicting groupPlacement values",
                    "Using Top from first declared button."
                )
                    .Returns(null)
                    .SetName($"Warning.Placement.{behavior}.RenderedAndCached");

                yield return new TestCaseData(
                    typeof(WButtonGroupPriorityConflictTarget),
                    "ConflictGroup",
                    "priority",
                    behavior,
                    "Conflicting groupPriority values",
                    "Using 0 from first declared button."
                )
                    .Returns(null)
                    .SetName($"Warning.Priority.{behavior}.RenderedAndCached");

                yield return new TestCaseData(
                    typeof(WButtonConflictingDrawOrderTarget),
                    "Setup",
                    "drawOrder",
                    behavior,
                    "Conflicting drawOrder values",
                    "from first declared button."
                )
                    .Returns(null)
                    .SetName($"Warning.DrawOrder.{behavior}.RenderedAndCached");
            }
        }

        [UnityTest]
        [TestCaseSource(nameof(NonConflictWarningRenderCases))]
        public IEnumerator ConflictWarningsAreNotCachedWhenNoConflictsExist(
            Type targetType,
            string groupName,
            string warningType
        )
        {
            CreateAssetAndEditor(targetType, out Editor editor);
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            yield return TestIMGUIExecutor.Run(() =>
            {
                WButtonGUI.DrawButtons(
                    editor,
                    WButtonPlacement.Top,
                    paginationStates,
                    foldoutStates,
                    UnityHelpersSettings.WButtonFoldoutBehavior.StartCollapsed,
                    triggeredContexts: null,
                    globalPlacementIsTop: true
                );
            });

            bool hasCachedWarning = TryGetCachedWarningText(
                warningType,
                groupName,
                out string warningText
            );

            LogWarningDiagnostics(
                warningType,
                UnityHelpersSettings.WButtonFoldoutBehavior.StartCollapsed,
                groupName,
                hasCachedWarning
            );

            Assert.IsFalse(
                hasCachedWarning,
                $"Did not expect '{warningType}' warning text for group '{groupName}'. Unexpected cached text: '{warningText}'."
            );
        }

        private static IEnumerable<TestCaseData> NonConflictWarningRenderCases()
        {
            yield return new TestCaseData(
                typeof(WButtonGroupPlacementTopTarget),
                "TopGroup",
                "placement"
            )
                .Returns(null)
                .SetName("Warning.Placement.NoConflict.NotCached");

            yield return new TestCaseData(
                typeof(WButtonAllSameExplicitPriorityTarget),
                "PriorityGroup",
                "priority"
            )
                .Returns(null)
                .SetName("Warning.Priority.NoConflict.NotCached");

            yield return new TestCaseData(
                typeof(WButtonGroupPlacementTopTarget),
                "TopGroup",
                "drawOrder"
            )
                .Returns(null)
                .SetName("Warning.DrawOrder.NoConflict.NotCached");
        }

        private static bool TryGetCachedWarningText(
            string warningType,
            string groupName,
            out string warningText
        )
        {
            switch (warningType)
            {
                case "placement":
                    return WButtonGUI.TryGetGroupPlacementWarningTextForTesting(
                        groupName,
                        out warningText
                    );
                case "priority":
                    return WButtonGUI.TryGetGroupPriorityWarningTextForTesting(
                        groupName,
                        out warningText
                    );
                case "drawOrder":
                    return WButtonGUI.TryGetDrawOrderWarningTextForTesting(
                        groupName,
                        out warningText
                    );
                default:
                    warningText = null;
                    return false;
            }
        }

        private static void LogWarningDiagnostics(
            string warningType,
            UnityHelpersSettings.WButtonFoldoutBehavior foldoutBehavior,
            string groupName,
            bool hasCachedWarning
        )
        {
            string availableGroups;
            switch (warningType)
            {
                case "placement":
                    availableGroups = string.Join(", ", GetPlacementWarnings().Keys);
                    break;
                case "priority":
                    availableGroups = string.Join(", ", GetPriorityWarnings().Keys);
                    break;
                case "drawOrder":
                    availableGroups = string.Join(", ", GetDrawOrderWarnings().Keys);
                    break;
                default:
                    availableGroups = string.Empty;
                    break;
            }

            TestContext.WriteLine(
                $"Warning diagnostics: type={warningType}, behavior={foldoutBehavior}, group={groupName}, hasCachedWarning={hasCachedWarning}"
            );
            TestContext.WriteLine(
                $"Available groups for type '{warningType}': [{availableGroups}]"
            );
        }

        [Test]
        [TestCaseSource(nameof(PriorityConflictDetectionCases))]
        public void PriorityConflictWarningHandlesAllScenarios(
            Type targetType,
            string groupName,
            bool shouldHaveConflict,
            int expectedConflictCount
        )
        {
            CreateAssetAndEditor(targetType, out Editor editor);
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            DrawButtonsWithDefaults(editor, paginationStates, foldoutStates);
            IReadOnlyDictionary<string, WButtonGUI.GroupPriorityConflictInfo> warnings =
                GetPriorityWarnings();

            Assert.AreEqual(
                shouldHaveConflict,
                warnings.ContainsKey(groupName),
                shouldHaveConflict
                    ? $"Expected conflict warning for group '{groupName}'. Available groups: [{string.Join(", ", warnings.Keys)}]"
                    : $"Expected no conflict warning for group '{groupName}'. Available groups: [{string.Join(", ", warnings.Keys)}]"
            );

            if (shouldHaveConflict && 0 < expectedConflictCount)
            {
                Assert.AreEqual(
                    expectedConflictCount,
                    warnings[groupName]._allGroupPriorities.Count,
                    $"Expected {expectedConflictCount} conflicting priority values, but got {warnings[groupName]._allGroupPriorities.Count}: [{string.Join(", ", warnings[groupName]._allGroupPriorities)}]"
                );
            }
        }

        private static IEnumerable<TestCaseData> PriorityConflictDetectionCases()
        {
            yield return new TestCaseData(
                typeof(WButtonGroupPriorityConflictTarget),
                "ConflictGroup",
                true,
                2
            ).SetName("Priority.ConflictingValues.GeneratesWarning");

            yield return new TestCaseData(
                typeof(WButtonAllSameExplicitPriorityTarget),
                "SameGroup",
                false,
                0
            ).SetName("Priority.AllSameExplicit.NoWarning");

            yield return new TestCaseData(
                typeof(WButtonMixedExplicitAndDefaultPriorityTarget),
                "Setup",
                false,
                0
            ).SetName("Priority.MixedExplicitAndDefault.NoWarning");

            yield return new TestCaseData(
                typeof(WButtonAllDefaultPriorityTarget),
                "DefaultGroup",
                false,
                0
            ).SetName("Priority.AllDefaults.NoWarning");
        }

        [Test]
        [TestCaseSource(nameof(PriorityConflictContentCases))]
        public void PriorityConflictWarningContainsExpectedContent(
            int expectedCanonicalPriority,
            int[] expectedConflictingPriorities
        )
        {
            CreateAssetAndEditor<WButtonGroupPriorityConflictTarget>(out Editor editor);
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            DrawButtonsWithDefaults(editor, paginationStates, foldoutStates);
            IReadOnlyDictionary<string, WButtonGUI.GroupPriorityConflictInfo> warnings =
                GetPriorityWarnings();
            WButtonGUI.GroupPriorityConflictInfo conflict = warnings["ConflictGroup"];

            Assert.AreEqual(
                expectedCanonicalPriority,
                conflict._canonicalGroupPriority,
                "Canonical priority should match first declared button"
            );

            for (int i = 0; i < expectedConflictingPriorities.Length; i++)
            {
                int expected = expectedConflictingPriorities[i];
                Assert.IsTrue(
                    conflict._allGroupPriorities.Contains(expected),
                    $"Should include priority {expected}"
                );
            }

            Assert.IsFalse(
                conflict._allGroupPriorities.Contains(WButtonAttribute.NoGroupPriority),
                "Should exclude default NoGroupPriority value"
            );
        }

        private static IEnumerable<TestCaseData> PriorityConflictContentCases()
        {
            yield return new TestCaseData(0, new[] { 0, 10 }).SetName(
                "Content.ConflictGroup.ContainsZeroAndTen"
            );
        }

        [Test]
        [TestCaseSource(nameof(DrawOrderConflictDetectionCases))]
        public void DrawOrderConflictWarningHandlesAllScenarios(
            Type targetType,
            string groupName,
            bool shouldHaveConflict,
            int minExpectedConflictCount
        )
        {
            CreateAssetAndEditor(targetType, out Editor editor);
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            DrawButtonsWithDefaults(editor, paginationStates, foldoutStates);
            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> warnings =
                GetDrawOrderWarnings();

            Assert.AreEqual(
                shouldHaveConflict,
                warnings.ContainsKey(groupName),
                shouldHaveConflict
                    ? $"Expected conflict warning for group '{groupName}'. Available groups: [{string.Join(", ", warnings.Keys)}]"
                    : $"Expected no conflict warning for group '{groupName}'. Available groups: [{string.Join(", ", warnings.Keys)}]"
            );

            if (shouldHaveConflict && 0 < minExpectedConflictCount)
            {
                Assert.IsTrue(
                    minExpectedConflictCount <= warnings[groupName]._allDrawOrders.Count,
                    $"Expected at least {minExpectedConflictCount} conflicting draw order values, but got {warnings[groupName]._allDrawOrders.Count}: [{string.Join(", ", warnings[groupName]._allDrawOrders)}]"
                );
            }
        }

        private static IEnumerable<TestCaseData> DrawOrderConflictDetectionCases()
        {
            yield return new TestCaseData(
                typeof(WButtonConflictingDrawOrderTarget),
                "Setup",
                true,
                2
            ).SetName("DrawOrder.ConflictingValues.GeneratesWarning");

            yield return new TestCaseData(
                typeof(WButtonGroupPlacementTopTarget),
                "TopGroup",
                false,
                0
            ).SetName("DrawOrder.ConsistentValues.NoWarning");

            yield return new TestCaseData(
                typeof(WButtonThreeWayConflictTarget),
                "Actions",
                true,
                3
            ).SetName("DrawOrder.ThreeWayConflict.GeneratesWarning");
        }

        [Test]
        public void DrawOrderConflictWarningIndicatesCanonicalValueIsIncluded()
        {
            CreateAssetAndEditor<WButtonConflictingDrawOrderTarget>(out Editor editor);
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            DrawButtonsWithDefaults(editor, paginationStates, foldoutStates);
            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> warnings =
                GetDrawOrderWarnings();

            Assert.IsTrue(
                warnings.ContainsKey("Setup"),
                $"Expected warnings to contain 'Setup' group. Available groups: [{string.Join(", ", warnings.Keys)}]"
            );

            WButtonGUI.DrawOrderConflictInfo conflict = warnings["Setup"];

            Assert.IsTrue(
                conflict._allDrawOrders.Contains(conflict._canonicalDrawOrder),
                $"Canonical draw order ({conflict._canonicalDrawOrder}) should be one of the conflicting values: [{string.Join(", ", conflict._allDrawOrders)}]"
            );
        }

        [Test]
        [TestCaseSource(nameof(CachingBehaviorCases))]
        public void ConflictWarningCachingBehavesCorrectly(bool shouldClearCache)
        {
            CreateAssetAndEditor<WButtonGroupPlacementConflictTarget>(out Editor editor);
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            DrawButtonsWithDefaults(editor, paginationStates, foldoutStates);
            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> firstWarnings =
                GetPlacementWarnings();

            if (shouldClearCache)
            {
                WButtonGUI.ClearConflictingGroupPlacementWarningsForTesting();
                WButtonGUI.ClearConflictWarningContentCacheForTesting();

                IReadOnlyDictionary<
                    string,
                    WButtonGUI.GroupPlacementConflictInfo
                > warningsAfterClear = GetPlacementWarnings();
                Assert.AreEqual(
                    0,
                    warningsAfterClear.Count,
                    $"Warnings should be cleared immediately after cache clear. Found groups: [{string.Join(", ", warningsAfterClear.Keys)}]"
                );

                DrawButtonsWithDefaults(editor, paginationStates, foldoutStates);
                IReadOnlyDictionary<
                    string,
                    WButtonGUI.GroupPlacementConflictInfo
                > warningsAfterRedraw = GetPlacementWarnings();
                Assert.IsTrue(
                    warningsAfterRedraw.ContainsKey("ConflictGroup"),
                    $"Warnings should be regenerated after redrawing. Available groups: [{string.Join(", ", warningsAfterRedraw.Keys)}]"
                );
            }
            else
            {
                DrawButtonsWithDefaults(editor, paginationStates, foldoutStates);
                IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> secondWarnings =
                    GetPlacementWarnings();

                Assert.IsTrue(
                    firstWarnings.ContainsKey("ConflictGroup"),
                    $"First draw should generate warnings. Available groups: [{string.Join(", ", firstWarnings.Keys)}]"
                );
                Assert.IsTrue(
                    secondWarnings.ContainsKey("ConflictGroup"),
                    $"Second draw should use cached warnings. Available groups: [{string.Join(", ", secondWarnings.Keys)}]"
                );
            }
        }

        private static IEnumerable<TestCaseData> CachingBehaviorCases()
        {
            yield return new TestCaseData(false).SetName("Caching.NoClear.WarningsPersist");
            yield return new TestCaseData(true).SetName("Caching.WithClear.WarningsCleared");
        }

        [Test]
        public void MultipleConflictTypesGenerateIndependentWarnings()
        {
            CreateAssetAndEditor<WButtonThreeWayConflictTarget>(out Editor editor);
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            DrawButtonsWithDefaults(editor, paginationStates, foldoutStates);

            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> drawOrderWarnings =
                GetDrawOrderWarnings();
            IReadOnlyDictionary<string, WButtonGUI.GroupPriorityConflictInfo> priorityWarnings =
                GetPriorityWarnings();
            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> placementWarnings =
                GetPlacementWarnings();

            bool hasDrawOrderConflict = drawOrderWarnings.ContainsKey("Actions");
            bool hasPriorityConflict = priorityWarnings.ContainsKey("Actions");
            bool hasPlacementConflict = placementWarnings.ContainsKey("Actions");

            int conflictCount =
                (hasDrawOrderConflict ? 1 : 0)
                + (hasPriorityConflict ? 1 : 0)
                + (hasPlacementConflict ? 1 : 0);

            Assert.IsTrue(
                1 <= conflictCount,
                "Should have at least one type of conflict for ThreeWayConflictTarget"
            );
        }

        [Test]
        [TestCaseSource(nameof(EdgeCaseTargetsCases))]
        public void EdgeCaseTargetsGenerateNoConflictWarnings(Type targetType)
        {
            CreateAssetAndEditor(targetType, out Editor editor);
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            DrawButtonsWithDefaults(editor, paginationStates, foldoutStates);

            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> drawOrderWarnings =
                GetDrawOrderWarnings();
            IReadOnlyDictionary<string, WButtonGUI.GroupPriorityConflictInfo> priorityWarnings =
                GetPriorityWarnings();
            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> placementWarnings =
                GetPlacementWarnings();

            Assert.AreEqual(0, drawOrderWarnings.Count, "No draw order warnings expected");
            Assert.AreEqual(0, priorityWarnings.Count, "No priority warnings expected");
            Assert.AreEqual(0, placementWarnings.Count, "No placement warnings expected");
        }

        private static IEnumerable<TestCaseData> EdgeCaseTargetsCases()
        {
            yield return new TestCaseData(typeof(WButtonSingleButtonTarget)).SetName(
                "EdgeCase.SingleButton.NoWarnings"
            );
            yield return new TestCaseData(typeof(WButtonUngroupedPlacementTarget)).SetName(
                "EdgeCase.UngroupedButtons.NoWarnings"
            );
            yield return new TestCaseData(
                typeof(WButtonMixedExplicitAndDefaultPlacementTarget)
            ).SetName("EdgeCase.MixedExplicitDefault.NoWarnings");
        }

        [Test]
        [TestCaseSource(nameof(NullAndDestroyedEditorCases))]
        public void NullAndDestroyedEditorHandledGracefully(bool destroyEditor)
        {
            CreateAssetAndEditor<WButtonGroupPlacementConflictTarget>(out Editor editor);

            if (destroyEditor)
            {
                UnityEngine.Object.DestroyImmediate(editor); // UNH-SUPPRESS: Test verifies behavior with destroyed editor
                _trackedObjects.Remove(editor);
            }

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            if (destroyEditor)
            {
                Assert.DoesNotThrow(() =>
                {
                    DrawButtonsWithDefaults(null, paginationStates, foldoutStates);
                });
            }
            else
            {
                Assert.DoesNotThrow(() =>
                {
                    DrawButtonsWithDefaults(editor, paginationStates, foldoutStates);
                });
            }
        }

        private static IEnumerable<TestCaseData> NullAndDestroyedEditorCases()
        {
            yield return new TestCaseData(false).SetName("Negative.ValidEditor.NoThrow");
            yield return new TestCaseData(true).SetName("Negative.DestroyedEditor.NoThrow");
        }

        [UnityTest]
        public IEnumerator InvalidEnumValuesHandledGracefully()
        {
            CreateAssetAndEditor<WButtonInvalidPlacementConflictTarget>(out Editor editor);
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            WButtonGroupPlacement invalidPlacement = (WButtonGroupPlacement)999;
            bool drawCompleted = false;
            Exception drawException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    drawCompleted = WButtonGUI.DrawButtons(
                        editor,
                        WButtonPlacement.Bottom,
                        paginationStates,
                        foldoutStates,
                        UnityHelpersSettings.WButtonFoldoutBehavior.StartCollapsed,
                        triggeredContexts: null,
                        globalPlacementIsTop: true
                    );
                }
                catch (Exception ex)
                {
                    drawException = ex;
                }
            });

            Assert.IsTrue(
                drawException == null,
                $"Expected DrawButtons to handle metadata-derived invalid groupPlacement values without throwing. Exception: {drawException}"
            );
            Assert.IsTrue(
                drawCompleted,
                "Expected DrawButtons to render at least one group when the canonical group placement is an invalid enum value."
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            int invalidPlacementGroupCount = 0;
            foreach (KeyValuePair<WButtonGroupKey, int> entry in groupCounts)
            {
                if (
                    string.Equals(
                        entry.Key._groupName,
                        "InvalidPlacementGroup",
                        StringComparison.Ordinal
                    )
                )
                {
                    invalidPlacementGroupCount++;
                    Assert.AreEqual(
                        invalidPlacement,
                        entry.Key._groupPlacement,
                        "Expected canonical group key to preserve the first-declared invalid groupPlacement value."
                    );
                }
            }
            Assert.AreEqual(
                1,
                invalidPlacementGroupCount,
                $"Expected exactly one InvalidPlacementGroup key. Available group keys: [{string.Join(", ", groupCounts.Keys)}]"
            );

            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> placementWarnings =
                GetPlacementWarnings();
            Assert.IsTrue(
                placementWarnings.ContainsKey("InvalidPlacementGroup"),
                $"Expected placement warnings to include 'InvalidPlacementGroup' after drawing. Available groups: [{string.Join(", ", placementWarnings.Keys)}]"
            );

            WButtonGUI.GroupPlacementConflictInfo conflict = placementWarnings[
                "InvalidPlacementGroup"
            ];
            Assert.AreEqual(
                invalidPlacement,
                conflict._canonicalGroupPlacement,
                "Expected placement warning canonical value to preserve invalid first-declared placement."
            );
            Assert.IsTrue(
                conflict._allGroupPlacements.Contains(invalidPlacement),
                "Expected conflict values to include the invalid enum value."
            );
            Assert.IsTrue(
                conflict._allGroupPlacements.Contains(WButtonGroupPlacement.Top),
                "Expected conflict values to include the explicit Top placement."
            );

            bool hasCachedWarning = WButtonGUI.TryGetGroupPlacementWarningTextForTesting(
                "InvalidPlacementGroup",
                out string warningText
            );
            LogWarningDiagnostics(
                "placement",
                UnityHelpersSettings.WButtonFoldoutBehavior.StartCollapsed,
                "InvalidPlacementGroup",
                hasCachedWarning
            );
            Assert.IsTrue(
                hasCachedWarning,
                "Expected placement warning text cache to be populated after IMGUI draw with metadata-derived invalid enum values."
            );
            StringAssert.Contains("Conflicting groupPlacement values", warningText);
            StringAssert.Contains("Using 999 from first declared button.", warningText);
            StringAssert.Contains("Top", warningText);
            StringAssert.Contains("999", warningText);
        }

        [Test]
        [TestCaseSource(nameof(InvalidEnumValueCases))]
        public void InvalidEnumBackedKeysRemainStableForCompareAndHashCode(
            WButtonGroupPlacement invalidPlacement,
            int invalidPriority
        )
        {
            WButtonGroupKey invalidKey = new(
                invalidPriority,
                0,
                "InvalidGroup",
                0,
                invalidPlacement
            );
            WButtonGroupKey canonicalKey = new(
                WButtonAttribute.NoGroupPriority,
                0,
                "InvalidGroup",
                1,
                WButtonGroupPlacement.UseGlobalSetting
            );
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            Assert.DoesNotThrow(() =>
            {
                paginationStates[invalidKey] = new WButtonPaginationState
                {
                    _pageIndex = int.MaxValue,
                };
                foldoutStates[invalidKey] = true;
                _ = invalidKey.CompareTo(canonicalKey);
                _ = invalidKey.GetHashCode();
            });

            Assert.IsTrue(
                foldoutStates.ContainsKey(invalidKey),
                "Expected foldout-state dictionary lookups using invalid enum-backed keys to remain stable."
            );
            Assert.IsTrue(
                paginationStates.ContainsKey(invalidKey),
                "Expected pagination-state dictionary lookups using invalid enum-backed keys to remain stable."
            );
        }

        private static IEnumerable<TestCaseData> InvalidEnumValueCases()
        {
            yield return new TestCaseData((WButtonGroupPlacement)999, int.MinValue).SetName(
                "Impossible.InvalidEnumValues.KeyComparison.Stable"
            );
            yield return new TestCaseData((WButtonGroupPlacement)(-1), int.MaxValue).SetName(
                "Impossible.NegativeEnumValue.KeyComparison.Stable"
            );
        }

        [UnityTest]
        [TestCaseSource(nameof(ExtremeScaleTestCases))]
        public IEnumerator ExtremeScaleHandledCorrectly(int drawIterations)
        {
            TestContext.WriteLine($"ExtremeScaleHandledCorrectly drawIterations={drawIterations}");

            CreateAssetAndEditor<WButtonThreeWayConflictTarget>(out Editor editor);
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            yield return TestIMGUIExecutor.Run(() =>
            {
                for (int i = 0; i < drawIterations; i++)
                {
                    DrawButtonsWithDefaults(editor, paginationStates, foldoutStates);
                }
            });

            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> drawOrderWarnings =
                GetDrawOrderWarnings();

            Assert.IsTrue(
                drawOrderWarnings.ContainsKey("Actions"),
                $"Expected draw-order warning for 'Actions' group after {drawIterations} draw iterations. Available groups: [{string.Join(", ", drawOrderWarnings.Keys)}]"
            );
        }

        private static IEnumerable<TestCaseData> ExtremeScaleTestCases()
        {
            yield return new TestCaseData(250)
                .Returns(null)
                .SetName("Extreme.DrawLoop.TwoHundredFiftyIterations.Stable");
            yield return new TestCaseData(1000)
                .Returns(null)
                .SetName("Extreme.DrawLoop.ThousandIterations.Stable");
        }
    }
}
#endif
