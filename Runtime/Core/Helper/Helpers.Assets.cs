// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    public static partial class Helpers
    {
        private static readonly string[] DefaultAssetSearchFolders = { "Assets" };
        private static readonly char[] ReservedAssetPathCharacters =
        {
            '<',
            '>',
            '"',
            '|',
            '?',
            '*',
            ':',
        };

        private static readonly HashSet<string> NativeAssetExtensions = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ".asset",
            ".anim",
            ".mat",
            ".guiskin",
            ".cubemap",
            ".mask",
            ".physicMaterial",
            ".physicsMaterial2D",
            ".renderTexture",
            ".controller",
            ".overrideController",
            ".terrainlayer",
            ".flare",
        };

        /// <summary>Enumerates matching main assets and subassets in the requested project folders.</summary>
        /// <remarks>Editor only. Null folders search Assets; an empty folder collection returns no assets.</remarks>
        public static IEnumerable<T> EnumerateAssets<T>(IEnumerable<string> assetPaths = null)
            where T : Object
        {
            string[] requested = PrepareSearchFolders(assetPaths, DefaultAssetSearchFolders);
            string[] folders = ExistingAssetSearchFolders(requested);
            if (folders.Length == 0)
            {
                yield break;
            }

            string filter = "t:" + typeof(T).Name;
            foreach (string guid in AssetDatabase.FindAssets(filter, folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Object main = AssetDatabase.LoadMainAssetAtPath(path);
                if (main is T matchingMain && matchingMain != null)
                {
                    yield return matchingMain;
                }
                if (main is SceneAsset || AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset != main && asset is T matching && matching != null)
                    {
                        yield return matching;
                    }
                }
            }
        }

        /// <summary>Creates a native asset or updates an existing asset of the same type, preserving its identity.</summary>
        /// <returns>The saved asset, or null when validation or saving fails.</returns>
        /// <remarks>Editor only. File creation and saving cannot be fully reversed through Undo.</remarks>
        public static Object CreateOrReplaceAsset(Object asset, string path)
        {
            if (TryCreateOrReplaceAsset(asset, path, out Object savedAsset, out _))
            {
                return savedAsset;
            }

            return null;
        }

        /// <summary>Tries to save a native asset while preserving an existing asset's GUID and local file ID.</summary>
        /// <remarks>
        /// Editor main thread only. Imported assets, subasset sources, objects marked DontSaveInEditor and type changes are refused.
        /// Copying from a distinct source records Undo; file creation and saving are not fully undoable.
        /// A failed save may leave an unsaved in-memory update; the error reports failure without deleting files.
        /// </remarks>
        public static bool TryCreateOrReplaceAsset(
            Object asset,
            string path,
            out Object savedAsset,
            out string error
        )
        {
            if (asset == null || string.IsNullOrWhiteSpace(path))
            {
                savedAsset = null;
                error = "An asset and project-relative destination are required.";
                return false;
            }

            path = path.SanitizePath();
            string[] segments = path.Split('/');
            if (
                !path.StartsWith("Assets/", StringComparison.Ordinal)
                || 0 <= path.IndexOfAny(ReservedAssetPathCharacters)
                || 0 <= Array.IndexOf(segments, "..")
                || 0 <= Array.IndexOf(segments, ".")
                || 0 <= Array.IndexOf(segments, string.Empty)
            )
            {
                savedAsset = null;
                error =
                    "The destination must be a file below Assets without relative path segments.";
                return false;
            }

            foreach (char character in path)
            {
                if (char.IsControl(character))
                {
                    savedAsset = null;
                    error = "The destination cannot contain control characters.";
                    return false;
                }
            }

            try
            {
                string parent = Path.GetDirectoryName(path).SanitizePath();
                string extension = Path.GetExtension(path);
                if (
                    !AssetDatabase.IsValidFolder(parent)
                    || AssetDatabase.IsValidFolder(path)
                    || !NativeAssetExtensions.Contains(extension)
                    || asset is GameObject
                    || asset is Component
                    || asset is DefaultAsset
                    || asset is MonoScript
                    || (asset.hideFlags & HideFlags.DontSaveInEditor) != HideFlags.None
                    || AssetDatabase.IsSubAsset(asset)
                    || (AssetDatabase.Contains(asset) && !AssetDatabase.IsNativeAsset(asset))
                )
                {
                    savedAsset = null;
                    error =
                        "The destination requires an existing folder and a native non-scene asset.";
                    return false;
                }

                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string absolutePath = Path.Combine(projectRoot, path);
                bool occupied = File.Exists(absolutePath);
                if (occupied && AssetDatabase.GetMainAssetTypeAtPath(path) == null)
                {
                    savedAsset = null;
                    error = "An unrecognized file already occupies the destination.";
                    return false;
                }
                Object existing = AssetDatabase.LoadMainAssetAtPath(path);
                if (existing != null)
                {
                    if (
                        !AssetDatabase.IsNativeAsset(existing)
                        || existing.GetType() != asset.GetType()
                    )
                    {
                        savedAsset = null;
                        error =
                            "Only a native main asset of the same concrete type can be replaced.";
                        return false;
                    }

                    bool copyContents = existing != asset;
                    if (copyContents)
                    {
                        Undo.RecordObject(existing, "Replace Asset Contents");
                        EditorUtility.CopySerialized(asset, existing);
                    }
                    EditorUtility.SetDirty(existing);
                    if (copyContents)
                    {
                        Undo.FlushUndoRecordObjects();
                    }
                }
                else
                {
                    if (occupied || AssetDatabase.Contains(asset))
                    {
                        savedAsset = null;
                        error =
                            "The destination is occupied or the source is already a persistent asset.";
                        return false;
                    }

                    AssetDatabase.CreateAsset(asset, path);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                Object loaded = AssetDatabase.LoadMainAssetAtPath(path);
                if (loaded == null || loaded.GetType() != asset.GetType())
                {
                    savedAsset = null;
                    error = "Unity did not save the requested asset.";
                    return false;
                }

                savedAsset = loaded;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                savedAsset = null;
                error = exception.Message;
                return false;
            }
        }

        private static string[] ExistingAssetSearchFolders(string[] requested)
        {
            using PooledResource<List<string>> lease = Buffers<string>.List.Get(
                out List<string> existing
            );
            foreach (string folder in requested)
            {
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }
                string normalized = folder.SanitizePath();
                if (AssetDatabase.IsValidFolder(normalized))
                {
                    existing.Add(normalized);
                }
            }
            return existing.ToArray();
        }
    }
#endif
}
