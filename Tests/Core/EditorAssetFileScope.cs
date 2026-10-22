// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    internal sealed class EditorAssetFileScope : IDisposable
    {
        private readonly string[] _assetPaths;
        private readonly Dictionary<string, byte[]> _originalFiles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _originalFolderMetas = new(
            StringComparer.Ordinal
        );
        private readonly List<string> _createdFolders = new();
        private bool _disposed;

        internal EditorAssetFileScope(params string[] assetPaths)
        {
            _assetPaths = (string[])assetPaths.Clone();
            foreach (string path in _assetPaths)
            {
                Assert.That(path, Does.StartWith("Assets/"));
                Assert.That(path.Split('/'), Does.Not.Contain(".."));
                CaptureFile(path);
                CaptureFile(path + ".meta");
                string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
                while (
                    !string.IsNullOrEmpty(folder)
                    && !string.Equals(folder, "Assets", StringComparison.Ordinal)
                )
                {
                    string metaPath = folder + ".meta";
                    if (
                        Directory.Exists(folder)
                        || AssetDatabase.IsValidFolder(folder)
                        || File.Exists(metaPath)
                    )
                    {
                        _originalFolderMetas[folder] = File.Exists(metaPath)
                            ? File.ReadAllBytes(metaPath)
                            : null;
                    }
                    else if (!_createdFolders.Contains(folder))
                    {
                        _createdFolders.Add(folder);
                    }
                    folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
                }
            }
            _createdFolders.Sort((left, right) => right.Length.CompareTo(left.Length));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            using (AssetDatabaseBatchHelper.PauseBatch())
            {
                AssetDatabase.SaveAssets();
                foreach (string path in _assetPaths)
                {
                    if (_originalFiles.TryGetValue(path, out byte[] contents))
                    {
                        Assert.That(
                            AssetDatabaseBatchHelper.EnsureAssetParentFolder(path),
                            Is.True,
                            path
                        );
                        File.WriteAllBytes(path, contents);
                        if (_originalFiles.TryGetValue(path + ".meta", out byte[] meta))
                        {
                            File.WriteAllBytes(path + ".meta", meta);
                        }
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                        Assert.That(File.ReadAllBytes(path), Is.EqualTo(contents), path);
                    }
                    else
                    {
                        if (
                            File.Exists(path)
                            || !string.IsNullOrEmpty(
                                AssetDatabase.AssetPathToGUID(
                                    path,
                                    AssetPathToGUIDOptions.OnlyExistingAssets
                                )
                            )
                        )
                        {
                            Assert.That(AssetDatabase.DeleteAsset(path), Is.True, path);
                        }
                        Assert.That(File.Exists(path), Is.False, path);
                        Assert.That(File.Exists(path + ".meta"), Is.False, path);
                    }
                }
                foreach (KeyValuePair<string, byte[]> folder in _originalFolderMetas)
                {
                    Assert.That(
                        AssetDatabaseBatchHelper.EnsureAssetFolder(folder.Key),
                        Is.True,
                        folder.Key
                    );
                    if (folder.Value != null)
                    {
                        File.WriteAllBytes(folder.Key + ".meta", folder.Value);
                        AssetDatabase.ImportAsset(
                            folder.Key,
                            ImportAssetOptions.ForceSynchronousImport
                        );
                    }
                }
                foreach (string folder in _createdFolders)
                {
                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }
                    bool hasEntries;
                    using (
                        IEnumerator<string> entries = Directory
                            .EnumerateFileSystemEntries(folder)
                            .GetEnumerator()
                    )
                    {
                        hasEntries = entries.MoveNext();
                    }
                    if (hasEntries)
                    {
                        continue;
                    }
                    Assert.That(AssetDatabase.DeleteAsset(folder), Is.True, folder);
                    Assert.That(Directory.Exists(folder), Is.False, folder);
                    Assert.That(File.Exists(folder + ".meta"), Is.False, folder);
                }
                _disposed = true;
            }
        }

        private void CaptureFile(string path)
        {
            if (File.Exists(path))
            {
                _originalFiles[path] = File.ReadAllBytes(path);
            }
        }
    }
#endif
}
