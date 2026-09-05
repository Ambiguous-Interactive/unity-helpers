// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Animation;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Tests for AnimationCreatorWindow save/load configuration functionality.
    /// </summary>
    [TestFixture]
    [Category("Slow")]
    [Category("Integration")]
    public sealed class AnimationCreatorWindowPersistenceTests : CommonTestBase
    {
        private AnimationCreatorWindow _window;
        private string _tempFolder;
        private string _tempFolderAbsolute;
        private DefaultAsset _tempFolderAsset;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _tempFolder = "Assets/TestAnimCreatorPersist_" + Guid.NewGuid().ToString("N")[..8];
            _tempFolderAbsolute = Path.GetFullPath(_tempFolder);
            if (!Directory.Exists(_tempFolderAbsolute))
            {
                Directory.CreateDirectory(_tempFolderAbsolute);
                AssetDatabase.Refresh();
            }
            _trackedFolders.Add(_tempFolder);
            _tempFolderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_tempFolder);

            _window = ScriptableObject.CreateInstance<AnimationCreatorWindow>();
            Track(_window);
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            if (Directory.Exists(_tempFolderAbsolute))
            {
                try
                {
                    Directory.Delete(_tempFolderAbsolute, true);
                    string metaPath = _tempFolderAbsolute + ".meta";
                    if (File.Exists(metaPath))
                    {
                        File.Delete(metaPath);
                    }
                    AssetDatabase.Refresh();
                }
                catch { }
            }
        }

        [Test]
        public void CreateConfigFromCurrentStateReturnsValidConfig()
        {
            _window.spriteNameRegex = "^Test_.*$";
            _window.autoRefresh = false;
            _window.groupingCaseInsensitive = false;
            _window.includeFolderNameInAnimName = true;
            _window.autoParseNamePrefix = "Prefix_";
            _window.autoParseNameSuffix = "_Suffix";

            AnimationCreatorConfig config = _window.CreateConfigFromCurrentState();

            Assert.IsTrue(config != null);
            Assert.AreEqual("^Test_.*$", config.spriteNameRegex);
            Assert.IsFalse(config.autoRefresh);
            Assert.IsFalse(config.groupingCaseInsensitive);
            Assert.IsTrue(config.includeFolderNameInAnimName);
            Assert.AreEqual("Prefix_", config.autoParseNamePrefix);
            Assert.AreEqual("_Suffix", config.autoParseNameSuffix);
        }

        [Test]
        public void CreateConfigFromCurrentStateIncludesAnimationData()
        {
            _window.animationData.Add(
                new AnimationData
                {
                    animationName = "TestAnim",
                    framesPerSecond = 24f,
                    loop = true,
                    framerateMode = FramerateMode.Curve,
                    cycleOffset = 0.5f,
                    framesPerSecondCurve = AnimationCurve.EaseInOut(0f, 6f, 1f, 18f),
                }
            );

            AnimationCreatorConfig config = _window.CreateConfigFromCurrentState();

            Assert.AreEqual(1, config.animationEntries.Count);
            Assert.AreEqual("TestAnim", config.animationEntries[0].animationName);
            Assert.AreEqual(24f, config.animationEntries[0].framesPerSecond, 0.0001f);
            Assert.IsTrue(config.animationEntries[0].loop);
            Assert.AreEqual(FramerateMode.Curve, config.animationEntries[0].framerateMode);
            Assert.AreEqual(0.5f, config.animationEntries[0].cycleOffset, 0.0001f);
        }

        [Test]
        public void ApplyConfigToCurrentStateUpdatesSettings()
        {
            AnimationCreatorConfig config = new()
            {
                spriteNameRegex = "^Applied_.*$",
                autoRefresh = false,
                groupingCaseInsensitive = false,
                includeFolderNameInAnimName = true,
                includeFullFolderPathInAnimName = true,
                autoParseNamePrefix = "Applied_",
                autoParseNameSuffix = "_Applied",
                useCustomGroupRegex = true,
                customGroupRegex = "test",
                customGroupRegexIgnoreCase = false,
                resolveDuplicateAnimationNames = false,
                strictNumericOrdering = true,
            };

            _window.ApplyConfigToCurrentState(config);

            Assert.AreEqual("^Applied_.*$", _window.spriteNameRegex);
            Assert.IsFalse(_window.autoRefresh);
            Assert.IsFalse(_window.groupingCaseInsensitive);
            Assert.IsTrue(_window.includeFolderNameInAnimName);
            Assert.IsTrue(_window.includeFullFolderPathInAnimName);
            Assert.AreEqual("Applied_", _window.autoParseNamePrefix);
            Assert.AreEqual("_Applied", _window.autoParseNameSuffix);
            Assert.IsTrue(_window.useCustomGroupRegex);
            Assert.AreEqual("test", _window.customGroupRegex);
            Assert.IsFalse(_window.customGroupRegexIgnoreCase);
            Assert.IsFalse(_window.resolveDuplicateAnimationNames);
            Assert.IsTrue(_window.strictNumericOrdering);
        }

        [Test]
        public void ApplyConfigToCurrentStateUpdatesAnimationData()
        {
            AnimationCreatorConfig config = new()
            {
                animationEntries = new List<AnimationCreatorConfig.AnimationDataEntry>
                {
                    new()
                    {
                        animationName = "Loaded",
                        framesPerSecond = 30f,
                        loop = true,
                        framerateMode = FramerateMode.Constant,
                    },
                },
            };

            _window.ApplyConfigToCurrentState(config);

            Assert.AreEqual(1, _window.animationData.Count);
            Assert.AreEqual("Loaded", _window.animationData[0].animationName);
            Assert.AreEqual(30f, _window.animationData[0].framesPerSecond, 0.0001f);
            Assert.IsTrue(_window.animationData[0].loop);
        }

        [Test]
        public void ApplyConfigWithNullDoesNotThrow()
        {
            Assert.DoesNotThrow(() => _window.ApplyConfigToCurrentState(null));
        }

        [Test]
        public void ApplyConfigClearsExistingAnimationData()
        {
            _window.animationData.Add(new AnimationData { animationName = "Existing" });

            AnimationCreatorConfig config = new()
            {
                animationEntries = new List<AnimationCreatorConfig.AnimationDataEntry>
                {
                    new() { animationName = "New" },
                },
            };

            _window.ApplyConfigToCurrentState(config);

            Assert.AreEqual(1, _window.animationData.Count);
            Assert.AreEqual("New", _window.animationData[0].animationName);
        }

        [Test]
        public void SaveConfigCreatesFile()
        {
            _window.spriteNameRegex = "^SaveTest_.*$";

            bool result = _window.SaveConfig(_tempFolder);

            Assert.IsTrue(result);
            string configPath = AnimationCreatorConfig.GetConfigPath(_tempFolder);
            string fullPath = Path.GetFullPath(configPath);
            Assert.IsTrue(File.Exists(fullPath));
        }

        [Test]
        public void SaveConfigWithNullPathReturnsFalse()
        {
            bool result = _window.SaveConfig(null);
            Assert.IsFalse(result);
        }

        [Test]
        public void SaveConfigWithEmptyPathReturnsFalse()
        {
            bool result = _window.SaveConfig(string.Empty);
            Assert.IsFalse(result);
        }

        [Test]
        public void LoadConfigAppliesSettings()
        {
            AnimationCreatorConfig config = new()
            {
                spriteNameRegex = "^Loaded_.*$",
                autoParseNamePrefix = "Loaded_",
            };
            string configPath = AnimationCreatorConfig.GetConfigPath(_tempFolder);
            string fullPath = Path.GetFullPath(configPath);
            string json = WallstopStudios.UnityHelpers.Core.Serialization.Serializer.JsonStringify(
                config,
                pretty: true
            );
            File.WriteAllText(fullPath, json, System.Text.Encoding.UTF8);

            bool result = _window.LoadConfig(_tempFolder);

            Assert.IsTrue(result);
            Assert.AreEqual("^Loaded_.*$", _window.spriteNameRegex);
            Assert.AreEqual("Loaded_", _window.autoParseNamePrefix);
        }

        [Test]
        public void LoadConfigWithNullPathReturnsFalse()
        {
            bool result = _window.LoadConfig(null);
            Assert.IsFalse(result);
        }

        [Test]
        public void LoadConfigWithEmptyPathReturnsFalse()
        {
            bool result = _window.LoadConfig(string.Empty);
            Assert.IsFalse(result);
        }

        [Test]
        public void LoadConfigWithMissingFileReturnsFalse()
        {
            bool result = _window.LoadConfig(_tempFolder);
            Assert.IsFalse(result);
        }

        [Test]
        public void SaveAndLoadRoundTrip()
        {
            _window.spriteNameRegex = "^RoundTrip_.*$";
            _window.autoParseNamePrefix = "RT_";
            _window.strictNumericOrdering = true;
            _window.animationData.Add(
                new AnimationData
                {
                    animationName = "RoundTripAnim",
                    framesPerSecond = 18f,
                    loop = true,
                }
            );

            _window.SaveConfig(_tempFolder);

            AnimationCreatorWindow newWindow =
                ScriptableObject.CreateInstance<AnimationCreatorWindow>();
            Track(newWindow);

            bool result = newWindow.LoadConfig(_tempFolder);

            Assert.IsTrue(result);
            Assert.AreEqual("^RoundTrip_.*$", newWindow.spriteNameRegex);
            Assert.AreEqual("RT_", newWindow.autoParseNamePrefix);
            Assert.IsTrue(newWindow.strictNumericOrdering);
            Assert.AreEqual(1, newWindow.animationData.Count);
            Assert.AreEqual("RoundTripAnim", newWindow.animationData[0].animationName);
            Assert.AreEqual(18f, newWindow.animationData[0].framesPerSecond, 0.0001f);
            Assert.IsTrue(newWindow.animationData[0].loop);
        }

        [Test]
        public void HasAnyConfigReturnsFalseWithNoSources()
        {
            bool result = _window.HasAnyConfig();
            Assert.IsFalse(result);
        }

        [Test]
        public void HasAnyConfigReturnsTrueWhenConfigExists()
        {
            _window.animationSources.Add(_tempFolderAsset);
            _window.SaveConfig(_tempFolder);

            bool result = _window.HasAnyConfig();

            Assert.IsTrue(result);
        }

        [Test]
        public void GetFoldersWithConfigsReturnsEmptyWithNoConfigs()
        {
            _window.animationSources.Add(_tempFolderAsset);

            List<string> folders = _window.GetFoldersWithConfigs();

            Assert.IsTrue(folders != null);
            Assert.AreEqual(0, folders.Count);
        }

        [Test]
        public void GetFoldersWithConfigsReturnsFoldersWithConfigs()
        {
            _window.animationSources.Add(_tempFolderAsset);
            _window.SaveConfig(_tempFolder);

            List<string> folders = _window.GetFoldersWithConfigs();

            Assert.AreEqual(1, folders.Count);
            Assert.AreEqual(_tempFolder, folders[0]);
        }

        [Test]
        public void ResetToDefaultResetsAllSettings()
        {
            _window.spriteNameRegex = "^Custom_.*$";
            _window.autoRefresh = false;
            _window.groupingCaseInsensitive = false;
            _window.includeFolderNameInAnimName = true;
            _window.autoParseNamePrefix = "Custom_";
            _window.animationData.Add(new AnimationData { animationName = "Custom" });

            _window.ResetToDefault();

            Assert.AreEqual(".*", _window.spriteNameRegex);
            Assert.IsTrue(_window.autoRefresh);
            Assert.IsTrue(_window.groupingCaseInsensitive);
            Assert.IsFalse(_window.includeFolderNameInAnimName);
            Assert.AreEqual(string.Empty, _window.autoParseNamePrefix);
            Assert.AreEqual(0, _window.animationData.Count);
        }

        [Test]
        public void DeleteConfigRemovesFile()
        {
            _window.SaveConfig(_tempFolder);
            string configPath = AnimationCreatorConfig.GetConfigPath(_tempFolder);
            string fullPath = Path.GetFullPath(configPath);
            Assert.IsTrue(File.Exists(fullPath));

            bool result = _window.DeleteConfig(_tempFolder);

            Assert.IsTrue(result);
            Assert.IsFalse(File.Exists(fullPath));
        }

        [Test]
        public void DeleteConfigWithNullPathReturnsFalse()
        {
            bool result = _window.DeleteConfig(null);
            Assert.IsFalse(result);
        }

        [Test]
        public void DeleteConfigWithMissingFileReturnsFalse()
        {
            bool result = _window.DeleteConfig(_tempFolder);
            Assert.IsFalse(result);
        }

        [Test]
        public void TryAutoLoadConfigsReturnsFalseWithNoSources()
        {
            bool result = _window.TryAutoLoadConfigs();
            Assert.IsFalse(result);
        }

        [Test]
        public void TryAutoLoadConfigsReturnsFalseWhenNoConfigExists()
        {
            _window.animationSources.Add(_tempFolderAsset);

            bool result = _window.TryAutoLoadConfigs();

            Assert.IsFalse(result);
        }

        [Test]
        public void TryAutoLoadConfigsLoadsExistingConfig()
        {
            AnimationCreatorConfig config = new() { spriteNameRegex = "^AutoLoaded_.*$" };
            string configPath = AnimationCreatorConfig.GetConfigPath(_tempFolder);
            string fullPath = Path.GetFullPath(configPath);
            string json = WallstopStudios.UnityHelpers.Core.Serialization.Serializer.JsonStringify(
                config,
                pretty: true
            );
            File.WriteAllText(fullPath, json, System.Text.Encoding.UTF8);

            _window.animationSources.Add(_tempFolderAsset);

            bool result = _window.TryAutoLoadConfigs();

            Assert.IsTrue(result);
            Assert.AreEqual("^AutoLoaded_.*$", _window.spriteNameRegex);
        }

        [Test]
        public void SaveAllConfigsSavesToAllSources()
        {
            string tempFolder2 =
                "Assets/TestAnimCreatorPersist2_" + Guid.NewGuid().ToString("N")[..8];
            string tempFolder2Absolute = Path.GetFullPath(tempFolder2);
            Directory.CreateDirectory(tempFolder2Absolute);
            AssetDatabase.Refresh();
            _trackedFolders.Add(tempFolder2);

            try
            {
                DefaultAsset folder2Asset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(
                    tempFolder2
                );
                _window.animationSources.Add(_tempFolderAsset);
                _window.animationSources.Add(folder2Asset);

                int savedCount = _window.SaveAllConfigs();

                Assert.AreEqual(2, savedCount);

                string config1Path = Path.GetFullPath(
                    AnimationCreatorConfig.GetConfigPath(_tempFolder)
                );
                string config2Path = Path.GetFullPath(
                    AnimationCreatorConfig.GetConfigPath(tempFolder2)
                );

                Assert.IsTrue(File.Exists(config1Path));
                Assert.IsTrue(File.Exists(config2Path));
            }
            finally
            {
                if (Directory.Exists(tempFolder2Absolute))
                {
                    Directory.Delete(tempFolder2Absolute, true);
                    string metaPath = tempFolder2Absolute + ".meta";
                    if (File.Exists(metaPath))
                    {
                        File.Delete(metaPath);
                    }
                    AssetDatabase.Refresh();
                }
            }
        }

        [Test]
        public void CurveDataPreservedThroughSaveLoad()
        {
            AnimationCurve original = AnimationCurve.EaseInOut(0f, 6f, 1f, 24f);
            original.preWrapMode = WrapMode.Loop;
            original.postWrapMode = WrapMode.PingPong;

            _window.animationData.Add(
                new AnimationData
                {
                    animationName = "CurveTest",
                    framerateMode = FramerateMode.Curve,
                    framesPerSecondCurve = original,
                }
            );

            _window.SaveConfig(_tempFolder);

            AnimationCreatorWindow newWindow =
                ScriptableObject.CreateInstance<AnimationCreatorWindow>();
            Track(newWindow);
            newWindow.LoadConfig(_tempFolder);

            AnimationCurve loaded = newWindow.animationData[0].framesPerSecondCurve;
            Assert.IsTrue(loaded != null);
            Assert.AreEqual(original.length, loaded.length);
            Assert.AreEqual(original.preWrapMode, loaded.preWrapMode);
            Assert.AreEqual(original.postWrapMode, loaded.postWrapMode);

            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                Assert.AreEqual(
                    original.Evaluate(t),
                    loaded.Evaluate(t),
                    0.01f,
                    $"Curve value at t={t}"
                );
            }
        }
    }
#endif
}
