// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;

    /// <summary>
    /// Attribute to control intelligent pool purging behavior for a specific type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Apply this attribute to types that should opt-out of or customize intelligent pool purging.
    /// This attribute is checked when determining the effective purge configuration for a type.
    /// </para>
    /// <para>
    /// The attribute takes precedence over global settings but can be overridden by explicit
    /// type-specific configuration via <see cref="PoolPurgeSettings.Configure{T}"/> or
    /// <see cref="PoolPurgeSettings.Disable{T}"/>.
    /// </para>
    /// <example>
    /// <code><![CDATA[
    /// // Disable intelligent purging for this type
    /// [PoolPurgePolicy(enabled: false)]
    /// public class ExpensiveToCreateObject
    /// {
    ///     // ...
    /// }
    ///
    /// // Enable with default settings
    /// [PoolPurgePolicy(enabled: true)]
    /// public class TemporaryBuffer
    /// {
    ///     // ...
    /// }
    /// ]]></code>
    /// </example>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public sealed class PoolPurgePolicyAttribute : Attribute
    {
        /// <summary>
        /// Gets whether intelligent purging is enabled for this type.
        /// </summary>
        public bool Enabled { get; }

        /// <summary>
        /// Gets the custom idle timeout in seconds, or null to use defaults.
        /// </summary>
        public float? IdleTimeoutSeconds { get; }

        /// <summary>
        /// Gets the minimum number of items to retain, or null to use defaults.
        /// </summary>
        public int? MinRetainCount { get; }

        /// <summary>
        /// Gets the warm retain count for active pools, or null to use defaults.
        /// Active pools (accessed within <see cref="IdleTimeoutSeconds"/>) keep this many items warm
        /// to avoid cold-start allocations.
        /// </summary>
        public int? WarmRetainCount { get; }

        /// <summary>
        /// Creates a new pool purge policy attribute.
        /// </summary>
        /// <param name="enabled">Whether intelligent purging is enabled for this type.</param>
        /// <param name="warmRetainCount">
        /// Optional warm retain count for active pools. Active pools keep this many items warm
        /// to avoid cold-start allocations. If not specified, uses global defaults.
        /// </param>
        public PoolPurgePolicyAttribute(bool enabled, int warmRetainCount = -1)
        {
            Enabled = enabled;
            IdleTimeoutSeconds = null;
            MinRetainCount = null;
            WarmRetainCount = 0 <= warmRetainCount ? warmRetainCount : null;
        }

        /// <summary>
        /// Creates a new pool purge policy attribute with custom settings.
        /// </summary>
        /// <param name="enabled">Whether intelligent purging is enabled for this type.</param>
        /// <param name="idleTimeoutSeconds">Custom idle timeout in seconds.</param>
        /// <param name="warmRetainCount">
        /// Optional warm retain count for active pools. Active pools keep this many items warm
        /// to avoid cold-start allocations. If not specified, uses global defaults.
        /// </param>
        public PoolPurgePolicyAttribute(
            bool enabled,
            float idleTimeoutSeconds,
            int warmRetainCount = -1
        )
        {
            Enabled = enabled;
            IdleTimeoutSeconds = idleTimeoutSeconds;
            MinRetainCount = null;
            WarmRetainCount = 0 <= warmRetainCount ? warmRetainCount : null;
        }

        /// <summary>
        /// Creates a new pool purge policy attribute with custom settings.
        /// </summary>
        /// <param name="enabled">Whether intelligent purging is enabled for this type.</param>
        /// <param name="idleTimeoutSeconds">Custom idle timeout in seconds.</param>
        /// <param name="minRetainCount">Minimum number of items to retain.</param>
        /// <param name="warmRetainCount">
        /// Optional warm retain count for active pools. Active pools keep this many items warm
        /// to avoid cold-start allocations. If not specified, uses global defaults.
        /// </param>
        public PoolPurgePolicyAttribute(
            bool enabled,
            float idleTimeoutSeconds,
            int minRetainCount,
            int warmRetainCount = -1
        )
        {
            Enabled = enabled;
            IdleTimeoutSeconds = idleTimeoutSeconds;
            MinRetainCount = minRetainCount;
            WarmRetainCount = 0 <= warmRetainCount ? warmRetainCount : null;
        }
    }
}
