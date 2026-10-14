// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Supplies a real nested generic enum declaration for migration binding.</summary>
    public sealed class OdinMigrationGenericBindingAsset : ScriptableObject
    {
        /// <summary>The original dictionary with a nested generic enum key.</summary>
        public Dictionary<OdinMigrationGenericKeys<List<int[]>>.Key, List<Texture2D>> Variations =
            new();

        /// <summary>The original dictionary with a forwarded framework enum key.</summary>
        public Dictionary<System.AttributeTargets, List<Texture2D>> Other = new();
    }
}
