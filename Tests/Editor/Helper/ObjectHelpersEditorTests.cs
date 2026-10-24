// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
#if UNITY_EDITOR
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ObjectHelpersEditorTests : BatchedEditorTestBase
    {
        private const string AssetName = "TestMaterial.mat";
        private readonly string _tempFolder =
            "Assets/TempObjectHelpersEditorTests-" + Guid.NewGuid().ToString("N");
        private string _assetPath;

        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            ExecuteWithImmediateImport(() =>
            {
                EnsureFolder(_tempFolder);
                TrackFolder(_tempFolder);
            });
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _assetPath = _tempFolder + "/" + AssetName;
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();

            if (!string.IsNullOrEmpty(_assetPath))
            {
                TrackAssetPath(_assetPath);
            }
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            try
            {
                CleanupTrackedFoldersAndAssets();
            }
            finally
            {
                base.OneTimeTearDown();
            }
            EditorAssetFolderAssertions.AssertUniqueRootRemoved(_tempFolder);
        }

        [Test]
        public void SmartDestroyDoesNotDeleteAssetsInEditMode()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Assert.Inconclusive("Required shader not found.");
            }

            Material loaded = null;
            ExecuteWithImmediateImport(() =>
            {
                Material mat = new(shader);
                AssetDatabase.CreateAsset(mat, _assetPath);
                AssetDatabase.SaveAssets();
                loaded = AssetDatabase.LoadAssetAtPath<Material>(_assetPath);
            });

            Assert.IsTrue(loaded != null, "Expected asset to be created and loadable");

            loaded.Destroy();

            Material reloaded = null;
            ExecuteWithImmediateImport(() =>
            {
                reloaded = AssetDatabase.LoadAssetAtPath<Material>(_assetPath);
            });
            Assert.IsTrue(reloaded != null, "Expected asset to remain after SmartDestroy");
        }
    }
#endif
}
