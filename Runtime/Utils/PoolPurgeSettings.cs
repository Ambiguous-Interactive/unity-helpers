// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    /// <summary>
    /// Global and type-specific configuration registry for intelligent pool purging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides a hierarchical configuration system for pool purging behavior:
    /// <list type="number">
    ///   <item><description>Specific type configuration (e.g., <c>List&lt;int&gt;</c>)</description></item>
    ///   <item><description>Generic type pattern configuration (e.g., <c>List&lt;&gt;</c> for any <c>List&lt;T&gt;</c>)</description></item>
    ///   <item><description>Global defaults</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// By default, intelligent purging is <strong>disabled</strong>. Enable it by setting
    /// <see cref="GlobalEnabled"/> to <c>true</c> or by configuring pool settings in the Unity Editor
    /// (Edit > Project Settings > Unity Helpers > Pool Purging).
    /// </para>
    /// <para>
    /// The retention model uses two settings:
    /// <list type="bullet">
    ///   <item><description><see cref="DefaultGlobalMinRetainCount"/> (default 0) - Absolute floor. Pools never purge below this.</description></item>
    ///   <item><description><see cref="DefaultGlobalWarmRetainCount"/> (default 2) - For active pools (accessed within IdleTimeoutSeconds), keep this many warm to avoid cold-start allocations.</description></item>
    /// </list>
    /// Effective floor = <c>max(MinRetainCount, isActive ? WarmRetainCount : 0)</c>
    /// </para>
    /// <example>
    /// <code><![CDATA[
    /// // Disable globally (one-liner opt-out)
    /// PoolPurgeSettings.DisableGlobally();
    ///
    /// // Configure type-specific settings
    /// PoolPurgeSettings.Configure<List<int>>(options => {
    ///     options.IdleTimeoutSeconds = 600f; // 10 minutes
    ///     options.MinRetainCount = 10;
    ///     options.WarmRetainCount = 5;
    /// });
    ///
    /// // Configure all List<T> types
    /// PoolPurgeSettings.ConfigureGeneric(typeof(List<>), options => {
    ///     options.IdleTimeoutSeconds = 300f;
    /// });
    ///
    /// // Disable for expensive objects
    /// PoolPurgeSettings.Disable<ExpensiveObject>();
    /// ]]></code>
    /// </example>
    /// </remarks>
    public static class PoolPurgeSettings
    {
        /// <summary>
        /// Default idle timeout in seconds when intelligent purging is enabled.
        /// Set to 5 minutes by default to be conservative and avoid GC churn.
        /// </summary>
        public const float DefaultIdleTimeoutSeconds = 300f;

        /// <summary>
        /// Default minimum retain count during purge operations.
        /// This is the absolute floor - pools never purge below this, ever.
        /// </summary>
        public const int DefaultMinRetainCount = 0;

        /// <summary>
        /// Default warm retain count for active pools.
        /// Active pools (accessed within IdleTimeoutSeconds) keep this many items warm
        /// to avoid cold-start allocations.
        /// </summary>
        public const int DefaultWarmRetainCount = 2;

        /// <summary>
        /// Default buffer multiplier for comfortable pool size calculation.
        /// The comfortable size is calculated as <c>max(effectiveMinRetain, rollingHighWaterMark * BufferMultiplier)</c>.
        /// </summary>
        public const float DefaultBufferMultiplier = 2.0f;

        /// <summary>
        /// Default rolling window duration in seconds for high water mark tracking.
        /// </summary>
        public const float DefaultRollingWindowSeconds = 300f;

        /// <summary>
        /// Default hysteresis duration in seconds.
        /// After a usage spike, purging is suppressed for this duration to prevent purge-allocate cycles.
        /// </summary>
        public const float DefaultHysteresisSeconds = 120f;

        /// <summary>
        /// Default spike threshold multiplier.
        /// A spike is detected when concurrent rentals exceed the rolling average by this factor.
        /// </summary>
        public const float DefaultSpikeThresholdMultiplier = 2.5f;

        /// <summary>
        /// Default maximum number of items to purge per operation.
        /// Limits GC pressure by spreading large purge operations across multiple calls.
        /// A value of 0 means unlimited (purge all eligible items in one operation).
        /// </summary>
        public const int DefaultMaxPurgesPerOperation = 10;

        /// <summary>
        /// Default maximum pool size (0 = unbounded).
        /// Pools exceeding this limit will have items purged.
        /// </summary>
        public const int DefaultMaxPoolSize = 0;

        /// <summary>
        /// Default Large Object Heap (LOH) threshold in bytes.
        /// Objects of this size or larger are allocated on the LOH and receive stricter purge policies.
        /// The .NET runtime uses 85,000 bytes as the LOH threshold.
        /// </summary>
        public const int DefaultLargeObjectThresholdBytes = 85000;

        /// <summary>
        /// Default buffer multiplier for large objects.
        /// Large objects use less buffer than regular objects to minimize LOH memory pressure.
        /// Default is 1.0 (no buffer) compared to 2.0 for regular objects.
        /// </summary>
        public const float DefaultLargeObjectBufferMultiplier = 1.0f;

        /// <summary>
        /// Default idle timeout multiplier for large objects.
        /// Large objects have shorter effective idle timeouts to be purged faster.
        /// Default is 0.5 (50% of normal timeout).
        /// </summary>
        public const float DefaultLargeObjectIdleTimeoutMultiplier = 0.5f;

        /// <summary>
        /// Default warm retain count for large objects.
        /// Large objects keep fewer warm items to minimize LOH memory usage.
        /// Default is 1 compared to 2 for regular objects.
        /// </summary>
        public const int DefaultLargeObjectWarmRetainCount = 1;

        private static int _globalEnabled = 0;
        private static int _purgeOnLowMemory = 1;
        private static int _purgeOnAppBackground = 1;
        private static int _purgeOnSceneUnload = 1;
        private static int _lifecycleHooksRegistered;
        private static float _defaultIdleTimeoutSeconds = DefaultIdleTimeoutSeconds;
        private static int _defaultMinRetainCount = DefaultMinRetainCount;
        private static int _defaultWarmRetainCount = DefaultWarmRetainCount;
        private static float _defaultBufferMultiplier = DefaultBufferMultiplier;
        private static float _defaultRollingWindowSeconds = DefaultRollingWindowSeconds;
        private static float _defaultHysteresisSeconds = DefaultHysteresisSeconds;
        private static float _defaultSpikeThresholdMultiplier = DefaultSpikeThresholdMultiplier;
        private static int _defaultMaxPurgesPerOperation = DefaultMaxPurgesPerOperation;
        private static int _defaultMaxPoolSize = DefaultMaxPoolSize;
        private static int _largeObjectThresholdBytes = DefaultLargeObjectThresholdBytes;
        private static float _largeObjectBufferMultiplier = DefaultLargeObjectBufferMultiplier;
        private static float _largeObjectIdleTimeoutMultiplier =
            DefaultLargeObjectIdleTimeoutMultiplier;
        private static int _largeObjectWarmRetainCount = DefaultLargeObjectWarmRetainCount;
        private static int _sizeAwarePoliciesEnabled = 1;

        private static readonly object ConfigLock = new object();
        private static readonly Dictionary<Type, PoolPurgeTypeOptions> TypeConfigurations =
            new Dictionary<Type, PoolPurgeTypeOptions>();
        private static readonly Dictionary<Type, PoolPurgeTypeOptions> GenericTypeConfigurations =
            new Dictionary<Type, PoolPurgeTypeOptions>();
        private static readonly HashSet<Type> DisabledTypes = new HashSet<Type>();

        // Settings-based per-type configurations (lower priority than programmatic API)
        private static readonly Dictionary<Type, PoolPurgeTypeOptions> SettingsTypeConfigurations =
            new Dictionary<Type, PoolPurgeTypeOptions>();
        private static readonly Dictionary<
            Type,
            PoolPurgeTypeOptions
        > SettingsGenericTypeConfigurations = new Dictionary<Type, PoolPurgeTypeOptions>();
        private static readonly HashSet<Type> SettingsDisabledTypes = new HashSet<Type>();

        // Built-in type-aware defaults (lowest priority - applied before user configuration)
        private static readonly Dictionary<Type, PoolPurgeTypeOptions> BuiltInTypeConfigurations =
            new Dictionary<Type, PoolPurgeTypeOptions>();
        private static readonly Dictionary<
            Type,
            PoolPurgeTypeOptions
        > BuiltInGenericTypeConfigurations = new Dictionary<Type, PoolPurgeTypeOptions>();
        private static int _builtInDefaultsInitialized;

        // Cache for PoolPurgePolicyAttribute reflection results to avoid repeated reflection on the same type
        private static readonly Dictionary<
            Type,
            (bool HasAttribute, bool Enabled, PoolPurgeTypeOptions Options)
        > AttributeCache =
            new Dictionary<Type, (bool HasAttribute, bool Enabled, PoolPurgeTypeOptions Options)>();

        /// <summary>
        /// Gets or sets whether intelligent pool purging is globally enabled.
        /// Default is <c>false</c> (disabled). Enable via Unity Editor settings or by setting this property.
        /// </summary>
        public static bool GlobalEnabled
        {
            get => Volatile.Read(ref _globalEnabled) != 0;
            set => Volatile.Write(ref _globalEnabled, value ? 1 : 0);
        }

        /// <summary>
        /// Gets or sets the default idle timeout in seconds for pools without type-specific configuration.
        /// Items idle longer than this are eligible for purging when intelligent purging is enabled.
        /// </summary>
        public static float DefaultGlobalIdleTimeoutSeconds
        {
            get => Volatile.Read(ref _defaultIdleTimeoutSeconds);
            set => Volatile.Write(ref _defaultIdleTimeoutSeconds, value);
        }

        /// <summary>
        /// Gets or sets the default minimum retain count for pools without type-specific configuration.
        /// This is the absolute floor - purge operations will never reduce the pool below this count.
        /// </summary>
        public static int DefaultGlobalMinRetainCount
        {
            get => Volatile.Read(ref _defaultMinRetainCount);
            set => Volatile.Write(ref _defaultMinRetainCount, value);
        }

        /// <summary>
        /// Gets or sets the default warm retain count for active pools.
        /// Active pools (accessed within <see cref="DefaultGlobalIdleTimeoutSeconds"/>) keep this many items
        /// warm to avoid cold-start allocations. Idle pools purge to <see cref="DefaultGlobalMinRetainCount"/>.
        /// Effective floor = <c>max(MinRetainCount, isActive ? WarmRetainCount : 0)</c>.
        /// </summary>
        public static int DefaultGlobalWarmRetainCount
        {
            get => Volatile.Read(ref _defaultWarmRetainCount);
            set => Volatile.Write(ref _defaultWarmRetainCount, value);
        }

        /// <summary>
        /// Gets or sets the default buffer multiplier for comfortable pool size calculation.
        /// </summary>
        public static float DefaultGlobalBufferMultiplier
        {
            get => Volatile.Read(ref _defaultBufferMultiplier);
            set => Volatile.Write(ref _defaultBufferMultiplier, value);
        }

        /// <summary>
        /// Gets or sets the default rolling window duration in seconds for high water mark tracking.
        /// </summary>
        public static float DefaultGlobalRollingWindowSeconds
        {
            get => Volatile.Read(ref _defaultRollingWindowSeconds);
            set => Volatile.Write(ref _defaultRollingWindowSeconds, value);
        }

        /// <summary>
        /// Gets or sets the default hysteresis duration in seconds.
        /// </summary>
        public static float DefaultGlobalHysteresisSeconds
        {
            get => Volatile.Read(ref _defaultHysteresisSeconds);
            set => Volatile.Write(ref _defaultHysteresisSeconds, value);
        }

        /// <summary>
        /// Gets or sets the default spike threshold multiplier.
        /// </summary>
        public static float DefaultGlobalSpikeThresholdMultiplier
        {
            get => Volatile.Read(ref _defaultSpikeThresholdMultiplier);
            set => Volatile.Write(ref _defaultSpikeThresholdMultiplier, value);
        }

        /// <summary>
        /// Gets or sets the default maximum number of items to purge per operation.
        /// Limits GC pressure by spreading large purge operations across multiple calls.
        /// A value of 0 means unlimited (purge all eligible items in one operation).
        /// Negative values are normalized to 0 (unlimited).
        /// </summary>
        /// <remarks>
        /// <para>
        /// When set to a positive value, purge operations will process at most this many items
        /// before returning, setting a "pending purges" flag to continue on subsequent operations.
        /// This prevents GC spikes from bulk deallocation.
        /// </para>
        /// <para>
        /// The following operations bypass this limit and purge all eligible items immediately:
        /// <list type="bullet">
        ///   <item><description>Emergency purges via <see cref="PurgeReason.MemoryPressure"/> (triggered by <see cref="Application.lowMemory"/>)</description></item>
        ///   <item><description>Explicit <c>Purge(reason)</c> calls with a specified reason</description></item>
        ///   <item><description><c>ForceFullPurge()</c> method calls</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public static int DefaultGlobalMaxPurgesPerOperation
        {
            get => Volatile.Read(ref _defaultMaxPurgesPerOperation);
            set => Volatile.Write(ref _defaultMaxPurgesPerOperation, Math.Max(0, value));
        }

        /// <summary>
        /// Gets or sets the default maximum pool size.
        /// Pools exceeding this limit will have items purged.
        /// A value of 0 means unbounded (no size limit).
        /// Negative values are normalized to 0 (unbounded).
        /// </summary>
        public static int DefaultGlobalMaxPoolSize
        {
            get => Volatile.Read(ref _defaultMaxPoolSize);
            set => Volatile.Write(ref _defaultMaxPoolSize, Math.Max(0, value));
        }

        /// <summary>
        /// Gets or sets whether size-aware purge policies are enabled.
        /// When enabled, pools containing large objects (above <see cref="LargeObjectThresholdBytes"/>)
        /// automatically receive stricter purge policies.
        /// Default is <c>true</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Size-aware policies help manage Large Object Heap (LOH) memory pressure by:
        /// <list type="bullet">
        ///   <item><description>Using a smaller buffer multiplier for large objects (default 1.0x vs 2.0x)</description></item>
        ///   <item><description>Reducing idle timeout for large objects (default 50% of normal)</description></item>
        ///   <item><description>Keeping fewer warm items for large objects (default 1 vs 2)</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// Set this to <c>false</c> if you want to manage large object pools manually or if
        /// automatic size estimation causes issues.
        /// </para>
        /// </remarks>
        public static bool SizeAwarePoliciesEnabled
        {
            get => Volatile.Read(ref _sizeAwarePoliciesEnabled) != 0;
            set => Volatile.Write(ref _sizeAwarePoliciesEnabled, value ? 1 : 0);
        }

        /// <summary>
        /// Gets or sets the threshold in bytes above which objects are considered "large objects"
        /// and receive stricter purge policies.
        /// Default is 85,000 bytes (the .NET Large Object Heap threshold).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Objects of this size or larger are allocated on the .NET Large Object Heap (LOH).
        /// The LOH has different garbage collection characteristics:
        /// <list type="bullet">
        ///   <item><description>Only collected during Gen2 collections (expensive full GC)</description></item>
        ///   <item><description>Not compacted by default (causes fragmentation)</description></item>
        ///   <item><description>Retaining large pooled objects wastes significant memory</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// For pools containing large objects, the purge system automatically applies:
        /// <list type="bullet">
        ///   <item><description><see cref="LargeObjectBufferMultiplier"/> instead of <see cref="DefaultGlobalBufferMultiplier"/></description></item>
        ///   <item><description>Idle timeout multiplied by <see cref="LargeObjectIdleTimeoutMultiplier"/></description></item>
        ///   <item><description><see cref="LargeObjectWarmRetainCount"/> instead of <see cref="DefaultGlobalWarmRetainCount"/></description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public static int LargeObjectThresholdBytes
        {
            get => Volatile.Read(ref _largeObjectThresholdBytes);
            set => Volatile.Write(ref _largeObjectThresholdBytes, Math.Max(0, value));
        }

        /// <summary>
        /// Gets or sets the buffer multiplier used for large objects.
        /// Large objects use a smaller buffer to minimize LOH memory pressure.
        /// Default is 1.0 (no buffer above peak usage).
        /// </summary>
        /// <remarks>
        /// <para>
        /// This value replaces <see cref="DefaultGlobalBufferMultiplier"/> for pools containing
        /// objects that exceed <see cref="LargeObjectThresholdBytes"/>. A smaller buffer means
        /// pools are kept closer to their actual usage patterns, freeing LOH memory faster.
        /// </para>
        /// </remarks>
        public static float LargeObjectBufferMultiplier
        {
            get => Volatile.Read(ref _largeObjectBufferMultiplier);
            set => Volatile.Write(ref _largeObjectBufferMultiplier, Math.Max(0f, value));
        }

        /// <summary>
        /// Gets or sets the multiplier applied to idle timeout for large objects.
        /// Large objects have shorter effective idle timeouts to be purged faster.
        /// Default is 0.5 (50% of normal timeout).
        /// </summary>
        /// <remarks>
        /// <para>
        /// This multiplier is applied to <see cref="DefaultGlobalIdleTimeoutSeconds"/> (or the
        /// type-specific idle timeout) for pools containing objects that exceed
        /// <see cref="LargeObjectThresholdBytes"/>. A smaller multiplier means large objects
        /// are purged sooner after becoming idle.
        /// </para>
        /// <para>
        /// Example: If <see cref="DefaultGlobalIdleTimeoutSeconds"/> is 300 seconds and
        /// <see cref="LargeObjectIdleTimeoutMultiplier"/> is 0.5, large objects become eligible
        /// for purging after 150 seconds of idle time.
        /// </para>
        /// </remarks>
        public static float LargeObjectIdleTimeoutMultiplier
        {
            get => Volatile.Read(ref _largeObjectIdleTimeoutMultiplier);
            set =>
                Volatile.Write(
                    ref _largeObjectIdleTimeoutMultiplier,
                    Math.Max(0f, Math.Min(1f, value))
                );
        }

        /// <summary>
        /// Gets or sets the warm retain count for large object pools.
        /// Large object pools keep fewer warm items to minimize LOH memory usage.
        /// Default is 1 (compared to 2 for regular objects).
        /// </summary>
        /// <remarks>
        /// <para>
        /// This value replaces <see cref="DefaultGlobalWarmRetainCount"/> for pools containing
        /// objects that exceed <see cref="LargeObjectThresholdBytes"/>. Keeping fewer warm items
        /// reduces LOH memory usage at the cost of potentially more allocations for bursty workloads.
        /// </para>
        /// </remarks>
        public static int LargeObjectWarmRetainCount
        {
            get => Volatile.Read(ref _largeObjectWarmRetainCount);
            set => Volatile.Write(ref _largeObjectWarmRetainCount, Math.Max(0, value));
        }

        /// <summary>
        /// Gets or sets whether pools should be purged when <see cref="Application.lowMemory"/> is triggered.
        /// When enabled, an emergency purge is performed that ignores hysteresis and purges to <see cref="DefaultGlobalMinRetainCount"/>.
        /// Default is <c>true</c>.
        /// </summary>
        public static bool PurgeOnLowMemory
        {
            get => Volatile.Read(ref _purgeOnLowMemory) != 0;
            set => Volatile.Write(ref _purgeOnLowMemory, value ? 1 : 0);
        }

        /// <summary>
        /// Gets or sets whether pools should be purged when the application loses focus (backgrounds).
        /// This is particularly useful on mobile platforms where backgrounded apps may be killed.
        /// When enabled, a normal purge is performed that respects hysteresis settings.
        /// Default is <c>true</c>.
        /// </summary>
        public static bool PurgeOnAppBackground
        {
            get => Volatile.Read(ref _purgeOnAppBackground) != 0;
            set => Volatile.Write(ref _purgeOnAppBackground, value ? 1 : 0);
        }

        /// <summary>
        /// Gets or sets whether pools should be purged when a scene is unloaded.
        /// When enabled, a purge check is triggered on all pools via <see cref="SceneManager.sceneUnloaded"/>.
        /// The purge respects hysteresis settings to avoid purge-allocate cycles during rapid scene transitions.
        /// Default is <c>true</c>.
        /// </summary>
        public static bool PurgeOnSceneUnload
        {
            get => Volatile.Read(ref _purgeOnSceneUnload) != 0;
            set => Volatile.Write(ref _purgeOnSceneUnload, value ? 1 : 0);
        }

        /// <summary>
        /// Configures intelligent purging options for a specific type.
        /// </summary>
        /// <typeparam name="T">The type to configure.</typeparam>
        /// <param name="configure">Action to configure the options.</param>
        /// <exception cref="ArgumentNullException">Thrown when configure is null.</exception>
        public static void Configure<T>(Action<PoolPurgeTypeOptions> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            Type type = typeof(T);
            PoolPurgeTypeOptions options = new PoolPurgeTypeOptions();
            configure(options);

            lock (ConfigLock)
            {
                TypeConfigurations[type] = options;
                DisabledTypes.Remove(type);
            }
        }

        /// <summary>
        /// Configures intelligent purging options for all types matching a generic type definition.
        /// </summary>
        /// <param name="genericTypeDefinition">The generic type definition (e.g., <c>typeof(List&lt;&gt;)</c>).</param>
        /// <param name="configure">Action to configure the options.</param>
        /// <exception cref="ArgumentNullException">Thrown when genericTypeDefinition or configure is null.</exception>
        /// <exception cref="ArgumentException">Thrown when genericTypeDefinition is not a generic type definition.</exception>
        public static void ConfigureGeneric(
            Type genericTypeDefinition,
            Action<PoolPurgeTypeOptions> configure
        )
        {
            if (genericTypeDefinition == null)
            {
                throw new ArgumentNullException(nameof(genericTypeDefinition));
            }

            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            if (!genericTypeDefinition.IsGenericTypeDefinition)
            {
                throw new ArgumentException(
                    "Type must be a generic type definition (e.g., typeof(List<>)).",
                    nameof(genericTypeDefinition)
                );
            }

            PoolPurgeTypeOptions options = new PoolPurgeTypeOptions();
            configure(options);

            lock (ConfigLock)
            {
                GenericTypeConfigurations[genericTypeDefinition] = options;
            }
        }

        /// <summary>
        /// Disables intelligent purging for a specific type.
        /// </summary>
        /// <typeparam name="T">The type to disable purging for.</typeparam>
        public static void Disable<T>()
        {
            Type type = typeof(T);
            lock (ConfigLock)
            {
                DisabledTypes.Add(type);
                TypeConfigurations.Remove(type);
            }
        }

        /// <summary>
        /// Disables intelligent purging for a specific type.
        /// </summary>
        /// <param name="type">The type to disable purging for.</param>
        /// <exception cref="ArgumentNullException">Thrown when type is null.</exception>
        public static void Disable(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            lock (ConfigLock)
            {
                DisabledTypes.Add(type);
                TypeConfigurations.Remove(type);
            }
        }

        /// <summary>
        /// Enables intelligent purging for a specific type that was previously disabled.
        /// </summary>
        /// <typeparam name="T">The type to enable purging for.</typeparam>
        public static void Enable<T>()
        {
            Type type = typeof(T);
            lock (ConfigLock)
            {
                DisabledTypes.Remove(type);
            }
        }

        /// <summary>
        /// Enables intelligent purging for a specific type that was previously disabled.
        /// </summary>
        /// <param name="type">The type to enable purging for.</param>
        /// <exception cref="ArgumentNullException">Thrown when type is null.</exception>
        public static void Enable(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            lock (ConfigLock)
            {
                DisabledTypes.Remove(type);
            }
        }

        /// <summary>
        /// Removes all type-specific and generic type configurations.
        /// Does not affect global settings.
        /// </summary>
        public static void ClearTypeConfigurations()
        {
            lock (ConfigLock)
            {
                TypeConfigurations.Clear();
                GenericTypeConfigurations.Clear();
                DisabledTypes.Clear();
                AttributeCache.Clear();
            }
        }

        /// <summary>
        /// Clears all settings-based type configurations.
        /// This is typically called before reloading configurations from UnityHelpersSettings.
        /// </summary>
        public static void ClearSettingsTypeConfigurations()
        {
            lock (ConfigLock)
            {
                SettingsTypeConfigurations.Clear();
                SettingsGenericTypeConfigurations.Clear();
                SettingsDisabledTypes.Clear();
            }
        }

        /// <summary>
        /// Clears all built-in type-aware default configurations and resets the initialization flag.
        /// This is primarily used for testing.
        /// </summary>
        internal static void ClearBuiltInTypeConfigurations()
        {
            lock (ConfigLock)
            {
                BuiltInTypeConfigurations.Clear();
                BuiltInGenericTypeConfigurations.Clear();
            }

            Volatile.Write(ref _builtInDefaultsInitialized, 0);
        }

        /// <summary>
        /// Ensures the built-in type-aware defaults are initialized.
        /// This method is called automatically when getting effective options.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Built-in defaults provide sensible out-of-box behavior for common types:
        /// <list type="bullet">
        ///   <item><description>Arrays: Purge more aggressively (1.5x buffer, 3 minute idle timeout)</description></item>
        ///   <item><description>StringBuilder: Short-lived temporaries (2 minute idle timeout, 1 min retain)</description></item>
        ///   <item><description>List&lt;&gt;: Common collections kept warm (2x buffer, 2 min retain)</description></item>
        ///   <item><description>Dictionary&lt;,&gt;: Common collections kept warm (2x buffer, 2 min retain)</description></item>
        ///   <item><description>HashSet&lt;&gt;: Common collections kept warm (2x buffer, 2 min retain)</description></item>
        ///   <item><description>Queue&lt;&gt;, Stack&lt;&gt;: Common collections kept warm</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// These defaults have the lowest priority and are overridden by any user configuration.
        /// </para>
        /// </remarks>
        private static void EnsureBuiltInDefaultsInitialized()
        {
            if (Volatile.Read(ref _builtInDefaultsInitialized) != 0)
            {
                return;
            }

            lock (ConfigLock)
            {
                // Double-check inside lock
                if (Volatile.Read(ref _builtInDefaultsInitialized) != 0)
                {
                    return;
                }

                InitializeBuiltInDefaults();
                Volatile.Write(ref _builtInDefaultsInitialized, 1);
            }
        }

        /// <summary>
        /// Initializes the built-in type-aware defaults for common types.
        /// This method is called once during lazy initialization.
        /// </summary>
        private static void InitializeBuiltInDefaults()
        {
            /*
                Arrays - generally larger memory footprint, purge more aggressively
                Using typeof(Array) which will be checked in GetEffectiveOptions for all array types
            */
            BuiltInTypeConfigurations[typeof(Array)] = new PoolPurgeTypeOptions
            {
                BufferMultiplier = 1.5f,
                IdleTimeoutSeconds = 180f, // 3 minutes
            };

            // StringBuilder - often temporary, used for string building operations
            BuiltInTypeConfigurations[typeof(StringBuilder)] = new PoolPurgeTypeOptions
            {
                IdleTimeoutSeconds = 120f, // 2 minutes
                MinRetainCount = 1,
            };

            // List<> - very common, keep warm for performance
            BuiltInGenericTypeConfigurations[typeof(List<>)] = new PoolPurgeTypeOptions
            {
                MinRetainCount = 2,
                BufferMultiplier = 2.0f,
            };

            // Dictionary<,> - common, keep warm for performance
            BuiltInGenericTypeConfigurations[typeof(Dictionary<,>)] = new PoolPurgeTypeOptions
            {
                MinRetainCount = 2,
                BufferMultiplier = 2.0f,
            };

            // HashSet<> - common, keep warm for performance
            BuiltInGenericTypeConfigurations[typeof(HashSet<>)] = new PoolPurgeTypeOptions
            {
                MinRetainCount = 2,
                BufferMultiplier = 2.0f,
            };

            // Queue<> - keep a few warm
            BuiltInGenericTypeConfigurations[typeof(Queue<>)] = new PoolPurgeTypeOptions
            {
                MinRetainCount = 1,
                BufferMultiplier = 1.5f,
            };

            // Stack<> - keep a few warm
            BuiltInGenericTypeConfigurations[typeof(Stack<>)] = new PoolPurgeTypeOptions
            {
                MinRetainCount = 1,
                BufferMultiplier = 1.5f,
            };

            // LinkedList<> - less common, can purge more aggressively
            BuiltInGenericTypeConfigurations[typeof(LinkedList<>)] = new PoolPurgeTypeOptions
            {
                MinRetainCount = 1,
                BufferMultiplier = 1.5f,
                IdleTimeoutSeconds = 180f, // 3 minutes
            };

            // SortedDictionary<,> - less common, can purge more aggressively
            BuiltInGenericTypeConfigurations[typeof(SortedDictionary<,>)] = new PoolPurgeTypeOptions
            {
                MinRetainCount = 1,
                BufferMultiplier = 1.5f,
            };

            // SortedSet<> - less common, can purge more aggressively
            BuiltInGenericTypeConfigurations[typeof(SortedSet<>)] = new PoolPurgeTypeOptions
            {
                MinRetainCount = 1,
                BufferMultiplier = 1.5f,
            };
        }

        /// <summary>
        /// Forces re-initialization of built-in type-aware defaults.
        /// This is primarily used for testing.
        /// </summary>
        internal static void ReinitializeBuiltInDefaults()
        {
            ClearBuiltInTypeConfigurations();
            EnsureBuiltInDefaultsInitialized();
        }

        /// <summary>
        /// Gets whether built-in type-aware defaults have been initialized.
        /// </summary>
        internal static bool BuiltInDefaultsInitialized =>
            Volatile.Read(ref _builtInDefaultsInitialized) != 0;

        /// <summary>
        /// Configures settings-based per-type options.
        /// These have lower priority than programmatic API configurations.
        /// </summary>
        /// <param name="type">The type to configure.</param>
        /// <param name="options">The configuration options.</param>
        /// <exception cref="ArgumentNullException">Thrown when type or options is null.</exception>
        public static void ConfigureFromSettings(Type type, PoolPurgeTypeOptions options)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            lock (ConfigLock)
            {
                SettingsTypeConfigurations[type] = options;
                SettingsDisabledTypes.Remove(type);
            }
        }

        /// <summary>
        /// Configures settings-based per-type options for a generic type definition.
        /// These have lower priority than programmatic API configurations.
        /// </summary>
        /// <param name="genericTypeDefinition">The generic type definition (e.g., typeof(List&lt;&gt;)).</param>
        /// <param name="options">The configuration options.</param>
        /// <exception cref="ArgumentNullException">Thrown when genericTypeDefinition or options is null.</exception>
        /// <exception cref="ArgumentException">Thrown when genericTypeDefinition is not a generic type definition.</exception>
        public static void ConfigureGenericFromSettings(
            Type genericTypeDefinition,
            PoolPurgeTypeOptions options
        )
        {
            if (genericTypeDefinition == null)
            {
                throw new ArgumentNullException(nameof(genericTypeDefinition));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (!genericTypeDefinition.IsGenericTypeDefinition)
            {
                throw new ArgumentException(
                    "Type must be a generic type definition (e.g., typeof(List<>)).",
                    nameof(genericTypeDefinition)
                );
            }

            lock (ConfigLock)
            {
                SettingsGenericTypeConfigurations[genericTypeDefinition] = options;
            }
        }

        /// <summary>
        /// Disables a type from settings-based configuration.
        /// </summary>
        /// <param name="type">The type to disable.</param>
        /// <exception cref="ArgumentNullException">Thrown when type is null.</exception>
        public static void DisableFromSettings(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            lock (ConfigLock)
            {
                SettingsDisabledTypes.Add(type);
                SettingsTypeConfigurations.Remove(type);
            }
        }

        /// <summary>
        /// Resets all settings to their default values.
        /// </summary>
        public static void ResetToDefaults()
        {
            GlobalEnabled = false;
            DefaultGlobalIdleTimeoutSeconds = DefaultIdleTimeoutSeconds;
            DefaultGlobalMinRetainCount = DefaultMinRetainCount;
            DefaultGlobalWarmRetainCount = DefaultWarmRetainCount;
            DefaultGlobalMaxPoolSize = DefaultMaxPoolSize;
            DefaultGlobalBufferMultiplier = DefaultBufferMultiplier;
            DefaultGlobalRollingWindowSeconds = DefaultRollingWindowSeconds;
            DefaultGlobalHysteresisSeconds = DefaultHysteresisSeconds;
            DefaultGlobalSpikeThresholdMultiplier = DefaultSpikeThresholdMultiplier;
            DefaultGlobalMaxPurgesPerOperation = DefaultMaxPurgesPerOperation;
            SizeAwarePoliciesEnabled = true;
            LargeObjectThresholdBytes = DefaultLargeObjectThresholdBytes;
            LargeObjectBufferMultiplier = DefaultLargeObjectBufferMultiplier;
            LargeObjectIdleTimeoutMultiplier = DefaultLargeObjectIdleTimeoutMultiplier;
            LargeObjectWarmRetainCount = DefaultLargeObjectWarmRetainCount;
            PurgeOnLowMemory = true;
            PurgeOnAppBackground = true;
            PurgeOnSceneUnload = true;
            ClearTypeConfigurations();
            ClearSettingsTypeConfigurations();
        }

        /// <summary>
        /// Disables intelligent pool purging globally. This is a convenience method for easy opt-out.
        /// Equivalent to setting <c>GlobalEnabled = false</c>.
        /// </summary>
        /// <remarks>
        /// Call this method early in application initialization if you prefer to manage pool memory manually
        /// or if the automatic purging behavior is undesirable for your use case.
        /// </remarks>
        /// <example>
        /// <code>
        /// // In your initialization code (e.g., RuntimeInitializeOnLoadMethod)
        /// PoolPurgeSettings.DisableGlobally();
        /// </code>
        /// </example>
        public static void DisableGlobally()
        {
            GlobalEnabled = false;
        }

        /// <summary>
        /// Gets the effective configuration for a specific type, considering the hierarchy:
        /// specific type > generic type pattern > global defaults.
        /// </summary>
        /// <typeparam name="T">The type to get configuration for.</typeparam>
        /// <returns>The effective purge configuration for the type.</returns>
        public static PoolPurgeEffectiveOptions GetEffectiveOptions<T>()
        {
            return GetEffectiveOptions(typeof(T));
        }

        /// <summary>
        /// Gets the effective configuration for a specific type, considering the hierarchy:
        /// <list type="number">
        ///   <item><description>Programmatic API (Configure, Disable) - highest priority</description></item>
        ///   <item><description>UnityHelpersSettings per-type configuration</description></item>
        ///   <item><description>PoolPurgePolicyAttribute on the type</description></item>
        ///   <item><description>Generic type patterns by specificity (exact > inner open > outer open)</description></item>
        ///   <item><description>Built-in type-aware defaults (for arrays, StringBuilder, common collections)</description></item>
        ///   <item><description>Hardcoded global defaults - lowest priority</description></item>
        /// </list>
        /// </summary>
        /// <param name="type">The type to get configuration for.</param>
        /// <returns>The effective purge configuration for the type. Returns global defaults if type is null.</returns>
        /// <remarks>
        /// <para>
        /// If <paramref name="type"/> is null, this method returns global default options rather than throwing.
        /// This follows the defensive programming principle of handling all inputs gracefully.
        /// </para>
        /// <para>
        /// For generic types like <c>List&lt;List&lt;int&gt;&gt;</c>, patterns are matched in order of specificity:
        /// <list type="number">
        ///   <item><description><c>List&lt;List&lt;int&gt;&gt;</c> - exact match</description></item>
        ///   <item><description><c>List&lt;List&lt;&gt;&gt;</c> - inner generic open</description></item>
        ///   <item><description><c>List&lt;&gt;</c> - outer generic open</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// Built-in type-aware defaults provide sensible out-of-box behavior for common types:
        /// <list type="bullet">
        ///   <item><description>Arrays: Purge more aggressively (1.5x buffer, 3 minute idle timeout)</description></item>
        ///   <item><description>StringBuilder: Short-lived temporaries (2 minute idle timeout, 1 min retain)</description></item>
        ///   <item><description>List&lt;&gt;, Dictionary&lt;,&gt;, HashSet&lt;&gt;: Common collections kept warm (2x buffer, 2 min retain)</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public static PoolPurgeEffectiveOptions GetEffectiveOptions(Type type)
        {
            if (type == null)
            {
                // Defensive: return global defaults rather than throwing for null input
                return new PoolPurgeEffectiveOptions(
                    enabled: GlobalEnabled,
                    idleTimeoutSeconds: DefaultGlobalIdleTimeoutSeconds,
                    minRetainCount: DefaultGlobalMinRetainCount,
                    warmRetainCount: DefaultGlobalWarmRetainCount,
                    bufferMultiplier: DefaultGlobalBufferMultiplier,
                    rollingWindowSeconds: DefaultGlobalRollingWindowSeconds,
                    hysteresisSeconds: DefaultGlobalHysteresisSeconds,
                    spikeThresholdMultiplier: DefaultGlobalSpikeThresholdMultiplier,
                    maxPurgesPerOperation: DefaultGlobalMaxPurgesPerOperation,
                    maxPoolSize: DefaultGlobalMaxPoolSize,
                    source: PoolPurgeConfigurationSource.GlobalDefaults
                );
            }

            // Ensure built-in defaults are initialized
            EnsureBuiltInDefaultsInitialized();

            bool globalEnabled = GlobalEnabled;
            bool typeDisabled;
            bool settingsTypeDisabled;
            PoolPurgeTypeOptions typeOptions = null;
            PoolPurgeTypeOptions settingsTypeOptions = null;
            PoolPurgeTypeOptions builtInTypeOptions = null;
            PoolPurgeTypeOptions bestProgrammaticGenericOptions = null;
            PoolPurgeTypeOptions bestSettingsGenericOptions = null;
            PoolPurgeTypeOptions bestBuiltInGenericOptions = null;
            int bestProgrammaticGenericPriority = int.MaxValue;
            int bestSettingsGenericPriority = int.MaxValue;
            int bestBuiltInGenericPriority = int.MaxValue;

            lock (ConfigLock)
            {
                // Check programmatic disabled first (highest priority)
                typeDisabled = DisabledTypes.Contains(type);
                settingsTypeDisabled = SettingsDisabledTypes.Contains(type);

                if (!typeDisabled)
                {
                    // Programmatic type-specific configuration
                    typeOptions = TypeConfigurations.TryGetValue(
                        type,
                        out PoolPurgeTypeOptions programmaticTypeOptions
                    )
                        ? programmaticTypeOptions
                        : null;

                    // Settings-based type-specific configuration (lower priority)
                    settingsTypeOptions = SettingsTypeConfigurations.TryGetValue(
                        type,
                        out PoolPurgeTypeOptions settingsSpecificOptions
                    )
                        ? settingsSpecificOptions
                        : null;

                    // Built-in type-specific configuration (lowest priority)
                    builtInTypeOptions = BuiltInTypeConfigurations.TryGetValue(
                        type,
                        out PoolPurgeTypeOptions builtInSpecificOptions
                    )
                        ? builtInSpecificOptions
                        : null;

                    // For generic types, find the best matching pattern by specificity
                    if (type.IsGenericType)
                    {
                        // Get all possible patterns for this type in order of specificity
                        foreach (Type pattern in PoolTypeResolver.GetAllMatchingPatterns(type))
                        {
                            // Skip the exact type (already handled above)
                            if (pattern == type)
                            {
                                continue;
                            }

                            // Check programmatic generic configurations
                            if (
                                GenericTypeConfigurations.TryGetValue(
                                    pattern,
                                    out PoolPurgeTypeOptions programmaticOptions
                                )
                            )
                            {
                                int priority = PoolTypeResolver.GetMatchPriority(type, pattern);
                                if (priority < bestProgrammaticGenericPriority)
                                {
                                    bestProgrammaticGenericPriority = priority;
                                    bestProgrammaticGenericOptions = programmaticOptions;
                                }
                            }

                            // Check settings-based generic configurations
                            if (
                                SettingsGenericTypeConfigurations.TryGetValue(
                                    pattern,
                                    out PoolPurgeTypeOptions settingsOptions
                                )
                            )
                            {
                                int priority = PoolTypeResolver.GetMatchPriority(type, pattern);
                                if (priority < bestSettingsGenericPriority)
                                {
                                    bestSettingsGenericPriority = priority;
                                    bestSettingsGenericOptions = settingsOptions;
                                }
                            }

                            // Check built-in generic configurations (lowest priority)
                            if (
                                BuiltInGenericTypeConfigurations.TryGetValue(
                                    pattern,
                                    out PoolPurgeTypeOptions builtInOptions
                                )
                            )
                            {
                                int priority = PoolTypeResolver.GetMatchPriority(type, pattern);
                                if (priority < bestBuiltInGenericPriority)
                                {
                                    bestBuiltInGenericPriority = priority;
                                    bestBuiltInGenericOptions = builtInOptions;
                                }
                            }
                        }
                    }

                    // For arrays, check if we have a built-in configuration for Array
                    if (type.IsArray && builtInTypeOptions == null)
                    {
                        builtInTypeOptions = BuiltInTypeConfigurations.TryGetValue(
                            typeof(Array),
                            out PoolPurgeTypeOptions arrayOptions
                        )
                            ? arrayOptions
                            : null;
                    }
                }
            }

            // 1. Programmatic disabled (highest priority for disabling)
            if (typeDisabled)
            {
                return new PoolPurgeEffectiveOptions(
                    enabled: false,
                    idleTimeoutSeconds: 0f,
                    minRetainCount: 0,
                    warmRetainCount: 0,
                    bufferMultiplier: DefaultBufferMultiplier,
                    rollingWindowSeconds: DefaultRollingWindowSeconds,
                    hysteresisSeconds: DefaultHysteresisSeconds,
                    spikeThresholdMultiplier: DefaultSpikeThresholdMultiplier,
                    maxPurgesPerOperation: DefaultMaxPurgesPerOperation,
                    maxPoolSize: DefaultMaxPoolSize,
                    source: PoolPurgeConfigurationSource.TypeDisabled
                );
            }

            // 2. Programmatic type-specific configuration
            if (typeOptions != null)
            {
                return BuildEffectiveOptions(
                    typeOptions,
                    PoolPurgeConfigurationSource.TypeSpecific
                );
            }

            // 3. Settings-based per-type configuration
            if (settingsTypeOptions != null)
            {
                return BuildEffectiveOptions(
                    settingsTypeOptions,
                    PoolPurgeConfigurationSource.UnityHelpersSettingsPerType
                );
            }

            // 4. Settings-based disabled
            if (settingsTypeDisabled)
            {
                return new PoolPurgeEffectiveOptions(
                    enabled: false,
                    idleTimeoutSeconds: 0f,
                    minRetainCount: 0,
                    warmRetainCount: 0,
                    bufferMultiplier: DefaultBufferMultiplier,
                    rollingWindowSeconds: DefaultRollingWindowSeconds,
                    hysteresisSeconds: DefaultHysteresisSeconds,
                    spikeThresholdMultiplier: DefaultSpikeThresholdMultiplier,
                    maxPurgesPerOperation: DefaultMaxPurgesPerOperation,
                    maxPoolSize: DefaultMaxPoolSize,
                    source: PoolPurgeConfigurationSource.UnityHelpersSettingsPerType
                );
            }

            // 5. PoolPurgePolicyAttribute on the type
            bool hasTypeAttribute = HasPoolPurgePolicyAttribute(
                type,
                out bool attributeEnabled,
                out PoolPurgeTypeOptions attributeOptions
            );
            if (hasTypeAttribute)
            {
                if (!attributeEnabled)
                {
                    return new PoolPurgeEffectiveOptions(
                        enabled: false,
                        idleTimeoutSeconds: 0f,
                        minRetainCount: 0,
                        warmRetainCount: 0,
                        bufferMultiplier: DefaultBufferMultiplier,
                        rollingWindowSeconds: DefaultRollingWindowSeconds,
                        hysteresisSeconds: DefaultHysteresisSeconds,
                        spikeThresholdMultiplier: DefaultSpikeThresholdMultiplier,
                        maxPurgesPerOperation: DefaultMaxPurgesPerOperation,
                        maxPoolSize: DefaultMaxPoolSize,
                        source: PoolPurgeConfigurationSource.Attribute
                    );
                }

                return BuildEffectiveOptions(
                    attributeOptions,
                    PoolPurgeConfigurationSource.Attribute
                );
            }

            // 6. Programmatic generic pattern (best match by specificity)
            if (bestProgrammaticGenericOptions != null)
            {
                return BuildEffectiveOptions(
                    bestProgrammaticGenericOptions,
                    PoolPurgeConfigurationSource.GenericPattern
                );
            }

            // 7. Settings-based generic pattern (best match by specificity)
            if (bestSettingsGenericOptions != null)
            {
                return BuildEffectiveOptions(
                    bestSettingsGenericOptions,
                    PoolPurgeConfigurationSource.UnityHelpersSettingsPerType
                );
            }

            // 8. Built-in type-specific defaults (lowest priority tier)
            if (builtInTypeOptions != null)
            {
                return BuildEffectiveOptions(
                    builtInTypeOptions,
                    PoolPurgeConfigurationSource.BuiltInDefaults
                );
            }

            // 9. Built-in generic pattern defaults (lowest priority tier)
            if (bestBuiltInGenericOptions != null)
            {
                return BuildEffectiveOptions(
                    bestBuiltInGenericOptions,
                    PoolPurgeConfigurationSource.BuiltInDefaults
                );
            }

            // 10. Global defaults
            return GetGlobalDefaultEffectiveOptions();
        }

        /// <summary>
        /// Gets the global default effective options with no type-specific configuration.
        /// </summary>
        /// <returns>The global default purge configuration.</returns>
        private static PoolPurgeEffectiveOptions GetGlobalDefaultEffectiveOptions()
        {
            return new PoolPurgeEffectiveOptions(
                enabled: GlobalEnabled,
                idleTimeoutSeconds: DefaultGlobalIdleTimeoutSeconds,
                minRetainCount: DefaultGlobalMinRetainCount,
                warmRetainCount: DefaultGlobalWarmRetainCount,
                bufferMultiplier: DefaultGlobalBufferMultiplier,
                rollingWindowSeconds: DefaultGlobalRollingWindowSeconds,
                hysteresisSeconds: DefaultGlobalHysteresisSeconds,
                spikeThresholdMultiplier: DefaultGlobalSpikeThresholdMultiplier,
                maxPurgesPerOperation: DefaultGlobalMaxPurgesPerOperation,
                maxPoolSize: DefaultGlobalMaxPoolSize,
                source: PoolPurgeConfigurationSource.GlobalDefaults
            );
        }

        /// <summary>
        /// Checks if intelligent purging is enabled for a specific type.
        /// </summary>
        /// <typeparam name="T">The type to check.</typeparam>
        /// <returns><c>true</c> if intelligent purging is enabled; otherwise, <c>false</c>.</returns>
        public static bool IsEnabled<T>()
        {
            return GetEffectiveOptions<T>().Enabled;
        }

        /// <summary>
        /// Checks if intelligent purging is enabled for a specific type.
        /// </summary>
        /// <param name="type">The type to check.</param>
        /// <returns><c>true</c> if intelligent purging is enabled; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when type is null.</exception>
        public static bool IsEnabled(Type type)
        {
            return GetEffectiveOptions(type).Enabled;
        }

        /// <summary>
        /// Gets the effective configuration for a specific type, with size-aware policy adjustments.
        /// Large objects (above <see cref="LargeObjectThresholdBytes"/>) receive stricter policies.
        /// </summary>
        /// <typeparam name="T">The type to get configuration for.</typeparam>
        /// <returns>The effective purge configuration for the type, adjusted for size.</returns>
        /// <remarks>
        /// <para>
        /// When <see cref="SizeAwarePoliciesEnabled"/> is <c>true</c> and the type is estimated to
        /// exceed <see cref="LargeObjectThresholdBytes"/>, the returned options are adjusted:
        /// <list type="bullet">
        ///   <item><description><see cref="PoolPurgeEffectiveOptions.BufferMultiplier"/> is reduced to <see cref="LargeObjectBufferMultiplier"/></description></item>
        ///   <item><description><see cref="PoolPurgeEffectiveOptions.IdleTimeoutSeconds"/> is multiplied by <see cref="LargeObjectIdleTimeoutMultiplier"/></description></item>
        ///   <item><description><see cref="PoolPurgeEffectiveOptions.WarmRetainCount"/> is reduced to <see cref="LargeObjectWarmRetainCount"/></description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public static PoolPurgeEffectiveOptions GetSizeAwareEffectiveOptions<T>()
        {
            return GetSizeAwareEffectiveOptions(typeof(T));
        }

        /// <summary>
        /// Gets the effective configuration for a specific type, with size-aware policy adjustments.
        /// Large objects (above <see cref="LargeObjectThresholdBytes"/>) receive stricter policies.
        /// </summary>
        /// <param name="type">The type to get configuration for.</param>
        /// <returns>The effective purge configuration for the type, adjusted for size.</returns>
        /// <remarks>
        /// <para>
        /// If <paramref name="type"/> is <c>null</c>, returns the result of <see cref="GetEffectiveOptions(Type)"/>
        /// with a null type (which uses global defaults).
        /// </para>
        /// <para>
        /// When <see cref="SizeAwarePoliciesEnabled"/> is <c>true</c> and the type is estimated to
        /// exceed <see cref="LargeObjectThresholdBytes"/>, the returned options are adjusted:
        /// <list type="bullet">
        ///   <item><description><see cref="PoolPurgeEffectiveOptions.BufferMultiplier"/> is reduced to <see cref="LargeObjectBufferMultiplier"/></description></item>
        ///   <item><description><see cref="PoolPurgeEffectiveOptions.IdleTimeoutSeconds"/> is multiplied by <see cref="LargeObjectIdleTimeoutMultiplier"/></description></item>
        ///   <item><description><see cref="PoolPurgeEffectiveOptions.WarmRetainCount"/> is reduced to <see cref="LargeObjectWarmRetainCount"/></description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public static PoolPurgeEffectiveOptions GetSizeAwareEffectiveOptions(Type type)
        {
            if (type == null)
            {
                return GetGlobalDefaultEffectiveOptions();
            }

            PoolPurgeEffectiveOptions baseOptions = GetEffectiveOptions(type);

            // If size-aware policies are disabled, return base options
            if (!SizeAwarePoliciesEnabled)
            {
                return baseOptions;
            }

            // Check if this type is a large object
            int estimatedSize = PoolSizeEstimator.EstimateItemSizeBytes(type);
            int threshold = LargeObjectThresholdBytes;

            if (estimatedSize < threshold)
            {
                return baseOptions;
            }

            // Apply large object adjustments
            float adjustedIdleTimeout =
                baseOptions.IdleTimeoutSeconds * LargeObjectIdleTimeoutMultiplier;
            float adjustedBufferMultiplier = LargeObjectBufferMultiplier;
            int adjustedWarmRetainCount = LargeObjectWarmRetainCount;

            // Use the smaller of the configured and large-object values
            if (baseOptions.BufferMultiplier < adjustedBufferMultiplier)
            {
                adjustedBufferMultiplier = baseOptions.BufferMultiplier;
            }
            if (baseOptions.WarmRetainCount < adjustedWarmRetainCount)
            {
                adjustedWarmRetainCount = baseOptions.WarmRetainCount;
            }

            return new PoolPurgeEffectiveOptions(
                enabled: baseOptions.Enabled,
                idleTimeoutSeconds: adjustedIdleTimeout,
                minRetainCount: baseOptions.MinRetainCount,
                warmRetainCount: adjustedWarmRetainCount,
                bufferMultiplier: adjustedBufferMultiplier,
                rollingWindowSeconds: baseOptions.RollingWindowSeconds,
                hysteresisSeconds: baseOptions.HysteresisSeconds,
                spikeThresholdMultiplier: baseOptions.SpikeThresholdMultiplier,
                maxPurgesPerOperation: baseOptions.MaxPurgesPerOperation,
                maxPoolSize: baseOptions.MaxPoolSize,
                source: baseOptions.Source
            );
        }

        /// <summary>
        /// Checks if a type is considered a large object based on its estimated size.
        /// </summary>
        /// <typeparam name="T">The type to check.</typeparam>
        /// <returns><c>true</c> if the type's estimated size exceeds <see cref="LargeObjectThresholdBytes"/>; otherwise, <c>false</c>.</returns>
        public static bool IsLargeObject<T>()
        {
            return IsLargeObject(typeof(T));
        }

        /// <summary>
        /// Checks if a type is considered a large object based on its estimated size.
        /// </summary>
        /// <param name="type">The type to check.</param>
        /// <returns><c>true</c> if the type's estimated size exceeds <see cref="LargeObjectThresholdBytes"/>; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// Returns <c>false</c> if <paramref name="type"/> is <c>null</c>.
        /// </remarks>
        public static bool IsLargeObject(Type type)
        {
            if (type == null)
            {
                return false;
            }

            int estimatedSize = PoolSizeEstimator.EstimateItemSizeBytes(type);
            return LargeObjectThresholdBytes <= estimatedSize;
        }

        private static PoolPurgeEffectiveOptions BuildEffectiveOptions(
            PoolPurgeTypeOptions options,
            PoolPurgeConfigurationSource source
        )
        {
            bool enabled = options.Enabled ?? GlobalEnabled;
            float idleTimeout = options.IdleTimeoutSeconds ?? DefaultGlobalIdleTimeoutSeconds;
            int minRetain = options.MinRetainCount ?? DefaultGlobalMinRetainCount;
            int warmRetain = options.WarmRetainCount ?? DefaultGlobalWarmRetainCount;
            float buffer = options.BufferMultiplier ?? DefaultGlobalBufferMultiplier;
            float window = options.RollingWindowSeconds ?? DefaultGlobalRollingWindowSeconds;
            float hysteresis = options.HysteresisSeconds ?? DefaultGlobalHysteresisSeconds;
            float spike = options.SpikeThresholdMultiplier ?? DefaultGlobalSpikeThresholdMultiplier;
            int maxPurges = options.MaxPurgesPerOperation ?? DefaultGlobalMaxPurgesPerOperation;
            int maxPoolSize = options.MaxPoolSize ?? DefaultGlobalMaxPoolSize;

            return new PoolPurgeEffectiveOptions(
                enabled,
                idleTimeout,
                minRetain,
                warmRetain,
                buffer,
                window,
                hysteresis,
                spike,
                maxPurges,
                maxPoolSize,
                source
            );
        }

        private static bool HasPoolPurgePolicyAttribute(
            Type type,
            out bool enabled,
            out PoolPurgeTypeOptions attributeOptions
        )
        {
            // Check cache first to avoid repeated reflection on the same type
            lock (ConfigLock)
            {
                if (
                    AttributeCache.TryGetValue(
                        type,
                        out (bool HasAttribute, bool Enabled, PoolPurgeTypeOptions Options) cached
                    )
                )
                {
                    enabled = cached.Enabled;
                    attributeOptions = cached.Options;
                    return cached.HasAttribute;
                }
            }

            // Perform reflection (outside lock to minimize contention)
            PoolPurgePolicyAttribute attribute =
                type.GetCustomAttribute<PoolPurgePolicyAttribute>();

            bool hasAttribute;
            bool attributeEnabled;
            PoolPurgeTypeOptions options;

            if (attribute == null)
            {
                hasAttribute = false;
                attributeEnabled = true;
                options = null;
            }
            else
            {
                hasAttribute = true;
                attributeEnabled = attribute.Enabled;
                options = new PoolPurgeTypeOptions
                {
                    Enabled = attribute.Enabled,
                    IdleTimeoutSeconds = attribute.IdleTimeoutSeconds,
                    MinRetainCount = attribute.MinRetainCount,
                    WarmRetainCount = attribute.WarmRetainCount,
                };
            }

            // Cache the result
            lock (ConfigLock)
            {
                AttributeCache[type] = (hasAttribute, attributeEnabled, options);
            }

            enabled = attributeEnabled;
            attributeOptions = options;
            return hasAttribute;
        }

        /// <summary>
        /// Registers application lifecycle hooks for automatic pool purging.
        /// This method is automatically called during runtime initialization via <see cref="RuntimeInitializeLoadType.AfterAssembliesLoaded"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method registers handlers for the following Unity events:
        /// <list type="bullet">
        ///   <item><description><see cref="Application.lowMemory"/> - Triggers emergency purge when the system is low on memory</description></item>
        ///   <item><description><see cref="Application.focusChanged"/> - Triggers purge when the app loses focus (backgrounds on mobile)</description></item>
        ///   <item><description><see cref="SceneManager.sceneUnloaded"/> - Triggers purge when a scene is unloaded</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// The method is idempotent - calling it multiple times has no additional effect.
        /// </para>
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        internal static void RegisterLifecycleHooks()
        {
            if (Interlocked.Exchange(ref _lifecycleHooksRegistered, 1) != 0)
            {
                return;
            }

            Application.lowMemory += OnLowMemory;
            Application.focusChanged += OnFocusChanged;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        /// <summary>
        /// Unregisters application lifecycle hooks. Primarily used for testing.
        /// </summary>
        internal static void UnregisterLifecycleHooks()
        {
            if (Interlocked.Exchange(ref _lifecycleHooksRegistered, 0) == 0)
            {
                return;
            }

            Application.lowMemory -= OnLowMemory;
            Application.focusChanged -= OnFocusChanged;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private static void OnLowMemory()
        {
            if (!PurgeOnLowMemory)
            {
                return;
            }

            /*
                Use ForceFullPurgeAll to bypass MaxPurgesPerOperation limits during memory pressure.
                This ensures all eligible items are purged immediately when the system is low on memory.
            */
            GlobalPoolRegistry.ForceFullPurgeAll(
                respectHysteresis: false,
                reason: PurgeReason.MemoryPressure
            );
        }

        private static void OnFocusChanged(bool hasFocus)
        {
            if (hasFocus || !PurgeOnAppBackground)
            {
                return;
            }

            PurgeAllPools(respectHysteresis: true, reason: PurgeReason.AppBackgrounded);
        }

        /// <summary>
        /// Handles scene unloaded events by triggering a purge on all pools.
        /// </summary>
        /// <param name="scene">
        /// The scene that was unloaded. This parameter is unused because purge operations
        /// are global (affecting all pools) rather than scene-specific. The parameter is
        /// required by the <see cref="SceneManager.sceneUnloaded"/> delegate signature.
        /// </param>
        /// <remarks>
        /// <para>
        /// When a scene is unloaded, pooled objects that were primarily used in that scene
        /// may no longer be needed. This handler triggers a purge check on all pools to
        /// reclaim memory from items that are no longer actively used.
        /// </para>
        /// <para>
        /// The purge respects hysteresis settings to avoid purge-allocate cycles during
        /// rapid scene transitions. Set <see cref="PurgeOnSceneUnload"/> to <c>false</c>
        /// to disable this behavior.
        /// </para>
        /// </remarks>
        private static void OnSceneUnloaded(Scene scene)
        {
            /*
                Scene parameter unused: purge is global across all pools, not scene-specific.
                The parameter is required by the SceneManager.sceneUnloaded delegate signature.
            */
            _ = scene;

            if (!PurgeOnSceneUnload)
            {
                return;
            }

            PurgeAllPools(respectHysteresis: true, reason: PurgeReason.SceneUnloaded);
        }

        /// <summary>
        /// Purges all registered pools.
        /// </summary>
        /// <param name="respectHysteresis">
        /// If <c>true</c>, pools in their hysteresis period (after a usage spike) will skip purging.
        /// If <c>false</c>, all pools are purged regardless of hysteresis state (emergency purge).
        /// </param>
        /// <param name="reason">The reason for purging (used in callbacks and statistics).</param>
        /// <returns>The total number of items purged across all pools.</returns>
        public static int PurgeAllPools(bool respectHysteresis, PurgeReason reason)
        {
            return GlobalPoolRegistry.PurgeAll(respectHysteresis, reason);
        }

        /// <summary>
        /// Purges all registered pools with the <see cref="PurgeReason.Explicit"/> reason.
        /// </summary>
        /// <returns>The total number of items purged across all pools.</returns>
        public static int PurgeAllPools()
        {
            return PurgeAllPools(respectHysteresis: true, reason: PurgeReason.Explicit);
        }
    }

    /// <summary>
    /// Global registry for all pool instances, enabling cross-pool operations like purge-all
    /// and global memory budget enforcement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pools automatically register themselves when created and unregister when disposed.
    /// The registry uses weak references to avoid preventing pool garbage collection.
    /// </para>
    /// <para>
    /// The global memory budget feature prevents aggregate memory bloat by tracking the total
    /// number of pooled items across all pools and purging from least-recently-used pools when
    /// the budget is exceeded. Use <see cref="GlobalMaxPooledItems"/> to configure the maximum
    /// allowed items and <see cref="EnforceBudget"/> to trigger purging.
    /// </para>
    /// </remarks>
    public static class GlobalPoolRegistry
    {
        /// <summary>
        /// Default maximum number of pooled items across all pools (50,000).
        /// </summary>
        public const long DefaultGlobalMaxPooledItems = 50000;

        /// <summary>
        /// Default interval in seconds between automatic budget enforcement checks.
        /// </summary>
        public const float DefaultBudgetEnforcementIntervalSeconds = 30f;

        private static readonly object RegistryLock = new object();
        private static readonly List<WeakReference<IPurgeable>> RegisteredPools =
            new List<WeakReference<IPurgeable>>();
        private static readonly List<IPoolStatistics> BudgetEnforcementPools =
            new List<IPoolStatistics>();

        private static long _globalMaxPooledItems = DefaultGlobalMaxPooledItems;
        private static int _budgetEnforcementEnabled = 1;
        private static float _budgetEnforcementIntervalSeconds =
            DefaultBudgetEnforcementIntervalSeconds;
        private static float _lastBudgetEnforcementTime;

        private static readonly System.Diagnostics.Stopwatch RegistryStopwatch =
            System.Diagnostics.Stopwatch.StartNew();

        /// <summary>
        /// Gets or sets the global maximum number of pooled items across all pools.
        /// When the total exceeds this limit, <see cref="EnforceBudget"/> purges from least-recently-used pools.
        /// A value of 0 or less disables budget enforcement.
        /// Default is 50,000.
        /// </summary>
        public static long GlobalMaxPooledItems
        {
            get => Volatile.Read(ref _globalMaxPooledItems);
            set => Volatile.Write(ref _globalMaxPooledItems, value);
        }

        /// <summary>
        /// Gets or sets whether automatic budget enforcement is enabled.
        /// When enabled, <see cref="EnforceBudget"/> is called periodically during pool operations.
        /// Default is <c>true</c>.
        /// </summary>
        public static bool BudgetEnforcementEnabled
        {
            get => Volatile.Read(ref _budgetEnforcementEnabled) != 0;
            set => Volatile.Write(ref _budgetEnforcementEnabled, value ? 1 : 0);
        }

        /// <summary>
        /// Gets or sets the interval in seconds between automatic budget enforcement checks.
        /// Default is 30 seconds.
        /// </summary>
        public static float BudgetEnforcementIntervalSeconds
        {
            get => Volatile.Read(ref _budgetEnforcementIntervalSeconds);
            set =>
                Volatile.Write(
                    ref _budgetEnforcementIntervalSeconds,
                    0f < value ? value : DefaultBudgetEnforcementIntervalSeconds
                );
        }

        /// <summary>
        /// Gets the current total number of pooled items across all registered pools.
        /// </summary>
        /// <remarks>
        /// This property iterates through all registered pools to calculate the total.
        /// For pools that don't implement <see cref="IPoolStatistics"/>, their items are not counted.
        /// Dead pool references are cleaned up during iteration.
        /// </remarks>
        public static long CurrentTotalPooledItems
        {
            get
            {
                long total = 0;
                lock (RegistryLock)
                {
                    for (int i = RegisteredPools.Count - 1; 0 <= i; i--)
                    {
                        if (!RegisteredPools[i].TryGetTarget(out IPurgeable pool))
                        {
                            RegisteredPools.RemoveAt(i);
                            continue;
                        }

                        if (pool is IPoolStatistics stats)
                        {
                            total += stats.CurrentPooledCount;
                        }
                    }
                }
                return total;
            }
        }

        /// <summary>
        /// Gets the current number of registered pools (including potentially collected ones).
        /// This count may be higher than the actual number of live pools.
        /// </summary>
        public static int RegisteredCount
        {
            get
            {
                lock (RegistryLock)
                {
                    return RegisteredPools.Count;
                }
            }
        }

        /// <summary>
        /// Registers a pool with the global registry.
        /// </summary>
        /// <param name="pool">The pool to register.</param>
        public static void Register(IPurgeable pool)
        {
            if (pool == null)
            {
                return;
            }

            lock (RegistryLock)
            {
                RegisteredPools.Add(new WeakReference<IPurgeable>(pool));
            }
        }

        /// <summary>
        /// Unregisters a pool from the global registry.
        /// </summary>
        /// <param name="pool">The pool to unregister.</param>
        public static void Unregister(IPurgeable pool)
        {
            if (pool == null)
            {
                return;
            }

            lock (RegistryLock)
            {
                for (int i = RegisteredPools.Count - 1; 0 <= i; i--)
                {
                    if (
                        !RegisteredPools[i].TryGetTarget(out IPurgeable target)
                        || ReferenceEquals(target, pool)
                    )
                    {
                        RegisteredPools.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// Purges all registered pools.
        /// </summary>
        /// <param name="respectHysteresis">
        /// If <c>true</c>, pools in their hysteresis period will skip purging.
        /// If <c>false</c>, all pools are purged regardless of hysteresis state (emergency purge).
        /// </param>
        /// <param name="reason">The reason for purging.</param>
        /// <returns>The total number of items purged across all pools.</returns>
        public static int PurgeAll(bool respectHysteresis, PurgeReason reason)
        {
            int totalPurged = 0;
            bool ignoreHysteresis = !respectHysteresis;

            /*
                Copy pool references while holding lock, then purge outside lock
                to avoid holding lock during potentially slow purge operations
            */
            using PooledResource<List<IPurgeable>> pooled = Buffers<IPurgeable>.List.Get(
                out List<IPurgeable> poolsToPurge
            );

            lock (RegistryLock)
            {
                for (int i = RegisteredPools.Count - 1; 0 <= i; i--)
                {
                    if (RegisteredPools[i].TryGetTarget(out IPurgeable pool))
                    {
                        poolsToPurge.Add(pool);
                    }
                    else
                    {
                        // Clean up dead references
                        RegisteredPools.RemoveAt(i);
                    }
                }
            }

            foreach (IPurgeable poolToPurge in poolsToPurge)
            {
                try
                {
                    totalPurged += poolToPurge.Purge(reason, ignoreHysteresis);
                }
                catch (Exception e)
                {
                    // Swallow exceptions from individual pools to ensure all pools are attempted
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogWarning($"[PoolPurgeSettings] Failed to purge pool: {e.Message}");
#endif
                    _ = e;
                }
            }

            return totalPurged;
        }

        /// <summary>
        /// Forces a full purge on all registered pools, bypassing <c>MaxPurgesPerOperation</c> limits.
        /// </summary>
        /// <param name="respectHysteresis">
        /// If <c>true</c>, pools in their hysteresis period will skip purging.
        /// If <c>false</c>, all pools are purged regardless of hysteresis state (emergency purge).
        /// </param>
        /// <param name="reason">The reason for purging.</param>
        /// <returns>The total number of items purged across all pools.</returns>
        /// <remarks>
        /// This method is used for emergency purges (e.g., <see cref="PurgeReason.MemoryPressure"/>)
        /// where immediate memory reclamation is critical and gradual purging limits should be bypassed.
        /// </remarks>
        public static int ForceFullPurgeAll(bool respectHysteresis, PurgeReason reason)
        {
            int totalPurged = 0;
            bool ignoreHysteresis = !respectHysteresis;

            /*
                Copy pool references while holding lock, then purge outside lock
                to avoid holding lock during potentially slow purge operations
            */
            using PooledResource<List<IPurgeable>> pooled = Buffers<IPurgeable>.List.Get(
                out List<IPurgeable> poolsToPurge
            );

            lock (RegistryLock)
            {
                for (int i = RegisteredPools.Count - 1; 0 <= i; i--)
                {
                    if (RegisteredPools[i].TryGetTarget(out IPurgeable pool))
                    {
                        poolsToPurge.Add(pool);
                    }
                    else
                    {
                        // Clean up dead references
                        RegisteredPools.RemoveAt(i);
                    }
                }
            }

            foreach (IPurgeable poolToPurge in poolsToPurge)
            {
                try
                {
                    totalPurged += poolToPurge.ForceFullPurge(reason, ignoreHysteresis);
                }
                catch (Exception e)
                {
                    // Swallow exceptions from individual pools to ensure all pools are attempted
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogWarning(
                        $"[PoolPurgeSettings] Failed to force-purge pool: {e.Message}"
                    );
#endif
                    _ = e;
                }
            }

            return totalPurged;
        }

        /// <summary>
        /// Enforces the global pool budget by purging from least-recently-used pools.
        /// </summary>
        /// <returns>The total number of items purged across all pools.</returns>
        /// <remarks>
        /// <para>
        /// This method calculates the total pooled items across all registered pools and,
        /// if the total exceeds <see cref="GlobalMaxPooledItems"/>, purges items from the
        /// least-recently-used pools until the budget is satisfied.
        /// </para>
        /// <para>
        /// Pools are sorted by <see cref="IPoolStatistics.LastAccessTime"/> (ascending)
        /// and items are purged starting from the oldest pools. Each pool's
        /// <see cref="PoolOptions{T}.MinRetainCount"/> is respected.
        /// </para>
        /// </remarks>
        public static int EnforceBudget()
        {
            long maxItems = GlobalMaxPooledItems;
            if (maxItems <= 0)
            {
                return 0;
            }

            long currentTotal = 0;
            int totalPurged = 0;

            lock (RegistryLock)
            {
                BudgetEnforcementPools.Clear();

                for (int i = RegisteredPools.Count - 1; 0 <= i; i--)
                {
                    if (!RegisteredPools[i].TryGetTarget(out IPurgeable pool))
                    {
                        RegisteredPools.RemoveAt(i);
                        continue;
                    }

                    if (pool is IPoolStatistics stats)
                    {
                        BudgetEnforcementPools.Add(stats);
                        currentTotal += stats.CurrentPooledCount;
                    }
                }

                if (currentTotal <= maxItems)
                {
                    BudgetEnforcementPools.Clear();
                    return 0;
                }

                long excess = currentTotal - maxItems;

                SortPoolsByLastAccessTime(BudgetEnforcementPools);

                long remaining = excess;

                for (int i = 0; i < BudgetEnforcementPools.Count && 0 < remaining; i++)
                {
                    IPoolStatistics pool = BudgetEnforcementPools[i];
                    int poolCount = pool.CurrentPooledCount;
                    if (poolCount <= 0)
                    {
                        continue;
                    }

                    int toPurge = (int)System.Math.Min(remaining, poolCount);
                    if (toPurge <= 0)
                    {
                        continue;
                    }

                    try
                    {
                        int purged = pool.PurgeForBudget(toPurge);
                        totalPurged += purged;
                        remaining -= purged;
                    }
                    catch (Exception e)
                    {
                        // Swallow exceptions to continue with other pools
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.LogWarning(
                            $"[PoolPurgeSettings] Failed to purge pool for budget: {e.Message}"
                        );
#endif
                        _ = e;
                    }
                }

                BudgetEnforcementPools.Clear();
            }

            return totalPurged;
        }

        /// <summary>
        /// Tries to enforce the budget if the enforcement interval has elapsed.
        /// Called automatically during pool operations when <see cref="BudgetEnforcementEnabled"/> is true.
        /// </summary>
        /// <returns>The number of items purged, or 0 if enforcement was not needed or not due.</returns>
        public static int TryEnforceBudgetIfNeeded()
        {
            if (!BudgetEnforcementEnabled)
            {
                return 0;
            }

            long maxItems = GlobalMaxPooledItems;
            if (maxItems <= 0)
            {
                return 0;
            }

            float currentTime = (float)RegistryStopwatch.Elapsed.TotalSeconds;
            float interval = BudgetEnforcementIntervalSeconds;
            float lastEnforcement = Volatile.Read(ref _lastBudgetEnforcementTime);

            if (currentTime - lastEnforcement < interval)
            {
                return 0;
            }

            float original = Interlocked.CompareExchange(
                ref _lastBudgetEnforcementTime,
                currentTime,
                lastEnforcement
            );
            if (original != lastEnforcement)
            {
                return 0;
            }

            return EnforceBudget();
        }

        /// <summary>
        /// Gets a snapshot of global pool statistics.
        /// </summary>
        /// <returns>A snapshot of current global pool metrics.</returns>
        public static GlobalPoolStatistics GetStatistics()
        {
            int livePoolCount = 0;
            int statsPoolCount = 0;
            long totalItems = 0;
            float oldestAccessTime = float.MaxValue;
            float newestAccessTime = float.MinValue;

            lock (RegistryLock)
            {
                for (int i = RegisteredPools.Count - 1; 0 <= i; i--)
                {
                    if (!RegisteredPools[i].TryGetTarget(out IPurgeable pool))
                    {
                        RegisteredPools.RemoveAt(i);
                        continue;
                    }

                    livePoolCount++;

                    if (pool is IPoolStatistics stats)
                    {
                        statsPoolCount++;
                        totalItems += stats.CurrentPooledCount;
                        float accessTime = stats.LastAccessTime;
                        if (accessTime < oldestAccessTime)
                        {
                            oldestAccessTime = accessTime;
                        }
                        if (newestAccessTime < accessTime)
                        {
                            newestAccessTime = accessTime;
                        }
                    }
                }
            }

            if (statsPoolCount == 0)
            {
                oldestAccessTime = 0f;
                newestAccessTime = 0f;
            }

            return new GlobalPoolStatistics(
                livePoolCount,
                statsPoolCount,
                totalItems,
                GlobalMaxPooledItems,
                oldestAccessTime,
                newestAccessTime
            );
        }

        /// <summary>
        /// Resets all budget-related settings to their default values.
        /// </summary>
        public static void ResetBudgetSettings()
        {
            GlobalMaxPooledItems = DefaultGlobalMaxPooledItems;
            BudgetEnforcementEnabled = true;
            BudgetEnforcementIntervalSeconds = DefaultBudgetEnforcementIntervalSeconds;
            Volatile.Write(ref _lastBudgetEnforcementTime, 0f);
        }

        /// <summary>
        /// Clears all registered pools from the registry.
        /// This does not dispose or purge the pools, only removes their registrations.
        /// Primarily used for testing.
        /// </summary>
        internal static void Clear()
        {
            lock (RegistryLock)
            {
                RegisteredPools.Clear();
            }
            Volatile.Write(ref _lastBudgetEnforcementTime, 0f);
        }

        private static void SortPoolsByLastAccessTime(List<IPoolStatistics> pools)
        {
            int count = pools.Count;
            for (int i = 1; i < count; i++)
            {
                IPoolStatistics key = pools[i];
                float keyTime = key.LastAccessTime;
                int j = i - 1;

                while (0 <= j && keyTime < pools[j].LastAccessTime)
                {
                    pools[j + 1] = pools[j];
                    j--;
                }
                pools[j + 1] = key;
            }
        }

        /// <summary>
        /// Interface for pools that can be purged via the global registry.
        /// </summary>
        public interface IPurgeable
        {
            /// <summary>
            /// Purges items from the pool with the specified reason.
            /// </summary>
            /// <param name="reason">The reason for purging.</param>
            /// <param name="ignoreHysteresis">
            /// If <c>true</c>, bypasses the hysteresis check and purges immediately.
            /// Used for emergency purges (e.g., low memory situations).
            /// </param>
            /// <returns>The number of items purged.</returns>
            /// <remarks>
            /// This method purges all eligible items without respecting <c>MaxPurgesPerOperation</c> limits.
            /// It is treated as an explicit cleanup operation similar to <see cref="ForceFullPurge"/>.
            /// </remarks>
            int Purge(PurgeReason reason, bool ignoreHysteresis = false);

            /// <summary>
            /// Forces a full purge with a specified reason, bypassing <c>MaxPurgesPerOperation</c> limits.
            /// </summary>
            /// <param name="reason">The reason for purging (used in callbacks).</param>
            /// <param name="ignoreHysteresis">
            /// If <c>true</c>, bypasses the hysteresis check and purges immediately.
            /// Used for emergency purges (e.g., low memory situations).
            /// </param>
            /// <returns>The number of items purged.</returns>
            int ForceFullPurge(PurgeReason reason, bool ignoreHysteresis = false);
        }

        /// <summary>
        /// Interface for pools that provide statistics for global budget tracking.
        /// Extends <see cref="IPurgeable"/> with the ability to report current size and last access time.
        /// </summary>
        public interface IPoolStatistics : IPurgeable
        {
            /// <summary>
            /// Gets the current number of items in the pool.
            /// </summary>
            int CurrentPooledCount { get; }

            /// <summary>
            /// Gets the time (in seconds since pool creation or epoch) when the pool was last accessed.
            /// Used for LRU-based purging when the global budget is exceeded.
            /// </summary>
            float LastAccessTime { get; }

            /// <summary>
            /// Purges a specific number of items from the pool for budget enforcement.
            /// </summary>
            /// <param name="count">The maximum number of items to purge.</param>
            /// <returns>The actual number of items purged (may be less than requested if pool has fewer items).</returns>
            /// <remarks>
            /// This method is called by <see cref="GlobalPoolRegistry.EnforceBudget"/> to reduce pool size.
            /// It respects <see cref="PoolOptions{T}.MinRetainCount"/> and will not purge below that threshold.
            /// </remarks>
            int PurgeForBudget(int count);
        }
    }

    /// <summary>
    /// Immutable snapshot of global pool registry statistics.
    /// </summary>
    public readonly struct GlobalPoolStatistics
    {
        /// <summary>
        /// Gets the number of live (non-collected) registered pools.
        /// </summary>
        public int LivePoolCount { get; }

        /// <summary>
        /// Gets the number of pools that implement <see cref="GlobalPoolRegistry.IPoolStatistics"/>.
        /// </summary>
        public int StatisticsPoolCount { get; }

        /// <summary>
        /// Gets the total number of pooled items across all statistics-enabled pools.
        /// </summary>
        public long TotalPooledItems { get; }

        /// <summary>
        /// Gets the configured global maximum pooled items budget.
        /// </summary>
        public long GlobalMaxPooledItems { get; }

        /// <summary>
        /// Gets the last access time of the oldest pool (earliest <see cref="GlobalPoolRegistry.IPoolStatistics.LastAccessTime"/>).
        /// </summary>
        public float OldestPoolAccessTime { get; }

        /// <summary>
        /// Gets the last access time of the newest pool (latest <see cref="GlobalPoolRegistry.IPoolStatistics.LastAccessTime"/>).
        /// </summary>
        public float NewestPoolAccessTime { get; }

        /// <summary>
        /// Gets the ratio of current pooled items to the budget.
        /// A value greater than 1.0 indicates the budget is exceeded.
        /// </summary>
        public float BudgetUtilization =>
            0 < GlobalMaxPooledItems ? (float)TotalPooledItems / GlobalMaxPooledItems : 0f;

        /// <summary>
        /// Gets whether the current total exceeds the configured budget.
        /// </summary>
        public bool IsBudgetExceeded =>
            0 < GlobalMaxPooledItems && GlobalMaxPooledItems < TotalPooledItems;

        /// <summary>
        /// Creates a new global pool statistics snapshot.
        /// </summary>
        public GlobalPoolStatistics(
            int livePoolCount,
            int statisticsPoolCount,
            long totalPooledItems,
            long globalMaxPooledItems,
            float oldestPoolAccessTime,
            float newestPoolAccessTime
        )
        {
            LivePoolCount = livePoolCount;
            StatisticsPoolCount = statisticsPoolCount;
            TotalPooledItems = totalPooledItems;
            GlobalMaxPooledItems = globalMaxPooledItems;
            OldestPoolAccessTime = oldestPoolAccessTime;
            NewestPoolAccessTime = newestPoolAccessTime;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"GlobalPoolStatistics(LivePools={LivePoolCount}, StatsPools={StatisticsPoolCount}, "
                + $"TotalItems={TotalPooledItems}, MaxItems={GlobalMaxPooledItems}, "
                + $"Utilization={BudgetUtilization:P1}, Exceeded={IsBudgetExceeded})";
        }
    }

    /// <summary>
    /// Mutable options for configuring intelligent pool purging for a specific type.
    /// </summary>
    public sealed class PoolPurgeTypeOptions
    {
        /// <summary>
        /// Gets or sets whether intelligent purging is enabled for this type.
        /// If null, uses the global setting.
        /// </summary>
        public bool? Enabled { get; set; }

        /// <summary>
        /// Gets or sets the idle timeout in seconds.
        /// Items idle longer than this are eligible for purging.
        /// If null, uses the global default.
        /// </summary>
        public float? IdleTimeoutSeconds { get; set; }

        /// <summary>
        /// Gets or sets the minimum number of items to always retain during purge operations.
        /// This is the absolute floor - pools never purge below this, ever.
        /// If null, uses the global default.
        /// </summary>
        public int? MinRetainCount { get; set; }

        /// <summary>
        /// Gets or sets the warm retain count for active pools.
        /// Active pools (accessed within <see cref="IdleTimeoutSeconds"/>) keep this many items warm
        /// to avoid cold-start allocations. Idle pools purge to <see cref="MinRetainCount"/>.
        /// Effective floor = <c>max(MinRetainCount, isActive ? WarmRetainCount : 0)</c>.
        /// If null, uses the global default.
        /// </summary>
        public int? WarmRetainCount { get; set; }

        /// <summary>
        /// Gets or sets the buffer multiplier for comfortable pool size calculation.
        /// If null, uses the global default.
        /// </summary>
        public float? BufferMultiplier { get; set; }

        /// <summary>
        /// Gets or sets the rolling window duration in seconds for high water mark tracking.
        /// If null, uses the global default.
        /// </summary>
        public float? RollingWindowSeconds { get; set; }

        /// <summary>
        /// Gets or sets the hysteresis duration in seconds.
        /// After a usage spike, purging is suppressed for this duration.
        /// If null, uses the global default.
        /// </summary>
        public float? HysteresisSeconds { get; set; }

        /// <summary>
        /// Gets or sets the spike threshold multiplier.
        /// A spike is detected when concurrent rentals exceed the rolling average by this factor.
        /// If null, uses the global default.
        /// </summary>
        public float? SpikeThresholdMultiplier { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of items to purge per operation.
        /// Limits GC pressure by spreading large purge operations across multiple calls.
        /// A value of 0 means unlimited (purge all eligible items in one operation).
        /// If null, uses the global default.
        /// </summary>
        public int? MaxPurgesPerOperation { get; set; }

        /// <summary>
        /// Gets or sets the maximum pool size.
        /// Pools exceeding this limit will have items purged.
        /// A value of 0 means unbounded (no size limit).
        /// If null, uses the global default.
        /// </summary>
        public int? MaxPoolSize { get; set; }
    }

    /// <summary>
    /// Immutable effective configuration for intelligent pool purging.
    /// </summary>
    public readonly struct PoolPurgeEffectiveOptions
    {
        /// <summary>
        /// Gets whether intelligent purging is enabled.
        /// </summary>
        public bool Enabled { get; }

        /// <summary>
        /// Gets the idle timeout in seconds.
        /// </summary>
        public float IdleTimeoutSeconds { get; }

        /// <summary>
        /// Gets the minimum number of items to always retain.
        /// This is the absolute floor - pools never purge below this, ever.
        /// </summary>
        public int MinRetainCount { get; }

        /// <summary>
        /// Gets the warm retain count for active pools.
        /// Active pools keep this many items warm to avoid cold-start allocations.
        /// </summary>
        public int WarmRetainCount { get; }

        /// <summary>
        /// Gets the buffer multiplier for comfortable pool size calculation.
        /// </summary>
        public float BufferMultiplier { get; }

        /// <summary>
        /// Gets the rolling window duration in seconds for high water mark tracking.
        /// </summary>
        public float RollingWindowSeconds { get; }

        /// <summary>
        /// Gets the hysteresis duration in seconds.
        /// </summary>
        public float HysteresisSeconds { get; }

        /// <summary>
        /// Gets the spike threshold multiplier.
        /// </summary>
        public float SpikeThresholdMultiplier { get; }

        /// <summary>
        /// Gets the maximum number of items to purge per operation.
        /// A value of 0 means unlimited (purge all eligible items in one operation).
        /// </summary>
        public int MaxPurgesPerOperation { get; }

        /// <summary>
        /// Gets the maximum pool size.
        /// A value of 0 means unbounded (no size limit).
        /// </summary>
        public int MaxPoolSize { get; }

        /// <summary>
        /// Gets the source of this configuration.
        /// </summary>
        public PoolPurgeConfigurationSource Source { get; }

        /// <summary>
        /// Creates a new effective options instance.
        /// </summary>
        public PoolPurgeEffectiveOptions(
            bool enabled,
            float idleTimeoutSeconds,
            int minRetainCount,
            int warmRetainCount,
            float bufferMultiplier,
            float rollingWindowSeconds,
            float hysteresisSeconds,
            float spikeThresholdMultiplier,
            int maxPurgesPerOperation,
            int maxPoolSize,
            PoolPurgeConfigurationSource source
        )
        {
            Enabled = enabled;
            IdleTimeoutSeconds = idleTimeoutSeconds;
            MinRetainCount = minRetainCount;
            WarmRetainCount = warmRetainCount;
            BufferMultiplier = bufferMultiplier;
            RollingWindowSeconds = rollingWindowSeconds;
            HysteresisSeconds = hysteresisSeconds;
            SpikeThresholdMultiplier = spikeThresholdMultiplier;
            MaxPurgesPerOperation = maxPurgesPerOperation;
            MaxPoolSize = maxPoolSize;
            Source = source;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"PoolPurgeEffectiveOptions(Enabled={Enabled}, IdleTimeout={IdleTimeoutSeconds}s, "
                + $"MinRetain={MinRetainCount}, WarmRetain={WarmRetainCount}, Buffer={BufferMultiplier}, "
                + $"Window={RollingWindowSeconds}s, Hysteresis={HysteresisSeconds}s, "
                + $"SpikeThreshold={SpikeThresholdMultiplier}, MaxPurgesPerOp={MaxPurgesPerOperation}, "
                + $"MaxPoolSize={MaxPoolSize}, Source={Source})";
        }
    }

    /// <summary>
    /// Indicates the source of a pool purge configuration.
    /// </summary>
    public enum PoolPurgeConfigurationSource
    {
        /// <summary>
        /// Configuration comes from global defaults.
        /// </summary>
        GlobalDefaults = 0,

        /// <summary>
        /// Configuration comes from a type-specific setting.
        /// </summary>
        TypeSpecific = 1,

        /// <summary>
        /// Configuration comes from a generic type pattern.
        /// </summary>
        GenericPattern = 2,

        /// <summary>
        /// Configuration comes from a <see cref="PoolPurgePolicyAttribute"/> on the type.
        /// </summary>
        Attribute = 3,

        /// <summary>
        /// The type is explicitly disabled.
        /// </summary>
        TypeDisabled = 4,

        /// <summary>
        /// Configuration comes from UnityHelpersSettings per-type configuration.
        /// </summary>
        UnityHelpersSettingsPerType = 5,

        /// <summary>
        /// Configuration comes from built-in type-aware defaults.
        /// These are sensible defaults for common types (arrays, collections, StringBuilder)
        /// that are applied at the lowest priority level before global defaults.
        /// </summary>
        BuiltInDefaults = 6,
    }
}
