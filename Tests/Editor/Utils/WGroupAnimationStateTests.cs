// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils
{
    using System.Collections.Generic;
    using System.Globalization;
    using NUnit.Framework;
    using UnityEditor.AnimatedValues;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils.WGroup;

    /// <summary>
    /// Tests for <see cref="WGroupAnimationState"/> to validate animation state management,
    /// caching behavior, and integration with settings.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WGroupAnimationStateTests
    {
        private bool _originalTweenEnabled;
        private float _originalTweenSpeed;

        [SetUp]
        public void SetUp()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            _originalTweenEnabled = settings.WGroupFoldoutTweenEnabled;
            _originalTweenSpeed = settings.WGroupFoldoutSpeed;

            WGroupAnimationState.ClearCache();
        }

        [TearDown]
        public void TearDown()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = _originalTweenEnabled;
            settings.WGroupFoldoutSpeed = _originalTweenSpeed;

            WGroupAnimationState.ClearCache();
        }

        /// <summary>
        /// The cache is keyed on a target instance id, so an editor session that inspects many
        /// objects adds an entry per object per group and nothing removes them.
        /// </summary>
        /// <remarks>
        /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/701">#701</see>.
        /// Each value carries a repaint listener the clear path deliberately unsubscribes, so
        /// eviction has to unsubscribe too -- a dropped animation that kept its listener would
        /// repaint every view for the life of the editor, which is exactly the cost the bound
        /// exists to avoid.
        /// </remarks>
        [Test]
        public void TheAnimationCacheStopsAtItsBound()
        {
            int bound = WGroupAnimationState.MaxCachedAnimations;
            Assert.Greater(bound, 0, "the bound must be positive for this to measure anything");

            for (int index = 0; index < bound * 2; ++index)
            {
                WGroupDefinition definition = CreateTestDefinition(
                    "Group" + index.ToString(CultureInfo.InvariantCulture),
                    "property" + index.ToString(CultureInfo.InvariantCulture)
                );
                _ = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            }

            Assert.AreEqual(bound, WGroupAnimationState.CachedAnimationCount);
        }

        [Test]
        public void GetOrCreateAnimReturnsConsistentInstanceForSameDefinition()
        {
            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");

            AnimBool first = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            AnimBool second = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);

            Assert.AreSame(
                first,
                second,
                "GetOrCreateAnim should return the same AnimBool instance for the same definition."
            );
        }

        [Test]
        public void GetOrCreateAnimReturnsDifferentInstancesForDifferentDefinitions()
        {
            WGroupDefinition definition1 = CreateTestDefinition("GroupOne", "property1");
            WGroupDefinition definition2 = CreateTestDefinition("GroupTwo", "property2");

            AnimBool anim1 = WGroupAnimationState.GetOrCreateAnim(definition1, expanded: true);
            AnimBool anim2 = WGroupAnimationState.GetOrCreateAnim(definition2, expanded: true);

            Assert.AreNotSame(
                anim1,
                anim2,
                "GetOrCreateAnim should return different AnimBool instances for different definitions."
            );
        }

        [Test]
        public void GetOrCreateAnimRespectsCurrentSpeedSetting()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutSpeed = 8f;

            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");
            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);

            Assert.That(
                anim.speed,
                Is.EqualTo(8f),
                "AnimBool speed should match the current settings value."
            );
        }

        [Test]
        public void GetOrCreateAnimUpdatesSpeedOnSubsequentCalls()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");

            settings.WGroupFoldoutSpeed = 4f;
            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            Assert.That(anim.speed, Is.EqualTo(4f), "Initial speed should be 4f.");

            settings.WGroupFoldoutSpeed = 10f;
            AnimBool samAnim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            Assert.That(
                samAnim.speed,
                Is.EqualTo(10f),
                "Speed should update to 10f on subsequent call."
            );
        }

        [Test]
        public void GetOrCreateAnimSetsTargetToExpandedValue()
        {
            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");

            AnimBool animExpanded = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true
            );
            Assert.IsTrue(
                animExpanded.target,
                "AnimBool target should be true when expanded is true."
            );

            AnimBool animCollapsed = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: false
            );
            Assert.IsFalse(
                animCollapsed.target,
                "AnimBool target should be false when expanded is false."
            );
        }

        [Test]
        public void GetFadeProgressReturnsImmediateValueWhenTweenDisabled()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = false;

            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");

            float expandedProgress = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: true
            );
            Assert.That(
                expandedProgress,
                Is.EqualTo(1f),
                "When tweening disabled, expanded=true should return 1f immediately."
            );

            float collapsedProgress = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: false
            );
            Assert.That(
                collapsedProgress,
                Is.EqualTo(0f),
                "When tweening disabled, expanded=false should return 0f immediately."
            );
        }

        [Test]
        public void GetFadeProgressReturnsAnimatedValueWhenTweenEnabled()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;
            settings.WGroupFoldoutSpeed = 4f;

            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");

            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: false);
            anim.value = false;

            anim.target = true;

            float progress = WGroupAnimationState.GetFadeProgress(definition, expanded: true);

            Assert.That(
                progress,
                Is.InRange(0f, 1f),
                "Fade progress should be between 0 and 1 during animation."
            );
        }

        [Test]
        public void ClearCacheRemovesAllAnimations()
        {
            WGroupDefinition definition1 = CreateTestDefinition("GroupOne", "property1");
            WGroupDefinition definition2 = CreateTestDefinition("GroupTwo", "property2");

            AnimBool anim1Before = WGroupAnimationState.GetOrCreateAnim(
                definition1,
                expanded: true
            );
            AnimBool anim2Before = WGroupAnimationState.GetOrCreateAnim(
                definition2,
                expanded: true
            );

            WGroupAnimationState.ClearCache();

            AnimBool anim1After = WGroupAnimationState.GetOrCreateAnim(definition1, expanded: true);
            AnimBool anim2After = WGroupAnimationState.GetOrCreateAnim(definition2, expanded: true);

            Assert.AreNotSame(
                anim1Before,
                anim1After,
                "After ClearCache, a new AnimBool should be created for definition1."
            );
            Assert.AreNotSame(
                anim2Before,
                anim2After,
                "After ClearCache, a new AnimBool should be created for definition2."
            );
        }

        [Test]
        public void AnimationUpdatesTargetOnExpandedChange()
        {
            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");

            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            Assert.IsTrue(anim.target, "Initial target should be true.");

            WGroupAnimationState.GetOrCreateAnim(definition, expanded: false);
            Assert.IsFalse(anim.target, "Target should update to false.");

            WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            Assert.IsTrue(anim.target, "Target should update back to true.");
        }

        [Test]
        public void DefinitionsWithSameNameButDifferentAnchorPathsAreDifferent()
        {
            WGroupDefinition definition1 = CreateTestDefinition("SameGroup", "anchorPath1");
            WGroupDefinition definition2 = CreateTestDefinition("SameGroup", "anchorPath2");

            AnimBool anim1 = WGroupAnimationState.GetOrCreateAnim(definition1, expanded: true);
            AnimBool anim2 = WGroupAnimationState.GetOrCreateAnim(definition2, expanded: true);

            Assert.AreNotSame(
                anim1,
                anim2,
                "Definitions with same name but different anchor paths should have separate AnimBool instances."
            );
        }

        [Test]
        public void DefinitionsWithDifferentNameButSameAnchorPathAreDifferent()
        {
            WGroupDefinition definition1 = CreateTestDefinition("GroupA", "sameAnchorPath");
            WGroupDefinition definition2 = CreateTestDefinition("GroupB", "sameAnchorPath");

            AnimBool anim1 = WGroupAnimationState.GetOrCreateAnim(definition1, expanded: true);
            AnimBool anim2 = WGroupAnimationState.GetOrCreateAnim(definition2, expanded: true);

            Assert.AreNotSame(
                anim1,
                anim2,
                "Definitions with different names but same anchor path should have separate AnimBool instances."
            );
        }

        [Test]
        public void GetFadeProgressCreatesAnimBoolWhenTweenEnabled()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");

            float progress = WGroupAnimationState.GetFadeProgress(definition, expanded: true);

            Assert.That(
                progress,
                Is.InRange(0f, 1f),
                "GetFadeProgress should create AnimBool and return valid progress value."
            );
        }

        [TestCase(2f)]
        [TestCase(4f)]
        [TestCase(8f)]
        [TestCase(12f)]
        public void GetOrCreateAnimRespectsVariousSpeedSettings(float speed)
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutSpeed = speed;

            WGroupAnimationState.ClearCache();
            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");
            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);

            Assert.That(
                anim.speed,
                Is.EqualTo(speed),
                $"AnimBool speed should be {speed} when settings speed is {speed}."
            );
        }

        [Test]
        public void ClearCacheCanBeCalledMultipleTimesSafely()
        {
            WGroupAnimationState.ClearCache();
            WGroupAnimationState.ClearCache();
            WGroupAnimationState.ClearCache();

            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");
            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);

            Assert.IsTrue(
                anim != null,
                "After multiple ClearCache calls, GetOrCreateAnim should still work."
            );
        }

        [Test]
        public void GetFadeProgressReturnsConsistentResultsForSameState()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = false;

            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");

            float progress1 = WGroupAnimationState.GetFadeProgress(definition, expanded: true);
            float progress2 = WGroupAnimationState.GetFadeProgress(definition, expanded: true);
            float progress3 = WGroupAnimationState.GetFadeProgress(definition, expanded: true);

            Assert.That(progress1, Is.EqualTo(1f), "Progress should be 1f for expanded state.");
            Assert.That(progress2, Is.EqualTo(1f), "Progress should be consistent.");
            Assert.That(progress3, Is.EqualTo(1f), "Progress should be consistent.");
        }

        [Test]
        public void DifferentTargetInstanceIdsProduceDifferentAnimBools()
        {
            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");
            const int targetId1 = 12345;
            const int targetId2 = 67890;

            AnimBool anim1 = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: targetId1
            );
            AnimBool anim2 = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: targetId2
            );

            Assert.AreNotSame(
                anim1,
                anim2,
                "Same definition but different targetInstanceId values should produce different AnimBool instances."
            );
        }

        [Test]
        public void SameTargetInstanceIdProducesSameAnimBool()
        {
            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");
            const int targetId = 12345;

            AnimBool first = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: targetId
            );
            AnimBool second = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: false,
                targetInstanceId: targetId
            );

            Assert.AreSame(
                first,
                second,
                "Same definition and same targetInstanceId should return the same AnimBool instance."
            );
        }

        [Test]
        public void DefaultTargetInstanceIdIsZero()
        {
            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");

            AnimBool animWithDefault = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true
            );
            AnimBool animWithExplicitZero = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: 0
            );

            Assert.AreSame(
                animWithDefault,
                animWithExplicitZero,
                "When not specified, targetInstanceId should default to 0."
            );
        }

        [Test]
        public void SameDefinitionDifferentTargetsHaveIndependentState()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = false;

            WGroupDefinition definition = CreateTestDefinition("SharedGroup", "sharedProperty");
            const int targetA = 100;
            const int targetB = 200;

            AnimBool animA = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: targetA
            );
            AnimBool animB = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: false,
                targetInstanceId: targetB
            );

            Assert.AreNotSame(
                animA,
                animB,
                "Same definition on different targets should have separate AnimBool instances."
            );

            Assert.IsTrue(animA.target, "Target A should have expanded=true state.");
            Assert.IsFalse(animB.target, "Target B should have expanded=false state.");

            WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: false,
                targetInstanceId: targetA
            );
            Assert.IsFalse(animA.target, "Target A state should update to collapsed.");
            Assert.IsFalse(animB.target, "Target B state should remain unchanged (collapsed).");

            WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: targetB
            );
            Assert.IsFalse(animA.target, "Target A state should remain unchanged (collapsed).");
            Assert.IsTrue(animB.target, "Target B state should update to expanded.");
        }

        [Test]
        public void GetFadeProgressWithTargetInstanceIdReturnsCorrectValues()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = false;

            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");
            const int targetId1 = 111;
            const int targetId2 = 222;

            float progress1Expanded = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: true,
                targetInstanceId: targetId1
            );
            float progress2Collapsed = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: false,
                targetInstanceId: targetId2
            );

            Assert.That(
                progress1Expanded,
                Is.EqualTo(1f),
                "Target 1 expanded should return 1f when tweening disabled."
            );
            Assert.That(
                progress2Collapsed,
                Is.EqualTo(0f),
                "Target 2 collapsed should return 0f when tweening disabled."
            );
        }

        [Test]
        public void ClearCacheRemovesAllAnimationsIncludingThoseWithTargetInstanceIds()
        {
            WGroupDefinition definition = CreateTestDefinition("TestGroup", "testProperty");
            const int targetId1 = 1001;
            const int targetId2 = 1002;

            AnimBool anim1Before = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: targetId1
            );
            AnimBool anim2Before = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: targetId2
            );
            AnimBool animDefaultBefore = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true
            );

            WGroupAnimationState.ClearCache();

            AnimBool anim1After = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: targetId1
            );
            AnimBool anim2After = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: targetId2
            );
            AnimBool animDefaultAfter = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true
            );

            Assert.AreNotSame(
                anim1Before,
                anim1After,
                "After ClearCache, a new AnimBool should be created for targetId1."
            );
            Assert.AreNotSame(
                anim2Before,
                anim2After,
                "After ClearCache, a new AnimBool should be created for targetId2."
            );
            Assert.AreNotSame(
                animDefaultBefore,
                animDefaultAfter,
                "After ClearCache, a new AnimBool should be created for default targetInstanceId."
            );
        }

        /// <summary>
        /// Creates a test WGroupDefinition with the specified name and anchor property path.
        /// </summary>
        private static WGroupDefinition CreateTestDefinition(string name, string anchorPropertyPath)
        {
            return new WGroupDefinition(
                name: name,
                displayName: name,
                collapsible: true,
                startCollapsed: false,
                hideHeader: false,
                propertyPaths: new List<string> { anchorPropertyPath },
                anchorPropertyPath: anchorPropertyPath,
                anchorIndex: 0,
                declarationOrder: 0
            );
        }
    }
}
#endif
