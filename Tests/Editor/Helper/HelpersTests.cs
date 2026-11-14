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
    using UnityEditor.SceneManagement;
    using UnityEditorInternal;
    using UnityEngine;
    using UnityEngine.SceneManagement;
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
        public void EnumerateAssetsIncludesEveryMatchingSubassetOnce()
        {
            string path = _scriptableFolder + "/Clips.asset";
            TrackAssetPath(path);
            DummyScriptableObject container = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            AnimationClip first = Track(new AnimationClip { name = "First" });
            AnimationClip second = Track(new AnimationClip { name = "Second" });
            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.CreateAsset(container, path);
                AssetDatabase.AddObjectToAsset(first, container);
                AssetDatabase.AddObjectToAsset(second, container);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            });

            AnimationClip[] clips = Helpers
                .EnumerateAssets<AnimationClip>(new[] { _scriptableFolder })
                .ToArray();
            Assert.That(clips, Is.EquivalentTo(new[] { first, second }));
            Assert.That(
                Helpers.EnumerateAssets<AnimationClip>().Any(clip => clip == first),
                Is.True
            );
            Assert.That(
                Helpers.EnumerateAssets<AnimationClip>(
                    new[] { " ", null, _scriptableFolder + "/Missing" }
                ),
                Is.Empty
            );
            Assert.That(Helpers.EnumerateAssets<AnimationClip>(Array.Empty<string>()), Is.Empty);
            Assert.That(Helpers.EnumerateAssets<AnimationClip>(new[] { _prefabFolder }), Is.Empty);
        }

        [Test]
        public void EnumerateAssetsFindsSceneMainAssets()
        {
            string path = _scriptableFolder + "/Scene.unity";
            TrackAssetPath(path);
            Scene scene = CreateTempScene(nameof(EnumerateAssetsFindsSceneMainAssets));
            ExecuteWithImmediateImport(() =>
            {
                Assert.That(EditorSceneManager.SaveScene(scene, path), Is.True);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            });
            SceneAsset[] scenes = Helpers
                .EnumerateAssets<SceneAsset>(new[] { _scriptableFolder })
                .ToArray();
            Assert.That(scenes.Length, Is.EqualTo(1));
            Assert.That(AssetDatabase.GetAssetPath(scenes[0]), Is.EqualTo(path));
        }

        [Test]
        public void CreateOrReplaceAssetRefusesSubassetSources()
        {
            string path = _scriptableFolder + "/Subasset.asset";
            TrackAssetPath(path);
            DummyScriptableObject original = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            DummyScriptableObject child = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            original.value = 7;
            child.value = 42;
            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.CreateAsset(original, path);
                AssetDatabase.AddObjectToAsset(child, original);
                AssetDatabase.SaveAssets();
                Assert.That(Helpers.CreateOrReplaceAsset(child, path), Is.Null);
                Assert.That(original.value, Is.EqualTo(7));
            });
        }

        [Test]
        public void CreateOrReplaceAssetPreservesIdentityAndReferences()
        {
            string path = _scriptableFolder + "/Replace.asset";
            string holderPath = _scriptableFolder + "/Reference.asset";
            TrackAssetPath(path);
            TrackAssetPath(holderPath);
            DummyScriptableObject original = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            DummyScriptableObject replacement = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            DummyScriptableObject holder = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            original.value = 7;
            replacement.value = 42;
            ExecuteWithImmediateImport(() =>
            {
                Assert.That(Helpers.CreateOrReplaceAsset(original, path), Is.SameAs(original));
                holder.reference = original;
                AssetDatabase.CreateAsset(holder, holderPath);
                AssetDatabase.SaveAssets();
                Assert.That(
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        original,
                        out string guidBefore,
                        out long idBefore
                    ),
                    Is.True
                );
                Assert.That(
                    Helpers.TryCreateOrReplaceAsset(
                        replacement,
                        path,
                        out UnityEngine.Object saved,
                        out string error
                    ),
                    Is.True,
                    error
                );
                Assert.That(saved, Is.SameAs(original));
                Assert.That(original.value, Is.EqualTo(42));
                Assert.That(holder.reference, Is.SameAs(original));
                Assert.That(
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        saved,
                        out string guidAfter,
                        out long idAfter
                    ),
                    Is.True
                );
                Assert.That(guidAfter, Is.EqualTo(guidBefore));
                Assert.That(idAfter, Is.EqualTo(idBefore));
                Assert.That(AssetDatabase.Contains(replacement), Is.False);
                Assert.That(Helpers.CreateOrReplaceAsset(original, path), Is.SameAs(original));
            });
        }

        [Test]
        public void ReplaceAssetSupportsInMemoryUndoAndRedo()
        {
            string path = _scriptableFolder + "/Undo.asset";
            TrackAssetPath(path);
            DummyScriptableObject original = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            DummyScriptableObject replacement = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            original.value = 3;
            replacement.value = 9;
            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.CreateAsset(original, path);
                AssetDatabase.SaveAssets();
                Undo.IncrementCurrentGroup();
                Assert.That(Helpers.CreateOrReplaceAsset(replacement, path), Is.SameAs(original));
                Undo.PerformUndo();
                Assert.That(original.value, Is.EqualTo(3));
                Undo.PerformRedo();
                Assert.That(original.value, Is.EqualTo(9));
            });
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("Packages/value.asset")]
        [TestCase("Assets/../value.asset")]
        [TestCase("Assets/./value.asset")]
        [TestCase("Assets//value.asset")]
        [TestCase("Assets/value.png")]
        [TestCase("Assets/value.prefab")]
        [TestCase("Assets/value.unity")]
        [TestCase("Assets/value")]
        [TestCase("Assets/bad?.asset")]
        [TestCase("Assets/bad*.asset")]
        [TestCase("Assets/bad\n.asset")]
        [TestCase("Assets/bad\0.asset")]
        public void CreateOrReplaceAssetRefusesInvalidDestinations(string path)
        {
            DummyScriptableObject source = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            Assert.That(
                Helpers.TryCreateOrReplaceAsset(
                    source,
                    path,
                    out UnityEngine.Object saved,
                    out string error
                ),
                Is.False
            );
            Assert.That(saved, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(AssetDatabase.Contains(source), Is.False);
        }

        [Test]
        public void CreateOrReplaceAssetRequiresAnExistingParentFolder()
        {
            DummyScriptableObject source = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            string path = _scriptableFolder + "/Missing/Value.asset";
            Assert.That(Helpers.CreateOrReplaceAsset(source, path), Is.Null);
            Assert.That(AssetDatabase.Contains(source), Is.False);
        }

        [Test]
        public void CreateOrReplaceAssetRefusesImportedSources()
        {
            string importedPath = _scriptableFolder + "/Imported.png";
            string destination = _scriptableFolder + "/ImportedCopy.asset";
            TrackAssetPath(importedPath);
            TrackAssetPath(destination);
            Texture2D texture = Track(new Texture2D(1, 1));
            ExecuteWithImmediateImport(() =>
            {
                File.WriteAllBytes(importedPath, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(importedPath, ImportAssetOptions.ForceSynchronousImport);
                Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(importedPath);
                Assert.That(imported, Is.Not.Null);
                Assert.That(Helpers.CreateOrReplaceAsset(imported, destination), Is.Null);
                Assert.That(AssetDatabase.LoadMainAssetAtPath(destination), Is.Null);
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<Texture2D>(importedPath),
                    Is.SameAs(imported)
                );
            });
        }

        [Test]
        public void CreateOrReplaceAssetPreservesUnrecognizedFiles()
        {
            string path = _scriptableFolder + "/Occupied.asset";
            TrackAssetPath(path);
            byte[] bytes = { 23, 42, 17 };
            DummyScriptableObject source = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            File.WriteAllBytes(path, bytes);
            try
            {
                Assert.That(Helpers.CreateOrReplaceAsset(source, path), Is.Null);
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
                Assert.That(AssetDatabase.Contains(source), Is.False);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void CreateOrReplaceAssetRefusesUnsavableSources()
        {
            DummyScriptableObject source = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            source.hideFlags = HideFlags.DontSaveInEditor;
            string path = _scriptableFolder + "/Unsavable.asset";
            Assert.That(
                Helpers.TryCreateOrReplaceAsset(
                    source,
                    path,
                    out UnityEngine.Object saved,
                    out string error
                ),
                Is.False
            );
            Assert.That(saved, Is.Null);
            Assert.That(error, Is.Not.Empty);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(path), Is.Null);
        }

        [Test]
        public void CreateOrReplaceAssetSavesEditsToItsExistingSource()
        {
            string path = _scriptableFolder + "/Same.asset";
            TrackAssetPath(path);
            DummyScriptableObject source = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.CreateAsset(source, path);
                AssetDatabase.SaveAssets();
                source.value = 42;
                Assert.That(Helpers.CreateOrReplaceAsset(source, path), Is.SameAs(source));
                Resources.UnloadAsset(source);
                DummyScriptableObject reloaded = Track(
                    AssetDatabase.LoadAssetAtPath<DummyScriptableObject>(path)
                );
                Assert.That(reloaded.value, Is.EqualTo(42));
            });
        }

        [Test]
        public void CreateOrReplaceAssetRefusesTypeChangesAndPersistentMoves()
        {
            string path = _scriptableFolder + "/Retained.asset";
            string other = _scriptableFolder + "/Other.asset";
            TrackAssetPath(path);
            TrackAssetPath(other);
            DummyScriptableObject original = Track(
                ScriptableObject.CreateInstance<DummyScriptableObject>()
            );
            AnimationClip differentType = Track(new AnimationClip());
            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.CreateAsset(original, path);
                AssetDatabase.SaveAssets();
                string guid = AssetDatabase.AssetPathToGUID(path);
                Assert.That(
                    Helpers.TryCreateOrReplaceAsset(
                        differentType,
                        path,
                        out UnityEngine.Object saved,
                        out string error
                    ),
                    Is.False
                );
                Assert.That(saved, Is.Null);
                Assert.That(error, Is.Not.Empty);
                Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
                Assert.That(AssetDatabase.LoadMainAssetAtPath(path), Is.SameAs(original));
                Assert.That(Helpers.CreateOrReplaceAsset(original, other), Is.Null);
                Assert.That(AssetDatabase.LoadMainAssetAtPath(other), Is.Null);
                Assert.That(Helpers.CreateOrReplaceAsset(null, path), Is.Null);
                UnityEngine.Object.DestroyImmediate(differentType);
                Assert.That(Helpers.CreateOrReplaceAsset(differentType, path), Is.Null);
            });
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
