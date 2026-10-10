// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Integration")]
    public sealed class AssetsPathContainmentTests : CommonTestBase
    {
        [TestCase("Assets/../Temp/{0}")]
        [TestCase(@"assets\..\Temp\{0}")]
        [TestCase("Assets/Nested/../../Temp/{0}")]
        [TestCase("Assets//../Temp/{0}")]
        public void FolderHelpersRejectTraversalWithoutCreatingExternalDirectory(string pattern)
        {
            string name = "AssetContainment" + Guid.NewGuid().ToString("N");
            string input = string.Format(pattern, name);
            string outside = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                "Temp",
                name
            );
            Assert.IsFalse(Directory.Exists(outside));
            try
            {
                Assert.Throws<ArgumentException>(() =>
                    DirectoryHelper.EnsureDirectoryExists(input)
                );
                Assert.IsFalse(AssetDatabaseBatchHelper.EnsureAssetFolder(input));
                Assert.IsFalse(
                    AssetDatabaseBatchHelper.EnsureAssetParentFolder(input + "/Data.asset")
                );
                Assert.IsFalse(Directory.Exists(outside));
                Assert.IsFalse(File.Exists(outside + ".meta"));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (Directory.Exists(outside))
                {
                    Directory.Delete(outside, true);
                }
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("AssetsSibling/Child")]
        [TestCase("Assets/Invalid\0Child")]
        [TestCase("Packages/Foo")]
        [TestCase("File.asset")]
        public void FolderHelpersRejectInvalidPaths(string path)
        {
            const string bareAssetName = "File.asset";
            bool isBareAssetName = string.Equals(path, bareAssetName, StringComparison.Ordinal);
            string bareAssetPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                bareAssetName
            );
            bool fileExisted = File.Exists(bareAssetPath);
            bool directoryExisted = Directory.Exists(bareAssetPath);
            bool metaExisted = File.Exists(bareAssetPath + ".meta");

            Assert.IsFalse(AssetDatabaseBatchHelper.EnsureAssetFolder(path));
            Assert.That(
                AssetDatabaseBatchHelper.EnsureAssetParentFolder(path),
                Is.EqualTo(isBareAssetName)
            );
            Assert.That(File.Exists(bareAssetPath), Is.EqualTo(fileExisted));
            Assert.That(Directory.Exists(bareAssetPath), Is.EqualTo(directoryExisted));
            Assert.That(File.Exists(bareAssetPath + ".meta"), Is.EqualTo(metaExisted));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase("assets/AssetsContainment/Discarded/../Nested")]
        [TestCase(@"ASSETS\AssetsContainment\Discarded/../Nested")]
        [TestCase("Assets//AssetsContainment/./Nested/")]
        public void FolderHelpersNormalizeInsideParentsAndPreserveFolderIdentity(string path)
        {
            const string root = "Assets/AssetsContainment";
            const string child = root + "/Nested";
            AssetDatabase.DeleteAsset(root);
            try
            {
                Assert.IsTrue(AssetDatabaseBatchHelper.EnsureAssetFolder(path));
                string guid = AssetDatabase.AssetPathToGUID(child);
                Assert.IsNotEmpty(guid);
                Assert.IsTrue(
                    AssetDatabaseBatchHelper.EnsureAssetParentFolder(path + "/Data.asset")
                );
                DirectoryHelper.EnsureDirectoryExists(path);
                Assert.That(AssetDatabase.AssetPathToGUID(child), Is.EqualTo(guid));
                Assert.IsFalse(
                    Directory.Exists(
                        Path.Combine(Application.dataPath, "AssetsContainment/Discarded")
                    )
                );
                Assert.IsFalse(
                    Directory.Exists(
                        Path.Combine(Application.dataPath, "AssetsContainment/Nested 1")
                    )
                );
            }
            finally
            {
                AssetDatabase.DeleteAsset(root);
            }
        }

        [TestCase("Assets")]
        [TestCase("assets/")]
        [TestCase("Assets/Unused/..")]
        public void FolderHelpersAcceptCanonicalRoot(string path)
        {
            Assert.IsTrue(AssetDatabaseBatchHelper.EnsureAssetFolder(path));
            Assert.IsTrue(AssetDatabaseBatchHelper.EnsureAssetParentFolder(path + "/Data.asset"));
            DirectoryHelper.EnsureDirectoryExists(path);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase("Assets/../Temp/{0}.asset")]
        [TestCase(@"Assets\..\Temp\{0}.asset")]
        public void SingletonFailureCleanupPreservesFilesOutsideAssets(string pattern)
        {
            string name = "SingletonContainment" + Guid.NewGuid().ToString("N");
            string absolute = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                "Temp",
                name + ".asset"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllText(absolute, "body sentinel");
            File.WriteAllText(absolute + ".meta", "metadata sentinel");
            try
            {
                ScriptableObjectSingletonCreator.TryCleanupPartiallyCreatedAsset(
                    string.Format(pattern, name)
                );
                Assert.That(File.ReadAllText(absolute), Is.EqualTo("body sentinel"));
                Assert.That(File.ReadAllText(absolute + ".meta"), Is.EqualTo("metadata sentinel"));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                File.Delete(absolute);
                File.Delete(absolute + ".meta");
            }
        }

        [TestCase("Assets")]
        [TestCase("Assets/Discarded/..")]
        public void SingletonFailureCleanupPreservesAssetsRoot(string path)
        {
            ScriptableObjectSingletonCreator.TryCleanupPartiallyCreatedAsset(path);
            Assert.IsTrue(Directory.Exists(Application.dataPath));
            Assert.IsTrue(AssetDatabase.IsValidFolder("Assets"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SingletonFailureCleanupRemovesCanonicalDiskOnlyFilesInsideAssets()
        {
            string name = "SingletonContainment" + Guid.NewGuid().ToString("N") + ".asset";
            string absolute = Path.Combine(Application.dataPath, name);
            File.WriteAllText(absolute, "partial body");
            File.WriteAllText(absolute + ".meta", "partial metadata");
            try
            {
                ScriptableObjectSingletonCreator.TryCleanupPartiallyCreatedAsset(
                    "assets/Unused/../" + name
                );
                Assert.IsFalse(File.Exists(absolute));
                Assert.IsFalse(File.Exists(absolute + ".meta"));
            }
            finally
            {
                File.Delete(absolute);
                File.Delete(absolute + ".meta");
            }
        }
    }
#endif
}
