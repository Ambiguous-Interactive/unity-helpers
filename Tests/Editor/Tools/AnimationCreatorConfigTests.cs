// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Animation;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Exhaustive test suite for AnimationCreatorConfig functionality including
    /// serialization, deserialization, curve conversion, path generation, and migration.
    /// </summary>
    [TestFixture]
    [Category("Slow")]
    [Category("Integration")]
    public sealed class AnimationCreatorConfigTests : CommonTestBase
    {
        private string _tempFolder;
        private string _tempFolderAbsolute;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _tempFolder = "Assets/TestAnimationCreatorConfig_" + Guid.NewGuid().ToString("N")[..8];
            _tempFolderAbsolute = Path.GetFullPath(_tempFolder);
            if (!Directory.Exists(_tempFolderAbsolute))
            {
                Directory.CreateDirectory(_tempFolderAbsolute);
                AssetDatabase.Refresh();
            }
            _trackedFolders.Add(_tempFolder);
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

        private static IEnumerable<TestCaseData> ConfigPathTestCases()
        {
            yield return new TestCaseData(
                "Assets/Sprites",
                "Assets/Sprites/.animation-creator.json"
            ).SetName("ConfigPath.Normal.AssetsSprites");
            yield return new TestCaseData(
                "Assets/Art/Characters",
                "Assets/Art/Characters/.animation-creator.json"
            ).SetName("ConfigPath.Normal.NestedFolder");
            yield return new TestCaseData(
                "Assets/Folder/",
                "Assets/Folder/.animation-creator.json"
            ).SetName("ConfigPath.Edge.TrailingSlash");
            yield return new TestCaseData(
                "Assets/Folder\\",
                "Assets/Folder/.animation-creator.json"
            ).SetName("ConfigPath.Edge.TrailingBackslash");
            yield return new TestCaseData("", "").SetName("ConfigPath.Edge.EmptyPath");
            yield return new TestCaseData(null, "").SetName("ConfigPath.Edge.NullPath");
        }

        [TestCaseSource(nameof(ConfigPathTestCases))]
        public void GetConfigPathReturnsExpectedPath(string folderPath, string expectedPath)
        {
            string result = AnimationCreatorConfig.GetConfigPath(folderPath);
            Assert.AreEqual(expectedPath, result);
        }

        [Test]
        public void DefaultConstructorInitializesFieldsCorrectly()
        {
            AnimationCreatorConfig config = new();

            Assert.AreEqual(AnimationCreatorConfig.CurrentVersion, config.version);
            Assert.AreEqual(".*", config.spriteNameRegex);
            Assert.IsTrue(config.autoRefresh);
            Assert.IsTrue(config.groupingCaseInsensitive);
            Assert.IsFalse(config.includeFolderNameInAnimName);
            Assert.IsFalse(config.includeFullFolderPathInAnimName);
            Assert.AreEqual(string.Empty, config.autoParseNamePrefix);
            Assert.AreEqual(string.Empty, config.autoParseNameSuffix);
            Assert.IsFalse(config.useCustomGroupRegex);
            Assert.AreEqual(string.Empty, config.customGroupRegex);
            Assert.IsTrue(config.customGroupRegexIgnoreCase);
            Assert.IsTrue(config.resolveDuplicateAnimationNames);
            Assert.IsFalse(config.strictNumericOrdering);
            Assert.IsTrue(config.animationEntries != null);
            Assert.AreEqual(0, config.animationEntries.Count);
        }

        [Test]
        public void AnimationDataEntryDefaultConstructorInitializesCorrectly()
        {
            AnimationCreatorConfig.AnimationDataEntry entry = new();

            Assert.AreEqual(string.Empty, entry.animationName);
            Assert.IsTrue(entry.framePaths != null);
            Assert.AreEqual(0, entry.framePaths.Count);
            Assert.AreEqual(AnimationData.DefaultFramesPerSecond, entry.framesPerSecond);
            Assert.IsFalse(entry.isCreatedFromAutoParse);
            Assert.IsFalse(entry.loop);
            Assert.AreEqual(FramerateMode.Constant, entry.framerateMode);
            Assert.IsTrue(entry.curveKeyframes != null);
            Assert.AreEqual(0, entry.curveKeyframes.Count);
            Assert.AreEqual(WrapMode.Clamp, entry.curvePreWrapMode);
            Assert.AreEqual(WrapMode.Clamp, entry.curvePostWrapMode);
            Assert.AreEqual(0f, entry.cycleOffset);
        }

        [Test]
        public void CurveKeyframeFromKeyframePreservesAllProperties()
        {
            Keyframe source = new(0.5f, 12f, 1f, 2f, 0.3f, 0.7f)
            {
                weightedMode = WeightedMode.Both,
            };

            AnimationCreatorConfig.CurveKeyframe result =
                AnimationCreatorConfig.CurveKeyframe.FromKeyframe(source);

            Assert.AreEqual(0.5f, result.time, 0.0001f);
            Assert.AreEqual(12f, result.value, 0.0001f);
            Assert.AreEqual(1f, result.inTangent, 0.0001f);
            Assert.AreEqual(2f, result.outTangent, 0.0001f);
            Assert.AreEqual(0.3f, result.inWeight, 0.0001f);
            Assert.AreEqual(0.7f, result.outWeight, 0.0001f);
            Assert.AreEqual(WeightedMode.Both, result.weightedMode);
        }

        [Test]
        public void CurveKeyframeToKeyframePreservesAllProperties()
        {
            AnimationCreatorConfig.CurveKeyframe source = new()
            {
                time = 0.25f,
                value = 24f,
                inTangent = -1f,
                outTangent = 1f,
                inWeight = 0.5f,
                outWeight = 0.5f,
                weightedMode = WeightedMode.In,
            };

            Keyframe result = source.ToKeyframe();

            Assert.AreEqual(0.25f, result.time, 0.0001f);
            Assert.AreEqual(24f, result.value, 0.0001f);
            Assert.AreEqual(-1f, result.inTangent, 0.0001f);
            Assert.AreEqual(1f, result.outTangent, 0.0001f);
            Assert.AreEqual(0.5f, result.inWeight, 0.0001f);
            Assert.AreEqual(0.5f, result.outWeight, 0.0001f);
            Assert.AreEqual(WeightedMode.In, result.weightedMode);
        }

        [Test]
        public void SerializeCurveWithNullReturnsEmptyList()
        {
            List<AnimationCreatorConfig.CurveKeyframe> result =
                AnimationCreatorConfig.SerializeCurve(null);

            Assert.IsTrue(result != null);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void SerializeCurvePreservesAllKeyframes()
        {
            AnimationCurve curve = new(
                new Keyframe(0f, 6f),
                new Keyframe(0.5f, 12f),
                new Keyframe(1f, 24f)
            );

            List<AnimationCreatorConfig.CurveKeyframe> result =
                AnimationCreatorConfig.SerializeCurve(curve);

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(0f, result[0].time, 0.0001f);
            Assert.AreEqual(6f, result[0].value, 0.0001f);
            Assert.AreEqual(0.5f, result[1].time, 0.0001f);
            Assert.AreEqual(12f, result[1].value, 0.0001f);
            Assert.AreEqual(1f, result[2].time, 0.0001f);
            Assert.AreEqual(24f, result[2].value, 0.0001f);
        }

        [Test]
        public void DeserializeCurveWithNullReturnsDefaultCurve()
        {
            AnimationCurve result = AnimationCreatorConfig.DeserializeCurve(
                null,
                WrapMode.Loop,
                WrapMode.PingPong
            );

            Assert.IsTrue(result != null);
            Assert.AreEqual(AnimationData.DefaultFramesPerSecond, result.Evaluate(0.5f), 0.0001f);
        }

        [Test]
        public void DeserializeCurveWithEmptyListReturnsDefaultCurve()
        {
            AnimationCurve result = AnimationCreatorConfig.DeserializeCurve(
                new List<AnimationCreatorConfig.CurveKeyframe>(),
                WrapMode.Loop,
                WrapMode.PingPong
            );

            Assert.IsTrue(result != null);
            Assert.AreEqual(AnimationData.DefaultFramesPerSecond, result.Evaluate(0.5f), 0.0001f);
        }

        [Test]
        public void DeserializeCurvePreservesWrapModes()
        {
            List<AnimationCreatorConfig.CurveKeyframe> keyframes = new()
            {
                new() { time = 0f, value = 10f },
                new() { time = 1f, value = 20f },
            };

            AnimationCurve result = AnimationCreatorConfig.DeserializeCurve(
                keyframes,
                WrapMode.Loop,
                WrapMode.PingPong
            );

            Assert.AreEqual(WrapMode.Loop, result.preWrapMode);
            Assert.AreEqual(WrapMode.PingPong, result.postWrapMode);
        }

        [Test]
        public void CurveRoundTripPreservesData()
        {
            AnimationCurve original = AnimationCurve.EaseInOut(0f, 8f, 1f, 24f);
            original.preWrapMode = WrapMode.Loop;
            original.postWrapMode = WrapMode.ClampForever;

            List<AnimationCreatorConfig.CurveKeyframe> serialized =
                AnimationCreatorConfig.SerializeCurve(original);

            AnimationCurve restored = AnimationCreatorConfig.DeserializeCurve(
                serialized,
                original.preWrapMode,
                original.postWrapMode
            );

            Assert.AreEqual(original.length, restored.length);
            Assert.AreEqual(original.preWrapMode, restored.preWrapMode);
            Assert.AreEqual(original.postWrapMode, restored.postWrapMode);

            for (int i = 0; i < original.length; i++)
            {
                Assert.AreEqual(original[i].time, restored[i].time, 0.0001f);
                Assert.AreEqual(original[i].value, restored[i].value, 0.0001f);
            }
        }

        [Test]
        public void MigrateConfigWithNullDoesNotThrow()
        {
            Assert.DoesNotThrow(() => AnimationCreatorConfig.MigrateConfig(null));
        }

        [Test]
        public void MigrateConfigWithCurrentVersionDoesNotModify()
        {
            AnimationCreatorConfig config = new()
            {
                version = AnimationCreatorConfig.CurrentVersion,
                spriteNameRegex = "test",
            };

            AnimationCreatorConfig.MigrateConfig(config);

            Assert.AreEqual(AnimationCreatorConfig.CurrentVersion, config.version);
            Assert.AreEqual("test", config.spriteNameRegex);
        }

        [Test]
        public void MigrateConfigWithOldVersionUpdatesToCurrentVersion()
        {
            AnimationCreatorConfig config = new() { version = 0 };

            AnimationCreatorConfig.MigrateConfig(config);

            Assert.AreEqual(AnimationCreatorConfig.CurrentVersion, config.version);
        }

        [Test]
        public void JsonSerializationRoundTripPreservesAllSettings()
        {
            AnimationCreatorConfig original = new()
            {
                version = AnimationCreatorConfig.CurrentVersion,
                spriteNameRegex = "^Player_.*$",
                autoRefresh = false,
                groupingCaseInsensitive = false,
                includeFolderNameInAnimName = true,
                includeFullFolderPathInAnimName = true,
                autoParseNamePrefix = "Anim_",
                autoParseNameSuffix = "_v2",
                useCustomGroupRegex = true,
                customGroupRegex = "(?<base>\\w+)_(?<index>\\d+)",
                customGroupRegexIgnoreCase = false,
                resolveDuplicateAnimationNames = false,
                strictNumericOrdering = true,
                animationEntries = new List<AnimationCreatorConfig.AnimationDataEntry>
                {
                    new()
                    {
                        animationName = "Walk",
                        framesPerSecond = 15f,
                        isCreatedFromAutoParse = true,
                        loop = true,
                        framerateMode = FramerateMode.Curve,
                        cycleOffset = 0.25f,
                        framePaths = new List<string>
                        {
                            "Assets/Sprites/Walk_01.png",
                            "Assets/Sprites/Walk_02.png",
                        },
                        curveKeyframes = AnimationCreatorConfig.SerializeCurve(
                            AnimationCurve.EaseInOut(0f, 6f, 1f, 18f)
                        ),
                        curvePreWrapMode = WrapMode.Loop,
                        curvePostWrapMode = WrapMode.PingPong,
                    },
                },
            };

            string json = Serializer.JsonStringify(original, pretty: true);
            AnimationCreatorConfig restored = Serializer.JsonDeserialize<AnimationCreatorConfig>(
                json
            );

            Assert.IsTrue(restored != null);
            Assert.AreEqual(original.version, restored.version);
            Assert.AreEqual(original.spriteNameRegex, restored.spriteNameRegex);
            Assert.AreEqual(original.autoRefresh, restored.autoRefresh);
            Assert.AreEqual(original.groupingCaseInsensitive, restored.groupingCaseInsensitive);
            Assert.AreEqual(
                original.includeFolderNameInAnimName,
                restored.includeFolderNameInAnimName
            );
            Assert.AreEqual(
                original.includeFullFolderPathInAnimName,
                restored.includeFullFolderPathInAnimName
            );
            Assert.AreEqual(original.autoParseNamePrefix, restored.autoParseNamePrefix);
            Assert.AreEqual(original.autoParseNameSuffix, restored.autoParseNameSuffix);
            Assert.AreEqual(original.useCustomGroupRegex, restored.useCustomGroupRegex);
            Assert.AreEqual(original.customGroupRegex, restored.customGroupRegex);
            Assert.AreEqual(
                original.customGroupRegexIgnoreCase,
                restored.customGroupRegexIgnoreCase
            );
            Assert.AreEqual(
                original.resolveDuplicateAnimationNames,
                restored.resolveDuplicateAnimationNames
            );
            Assert.AreEqual(original.strictNumericOrdering, restored.strictNumericOrdering);

            Assert.AreEqual(1, restored.animationEntries.Count);
            AnimationCreatorConfig.AnimationDataEntry restoredEntry = restored.animationEntries[0];
            Assert.AreEqual("Walk", restoredEntry.animationName);
            Assert.AreEqual(15f, restoredEntry.framesPerSecond, 0.0001f);
            Assert.IsTrue(restoredEntry.isCreatedFromAutoParse);
            Assert.IsTrue(restoredEntry.loop);
            Assert.AreEqual(FramerateMode.Curve, restoredEntry.framerateMode);
            Assert.AreEqual(0.25f, restoredEntry.cycleOffset, 0.0001f);
            Assert.AreEqual(2, restoredEntry.framePaths.Count);
            Assert.AreEqual(WrapMode.Loop, restoredEntry.curvePreWrapMode);
            Assert.AreEqual(WrapMode.PingPong, restoredEntry.curvePostWrapMode);
        }

        [Test]
        public void FileNameConstantIsCorrect()
        {
            Assert.AreEqual(".animation-creator.json", AnimationCreatorConfig.FileName);
        }

        [Test]
        public void CurrentVersionIsOne()
        {
            Assert.AreEqual(1, AnimationCreatorConfig.CurrentVersion);
        }

        [Test]
        public void SaveAndLoadConfigFileRoundTrip()
        {
            AnimationCreatorConfig original = new()
            {
                spriteNameRegex = "^Enemy_.*$",
                autoParseNamePrefix = "AI_",
                animationEntries = new List<AnimationCreatorConfig.AnimationDataEntry>
                {
                    new()
                    {
                        animationName = "Attack",
                        framesPerSecond = 24f,
                        loop = false,
                    },
                },
            };

            string configPath = AnimationCreatorConfig.GetConfigPath(_tempFolder);
            string fullPath = Path.GetFullPath(configPath);

            string json = Serializer.JsonStringify(original, pretty: true);
            File.WriteAllText(fullPath, json, Encoding.UTF8);

            Assert.IsTrue(File.Exists(fullPath));

            string loadedJson = File.ReadAllText(fullPath, Encoding.UTF8);
            AnimationCreatorConfig loaded = Serializer.JsonDeserialize<AnimationCreatorConfig>(
                loadedJson
            );

            Assert.IsTrue(loaded != null);
            Assert.AreEqual("^Enemy_.*$", loaded.spriteNameRegex);
            Assert.AreEqual("AI_", loaded.autoParseNamePrefix);
            Assert.AreEqual(1, loaded.animationEntries.Count);
            Assert.AreEqual("Attack", loaded.animationEntries[0].animationName);
        }

        [Test]
        public void EmptyAnimationEntriesListSerializesCorrectly()
        {
            AnimationCreatorConfig config = new()
            {
                animationEntries = new List<AnimationCreatorConfig.AnimationDataEntry>(),
            };

            string json = Serializer.JsonStringify(config, pretty: true);
            AnimationCreatorConfig restored = Serializer.JsonDeserialize<AnimationCreatorConfig>(
                json
            );

            Assert.IsTrue(restored.animationEntries != null);
            Assert.AreEqual(0, restored.animationEntries.Count);
        }

        [Test]
        public void EmptyFramePathsListSerializesCorrectly()
        {
            AnimationCreatorConfig.AnimationDataEntry entry = new()
            {
                animationName = "Test",
                framePaths = new List<string>(),
            };

            AnimationCreatorConfig config = new()
            {
                animationEntries = new List<AnimationCreatorConfig.AnimationDataEntry> { entry },
            };

            string json = Serializer.JsonStringify(config, pretty: true);
            AnimationCreatorConfig restored = Serializer.JsonDeserialize<AnimationCreatorConfig>(
                json
            );

            Assert.IsTrue(restored.animationEntries[0].framePaths != null);
            Assert.AreEqual(0, restored.animationEntries[0].framePaths.Count);
        }

        [Test]
        public void MultipleAnimationEntriesSerializeCorrectly()
        {
            AnimationCreatorConfig config = new()
            {
                animationEntries = new List<AnimationCreatorConfig.AnimationDataEntry>
                {
                    new() { animationName = "Idle", framesPerSecond = 8f },
                    new() { animationName = "Walk", framesPerSecond = 12f },
                    new() { animationName = "Run", framesPerSecond = 16f },
                    new() { animationName = "Attack", framesPerSecond = 24f },
                },
            };

            string json = Serializer.JsonStringify(config, pretty: true);
            AnimationCreatorConfig restored = Serializer.JsonDeserialize<AnimationCreatorConfig>(
                json
            );

            Assert.AreEqual(4, restored.animationEntries.Count);
            Assert.AreEqual("Idle", restored.animationEntries[0].animationName);
            Assert.AreEqual(8f, restored.animationEntries[0].framesPerSecond, 0.0001f);
            Assert.AreEqual("Walk", restored.animationEntries[1].animationName);
            Assert.AreEqual(12f, restored.animationEntries[1].framesPerSecond, 0.0001f);
            Assert.AreEqual("Run", restored.animationEntries[2].animationName);
            Assert.AreEqual(16f, restored.animationEntries[2].framesPerSecond, 0.0001f);
            Assert.AreEqual("Attack", restored.animationEntries[3].animationName);
            Assert.AreEqual(24f, restored.animationEntries[3].framesPerSecond, 0.0001f);
        }

        [Test]
        public void SpecialCharactersInRegexSerializeCorrectly()
        {
            AnimationCreatorConfig config = new()
            {
                spriteNameRegex = @"^(?<base>\w+)_(?<index>\d{2,3})\.png$",
                customGroupRegex = @"(?<base>[A-Za-z]+)[-_.](?<index>\d+)",
            };

            string json = Serializer.JsonStringify(config, pretty: true);
            AnimationCreatorConfig restored = Serializer.JsonDeserialize<AnimationCreatorConfig>(
                json
            );

            Assert.AreEqual(config.spriteNameRegex, restored.spriteNameRegex);
            Assert.AreEqual(config.customGroupRegex, restored.customGroupRegex);
        }

        [Test]
        public void AllFramerateModesSerializeCorrectly()
        {
            AnimationCreatorConfig config = new()
            {
                animationEntries = new List<AnimationCreatorConfig.AnimationDataEntry>
                {
                    new() { animationName = "Constant", framerateMode = FramerateMode.Constant },
                    new() { animationName = "Curve", framerateMode = FramerateMode.Curve },
                },
            };

            string json = Serializer.JsonStringify(config, pretty: true);
            AnimationCreatorConfig restored = Serializer.JsonDeserialize<AnimationCreatorConfig>(
                json
            );

            Assert.AreEqual(FramerateMode.Constant, restored.animationEntries[0].framerateMode);
            Assert.AreEqual(FramerateMode.Curve, restored.animationEntries[1].framerateMode);
        }

        [Test]
        public void AllWrapModesSerializeCorrectly()
        {
            WrapMode[] modes =
            {
                WrapMode.Default,
                WrapMode.Clamp,
                WrapMode.ClampForever,
                WrapMode.Loop,
                WrapMode.PingPong,
            };

            foreach (WrapMode preMode in modes)
            {
                foreach (WrapMode postMode in modes)
                {
                    AnimationCreatorConfig.AnimationDataEntry entry = new()
                    {
                        animationName = $"Test_{preMode}_{postMode}",
                        curvePreWrapMode = preMode,
                        curvePostWrapMode = postMode,
                    };

                    AnimationCreatorConfig config = new()
                    {
                        animationEntries = new List<AnimationCreatorConfig.AnimationDataEntry>
                        {
                            entry,
                        },
                    };

                    string json = Serializer.JsonStringify(config, pretty: true);
                    AnimationCreatorConfig restored =
                        Serializer.JsonDeserialize<AnimationCreatorConfig>(json);

                    Assert.AreEqual(
                        preMode,
                        restored.animationEntries[0].curvePreWrapMode,
                        $"PreWrapMode {preMode} should serialize correctly"
                    );
                    Assert.AreEqual(
                        postMode,
                        restored.animationEntries[0].curvePostWrapMode,
                        $"PostWrapMode {postMode} should serialize correctly"
                    );
                }
            }
        }

        [Test]
        public void ExtremeFloatValuesSerializeCorrectly()
        {
            AnimationCreatorConfig.AnimationDataEntry entry = new()
            {
                animationName = "Extreme",
                framesPerSecond = 0.001f,
                cycleOffset = 0.999f,
            };

            AnimationCreatorConfig config = new()
            {
                animationEntries = new List<AnimationCreatorConfig.AnimationDataEntry> { entry },
            };

            string json = Serializer.JsonStringify(config, pretty: true);
            AnimationCreatorConfig restored = Serializer.JsonDeserialize<AnimationCreatorConfig>(
                json
            );

            Assert.AreEqual(0.001f, restored.animationEntries[0].framesPerSecond, 0.00001f);
            Assert.AreEqual(0.999f, restored.animationEntries[0].cycleOffset, 0.00001f);
        }

        [Test]
        public void LargeNumberOfKeyframesSerializesCorrectly()
        {
            AnimationCurve curve = new();
            for (int i = 0; i < 100; i++)
            {
                float t = i / 99f;
                curve.AddKey(new Keyframe(t, 6f + 18f * t));
            }

            List<AnimationCreatorConfig.CurveKeyframe> serialized =
                AnimationCreatorConfig.SerializeCurve(curve);

            Assert.AreEqual(100, serialized.Count);

            AnimationCurve restored = AnimationCreatorConfig.DeserializeCurve(
                serialized,
                WrapMode.Clamp,
                WrapMode.Clamp
            );

            Assert.AreEqual(100, restored.length);
        }

        [Test]
        public void UnicodePathsSerializeCorrectly()
        {
            AnimationCreatorConfig.AnimationDataEntry entry = new()
            {
                animationName = "Walk",
                framePaths = new List<string>
                {
                    "Assets/Sprites/日本語/Walk_01.png",
                    "Assets/Sprites/中文/Walk_02.png",
                    "Assets/Sprites/한글/Walk_03.png",
                },
            };

            AnimationCreatorConfig config = new()
            {
                animationEntries = new List<AnimationCreatorConfig.AnimationDataEntry> { entry },
            };

            string json = Serializer.JsonStringify(config, pretty: true);
            AnimationCreatorConfig restored = Serializer.JsonDeserialize<AnimationCreatorConfig>(
                json
            );

            Assert.AreEqual(3, restored.animationEntries[0].framePaths.Count);
            Assert.AreEqual(
                "Assets/Sprites/日本語/Walk_01.png",
                restored.animationEntries[0].framePaths[0]
            );
            Assert.AreEqual(
                "Assets/Sprites/中文/Walk_02.png",
                restored.animationEntries[0].framePaths[1]
            );
            Assert.AreEqual(
                "Assets/Sprites/한글/Walk_03.png",
                restored.animationEntries[0].framePaths[2]
            );
        }
    }
#endif
}
