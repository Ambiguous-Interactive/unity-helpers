// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    /// <summary>
    /// Creates Gaussian-blurred textures without opening the Image Blur window.
    /// </summary>
    public static class ImageBlurAPI
    {
        /// <summary>
        /// Writes a blurred image beside a project texture. Temporary importer settings are
        /// restored after processing; restoration failures are reported. An output path can be
        /// returned with an error if the file was written but a later operation failed. Existing
        /// output files are preserved and a free numbered name is chosen.
        /// </summary>
        /// <remarks>Temporary-file cleanup failures are reported alongside write failures.</remarks>
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
                        byte[] bytes = encodeJpeg
                            ? blurred.EncodeToJPG(100)
                            : blurred.EncodeToPNG();
                        if (bytes == null)
                        {
                            failure = $"Failed to encode texture: {current.name}.";
                        }
                        else
                        {
                            string stagedPath = Path.Combine(
                                projectRoot,
                                directory,
                                Path.GetRandomFileName()
                            );
                            bool stagedOwned = false;
                            try
                            {
                                using (
                                    FileStream stream = new(
                                        stagedPath,
                                        FileMode.CreateNew,
                                        FileAccess.Write,
                                        FileShare.None
                                    )
                                )
                                {
                                    stagedOwned = true;
                                    stream.Write(bytes, 0, bytes.Length);
                                    stream.Flush(flushToDisk: true);
                                }

                                for (int counter = 0; ; ++counter)
                                {
                                    string finalPath =
                                        counter == 0
                                            ? newPathBase + outputExtension
                                            : $"{newPathBase}_{counter}{outputExtension}";
                                    string absolutePath = Path.Combine(projectRoot, finalPath);
                                    if (
                                        TryPublishNewFile(
                                            stagedPath,
                                            absolutePath,
                                            out Exception publishCleanupWarning
                                        )
                                    )
                                    {
                                        stagedOwned = publishCleanupWarning != null;
                                        producedPath = finalPath.Replace('\\', '/');
                                        wroteOutput = true;
                                        if (publishCleanupWarning != null)
                                        {
                                            failure = AppendError(
                                                failure,
                                                publishCleanupWarning.Message
                                            );
                                        }
                                        break;
                                    }

                                    if (counter == int.MaxValue)
                                    {
                                        throw new IOException(
                                            "No available blurred output name was found."
                                        );
                                    }
                                }
                            }
                            finally
                            {
                                if (stagedOwned)
                                {
                                    try
                                    {
                                        File.Delete(stagedPath);
                                    }
                                    catch (Exception cleanupError)
                                    {
                                        failure = AppendError(
                                            failure,
                                            $"Could not remove temporary blur file: {cleanupError.Message}"
                                        );
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                failure = AppendError(
                    failure,
                    $"Failed to write blurred image for '{assetPath}': {exception.Message}"
                );
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

            if (wroteOutput && importOutput)
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
                    failure = AppendError(
                        failure,
                        $"Could not import blurred image '{producedPath}': {exception.Message}"
                    );
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

        internal static bool TryPublishNewFile(
            string stagedPath,
            string destinationPath,
            out Exception cleanupWarning
        )
        {
            return ExclusiveFilePublisher.TryPublishNewFile(
                stagedPath,
                destinationPath,
                out cleanupWarning
            );
        }

        private static string AppendError(string existing, string additional)
        {
            return string.IsNullOrEmpty(existing) ? additional : $"{existing} {additional}";
        }
    }
#endif
}
