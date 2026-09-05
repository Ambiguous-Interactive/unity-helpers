// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
#if UNITY_EDITOR
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
        private const string TempFolder = "Assets/TempObjectHelpersEditorTests";
        private const string AssetName = "TestMaterial.mat";
        private string _assetPath;

        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            EnsureFolder(TempFolder);
            TrackFolder(TempFolder);
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _assetPath = TempFolder + "/" + AssetName;
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
