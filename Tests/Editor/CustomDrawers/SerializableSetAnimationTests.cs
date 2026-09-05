// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;

    /// <summary>
    /// Comprehensive tests for duplicate/warning animation behavior in SerializableSetPropertyDrawer.
    /// Validates smooth tweening, animation completion tracking, shake offset calculation,
    /// and repaint triggering mechanisms to match SerializableDictionaryPropertyDrawer behavior.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableSetAnimationTests : CommonTestBase
    {
        [Test]
        public void EvaluateDuplicateShakeOffsetHonorsCycleLimit()
        {
            const double startTime = 0d;
            const double activeTime = 0.1432d;

            float activeOffset = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                0,
                startTime,
                activeTime,
                2
            );

            Assert.That(
                Mathf.Abs(activeOffset),
                Is.GreaterThan(1e-3f),
                "Active offset should be non-zero during animation cycle."
            );

            float exhaustedOffset = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                0,
                startTime,
                startTime + 10d,
                1
            );

            Assert.AreEqual(
                0f,
                exhaustedOffset,
                "Offset should be zero after cycle limit exhausted."
            );
        }

        [Test]
        public void EvaluateDuplicateShakeOffsetSupportsInfiniteCycles()
        {
            float offset = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                2,
                0d,
                100d,
                -1
            );

            Assert.That(
                Mathf.Abs(offset),
                Is.GreaterThan(1e-3f),
                "Infinite cycle animation should produce non-zero offset."
            );
        }

        [Test]
        public void EvaluateDuplicateShakeOffsetReturnsZeroWhenCycleLimitZero()
        {
            float offset = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                0,
                0d,
                0.1d,
                0
            );

            Assert.AreEqual(
                0f,
                offset,
                "Offset should be zero when cycle limit is zero (disabled)."
            );
        }

        [Test]
        public void EvaluateDuplicateShakeOffsetHandlesCurrentTimeBeforeStart()
        {
            float offset = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                0,
                10d,
                5d,
                3
            );

            float baseline = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                0,
                5d,
                5d,
                3
            );

            Assert.AreEqual(
                baseline,
                offset,
                0.0001f,
                "When currentTime < startTime, should treat as if startTime = currentTime."
            );
        }

        [Test]
        public void EvaluateDuplicateShakeOffsetVariesByArrayIndex()
        {
            const double currentTime = 1.0d;
            const double startTime = 0d;
            const int cycleLimit = 5;

            float offset0 = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                0,
                startTime,
                currentTime,
                cycleLimit
            );
            float offset1 = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                1,
                startTime,
                currentTime,
                cycleLimit
            );
            float offset10 = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                10,
                startTime,
                currentTime,
                cycleLimit
            );

            Assert.AreNotEqual(
                offset0,
                offset1,
                "Different array indices should produce different offsets (phase seed)."
            );
            Assert.AreNotEqual(
                offset1,
                offset10,
                "Different array indices should produce different offsets (phase seed)."
            );
        }

        [Test]
        public void EvaluateDuplicateShakeOffsetProducesOscillatingValues()
        {
            const double startTime = 0d;
            const int cycleLimit = 10;
            int positiveCount = 0;
            int negativeCount = 0;

            for (double t = 0.01; t < 2.0; t += 0.1)
            {
                float offset = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                    0,
                    startTime,
                    t,
                    cycleLimit
                );

                if (0.01f < offset)
                {
                    positiveCount++;
                }
                else if (offset < -0.01f)
                {
                    negativeCount++;
                }
            }

            Assert.That(
                positiveCount,
                Is.GreaterThan(0),
                "Should have positive offset values during oscillation."
            );
            Assert.That(
                negativeCount,
                Is.GreaterThan(0),
                "Should have negative offset values during oscillation."
            );
        }

        [Test]
        public void DuplicateStateGetAnimationOffsetRegistersNewStartTime()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new() { hasDuplicates = true };

            Assert.IsFalse(
                state.animationStartTimes.ContainsKey(5),
                "Index 5 should not be registered initially."
            );

            double currentTime = 10d;
            float offset = state.GetAnimationOffset(5, currentTime, 3);

            Assert.IsTrue(
                state.animationStartTimes.ContainsKey(5),
                "Index 5 should be registered after GetAnimationOffset call."
            );
            Assert.AreEqual(
                currentTime,
                state.animationStartTimes[5],
                "Start time should match the current time passed in."
            );
            Assert.That(
                Mathf.Abs(offset),
                Is.LessThanOrEqualTo(3f),
                "Offset magnitude should be bounded."
            );
        }

        [Test]
        public void DuplicateStateGetAnimationOffsetResetsAnimationCompleted()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new() { hasDuplicates = true };

            state.animationStartTimes[0] = 0d;
            state.CheckAnimationCompletion(100d, 1);

            Assert.IsFalse(
                state.IsAnimating,
                "Animation should be completed after CheckAnimationCompletion with elapsed > maxDuration."
            );

            float offset = state.GetAnimationOffset(99, 100d, 3);

            Assert.IsTrue(
                state.IsAnimating,
                "Animation should restart (IsAnimating = true) after adding new animation index."
            );
        }

        [Test]
        public void DuplicateStateGetAnimationOffsetReusesExistingStartTime()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new() { hasDuplicates = true };

            /*
                The 7-radian frequency and five-cycle limit give a 4.487-second animation window; sample inside
                it for nonzero offsets.
            */
            const double initialStartTime = 0d;
            state.animationStartTimes[3] = initialStartTime;

            float offset1 = state.GetAnimationOffset(3, 0.5d, 5);
            float offset2 = state.GetAnimationOffset(3, 1.0d, 5);

            Assert.AreEqual(
                initialStartTime,
                state.animationStartTimes[3],
                $"Start time should not be overwritten when key already exists. Expected: {initialStartTime}, Actual: {state.animationStartTimes[3]}"
            );

            Assert.AreNotEqual(
                0f,
                offset1,
                $"First offset should be non-zero during active animation. Offset1: {offset1}"
            );
            Assert.AreNotEqual(
                0f,
                offset2,
                $"Second offset should be non-zero during active animation. Offset2: {offset2}"
            );

            Assert.AreNotEqual(
                offset1,
                offset2,
                $"Offset should vary with different current times. Offset1: {offset1}, Offset2: {offset2}"
            );
        }

        [Test]
        public void DuplicateStateCheckAnimationCompletionMarksCompleteWhenAllExpired()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new() { hasDuplicates = true };

            state.animationStartTimes[0] = 0d;
            state.animationStartTimes[1] = 0.1d;
            state.animationStartTimes[2] = 0.2d;

            Assert.IsTrue(state.IsAnimating, "Should be animating initially.");

            state.CheckAnimationCompletion(100d, 1);

            Assert.IsFalse(
                state.IsAnimating,
                "Should not be animating after all animations have completed."
            );
        }

        [Test]
        public void DuplicateStateCheckAnimationCompletionDoesNotCompleteWhileAnimating()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new() { hasDuplicates = true };

            double startTime = 10d;
            state.animationStartTimes[0] = startTime;
            state.animationStartTimes[1] = startTime;

            Assert.IsTrue(state.IsAnimating, "Should be animating initially.");

            state.CheckAnimationCompletion(startTime + 0.05d, 3);

            Assert.IsTrue(
                state.IsAnimating,
                "Should still be animating when not enough time has passed."
            );
        }

        [Test]
        public void DuplicateStateCheckAnimationCompletionIgnoresWhenNoDuplicates()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new() { hasDuplicates = false };

            state.animationStartTimes[0] = 0d;

            state.CheckAnimationCompletion(100d, 5);

            Assert.IsFalse(
                state.IsAnimating,
                "Should not report animating when hasDuplicates is false."
            );
        }

        [Test]
        public void DuplicateStateCheckAnimationCompletionIgnoresZeroCycleLimit()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new() { hasDuplicates = true };

            state.animationStartTimes[0] = 0d;

            state.CheckAnimationCompletion(100d, 0);

            Assert.IsTrue(
                state.IsAnimating,
                "Should remain animating when cycleLimit is zero (disabled check)."
            );
        }

        [Test]
        public void DuplicateStateUpdateLastHadDuplicatesResetsAnimationCompletedOnChange()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new() { hasDuplicates = true };

            state.animationStartTimes[0] = 0d;

            // The normal flow: UpdateLastHadDuplicates runs during evaluation, before animations complete.
            state.UpdateLastHadDuplicates(true, forceReset: false);

            state.CheckAnimationCompletion(100d, 1);

            Assert.IsFalse(
                state.IsAnimating,
                $"Animation should be completed after CheckAnimationCompletion. IsAnimating: {state.IsAnimating}"
            );

            state.hasDuplicates = true;
            state.UpdateLastHadDuplicates(true, forceReset: false);

            Assert.IsFalse(
                state.IsAnimating,
                $"Animation should remain completed when hasDuplicates state unchanged. IsAnimating: {state.IsAnimating}"
            );

            state.hasDuplicates = true;
            state.UpdateLastHadDuplicates(false, forceReset: false);

            state.hasDuplicates = true;
            state.UpdateLastHadDuplicates(true, forceReset: false);

            Assert.IsTrue(
                state.IsAnimating,
                $"Animation should restart when hasDuplicates transitions from false to true. IsAnimating: {state.IsAnimating}"
            );
        }

        [Test]
        public void DuplicateStateUpdateLastHadDuplicatesWithForceResetAlwaysRestarts()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new() { hasDuplicates = true };

            state.animationStartTimes[0] = 0d;
            state.CheckAnimationCompletion(100d, 1);

            Assert.IsFalse(state.IsAnimating, "Animation should be completed.");

            state.hasDuplicates = true;
            state.UpdateLastHadDuplicates(true, forceReset: true);

            Assert.IsTrue(
                state.IsAnimating,
                "Animation should restart with forceReset even when state unchanged."
            );
        }

        [Test]
        public void DuplicateStateClearAnimationTrackingResetsAllState()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new() { hasDuplicates = true };

            state.animationStartTimes[0] = 5d;
            state.animationStartTimes[1] = 6d;
            state.animationStartTimes[2] = 7d;

            state.ClearAnimationTracking();

            Assert.AreEqual(
                0,
                state.animationStartTimes.Count,
                "Animation start times should be cleared."
            );
            Assert.IsTrue(state.IsDirty, "State should be marked dirty after clearing.");
        }

        [Test]
        public void DuplicateStateMarkDirtySetsIsDirtyTrue()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new();

            state.UpdateArraySize(10);

            Assert.IsFalse(state.IsDirty, "Should not be dirty after setting array size.");

            state.MarkDirty();

            Assert.IsTrue(state.IsDirty, "Should be dirty after MarkDirty call.");
        }

        [Test]
        public void DuplicateStateShouldSkipRefreshReturnsTrueWhenNoChanges()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new();

            state.UpdateArraySize(5);
            state.hasDuplicates = false;
            state.UpdateLastHadDuplicates(false);

            Assert.IsTrue(
                state.ShouldSkipRefresh(5),
                "Should skip refresh when array size unchanged and no duplicates."
            );
        }

        [Test]
        public void DuplicateStateShouldSkipRefreshReturnsFalseWhenArraySizeChanged()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new();

            state.UpdateArraySize(5);
            state.hasDuplicates = false;
            state.UpdateLastHadDuplicates(false);

            Assert.IsFalse(
                state.ShouldSkipRefresh(10),
                "Should not skip refresh when array size changed."
            );
        }

        [Test]
        public void DuplicateStateShouldSkipRefreshReturnsFalseWhenHasDuplicates()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new();

            state.UpdateArraySize(5);
            state.hasDuplicates = true;

            Assert.IsFalse(
                state.ShouldSkipRefresh(5),
                "Should not skip refresh when currently has duplicates."
            );
        }

        [Test]
        public void EvaluateDuplicateStateDetectsDuplicatesInSet()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            itemsProperty.arraySize = 3;
            itemsProperty.GetArrayElementAtIndex(0).stringValue = "apple";
            itemsProperty.GetArrayElementAtIndex(1).stringValue = "banana";
            itemsProperty.GetArrayElementAtIndex(2).stringValue = "apple";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(state.hasDuplicates, "Should detect duplicate entries.");
            Assert.IsTrue(
                state.duplicateIndices.Contains(0),
                "First 'apple' should be marked as duplicate."
            );
            Assert.IsTrue(
                state.duplicateIndices.Contains(2),
                "Second 'apple' should be marked as duplicate."
            );
            Assert.IsFalse(
                state.duplicateIndices.Contains(1),
                "'banana' should not be marked as duplicate."
            );
        }

        [Test]
        public void EvaluateDuplicateStateMarksFirstOccurrenceAsPrimary()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            itemsProperty.arraySize = 4;
            itemsProperty.GetArrayElementAtIndex(0).stringValue = "test";
            itemsProperty.GetArrayElementAtIndex(1).stringValue = "unique";
            itemsProperty.GetArrayElementAtIndex(2).stringValue = "test";
            itemsProperty.GetArrayElementAtIndex(3).stringValue = "test";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(state.primaryFlags[0], "First 'test' should be marked as primary.");
            Assert.IsFalse(state.primaryFlags[2], "Second 'test' should not be primary.");
            Assert.IsFalse(state.primaryFlags[3], "Third 'test' should not be primary.");
        }

        [Test]
        public void EvaluateDuplicateStateGeneratesSummaryForMultipleGroups()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            itemsProperty.arraySize = 6;
            itemsProperty.GetArrayElementAtIndex(0).stringValue = "a";
            itemsProperty.GetArrayElementAtIndex(1).stringValue = "b";
            itemsProperty.GetArrayElementAtIndex(2).stringValue = "a";
            itemsProperty.GetArrayElementAtIndex(3).stringValue = "b";
            itemsProperty.GetArrayElementAtIndex(4).stringValue = "c";
            itemsProperty.GetArrayElementAtIndex(5).stringValue = "c";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(state.hasDuplicates, "Should detect duplicates.");
            Assert.IsFalse(
                string.IsNullOrEmpty(state.summary),
                "Summary should not be empty when duplicates exist."
            );
            Assert.That(
                state.summary,
                Does.Contain("Duplicate"),
                "Summary should mention duplicates."
            );
        }

        [Test]
        public void EvaluateDuplicateStateNoDuplicatesProducesEmptySummary()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            itemsProperty.arraySize = 3;
            itemsProperty.GetArrayElementAtIndex(0).stringValue = "unique1";
            itemsProperty.GetArrayElementAtIndex(1).stringValue = "unique2";
            itemsProperty.GetArrayElementAtIndex(2).stringValue = "unique3";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(state.hasDuplicates, "Should not detect duplicates.");
            Assert.IsTrue(
                string.IsNullOrEmpty(state.summary),
                "Summary should be empty when no duplicates."
            );
        }

        [Test]
        public void EvaluateDuplicateStateHandlesEmptySet()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            itemsProperty.arraySize = 0;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(state.hasDuplicates, "Empty set should have no duplicates.");
            Assert.AreEqual(0, state.duplicateIndices.Count, "No indices should be marked.");
        }

        [Test]
        public void EvaluateDuplicateStateHandlesSingleElement()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            itemsProperty.arraySize = 1;
            itemsProperty.GetArrayElementAtIndex(0).stringValue = "single";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsFalse(state.hasDuplicates, "Single element cannot have duplicates.");
            Assert.AreEqual(0, state.duplicateIndices.Count, "No indices should be marked.");
        }

        [Test]
        public void EvaluateDuplicateStateWithIntegersDetectsDuplicates()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            itemsProperty.arraySize = 4;
            itemsProperty.GetArrayElementAtIndex(0).intValue = 42;
            itemsProperty.GetArrayElementAtIndex(1).intValue = 100;
            itemsProperty.GetArrayElementAtIndex(2).intValue = 42;
            itemsProperty.GetArrayElementAtIndex(3).intValue = 200;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.DuplicateState state = drawer.EvaluateDuplicateState(
                setProperty,
                itemsProperty,
                force: true
            );

            Assert.IsTrue(state.hasDuplicates, "Should detect duplicate integers.");
            Assert.IsTrue(
                state.duplicateIndices.Contains(0) && state.duplicateIndices.Contains(2),
                "Both occurrences of 42 should be marked."
            );
        }

        [Test]
        public void AnimationOffsetMatchesDictionaryDrawerFormula()
        {
            const int arrayIndex = 3;
            const double startTime = 0d;
            const double currentTime = 1.5d;
            const int cycleLimit = 5;

            float setOffset = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                arrayIndex,
                startTime,
                currentTime,
                cycleLimit
            );
            float dictOffset = SerializableDictionaryPropertyDrawer.EvaluateDuplicateTweenOffset(
                arrayIndex,
                startTime,
                currentTime,
                cycleLimit
            );

            Assert.AreEqual(
                dictOffset,
                setOffset,
                0.0001f,
                "Set and Dictionary animation formulas should produce identical results."
            );
        }

        [Test]
        public void AnimationOffsetMatchesDictionaryDrawerFormulaAcrossMultipleIndices()
        {
            const double startTime = 0d;
            const double currentTime = 2.7d;
            const int cycleLimit = 3;

            for (int arrayIndex = 0; arrayIndex < 20; arrayIndex++)
            {
                float setOffset = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                    arrayIndex,
                    startTime,
                    currentTime,
                    cycleLimit
                );
                float dictOffset =
                    SerializableDictionaryPropertyDrawer.EvaluateDuplicateTweenOffset(
                        arrayIndex,
                        startTime,
                        currentTime,
                        cycleLimit
                    );

                Assert.AreEqual(
                    dictOffset,
                    setOffset,
                    0.0001f,
                    $"Set and Dictionary animation should match for index {arrayIndex}."
                );
            }
        }

        [Test]
        public void AnimationOffsetMatchesDictionaryDrawerFormulaAcrossTimeRange()
        {
            const int arrayIndex = 7;
            const double startTime = 0d;
            const int cycleLimit = 10;

            for (double t = 0.1; t < 5.0; t += 0.3)
            {
                float setOffset = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                    arrayIndex,
                    startTime,
                    t,
                    cycleLimit
                );
                float dictOffset =
                    SerializableDictionaryPropertyDrawer.EvaluateDuplicateTweenOffset(
                        arrayIndex,
                        startTime,
                        t,
                        cycleLimit
                    );

                Assert.AreEqual(
                    dictOffset,
                    setOffset,
                    0.0001f,
                    $"Set and Dictionary animation should match at time {t}."
                );
            }
        }

        [Test]
        public void AnimationOffsetMatchesDictionaryForCycleLimitBoundary()
        {
            const int arrayIndex = 0;
            const double startTime = 0d;
            const int cycleLimit = 2;
            double cycleDuration = (2d * Math.PI) / 7d;
            double justBeforeLimit = startTime + cycleDuration * cycleLimit - 0.01d;
            double justAfterLimit = startTime + cycleDuration * cycleLimit + 0.01d;

            float setBeforeOffset = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                arrayIndex,
                startTime,
                justBeforeLimit,
                cycleLimit
            );
            float dictBeforeOffset =
                SerializableDictionaryPropertyDrawer.EvaluateDuplicateTweenOffset(
                    arrayIndex,
                    startTime,
                    justBeforeLimit,
                    cycleLimit
                );

            float setAfterOffset = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                arrayIndex,
                startTime,
                justAfterLimit,
                cycleLimit
            );
            float dictAfterOffset =
                SerializableDictionaryPropertyDrawer.EvaluateDuplicateTweenOffset(
                    arrayIndex,
                    startTime,
                    justAfterLimit,
                    cycleLimit
                );

            Assert.AreEqual(
                dictBeforeOffset,
                setBeforeOffset,
                0.0001f,
                "Offsets should match just before cycle limit."
            );
            Assert.AreEqual(
                dictAfterOffset,
                setAfterOffset,
                0.0001f,
                "Offsets should match just after cycle limit."
            );
            Assert.AreEqual(
                0f,
                setAfterOffset,
                "Offset should be zero after cycle limit exhausted."
            );
        }

        [Test]
        public void DuplicateStateIsAnimatingPropertyReflectsCurrentState()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new();

            Assert.IsFalse(
                state.IsAnimating,
                "Should not be animating when hasDuplicates is false."
            );

            state.hasDuplicates = true;

            Assert.IsTrue(state.IsAnimating, "Should be animating when hasDuplicates is true.");

            state.animationStartTimes[0] = 0d;
            state.CheckAnimationCompletion(100d, 1);

            Assert.IsFalse(state.IsAnimating, "Should not be animating after animations complete.");
        }

        [Test]
        public void DuplicateStateMultipleIndicesAnimateIndependently()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new() { hasDuplicates = true };

            double time1 = 1d;
            double time2 = 2d;
            double time3 = 3d;

            float offset1a = state.GetAnimationOffset(0, time1, 5);
            float offset2a = state.GetAnimationOffset(1, time2, 5);
            float offset3a = state.GetAnimationOffset(2, time3, 5);

            Assert.AreEqual(time1, state.animationStartTimes[0], "Index 0 should start at time1.");
            Assert.AreEqual(time2, state.animationStartTimes[1], "Index 1 should start at time2.");
            Assert.AreEqual(time3, state.animationStartTimes[2], "Index 2 should start at time3.");

            double queryTime = 5d;
            float offset1b = state.GetAnimationOffset(0, queryTime, 5);
            float offset2b = state.GetAnimationOffset(1, queryTime, 5);
            float offset3b = state.GetAnimationOffset(2, queryTime, 5);

            Assert.AreEqual(
                time1,
                state.animationStartTimes[0],
                "Index 0 start time should be preserved."
            );
            Assert.AreEqual(
                time2,
                state.animationStartTimes[1],
                "Index 1 start time should be preserved."
            );
            Assert.AreEqual(
                time3,
                state.animationStartTimes[2],
                "Index 2 start time should be preserved."
            );
        }

        [Test]
        public void NullEntryStateDetectsNullStrings()
        {
            SerializableSetPropertyDrawer.NullEntryState state = new();

            Assert.IsFalse(state.hasNullEntries, "Should initially have no null entries.");
            Assert.AreEqual(0, state.nullIndices.Count, "Null indices should be empty initially.");
        }

        [Test]
        public void NullEntryStateTooltipsCanBeSetAndRetrieved()
        {
            SerializableSetPropertyDrawer.NullEntryState state = new();

            state.tooltips[0] = "Entry at index 0 is null.";
            state.tooltips[5] = "Entry at index 5 is null.";

            Assert.IsTrue(state.tooltips.ContainsKey(0), "Should contain tooltip for index 0.");
            Assert.IsTrue(state.tooltips.ContainsKey(5), "Should contain tooltip for index 5.");
            Assert.AreEqual("Entry at index 0 is null.", state.tooltips[0]);
            Assert.AreEqual("Entry at index 5 is null.", state.tooltips[5]);
        }

        [Test]
        public void NullEntryStateScratchListIsAvailableForReuse()
        {
            SerializableSetPropertyDrawer.NullEntryState state = new();

            state.scratch.Add(1);
            state.scratch.Add(2);
            state.scratch.Add(3);

            Assert.AreEqual(3, state.scratch.Count, "Scratch list should contain added items.");

            state.scratch.Clear();

            Assert.AreEqual(0, state.scratch.Count, "Scratch list should be clearable.");
        }

        [Test]
        public void DuplicateAndNullStatesAreIndependent()
        {
            SerializableSetPropertyDrawer.DuplicateState dupState = new() { hasDuplicates = true };
            SerializableSetPropertyDrawer.NullEntryState nullState = new()
            {
                hasNullEntries = true,
            };

            dupState.duplicateIndices.Add(0);
            dupState.duplicateIndices.Add(2);

            nullState.nullIndices.Add(1);
            nullState.nullIndices.Add(3);

            Assert.IsTrue(dupState.hasDuplicates, "Duplicate state should track duplicates.");
            Assert.IsTrue(nullState.hasNullEntries, "Null state should track null entries.");

            Assert.IsTrue(dupState.duplicateIndices.Contains(0), "Index 0 is a duplicate.");
            Assert.IsTrue(dupState.duplicateIndices.Contains(2), "Index 2 is a duplicate.");
            Assert.IsFalse(dupState.duplicateIndices.Contains(1), "Index 1 is not a duplicate.");
            Assert.IsFalse(dupState.duplicateIndices.Contains(3), "Index 3 is not a duplicate.");

            Assert.IsTrue(nullState.nullIndices.Contains(1), "Index 1 is null.");
            Assert.IsTrue(nullState.nullIndices.Contains(3), "Index 3 is null.");
            Assert.IsFalse(nullState.nullIndices.Contains(0), "Index 0 is not null.");
            Assert.IsFalse(nullState.nullIndices.Contains(2), "Index 2 is not null.");
        }

        [Test]
        public void AnimationOffsetAppliesEvenWhenEntryIsBothDuplicateAndNull()
        {
            SerializableSetPropertyDrawer.DuplicateState dupState = new() { hasDuplicates = true };
            SerializableSetPropertyDrawer.NullEntryState nullState = new()
            {
                hasNullEntries = true,
            };

            dupState.duplicateIndices.Add(5);
            nullState.nullIndices.Add(5);

            double currentTime = 1.0d;
            float offset = dupState.GetAnimationOffset(5, currentTime, 3);

            Assert.That(
                Mathf.Abs(offset),
                Is.GreaterThan(0f).Or.EqualTo(0f),
                "Animation offset should be calculated for duplicate index regardless of null state."
            );

            Assert.IsTrue(
                dupState.animationStartTimes.ContainsKey(5),
                "Animation start time should be registered for dual-flagged entry."
            );
        }

        [Test]
        public void DuplicateStateAnimationContinuesWhileNullStatePresent()
        {
            SerializableSetPropertyDrawer.DuplicateState dupState = new() { hasDuplicates = true };
            SerializableSetPropertyDrawer.NullEntryState nullState = new()
            {
                hasNullEntries = true,
            };

            dupState.duplicateIndices.Add(0);
            nullState.nullIndices.Add(1);

            double startTime = 0d;
            dupState.animationStartTimes[0] = startTime;

            Assert.IsTrue(
                dupState.IsAnimating,
                "Duplicate animation should continue regardless of null state."
            );

            dupState.CheckAnimationCompletion(startTime + 0.5d, 3);

            Assert.IsTrue(
                dupState.IsAnimating,
                "Duplicate animation should still be active during animation period."
            );
        }

        [Test]
        public void EvaluateDuplicateShakeOffsetWithLargeCycleLimit()
        {
            const int arrayIndex = 0;
            const double startTime = 0d;
            const double currentTime = 50d;
            const int cycleLimit = 100;

            float offset = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                arrayIndex,
                startTime,
                currentTime,
                cycleLimit
            );

            Assert.That(
                Mathf.Abs(offset),
                Is.GreaterThan(1e-3f),
                "Animation should still be active with large cycle limit."
            );
        }

        [Test]
        public void EvaluateDuplicateShakeOffsetWithNegativeCycleLimitNeverExpires()
        {
            const int arrayIndex = 5;
            const double startTime = 0d;
            const int cycleLimit = -1;

            float offset1 = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                arrayIndex,
                startTime,
                1000d,
                cycleLimit
            );
            float offset2 = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                arrayIndex,
                startTime,
                10000d,
                cycleLimit
            );
            float offset3 = SerializableSetPropertyDrawer.EvaluateDuplicateShakeOffset(
                arrayIndex,
                startTime,
                100000d,
                cycleLimit
            );

            Assert.That(
                Mathf.Abs(offset1),
                Is.GreaterThan(0f).Or.EqualTo(0f),
                "Animation with negative cycle limit should produce offset at t=1000."
            );
            Assert.That(
                Mathf.Abs(offset2),
                Is.GreaterThan(0f).Or.EqualTo(0f),
                "Animation with negative cycle limit should produce offset at t=10000."
            );
            Assert.That(
                Mathf.Abs(offset3),
                Is.GreaterThan(0f).Or.EqualTo(0f),
                "Animation with negative cycle limit should produce offset at t=100000."
            );
        }

        [Test]
        public void DuplicateStateGetAnimationOffsetWithZeroCycleLimit()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new() { hasDuplicates = true };

            float offset = state.GetAnimationOffset(0, 1.0d, 0);

            Assert.AreEqual(
                0f,
                offset,
                "Animation offset should be zero when cycle limit is zero."
            );
        }

        [Test]
        public void DuplicateStateCheckAnimationCompletionWithInfiniteCycles()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new() { hasDuplicates = true };

            state.animationStartTimes[0] = 0d;

            state.CheckAnimationCompletion(1000000d, -1);

            Assert.IsTrue(
                state.IsAnimating,
                "Animation should never complete with negative cycle limit (infinite)."
            );
        }

        [Test]
        public void DuplicateStatePrimaryFlagsAreCorrectlySet()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new();

            state.primaryFlags[0] = true;
            state.primaryFlags[1] = false;
            state.primaryFlags[2] = false;

            Assert.IsTrue(state.primaryFlags[0], "Index 0 should be primary.");
            Assert.IsFalse(state.primaryFlags[1], "Index 1 should not be primary.");
            Assert.IsFalse(state.primaryFlags[2], "Index 2 should not be primary.");
        }

        [Test]
        public void DuplicateStateSummaryCanBeSetAndRead()
        {
            SerializableSetPropertyDrawer.DuplicateState state = new();

            const string testSummary = "Duplicate entry 'test' at indices 0, 3, 7";
            state.summary = testSummary;

            Assert.AreEqual(testSummary, state.summary, "Summary should match set value.");
        }

        [Test]
        public void NullEntryStateSummaryCanBeSetAndRead()
        {
            SerializableSetPropertyDrawer.NullEntryState state = new();

            const string testSummary = "Null entries at indices 1, 4";
            state.summary = testSummary;

            Assert.AreEqual(testSummary, state.summary, "Summary should match set value.");
        }
    }
}
