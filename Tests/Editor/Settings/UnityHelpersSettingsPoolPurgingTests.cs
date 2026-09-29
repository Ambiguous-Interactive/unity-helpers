// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Settings
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils.WGroup;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Tests for verifying that <see cref="UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime"/>
    /// correctly transfers intelligent buffer purging settings to <see cref="PoolPurgeSettings"/>.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class UnityHelpersSettingsPoolPurgingTests
    {
        private UnityHelpersSettings _settings;
        private SerializedObject _serializedSettings;

        private bool _originalGlobalEnabled;
        private float _originalIdleTimeoutSeconds;
        private int _originalMinRetainCount;
        private int _originalWarmRetainCount;
        private int _originalMaxPoolSize;
        private float _originalBufferMultiplier;
        private float _originalRollingWindowSeconds;
        private float _originalHysteresisSeconds;
        private float _originalSpikeThresholdMultiplier;

        [SetUp]
        public void SetUp()
        {
            _settings = UnityHelpersSettings.instance;
            _serializedSettings = new SerializedObject(_settings);
            _serializedSettings.UpdateIfRequiredOrScript();

            _originalGlobalEnabled = PoolPurgeSettings.GlobalEnabled;
            _originalIdleTimeoutSeconds = PoolPurgeSettings.DefaultGlobalIdleTimeoutSeconds;
            _originalMinRetainCount = PoolPurgeSettings.DefaultGlobalMinRetainCount;
            _originalWarmRetainCount = PoolPurgeSettings.DefaultGlobalWarmRetainCount;
            _originalMaxPoolSize = PoolPurgeSettings.DefaultGlobalMaxPoolSize;
            _originalBufferMultiplier = PoolPurgeSettings.DefaultGlobalBufferMultiplier;
            _originalRollingWindowSeconds = PoolPurgeSettings.DefaultGlobalRollingWindowSeconds;
            _originalHysteresisSeconds = PoolPurgeSettings.DefaultGlobalHysteresisSeconds;
            _originalSpikeThresholdMultiplier =
                PoolPurgeSettings.DefaultGlobalSpikeThresholdMultiplier;

            PoolPurgeSettings.ClearSettingsTypeConfigurations();
        }

        [TearDown]
        public void TearDown()
        {
            PoolPurgeSettings.GlobalEnabled = _originalGlobalEnabled;
            PoolPurgeSettings.DefaultGlobalIdleTimeoutSeconds = _originalIdleTimeoutSeconds;
            PoolPurgeSettings.DefaultGlobalMinRetainCount = _originalMinRetainCount;
            PoolPurgeSettings.DefaultGlobalWarmRetainCount = _originalWarmRetainCount;
            PoolPurgeSettings.DefaultGlobalMaxPoolSize = _originalMaxPoolSize;
            PoolPurgeSettings.DefaultGlobalBufferMultiplier = _originalBufferMultiplier;
            PoolPurgeSettings.DefaultGlobalRollingWindowSeconds = _originalRollingWindowSeconds;
            PoolPurgeSettings.DefaultGlobalHysteresisSeconds = _originalHysteresisSeconds;
            PoolPurgeSettings.DefaultGlobalSpikeThresholdMultiplier =
                _originalSpikeThresholdMultiplier;

            PoolPurgeSettings.ClearSettingsTypeConfigurations();

            _serializedSettings?.Dispose();
            _serializedSettings = null;
        }

        [Test]
        public void PoolPurgingFoldoutContainsEveryGlobalOption()
        {
            WGroupLayoutBuilder.ClearCache();
            WGroupLayout layout = WGroupLayoutBuilder.Build(_serializedSettings, "m_Script");
            Assert.That(
                layout.TryGetGroup(
                    UnityHelpersSettings.PoolPurgingFoldoutKey,
                    out WGroupDefinition group
                ),
                Is.True
            );
            Assert.That(
                group.PropertyPaths,
                Is.EquivalentTo(
                    new[]
                    {
                        nameof(UnityHelpersSettings._poolPurgingEnabled),
                        nameof(UnityHelpersSettings._poolIdleTimeoutSeconds),
                        nameof(UnityHelpersSettings._poolMinRetainCount),
                        nameof(UnityHelpersSettings._poolWarmRetainCount),
                        nameof(UnityHelpersSettings._poolMaxSize),
                        nameof(UnityHelpersSettings._poolBufferMultiplier),
                        nameof(UnityHelpersSettings._poolRollingWindowSeconds),
                        nameof(UnityHelpersSettings._poolHysteresisSeconds),
                        nameof(UnityHelpersSettings._poolSpikeThresholdMultiplier),
                    }
                )
            );
        }

        [Test]
        public void ApplyPoolPurgingSettingsGlobalEnabledTrueTransfersToRuntime()
        {
            SerializedProperty enabledProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolPurgingEnabled)
            );
            bool originalValue = enabledProperty.boolValue;

            try
            {
                enabledProperty.boolValue = true;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.IsTrue(
                    PoolPurgeSettings.GlobalEnabled,
                    "GlobalEnabled should be true after applying settings with _poolPurgingEnabled=true."
                );
            }
            finally
            {
                enabledProperty.boolValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsGlobalEnabledFalseTransfersToRuntime()
        {
            SerializedProperty enabledProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolPurgingEnabled)
            );
            bool originalValue = enabledProperty.boolValue;

            try
            {
                enabledProperty.boolValue = false;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.IsFalse(
                    PoolPurgeSettings.GlobalEnabled,
                    "GlobalEnabled should be false after applying settings with _poolPurgingEnabled=false."
                );
            }
            finally
            {
                enabledProperty.boolValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsIdleTimeoutSecondsTransfersToRuntime()
        {
            SerializedProperty timeoutProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolIdleTimeoutSeconds)
            );
            float originalValue = timeoutProperty.floatValue;
            float testValue = 600f;

            try
            {
                timeoutProperty.floatValue = testValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalIdleTimeoutSeconds,
                    Is.EqualTo(testValue),
                    "DefaultGlobalIdleTimeoutSeconds should match configured _poolIdleTimeoutSeconds."
                );
            }
            finally
            {
                timeoutProperty.floatValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsIdleTimeoutSecondsNegativeClampsToZero()
        {
            SerializedProperty timeoutProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolIdleTimeoutSeconds)
            );
            float originalValue = timeoutProperty.floatValue;

            try
            {
                timeoutProperty.floatValue = -50f;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalIdleTimeoutSeconds,
                    Is.EqualTo(0f),
                    "DefaultGlobalIdleTimeoutSeconds should be clamped to 0 when negative."
                );
            }
            finally
            {
                timeoutProperty.floatValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsMinRetainCountTransfersToRuntime()
        {
            SerializedProperty minRetainProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolMinRetainCount)
            );
            int originalValue = minRetainProperty.intValue;
            int testValue = 5;

            try
            {
                minRetainProperty.intValue = testValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalMinRetainCount,
                    Is.EqualTo(testValue),
                    "DefaultGlobalMinRetainCount should match configured _poolMinRetainCount."
                );
            }
            finally
            {
                minRetainProperty.intValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsMinRetainCountNegativeClampsToZero()
        {
            SerializedProperty minRetainProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolMinRetainCount)
            );
            int originalValue = minRetainProperty.intValue;

            try
            {
                minRetainProperty.intValue = -10;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalMinRetainCount,
                    Is.EqualTo(0),
                    "DefaultGlobalMinRetainCount should be clamped to 0 when negative."
                );
            }
            finally
            {
                minRetainProperty.intValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsWarmRetainCountTransfersToRuntime()
        {
            SerializedProperty warmRetainProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolWarmRetainCount)
            );
            int originalValue = warmRetainProperty.intValue;
            int testValue = 8;

            try
            {
                warmRetainProperty.intValue = testValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalWarmRetainCount,
                    Is.EqualTo(testValue),
                    "DefaultGlobalWarmRetainCount should match configured _poolWarmRetainCount."
                );
            }
            finally
            {
                warmRetainProperty.intValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsWarmRetainCountNegativeClampsToZero()
        {
            SerializedProperty warmRetainProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolWarmRetainCount)
            );
            int originalValue = warmRetainProperty.intValue;

            try
            {
                warmRetainProperty.intValue = -5;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalWarmRetainCount,
                    Is.EqualTo(0),
                    "DefaultGlobalWarmRetainCount should be clamped to 0 when negative."
                );
            }
            finally
            {
                warmRetainProperty.intValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsBufferMultiplierTransfersToRuntime()
        {
            SerializedProperty bufferProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolBufferMultiplier)
            );
            float originalValue = bufferProperty.floatValue;
            float testValue = 3.5f;

            try
            {
                bufferProperty.floatValue = testValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalBufferMultiplier,
                    Is.EqualTo(testValue),
                    "DefaultGlobalBufferMultiplier should match configured _poolBufferMultiplier."
                );
            }
            finally
            {
                bufferProperty.floatValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsBufferMultiplierBelowOneClampsToOne()
        {
            SerializedProperty bufferProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolBufferMultiplier)
            );
            float originalValue = bufferProperty.floatValue;

            try
            {
                bufferProperty.floatValue = 0.5f;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalBufferMultiplier,
                    Is.EqualTo(1f),
                    "DefaultGlobalBufferMultiplier should be clamped to 1 when below minimum."
                );
            }
            finally
            {
                bufferProperty.floatValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsRollingWindowSecondsTransfersToRuntime()
        {
            SerializedProperty windowProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolRollingWindowSeconds)
            );
            float originalValue = windowProperty.floatValue;
            float testValue = 450f;

            try
            {
                windowProperty.floatValue = testValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalRollingWindowSeconds,
                    Is.EqualTo(testValue),
                    "DefaultGlobalRollingWindowSeconds should match configured _poolRollingWindowSeconds."
                );
            }
            finally
            {
                windowProperty.floatValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsRollingWindowSecondsBelowOneClampsToOne()
        {
            SerializedProperty windowProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolRollingWindowSeconds)
            );
            float originalValue = windowProperty.floatValue;

            try
            {
                windowProperty.floatValue = 0.1f;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalRollingWindowSeconds,
                    Is.EqualTo(1f),
                    "DefaultGlobalRollingWindowSeconds should be clamped to 1 when below minimum."
                );
            }
            finally
            {
                windowProperty.floatValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsHysteresisSecondsTransfersToRuntime()
        {
            SerializedProperty hysteresisProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolHysteresisSeconds)
            );
            float originalValue = hysteresisProperty.floatValue;
            float testValue = 180f;

            try
            {
                hysteresisProperty.floatValue = testValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalHysteresisSeconds,
                    Is.EqualTo(testValue),
                    "DefaultGlobalHysteresisSeconds should match configured _poolHysteresisSeconds."
                );
            }
            finally
            {
                hysteresisProperty.floatValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsHysteresisSecondsNegativeClampsToZero()
        {
            SerializedProperty hysteresisProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolHysteresisSeconds)
            );
            float originalValue = hysteresisProperty.floatValue;

            try
            {
                hysteresisProperty.floatValue = -30f;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalHysteresisSeconds,
                    Is.EqualTo(0f),
                    "DefaultGlobalHysteresisSeconds should be clamped to 0 when negative."
                );
            }
            finally
            {
                hysteresisProperty.floatValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsSpikeThresholdMultiplierTransfersToRuntime()
        {
            SerializedProperty spikeProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolSpikeThresholdMultiplier)
            );
            float originalValue = spikeProperty.floatValue;
            float testValue = 4.0f;

            try
            {
                spikeProperty.floatValue = testValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalSpikeThresholdMultiplier,
                    Is.EqualTo(testValue),
                    "DefaultGlobalSpikeThresholdMultiplier should match configured _poolSpikeThresholdMultiplier."
                );
            }
            finally
            {
                spikeProperty.floatValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsSpikeThresholdMultiplierBelowOneClampsToOne()
        {
            SerializedProperty spikeProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolSpikeThresholdMultiplier)
            );
            float originalValue = spikeProperty.floatValue;

            try
            {
                spikeProperty.floatValue = 0.3f;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalSpikeThresholdMultiplier,
                    Is.EqualTo(1f),
                    "DefaultGlobalSpikeThresholdMultiplier should be clamped to 1 when below minimum."
                );
            }
            finally
            {
                spikeProperty.floatValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsAllGlobalSettingsTransferCorrectly()
        {
            SerializedProperty enabledProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolPurgingEnabled)
            );
            SerializedProperty timeoutProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolIdleTimeoutSeconds)
            );
            SerializedProperty minRetainProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolMinRetainCount)
            );
            SerializedProperty warmRetainProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolWarmRetainCount)
            );
            SerializedProperty bufferProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolBufferMultiplier)
            );
            SerializedProperty windowProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolRollingWindowSeconds)
            );
            SerializedProperty hysteresisProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolHysteresisSeconds)
            );
            SerializedProperty spikeProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolSpikeThresholdMultiplier)
            );

            bool originalEnabled = enabledProperty.boolValue;
            float originalTimeout = timeoutProperty.floatValue;
            int originalMinRetain = minRetainProperty.intValue;
            int originalWarmRetain = warmRetainProperty.intValue;
            float originalBuffer = bufferProperty.floatValue;
            float originalWindow = windowProperty.floatValue;
            float originalHysteresis = hysteresisProperty.floatValue;
            float originalSpike = spikeProperty.floatValue;

            bool testEnabled = true;
            float testTimeout = 720f;
            int testMinRetain = 3;
            int testWarmRetain = 7;
            float testBuffer = 2.75f;
            float testWindow = 400f;
            float testHysteresis = 90f;
            float testSpike = 3.5f;

            try
            {
                enabledProperty.boolValue = testEnabled;
                timeoutProperty.floatValue = testTimeout;
                minRetainProperty.intValue = testMinRetain;
                warmRetainProperty.intValue = testWarmRetain;
                bufferProperty.floatValue = testBuffer;
                windowProperty.floatValue = testWindow;
                hysteresisProperty.floatValue = testHysteresis;
                spikeProperty.floatValue = testSpike;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.GlobalEnabled,
                    Is.EqualTo(testEnabled),
                    "GlobalEnabled mismatch."
                );
                Assert.That(
                    PoolPurgeSettings.DefaultGlobalIdleTimeoutSeconds,
                    Is.EqualTo(testTimeout),
                    "IdleTimeoutSeconds mismatch."
                );
                Assert.That(
                    PoolPurgeSettings.DefaultGlobalMinRetainCount,
                    Is.EqualTo(testMinRetain),
                    "MinRetainCount mismatch."
                );
                Assert.That(
                    PoolPurgeSettings.DefaultGlobalWarmRetainCount,
                    Is.EqualTo(testWarmRetain),
                    "WarmRetainCount mismatch."
                );
                Assert.That(
                    PoolPurgeSettings.DefaultGlobalBufferMultiplier,
                    Is.EqualTo(testBuffer),
                    "BufferMultiplier mismatch."
                );
                Assert.That(
                    PoolPurgeSettings.DefaultGlobalRollingWindowSeconds,
                    Is.EqualTo(testWindow),
                    "RollingWindowSeconds mismatch."
                );
                Assert.That(
                    PoolPurgeSettings.DefaultGlobalHysteresisSeconds,
                    Is.EqualTo(testHysteresis),
                    "HysteresisSeconds mismatch."
                );
                Assert.That(
                    PoolPurgeSettings.DefaultGlobalSpikeThresholdMultiplier,
                    Is.EqualTo(testSpike),
                    "SpikeThresholdMultiplier mismatch."
                );
            }
            finally
            {
                enabledProperty.boolValue = originalEnabled;
                timeoutProperty.floatValue = originalTimeout;
                minRetainProperty.intValue = originalMinRetain;
                warmRetainProperty.intValue = originalWarmRetain;
                bufferProperty.floatValue = originalBuffer;
                windowProperty.floatValue = originalWindow;
                hysteresisProperty.floatValue = originalHysteresis;
                spikeProperty.floatValue = originalSpike;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsPerTypeConfigurationTransfersToRuntime()
        {
            SerializedProperty configurationsProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolTypeConfigurations)
            );
            int originalArraySize = configurationsProperty.arraySize;

            try
            {
                configurationsProperty.ClearArray();
                configurationsProperty.InsertArrayElementAtIndex(0);
                SerializedProperty element = configurationsProperty.GetArrayElementAtIndex(0);

                element.FindPropertyRelative(nameof(PoolTypeConfiguration._typeName)).stringValue =
                    "List<int>";
                element.FindPropertyRelative(nameof(PoolTypeConfiguration._enabled)).boolValue =
                    true;
                element
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._idleTimeoutSeconds))
                    .floatValue = 500f;
                element
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._minRetainCount))
                    .intValue = 10;
                element
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._warmRetainCount))
                    .intValue = 5;
                element
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._bufferMultiplier))
                    .floatValue = 1.5f;
                element
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._rollingWindowSeconds))
                    .floatValue = 200f;
                element
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._hysteresisSeconds))
                    .floatValue = 60f;
                element
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._spikeThresholdMultiplier))
                    .floatValue = 3.0f;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<
                    List<int>
                >();

                Assert.That(
                    options.IdleTimeoutSeconds,
                    Is.EqualTo(500f),
                    "Per-type IdleTimeoutSeconds should be applied."
                );
                Assert.That(
                    options.MinRetainCount,
                    Is.EqualTo(10),
                    "Per-type MinRetainCount should be applied."
                );
                Assert.That(
                    options.WarmRetainCount,
                    Is.EqualTo(5),
                    "Per-type WarmRetainCount should be applied."
                );
                Assert.That(
                    options.BufferMultiplier,
                    Is.EqualTo(1.5f),
                    "Per-type BufferMultiplier should be applied."
                );
                Assert.That(
                    options.RollingWindowSeconds,
                    Is.EqualTo(200f),
                    "Per-type RollingWindowSeconds should be applied."
                );
                Assert.That(
                    options.HysteresisSeconds,
                    Is.EqualTo(60f),
                    "Per-type HysteresisSeconds should be applied."
                );
                Assert.That(
                    options.SpikeThresholdMultiplier,
                    Is.EqualTo(3.0f),
                    "Per-type SpikeThresholdMultiplier should be applied."
                );
            }
            finally
            {
                configurationsProperty.ClearArray();
                for (int i = 0; i < originalArraySize; i++)
                {
                    configurationsProperty.InsertArrayElementAtIndex(i);
                }
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsPerTypeConfigurationDisabledDisablesType()
        {
            SerializedProperty configurationsProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolTypeConfigurations)
            );
            int originalArraySize = configurationsProperty.arraySize;

            try
            {
                configurationsProperty.ClearArray();
                configurationsProperty.InsertArrayElementAtIndex(0);
                SerializedProperty element = configurationsProperty.GetArrayElementAtIndex(0);

                element.FindPropertyRelative(nameof(PoolTypeConfiguration._typeName)).stringValue =
                    "Dictionary<string, int>";
                element.FindPropertyRelative(nameof(PoolTypeConfiguration._enabled)).boolValue =
                    false;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<
                    Dictionary<string, int>
                >();

                Assert.IsFalse(
                    options.Enabled,
                    "Per-type configuration with Enabled=false should result in Enabled=false."
                );
            }
            finally
            {
                configurationsProperty.ClearArray();
                for (int i = 0; i < originalArraySize; i++)
                {
                    configurationsProperty.InsertArrayElementAtIndex(i);
                }
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsOpenGenericConfigurationAppliesToClosedGenerics()
        {
            SerializedProperty configurationsProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolTypeConfigurations)
            );
            int originalArraySize = configurationsProperty.arraySize;

            try
            {
                configurationsProperty.ClearArray();
                configurationsProperty.InsertArrayElementAtIndex(0);
                SerializedProperty element = configurationsProperty.GetArrayElementAtIndex(0);

                element.FindPropertyRelative(nameof(PoolTypeConfiguration._typeName)).stringValue =
                    "HashSet<>";
                element.FindPropertyRelative(nameof(PoolTypeConfiguration._enabled)).boolValue =
                    true;
                element
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._idleTimeoutSeconds))
                    .floatValue = 250f;
                element
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._minRetainCount))
                    .intValue = 4;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                PoolPurgeEffectiveOptions optionsInt = PoolPurgeSettings.GetEffectiveOptions<
                    HashSet<int>
                >();
                PoolPurgeEffectiveOptions optionsString = PoolPurgeSettings.GetEffectiveOptions<
                    HashSet<string>
                >();

                Assert.That(
                    optionsInt.IdleTimeoutSeconds,
                    Is.EqualTo(250f),
                    "Open generic config should apply to HashSet<int>."
                );
                Assert.That(
                    optionsInt.MinRetainCount,
                    Is.EqualTo(4),
                    "Open generic MinRetainCount should apply to HashSet<int>."
                );
                Assert.That(
                    optionsString.IdleTimeoutSeconds,
                    Is.EqualTo(250f),
                    "Open generic config should apply to HashSet<string>."
                );
                Assert.That(
                    optionsString.MinRetainCount,
                    Is.EqualTo(4),
                    "Open generic MinRetainCount should apply to HashSet<string>."
                );
            }
            finally
            {
                configurationsProperty.ClearArray();
                for (int i = 0; i < originalArraySize; i++)
                {
                    configurationsProperty.InsertArrayElementAtIndex(i);
                }
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsClearsOldSettingsBasedConfigurations()
        {
            SerializedProperty configurationsProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolTypeConfigurations)
            );
            int originalArraySize = configurationsProperty.arraySize;

            try
            {
                configurationsProperty.ClearArray();
                configurationsProperty.InsertArrayElementAtIndex(0);
                SerializedProperty element = configurationsProperty.GetArrayElementAtIndex(0);
                element.FindPropertyRelative(nameof(PoolTypeConfiguration._typeName)).stringValue =
                    "Queue<int>";
                element.FindPropertyRelative(nameof(PoolTypeConfiguration._enabled)).boolValue =
                    true;
                element
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._idleTimeoutSeconds))
                    .floatValue = 999f;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                PoolPurgeEffectiveOptions firstOptions = PoolPurgeSettings.GetEffectiveOptions<
                    Queue<int>
                >();
                Assert.That(
                    firstOptions.IdleTimeoutSeconds,
                    Is.EqualTo(999f),
                    "First apply should set IdleTimeout to 999."
                );

                configurationsProperty.ClearArray();
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                PoolPurgeEffectiveOptions secondOptions = PoolPurgeSettings.GetEffectiveOptions<
                    Queue<int>
                >();

                Assert.That(
                    secondOptions.IdleTimeoutSeconds,
                    Is.Not.EqualTo(999f),
                    "After removing type configuration and reapplying, old settings-based config should be cleared."
                );
            }
            finally
            {
                configurationsProperty.ClearArray();
                for (int i = 0; i < originalArraySize; i++)
                {
                    configurationsProperty.InsertArrayElementAtIndex(i);
                }
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsConfigsHaveLowerPriorityThanProgrammaticApi()
        {
            SerializedProperty configurationsProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolTypeConfigurations)
            );
            int originalArraySize = configurationsProperty.arraySize;

            try
            {
                configurationsProperty.ClearArray();
                configurationsProperty.InsertArrayElementAtIndex(0);
                SerializedProperty element = configurationsProperty.GetArrayElementAtIndex(0);
                element.FindPropertyRelative(nameof(PoolTypeConfiguration._typeName)).stringValue =
                    "Stack<int>";
                element.FindPropertyRelative(nameof(PoolTypeConfiguration._enabled)).boolValue =
                    true;
                element
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._idleTimeoutSeconds))
                    .floatValue = 100f;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                PoolPurgeSettings.Configure<Stack<int>>(options =>
                {
                    options.IdleTimeoutSeconds = 777f;
                });

                PoolPurgeEffectiveOptions effectiveOptions = PoolPurgeSettings.GetEffectiveOptions<
                    Stack<int>
                >();

                Assert.That(
                    effectiveOptions.IdleTimeoutSeconds,
                    Is.EqualTo(777f),
                    "Programmatic API configuration should take precedence over settings-based configuration."
                );
            }
            finally
            {
                PoolPurgeSettings.ClearTypeConfigurations();

                configurationsProperty.ClearArray();
                for (int i = 0; i < originalArraySize; i++)
                {
                    configurationsProperty.InsertArrayElementAtIndex(i);
                }
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsMultiplePerTypeConfigurationsAllTransferCorrectly()
        {
            SerializedProperty configurationsProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolTypeConfigurations)
            );
            int originalArraySize = configurationsProperty.arraySize;

            try
            {
                configurationsProperty.ClearArray();

                configurationsProperty.InsertArrayElementAtIndex(0);
                SerializedProperty element1 = configurationsProperty.GetArrayElementAtIndex(0);
                element1.FindPropertyRelative(nameof(PoolTypeConfiguration._typeName)).stringValue =
                    "List<string>";
                element1.FindPropertyRelative(nameof(PoolTypeConfiguration._enabled)).boolValue =
                    true;
                element1
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._idleTimeoutSeconds))
                    .floatValue = 111f;

                configurationsProperty.InsertArrayElementAtIndex(1);
                SerializedProperty element2 = configurationsProperty.GetArrayElementAtIndex(1);
                element2.FindPropertyRelative(nameof(PoolTypeConfiguration._typeName)).stringValue =
                    "List<float>";
                element2.FindPropertyRelative(nameof(PoolTypeConfiguration._enabled)).boolValue =
                    true;
                element2
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._idleTimeoutSeconds))
                    .floatValue = 222f;

                configurationsProperty.InsertArrayElementAtIndex(2);
                SerializedProperty element3 = configurationsProperty.GetArrayElementAtIndex(2);
                element3.FindPropertyRelative(nameof(PoolTypeConfiguration._typeName)).stringValue =
                    "Queue<double>";
                element3.FindPropertyRelative(nameof(PoolTypeConfiguration._enabled)).boolValue =
                    true;
                element3
                    .FindPropertyRelative(nameof(PoolTypeConfiguration._idleTimeoutSeconds))
                    .floatValue = 333f;

                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                PoolPurgeEffectiveOptions optionsListString = PoolPurgeSettings.GetEffectiveOptions<
                    List<string>
                >();
                PoolPurgeEffectiveOptions optionsListFloat = PoolPurgeSettings.GetEffectiveOptions<
                    List<float>
                >();
                PoolPurgeEffectiveOptions optionsQueueDouble =
                    PoolPurgeSettings.GetEffectiveOptions<Queue<double>>();

                Assert.That(
                    optionsListString.IdleTimeoutSeconds,
                    Is.EqualTo(111f),
                    "List<string> should have IdleTimeout=111."
                );
                Assert.That(
                    optionsListFloat.IdleTimeoutSeconds,
                    Is.EqualTo(222f),
                    "List<float> should have IdleTimeout=222."
                );
                Assert.That(
                    optionsQueueDouble.IdleTimeoutSeconds,
                    Is.EqualTo(333f),
                    "Queue<double> should have IdleTimeout=333."
                );
            }
            finally
            {
                configurationsProperty.ClearArray();
                for (int i = 0; i < originalArraySize; i++)
                {
                    configurationsProperty.InsertArrayElementAtIndex(i);
                }
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsEmptyTypeNameSkipsGracefully()
        {
            SerializedProperty configurationsProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolTypeConfigurations)
            );
            int originalArraySize = configurationsProperty.arraySize;

            try
            {
                configurationsProperty.ClearArray();
                configurationsProperty.InsertArrayElementAtIndex(0);
                SerializedProperty element = configurationsProperty.GetArrayElementAtIndex(0);

                element.FindPropertyRelative(nameof(PoolTypeConfiguration._typeName)).stringValue =
                    "";
                element.FindPropertyRelative(nameof(PoolTypeConfiguration._enabled)).boolValue =
                    true;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                Assert.DoesNotThrow(
                    () => UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime(),
                    "ApplyPoolPurgingSettingsToRuntime should handle empty type names gracefully."
                );
            }
            finally
            {
                configurationsProperty.ClearArray();
                for (int i = 0; i < originalArraySize; i++)
                {
                    configurationsProperty.InsertArrayElementAtIndex(i);
                }
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsInvalidTypeNameSkipsGracefully()
        {
            SerializedProperty configurationsProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolTypeConfigurations)
            );
            int originalArraySize = configurationsProperty.arraySize;

            try
            {
                configurationsProperty.ClearArray();
                configurationsProperty.InsertArrayElementAtIndex(0);
                SerializedProperty element = configurationsProperty.GetArrayElementAtIndex(0);

                element.FindPropertyRelative(nameof(PoolTypeConfiguration._typeName)).stringValue =
                    "NonExistent.Fake.Type.That.Does.Not.Exist";
                element.FindPropertyRelative(nameof(PoolTypeConfiguration._enabled)).boolValue =
                    true;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                Assert.DoesNotThrow(
                    () => UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime(),
                    "ApplyPoolPurgingSettingsToRuntime should handle invalid type names gracefully."
                );
            }
            finally
            {
                configurationsProperty.ClearArray();
                for (int i = 0; i < originalArraySize; i++)
                {
                    configurationsProperty.InsertArrayElementAtIndex(i);
                }
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsMaxPoolSizeTransfersToRuntime()
        {
            SerializedProperty maxSizeProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolMaxSize)
            );
            int originalValue = maxSizeProperty.intValue;
            int testValue = 100;

            try
            {
                maxSizeProperty.intValue = testValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalMaxPoolSize,
                    Is.EqualTo(testValue),
                    "DefaultGlobalMaxPoolSize should match configured _poolMaxSize."
                );
            }
            finally
            {
                maxSizeProperty.intValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsMaxPoolSizeNegativeClampsToZero()
        {
            SerializedProperty maxSizeProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolMaxSize)
            );
            int originalValue = maxSizeProperty.intValue;

            try
            {
                maxSizeProperty.intValue = -50;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                Assert.That(
                    PoolPurgeSettings.DefaultGlobalMaxPoolSize,
                    Is.EqualTo(0),
                    "DefaultGlobalMaxPoolSize should be clamped to 0 when negative."
                );
            }
            finally
            {
                maxSizeProperty.intValue = originalValue;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ApplyPoolPurgingSettingsPerTypeMaxPoolSizeTransfersToRuntime()
        {
            SerializedProperty configurationsProperty = _serializedSettings.FindProperty(
                nameof(UnityHelpersSettings._poolTypeConfigurations)
            );
            int originalArraySize = configurationsProperty.arraySize;

            try
            {
                configurationsProperty.ClearArray();
                configurationsProperty.InsertArrayElementAtIndex(0);
                SerializedProperty element = configurationsProperty.GetArrayElementAtIndex(0);

                element.FindPropertyRelative(nameof(PoolTypeConfiguration._typeName)).stringValue =
                    "List<byte>";
                element.FindPropertyRelative(nameof(PoolTypeConfiguration._enabled)).boolValue =
                    true;
                element.FindPropertyRelative(nameof(PoolTypeConfiguration._maxPoolSize)).intValue =
                    50;
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.ApplyPoolPurgingSettingsToRuntime();

                PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<
                    List<byte>
                >();

                Assert.That(
                    options.MaxPoolSize,
                    Is.EqualTo(50),
                    "Per-type MaxPoolSize should be applied."
                );
            }
            finally
            {
                configurationsProperty.ClearArray();
                for (int i = 0; i < originalArraySize; i++)
                {
                    configurationsProperty.InsertArrayElementAtIndex(i);
                }
                _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
#endif
