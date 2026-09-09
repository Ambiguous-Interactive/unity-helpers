// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if ZENJECT_PRESENT
namespace WallstopStudios.UnityHelpers.Integrations.Zenject
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Controls how the Zenject integration scans the scene for relational components.
    /// </summary>
    /// <example>
    /// <code>
    /// // In a custom installer, if you want to bind your own options instance:
    /// Container.BindInstance(new RelationalSceneAssignmentOptions(includeInactive: false));
    /// </code>
    /// </example>
    public readonly struct RelationalSceneAssignmentOptions
        : IEquatable<RelationalSceneAssignmentOptions>
    {
        /// <summary>
        /// Options used when no explicit configuration is supplied.
        /// </summary>
        public static RelationalSceneAssignmentOptions Default => new(true, true);

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(
            RelationalSceneAssignmentOptions left,
            RelationalSceneAssignmentOptions right
        )
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(
            RelationalSceneAssignmentOptions left,
            RelationalSceneAssignmentOptions right
        )
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Gets whether inactive GameObjects should be included when scanning the scene.
        /// </summary>
        public bool IncludeInactive { get; }

        /// <summary>
        /// Gets whether to use a single-pass scene scan to locate relational components.
        /// </summary>
        public bool UseSinglePassScan { get; }

        /// <summary>
        /// Initializes a new set of options.
        /// </summary>
        /// <param name="includeInactive">
        /// When true the initializer will scan inactive scene objects so that relational fields are
        /// populated even for disabled hierarchies. Defaults to <c>true</c>.
        /// </param>
        /// <param name="useSinglePassScan">
        /// When true uses a single-pass scene scan for performance (recommended). Defaults to
        /// <c>true</c>.
        /// </param>
        public RelationalSceneAssignmentOptions(bool includeInactive, bool useSinglePassScan = true)
        {
            IncludeInactive = includeInactive;
            UseSinglePassScan = useSinglePassScan;
        }

        /// <inheritdoc />
        public bool Equals(RelationalSceneAssignmentOptions other)
        {
            return IncludeInactive == other.IncludeInactive
                && UseSinglePassScan == other.UseSinglePassScan;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is RelationalSceneAssignmentOptions other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Objects.HashCode(IncludeInactive, UseSinglePassScan);
        }
    }
}
#endif
