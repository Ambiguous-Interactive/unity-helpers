// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    public sealed class ValidationProjectFixTests : CommonTestBase
    {
        private string _folder;
        private bool _auto;
        private Scene _scene;
        private Scene _previous;

        [SetUp]
        public void SetUp()
        {
            _auto = ValidationAutoRun.Enabled;
            ValidationAutoRun.Enabled = false;
            _folder = "Assets/SentinelFix" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", _folder.Substring("Assets/".Length));
            TrackFolder(_folder);
            _previous = SceneManager.GetActiveScene();
        }

        [TearDown]
        public void RestoreState()
        {
            if (_scene.IsValid() && _scene.isLoaded)
                EditorSceneManager.CloseScene(_scene, true);
            if (_previous.IsValid() && _previous.isLoaded)
                SceneManager.SetActiveScene(_previous);
            ValidationAutoRun.Enabled = _auto;
        }

        [Test]
        public void ReorderedPrefabRefusesStaleFix()
        {
            string path = Prefab();
            ValidationWorkspaceSettings.RuleDefinition rule = Rule();
            List<ValidationFinding> findings = Scan(path, rule);
            GameObject edited = PrefabUtility.LoadPrefabContents(path);
            try
            {
                edited.transform.GetChild(0).SetSiblingIndex(1);
                PrefabUtility.SaveAsPrefabAsset(edited, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(edited);
            }
            Assert.Throws<InvalidOperationException>(() =>
                ValidationProjectFix.Apply(rule, findings[0])
            );
            Assert.AreEqual(
                2,
                AssetDatabase
                    .LoadAssetAtPath<GameObject>(path)
                    .GetComponentsInChildren<Rigidbody>()
                    .Length
            );
        }

        [Test]
        public void AssetReplacingOldPathCannotReceiveOldFindingFix()
        {
            string path = Prefab();
            ValidationWorkspaceSettings.RuleDefinition rule = Rule();
            ValidationFinding finding = Scan(path, rule)[0];
            Assert.IsEmpty(AssetDatabase.MoveAsset(path, _folder + "/Moved.prefab"));
            Prefab();
            Assert.Throws<InvalidOperationException>(() =>
                ValidationProjectFix.Apply(rule, finding)
            );
            Assert.AreEqual(
                2,
                AssetDatabase
                    .LoadAssetAtPath<GameObject>(path)
                    .GetComponentsInChildren<Rigidbody>()
                    .Length
            );
        }

        [Test]
        public void BulkFixCanRemoveTwoComponentsFromOnePrefab()
        {
            string path = Prefab();
            ValidationWorkspaceSettings.RuleDefinition rule = Rule();
            List<ValidationProjectFix.Request> requests = new List<ValidationProjectFix.Request>();
            foreach (ValidationFinding finding in Scan(path, rule))
                requests.Add(new ValidationProjectFix.Request(rule, finding));
            List<string> failures = new List<string>();
            List<Action> undo = ValidationProjectFix.ApplyMany(requests, failures);
            Assert.IsEmpty(failures);
            Assert.AreEqual(2, undo.Count);
            Assert.AreEqual(
                0,
                AssetDatabase
                    .LoadAssetAtPath<GameObject>(path)
                    .GetComponentsInChildren<Rigidbody>()
                    .Length
            );
            for (int index = undo.Count - 1; 0 <= index; index--)
                undo[index]();
            Assert.AreEqual(
                2,
                AssetDatabase
                    .LoadAssetAtPath<GameObject>(path)
                    .GetComponentsInChildren<Rigidbody>()
                    .Length
            );
        }

        [Test]
        public void BulkFixCanRemoveTwoAudioSourcesOnTheSameObject()
        {
            GameObject root = Track(new GameObject("Audio"));
            root.AddComponent<AudioSource>().spatialBlend = 1;
            root.AddComponent<AudioSource>().spatialBlend = 1;
            string path = _folder + "/Audio.prefab";
            Assert.IsTrue(PrefabUtility.SaveAsPrefabAsset(root, path) != null);
            ValidationWorkspaceSettings.RuleDefinition rule = Rule();
            rule.checks[0] = new ValidationWorkspaceSettings.RuleCondition
            {
                property = "AudioSource.spatialBlend",
                comparison = ">",
                value = "0.5",
            };
            List<ValidationProjectFix.Request> requests = new List<ValidationProjectFix.Request>();
            foreach (ValidationFinding finding in Scan(path, rule))
                requests.Add(new ValidationProjectFix.Request(rule, finding));
            Assert.AreEqual(2, requests.Count);
            List<string> failures = new List<string>();
            List<Action> undo = ValidationProjectFix.ApplyMany(requests, failures);
            Assert.IsEmpty(failures);
            Assert.AreEqual(2, undo.Count);
            Assert.AreEqual(
                0,
                AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponents<AudioSource>().Length
            );
            for (int index = undo.Count - 1; 0 <= index; index--)
                undo[index]();
            Assert.AreEqual(
                2,
                AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponents<AudioSource>().Length
            );
        }

        [Test]
        public void ClosedUnchangedSceneCanBeFixedAfterReopening()
        {
            string path = SavedScene();
            EditorSceneManager.CloseScene(_scene, true);
            ValidationWorkspaceSettings.RuleDefinition rule = Rule();
            rule.target = "Scenes";
            ValidationFinding finding = Scan(path, rule)[0];
            Action undo = ValidationProjectFix.Apply(rule, finding);
            _scene = SceneManager.GetSceneByPath(path);
            Assert.AreEqual(0, _scene.GetRootGameObjects()[0].GetComponents<Rigidbody>().Length);
            Assert.IsTrue(
                undo == null,
                "Scene component identity is restored through Unity's native Edit > Undo history."
            );
            Undo.PerformUndo();
            Assert.AreEqual(1, _scene.GetRootGameObjects()[0].GetComponents<Rigidbody>().Length);
        }

        [Test]
        public void SceneFixDoesNotExposeAnAmbiguousToastUndo()
        {
            string path = SavedScene();
            ValidationWorkspaceSettings.RuleDefinition rule = Rule();
            rule.target = "Scenes";
            Action undo = ValidationProjectFix.Apply(rule, Scan(path, rule)[0]);
            GameObject unrelated = Track(new GameObject("Original"));
            Undo.RecordObject(unrelated, "Unrelated rename");
            unrelated.name = "Changed";
            Assert.IsTrue(undo == null);
            Assert.AreEqual("Changed", unrelated.name);
            Assert.AreEqual(0, _scene.GetRootGameObjects()[0].GetComponents<Rigidbody>().Length);
            Undo.ClearUndo(unrelated);
        }

        [Test]
        public void ImporterUndoRefusesReplacementAtTheOriginalPath()
        {
            string path = _folder + "/Texture.png";
            Texture2D texture = Track(new Texture2D(2, 2));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
            ValidationWorkspaceSettings.RuleDefinition rule = Rule();
            rule.target = "Materials";
            rule.fix = "Set import max size";
            rule.fixValue = "128";
            rule.checks[0] = new ValidationWorkspaceSettings.RuleCondition
            {
                property = "Texture.maxSize",
                comparison = ">",
                value = "256",
            };
            Action undo = ValidationProjectFix.Apply(rule, Scan(path, rule)[0]);
            Assert.IsEmpty(AssetDatabase.MoveAsset(path, _folder + "/Moved.png"));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            TextureImporter replacement = AssetImporter.GetAtPath(path) as TextureImporter;
            replacement.maxTextureSize = 128;
            replacement.SaveAndReimport();
            Assert.Throws<InvalidOperationException>(() => undo());
            Assert.AreEqual(128, ((TextureImporter)AssetImporter.GetAtPath(path)).maxTextureSize);
        }

        private string Prefab()
        {
            GameObject root = Track(new GameObject("Root"));
            foreach (string name in new[] { "First", "Second" })
            {
                GameObject child = Track(new GameObject(name));
                child.transform.SetParent(root.transform);
                child.AddComponent<Rigidbody>().mass = 20;
            }
            string path = _folder + "/Subject.prefab";
            Assert.IsTrue(PrefabUtility.SaveAsPrefabAsset(root, path) != null);
            return path;
        }

        private string SavedScene()
        {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject subject = Track(new GameObject("Subject"));
            SceneManager.MoveGameObjectToScene(subject, _scene);
            subject.AddComponent<Rigidbody>().mass = 20;
            string path = _folder + "/Subject.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(_scene, path));
            return path;
        }

        private static ValidationWorkspaceSettings.RuleDefinition Rule()
        {
            return new ValidationWorkspaceSettings.RuleDefinition
            {
                id = "project.fix",
                name = "Heavy bodies",
                pathFilter = string.Empty,
                fix = "Remove component",
                checks = new List<ValidationWorkspaceSettings.RuleCondition>
                {
                    new ValidationWorkspaceSettings.RuleCondition
                    {
                        property = "Rigidbody.mass",
                        comparison = ">",
                        value = "10",
                    },
                },
            };
        }

        private static List<ValidationFinding> Scan(
            string path,
            ValidationWorkspaceSettings.RuleDefinition rule
        )
        {
            ValidationTarget target = new ValidationTarget(
                AssetDatabase.AssetPathToGUID(path),
                path,
                AssetDatabase.GetMainAssetTypeAtPath(path)
            );
            List<ValidationFinding> findings = new List<ValidationFinding>();
            new ValidationProjectRule(rule).Validate(
                in target,
                AssetDatabase.LoadMainAssetAtPath(path),
                findings
            );
            Assert.IsNotEmpty(findings);
            return findings;
        }
    }
}
