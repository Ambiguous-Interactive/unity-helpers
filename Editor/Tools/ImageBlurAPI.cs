// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Creates Gaussian-blurred textures without opening the Image Blur window.
    /// </summary>
    public static class ImageBlurAPI
    {
        /// <summary>
        /// Writes a blurred image beside a project texture. Temporary importer settings are
        /// restored after processing; restoration failures are reported. An output path can be
        /// returned with an error if the file was written but a later operation failed.
        /// </summary>
        public static bool TryWriteAsset(
            Texture2D source,
            int radius,
            out string outputPath,
            out string error,
            bool importOutput = true
        )
        {
            string producedPath = null;
            string failure = null;
            if (source == null)
            {
                outputPath = null;
                error = "A source texture is required.";
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(source);
            if (
                string.IsNullOrWhiteSpace(assetPath)
                || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)
            )
            {
                outputPath = null;
                error = $"Texture is not an Assets/ project asset: {source.name}.";
                return false;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                outputPath = null;
                error = "Could not determine the Unity project directory.";
                return false;
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            bool importerSettingsChanged = false;
            bool originalReadable = false;
            TextureImporterCompression originalCompression = default;
            Texture2D blurred = null;
            bool wroteOutput = false;
            try
            {
                if (importer != null)
                {
                    originalReadable = importer.isReadable;
                    originalCompression = importer.textureCompression;
                    importerSettingsChanged =
                        !originalReadable
                        || originalCompression != TextureImporterCompression.Uncompressed;
                    if (importerSettingsChanged)
                    {
                        Undo.RecordObject(importer, "Prepare Texture for Blur");
                        importer.isReadable = true;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.SaveAndReimport();
                    }
                }

                Texture2D current = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (current == null || !current.isReadable)
                {
                    failure = $"Texture is null or could not be made readable: {assetPath}.";
                }
                else if (!TryBlur(current, radius, out blurred, out failure))
                {
                    failure = $"Failed to create blurred texture for: {source.name}. {failure}";
                }
                else
                {
                    string directory = Path.GetDirectoryName(assetPath);
                    if (string.IsNullOrWhiteSpace(directory))
                    {
                        failure = $"Texture has no project directory: {assetPath}.";
                    }
                    else
                    {
                        string fileName = Path.GetFileNameWithoutExtension(assetPath);
                        string sourceExtension = Path.GetExtension(assetPath);
                        bool encodeJpeg =
                            string.Equals(
                                sourceExtension,
                                ".jpg",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || string.Equals(
                                sourceExtension,
                                ".jpeg",
                                StringComparison.OrdinalIgnoreCase
                            );
                        string outputExtension = encodeJpeg ? sourceExtension : ".png";
                        string newPathBase = Path.Combine(
                            directory,
                            $"{fileName}_blurred_{radius}"
                        );
                        string finalPath = newPathBase + outputExtension;
                        string absolutePath = Path.Combine(projectRoot, finalPath);
                        int counter = 0;
                        while (File.Exists(absolutePath))
                        {
                            ++counter;
                            finalPath = $"{newPathBase}_{counter}{outputExtension}";
                            absolutePath = Path.Combine(projectRoot, finalPath);
                        }

                        byte[] bytes = encodeJpeg
                            ? blurred.EncodeToJPG(100)
                            : blurred.EncodeToPNG();
                        if (bytes == null)
                        {
                            failure = $"Failed to encode texture: {current.name}.";
                        }
                        else
                        {
                            File.WriteAllBytes(absolutePath, bytes);
                            producedPath = finalPath.Replace('\\', '/');
                            wroteOutput = true;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                failure = $"Failed to write blurred image for '{assetPath}': {exception.Message}";
            }
            finally
            {
                if (blurred != null)
                {
                    try
                    {
                        UnityEngine.Object.DestroyImmediate(blurred);
                    }
                    catch (Exception exception)
                    {
                        failure = AppendError(
                            failure,
                            $"Could not release temporary blur texture: {exception.Message}"
                        );
                    }
                }

                if (importerSettingsChanged)
                {
                    try
                    {
                        TextureImporter currentImporter =
                            AssetImporter.GetAtPath(assetPath) as TextureImporter;
                        if (currentImporter == null)
                        {
                            failure = AppendError(
                                failure,
                                $"Could not restore texture importer for '{assetPath}'."
                            );
                        }
                        else
                        {
                            currentImporter.isReadable = originalReadable;
                            currentImporter.textureCompression = originalCompression;
                            currentImporter.SaveAndReimport();
                        }
                    }
                    catch (Exception exception)
                    {
                        failure = AppendError(
                            failure,
                            $"Could not restore texture importer for '{assetPath}': {exception.Message}"
                        );
                    }
                }
            }

            if (wroteOutput && importOutput && string.IsNullOrEmpty(failure))
            {
                try
                {
                    AssetDatabase.ImportAsset(
                        producedPath,
                        ImportAssetOptions.ForceSynchronousImport
                    );
                }
                catch (Exception exception)
                {
                    failure =
                        $"Could not import blurred image '{producedPath}': {exception.Message}";
                }
            }

            outputPath = producedPath;
            error = failure;
            return wroteOutput && string.IsNullOrEmpty(failure);
        }

        /// <summary>
        /// Creates a blurred copy of a readable texture. The caller owns the returned texture and
        /// must destroy it when finished.
        /// </summary>
        public static bool TryBlur(
            Texture2D source,
            int radius,
            out Texture2D blurred,
            out string error
        )
        {
            if (source == null)
            {
                blurred = null;
                error = "A source texture is required.";
                return false;
            }

            if (radius < 1 || 200 < radius)
            {
                blurred = null;
                error = "Blur radius must be between 1 and 200.";
                return false;
            }

            if (!source.isReadable)
            {
                blurred = null;
                error = "The source texture must be readable.";
                return false;
            }

            try
            {
                Texture2D created = ImageBlurTool.CreateBlurredTexture(source, radius, null);
                if (created != null)
                {
                    blurred = created;
                    error = null;
                    return true;
                }

                blurred = null;
                error = $"Could not blur texture '{source.name}'.";
                return false;
            }
            catch (Exception exception)
            {
                blurred = null;
                error = $"Could not blur texture '{source.name}': {exception.Message}";
                return false;
            }
        }

        private static string AppendError(string existing, string additional)
        {
            return string.IsNullOrEmpty(existing) ? additional : $"{existing} {additional}";
        }
    }
#endif
}
