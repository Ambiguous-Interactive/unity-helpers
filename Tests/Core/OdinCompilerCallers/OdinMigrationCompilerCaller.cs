// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.OdinCompilerCallers
{
    using WallstopStudios.UnityHelpers.Tests.Core.OdinCompilerTargets;

    /// <summary>Exercises a transitive runtime caller of a migrated dictionary.</summary>
    public static class OdinMigrationCompilerCaller
    {
        /// <summary>Reads the migrated dictionary through the original member name.</summary>
        public static int Count(OdinMigrationCompilerAsset asset)
        {
            return asset.Variations.Count;
        }
    }
}
