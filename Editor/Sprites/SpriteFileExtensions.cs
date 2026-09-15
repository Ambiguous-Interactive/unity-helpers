// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;

    internal static class SpriteFileExtensions
    {
        internal static bool HasAny(string path, string[] extensions)
        {
            if (string.IsNullOrEmpty(path) || extensions == null)
            {
                return false;
            }

            foreach (string extension in extensions)
            {
                if (
                    !string.IsNullOrEmpty(extension)
                    && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }

            return false;
        }
    }
#endif
}
