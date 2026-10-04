// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using UnityEngine;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    public static class SetTextureImportData
    {
        public const int MaxTextureSize = 8192;

        // ReSharper disable once UnusedMember.Global
        public const int RegularTextureSize = 2048;

        public static void SetReadable(Texture2D texture)
        {
#if UNITY_EDITOR

            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            TextureImporter tImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            if (tImporter == null)
            {
                return;
            }

            tImporter.isReadable = true;
            tImporter.SaveAndReimport();
#endif
        }

        public static void SetTextureImporterFormat(Texture2D texture, bool isReadable = true)
        {
#if UNITY_EDITOR
            if (texture == null)
            {
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            TextureImporter tImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            if (tImporter == null)
            {
                return;
            }

            tImporter.isReadable = isReadable;

            TextureImporterPlatformSettings importerSettings = new()
            {
                resizeAlgorithm = TextureResizeAlgorithm.Bilinear,
                maxTextureSize = MaxTextureSize,
                format = TextureImporterFormat.Automatic,
                textureCompression = TextureImporterCompression.Uncompressed,
            };
            tImporter.SetPlatformTextureSettings(importerSettings);

            tImporter.SaveAndReimport();
# endif
        }
    }
}
