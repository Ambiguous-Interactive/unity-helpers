// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers.Utils
{
#if UNITY_EDITOR

    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class InLineEditorSharedCacheTests : CommonTestBase
    {
        private static IEnumerable<TestCaseData> FoldoutStateCases()
        {
            yield return new TestCaseData(1, 1).SetName("FoldoutStates.Single.AcceptsEntry");

            yield return new TestCaseData(10, 10).SetName("FoldoutStates.Small.AcceptsAll");

            yield return new TestCaseData(100, 100).SetName("FoldoutStates.Medium.AcceptsAll");

            yield return new TestCaseData(500, 500).SetName("FoldoutStates.Large.AcceptsAll");
        }

        private static IEnumerable<TestCaseData> ScrollPositionCases()
        {
            yield return new TestCaseData(1).SetName("ScrollPositions.Single.AcceptsEntry");

            yield return new TestCaseData(10).SetName("ScrollPositions.Small.AcceptsAll");

            yield return new TestCaseData(100).SetName("ScrollPositions.Medium.AcceptsAll");

            yield return new TestCaseData(500).SetName("ScrollPositions.Large.AcceptsAll");
        }

        private static IEnumerable<TestCaseData> IntToStringCases()
        {
            yield return new TestCaseData(0, "0").SetName("IntToString.Zero.ReturnsZero");

            yield return new TestCaseData(1, "1").SetName("IntToString.One.ReturnsOne");

            yield return new TestCaseData(-1, "-1").SetName(
                "IntToString.NegativeOne.ReturnsNegative"
            );

            yield return new TestCaseData(12345, "12345").SetName(
                "IntToString.PositiveLarge.ReturnsCorrect"
            );

            yield return new TestCaseData(-99999, "-99999").SetName(
                "IntToString.NegativeLarge.ReturnsCorrect"
            );

            yield return new TestCaseData(int.MaxValue, "2147483647").SetName(
                "IntToString.MaxValue.ReturnsCorrect"
            );

            yield return new TestCaseData(int.MinValue, "-2147483648").SetName(
                "IntToString.MinValue.ReturnsCorrect"
            );
        }

        private static IEnumerable<TestCaseData> SpecialKeyCharacterCases()
        {
            yield return new TestCaseData("key with spaces", true).SetName(
                "SpecialKeys.Spaces.Stored"
            );

            yield return new TestCaseData("key::with::colons", false).SetName(
                "SpecialKeys.Colons.Stored"
            );

            yield return new TestCaseData("key\twith\ttabs", true).SetName(
                "SpecialKeys.Tabs.Stored"
            );

            yield return new TestCaseData("key.with.dots", false).SetName(
                "SpecialKeys.Dots.Stored"
            );

            yield return new TestCaseData("key/with/slashes", true).SetName(
                "SpecialKeys.Slashes.Stored"
            );

            yield return new TestCaseData("key[with]brackets", false).SetName(
                "SpecialKeys.Brackets.Stored"
            );

            yield return new TestCaseData("123::propertyPath.nested[0]", true).SetName(
                "SpecialKeys.RealisticKey.Stored"
            );
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            InLineEditorShared.ClearCachedStateForTesting();
        }

        [TearDown]
        public override void TearDown()
        {
            InLineEditorShared.ClearCachedStateForTesting();
            base.TearDown();
        }

        [Test]
        [TestCaseSource(nameof(FoldoutStateCases))]
        public void FoldoutStatesUnderLimitAcceptsAll(int numberOfEntries, int expectedCount)
        {
            for (int i = 0; i < numberOfEntries; i++)
            {
                InLineEditorShared.SetFoldoutState($"foldout{i}", i % 2 == 0);
            }

            Assert.That(
                InLineEditorShared.GetFoldoutStateCacheCountForTesting(),
                Is.EqualTo(expectedCount)
            );

            for (int i = 0; i < numberOfEntries; i++)
            {
                bool expected = i % 2 == 0;
                Assert.That(
                    InLineEditorShared.GetFoldoutStateForTesting($"foldout{i}"),
                    Is.EqualTo(expected),
                    $"Foldout state for foldout{i} should be {expected}"
                );
            }
        }

        [Test]
        public void FoldoutStatesSetAndGetReturnCorrectValue()
        {
            InLineEditorShared.SetFoldoutState("expandedKey", true);
            InLineEditorShared.SetFoldoutState("collapsedKey", false);

            Assert.That(InLineEditorShared.GetFoldoutStateForTesting("expandedKey"), Is.True);
            Assert.That(InLineEditorShared.GetFoldoutStateForTesting("collapsedKey"), Is.False);
        }

        [Test]
        public void FoldoutStatesUpdateExistingKeyCorrectly()
        {
            InLineEditorShared.SetFoldoutState("toggleKey", true);
            Assert.That(InLineEditorShared.GetFoldoutStateForTesting("toggleKey"), Is.True);

            InLineEditorShared.SetFoldoutState("toggleKey", false);
            Assert.That(InLineEditorShared.GetFoldoutStateForTesting("toggleKey"), Is.False);

            InLineEditorShared.SetFoldoutState("toggleKey", true);
            Assert.That(InLineEditorShared.GetFoldoutStateForTesting("toggleKey"), Is.True);
        }

        [Test]
        public void FoldoutStatesEmptyKeyIsIgnored()
        {
            int countBefore = InLineEditorShared.GetFoldoutStateCacheCountForTesting();

            InLineEditorShared.SetFoldoutState(string.Empty, true);
            InLineEditorShared.SetFoldoutState(null, true);

            int countAfter = InLineEditorShared.GetFoldoutStateCacheCountForTesting();
            Assert.That(countAfter, Is.EqualTo(countBefore));
        }

        [Test]
        public void FoldoutStatesGetForEmptyKeyReturnsFalse()
        {
            Assert.That(InLineEditorShared.GetFoldoutStateForTesting(string.Empty), Is.False);
            Assert.That(InLineEditorShared.GetFoldoutStateForTesting(null), Is.False);
        }

        [Test]
        [TestCaseSource(nameof(ScrollPositionCases))]
        public void ScrollPositionsUnderLimitAcceptsAll(int numberOfEntries)
        {
            for (int i = 0; i < numberOfEntries; i++)
            {
                InLineEditorShared.SetScrollPosition($"scroll{i}", new Vector2(i, i * 2));
            }

            Assert.That(
                InLineEditorShared.GetScrollPositionCacheCountForTesting(),
                Is.EqualTo(numberOfEntries)
            );

            for (int i = 0; i < numberOfEntries; i++)
            {
                Vector2 position = InLineEditorShared.GetScrollPosition($"scroll{i}");
                Assert.That(position.x, Is.EqualTo(i).Within(0.001f));
                Assert.That(position.y, Is.EqualTo(i * 2).Within(0.001f));
            }
        }

        [Test]
        public void ScrollPositionsSetAndGetReturnCorrectValue()
        {
            Vector2 expectedPosition = new(100f, 250f);
            InLineEditorShared.SetScrollPosition("testScroll", expectedPosition);

            Vector2 actual = InLineEditorShared.GetScrollPosition("testScroll");

            Assert.That(actual.x, Is.EqualTo(expectedPosition.x).Within(0.001f));
            Assert.That(actual.y, Is.EqualTo(expectedPosition.y).Within(0.001f));
        }

        [Test]
        public void ScrollPositionsUpdateExistingKeyCorrectly()
        {
            InLineEditorShared.SetScrollPosition("updateKey", new Vector2(10, 20));
            Assert.That(InLineEditorShared.GetScrollPosition("updateKey").x, Is.EqualTo(10f));

            InLineEditorShared.SetScrollPosition("updateKey", new Vector2(100, 200));
            Assert.That(InLineEditorShared.GetScrollPosition("updateKey").x, Is.EqualTo(100f));
        }

        [Test]
        public void ScrollPositionsEmptyKeyIsIgnored()
        {
            int countBefore = InLineEditorShared.GetScrollPositionCacheCountForTesting();

            InLineEditorShared.SetScrollPosition(string.Empty, new Vector2(1, 2));
            InLineEditorShared.SetScrollPosition(null, new Vector2(3, 4));

            int countAfter = InLineEditorShared.GetScrollPositionCacheCountForTesting();
            Assert.That(countAfter, Is.EqualTo(countBefore));
        }

        [Test]
        public void ScrollPositionsGetForEmptyKeyReturnsZero()
        {
            Assert.That(
                InLineEditorShared.GetScrollPosition(string.Empty),
                Is.EqualTo(Vector2.zero)
            );
            Assert.That(InLineEditorShared.GetScrollPosition(null), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ScrollPositionsGetForNonExistentKeyReturnsZero()
        {
            Vector2 position = InLineEditorShared.GetScrollPosition("nonExistentKey");
            Assert.That(position, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void EditorCacheGetOrCreateEditorReturnsValidEditor()
        {
            ScriptableObject target = CreateScriptableObject<EditorCacheTestTarget>();

            Editor editor = InLineEditorShared.GetOrCreateEditor(target);
            Track(editor);

            Assert.That(editor != null, Is.True, "Editor should be created");
            Assert.That(InLineEditorShared.GetEditorCacheCountForTesting(), Is.EqualTo(1));
        }

        [Test]
        public void EditorCacheGetOrCreateEditorReturnsCachedInstance()
        {
            ScriptableObject target = CreateScriptableObject<EditorCacheTestTarget>();

            Editor firstEditor = InLineEditorShared.GetOrCreateEditor(target);
            Track(firstEditor);
            Editor secondEditor = InLineEditorShared.GetOrCreateEditor(target);

            Assert.That(ReferenceEquals(firstEditor, secondEditor), Is.True);
            Assert.That(InLineEditorShared.GetEditorCacheCountForTesting(), Is.EqualTo(1));
        }

        [Test]
        public void EditorCacheGetOrCreateEditorWithNullReturnsNull()
        {
            Editor editor = InLineEditorShared.GetOrCreateEditor(null);

            Assert.That(editor == null, Is.True);
            Assert.That(InLineEditorShared.GetEditorCacheCountForTesting(), Is.EqualTo(0));
        }

        [Test]
        public void EditorCacheMultipleTargetsCreatesSeparateEditors()
        {
            ScriptableObject target1 = CreateScriptableObject<EditorCacheTestTarget>();
            ScriptableObject target2 = CreateScriptableObject<EditorCacheTestTarget>();

            Editor editor1 = InLineEditorShared.GetOrCreateEditor(target1);
            Track(editor1);
            Editor editor2 = InLineEditorShared.GetOrCreateEditor(target2);
            Track(editor2);

            Assert.That(ReferenceEquals(editor1, editor2), Is.False);
            Assert.That(InLineEditorShared.GetEditorCacheCountForTesting(), Is.EqualTo(2));
        }

        [Test]
        public void IntToStringGetCachedIntStringReturnsSameReference()
        {
            string first = InLineEditorShared.GetCachedIntString(42);
            string second = InLineEditorShared.GetCachedIntString(42);

            Assert.That(ReferenceEquals(first, second), Is.True);
            Assert.That(first, Is.EqualTo("42"));
        }

        [Test]
        [TestCaseSource(nameof(IntToStringCases))]
        public void IntToStringReturnsCorrectValue(int value, string expected)
        {
            string result = InLineEditorShared.GetCachedIntString(value);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void ClearCacheClearsAllCaches()
        {
            ScriptableObject target = CreateScriptableObject<EditorCacheTestTarget>();
            Editor editor = InLineEditorShared.GetOrCreateEditor(target);
            Track(editor);

            InLineEditorShared.SetFoldoutState("testFoldout", true);
            InLineEditorShared.SetScrollPosition("testScroll", new Vector2(10, 20));

            Assert.That(
                InLineEditorShared.GetFoldoutStateCacheCountForTesting(),
                Is.GreaterThan(0)
            );
            Assert.That(
                InLineEditorShared.GetScrollPositionCacheCountForTesting(),
                Is.GreaterThan(0)
            );

            _trackedObjects.Remove(editor);
            InLineEditorShared.ClearCachedStateForTesting();

            Assert.That(InLineEditorShared.GetFoldoutStateCacheCountForTesting(), Is.EqualTo(0));
            Assert.That(InLineEditorShared.GetScrollPositionCacheCountForTesting(), Is.EqualTo(0));
            Assert.That(InLineEditorShared.GetEditorCacheCountForTesting(), Is.EqualTo(0));
        }

        [Test]
        public void BuildFoldoutKeyCreatesConsistentKey()
        {
            string key1 = InLineEditorShared.BuildFoldoutKey(123, "myProperty");
            string key2 = InLineEditorShared.BuildFoldoutKey(123, "myProperty");

            Assert.That(key1, Is.EqualTo(key2));
            Assert.That(key1, Does.Contain("123"));
            Assert.That(key1, Does.Contain("myProperty"));
            Assert.That(key1, Does.Contain(InLineEditorShared.FoldoutKeySeparator));
        }

        [Test]
        public void BuildFoldoutKeyDifferentInstanceIdCreatesDifferentKey()
        {
            string key1 = InLineEditorShared.BuildFoldoutKey(100, "property");
            string key2 = InLineEditorShared.BuildFoldoutKey(200, "property");

            Assert.That(key1, Is.Not.EqualTo(key2));
        }

        [Test]
        public void BuildFoldoutKeyDifferentPropertyPathCreatesDifferentKey()
        {
            string key1 = InLineEditorShared.BuildFoldoutKey(100, "property1");
            string key2 = InLineEditorShared.BuildFoldoutKey(100, "property2");

            Assert.That(key1, Is.Not.EqualTo(key2));
        }

        [Test]
        public void BuildScrollKeyCreatesConsistentKey()
        {
            string foldoutKey = InLineEditorShared.BuildFoldoutKey(123, "myProperty");
            string scrollKey1 = InLineEditorShared.BuildScrollKey(foldoutKey);
            string scrollKey2 = InLineEditorShared.BuildScrollKey(foldoutKey);

            Assert.That(scrollKey1, Is.EqualTo(scrollKey2));
            Assert.That(scrollKey1, Does.Contain(InLineEditorShared.ScrollKeyPrefix));
        }

        [Test]
        public void BuildScrollKeyFromInstanceIdCreatesConsistentKey()
        {
            string key1 = InLineEditorShared.BuildScrollKey(456, "scrollProp");
            string key2 = InLineEditorShared.BuildScrollKey(456, "scrollProp");

            Assert.That(key1, Is.EqualTo(key2));
            Assert.That(key1, Does.Contain(InLineEditorShared.ScrollKeyPrefix));
            Assert.That(key1, Does.Contain("456"));
            Assert.That(key1, Does.Contain("scrollProp"));
        }

        [Test]
        [TestCaseSource(nameof(SpecialKeyCharacterCases))]
        public void FoldoutStatesHandleSpecialCharacterKeys(string key, bool value)
        {
            InLineEditorShared.SetFoldoutState(key, value);

            bool result = InLineEditorShared.GetFoldoutStateForTesting(key);
            Assert.That(result, Is.EqualTo(value));
        }

        [Test]
        public void ConstantsHaveExpectedValues()
        {
            Assert.That(InLineEditorShared.HeaderHeight, Is.EqualTo(20f));
            Assert.That(InLineEditorShared.Spacing, Is.EqualTo(2f));
            Assert.That(InLineEditorShared.ContentPadding, Is.EqualTo(2f));
            Assert.That(InLineEditorShared.ScriptPropertyPath, Is.EqualTo("m_Script"));
            Assert.That(InLineEditorShared.FoldoutKeySeparator, Is.EqualTo("::"));
            Assert.That(InLineEditorShared.ScrollKeyPrefix, Is.EqualTo("scroll"));
        }

        [Test]
        public void PrepareHeaderContentWithNullValueReturnsLabelOrNone()
        {
            GUIContent label = new("TestLabel");
            GUIContent result = InLineEditorShared.PrepareHeaderContent(null, label);

            Assert.That(result.text, Is.EqualTo("TestLabel"));
        }

        [Test]
        public void PrepareHeaderContentWithNullLabelAndNullValueReturnsNone()
        {
            GUIContent result = InLineEditorShared.PrepareHeaderContent(null, null);

            Assert.That(result, Is.EqualTo(GUIContent.none));
        }

        [Test]
        public void PrepareHeaderContentWithValidObjectReturnsContent()
        {
            ScriptableObject target = CreateScriptableObject<EditorCacheTestTarget>();
            target.name = "TestTargetName";

            GUIContent result = InLineEditorShared.PrepareHeaderContent(target, null);

            Assert.That(result.text, Does.Contain("TestTargetName"));
        }

        [Test]
        public void PrepareHeaderContentWithLabelCombinesContent()
        {
            ScriptableObject target = CreateScriptableObject<EditorCacheTestTarget>();
            target.name = "TargetName";
            GUIContent label = new("PropertyLabel");

            GUIContent result = InLineEditorShared.PrepareHeaderContent(target, label);

            Assert.That(result.text, Does.Contain("PropertyLabel"));
            Assert.That(result.text, Does.Contain("TargetName"));
        }

        [Test]
        public void ShouldShowPingButtonWithNullReturnsFalse()
        {
            bool result = InLineEditorShared.ShouldShowPingButton(null);

            Assert.That(result, Is.False);
        }

        [Test]
        public void GetPingButtonWidthReturnsPositiveValue()
        {
            float width = InLineEditorShared.GetPingButtonWidth();

            Assert.That(width, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void ReusableHeaderContentIsNotNull()
        {
            Assert.That(InLineEditorShared.ReusableHeaderContent != null, Is.True);
        }

        [Test]
        public void PingButtonContentIsNotNull()
        {
            Assert.That(InLineEditorShared.PingButtonContent != null, Is.True);
            Assert.That(InLineEditorShared.PingButtonContent.text, Is.EqualTo("Ping"));
        }
    }

#endif
}
