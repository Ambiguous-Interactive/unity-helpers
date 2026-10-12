// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Integration")]
    public sealed class SceneCanonicalizerIntegrationTests : CommonTestBase
    {
        private string _folder;
        private string[] _previousAutomaticFolders;

        private static bool HasUntitledScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (string.IsNullOrEmpty(scene.path))
                {
                    return true;
                }
            }
            return false;
        }

        private static Scene CreateWorkingScene()
        {
            return EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                HasUntitledScene() ? NewSceneMode.Single : NewSceneMode.Additive
            );
        }

        private static Scene OpenWorkingScene(string path)
        {
            return EditorSceneManager.OpenScene(
                path,
                HasUntitledScene() ? OpenSceneMode.Single : OpenSceneMode.Additive
            );
        }

        private static void CloseWorkingScene(Scene scene)
        {
            if (1 < SceneManager.sceneCount)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
            else
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [SetUp]
        public void SetUpAssets()
        {
            _previousAutomaticFolders = SceneCanonicalizer.GetAutomaticFolders();
            Assert.That(SceneCanonicalizer.TrySetAutomaticFolders(null, out _), Is.True);
            _folder = "Assets/SceneCanonicalizer-" + Guid.NewGuid().ToString("N");
            EnsureFolder(_folder);
            TrackFolder(_folder);
        }

        [TearDown]
        public void RestoreAutomaticFolders()
        {
            Assert.That(
                SceneCanonicalizer.TrySetAutomaticFolders(_previousAutomaticFolders, out _),
                Is.True
            );
        }

        [Test]
        public void FreshSceneBakesMatchAndReloadWithReferencesIntact()
        {
            string firstPath = _folder + "/First.unity";
            string secondPath = _folder + "/Second.unity";
            SaveFreshScene(firstPath);
            SaveFreshScene(secondPath);
            string first = File.ReadAllText(firstPath);
            string second = File.ReadAllText(secondPath);
            Assert.That(first, Is.Not.EqualTo(SceneCanonicalizer.CanonicalizeYaml(first)));
            Assert.That(second, Is.Not.EqualTo(SceneCanonicalizer.CanonicalizeYaml(second)));
            Assert.That(
                SceneCanonicalizer.TryCanonicalizeFile(
                    firstPath,
                    out bool changed,
                    out Exception error
                ),
                Is.True,
                error?.ToString()
            );
            Assert.That(changed, Is.True);
            Assert.That(
                SceneCanonicalizer.TryCanonicalizeFile(secondPath, out changed, out error),
                Is.True,
                error?.ToString()
            );
            Assert.That(File.ReadAllBytes(firstPath), Is.EqualTo(File.ReadAllBytes(secondPath)));
            Scene loaded = OpenWorkingScene(firstPath);
            try
            {
                GameObject[] roots = loaded.GetRootGameObjects();
                Assert.That(roots.Length, Is.EqualTo(1));
                GameObject root = roots[0];
                Assert.That(root.name, Is.EqualTo("Root"));
                Assert.That(root.transform.childCount, Is.EqualTo(2));
                Assert.That(root.transform.GetChild(0).name, Is.EqualTo("Twin"));
                Assert.That(root.transform.GetChild(1).name, Is.EqualTo("Twin"));
                Rigidbody body = root.GetComponent<Rigidbody>();
                Assert.That(body, Is.Not.Null);
                Joint joint = root.transform.GetChild(0).GetComponent<FixedJoint>();
                Assert.That(joint, Is.Not.Null);
                Assert.That(joint.connectedBody, Is.SameAs(body));
                Assert.That(
                    root.transform.GetChild(1).localPosition,
                    Is.EqualTo(new Vector3(1, 2, 3))
                );
            }
            finally
            {
                CloseWorkingScene(loaded);
            }
        }

        [Test]
        public void CanonicalPrefabLoadsAndItsInstancesSurviveSceneCanonicalization()
        {
            string prefabPath = _folder + "/Generated.prefab";
            GameObject source = Track(new GameObject("Generated"));
            GameObject child = Track(new GameObject("Child"));
            child.transform.SetParent(source.transform);
            Rigidbody body = source.AddComponent<Rigidbody>();
            FixedJoint joint = child.AddComponent<FixedJoint>();
            joint.connectedBody = body;
            Assert.That(PrefabUtility.SaveAsPrefabAsset(source, prefabPath), Is.Not.Null);
            Assert.That(
                SceneCanonicalizer.TryCanonicalizeFile(prefabPath, out _, out Exception error),
                Is.True,
                error?.ToString()
            );
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(
                prefab.GetComponentInChildren<FixedJoint>().connectedBody,
                Is.SameAs(prefab.GetComponent<Rigidbody>())
            );
            string scenePath = _folder + "/PrefabInstances.unity";
            Scene scene = CreateWorkingScene();
            try
            {
                for (int i = 0; i < 2; ++i)
                {
                    GameObject instance = (GameObject)
                        PrefabUtility.InstantiatePrefab(prefab, scene);
                    Assert.That(instance, Is.Not.Null);
                }
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
            }
            finally
            {
                CloseWorkingScene(scene);
            }
            Assert.That(
                SceneCanonicalizer.TryCanonicalizeFile(scenePath, out _, out error),
                Is.True,
                error?.ToString()
            );
            Scene loaded = OpenWorkingScene(scenePath);
            try
            {
                GameObject[] roots = loaded.GetRootGameObjects();
                Assert.That(roots.Length, Is.EqualTo(2));
                foreach (GameObject root in roots)
                {
                    Assert.That(
                        AssetDatabase.GetAssetPath(
                            PrefabUtility.GetCorrespondingObjectFromSource(root)
                        ),
                        Is.EqualTo(prefabPath)
                    );
                    Assert.That(
                        root.GetComponentInChildren<FixedJoint>().connectedBody,
                        Is.SameAs(root.GetComponent<Rigidbody>())
                    );
                }
                string canonical = File.ReadAllText(scenePath);
                Assert.That(SceneCanonicalizer.CanonicalizeYaml(canonical), Is.EqualTo(canonical));
            }
            finally
            {
                CloseWorkingScene(loaded);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AddedSiblingsUnderPrefabParentsBakeAndReloadConsistently(bool prefabChildren)
        {
            string parentPath = _folder + "/Parent.prefab";
            string childPath = _folder + "/Nested.prefab";
            GameObject parentSource = Track(new GameObject("Parent"));
            GameObject childSource = Track(new GameObject("Nested"));
            Assert.That(PrefabUtility.SaveAsPrefabAsset(parentSource, parentPath), Is.Not.Null);
            Assert.That(PrefabUtility.SaveAsPrefabAsset(childSource, childPath), Is.Not.Null);
            foreach (string path in new[] { parentPath, childPath })
            {
                Assert.That(
                    SceneCanonicalizer.TryCanonicalizeFile(path, out _, out Exception error),
                    Is.True,
                    error?.ToString()
                );
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            GameObject parentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(parentPath);
            GameObject childPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(childPath);
            string scenePath = _folder + "/NestedSiblings.unity";
            string[] canonical = new string[2];
            for (int generation = 0; generation < 2; ++generation)
            {
                Scene scene = CreateWorkingScene();
                try
                {
                    GameObject parent = (GameObject)
                        PrefabUtility.InstantiatePrefab(parentPrefab, scene);
                    for (int i = 0; i < 2; ++i)
                    {
                        if (prefabChildren)
                        {
                            PrefabUtility.InstantiatePrefab(childPrefab, parent.transform);
                        }
                        else
                        {
                            GameObject child = Track(new GameObject("Nested"));
                            SceneManager.MoveGameObjectToScene(child, scene);
                            child.transform.SetParent(parent.transform);
                        }
                    }
                    Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                }
                finally
                {
                    CloseWorkingScene(scene);
                }
                Assert.That(
                    SceneCanonicalizer.TryCanonicalizeFile(scenePath, out _, out Exception error),
                    Is.True,
                    error?.ToString()
                );
                canonical[generation] = File.ReadAllText(scenePath);
                Scene loaded = OpenWorkingScene(scenePath);
                try
                {
                    GameObject[] roots = loaded.GetRootGameObjects();
                    Assert.That(roots.Length, Is.EqualTo(1));
                    Assert.That(roots[0].transform.childCount, Is.EqualTo(2));
                    for (int i = 0; i < 2; ++i)
                    {
                        GameObject child = roots[0].transform.GetChild(i).gameObject;
                        Assert.That(child.name, Is.EqualTo("Nested"));
                        if (prefabChildren)
                        {
                            Assert.That(
                                AssetDatabase.GetAssetPath(
                                    PrefabUtility.GetCorrespondingObjectFromSource(child)
                                ),
                                Is.EqualTo(childPath)
                            );
                        }
                    }
                }
                finally
                {
                    CloseWorkingScene(loaded);
                }
            }
            Assert.That(canonical[1], Is.EqualTo(canonical[0]));
        }

        [Test]
        public void AutomaticFoldersRespectBoundariesAndCanBeDisabled()
        {
            string[] paths = { _folder + "/", _folder, "Assets/Other" };
            Assert.That(
                SceneCanonicalizer.TrySetAutomaticFolders(paths, out Exception error),
                Is.True,
                error?.ToString()
            );
            paths[0] = "Assets/Changed";
            Assert.That(SceneCanonicalizer.GetAutomaticFolders().Length, Is.EqualTo(2));
            Assert.That(
                SceneCanonicalizer.IsAutomaticPath(_folder + "/Nested/Scene.unity"),
                Is.True
            );
            Assert.That(
                SceneCanonicalizer.IsAutomaticPath(_folder + "Suffix/Scene.unity"),
                Is.False
            );
            Assert.That(SceneCanonicalizer.IsAutomaticPath(_folder + "/Config.asset"), Is.False);
            Assert.That(
                SceneCanonicalizer.TrySetAutomaticFolders(new[] { "Assets/../Outside" }, out error),
                Is.False
            );
            Assert.That(error, Is.Not.Null);
            Assert.That(SceneCanonicalizer.IsAutomaticPath(_folder + "/Scene.unity"), Is.True);
            Assert.That(
                SceneCanonicalizer.TrySetAutomaticFolders(Array.Empty<string>(), out error),
                Is.True
            );
            Assert.That(SceneCanonicalizer.IsAutomaticPath(_folder + "/Scene.unity"), Is.False);
        }

        [Test]
        public void AutomaticImportCanonicalizesOnceWithoutReimportLoop()
        {
            string path = _folder + "/Automatic.unity";
            SaveFreshScene(path);
            string original = File.ReadAllText(path);
            Assert.That(
                SceneCanonicalizer.TrySetAutomaticFolders(new[] { _folder }, out _),
                Is.True
            );
            SceneCanonicalizationPostprocessor.QueuePaths(new[] { path, path });
            SceneCanonicalizationPostprocessor.DrainPendingPaths();
            string canonical = File.ReadAllText(path);
            Assert.That(canonical, Is.Not.EqualTo(original));
            Assert.That(canonical, Is.EqualTo(SceneCanonicalizer.CanonicalizeYaml(original)));
            SceneCanonicalizationPostprocessor.QueuePaths(new[] { path });
            SceneCanonicalizationPostprocessor.DrainPendingPaths();
            Assert.That(File.ReadAllText(path), Is.EqualTo(canonical));
        }

        [Test]
        public void AutomaticImportSkipsOpenScenesAndPendingWorkAfterDisable()
        {
            string path = _folder + "/Open.unity";
            SaveFreshScene(path);
            string original = File.ReadAllText(path);
            Scene open = OpenWorkingScene(path);
            try
            {
                Assert.That(
                    SceneCanonicalizer.TrySetAutomaticFolders(new[] { _folder }, out _),
                    Is.True
                );
                SceneCanonicalizationPostprocessor.QueuePaths(new[] { path });
                SceneCanonicalizationPostprocessor.DrainPendingPaths();
                Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            }
            finally
            {
                CloseWorkingScene(open);
            }
            SceneCanonicalizationPostprocessor.QueuePaths(new[] { path });
            Assert.That(SceneCanonicalizer.TrySetAutomaticFolders(null, out _), Is.True);
            SceneCanonicalizationPostprocessor.DrainPendingPaths();
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        }

        private void SaveFreshScene(string path)
        {
            Scene scene = CreateWorkingScene();
            try
            {
                GameObject root = Track(new GameObject("Root"));
                SceneManager.MoveGameObjectToScene(root, scene);
                Rigidbody body = root.AddComponent<Rigidbody>();
                for (int i = 0; i < 2; ++i)
                {
                    GameObject child = Track(new GameObject("Twin"));
                    child.transform.SetParent(root.transform);
                    child.transform.localPosition = new Vector3(i, i * 2, i * 3);
                    if (i == 0)
                    {
                        FixedJoint joint = child.AddComponent<FixedJoint>();
                        joint.connectedBody = body;
                    }
                }
                Assert.That(EditorSceneManager.SaveScene(scene, path), Is.True);
            }
            finally
            {
                CloseWorkingScene(scene);
            }
        }
    }
}
