// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
#if UNITY_EDITOR && UNITY_6000_0_OR_NEWER
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.Build.Profile;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Tools;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("IdleEditorIsolation")]
    public sealed class BuildProfileSceneSyncTests : CommonTestBase
    {
        private const string ScenesPropertyName = "m_Scenes";
        private string _folder;
        private string _profilesFolder;
        private string _outsideFolder;
        private EditorBuildSettingsScene[] _originalScenes;
        private EditorBuildSettingsScene[] _scenes;
        private bool _originalEnabled;
        private string[] _originalFolders;
        private BuildProfile _originalActiveProfile;
        private EditorBuildSettingsScene[] _originalActiveScenes;
        private bool _originalActiveOverride;
        private bool _originalActiveDirty;
        private BuildProfile _inherited;
        private BuildProfile _custom;
        private BuildProfile _outside;

        private static BuildProfile CreateProfile(string path)
        {
            BuildProfile profile = ScriptableObject.CreateInstance<BuildProfile>(); // UNH-SUPPRESS UNH002: Fixture-owned asset is deleted with its folder in OneTimeTearDown.
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        private static IEnumerable<string> FailingFolders(string first)
        {
            yield return first;
            throw new InvalidOperationException("Folder enumeration failed.");
        }

        private static IEnumerable<string> ReentrantFolders(Action callback, string folder)
        {
            callback();
            yield return folder;
        }

        private static void SetScenes(
            BuildProfile profile,
            EditorBuildSettingsScene[] scenes,
            bool custom,
            bool save = true
        )
        {
            profile.overrideGlobalScenes = custom;
            using SerializedObject serialized = new(profile);
            using SerializedProperty property = serialized.FindProperty(ScenesPropertyName);
            Assert.IsTrue(property != null);
            property.arraySize = scenes.Length;
            for (int index = 0; index < scenes.Length; ++index)
            {
                using SerializedProperty entry = property.GetArrayElementAtIndex(index);
                entry.boxedValue = new EditorBuildSettingsScene
                {
                    guid = scenes[index].guid,
                    path = scenes[index].path,
                    enabled = scenes[index].enabled,
                };
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
            if (save)
            {
                AssetDatabase.SaveAssetIfDirty(profile);
            }
        }

        private static EditorBuildSettingsScene[] ReadScenes(BuildProfile profile)
        {
            using SerializedObject serialized = new(profile);
            using SerializedProperty property = serialized.FindProperty(ScenesPropertyName);
            Assert.IsTrue(property != null);
            EditorBuildSettingsScene[] scenes = new EditorBuildSettingsScene[property.arraySize];
            for (int index = 0; index < scenes.Length; ++index)
            {
                using SerializedProperty entry = property.GetArrayElementAtIndex(index);
                scenes[index] = entry.boxedValue as EditorBuildSettingsScene;
            }
            return scenes;
        }

        private static void AssertScenes(EditorBuildSettingsScene[] expected, BuildProfile profile)
        {
            EditorBuildSettingsScene[] actual = ReadScenes(profile);
            Assert.AreEqual(expected.Length, actual.Length);
            for (int index = 0; index < expected.Length; ++index)
            {
                Assert.IsTrue(actual[index] != null);
                Assert.AreEqual(expected[index].guid, actual[index].guid);
                Assert.AreEqual(expected[index].path, actual[index].path);
                Assert.AreEqual(expected[index].enabled, actual[index].enabled);
            }
        }

        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            DeferAssetCleanupToOneTimeTearDown = true;
            _originalScenes = EditorBuildSettings.globalScenes;
            _originalActiveProfile = BuildProfile.GetActiveBuildProfile();
            if (_originalActiveProfile != null)
            {
                _originalActiveDirty = EditorUtility.IsDirty(_originalActiveProfile);
                _originalActiveScenes = ReadScenes(_originalActiveProfile);
                _originalActiveOverride = _originalActiveProfile.overrideGlobalScenes;
            }
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            _originalEnabled = settings._autoSynchronizeBuildProfileScenes;
            _originalFolders = settings._buildProfileSceneFolders;
            settings._autoSynchronizeBuildProfileScenes = false;
            _folder = "Assets/" + nameof(BuildProfileSceneSyncTests) + Guid.NewGuid().ToString("N");
            EnsureFolder(_folder);
            _profilesFolder = _folder + "/Profiles";
            _outsideFolder = _folder + "/Outside";
            EnsureFolder(_profilesFolder);
            EnsureFolder(_outsideFolder);
            Scene scene = CreateTempScene(nameof(BuildProfileSceneSyncTests));
            _scenes = new EditorBuildSettingsScene[3];
            for (int index = 0; index < _scenes.Length; ++index)
            {
                string path = _folder + "/Scene" + index + ".unity";
                Assert.IsTrue(EditorSceneManager.SaveScene(scene, path, saveAsCopy: true));
                _scenes[index] = new EditorBuildSettingsScene(path, index != 1);
            }
            _inherited = CreateProfile(_profilesFolder + "/Inherited.asset");
            _custom = CreateProfile(_profilesFolder + "/Custom.asset");
            _outside = CreateProfile(_outsideFolder + "/Outside.asset");
        }

        [SetUp]
        public void PrepareProfiles()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings._autoSynchronizeBuildProfileScenes = false;
            settings._buildProfileSceneFolders = new[] { _profilesFolder };
            EditorBuildSettings.globalScenes = _scenes;
            SetScenes(_inherited, Array.Empty<EditorBuildSettingsScene>(), false);
            SetScenes(_custom, new[] { _scenes[2] }, true);
            SetScenes(_outside, Array.Empty<EditorBuildSettingsScene>(), false);
            Undo.ClearUndo(_inherited);
            Undo.ClearUndo(_custom);
            Undo.ClearUndo(_outside);
        }

        [TearDown]
        public void RestoreAutomaticPolicy()
        {
            UnityHelpersSettings.instance._autoSynchronizeBuildProfileScenes = false;
        }

        [UnitySetUp]
        public IEnumerator WaitForIdleEditor()
        {
            double deadline = EditorApplication.timeSinceStartup + 5;
            while (
                (EditorApplication.isUpdating || EditorApplication.isCompiling)
                && EditorApplication.timeSinceStartup < deadline
            )
            {
                yield return null;
            }

            Assert.IsFalse(
                EditorApplication.isUpdating,
                "Asset imports must complete before synchronization."
            );
            Assert.IsFalse(
                EditorApplication.isCompiling,
                "Compilation must complete before synchronization."
            );
            Assert.IsFalse(EditorApplication.isPlayingOrWillChangePlaymode);
            Assert.IsFalse(BuildPipeline.isBuildingPlayer);
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            try
            {
                UnityHelpersSettings settings = UnityHelpersSettings.instance;
                settings._autoSynchronizeBuildProfileScenes = false;
                EditorBuildSettings.globalScenes = _originalScenes;
                if (_originalActiveProfile != null)
                {
                    SetScenes(
                        _originalActiveProfile,
                        _originalActiveScenes,
                        _originalActiveOverride,
                        save: false
                    );
                    if (!_originalActiveDirty)
                    {
                        EditorUtility.ClearDirty(_originalActiveProfile);
                    }
                }
                settings._buildProfileSceneFolders = _originalFolders;
                settings._autoSynchronizeBuildProfileScenes = _originalEnabled;
            }
            finally
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(_folder) && AssetDatabase.IsValidFolder(_folder))
                    {
                        Assert.IsTrue(AssetDatabase.DeleteAsset(_folder));
                    }
                }
                finally
                {
                    base.OneTimeTearDown();
                }
            }
        }

        [Test]
        public void FailedSaveRestoresScenesAndCanBeRetried()
        {
            if (SystemInfo.operatingSystemFamily != OperatingSystemFamily.Windows)
            {
                Assert.Ignore("The native Windows file-sharing failure control requires Windows.");
            }
            string path = AssetDatabase.GetAssetPath(_inherited);
            byte[] original = File.ReadAllBytes(path);
            Regex saveError = new(
                "^File " + Regex.Escape(path) + " couldn't be written\\..*failed\\.$"
            );
            LogAssert.Expect(LogType.Error, saveError);
            LogAssert.Expect(LogType.Error, saveError);
            using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.IsFalse(
                    BuildProfileSceneSync.TrySynchronize(
                        new[] { _profilesFolder },
                        out int changed,
                        out string error
                    )
                );
                Assert.AreEqual(0, changed);
                Assert.IsNotEmpty(error);
            }
            AssertScenes(Array.Empty<EditorBuildSettingsScene>(), _inherited);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.IsTrue(
                BuildProfileSceneSync.TrySynchronize(
                    new[] { _profilesFolder },
                    out int updated,
                    out string retryError
                ),
                retryError
            );
            Assert.AreEqual(1, updated);
            AssertScenes(_scenes, _inherited);
            Assert.IsFalse(EditorUtility.IsDirty(_inherited));
            using (FileStream locked = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.IsTrue(
                    BuildProfileSceneSync.TrySynchronize(
                        new[] { _profilesFolder },
                        out updated,
                        out retryError
                    ),
                    retryError
                );
                Assert.AreEqual(0, updated);
            }
        }

        [Test]
        public void SynchronizesInheritedListsAndPreservesCustomListsAndOtherFolders()
        {
            bool success = BuildProfileSceneSync.TrySynchronize(
                new[] { _profilesFolder },
                out int updated,
                out string error
            );

            Assert.IsTrue(success, error);
            Assert.AreEqual(1, updated);
            AssertScenes(_scenes, _inherited);
            AssertScenes(new[] { _scenes[2] }, _custom);
            AssertScenes(Array.Empty<EditorBuildSettingsScene>(), _outside);
            Assert.IsFalse(_inherited.overrideGlobalScenes);
            Assert.IsTrue(_custom.overrideGlobalScenes);
            Assert.IsFalse(EditorUtility.IsDirty(_inherited));
        }

        [Test]
        public void RepeatedSynchronizationDoesNotRewriteMatchingProfiles()
        {
            string[] folders = { _profilesFolder };
            Assert.IsTrue(
                BuildProfileSceneSync.TrySynchronize(folders, out _, out string firstError),
                firstError
            );
            string path = AssetDatabase.GetAssetPath(_inherited);
            byte[] before = File.ReadAllBytes(path);
            DateTime written = File.GetLastWriteTimeUtc(path);

            Assert.IsTrue(
                BuildProfileSceneSync.TrySynchronize(folders, out int updated, out string error),
                error
            );

            Assert.AreEqual(0, updated);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            Assert.AreEqual(written, File.GetLastWriteTimeUtc(path));
            Assert.IsFalse(EditorUtility.IsDirty(_inherited));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void RepairsEverySceneListDifference(int difference)
        {
            EditorBuildSettingsScene[] previous = difference switch
            {
                0 => new[] { _scenes[0] },
                1 => new[] { _scenes[0], _scenes[0], _scenes[2] },
                2 => new[] { _scenes[2], _scenes[1], _scenes[0] },
                3 => new[]
                {
                    _scenes[0],
                    new EditorBuildSettingsScene(_scenes[1].guid, true),
                    _scenes[2],
                },
                _ => new[]
                {
                    new EditorBuildSettingsScene(_scenes[0].guid, _scenes[0].enabled)
                    {
                        path = "Assets/StaleScene.unity",
                    },
                    _scenes[1],
                    _scenes[2],
                },
            };
            SetScenes(_inherited, previous, false);

            Assert.IsTrue(
                BuildProfileSceneSync.TrySynchronize(
                    new[] { _profilesFolder },
                    out int updated,
                    out string error
                ),
                error
            );

            Assert.AreEqual(1, updated);
            AssertScenes(_scenes, _inherited);
            Undo.PerformUndo();
            AssertScenes(previous, _inherited);
            Undo.PerformRedo();
            AssertScenes(_scenes, _inherited);
        }

        [Test]
        public void EmptyGlobalScenesClearOnlyInheritedLists()
        {
            SetScenes(_inherited, _scenes, false);
            EditorBuildSettings.globalScenes = Array.Empty<EditorBuildSettingsScene>();

            Assert.IsTrue(
                BuildProfileSceneSync.TrySynchronize(
                    new[] { _profilesFolder },
                    out int updated,
                    out string error
                ),
                error
            );

            Assert.AreEqual(1, updated);
            AssertScenes(Array.Empty<EditorBuildSettingsScene>(), _inherited);
            AssertScenes(new[] { _scenes[2] }, _custom);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("Assets/DoesNotExist")]
        [TestCase("Packages")]
        public void InvalidFoldersFailBeforeChangingAnyProfile(string invalid)
        {
            string[] folders = { _profilesFolder, invalid };

            Assert.IsFalse(
                BuildProfileSceneSync.TrySynchronize(folders, out int updated, out string error)
            );

            Assert.AreEqual(0, updated);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            AssertScenes(Array.Empty<EditorBuildSettingsScene>(), _inherited);
        }

        [Test]
        public void EmptyFolderCollectionChangesNothing()
        {
            Assert.IsTrue(
                BuildProfileSceneSync.TrySynchronize(
                    Array.Empty<string>(),
                    out int updated,
                    out string error
                ),
                error
            );

            Assert.AreEqual(0, updated);
            AssertScenes(Array.Empty<EditorBuildSettingsScene>(), _inherited);
        }

        [Test]
        public void FailingFolderEnumerationPreservesProfiles()
        {
            IEnumerable<string> folders = FailingFolders(_profilesFolder);

            Assert.IsFalse(
                BuildProfileSceneSync.TrySynchronize(folders, out int updated, out string error)
            );

            Assert.AreEqual(0, updated);
            Assert.That(error, Does.Contain("Folder enumeration failed"));
            AssertScenes(Array.Empty<EditorBuildSettingsScene>(), _inherited);
        }

        [Test]
        public void ReentrantSynchronizationIsRefusedWithoutInterruptingTheOuterBatch()
        {
            bool innerSuccess = true;
            int innerUpdated = -1;
            string innerError = null;
            IEnumerable<string> folders = ReentrantFolders(
                () =>
                    innerSuccess = BuildProfileSceneSync.TrySynchronize(
                        Array.Empty<string>(),
                        out innerUpdated,
                        out innerError
                    ),
                _profilesFolder
            );

            Assert.IsTrue(
                BuildProfileSceneSync.TrySynchronize(folders, out int updated, out string error),
                error
            );

            Assert.IsFalse(innerSuccess);
            Assert.AreEqual(0, innerUpdated);
            Assert.IsFalse(string.IsNullOrWhiteSpace(innerError));
            Assert.AreEqual(1, updated);
            AssertScenes(_scenes, _inherited);
        }

        [Test]
        public void ConfiguredCommandUsesTheConfiguredFolderScope()
        {
            BuildProfileSceneSync.SynchronizeConfiguredProfiles();

            AssertScenes(_scenes, _inherited);
            AssertScenes(Array.Empty<EditorBuildSettingsScene>(), _outside);
            AssertScenes(new[] { _scenes[2] }, _custom);
        }

        [Test]
        public void SynchronizationLeavesUnrelatedDirtyAssetsUnsaved()
        {
            string outsidePath = AssetDatabase.GetAssetPath(_outside);
            byte[] before = File.ReadAllBytes(outsidePath);
            _outside.name = "Unsaved profile edit";
            EditorUtility.SetDirty(_outside);

            Assert.IsTrue(
                BuildProfileSceneSync.TrySynchronize(
                    new[] { _profilesFolder },
                    out _,
                    out string error
                ),
                error
            );

            Assert.IsTrue(EditorUtility.IsDirty(_outside));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(outsidePath));
        }

        [Test]
        public void LargeSceneListRetainsEveryEntryAndFlag()
        {
            const int sceneCount = 10001;
            EditorBuildSettingsScene[] expected = new EditorBuildSettingsScene[sceneCount];
            for (int index = 0; index < sceneCount; ++index)
            {
                expected[index] = new EditorBuildSettingsScene(
                    _scenes[index % _scenes.Length].guid,
                    (index & 1) == 0
                );
            }
            EditorBuildSettings.globalScenes = expected;
            Assert.AreEqual(sceneCount, EditorBuildSettings.globalScenes.Length);

            Assert.IsTrue(
                BuildProfileSceneSync.TrySynchronize(
                    new[] { _profilesFolder },
                    out int updated,
                    out string error
                ),
                error
            );

            Assert.AreEqual(1, updated);
            AssertScenes(expected, _inherited);
        }

        [Test]
        public void OverlappingFoldersUpdateEachProfileOnce()
        {
            string[] folders = { _folder, _profilesFolder, _folder };

            Assert.IsTrue(
                BuildProfileSceneSync.TrySynchronize(folders, out int updated, out string error),
                error
            );

            Assert.AreEqual(2, updated);
            AssertScenes(_scenes, _inherited);
            AssertScenes(_scenes, _outside);
            AssertScenes(new[] { _scenes[2] }, _custom);
        }

        [Test]
        public void SavedProfilesRetainTheirSceneListAfterReload()
        {
            Assert.IsTrue(
                BuildProfileSceneSync.TrySynchronize(
                    new[] { _profilesFolder },
                    out _,
                    out string error
                ),
                error
            );
            string path = AssetDatabase.GetAssetPath(_inherited);

            AssetDatabase.ImportAsset(
                path,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
            );

            BuildProfile loaded = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
            Assert.IsTrue(loaded != null);
            AssertScenes(_scenes, loaded);
            Assert.IsFalse(loaded.overrideGlobalScenes);
        }

        [UnityTest]
        public IEnumerator GlobalSceneEventSynchronizesWhenEnabled()
        {
            UnityHelpersSettings.instance._autoSynchronizeBuildProfileScenes = true;
            EditorBuildSettingsScene[] next = { _scenes[2], _scenes[0] };
            int sceneEvents = 0;
            void OnSceneChanged()
            {
                ++sceneEvents;
            }
            EditorBuildSettings.sceneListChanged += OnSceneChanged;
            try
            {
                EditorBuildSettings.scenes = next;
                for (
                    int frame = 0;
                    frame < 120 && ReadScenes(_inherited).Length != next.Length;
                    ++frame
                )
                {
                    yield return null;
                }
                Assert.Less(0, sceneEvents);
                AssertScenes(next, _inherited);
                AssertScenes(new[] { _scenes[2] }, _custom);
            }
            finally
            {
                EditorBuildSettings.sceneListChanged -= OnSceneChanged;
            }
        }

        [UnityTest]
        public IEnumerator GlobalSceneEventPreservesProfilesWhenDisabled()
        {
            EditorBuildSettings.scenes = new[] { _scenes[2], _scenes[0] };
            yield return null;
            yield return null;

            AssertScenes(Array.Empty<EditorBuildSettingsScene>(), _inherited);
        }
    }
#endif
}
