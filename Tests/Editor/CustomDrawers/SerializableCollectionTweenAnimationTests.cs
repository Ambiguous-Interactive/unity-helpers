// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.AnimatedValues;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Editor.Utils.WGroup;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;

    /// <summary>
    /// Tests for foldout tween animation behavior in SerializableDictionary, SerializableSet,
    /// and WGroup contexts, ensuring animations trigger properly in all contexts including SettingsProvider.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableCollectionTweenAnimationTests : CommonTestBase
    {
        private const string SerializableDictionaryFoldoutSpeedPropertyPath = UnityHelpersSettings
            .SerializedPropertyNames
            .SerializableDictionaryFoldoutSpeed;
        private const string SerializableSortedDictionaryFoldoutSpeedPropertyPath =
            UnityHelpersSettings.SerializedPropertyNames.SerializableSortedDictionaryFoldoutSpeed;
        private const string SerializableSetFoldoutSpeedPropertyPath = UnityHelpersSettings
            .SerializedPropertyNames
            .SerializableSetFoldoutSpeed;
        private const string SerializableSortedSetFoldoutSpeedPropertyPath = UnityHelpersSettings
            .SerializedPropertyNames
            .SerializableSortedSetFoldoutSpeed;

        private bool _originalDictionaryTweenEnabled;
        private bool _originalSortedDictionaryTweenEnabled;
        private bool _originalSetTweenEnabled;
        private bool _originalSortedSetTweenEnabled;
        private bool _originalWGroupTweenEnabled;
        private float _originalDictionarySpeed;
        private float _originalSortedDictionarySpeed;
        private float _originalSetSpeed;
        private float _originalSortedSetSpeed;
        private float _originalWGroupSpeed;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            GroupGUIWidthUtility.ResetForTests();
            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();
            WGroupAnimationState.ClearCache();

            _originalDictionaryTweenEnabled =
                UnityHelpersSettings.ShouldTweenSerializableDictionaryFoldouts();
            _originalSortedDictionaryTweenEnabled =
                UnityHelpersSettings.ShouldTweenSerializableSortedDictionaryFoldouts();
            _originalSetTweenEnabled = UnityHelpersSettings.ShouldTweenSerializableSetFoldouts();
            _originalSortedSetTweenEnabled =
                UnityHelpersSettings.ShouldTweenSerializableSortedSetFoldouts();
            _originalWGroupTweenEnabled = UnityHelpersSettings.ShouldTweenWGroupFoldouts();

            _originalDictionarySpeed = UnityHelpersSettings.GetSerializableDictionaryFoldoutSpeed();
            _originalSortedDictionarySpeed =
                UnityHelpersSettings.GetSerializableSortedDictionaryFoldoutSpeed();
            _originalSetSpeed = UnityHelpersSettings.GetSerializableSetFoldoutSpeed();
            _originalSortedSetSpeed = UnityHelpersSettings.GetSerializableSortedSetFoldoutSpeed();
            _originalWGroupSpeed = UnityHelpersSettings.GetWGroupFoldoutSpeed();
        }

        [TearDown]
        public override void TearDown()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(
                _originalDictionaryTweenEnabled
            );
            UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(
                _originalSortedDictionaryTweenEnabled
            );
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(_originalSetTweenEnabled);
            UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(
                _originalSortedSetTweenEnabled
            );
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(_originalWGroupTweenEnabled);

            SetFoldoutSpeed(
                SerializableDictionaryFoldoutSpeedPropertyPath,
                _originalDictionarySpeed
            );
            SetFoldoutSpeed(
                SerializableSortedDictionaryFoldoutSpeedPropertyPath,
                _originalSortedDictionarySpeed
            );
            SetFoldoutSpeed(SerializableSetFoldoutSpeedPropertyPath, _originalSetSpeed);
            SetFoldoutSpeed(SerializableSortedSetFoldoutSpeedPropertyPath, _originalSortedSetSpeed);
            UnityHelpersSettings.instance.WGroupFoldoutSpeed = _originalWGroupSpeed;

            WGroupAnimationState.ClearCache();
            GroupGUIWidthUtility.ResetForTests();
            base.TearDown();
        }

        [Test]
        public void DictionaryTweenEnabledSettingIsRespected()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                ),
                "Dictionary tweening should be enabled when setting is true."
            );

            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);
            Assert.IsFalse(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                ),
                "Dictionary tweening should be disabled when setting is false."
            );
        }

        [Test]
        public void SortedDictionaryTweenEnabledSettingIsRespected()
        {
            UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(true);
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: true
                ),
                "Sorted dictionary tweening should be enabled when setting is true."
            );

            UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(false);
            Assert.IsFalse(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: true
                ),
                "Sorted dictionary tweening should be disabled when setting is false."
            );
        }

        [Test]
        public void SetTweenEnabledSettingIsRespected()
        {
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(true);
            Assert.IsTrue(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: false),
                "Set tweening should be enabled when setting is true."
            );

            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(false);
            Assert.IsFalse(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: false),
                "Set tweening should be disabled when setting is false."
            );
        }

        [Test]
        public void SortedSetTweenEnabledSettingIsRespected()
        {
            UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(true);
            Assert.IsTrue(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: true),
                "Sorted set tweening should be enabled when setting is true."
            );

            UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(false);
            Assert.IsFalse(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: true),
                "Sorted set tweening should be disabled when setting is false."
            );
        }

        [Test]
        public void WGroupTweenEnabledSettingIsRespected()
        {
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(true);
            Assert.IsTrue(
                UnityHelpersSettings.ShouldTweenWGroupFoldouts(),
                "WGroup tweening should be enabled when setting is true."
            );

            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(false);
            Assert.IsFalse(
                UnityHelpersSettings.ShouldTweenWGroupFoldouts(),
                "WGroup tweening should be disabled when setting is false."
            );
        }

        [Test]
        public void DictionaryTweenIsIndependentOfSortedDictionaryTween()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);
            UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(false);

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                ),
                "Dictionary tweening should be enabled."
            );
            Assert.IsFalse(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: true
                ),
                "Sorted dictionary tweening should be disabled."
            );

            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);
            UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(true);

            Assert.IsFalse(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                ),
                "Dictionary tweening should be disabled."
            );
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: true
                ),
                "Sorted dictionary tweening should be enabled."
            );
        }

        [Test]
        public void SetTweenIsIndependentOfSortedSetTween()
        {
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(true);
            UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(false);

            Assert.IsTrue(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: false),
                "Set tweening should be enabled."
            );
            Assert.IsFalse(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: true),
                "Sorted set tweening should be disabled."
            );

            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(false);
            UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(true);

            Assert.IsFalse(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: false),
                "Set tweening should be disabled."
            );
            Assert.IsTrue(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: true),
                "Sorted set tweening should be enabled."
            );
        }

        [Test]
        public void WGroupTweenIsIndependentOfCollectionTweens()
        {
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(true);
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(false);

            Assert.IsTrue(
                UnityHelpersSettings.ShouldTweenWGroupFoldouts(),
                "WGroup tweening should be enabled independently."
            );
            Assert.IsFalse(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                ),
                "Dictionary tweening should remain disabled."
            );
            Assert.IsFalse(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: false),
                "Set tweening should remain disabled."
            );
        }

        [Test]
        public void WGroupAnimationStateGetFadeProgressReturnsImmediatelyWhenTweenDisabled()
        {
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(false);
            WGroupAnimationState.ClearCache();

            WGroupDefinition definition = CreateGroupDefinition("TestGroup");

            float expandedFade = WGroupAnimationState.GetFadeProgress(definition, expanded: true);
            Assert.AreEqual(
                1f,
                expandedFade,
                0.001f,
                "When tween disabled and expanded, fade should be 1."
            );

            float collapsedFade = WGroupAnimationState.GetFadeProgress(definition, expanded: false);
            Assert.AreEqual(
                0f,
                collapsedFade,
                0.001f,
                "When tween disabled and collapsed, fade should be 0."
            );
        }

        [Test]
        public void WGroupAnimationStateCreatesAnimBoolWhenTweenEnabled()
        {
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(true);
            WGroupAnimationState.ClearCache();

            WGroupDefinition definition = CreateGroupDefinition("TestGroup");

            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);

            Assert.IsTrue(anim != null, "AnimBool should be created when tweening is enabled.");
            Assert.IsTrue(anim.target, "AnimBool target should be set to expanded state.");
        }

        [Test]
        public void WGroupAnimationStateCacheCanBeCleared()
        {
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(true);

            WGroupDefinition definition = CreateGroupDefinition("TestGroup");

            AnimBool anim1 = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            Assert.IsTrue(anim1 != null, "First AnimBool should be created.");

            WGroupAnimationState.ClearCache();

            AnimBool anim2 = WGroupAnimationState.GetOrCreateAnim(definition, expanded: false);
            Assert.IsTrue(anim2 != null, "Second AnimBool should be created after cache clear.");

            // After clear, a new AnimBool should be created (we can't guarantee reference inequality
            // due to pooling, but target should match the new state)
            Assert.IsFalse(anim2.target, "New AnimBool should have target set to collapsed state.");
        }

        [Test]
        public void WGroupAnimationStateSpeedIsConfigurable()
        {
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(true);

            const float TestSpeed = 7.5f;
            UnityHelpersSettings.instance.WGroupFoldoutSpeed = TestSpeed;
            WGroupAnimationState.ClearCache();

            WGroupDefinition definition = CreateGroupDefinition("TestGroup");
            AnimBool anim = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);

            Assert.AreEqual(
                TestSpeed,
                anim.speed,
                0.001f,
                "AnimBool speed should match configured speed."
            );
        }

        [Test]
        public void DictionaryFoldoutSpeedIsConfigurable()
        {
            const float TestSpeed = 5.5f;
            SetFoldoutSpeed(SerializableDictionaryFoldoutSpeedPropertyPath, TestSpeed);

            float retrievedSpeed = UnityHelpersSettings.GetSerializableDictionaryFoldoutSpeed();

            Assert.AreEqual(
                TestSpeed,
                retrievedSpeed,
                0.001f,
                "Dictionary foldout speed should match configured value."
            );
        }

        [Test]
        public void SortedDictionaryFoldoutSpeedIsConfigurable()
        {
            const float TestSpeed = 6.0f;
            SetFoldoutSpeed(SerializableSortedDictionaryFoldoutSpeedPropertyPath, TestSpeed);

            float retrievedSpeed =
                UnityHelpersSettings.GetSerializableSortedDictionaryFoldoutSpeed();

            Assert.AreEqual(
                TestSpeed,
                retrievedSpeed,
                0.001f,
                "Sorted dictionary foldout speed should match configured value."
            );
        }

        [Test]
        public void SetFoldoutSpeedIsConfigurable()
        {
            const float TestSpeed = 4.0f;
            SetFoldoutSpeed(SerializableSetFoldoutSpeedPropertyPath, TestSpeed);

            float retrievedSpeed = UnityHelpersSettings.GetSerializableSetFoldoutSpeed();

            Assert.AreEqual(
                TestSpeed,
                retrievedSpeed,
                0.001f,
                "Set foldout speed should match configured value."
            );
        }

        [Test]
        public void SortedSetFoldoutSpeedIsConfigurable()
        {
            const float TestSpeed = 4.5f;
            SetFoldoutSpeed(SerializableSortedSetFoldoutSpeedPropertyPath, TestSpeed);

            float retrievedSpeed = UnityHelpersSettings.GetSerializableSortedSetFoldoutSpeed();

            Assert.AreEqual(
                TestSpeed,
                retrievedSpeed,
                0.001f,
                "Sorted set foldout speed should match configured value."
            );
        }

        [Test]
        public void WGroupFoldoutSpeedIsConfigurable()
        {
            const float TestSpeed = 8.0f;
            UnityHelpersSettings.instance.WGroupFoldoutSpeed = TestSpeed;

            float retrievedSpeed = UnityHelpersSettings.GetWGroupFoldoutSpeed();

            Assert.AreEqual(
                TestSpeed,
                retrievedSpeed,
                0.001f,
                "WGroup foldout speed should match configured value."
            );
        }

        [Test]
        public void DictionaryTweenSettingPersistsAcrossSettingsReload()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);
            UnityHelpersSettings.instance.SaveSettings();

            bool value1 = UnityHelpersSettings.ShouldTweenSerializableDictionaryFoldouts();
            Assert.IsTrue(value1, "Dictionary tween should be enabled after save.");

            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);
            UnityHelpersSettings.instance.SaveSettings();

            bool value2 = UnityHelpersSettings.ShouldTweenSerializableDictionaryFoldouts();
            Assert.IsFalse(value2, "Dictionary tween should be disabled after save.");
        }

        [Test]
        public void WGroupAnimationStateHandlesSameDefinitionMultipleTimes()
        {
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(true);
            WGroupAnimationState.ClearCache();

            WGroupDefinition definition = CreateGroupDefinition("TestGroup");

            AnimBool anim1 = WGroupAnimationState.GetOrCreateAnim(definition, expanded: true);
            AnimBool anim2 = WGroupAnimationState.GetOrCreateAnim(definition, expanded: false);

            // Same definition should return same AnimBool (cached)
            Assert.AreSame(anim1, anim2, "Same definition should return cached AnimBool.");

            // Target should be updated to latest state
            Assert.IsFalse(anim2.target, "Target should be updated to collapsed state.");
        }

        [Test]
        public void WGroupAnimationStateDifferentDefinitionsHaveSeparateState()
        {
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(true);
            WGroupAnimationState.ClearCache();

            WGroupDefinition definition1 = CreateGroupDefinition("Group1");
            WGroupDefinition definition2 = CreateGroupDefinition("Group2");

            AnimBool anim1 = WGroupAnimationState.GetOrCreateAnim(definition1, expanded: true);
            AnimBool anim2 = WGroupAnimationState.GetOrCreateAnim(definition2, expanded: false);

            // Different definitions should have separate AnimBools
            Assert.AreNotSame(
                anim1,
                anim2,
                "Different definitions should have separate AnimBools."
            );
            Assert.IsTrue(anim1.target, "First definition should be expanded.");
            Assert.IsFalse(anim2.target, "Second definition should be collapsed.");
        }

        [Test]
        public void CollectionTweenSettingsAreIndependentPerType()
        {
            // Enable all
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);
            UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(true);
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(true);
            UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(true);

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                )
            );
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: true
                )
            );
            Assert.IsTrue(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: false)
            );
            Assert.IsTrue(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: true)
            );

            // Disable only regular dictionary
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);

            Assert.IsFalse(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                ),
                "Regular dictionary should be disabled."
            );
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: true
                ),
                "Sorted dictionary should still be enabled."
            );
            Assert.IsTrue(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: false),
                "Regular set should still be enabled."
            );
            Assert.IsTrue(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: true),
                "Sorted set should still be enabled."
            );
        }

        [Test]
        public void DictionaryFoldoutProgressWithTweenDisabledIsImmediate()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);

            TweenAnimationSimpleDictionaryHost host =
                CreateScriptableObject<TweenAnimationSimpleDictionaryHost>();
            host.dictionary["key"] = 1;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TweenAnimationSimpleDictionaryHost.dictionary)
            );

            // When tween is disabled, the foldout progress should be immediate (0 or 1)
            float progress = SerializableDictionaryPropertyDrawer.GetPendingFoldoutProgressForTests(
                dictionaryProperty,
                expanded: true,
                isSorted: false
            );

            Assert.AreEqual(
                1f,
                progress,
                0.001f,
                "With tween disabled, expanded progress should be 1."
            );

            progress = SerializableDictionaryPropertyDrawer.GetPendingFoldoutProgressForTests(
                dictionaryProperty,
                expanded: false,
                isSorted: false
            );

            Assert.AreEqual(
                0f,
                progress,
                0.001f,
                "With tween disabled, collapsed progress should be 0."
            );
        }

        [Test]
        public void SetFoldoutProgressWithTweenDisabledIsImmediate()
        {
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(false);

            TweenAnimationSimpleSetHost host =
                CreateScriptableObject<TweenAnimationSimpleSetHost>();
            host.set.Add(1);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(TweenAnimationSimpleSetHost.set)
            );

            // When tween is disabled, the foldout progress should be immediate (0 or 1)
            float progress = SerializableSetPropertyDrawer.GetPendingFoldoutProgressForTests(
                setProperty,
                expanded: true,
                isSorted: false
            );

            Assert.AreEqual(
                1f,
                progress,
                0.001f,
                "With tween disabled, expanded progress should be 1."
            );

            progress = SerializableSetPropertyDrawer.GetPendingFoldoutProgressForTests(
                setProperty,
                expanded: false,
                isSorted: false
            );

            Assert.AreEqual(
                0f,
                progress,
                0.001f,
                "With tween disabled, collapsed progress should be 0."
            );
        }

        [Test]
        public void GetListKeyUsesSerializedObjectInCache()
        {
            SerializableDictionaryPropertyDrawer drawer =
                new SerializableDictionaryPropertyDrawer();

            TweenAnimationSimpleDictionaryHost hostA =
                CreateScriptableObject<TweenAnimationSimpleDictionaryHost>();
            TweenAnimationSimpleDictionaryHost hostB =
                CreateScriptableObject<TweenAnimationSimpleDictionaryHost>();

            SerializedObject serializedObjectA = TrackDisposable(new SerializedObject(hostA));
            SerializedObject serializedObjectB = TrackDisposable(new SerializedObject(hostB));

            SerializedProperty propertyA = serializedObjectA.FindProperty(
                nameof(TweenAnimationSimpleDictionaryHost.dictionary)
            );
            SerializedProperty propertyB = serializedObjectB.FindProperty(
                nameof(TweenAnimationSimpleDictionaryHost.dictionary)
            );

            string keyA = drawer.GetListKey(propertyA);
            string keyB = drawer.GetListKey(propertyB);

            Assert.AreNotEqual(
                keyA,
                keyB,
                "List keys must include the serialized object to avoid cross-object cache reuse."
            );
        }

        [Test]
        public void PendingFoldoutStateIsIsolatedPerSerializedObject()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);
            SerializableDictionaryPropertyDrawer drawer =
                new SerializableDictionaryPropertyDrawer();

            TweenAnimationSimpleDictionaryHost hostA =
                CreateScriptableObject<TweenAnimationSimpleDictionaryHost>();
            TweenAnimationSimpleDictionaryHost hostB =
                CreateScriptableObject<TweenAnimationSimpleDictionaryHost>();

            SerializedObject serializedObjectA = TrackDisposable(new SerializedObject(hostA));
            SerializedObject serializedObjectB = TrackDisposable(new SerializedObject(hostB));

            SerializedProperty propertyA = serializedObjectA.FindProperty(
                nameof(TweenAnimationSimpleDictionaryHost.dictionary)
            );
            SerializedProperty propertyB = serializedObjectB.FindProperty(
                nameof(TweenAnimationSimpleDictionaryHost.dictionary)
            );

            drawer.GetOrCreatePendingEntry(
                propertyA,
                typeof(string),
                typeof(int),
                isSortedDictionary: false
            );
            drawer.GetOrCreatePendingEntry(
                propertyB,
                typeof(string),
                typeof(int),
                isSortedDictionary: false
            );

            drawer.SetPendingExpandedStateForTests(propertyA, true);

            bool foundA = drawer.TryGetPendingAnimationStateForTests(
                propertyA,
                out bool isExpandedA,
                out float progressA,
                out bool hasAnimA
            );
            bool foundB = drawer.TryGetPendingAnimationStateForTests(
                propertyB,
                out bool isExpandedB,
                out float progressB,
                out bool hasAnimB
            );

            Assert.IsTrue(foundA, "First pending entry should exist for the first object.");
            Assert.IsTrue(foundB, "Second pending entry should exist for the second object.");
            Assert.IsTrue(isExpandedA, "First pending entry should remain expanded after toggle.");
            Assert.IsFalse(
                isExpandedB,
                "Second pending entry should remain collapsed when untouched."
            );
            Assert.IsTrue(
                hasAnimA,
                "First pending entry should have an AnimBool when tweening is enabled."
            );
            Assert.IsTrue(hasAnimB, "Second pending entry should have its own AnimBool instance.");
            Assert.GreaterOrEqual(
                progressA,
                0f,
                "First pending entry should return a progress value."
            );
            Assert.AreEqual(
                0f,
                progressB,
                0.001f,
                "Second pending entry progress should stay at collapsed state."
            );
        }

        private static WGroupDefinition CreateGroupDefinition(string name)
        {
            List<string> propertyPaths = new List<string> { name };
            return new WGroupDefinition(
                name: name,
                displayName: name,
                collapsible: true,
                startCollapsed: false,
                hideHeader: false,
                propertyPaths: propertyPaths,
                anchorPropertyPath: name,
                anchorIndex: 0,
                declarationOrder: 0
            );
        }

        private static void SetFoldoutSpeed(string propertyPath, float speed)
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serializedSettings = new SerializedObject(settings);
            serializedSettings.Update();

            SerializedProperty property = serializedSettings.FindProperty(propertyPath);
            Assert.IsTrue(property != null, $"Settings did not contain property '{propertyPath}'.");

            float clamped = Mathf.Clamp(
                speed,
                UnityHelpersSettings.MinFoldoutSpeed,
                UnityHelpersSettings.MaxFoldoutSpeed
            );
            property.floatValue = clamped;

            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            settings.SaveSettings();
        }

        [Test]
        public void DictionaryMainFoldoutAnimationIsIsolatedByTargetObject()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);
            SerializableDictionaryPropertyDrawer.ClearMainFoldoutAnimCacheForTests();

            TweenAnimationSimpleDictionaryHost hostA =
                CreateScriptableObject<TweenAnimationSimpleDictionaryHost>();
            TweenAnimationSimpleDictionaryHost hostB =
                CreateScriptableObject<TweenAnimationSimpleDictionaryHost>();

            SerializedObject serializedObjectA = TrackDisposable(new SerializedObject(hostA));
            SerializedObject serializedObjectB = TrackDisposable(new SerializedObject(hostB));

            string propertyPath = nameof(TweenAnimationSimpleDictionaryHost.dictionary);

            // Trigger animation state creation for both objects with different expanded states
            SerializableDictionaryPropertyDrawer.GetMainFoldoutProgressForTests(
                serializedObjectA,
                propertyPath,
                isExpanded: true,
                isSortedDictionary: false
            );
            SerializableDictionaryPropertyDrawer.GetMainFoldoutProgressForTests(
                serializedObjectB,
                propertyPath,
                isExpanded: false,
                isSortedDictionary: false
            );

            // Both should have their own AnimBool entry in the cache
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasMainFoldoutAnimBoolForTests(
                    serializedObjectA,
                    propertyPath
                ),
                "First object should have its own main foldout AnimBool."
            );
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasMainFoldoutAnimBoolForTests(
                    serializedObjectB,
                    propertyPath
                ),
                "Second object should have its own main foldout AnimBool."
            );

            // Verify the animation states are independent
            float progressA = SerializableDictionaryPropertyDrawer.GetMainFoldoutProgressForTests(
                serializedObjectA,
                propertyPath,
                isExpanded: true,
                isSortedDictionary: false
            );
            float progressB = SerializableDictionaryPropertyDrawer.GetMainFoldoutProgressForTests(
                serializedObjectB,
                propertyPath,
                isExpanded: false,
                isSortedDictionary: false
            );

            // Progress values should reflect their individual expanded states
            Assert.GreaterOrEqual(
                progressA,
                0f,
                "First object progress should be valid (animating toward 1)."
            );
            Assert.LessOrEqual(
                progressB,
                1f,
                "Second object progress should be valid (animating toward 0)."
            );
        }

        [Test]
        public void SetMainFoldoutAnimationIsIsolatedByTargetObject()
        {
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(true);
            SerializableSetPropertyDrawer.ClearMainFoldoutAnimCacheForTests();

            TweenAnimationSimpleSetHost hostA =
                CreateScriptableObject<TweenAnimationSimpleSetHost>();
            TweenAnimationSimpleSetHost hostB =
                CreateScriptableObject<TweenAnimationSimpleSetHost>();

            SerializedObject serializedObjectA = TrackDisposable(new SerializedObject(hostA));
            SerializedObject serializedObjectB = TrackDisposable(new SerializedObject(hostB));

            string propertyPath = nameof(TweenAnimationSimpleSetHost.set);

            // Trigger animation state creation for both objects with different expanded states
            SerializableSetPropertyDrawer.GetMainFoldoutProgressForTests(
                serializedObjectA,
                propertyPath,
                isExpanded: true,
                isSortedSet: false
            );
            SerializableSetPropertyDrawer.GetMainFoldoutProgressForTests(
                serializedObjectB,
                propertyPath,
                isExpanded: false,
                isSortedSet: false
            );

            // Both should have their own AnimBool entry in the cache
            Assert.IsTrue(
                SerializableSetPropertyDrawer.HasMainFoldoutAnimBoolForTests(
                    serializedObjectA,
                    propertyPath
                ),
                "First object should have its own main foldout AnimBool."
            );
            Assert.IsTrue(
                SerializableSetPropertyDrawer.HasMainFoldoutAnimBoolForTests(
                    serializedObjectB,
                    propertyPath
                ),
                "Second object should have its own main foldout AnimBool."
            );

            // Verify the animation states are independent
            float progressA = SerializableSetPropertyDrawer.GetMainFoldoutProgressForTests(
                serializedObjectA,
                propertyPath,
                isExpanded: true,
                isSortedSet: false
            );
            float progressB = SerializableSetPropertyDrawer.GetMainFoldoutProgressForTests(
                serializedObjectB,
                propertyPath,
                isExpanded: false,
                isSortedSet: false
            );

            // Progress values should reflect their individual expanded states
            Assert.GreaterOrEqual(
                progressA,
                0f,
                "First object progress should be valid (animating toward 1)."
            );
            Assert.LessOrEqual(
                progressB,
                1f,
                "Second object progress should be valid (animating toward 0)."
            );
        }

        [Test]
        public void DictionaryAnimationCacheKeyIncludesInstanceId()
        {
            TweenAnimationSimpleDictionaryHost hostA =
                CreateScriptableObject<TweenAnimationSimpleDictionaryHost>();
            TweenAnimationSimpleDictionaryHost hostB =
                CreateScriptableObject<TweenAnimationSimpleDictionaryHost>();

            SerializedObject serializedObjectA = TrackDisposable(new SerializedObject(hostA));
            SerializedObject serializedObjectB = TrackDisposable(new SerializedObject(hostB));

            string propertyPath = nameof(TweenAnimationSimpleDictionaryHost.dictionary);

            string cacheKeyA = SerializableDictionaryPropertyDrawer.GetMainFoldoutCacheKeyForTests(
                serializedObjectA,
                propertyPath
            );
            string cacheKeyB = SerializableDictionaryPropertyDrawer.GetMainFoldoutCacheKeyForTests(
                serializedObjectB,
                propertyPath
            );

            // Cache keys should be different for different objects
            Assert.AreNotEqual(
                cacheKeyA,
                cacheKeyB,
                "Cache keys for different objects with the same property path should be different."
            );

            // Cache key should include the instance ID
            int instanceIdA = hostA.GetUnityObjectId();
            int instanceIdB = hostB.GetUnityObjectId();

            Assert.IsTrue(
                cacheKeyA.Contains(instanceIdA.ToString()),
                $"Cache key '{cacheKeyA}' should contain instance ID {instanceIdA}."
            );
            Assert.IsTrue(
                cacheKeyB.Contains(instanceIdB.ToString()),
                $"Cache key '{cacheKeyB}' should contain instance ID {instanceIdB}."
            );

            // Cache key should include the property path
            Assert.IsTrue(
                cacheKeyA.Contains(propertyPath),
                $"Cache key '{cacheKeyA}' should contain property path '{propertyPath}'."
            );
            Assert.IsTrue(
                cacheKeyB.Contains(propertyPath),
                $"Cache key '{cacheKeyB}' should contain property path '{propertyPath}'."
            );
        }

        [Test]
        public void SetAnimationCacheKeyIncludesInstanceId()
        {
            TweenAnimationSimpleSetHost hostA =
                CreateScriptableObject<TweenAnimationSimpleSetHost>();
            TweenAnimationSimpleSetHost hostB =
                CreateScriptableObject<TweenAnimationSimpleSetHost>();

            SerializedObject serializedObjectA = TrackDisposable(new SerializedObject(hostA));
            SerializedObject serializedObjectB = TrackDisposable(new SerializedObject(hostB));

            string propertyPath = nameof(TweenAnimationSimpleSetHost.set);

            string cacheKeyA = SerializableSetPropertyDrawer.GetMainFoldoutCacheKeyForTests(
                serializedObjectA,
                propertyPath
            );
            string cacheKeyB = SerializableSetPropertyDrawer.GetMainFoldoutCacheKeyForTests(
                serializedObjectB,
                propertyPath
            );

            // Cache keys should be different for different objects
            Assert.AreNotEqual(
                cacheKeyA,
                cacheKeyB,
                "Cache keys for different objects with the same property path should be different."
            );

            // Cache key should include the instance ID
            int instanceIdA = hostA.GetUnityObjectId();
            int instanceIdB = hostB.GetUnityObjectId();

            Assert.IsTrue(
                cacheKeyA.Contains(instanceIdA.ToString()),
                $"Cache key '{cacheKeyA}' should contain instance ID {instanceIdA}."
            );
            Assert.IsTrue(
                cacheKeyB.Contains(instanceIdB.ToString()),
                $"Cache key '{cacheKeyB}' should contain instance ID {instanceIdB}."
            );

            // Cache key should include the property path
            Assert.IsTrue(
                cacheKeyA.Contains(propertyPath),
                $"Cache key '{cacheKeyA}' should contain property path '{propertyPath}'."
            );
            Assert.IsTrue(
                cacheKeyB.Contains(propertyPath),
                $"Cache key '{cacheKeyB}' should contain property path '{propertyPath}'."
            );
        }
    }
}
