// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
    using UnityEditor;

    internal static class TextureImporterSourceSizeShim
    {
        internal static void GetSourceTextureWidthAndHeight(
            this TextureImporter importer,
            out int width,
            out int height
        )
        {
            width = 0;
            height = 0;
        }
    }
}
