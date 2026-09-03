// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tests.Runtime.Pool.TestTypes;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Tests for Type-Aware Default Policies feature.
    /// Verifies that common types receive sensible built-in defaults that can be overridden.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class TypeAwarePoliciesTests
    {
        private bool _wasMemoryPressureEnabled;

        [SetUp]
        public void SetUp()
        {
            PoolPurgeSettings.ResetToDefaults();
            PoolPurgeSettings.ReinitializeBuiltInDefaults();
            _wasMemoryPressureEnabled = MemoryPressureMonitor.Enabled;
            MemoryPressureMonitor.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            PoolPurgeSettings.ResetToDefaults();
            MemoryPressureMonitor.Enabled = _wasMemoryPressureEnabled;
        }

        [Test]
        public void BuiltInDefaultsAreInitializedLazily()
        {
            PoolPurgeSettings.ClearBuiltInTypeConfigurations();

            Assert.IsFalse(
                PoolPurgeSettings.BuiltInDefaultsInitialized,
                "Should not be initialized after clear"
            );

            // Trigger initialization via GetEffectiveOptions
            PoolPurgeSettings.GetEffectiveOptions<List<int>>();

            Assert.IsTrue(
                PoolPurgeSettings.BuiltInDefaultsInitialized,
                "Should be initialized after GetEffectiveOptions"
            );
        }

        [Test]
        public void ReinitializeBuiltInDefaultsClearsAndReinitializes()
        {
            PoolPurgeSettings.GetEffectiveOptions<List<int>>();
            Assert.IsTrue(PoolPurgeSettings.BuiltInDefaultsInitialized);

            PoolPurgeSettings.ClearBuiltInTypeConfigurations();
            Assert.IsFalse(PoolPurgeSettings.BuiltInDefaultsInitialized);

            PoolPurgeSettings.ReinitializeBuiltInDefaults();
            Assert.IsTrue(PoolPurgeSettings.BuiltInDefaultsInitialized);
        }

        private static IEnumerable<TestCaseData> ArrayTypeTestCases()
        {
            yield return new TestCaseData(typeof(int[])).SetName("IntArrayReceivesBuiltInDefaults");
            yield return new TestCaseData(typeof(string[])).SetName(
                "StringArrayReceivesBuiltInDefaults"
            );
            yield return new TestCaseData(typeof(int[,])).SetName(
                "MultidimensionalArrayReceivesBuiltInDefaults"
            );
            yield return new TestCaseData(typeof(int[][])).SetName(
                "JaggedArrayReceivesBuiltInDefaults"
            );
            yield return new TestCaseData(typeof(byte[])).SetName(
                "ByteArrayReceivesBuiltInDefaults"
            );
        }

        [Test]
        [TestCaseSource(nameof(ArrayTypeTestCases))]
        public void ArrayTypesReceiveBuiltInDefaults(Type arrayType)
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions(arrayType);

            Assert.AreEqual(
                PoolPurgeConfigurationSource.BuiltInDefaults,
                options.Source,
                $"{arrayType.Name} should use built-in defaults"
            );
            Assert.AreEqual(
                1.5f,
                options.BufferMultiplier,
                0.001f,
                $"{arrayType.Name} should have 1.5x buffer"
            );
        }

        [Test]
        public void ArraysHaveExpectedIdleTimeout()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<int[]>();

            Assert.AreEqual(
                180f,
                options.IdleTimeoutSeconds,
                0.001f,
                "Arrays should have 3 minute idle timeout"
            );
        }

        [Test]
        public void ArraysPurgeMoreAggressivelyThanCollections()
        {
            PoolPurgeEffectiveOptions arrayOptions = PoolPurgeSettings.GetEffectiveOptions<int[]>();
            PoolPurgeEffectiveOptions listOptions = PoolPurgeSettings.GetEffectiveOptions<
                List<int>
            >();

            Assert.Less(
                arrayOptions.BufferMultiplier,
                listOptions.BufferMultiplier,
                "Arrays should have smaller buffer multiplier (more aggressive purging)"
            );
        }

        [Test]
        public void StringBuilderReceivesBuiltInDefaults()
        {
            PoolPurgeEffectiveOptions options =
                PoolPurgeSettings.GetEffectiveOptions<StringBuilder>();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.BuiltInDefaults,
                options.Source,
                "StringBuilder should use built-in defaults"
            );
            Assert.AreEqual(
                120f,
                options.IdleTimeoutSeconds,
                0.001f,
                "StringBuilder should have 2 minute idle timeout"
            );
            Assert.AreEqual(1, options.MinRetainCount, "StringBuilder should have min retain of 1");
        }

        [Test]
        public void StringBuilderHasShorterTimeoutThanArrays()
        {
            PoolPurgeEffectiveOptions sbOptions =
                PoolPurgeSettings.GetEffectiveOptions<StringBuilder>();
            PoolPurgeEffectiveOptions arrayOptions =
                PoolPurgeSettings.GetEffectiveOptions<byte[]>();

            Assert.Less(
                sbOptions.IdleTimeoutSeconds,
                arrayOptions.IdleTimeoutSeconds,
                "StringBuilder should have shorter timeout than arrays"
            );
        }

        [Test]
        public void ListOfIntReceivesBuiltInDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<List<int>>();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.BuiltInDefaults,
                options.Source,
                "List<int> should use built-in defaults"
            );
            Assert.AreEqual(2, options.MinRetainCount, "List<> should have min retain of 2");
            Assert.AreEqual(2.0f, options.BufferMultiplier, 0.001f, "List<> should have 2x buffer");
        }

        [Test]
        public void ListOfStringReceivesBuiltInDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<
                List<string>
            >();

            Assert.AreEqual(PoolPurgeConfigurationSource.BuiltInDefaults, options.Source);
            Assert.AreEqual(2, options.MinRetainCount);
            Assert.AreEqual(2.0f, options.BufferMultiplier, 0.001f);
        }

        [Test]
        public void NestedListReceivesBuiltInDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<
                List<List<int>>
            >();

            Assert.AreEqual(PoolPurgeConfigurationSource.BuiltInDefaults, options.Source);
            Assert.AreEqual(2, options.MinRetainCount);
        }

        [Test]
        public void DictionaryReceivesBuiltInDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<
                Dictionary<string, int>
            >();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.BuiltInDefaults,
                options.Source,
                "Dictionary<,> should use built-in defaults"
            );
            Assert.AreEqual(2, options.MinRetainCount, "Dictionary<,> should have min retain of 2");
            Assert.AreEqual(
                2.0f,
                options.BufferMultiplier,
                0.001f,
                "Dictionary<,> should have 2x buffer"
            );
        }

        [Test]
        public void DictionaryWithComplexValueReceivesBuiltInDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<
                Dictionary<int, List<string>>
            >();

            Assert.AreEqual(PoolPurgeConfigurationSource.BuiltInDefaults, options.Source);
            Assert.AreEqual(2, options.MinRetainCount);
        }

        [Test]
        public void HashSetReceivesBuiltInDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<
                HashSet<int>
            >();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.BuiltInDefaults,
                options.Source,
                "HashSet<> should use built-in defaults"
            );
            Assert.AreEqual(2, options.MinRetainCount, "HashSet<> should have min retain of 2");
            Assert.AreEqual(
                2.0f,
                options.BufferMultiplier,
                0.001f,
                "HashSet<> should have 2x buffer"
            );
        }

        [Test]
        public void QueueReceivesBuiltInDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<Queue<int>>();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.BuiltInDefaults,
                options.Source,
                "Queue<> should use built-in defaults"
            );
            Assert.AreEqual(1, options.MinRetainCount, "Queue<> should have min retain of 1");
            Assert.AreEqual(
                1.5f,
                options.BufferMultiplier,
                0.001f,
                "Queue<> should have 1.5x buffer"
            );
        }

        [Test]
        public void StackReceivesBuiltInDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<Stack<int>>();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.BuiltInDefaults,
                options.Source,
                "Stack<> should use built-in defaults"
            );
            Assert.AreEqual(1, options.MinRetainCount, "Stack<> should have min retain of 1");
            Assert.AreEqual(
                1.5f,
                options.BufferMultiplier,
                0.001f,
                "Stack<> should have 1.5x buffer"
            );
        }

        [Test]
        public void LinkedListReceivesBuiltInDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<
                LinkedList<int>
            >();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.BuiltInDefaults,
                options.Source,
                "LinkedList<> should use built-in defaults"
            );
            Assert.AreEqual(1, options.MinRetainCount, "LinkedList<> should have min retain of 1");
            Assert.AreEqual(
                1.5f,
                options.BufferMultiplier,
                0.001f,
                "LinkedList<> should have 1.5x buffer"
            );
            Assert.AreEqual(
                180f,
                options.IdleTimeoutSeconds,
                0.001f,
                "LinkedList<> should have 3 minute timeout"
            );
        }

        [Test]
        public void SortedDictionaryReceivesBuiltInDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<
                SortedDictionary<int, string>
            >();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.BuiltInDefaults,
                options.Source,
                "SortedDictionary<,> should use built-in defaults"
            );
            Assert.AreEqual(
                1,
                options.MinRetainCount,
                "SortedDictionary<,> should have min retain of 1"
            );
            Assert.AreEqual(
                1.5f,
                options.BufferMultiplier,
                0.001f,
                "SortedDictionary<,> should have 1.5x buffer"
            );
        }

        [Test]
        public void SortedSetReceivesBuiltInDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<
                SortedSet<int>
            >();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.BuiltInDefaults,
                options.Source,
                "SortedSet<> should use built-in defaults"
            );
            Assert.AreEqual(1, options.MinRetainCount, "SortedSet<> should have min retain of 1");
            Assert.AreEqual(
                1.5f,
                options.BufferMultiplier,
                0.001f,
                "SortedSet<> should have 1.5x buffer"
            );
        }

        [Test]
        public void UserConfigurationOverridesBuiltInDefaults()
        {
            PoolPurgeSettings.Configure<List<int>>(options =>
            {
                options.MinRetainCount = 10;
                options.BufferMultiplier = 3.0f;
            });

            PoolPurgeEffectiveOptions effectiveOptions = PoolPurgeSettings.GetEffectiveOptions<
                List<int>
            >();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.TypeSpecific,
                effectiveOptions.Source,
                "User configuration should override built-in defaults"
            );
            Assert.AreEqual(10, effectiveOptions.MinRetainCount, "User min retain should be used");
            Assert.AreEqual(
                3.0f,
                effectiveOptions.BufferMultiplier,
                0.001f,
                "User buffer multiplier should be used"
            );
        }

        [Test]
        public void UserGenericConfigurationOverridesBuiltInDefaults()
        {
            PoolPurgeSettings.ConfigureGeneric(
                typeof(List<>),
                options =>
                {
                    options.MinRetainCount = 5;
                    options.IdleTimeoutSeconds = 600f;
                }
            );

            PoolPurgeEffectiveOptions effectiveOptions = PoolPurgeSettings.GetEffectiveOptions<
                List<string>
            >();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.GenericPattern,
                effectiveOptions.Source,
                "User generic configuration should override built-in defaults"
            );
            Assert.AreEqual(5, effectiveOptions.MinRetainCount);
            Assert.AreEqual(600f, effectiveOptions.IdleTimeoutSeconds, 0.001f);
        }

        [Test]
        public void SettingsConfigurationOverridesBuiltInDefaults()
        {
            PoolPurgeSettings.ConfigureFromSettings(
                typeof(StringBuilder),
                new PoolPurgeTypeOptions { MinRetainCount = 5, IdleTimeoutSeconds = 60f }
            );

            PoolPurgeEffectiveOptions effectiveOptions =
                PoolPurgeSettings.GetEffectiveOptions<StringBuilder>();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.UnityHelpersSettingsPerType,
                effectiveOptions.Source,
                "Settings configuration should override built-in defaults"
            );
            Assert.AreEqual(5, effectiveOptions.MinRetainCount);
            Assert.AreEqual(60f, effectiveOptions.IdleTimeoutSeconds, 0.001f);
        }

        [Test]
        public void AttributeOverridesBuiltInDefaults()
        {
            // TypeWithPurgePolicyAttribute has [PoolPurgePolicy] attribute
            PoolPurgeEffectiveOptions options =
                PoolPurgeSettings.GetEffectiveOptions<TypeWithPurgePolicyAttribute>();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.Attribute,
                options.Source,
                "Attribute should override built-in defaults"
            );
        }

        [Test]
        public void DisableOverridesBuiltInDefaults()
        {
            PoolPurgeSettings.Disable<List<int>>();

            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<List<int>>();

            Assert.AreEqual(PoolPurgeConfigurationSource.TypeDisabled, options.Source);
            Assert.IsFalse(options.Enabled, "Type should be disabled");
        }

        [Test]
        public void TypeWithoutBuiltInDefaultsUsesGlobalDefaults()
        {
            PoolPurgeEffectiveOptions options =
                PoolPurgeSettings.GetEffectiveOptions<CustomTypeWithoutDefaults>();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.GlobalDefaults,
                options.Source,
                "Custom type without built-in defaults should use global defaults"
            );
            Assert.AreEqual(PoolPurgeSettings.DefaultGlobalMinRetainCount, options.MinRetainCount);
            Assert.AreEqual(
                PoolPurgeSettings.DefaultGlobalBufferMultiplier,
                options.BufferMultiplier,
                0.001f
            );
        }

        [Test]
        public void IntTypeUsesGlobalDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<int>();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.GlobalDefaults,
                options.Source,
                "Primitive int should use global defaults"
            );
        }

        [Test]
        public void StringTypeUsesGlobalDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<string>();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.GlobalDefaults,
                options.Source,
                "String should use global defaults"
            );
        }

        [Test]
        public void PriorityHierarchyIsCorrect()
        {
            // Set up all levels of configuration for Dictionary<string, int>
            Type targetType = typeof(Dictionary<string, int>);

            // Built-in defaults are already configured
            PoolPurgeEffectiveOptions builtInOptions = PoolPurgeSettings.GetEffectiveOptions(
                targetType
            );
            Assert.AreEqual(PoolPurgeConfigurationSource.BuiltInDefaults, builtInOptions.Source);

            // Settings-based generic should override built-in
            PoolPurgeSettings.ConfigureGenericFromSettings(
                typeof(Dictionary<,>),
                new PoolPurgeTypeOptions { MinRetainCount = 3 }
            );
            PoolPurgeEffectiveOptions settingsGenericOptions =
                PoolPurgeSettings.GetEffectiveOptions(targetType);
            Assert.AreEqual(
                PoolPurgeConfigurationSource.UnityHelpersSettingsPerType,
                settingsGenericOptions.Source
            );

            // Programmatic generic should override settings
            PoolPurgeSettings.ConfigureGeneric(
                typeof(Dictionary<,>),
                options => options.MinRetainCount = 4
            );
            PoolPurgeEffectiveOptions programmaticGenericOptions =
                PoolPurgeSettings.GetEffectiveOptions(targetType);
            Assert.AreEqual(
                PoolPurgeConfigurationSource.GenericPattern,
                programmaticGenericOptions.Source
            );

            // Settings per-type should override generic
            PoolPurgeSettings.ConfigureFromSettings(
                targetType,
                new PoolPurgeTypeOptions { MinRetainCount = 5 }
            );
            PoolPurgeEffectiveOptions settingsTypeOptions = PoolPurgeSettings.GetEffectiveOptions(
                targetType
            );
            Assert.AreEqual(
                PoolPurgeConfigurationSource.UnityHelpersSettingsPerType,
                settingsTypeOptions.Source
            );

            // Programmatic per-type should override all
            PoolPurgeSettings.Configure<Dictionary<string, int>>(options =>
                options.MinRetainCount = 6
            );
            PoolPurgeEffectiveOptions programmaticTypeOptions =
                PoolPurgeSettings.GetEffectiveOptions(targetType);
            Assert.AreEqual(
                PoolPurgeConfigurationSource.TypeSpecific,
                programmaticTypeOptions.Source
            );
            Assert.AreEqual(6, programmaticTypeOptions.MinRetainCount);
        }

        [Test]
        public void ResetToDefaultsClearsUserConfigurationsButNotBuiltIn()
        {
            PoolPurgeSettings.Configure<List<int>>(options => options.MinRetainCount = 99);

            PoolPurgeSettings.ResetToDefaults();

            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<List<int>>();

            Assert.AreEqual(
                PoolPurgeConfigurationSource.BuiltInDefaults,
                options.Source,
                "After reset, List<int> should use built-in defaults (not global)"
            );
            Assert.AreEqual(2, options.MinRetainCount, "Should use built-in value");
        }

        [Test]
        public void ClearTypeConfigurationsClearsUserConfigurationsButNotBuiltIn()
        {
            PoolPurgeSettings.Configure<Dictionary<int, int>>(options =>
                options.MinRetainCount = 99
            );

            PoolPurgeSettings.ClearTypeConfigurations();

            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<
                Dictionary<int, int>
            >();

            Assert.AreEqual(PoolPurgeConfigurationSource.BuiltInDefaults, options.Source);
            Assert.AreEqual(2, options.MinRetainCount);
        }

        [Test]
        public void GetEffectiveOptionsWithNullTypeReturnsGlobalDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions(null);

            Assert.AreEqual(
                PoolPurgeConfigurationSource.GlobalDefaults,
                options.Source,
                "Null type should return global defaults"
            );
            Assert.AreEqual(PoolPurgeSettings.DefaultGlobalMinRetainCount, options.MinRetainCount);
            Assert.AreEqual(
                PoolPurgeSettings.DefaultGlobalBufferMultiplier,
                options.BufferMultiplier,
                0.001f
            );
            Assert.AreEqual(
                PoolPurgeSettings.DefaultGlobalIdleTimeoutSeconds,
                options.IdleTimeoutSeconds,
                0.001f
            );
        }

        [Test]
        public void BuiltInDefaultsUseGlobalDefaultsForUnspecifiedProperties()
        {
            // List<> built-in only specifies MinRetainCount and BufferMultiplier
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetEffectiveOptions<List<int>>();

            // IdleTimeoutSeconds should come from global defaults
            Assert.AreEqual(
                PoolPurgeSettings.DefaultGlobalIdleTimeoutSeconds,
                options.IdleTimeoutSeconds,
                0.001f,
                "Unspecified properties should use global defaults"
            );

            // WarmRetainCount should come from global defaults
            Assert.AreEqual(
                PoolPurgeSettings.DefaultGlobalWarmRetainCount,
                options.WarmRetainCount,
                "Unspecified properties should use global defaults"
            );
        }

        [Test]
        public void CommonCollectionsHaveHigherMinRetainThanLessCommonOnes()
        {
            PoolPurgeEffectiveOptions listOptions = PoolPurgeSettings.GetEffectiveOptions<
                List<int>
            >();
            PoolPurgeEffectiveOptions linkedListOptions = PoolPurgeSettings.GetEffectiveOptions<
                LinkedList<int>
            >();

            Assert.Greater(
                listOptions.MinRetainCount,
                linkedListOptions.MinRetainCount,
                "List<> (common) should have higher min retain than LinkedList<> (less common)"
            );
        }

#if !SINGLE_THREADED
        [Test]
        public void BuiltInDefaultsInitializationIsThreadSafe()
        {
            const int threadCount = 10;
            const int iterationsPerThread = 100;
            Exception capturedException = null;
            int successCount = 0;

            PoolPurgeSettings.ClearBuiltInTypeConfigurations();

            System.Threading.Thread[] threads = new System.Threading.Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                threads[t] = new System.Threading.Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < iterationsPerThread; i++)
                        {
                            PoolPurgeEffectiveOptions options =
                                PoolPurgeSettings.GetEffectiveOptions<List<int>>();
                            if (options.Source != PoolPurgeConfigurationSource.BuiltInDefaults)
                            {
                                throw new InvalidOperationException(
                                    $"Expected BuiltInDefaults, got {options.Source}"
                                );
                            }
                        }
                        System.Threading.Interlocked.Increment(ref successCount);
                    }
                    catch (Exception ex)
                    {
                        System.Threading.Interlocked.CompareExchange(
                            ref capturedException,
                            ex,
                            null
                        );
                    }
                });
            }

            for (int t = 0; t < threadCount; t++)
            {
                threads[t].Start();
            }

            for (int t = 0; t < threadCount; t++)
            {
                threads[t].Join(TimeSpan.FromSeconds(30));
            }

            Assert.IsTrue(capturedException == null, $"Exception in thread: {capturedException}");
            Assert.AreEqual(threadCount, successCount, "All threads should complete successfully");
        }
#endif

        [Test]
        public void BuiltInDefaultsEnumValueExists()
        {
            Assert.AreEqual(6, (int)PoolPurgeConfigurationSource.BuiltInDefaults);
        }
    }
}
