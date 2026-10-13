// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    public sealed class OdinMigrationDictionaryAsset : ScriptableObject
    {
#pragma warning disable WUH002 // Import tests verify the dictionary's boxed collection values.
        public SerializableDictionary<SpriteAlignment, List<Texture2D>> Variations = new();
        public SerializableDictionary<HideFlags, List<Texture2D>> Other = new();
#pragma warning restore WUH002
    }
}
