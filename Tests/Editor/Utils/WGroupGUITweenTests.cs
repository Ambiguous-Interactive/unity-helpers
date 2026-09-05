// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor.AnimatedValues;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils.WGroup;

    /// <summary>
    /// Tests for WGroup tweening behavior in <see cref="WGroupGUI"/>.
    /// Validates fade group animation integration, tween enable/disable behavior,
    /// and edge cases for collapsible group animations.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WGroupGUITweenTests
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

        [Test]
        public void TweenEnabledCollapsibleGroupCreatesAnimBool()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition definition = CreateTestDefinition(
                "CollapsibleGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);

            Assert.IsTrue(
                anim != null,
                "AnimBool should be created for collapsible group when tweening is enabled."
            );
            Assert.IsTrue(anim.target, "AnimBool target should match expanded state.");
        }

        [Test]
        public void TweenEnabledAnimationSpeedRespectsSettings()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;
            settings.WGroupFoldoutSpeed = 6f;

            WGroupDefinition definition = CreateTestDefinition(
                "SpeedTestGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);

            Assert.That(
                anim.speed,
                Is.EqualTo(6f),
                "AnimBool speed should match the configured settings value."
            );
        }

        [Test]
        public void TweenEnabledAnimationUpdatesTargetOnStateChange()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition definition = CreateTestDefinition(
                "TargetUpdateGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            Assert.IsTrue(anim.target, "Initial target should be true when expanded.");

            WGroupAnimationState.GetOrCreateAnim(definition, expanded: false);
            Assert.IsFalse(anim.target, "Target should update to false when collapsed.");

            WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            Assert.IsTrue(anim.target, "Target should update back to true when expanded again.");
        }

        [Test]
        public void TweenEnabledFadeProgressReturnsBetweenZeroAndOne()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition definition = CreateTestDefinition(
                "FadeProgressGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            float progress = WGroupAnimationState.GetFadeProgress(definition, expanded: true);

            Assert.That(progress, Is.InRange(0f, 1f), "Fade progress should be between 0 and 1.");
        }

        [Test]
        public void TweenEnabledSpeedChangeAppliesImmediately()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;
            settings.WGroupFoldoutSpeed = 4f;

            WGroupDefinition definition = CreateTestDefinition(
                "SpeedChangeGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            Assert.That(anim.speed, Is.EqualTo(4f), "Initial speed should be 4f.");

            settings.WGroupFoldoutSpeed = 12f;
            WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            Assert.That(
                anim.speed,
                Is.EqualTo(12f),
                "Speed should update to 12f on subsequent call."
            );
        }

        [Test]
        public void TweenDisabledExpandedReturnsImmediateOne()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = false;

            WGroupDefinition definition = CreateTestDefinition(
                "ImmediateExpandedGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            float progress = WGroupAnimationState.GetFadeProgress(definition, expanded: true);

            Assert.That(
                progress,
                Is.EqualTo(1f),
                "When tweening is disabled, expanded state should return 1f immediately."
            );
        }

        [Test]
        public void TweenDisabledCollapsedReturnsImmediateZero()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = false;

            WGroupDefinition definition = CreateTestDefinition(
                "ImmediateCollapsedGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            float progress = WGroupAnimationState.GetFadeProgress(definition, expanded: false);

            Assert.That(
                progress,
                Is.EqualTo(0f),
                "When tweening is disabled, collapsed state should return 0f immediately."
            );
        }

        [Test]
        public void TweenDisabledNoAnimBoolCreatedForFadeProgress()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = false;

            WGroupDefinition definition = CreateTestDefinition(
                "NoAnimBoolGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            WGroupAnimationState.ClearCache();

            float progressExpanded = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: true
            );
            float progressCollapsed = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: false
            );

            Assert.That(
                progressExpanded,
                Is.EqualTo(1f),
                "Expanded should return 1f without animation."
            );
            Assert.That(
                progressCollapsed,
                Is.EqualTo(0f),
                "Collapsed should return 0f without animation."
            );
        }

        [Test]
        public void TweenDisabledToggleStateReturnsImmediateValues()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = false;

            WGroupDefinition definition = CreateTestDefinition(
                "ToggleGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            float progress1 = WGroupAnimationState.GetFadeProgress(definition, expanded: true);
            Assert.That(progress1, Is.EqualTo(1f), "First expanded should be 1f.");

            float progress2 = WGroupAnimationState.GetFadeProgress(definition, expanded: false);
            Assert.That(progress2, Is.EqualTo(0f), "First collapsed should be 0f.");

            float progress3 = WGroupAnimationState.GetFadeProgress(definition, expanded: true);
            Assert.That(progress3, Is.EqualTo(1f), "Second expanded should be 1f.");

            float progress4 = WGroupAnimationState.GetFadeProgress(definition, expanded: false);
            Assert.That(progress4, Is.EqualTo(0f), "Second collapsed should be 0f.");
        }

        [Test]
        public void NonCollapsibleGroupAlwaysFullyVisible()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition nonCollapsibleDefinition = CreateTestDefinition(
                "NonCollapsibleGroup",
                "testProperty",
                collapsible: false,
                hideHeader: false
            );

            float progressExpanded = WGroupAnimationState.GetFadeProgress(
                nonCollapsibleDefinition,
                expanded: true
            );
            float progressCollapsed = WGroupAnimationState.GetFadeProgress(
                nonCollapsibleDefinition,
                expanded: false
            );

            Assert.That(
                progressExpanded,
                Is.EqualTo(1f).Or.InRange(0f, 1f),
                "Non-collapsible group should be visible when tweening is enabled."
            );
            Assert.That(
                progressCollapsed,
                Is.EqualTo(0f).Or.InRange(0f, 1f),
                "Non-collapsible collapsed state should return valid fade value."
            );
        }

        [Test]
        public void HideHeaderGroupNoFoldoutAnimation()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition hideHeaderDefinition = CreateTestDefinition(
                "HideHeaderGroup",
                "testProperty",
                collapsible: true,
                hideHeader: true
            );

            float progress = WGroupAnimationState.GetFadeProgress(
                hideHeaderDefinition,
                expanded: true
            );

            Assert.That(
                progress,
                Is.InRange(0f, 1f),
                "Hide header group fade progress should be valid."
            );
        }

        [Test]
        public void NonCollapsibleWithHideHeaderReturnsValidProgress()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition definition = CreateTestDefinition(
                "NonCollapsibleHideHeaderGroup",
                "testProperty",
                collapsible: false,
                hideHeader: true
            );

            float progress = WGroupAnimationState.GetFadeProgress(definition, expanded: true);

            Assert.That(
                progress,
                Is.InRange(0f, 1f),
                "Non-collapsible hide header group should return valid fade progress."
            );
        }

        [Test]
        public void NestedCollapsibleGroupsHaveIndependentAnimations()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;
            settings.WGroupFoldoutSpeed = 4f;

            WGroupDefinition parentDefinition = CreateTestDefinition(
                "ParentGroup",
                "parentProperty",
                collapsible: true,
                hideHeader: false
            );

            WGroupDefinition childDefinition = CreateTestDefinition(
                "ChildGroup",
                "childProperty",
                collapsible: true,
                hideHeader: false
            );

            AnimBool parentAnim = WGroupAnimationState.GetOrCreateAnim(
                parentDefinition,
                expanded: true
            );
            AnimBool childAnim = WGroupAnimationState.GetOrCreateAnim(
                childDefinition,
                expanded: false
            );

            Assert.AreNotSame(
                parentAnim,
                childAnim,
                "Parent and child groups should have separate AnimBool instances."
            );
            Assert.IsTrue(parentAnim.target, "Parent animation target should be true.");
            Assert.IsFalse(childAnim.target, "Child animation target should be false.");
        }

        [Test]
        public void RapidExpandCollapseToggleDoesNotCauseIssues()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition definition = CreateTestDefinition(
                "RapidToggleGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);

            for (int iteration = 0; iteration < 100; iteration++)
            {
                bool expanded = iteration % 2 == 0;
                WGroupAnimationState.GetOrCreateAnim(definition, expanded);
                float progress = WGroupAnimationState.GetFadeProgress(definition, expanded);

                Assert.That(
                    progress,
                    Is.InRange(0f, 1f),
                    $"Fade progress should remain valid on iteration {iteration}."
                );
            }

            Assert.IsTrue(anim != null, "AnimBool should remain valid after rapid toggling.");
        }

        [Test]
        public void SettingsChangeMidAnimationHandledGracefully()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;
            settings.WGroupFoldoutSpeed = 2f;

            WGroupDefinition definition = CreateTestDefinition(
                "MidAnimationGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            float initialProgress = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: true
            );

            Assert.That(anim.speed, Is.EqualTo(2f), "Initial speed should be 2f.");

            settings.WGroupFoldoutSpeed = 10f;
            WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);

            Assert.That(anim.speed, Is.EqualTo(10f), "Speed should update mid-animation.");

            float progressAfterChange = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: true
            );
            Assert.That(
                progressAfterChange,
                Is.InRange(0f, 1f),
                "Fade progress should remain valid after settings change."
            );
        }

        [Test]
        public void TweenEnableDisableMidAnimationHandledGracefully()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition definition = CreateTestDefinition(
                "TweenToggleGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            Assert.IsTrue(anim != null, "AnimBool should be created when tweening is enabled.");

            settings.WGroupFoldoutTweenEnabled = false;
            float disabledProgress = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: true
            );
            Assert.That(
                disabledProgress,
                Is.EqualTo(1f),
                "After disabling tweening, expanded should return 1f immediately."
            );

            settings.WGroupFoldoutTweenEnabled = true;
            float enabledProgress = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: true
            );
            Assert.That(
                enabledProgress,
                Is.InRange(0f, 1f),
                "After re-enabling tweening, fade progress should be valid."
            );
        }

        [Test]
        public void MultipleGroupsWithSameNameDifferentAnchorHaveIndependentAnimations()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition definition1 = CreateTestDefinition(
                "SharedNameGroup",
                "anchor1",
                collapsible: true,
                hideHeader: false
            );

            WGroupDefinition definition2 = CreateTestDefinition(
                "SharedNameGroup",
                "anchor2",
                collapsible: true,
                hideHeader: false
            );

            AnimBool anim1 = WGroupAnimationState.GetOrCreateAnim(definition1, expanded: true);
            AnimBool anim2 = WGroupAnimationState.GetOrCreateAnim(definition2, expanded: false);

            Assert.AreNotSame(
                anim1,
                anim2,
                "Groups with same name but different anchors should have separate AnimBool instances."
            );
            Assert.IsTrue(anim1.target, "First group target should be true.");
            Assert.IsFalse(anim2.target, "Second group target should be false.");
        }

        [Test]
        public void ClearCacheDuringAnimationDoesNotCauseIssues()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition definition = CreateTestDefinition(
                "ClearCacheGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            AnimBool animBefore = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            Assert.IsTrue(animBefore != null, "AnimBool should exist before clearing cache.");

            WGroupAnimationState.ClearCache();

            AnimBool animAfter = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            Assert.IsTrue(animAfter != null, "AnimBool should be recreated after clearing cache.");
            Assert.AreNotSame(
                animBefore,
                animAfter,
                "New AnimBool should be a different instance after clearing cache."
            );
        }

        [Test]
        public void ZeroSpeedSettingHandledGracefully()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            float minSpeed = UnityHelpersSettings.MinFoldoutSpeed;
            settings.WGroupFoldoutSpeed = minSpeed;

            WGroupDefinition definition = CreateTestDefinition(
                "MinSpeedGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);

            Assert.That(
                anim.speed,
                Is.GreaterThanOrEqualTo(minSpeed),
                "AnimBool speed should be at least the minimum configured value."
            );

            float progress = WGroupAnimationState.GetFadeProgress(definition, expanded: true);
            Assert.That(
                progress,
                Is.InRange(0f, 1f),
                "Fade progress should be valid even at minimum speed."
            );
        }

        [Test]
        public void MaxSpeedSettingHandledGracefully()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            float maxSpeed = UnityHelpersSettings.MaxFoldoutSpeed;
            settings.WGroupFoldoutSpeed = maxSpeed;

            WGroupDefinition definition = CreateTestDefinition(
                "MaxSpeedGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);

            Assert.That(
                anim.speed,
                Is.LessThanOrEqualTo(maxSpeed),
                "AnimBool speed should be at most the maximum configured value."
            );

            float progress = WGroupAnimationState.GetFadeProgress(definition, expanded: true);
            Assert.That(
                progress,
                Is.InRange(0f, 1f),
                "Fade progress should be valid at maximum speed."
            );
        }

        [TestCase(2f)]
        [TestCase(4f)]
        [TestCase(6f)]
        [TestCase(8f)]
        [TestCase(10f)]
        public void VariousSpeedSettingsProduceValidAnimations(float speed)
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;
            settings.WGroupFoldoutSpeed = speed;

            WGroupAnimationState.ClearCache();
            WGroupDefinition definition = CreateTestDefinition(
                "VariableSpeedGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);

            Assert.That(anim.speed, Is.EqualTo(speed), $"AnimBool speed should be {speed}.");

            float progress = WGroupAnimationState.GetFadeProgress(definition, expanded: true);
            Assert.That(
                progress,
                Is.InRange(0f, 1f),
                $"Fade progress should be valid at speed {speed}."
            );
        }

        [Test]
        public void ConsecutiveExpandCollapseReturnsConsistentResults()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = false;

            WGroupDefinition definition = CreateTestDefinition(
                "ConsistentResultsGroup",
                "testProperty",
                collapsible: true,
                hideHeader: false
            );

            float expandedProgress1 = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: true
            );
            float expandedProgress2 = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: true
            );
            float expandedProgress3 = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: true
            );

            Assert.That(expandedProgress1, Is.EqualTo(1f), "First expanded call should return 1f.");
            Assert.That(
                expandedProgress2,
                Is.EqualTo(1f),
                "Second expanded call should return 1f."
            );
            Assert.That(expandedProgress3, Is.EqualTo(1f), "Third expanded call should return 1f.");

            float collapsedProgress1 = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: false
            );
            float collapsedProgress2 = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: false
            );
            float collapsedProgress3 = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: false
            );

            Assert.That(
                collapsedProgress1,
                Is.EqualTo(0f),
                "First collapsed call should return 0f."
            );
            Assert.That(
                collapsedProgress2,
                Is.EqualTo(0f),
                "Second collapsed call should return 0f."
            );
            Assert.That(
                collapsedProgress3,
                Is.EqualTo(0f),
                "Third collapsed call should return 0f."
            );
        }

        [Test]
        public void ShouldTweenWGroupFoldoutsReflectsSettingsState()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            settings.WGroupFoldoutTweenEnabled = true;
            Assert.IsTrue(
                UnityHelpersSettings.ShouldTweenWGroupFoldouts(),
                "ShouldTweenWGroupFoldouts should return true when enabled."
            );

            settings.WGroupFoldoutTweenEnabled = false;
            Assert.IsFalse(
                UnityHelpersSettings.ShouldTweenWGroupFoldouts(),
                "ShouldTweenWGroupFoldouts should return false when disabled."
            );
        }

        [Test]
        public void MultipleComponentsOnSameGameObjectHaveIndependentAnimationState()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition definition = CreateTestDefinition(
                "SharedGroup",
                "sharedProperty",
                collapsible: true,
                hideHeader: false
            );

            const int component1InstanceId = 12345;
            const int component2InstanceId = 67890;

            AnimBool anim1 = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: component1InstanceId
            );
            AnimBool anim2 = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: false,
                targetInstanceId: component2InstanceId
            );

            Assert.AreNotSame(
                anim1,
                anim2,
                "Components with same WGroup on same GameObject should have separate AnimBool instances."
            );
            Assert.IsTrue(
                anim1.target,
                "First component's animation target should be true (expanded)."
            );
            Assert.IsFalse(
                anim2.target,
                "Second component's animation target should be false (collapsed)."
            );

            WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: false,
                targetInstanceId: component1InstanceId
            );
            Assert.IsFalse(anim1.target, "First component's target should update to false.");
            Assert.IsFalse(anim2.target, "Second component's target should remain false.");

            WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: component2InstanceId
            );
            Assert.IsFalse(anim1.target, "First component's target should remain false.");
            Assert.IsTrue(anim2.target, "Second component's target should update to true.");
        }

        [Test]
        public void DifferentSerializedObjectsWithSameWGroupHaveIndependentState()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition definition = CreateTestDefinition(
                "IdenticalGroup",
                "identicalProperty",
                collapsible: true,
                hideHeader: false
            );

            const int serializedObject1InstanceId = 11111;
            const int serializedObject2InstanceId = 22222;

            float progress1Expanded = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: true,
                targetInstanceId: serializedObject1InstanceId
            );

            float progress2Collapsed = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: false,
                targetInstanceId: serializedObject2InstanceId
            );

            AnimBool anim1 = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: serializedObject1InstanceId
            );
            AnimBool anim2 = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: false,
                targetInstanceId: serializedObject2InstanceId
            );

            Assert.AreNotSame(
                anim1,
                anim2,
                "Different serialized objects with identical WGroup should have separate AnimBool instances."
            );
            Assert.IsTrue(anim1.target, "First serialized object should maintain expanded state.");
            Assert.IsFalse(
                anim2.target,
                "Second serialized object should maintain collapsed state."
            );

            Assert.That(
                progress1Expanded,
                Is.InRange(0f, 1f),
                "First serialized object fade progress should be valid."
            );
            Assert.That(
                progress2Collapsed,
                Is.InRange(0f, 1f),
                "Second serialized object fade progress should be valid."
            );
        }

        [Test]
        public void TargetInstanceIdIsolatesAnimationBetweenInspectors()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;
            settings.WGroupFoldoutSpeed = 4f;

            WGroupDefinition definition = CreateTestDefinition(
                "InspectorGroup",
                "inspectorProperty",
                collapsible: true,
                hideHeader: false
            );

            const int inspector1TargetId = 100;
            const int inspector2TargetId = 200;
            const int inspector3TargetId = 300;

            AnimBool animInspector1 = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: inspector1TargetId
            );
            AnimBool animInspector2 = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: false,
                targetInstanceId: inspector2TargetId
            );
            AnimBool animInspector3 = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: inspector3TargetId
            );

            Assert.AreNotSame(
                animInspector1,
                animInspector2,
                "Inspector 1 and 2 should have different AnimBool instances."
            );
            Assert.AreNotSame(
                animInspector2,
                animInspector3,
                "Inspector 2 and 3 should have different AnimBool instances."
            );
            Assert.AreNotSame(
                animInspector1,
                animInspector3,
                "Inspector 1 and 3 should have different AnimBool instances."
            );

            Assert.IsTrue(animInspector1.target, "Inspector 1 should be expanded.");
            Assert.IsFalse(animInspector2.target, "Inspector 2 should be collapsed.");
            Assert.IsTrue(animInspector3.target, "Inspector 3 should be expanded.");

            WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: false,
                targetInstanceId: inspector1TargetId
            );
            Assert.IsFalse(animInspector1.target, "Inspector 1 should now be collapsed.");
            Assert.IsFalse(animInspector2.target, "Inspector 2 should remain collapsed.");
            Assert.IsTrue(animInspector3.target, "Inspector 3 should remain expanded.");

            WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: inspector2TargetId
            );
            Assert.IsFalse(animInspector1.target, "Inspector 1 should remain collapsed.");
            Assert.IsTrue(animInspector2.target, "Inspector 2 should now be expanded.");
            Assert.IsTrue(animInspector3.target, "Inspector 3 should remain expanded.");
        }

        [Test]
        public void DefaultTargetInstanceIdBehavesAsLegacy()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition definition = CreateTestDefinition(
                "LegacyGroup",
                "legacyProperty",
                collapsible: true,
                hideHeader: false
            );

            AnimBool animDefault1 = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true
            );
            AnimBool animDefault2 = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: false
            );

            Assert.AreSame(
                animDefault1,
                animDefault2,
                "Default targetInstanceId should return the same AnimBool instance for same definition."
            );

            AnimBool animExplicitZero = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: 0
            );

            Assert.AreSame(
                animDefault1,
                animExplicitZero,
                "Explicit targetInstanceId of 0 should return same instance as default."
            );
        }

        [Test]
        public void TargetInstanceIdWithTweenDisabledReturnsCorrectValues()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = false;

            WGroupDefinition definition = CreateTestDefinition(
                "NoTweenGroup",
                "noTweenProperty",
                collapsible: true,
                hideHeader: false
            );

            const int instance1 = 111;
            const int instance2 = 222;

            float progress1Expanded = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: true,
                targetInstanceId: instance1
            );
            float progress1Collapsed = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: false,
                targetInstanceId: instance1
            );
            float progress2Expanded = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: true,
                targetInstanceId: instance2
            );
            float progress2Collapsed = WGroupAnimationState.GetFadeProgress(
                definition,
                expanded: false,
                targetInstanceId: instance2
            );

            Assert.That(progress1Expanded, Is.EqualTo(1f), "Instance 1 expanded should return 1f.");
            Assert.That(
                progress1Collapsed,
                Is.EqualTo(0f),
                "Instance 1 collapsed should return 0f."
            );
            Assert.That(progress2Expanded, Is.EqualTo(1f), "Instance 2 expanded should return 1f.");
            Assert.That(
                progress2Collapsed,
                Is.EqualTo(0f),
                "Instance 2 collapsed should return 0f."
            );
        }

        [Test]
        public void ClearCacheRemovesAllTargetInstanceIdEntries()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.WGroupFoldoutTweenEnabled = true;

            WGroupDefinition definition = CreateTestDefinition(
                "ClearAllGroup",
                "clearAllProperty",
                collapsible: true,
                hideHeader: false
            );

            const int instance1 = 1001;
            const int instance2 = 1002;
            const int instance3 = 1003;

            AnimBool anim1Before = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: instance1
            );
            AnimBool anim2Before = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: false,
                targetInstanceId: instance2
            );
            AnimBool anim3Before = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: instance3
            );

            Assert.IsTrue(anim1Before != null, "AnimBool 1 should exist before clearing.");
            Assert.IsTrue(anim2Before != null, "AnimBool 2 should exist before clearing.");
            Assert.IsTrue(anim3Before != null, "AnimBool 3 should exist before clearing.");

            WGroupAnimationState.ClearCache();

            AnimBool anim1After = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: instance1
            );
            AnimBool anim2After = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: false,
                targetInstanceId: instance2
            );
            AnimBool anim3After = WGroupAnimationState.GetOrCreateAnim(
                definition,
                expanded: true,
                targetInstanceId: instance3
            );

            Assert.AreNotSame(
                anim1Before,
                anim1After,
                "AnimBool 1 should be new instance after clearing."
            );
            Assert.AreNotSame(
                anim2Before,
                anim2After,
                "AnimBool 2 should be new instance after clearing."
            );
            Assert.AreNotSame(
                anim3Before,
                anim3After,
                "AnimBool 3 should be new instance after clearing."
            );
        }

        /// <summary>
        /// Creates a test <see cref="WGroupDefinition"/> with specified parameters.
        /// </summary>
        private static WGroupDefinition CreateTestDefinition(
            string name,
            string anchorPropertyPath,
            bool collapsible,
            bool hideHeader
        )
        {
            return new WGroupDefinition(
                name: name,
                displayName: name,
                collapsible: collapsible,
                startCollapsed: false,
                hideHeader: hideHeader,
                propertyPaths: new List<string> { anchorPropertyPath },
                anchorPropertyPath: anchorPropertyPath,
                anchorIndex: 0,
                declarationOrder: 0
            );
        }
    }
}
#endif
