// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditorInternal;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class HelpersTests : BatchedEditorTestBase
    {
        private readonly string _prefabFolder =
            "Assets/TempHelpersPrefabs-" + Guid.NewGuid().ToString("N");
        private readonly string _scriptableFolder =
            "Assets/TempHelpersScriptables-" + Guid.NewGuid().ToString("N");

        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();

            ExecuteWithImmediateImport(() =>
            {
                EnsureFolder(_prefabFolder);
                TrackFolder(_prefabFolder);
            });

            ExecuteWithImmediateImport(() =>
            {
                EnsureFolder(_scriptableFolder);
                TrackFolder(_scriptableFolder);
            });
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
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
            EditorAssetFolderAssertions.AssertUniqueRootRemoved(_prefabFolder);
            EditorAssetFolderAssertions.AssertUniqueRootRemoved(_scriptableFolder);
        }

        [Test]
        public void EnumeratePrefabsFindsGeneratedPrefab()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            string assetPath = Path.Combine(_prefabFolder, "TestPrefab.prefab").SanitizePath();
            TrackAssetPath(assetPath);
            GameObject prefabSource = Track(new GameObject("Helpers_PrefabSource"));

            GameObject prefab = null;
            ExecuteWithImmediateImport(() =>
            {
                prefab = PrefabUtility.SaveAsPrefabAsset(prefabSource, assetPath);
            });
            Assert.IsTrue(prefab != null);

            HashSet<string> names = Helpers
                .EnumeratePrefabs(new[] { _prefabFolder })
                .Select(go => go.name)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains(prefab.name, names.ToList());
        }

        [Test]
        public void EnumerateScriptableObjectsFindsAsset()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            string assetPath = Path.Combine(_scriptableFolder, "Dummy.asset").SanitizePath();
            TrackAssetPath(assetPath);
            DummyScriptableObject asset = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );

            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();
            });

            HashSet<string> guids = Helpers
                .EnumerateScriptableObjects<DummyScriptableObject>(new[] { _scriptableFolder })
                .Select(so => AssetDatabase.GetAssetPath(so))
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains(assetPath, guids.ToList());
        }

        [Test]
        public void GetAllLayerNamesMatch()
        {
            Assert.That(Helpers.GetAllLayerNames(), Is.EqualTo(InternalEditorUtility.layers));

            using PooledResource<List<string>> bufferResource = Buffers<string>.List.Get(
                out List<string> buffer
            );
            Helpers.GetAllLayerNames(buffer);
            Assert.That(buffer, Is.EqualTo(InternalEditorUtility.layers));
        }
    }
}
