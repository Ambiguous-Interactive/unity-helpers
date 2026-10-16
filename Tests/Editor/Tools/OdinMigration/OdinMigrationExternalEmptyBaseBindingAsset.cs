// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>Provides dictionary fields beneath a referenced dictionary-free helper.</summary>
    public sealed class OdinMigrationExternalEmptyBaseBindingAsset : OdinMigrationEmptyBindingAsset
    {
        /// <summary>The original variations dictionary.</summary>
        public Dictionary<SpriteAlignment, List<Texture2D>> Variations =
            new Dictionary<SpriteAlignment, List<Texture2D>>();
    }
}
