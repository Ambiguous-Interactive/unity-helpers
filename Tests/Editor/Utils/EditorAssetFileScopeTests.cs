// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Integration")]
    [Category("Slow")]
    public sealed class EditorAssetFileScopeTests : CommonTestBase
    {
        private string _root;

        private static void WriteAsset(string path, string contents)
        {
            Assert.That(AssetDatabaseBatchHelper.EnsureAssetParentFolder(path), Is.True);
            File.WriteAllText(path, contents);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        [SetUp]
        public void SetUpAssets()
        {
            _root = "Assets/AssetFileScope-" + Guid.NewGuid().ToString("N");
            Assert.That(AssetDatabaseBatchHelper.EnsureAssetFolder(_root), Is.True);
            TrackFolder(_root);
        }

        [TearDown]
        public void RemoveOwnedAssets()
        {
            CleanupTrackedFoldersAndAssets();
        }

        [TestCase(false, TestName = "ExistingAsset.NormalExit.RestoresBytesAndGuid")]
        [TestCase(true, TestName = "ExistingAsset.InterruptedExit.RestoresBytesAndGuid")]
        public void ScopeRestoresExistingAssetAndMetadata(bool interrupted)
        {
            string path = _root + "/Original.txt";
            WriteAsset(path, "retained player data");
            byte[] original = File.ReadAllBytes(path);
            byte[] meta = File.ReadAllBytes(path + ".meta");
            byte[] folderMeta = File.ReadAllBytes(_root + ".meta");
            string guid = AssetDatabase.AssetPathToGUID(path);

            void ChangeAsset()
            {
                using EditorAssetFileScope scope = new(path);
                AssetDatabase.DeleteAsset(path);
                WriteAsset(path, "temporary test data");
                File.WriteAllText(
                    path + ".meta",
                    File.ReadAllText(path + ".meta").Replace(guid, Guid.NewGuid().ToString("N"))
                );
                Assert.That(File.ReadAllBytes(path + ".meta"), Is.Not.EqualTo(meta));
                if (interrupted)
                {
                    throw new InvalidOperationException("Interrupted test");
                }
            }

            if (interrupted)
            {
                Assert.Throws<InvalidOperationException>(ChangeAsset);
            }
            else
            {
                ChangeAsset();
            }

            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            Assert.That(File.ReadAllBytes(path + ".meta"), Is.EqualTo(meta));
            Assert.That(File.ReadAllBytes(_root + ".meta"), Is.EqualTo(folderMeta));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
        }

        [TestCase(false, TestName = "NewAsset.EmptyParents.RemovesOwnedHierarchy")]
        [TestCase(true, TestName = "NewAsset.UntrackedSibling.PreservesSiblingAndParents")]
        public void ScopeRemovesOnlyItsOwnNewAsset(bool retainSibling)
        {
            string folder = _root + "/New/Deep";
            string path = folder + "/Created.txt";
            string sibling = folder + "/Neighbor.txt";
            byte[] parentMeta = File.ReadAllBytes(_root + ".meta");
            using (EditorAssetFileScope scope = new(path))
            {
                WriteAsset(path, "owned");
                if (retainSibling)
                {
                    WriteAsset(sibling, "unrelated");
                }
            }
            Assert.That(File.Exists(path), Is.False);
            Assert.That(File.Exists(path + ".meta"), Is.False);
            Assert.That(
                AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets),
                Is.Empty
            );
            Assert.That(Directory.Exists(folder), Is.EqualTo(retainSibling));
            Assert.That(Directory.Exists(_root + "/New"), Is.EqualTo(retainSibling));
            Assert.That(File.Exists(_root + "/New.meta"), Is.EqualTo(retainSibling));
            Assert.That(File.ReadAllBytes(_root + ".meta"), Is.EqualTo(parentMeta));
            if (retainSibling)
            {
                Assert.That(File.ReadAllText(sibling), Is.EqualTo("unrelated"));
            }
        }

        [Test]
        public void ScopeRestoresDeletedExistingAsset()
        {
            string path = _root + "/Retained.txt";
            WriteAsset(path, "retained");
            string guid = AssetDatabase.AssetPathToGUID(path);
            using (EditorAssetFileScope scope = new(path))
            {
                Assert.That(AssetDatabase.DeleteAsset(path), Is.True);
            }
            Assert.That(File.ReadAllText(path), Is.EqualTo("retained"));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
        }

        [Test]
        public void ScopeAcceptsAnAlreadyDeletedNewAsset()
        {
            string path = _root + "/Deleted.txt";
            using (EditorAssetFileScope scope = new(path))
            {
                WriteAsset(path, "owned");
                Assert.That(AssetDatabase.DeleteAsset(path), Is.True);
            }
            Assert.That(File.Exists(path), Is.False);
            Assert.That(File.Exists(path + ".meta"), Is.False);
        }

        [Test]
        public void RepeatedDisposePreservesSubsequentWriter()
        {
            string path = _root + "/Later.txt";
            using EditorAssetFileScope scope = new(path);
            WriteAsset(path, "owned");
            scope.Dispose();
            WriteAsset(path, "later writer");
            scope.Dispose();
            Assert.That(File.ReadAllText(path), Is.EqualTo("later writer"));
        }
    }
}
