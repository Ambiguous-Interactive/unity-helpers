// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Serialization;
    using Object = UnityEngine.Object;

    /// <summary>
    /// ScriptableWizard to batch resize textures by a computed delta using a chosen algorithm.
    /// Useful for adjusting imported assets to target pixel density or scale without external tools.
    /// </summary>
    /// <remarks>
    /// <para>
    /// How it works: for each selected texture (or those discovered under provided directories), the
    /// tool computes a size increment from <c>pixelsPerUnit</c> and the width/height multipliers.
    /// A dry run reports the target size without reading pixel data or changing importer settings.
    /// A real run ensures readability, resizes via bilinear or point, and writes the PNG to the
    /// original path or a selected output folder.
    /// </para>
    /// <para>
    /// Pros: fast iteration inside Unity, supports multiple discovery paths, preserves import
    /// settings, and can be run multiple times (<c>numResizes</c>) for step changes.
    /// </para>
    /// <para>
    /// Caveats: real runs replace original files when no output folder is selected. If textures
    /// are non-readable, a real run temporarily changes their importer settings.
    /// </para>
    /// <example>
    /// <![CDATA[
    /// // Open from menu: Tools/Wallstop Studios/Unity Helpers/Texture Resizer
    /// // Typical settings for retro pixel art:
    /// //   scalingResizeAlgorithm = Point
    /// //   pixelsPerUnit = 100
    /// //   widthMultiplier = 1.0f, heightMultiplier = 1.0f (double by setting numResizes=1 and multipliers)
    /// ]]>
    /// </example>
    public sealed class TextureResizerWizard : ScriptableWizard
    {
        public List<Texture2D> textures = new();

        [FormerlySerializedAs("animationSources")]
        [Tooltip(
            "Drag a folder from Unity here to apply the configuration to all textures under it. No textures are modified if no directories are provided."
        )]
        public List<Object> textureSourcePaths = new();

        public int numResizes = 1;

        [Tooltip("Resize algorithm to use for scaling.")]
        public ResizeAlgorithm scalingResizeAlgorithm = ResizeAlgorithm.Bilinear;

        public int pixelsPerUnit = 100;
        public float widthMultiplier = 0.54f;
        public float heightMultiplier = 0.245f;

        [Tooltip("If true, only simulates the operation without writing files.")]
        public bool dryRun;

        [Tooltip(
            "Optional output folder (Unity project relative). If set, resized PNGs are written here instead of overwriting originals."
        )]
        public DefaultAsset outputFolder;

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Texture Resizer")]
        public static void ResizeTextures()
        {
            _ = DisplayWizard<TextureResizerWizard>("Texture Resizer", "Resize");
        }

        internal void OnWizardCreate()
        {
            if (numResizes <= 0)
            {
                return;
            }

            List<string> sourceFolders = new();
            if (textureSourcePaths != null)
            {
                foreach (Object source in textureSourcePaths)
                {
                    if (source == null)
                    {
                        continue;
                    }

                    string path = AssetDatabase.GetAssetPath(source);
                    if (!string.IsNullOrEmpty(path))
                    {
                        sourceFolders.Add(path);
                    }
                }
            }

            string outputPath =
                outputFolder != null ? AssetDatabase.GetAssetPath(outputFolder) : null;
            _ = TextureResizerAPI.TryResizeTextures(
                textures,
                sourceFolders,
                numResizes,
                scalingResizeAlgorithm,
                pixelsPerUnit,
                widthMultiplier,
                heightMultiplier,
                outputPath,
                dryRun
            );
        }

        public enum ResizeAlgorithm
        {
            Bilinear,
            Point,
        }
    }
#endif
}
