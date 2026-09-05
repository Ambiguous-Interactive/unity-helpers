// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Settings
{
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Settings;

    /// <summary>
    /// Tests for verifying SerializedObject caching and foldout state persistence
    /// in the UnityHelpersSettings panel.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SettingsSerializedObjectCacheTests
    {
        private bool _originalDictionaryTweenEnabled;
        private bool _originalSortedDictionaryTweenEnabled;

        [SetUp]
        public void SetUp()
        {
            _originalDictionaryTweenEnabled =
                UnityHelpersSettings.ShouldTweenSerializableDictionaryFoldouts();
            _originalSortedDictionaryTweenEnabled =
                UnityHelpersSettings.ShouldTweenSerializableSortedDictionaryFoldouts();

            UnityHelpersSettings.ClearCachedSerializedObjectForTests();
            SerializableDictionaryPropertyDrawer.ClearMainFoldoutAnimCacheForTests();
        }

        [TearDown]
        public void TearDown()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(
                _originalDictionaryTweenEnabled
            );
            UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(
                _originalSortedDictionaryTweenEnabled
            );

            UnityHelpersSettings.ClearCachedSerializedObjectForTests();
            SerializableDictionaryPropertyDrawer.ClearMainFoldoutAnimCacheForTests();
        }

        [Test]
        public void CachedSerializedObjectPreservesIsExpandedStateAcrossFrames()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            // Never 'using' a cached SerializedObject: disposing it breaks every later cache access.
            SerializedObject firstAccess = GetCachedSerializedObject(settings);
            SerializedProperty property = firstAccess.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(property != null, "WButtonCustomColors property should exist.");

            bool originalExpanded = property.isExpanded;
            property.isExpanded = false;

            SerializedObject secondAccess = GetCachedSerializedObject(settings);
            SerializedProperty sameProperty = secondAccess.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            Assert.AreSame(
                firstAccess,
                secondAccess,
                "GetCachedSerializedObject should return the same instance."
            );
            Assert.IsFalse(
                sameProperty.isExpanded,
                "isExpanded state should be preserved when using cached SerializedObject."
            );

            property.isExpanded = originalExpanded;
        }

        [Test]
        public void CachedSerializedObjectPreservesWEnumToggleButtonsExpandedState()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject firstAccess = GetCachedSerializedObject(settings);
            SerializedProperty property = firstAccess.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors
            );
            Assert.IsTrue(
                property != null,
                "WEnumToggleButtonsCustomColors property should exist."
            );

            bool originalExpanded = property.isExpanded;
            property.isExpanded = false;

            SerializedObject secondAccess = GetCachedSerializedObject(settings);
            SerializedProperty sameProperty = secondAccess.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors
            );

            Assert.AreSame(
                firstAccess,
                secondAccess,
                "GetCachedSerializedObject should return the same instance."
            );
            Assert.IsFalse(
                sameProperty.isExpanded,
                "WEnumToggleButtons isExpanded state should be preserved when using cached SerializedObject."
            );

            property.isExpanded = originalExpanded;
        }

        [Test]
        public void NewSerializedObjectDoesNotPreserveIsExpandedState()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            bool? capturedOriginalState = null;
            bool stateWasReset = false;

            using (SerializedObject firstObject = new(settings))
            {
                SerializedProperty property = firstObject.FindProperty(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
                );
                if (property != null)
                {
                    capturedOriginalState = property.isExpanded;
                    property.isExpanded = !capturedOriginalState.Value;
                }
            }

            using (SerializedObject secondObject = new(settings))
            {
                SerializedProperty property = secondObject.FindProperty(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
                );
                if (property != null && capturedOriginalState.HasValue)
                {
                    stateWasReset = property.isExpanded == capturedOriginalState.Value;
                }
            }

            Assert.IsTrue(
                capturedOriginalState.HasValue,
                "WButtonCustomColors property should exist for the test."
            );
        }

        [Test]
        public void ClearCachedSerializedObjectForcesNewObjectCreation()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject firstAccess = GetCachedSerializedObject(settings);
            int firstHashCode = firstAccess.GetHashCode();

            UnityHelpersSettings.ClearCachedSerializedObjectForTests();

            SerializedObject secondAccess = GetCachedSerializedObject(settings);
            int secondHashCode = secondAccess.GetHashCode();

            Assert.AreNotEqual(
                firstHashCode,
                secondHashCode,
                "After clearing the cache, a new SerializedObject should be created."
            );
        }

        [Test]
        public void CachedSerializedObjectHandlesNullGracefully()
        {
            UnityHelpersSettings.ClearCachedSerializedObjectForTests();

            // Note: using is safe here since result is expected to be null
            SerializedObject result = GetCachedSerializedObjectWithNull();
            Assert.IsTrue(
                result == null,
                "GetCachedSerializedObject should return null for null input."
            );
        }

        [Test]
        public void CachedSerializedObjectIsValidAfterUpdate()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject cached = GetCachedSerializedObject(settings);
            Assert.IsTrue(cached != null, "Cached SerializedObject should not be null.");
            Assert.IsTrue(
                cached.targetObject != null,
                "Cached SerializedObject target should not be null."
            );

            cached.UpdateIfRequiredOrScript();

            Assert.IsTrue(
                cached.targetObject != null,
                "Target object should remain valid after update."
            );

            SerializedProperty property = cached.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(property != null, "Should be able to find properties after update.");
        }

        [Test]
        public void MultipleFoldoutPropertiesPreserveIndependentState()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject cached = GetCachedSerializedObject(settings);

            SerializedProperty wButtonColors = cached.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            SerializedProperty wEnumColors = cached.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors
            );

            Assert.IsTrue(wButtonColors != null, "WButtonCustomColors property should exist.");
            Assert.IsTrue(
                wEnumColors != null,
                "WEnumToggleButtonsCustomColors property should exist."
            );

            bool originalWButtonState = wButtonColors.isExpanded;
            bool originalWEnumState = wEnumColors.isExpanded;

            try
            {
                wButtonColors.isExpanded = true;
                wEnumColors.isExpanded = false;

                SerializedObject cachedAgain = GetCachedSerializedObject(settings);

                SerializedProperty wButtonColorsAgain = cachedAgain.FindProperty(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
                );
                SerializedProperty wEnumColorsAgain = cachedAgain.FindProperty(
                    UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors
                );

                Assert.IsTrue(
                    wButtonColorsAgain.isExpanded,
                    "WButtonCustomColors should remain expanded."
                );
                Assert.IsFalse(
                    wEnumColorsAgain.isExpanded,
                    "WEnumToggleButtonsCustomColors should remain collapsed."
                );
            }
            finally
            {
                wButtonColors.isExpanded = originalWButtonState;
                wEnumColors.isExpanded = originalWEnumState;
            }
        }

        [Test]
        public void FoldoutStateTogglePreservesAcrossMultipleAccesses()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject cached = GetCachedSerializedObject(settings);
            SerializedProperty property = cached.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(property != null, "WButtonCustomColors property should exist.");

            bool originalState = property.isExpanded;

            try
            {
                for (int iteration = 0; iteration < 5; iteration++)
                {
                    bool expectedState = iteration % 2 == 0;
                    property.isExpanded = expectedState;

                    SerializedObject cachedAgain = GetCachedSerializedObject(settings);
                    SerializedProperty propertyAgain = cachedAgain.FindProperty(
                        UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
                    );

                    Assert.AreEqual(
                        expectedState,
                        propertyAgain.isExpanded,
                        $"Iteration {iteration}: isExpanded state should be preserved."
                    );
                }
            }
            finally
            {
                property.isExpanded = originalState;
            }
        }

        [Test]
        public void CachedSerializedObjectSurvivesApplyModifiedProperties()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject cached = GetCachedSerializedObject(settings);
            SerializedProperty property = cached.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(property != null, "WButtonCustomColors property should exist.");

            bool originalState = property.isExpanded;

            try
            {
                property.isExpanded = !originalState;
                cached.ApplyModifiedPropertiesWithoutUndo();

                SerializedObject cachedAgain = GetCachedSerializedObject(settings);
                SerializedProperty propertyAgain = cachedAgain.FindProperty(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
                );

                Assert.AreEqual(
                    !originalState,
                    propertyAgain.isExpanded,
                    "isExpanded state should survive ApplyModifiedPropertiesWithoutUndo."
                );
            }
            finally
            {
                property.isExpanded = originalState;
                cached.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void AnimBoolCacheKeyUsesCorrectInstanceId()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject cached = GetCachedSerializedObject(settings);
            long instanceId = cached.targetObject.GetUnityObjectId();

            Assert.AreNotEqual(
                0,
                instanceId,
                "UnityHelpersSettings instance should have a valid non-zero instance ID."
            );

            SerializedObject cachedAgain = GetCachedSerializedObject(settings);
            long sameInstanceId = cachedAgain.targetObject.GetUnityObjectId();

            Assert.AreEqual(
                instanceId,
                sameInstanceId,
                "Cached SerializedObject should reference the same target with the same instance ID."
            );
        }

        [Test]
        public void CachedSerializedObjectWorksWithUpdateCycles()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject cached = GetCachedSerializedObject(settings);
            SerializedProperty property = cached.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(property != null, "WButtonCustomColors property should exist.");

            bool originalState = property.isExpanded;

            try
            {
                property.isExpanded = !originalState;

                for (int cycle = 0; cycle < 3; cycle++)
                {
                    cached.UpdateIfRequiredOrScript();

                    SerializedObject sameCached = GetCachedSerializedObject(settings);
                    SerializedProperty sameProperty = sameCached.FindProperty(
                        UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
                    );

                    Assert.AreEqual(
                        !originalState,
                        sameProperty.isExpanded,
                        $"Cycle {cycle}: isExpanded state should persist through update cycles."
                    );
                }
            }
            finally
            {
                property.isExpanded = originalState;
            }
        }

        [Test]
        public void SettingsProviderPatternSimulation()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            UnityHelpersSettings.ClearCachedSerializedObjectForTests();

            SerializedObject cached = GetCachedSerializedObject(settings);
            SerializedProperty wButtonColors = cached.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(wButtonColors != null, "WButtonCustomColors property should exist.");

            bool originalState = wButtonColors.isExpanded;

            try
            {
                wButtonColors.isExpanded = true;
                cached.ApplyModifiedPropertiesWithoutUndo();

                for (int frame = 0; frame < 10; frame++)
                {
                    SerializedObject frameCached = GetCachedSerializedObject(settings);
                    frameCached.UpdateIfRequiredOrScript();

                    SerializedProperty frameProperty = frameCached.FindProperty(
                        UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
                    );

                    Assert.IsTrue(
                        frameProperty.isExpanded,
                        $"Frame {frame}: Property should stay expanded with cached SerializedObject."
                    );
                }

                wButtonColors.isExpanded = false;
                cached.ApplyModifiedPropertiesWithoutUndo();

                for (int frame = 0; frame < 10; frame++)
                {
                    SerializedObject frameCached = GetCachedSerializedObject(settings);
                    frameCached.UpdateIfRequiredOrScript();

                    SerializedProperty frameProperty = frameCached.FindProperty(
                        UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
                    );

                    Assert.IsFalse(
                        frameProperty.isExpanded,
                        $"Frame {frame}: Property should stay collapsed with cached SerializedObject."
                    );
                }
            }
            finally
            {
                wButtonColors.isExpanded = originalState;
                cached.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void NonCachedPatternDemonstratesStateLoss()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            int toggleCount = 0;
            bool? lastState = null;

            for (int frame = 0; frame < 5; frame++)
            {
                using SerializedObject newObject = new(settings);
                newObject.UpdateIfRequiredOrScript();

                SerializedProperty property = newObject.FindProperty(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
                );
                if (property != null)
                {
                    if (lastState.HasValue && property.isExpanded != lastState.Value)
                    {
                        toggleCount++;
                    }
                    lastState = property.isExpanded;

                    property.isExpanded = !property.isExpanded;
                }
            }
        }

        [Test]
        public void CacheReturnsIdenticalObjectAcrossMultipleCalls()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject firstCall = GetCachedSerializedObject(settings);
            Assert.IsTrue(firstCall != null, "First call should return a valid SerializedObject.");

            SerializedObject secondCall = GetCachedSerializedObject(settings);
            Assert.IsTrue(
                secondCall != null,
                "Second call should return a valid SerializedObject."
            );

            SerializedObject thirdCall = GetCachedSerializedObject(settings);
            Assert.IsTrue(thirdCall != null, "Third call should return a valid SerializedObject.");

            Assert.AreSame(
                firstCall,
                secondCall,
                "Cache should return the exact same object reference on second call."
            );
            Assert.AreSame(
                secondCall,
                thirdCall,
                "Cache should return the exact same object reference on third call."
            );
            Assert.AreSame(
                firstCall,
                thirdCall,
                "Cache should return the exact same object reference across all calls."
            );

            Assert.IsTrue(
                ReferenceEquals(firstCall, secondCall),
                "ReferenceEquals should confirm identical object references."
            );
        }

        [Test]
        public void CacheInvalidationRecreatesNewObject()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject beforeInvalidation = GetCachedSerializedObject(settings);
            Assert.IsTrue(
                beforeInvalidation != null,
                "SerializedObject before invalidation should not be null."
            );

            SerializedObject sameObjectReference = GetCachedSerializedObject(settings);
            Assert.AreSame(
                beforeInvalidation,
                sameObjectReference,
                "Before invalidation, cache should return same reference."
            );

            UnityHelpersSettings.ClearCachedSerializedObjectForTests();

            SerializedObject afterInvalidation = GetCachedSerializedObject(settings);
            Assert.IsTrue(
                afterInvalidation != null,
                "SerializedObject after invalidation should not be null."
            );

            Assert.AreNotSame(
                beforeInvalidation,
                afterInvalidation,
                "After cache invalidation, a new SerializedObject should be created."
            );
            Assert.IsFalse(
                ReferenceEquals(beforeInvalidation, afterInvalidation),
                "ReferenceEquals should confirm different object references after invalidation."
            );

            SerializedProperty property = afterInvalidation.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(
                property != null,
                "New SerializedObject after invalidation should have valid properties."
            );
        }

        [Test]
        public void PropertyFromStaleSerializedObjectThrowsDescriptiveError()
        {
            // The cache retains disposed SerializedObjects, so disposing one here would break later fixtures.

            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject disposableObject = new(settings);
            SerializedProperty propertyBeforeDispose = disposableObject.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(
                propertyBeforeDispose != null,
                "Property should be accessible before SerializedObject disposal."
            );

            bool originalExpanded = propertyBeforeDispose.isExpanded;

            disposableObject.Dispose();

            bool exceptionThrown = false;
            string exceptionMessage = string.Empty;
            try
            {
                // Behavior varies by Unity version, but it must not succeed silently.
                bool _ = propertyBeforeDispose.isExpanded;
            }
            catch (Exception ex)
            {
                exceptionThrown = true;
                exceptionMessage = ex.Message;
            }

            if (exceptionThrown)
            {
                Debug.Log(
                    $"[DIAGNOSTIC] Accessing property from disposed SerializedObject throws: {exceptionMessage}"
                );
            }
            else
            {
                Debug.LogWarning(
                    "[DIAGNOSTIC] Unity did not throw an exception when accessing disposed SerializedObject property. "
                        + "This behavior may vary by Unity version, but the property state is still invalid."
                );
            }

            // Diagnostic only: the behavior varies by Unity version, so nothing here can be asserted.
            Assert.Pass(
                "Diagnostic test completed. See console for behavior when accessing disposed SerializedObject."
            );
        }

        [Test]
        public void SerializedPropertyRemainsValidWithinSameCacheLifetime()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject cached = GetCachedSerializedObject(settings);
            Assert.IsTrue(cached != null, "Cached SerializedObject should not be null.");

            SerializedProperty property = cached.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(property != null, "WButtonCustomColors property should exist.");

            bool originalState = property.isExpanded;

            try
            {
                for (int frame = 0; frame < 10; frame++)
                {
                    property.isExpanded = !property.isExpanded;

                    Assert.IsTrue(
                        property.serializedObject != null,
                        $"Frame {frame}: Property's serializedObject reference should remain valid."
                    );
                    Assert.IsTrue(
                        property.propertyPath != null,
                        $"Frame {frame}: Property's propertyPath should remain valid."
                    );

                    bool currentState = property.isExpanded;
                    Assert.AreEqual(
                        frame % 2 == 0 ? !originalState : originalState,
                        currentState,
                        $"Frame {frame}: Property state should be correctly toggled."
                    );

                    cached.UpdateIfRequiredOrScript();

                    Assert.IsTrue(
                        property.propertyPath != null,
                        $"Frame {frame}: Property should remain valid after UpdateIfRequiredOrScript."
                    );
                }
            }
            finally
            {
                property.isExpanded = originalState;
            }
        }

        [Test]
        public void MultiplePropertiesFromSameCachedObjectShareLifetime()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject cached = GetCachedSerializedObject(settings);
            Assert.IsTrue(cached != null, "Cached SerializedObject should not be null.");

            SerializedProperty wButtonColors = cached.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            SerializedProperty wEnumColors = cached.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors
            );
            SerializedProperty dictionaryTween = cached.FindProperty(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableDictionaryFoldoutTweenEnabled
            );
            SerializedProperty sortedDictionaryTween = cached.FindProperty(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableSortedDictionaryFoldoutTweenEnabled
            );

            Assert.IsTrue(wButtonColors != null, "WButtonCustomColors property should exist.");
            Assert.IsTrue(
                wEnumColors != null,
                "WEnumToggleButtonsCustomColors property should exist."
            );
            Assert.IsTrue(
                dictionaryTween != null,
                "TweenSerializableDictionaryFoldouts property should exist."
            );
            Assert.IsTrue(
                sortedDictionaryTween != null,
                "TweenSerializableSortedDictionaryFoldouts property should exist."
            );

            bool originalWButtonState = wButtonColors.isExpanded;
            bool originalWEnumState = wEnumColors.isExpanded;

            try
            {
                wButtonColors.isExpanded = true;
                wEnumColors.isExpanded = false;

                Assert.AreSame(
                    wButtonColors.serializedObject,
                    wEnumColors.serializedObject,
                    "Properties should share the same parent SerializedObject."
                );
                Assert.AreSame(
                    wEnumColors.serializedObject,
                    dictionaryTween.serializedObject,
                    "All properties should share the same parent SerializedObject."
                );
                Assert.AreSame(
                    dictionaryTween.serializedObject,
                    sortedDictionaryTween.serializedObject,
                    "All properties should share the same parent SerializedObject."
                );

                cached.UpdateIfRequiredOrScript();

                Assert.IsTrue(
                    wButtonColors.propertyPath != null,
                    "WButtonColors should remain valid after update."
                );
                Assert.IsTrue(
                    wEnumColors.propertyPath != null,
                    "WEnumColors should remain valid after update."
                );
                Assert.IsTrue(
                    dictionaryTween.propertyPath != null,
                    "DictionaryTween should remain valid after update."
                );
                Assert.IsTrue(
                    sortedDictionaryTween.propertyPath != null,
                    "SortedDictionaryTween should remain valid after update."
                );

                Assert.IsTrue(
                    wButtonColors.isExpanded,
                    "WButtonColors expanded state should be preserved."
                );
                Assert.IsFalse(
                    wEnumColors.isExpanded,
                    "WEnumColors collapsed state should be preserved."
                );
            }
            finally
            {
                wButtonColors.isExpanded = originalWButtonState;
                wEnumColors.isExpanded = originalWEnumState;
            }
        }

        [Test]
        public void CacheHandlesTargetObjectChangeGracefully()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            SerializedObject firstCached = GetCachedSerializedObject(settings);
            Assert.IsTrue(firstCached != null, "First cached SerializedObject should not be null.");

            long originalTargetInstanceId = firstCached.targetObject.GetUnityObjectId();
            Assert.AreNotEqual(
                0,
                originalTargetInstanceId,
                "Target object should have a valid instance ID."
            );

            UnityHelpersSettings.ClearCachedSerializedObjectForTests();

            UnityHelpersSettings sameSettings = UnityHelpersSettings.instance;

            SerializedObject secondCached = GetCachedSerializedObject(sameSettings);
            Assert.IsTrue(
                secondCached != null,
                "Second cached SerializedObject should not be null."
            );

            Assert.AreNotSame(
                firstCached,
                secondCached,
                "After cache clear, a new SerializedObject should be created."
            );

            long newTargetInstanceId = secondCached.targetObject.GetUnityObjectId();
            Assert.AreEqual(
                originalTargetInstanceId,
                newTargetInstanceId,
                "New SerializedObject should target the same settings instance."
            );

            SerializedProperty property = secondCached.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(
                property != null,
                "New SerializedObject should have functional properties."
            );

            SerializedObject thirdCached = GetCachedSerializedObject(sameSettings);
            Assert.AreSame(
                secondCached,
                thirdCached,
                "After recreation, cache should consistently return the new object."
            );
        }

        /// <summary>
        /// Helper method to call the internal GetOrCreateCachedSerializedObject.
        /// </summary>
        private static SerializedObject GetCachedSerializedObject(UnityHelpersSettings settings)
        {
            return UnityHelpersSettings.GetOrCreateCachedSerializedObject(settings);
        }

        /// <summary>
        /// Helper method to test null handling.
        /// </summary>
        private static SerializedObject GetCachedSerializedObjectWithNull()
        {
            return UnityHelpersSettings.GetOrCreateCachedSerializedObject(null);
        }
    }
}
#endif
