// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using UnityEngine;
    using UnityEngine.Serialization;

    /// <summary>
    /// Serializable per-type configuration for pool purging behavior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class stores type-specific pool configuration that can be serialized in
    /// Unity project settings. It supports generic types via assembly-qualified type names.
    /// </para>
    /// <para>
    /// Configuration priority (highest to lowest):
    /// <list type="number">
    ///   <item><description>Programmatic API (PoolPurgeSettings.Configure)</description></item>
    ///   <item><description>Per-type configuration from settings</description></item>
    ///   <item><description>PoolPurgePolicyAttribute on the type</description></item>
    ///   <item><description>Global defaults from settings</description></item>
    ///   <item><description>Hardcoded defaults</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class PoolTypeConfiguration
    {
        /// <summary>
        /// Gets or sets the full type name including assembly.
        /// </summary>
        public string TypeName
        {
            get => _typeName ?? string.Empty;
            set => _typeName = value ?? string.Empty;
        }

        /// <summary>
        /// Gets or sets whether intelligent purging is enabled for this type.
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <summary>
        /// Gets or sets the idle timeout in seconds.
        /// </summary>
        public float IdleTimeoutSeconds
        {
            get => _idleTimeoutSeconds;
            set => _idleTimeoutSeconds = value < 0f ? 0f : value;
        }

        /// <summary>
        /// Gets or sets the minimum retain count.
        /// </summary>
        public int MinRetainCount
        {
            get => _minRetainCount;
            set => _minRetainCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Gets or sets the warm retain count for active pools.
        /// </summary>
        public int WarmRetainCount
        {
            get => _warmRetainCount;
            set => _warmRetainCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Gets or sets the maximum pool size.
        /// </summary>
        public int MaxPoolSize
        {
            get => _maxPoolSize;
            set => _maxPoolSize = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Gets or sets the buffer multiplier.
        /// </summary>
        public float BufferMultiplier
        {
            get => _bufferMultiplier;
            set => _bufferMultiplier = value < 1f ? 1f : value;
        }

        /// <summary>
        /// Gets or sets the rolling window duration in seconds.
        /// </summary>
        public float RollingWindowSeconds
        {
            get => _rollingWindowSeconds;
            set => _rollingWindowSeconds = value < 1f ? 1f : value;
        }

        /// <summary>
        /// Gets or sets the hysteresis duration in seconds.
        /// </summary>
        public float HysteresisSeconds
        {
            get => _hysteresisSeconds;
            set => _hysteresisSeconds = value < 0f ? 0f : value;
        }

        /// <summary>
        /// Gets or sets the spike threshold multiplier.
        /// </summary>
        public float SpikeThresholdMultiplier
        {
            get => _spikeThresholdMultiplier;
            set => _spikeThresholdMultiplier = value < 1f ? 1f : value;
        }

        /// <summary>
        /// Gets the resolved <see cref="Type"/> for this configuration.
        /// The result is cached for performance.
        /// </summary>
        /// <remarks>
        /// Uses <see cref="PoolTypeResolver"/> for type resolution, which supports
        /// simplified generic syntax like <c>List&lt;int&gt;</c> and open generics like <c>List&lt;&gt;</c>.
        /// </remarks>
        public Type ResolvedType
        {
            get
            {
                string currentTypeName = TypeName;
                if (
                    !_resolvedTypeCached
                    || !string.Equals(_cachedTypeName, currentTypeName, StringComparison.Ordinal)
                )
                {
                    _resolvedType = PoolTypeResolver.ResolveType(currentTypeName);
                    _cachedTypeName = currentTypeName;
                    _resolvedTypeCached = true;
                }

                return _resolvedType;
            }
        }

        /// <summary>
        /// Gets whether the configured type is an open generic type definition.
        /// </summary>
        /// <remarks>
        /// An open generic type definition is a type like <c>List&lt;&gt;</c> that can match
        /// any closed generic type like <c>List&lt;int&gt;</c>, <c>List&lt;string&gt;</c>, etc.
        /// </remarks>
        public bool IsOpenGeneric
        {
            get
            {
                Type type = ResolvedType;
                return type != null && type.IsGenericTypeDefinition;
            }
        }

        /// <summary>
        /// Type name in any supported format.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Supported formats include:
        /// <list type="bullet">
        ///   <item><description><c>System.Collections.Generic.List`1</c> - Open generic CLR syntax</description></item>
        ///   <item><description><c>System.Collections.Generic.List`1[[System.Int32]]</c> - Closed generic CLR syntax</description></item>
        ///   <item><description><c>List&lt;int&gt;</c> - Simplified closed generic</description></item>
        ///   <item><description><c>List&lt;&gt;</c> - Simplified open generic (matches any List)</description></item>
        ///   <item><description><c>Dictionary&lt;string, int&gt;</c> - Multiple type arguments</description></item>
        ///   <item><description><c>Dictionary&lt;,&gt;</c> - Open with multiple type arguments</description></item>
        ///   <item><description><c>List&lt;List&lt;int&gt;&gt;</c> - Nested generics</description></item>
        ///   <item><description><c>List&lt;List&lt;&gt;&gt;</c> - Nested with inner open</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        [FormerlySerializedAs("typeName")]
        [SerializeField]
        [Tooltip(
            "Type name. Supports: List<int>, List<>, Dictionary<string, int>, Dictionary<,>, List<List<int>>, System.Collections.Generic.List`1"
        )]
        internal string _typeName = string.Empty;

        /// <summary>
        /// Whether intelligent pool purging is enabled for this type.
        /// </summary>
        [FormerlySerializedAs("enabled")]
        [SerializeField]
        [Tooltip("Whether intelligent pool purging is enabled for this type.")]
        internal bool _enabled = true;

        /// <summary>
        /// Idle timeout in seconds before items become eligible for purging.
        /// A value of 0 or less disables idle-based purging.
        /// </summary>
        [FormerlySerializedAs("idleTimeoutSeconds")]
        [SerializeField]
        [Tooltip(
            "Idle timeout in seconds before items become eligible for purging. 0 disables idle-based purging."
        )]
        [Min(0f)]
        internal float _idleTimeoutSeconds = PoolPurgeSettings.DefaultIdleTimeoutSeconds;

        /// <summary>
        /// Minimum number of items to always retain in the pool during purge operations.
        /// This is the absolute floor - pools never purge below this.
        /// </summary>
        [FormerlySerializedAs("minRetainCount")]
        [SerializeField]
        [Tooltip(
            "Minimum number of items to always retain during purge operations. Absolute floor."
        )]
        [Min(0)]
        internal int _minRetainCount = PoolPurgeSettings.DefaultMinRetainCount;

        /// <summary>
        /// Warm retain count for active pools.
        /// Active pools (accessed within IdleTimeoutSeconds) keep this many items warm
        /// to avoid cold-start allocations.
        /// </summary>
        [SerializeField]
        [Tooltip(
            "Warm retain count for active pools. Active pools keep this many items warm to avoid cold-start allocations."
        )]
        [Min(0)]
        internal int _warmRetainCount = PoolPurgeSettings.DefaultWarmRetainCount;

        /// <summary>
        /// Maximum pool size. Items exceeding this limit will be purged.
        /// A value of 0 or less means unbounded.
        /// </summary>
        [FormerlySerializedAs("maxPoolSize")]
        [SerializeField]
        [Tooltip("Maximum pool size. 0 means unbounded.")]
        [Min(0)]
        internal int _maxPoolSize;

        /// <summary>
        /// Buffer multiplier for comfortable pool size calculation.
        /// Comfortable size = max(MinRetainCount, rollingHighWaterMark * BufferMultiplier).
        /// </summary>
        [FormerlySerializedAs("bufferMultiplier")]
        [SerializeField]
        [Tooltip(
            "Buffer multiplier for comfortable pool size calculation. Higher values retain more items."
        )]
        [Min(1f)]
        internal float _bufferMultiplier = PoolPurgeSettings.DefaultBufferMultiplier;

        /// <summary>
        /// Rolling window duration in seconds for high water mark tracking.
        /// </summary>
        [FormerlySerializedAs("rollingWindowSeconds")]
        [SerializeField]
        [Tooltip("Rolling window duration in seconds for high water mark tracking.")]
        [Min(1f)]
        internal float _rollingWindowSeconds = PoolPurgeSettings.DefaultRollingWindowSeconds;

        /// <summary>
        /// Hysteresis duration in seconds. Purging is suppressed for this duration after a usage spike.
        /// </summary>
        [FormerlySerializedAs("hysteresisSeconds")]
        [SerializeField]
        [Tooltip("Hysteresis duration in seconds. Purging is suppressed after a usage spike.")]
        [Min(0f)]
        internal float _hysteresisSeconds = PoolPurgeSettings.DefaultHysteresisSeconds;

        /// <summary>
        /// Spike threshold multiplier. A spike is detected when concurrent rentals exceed
        /// the rolling average by this factor.
        /// </summary>
        [FormerlySerializedAs("spikeThresholdMultiplier")]
        [SerializeField]
        [Tooltip(
            "Spike threshold multiplier. A spike is detected when concurrent rentals exceed the rolling average by this factor."
        )]
        [Min(1f)]
        internal float _spikeThresholdMultiplier =
            PoolPurgeSettings.DefaultSpikeThresholdMultiplier;

        private Type _resolvedType;
        private bool _resolvedTypeCached;
        private string _cachedTypeName;

        /// <summary>
        /// Creates a new pool type configuration with default values.
        /// </summary>
        public PoolTypeConfiguration() { }

        /// <summary>
        /// Creates a new pool type configuration for the specified type.
        /// </summary>
        /// <param name="typeName">The full type name including assembly.</param>
        public PoolTypeConfiguration(string typeName)
        {
            TypeName = typeName;
        }

        /// <summary>
        /// Creates a new pool type configuration from the specified type.
        /// </summary>
        /// <param name="type">The type to configure.</param>
        public PoolTypeConfiguration(Type type)
        {
            TypeName = type?.AssemblyQualifiedName ?? string.Empty;
        }

        /// <summary>
        /// Attempts to resolve the configured type name to a Type instance.
        /// </summary>
        /// <returns>The resolved Type, or null if the type could not be resolved.</returns>
        /// <remarks>
        /// This method uses <see cref="PoolTypeResolver.ResolveType"/> which supports:
        /// <list type="bullet">
        ///   <item><description>Assembly-qualified names</description></item>
        ///   <item><description>Open generic CLR syntax (<c>System.Collections.Generic.List`1</c>)</description></item>
        ///   <item><description>Closed generic CLR syntax (<c>System.Collections.Generic.List`1[[System.Int32]]</c>)</description></item>
        ///   <item><description>Simplified open generic syntax (<c>List&lt;&gt;</c>)</description></item>
        ///   <item><description>Simplified closed generic syntax (<c>List&lt;int&gt;</c>)</description></item>
        ///   <item><description>Nested generics (<c>List&lt;List&lt;int&gt;&gt;</c>)</description></item>
        /// </list>
        /// </remarks>
        public Type ResolveType()
        {
            return ResolvedType;
        }

        /// <summary>
        /// Checks if this configuration matches the specified concrete type.
        /// </summary>
        /// <param name="concreteType">The concrete type to check.</param>
        /// <returns>
        /// <c>true</c> if this configuration matches the type; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Matching rules:
        /// <list type="bullet">
        ///   <item><description>Exact type match always succeeds</description></item>
        ///   <item><description>Open generic definitions match any closed generic
        ///   (e.g., <c>List&lt;&gt;</c> matches <c>List&lt;int&gt;</c>)</description></item>
        ///   <item><description>Partially open generics match corresponding types
        ///   (e.g., <c>List&lt;List&lt;&gt;&gt;</c> matches <c>List&lt;List&lt;int&gt;&gt;</c>)</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public bool Matches(Type concreteType)
        {
            if (concreteType == null)
            {
                return false;
            }

            Type patternType = ResolvedType;
            if (patternType == null)
            {
                return false;
            }

            return PoolTypeResolver.TypeMatchesPattern(concreteType, patternType);
        }

        /// <summary>
        /// Gets the match priority for this configuration when matching against a concrete type.
        /// Lower values indicate higher priority (more specific match).
        /// </summary>
        /// <param name="concreteType">The concrete type being matched.</param>
        /// <returns>
        /// A priority value where:
        /// <list type="bullet">
        ///   <item><description>0 = exact match</description></item>
        ///   <item><description>1 = partially open generic (inner args open)</description></item>
        ///   <item><description>2 = fully open generic definition</description></item>
        ///   <item><description><see cref="int.MaxValue"/> = no match</description></item>
        /// </list>
        /// </returns>
        public int GetMatchPriority(Type concreteType)
        {
            if (concreteType == null)
            {
                return int.MaxValue;
            }

            Type patternType = ResolvedType;
            if (patternType == null)
            {
                return int.MaxValue;
            }

            return PoolTypeResolver.GetMatchPriority(concreteType, patternType);
        }

        /// <summary>
        /// Invalidates the cached resolved type, forcing re-resolution on next access.
        /// </summary>
        public void InvalidateCache()
        {
            _resolvedTypeCached = false;
            _resolvedType = null;
            _cachedTypeName = null;
        }

        /// <summary>
        /// Converts this configuration to a PoolPurgeTypeOptions instance.
        /// </summary>
        /// <returns>A PoolPurgeTypeOptions instance with the configured values.</returns>
        public PoolPurgeTypeOptions ToPoolPurgeTypeOptions()
        {
            return new PoolPurgeTypeOptions
            {
                Enabled = _enabled,
                IdleTimeoutSeconds = _idleTimeoutSeconds,
                MinRetainCount = _minRetainCount,
                WarmRetainCount = _warmRetainCount,
                BufferMultiplier = _bufferMultiplier,
                RollingWindowSeconds = _rollingWindowSeconds,
                HysteresisSeconds = _hysteresisSeconds,
                SpikeThresholdMultiplier = _spikeThresholdMultiplier,
                MaxPoolSize = _maxPoolSize,
            };
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"PoolTypeConfiguration(Type={_typeName}, Enabled={_enabled}, IdleTimeout={_idleTimeoutSeconds}s, "
                + $"MinRetain={_minRetainCount}, WarmRetain={_warmRetainCount}, MaxSize={_maxPoolSize}, Buffer={_bufferMultiplier})";
        }
    }
}
