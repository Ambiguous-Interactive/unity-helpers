// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.OdinCompilerTargets
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Provides dictionary declarations for isolated consumer compilation.</summary>
    public sealed class OdinMigrationCompilerAsset : ScriptableObject
    {
        /// <summary>The original variations dictionary.</summary>
        public Dictionary<SpriteAlignment, List<Texture2D>> Variations = new();

        /// <summary>The original secondary dictionary.</summary>
        public Dictionary<HideFlags, List<Texture2D>> Other = new();
    }
}
