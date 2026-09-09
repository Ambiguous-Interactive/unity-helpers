// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if REFLEX_PRESENT
namespace WallstopStudios.UnityHelpers.Integrations.Reflex
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Controls how the Reflex integration performs relational component assignment across scenes.
    /// </summary>
    public readonly struct RelationalSceneAssignmentOptions
        : IEquatable<RelationalSceneAssignmentOptions>
    {
        /// <summary>
        /// Gets default options (include inactive objects, single-pass scan).
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
        /// Gets whether inactive GameObjects are included during scans.
        /// </summary>
        public bool IncludeInactive { get; }

        /// <summary>
        /// Gets whether to use a single-pass scan strategy.
        /// </summary>
        public bool UseSinglePassScan { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RelationalSceneAssignmentOptions"/> struct.
        /// </summary>
        /// <param name="includeInactive">
        /// When true, relational assignment scans include inactive GameObjects. Defaults to true.
        /// </param>
        /// <param name="useSinglePassScan">
        /// When true, performs a single-pass scan for relational types to maximize performance.
        /// Defaults to true.
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
