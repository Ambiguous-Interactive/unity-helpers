// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Supplies an implicitly private dictionary declaration for migration previews.</summary>
    public sealed class OdinMigrationImplicitPrivateBindingAsset : ScriptableObject
    {
        /// <summary>The original field name.</summary>
        public const string FieldName = nameof(_variations);

        /// <summary>Gets the original dictionary count.</summary>
        public int Count => _variations.Count;

        Dictionary<SpriteAlignment, List<Texture2D>> _variations = new();
    }
}
