// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Provides a readonly original dictionary for generated recovery checks.</summary>
    public sealed class OdinMigrationReadonlyBindingAsset : ScriptableObject
    {
        /// <summary>The original readonly dictionary.</summary>
        public readonly Dictionary<SpriteAlignment, List<Texture2D>> Variations = new();
    }
}
