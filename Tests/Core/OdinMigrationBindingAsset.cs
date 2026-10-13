// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Provides original dictionary declarations for compiler binding checks.</summary>
    public sealed class OdinMigrationBindingAsset : ScriptableObject
    {
        /// <summary>The original variations dictionary.</summary>
        public Dictionary<SpriteAlignment, List<Texture2D>> Variations =
            new Dictionary<SpriteAlignment, List<Texture2D>>();

        /// <summary>The original secondary dictionary.</summary>
        public Dictionary<HideFlags, List<Texture2D>> Other =
            new Dictionary<HideFlags, List<Texture2D>>();
    }
}
