// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;

    /// <summary>Applies a fixture edit during an asset save.</summary>
    public sealed class SpriteProfileStagingEditProcessor : AssetModificationProcessor
    {
        internal static Action<string[]> BeforeSave { get; set; }

        private static string[] OnWillSaveAssets(string[] paths)
        {
            BeforeSave?.Invoke(paths);
            return paths;
        }
    }
#endif
}
