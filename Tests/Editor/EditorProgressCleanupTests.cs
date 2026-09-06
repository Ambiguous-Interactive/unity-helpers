// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Slow")]
    [Category("Integration")]
    public sealed class EditorProgressCleanupTests : CommonTestBase
    {
        private const string Root = "Assets/Temp/EditorProgressCleanupTests";

        [TestCase(typeof(SpriteSettingsApplierWindow), true)]
        [TestCase(typeof(SpriteSettingsApplierWindow), false)]
        [TestCase(typeof(TextureSettingsApplierWindow), true)]
        [TestCase(typeof(TextureSettingsApplierWindow), false)]
        [TestCase(typeof(ScriptableSpriteAtlasEditor), false)]
        [TestCase(typeof(ScriptableSpriteAtlasEditor), true)]
        [TestCase(typeof(PrefabChecker), false)]
        [TestCase(typeof(TextureResizerWizard), false)]
        [TestCase(typeof(SpriteSheetExtractor), false)]
        public void ProcessingExceptionClearsProgress(Type windowType, bool calculateStats)
        {
            Action operation = PrepareOperation(windowType, calculateStats);
            InvalidOperationException expected = new("Injected processing failure");
            int shown = 0;
            int cleared = 0;
            Func<bool> previousProgress = EditorUi.ProgressForTesting;
            Action previousClear = EditorUi.ProgressClearedForTesting;
            try
            {
                EditorUi.ProgressForTesting = () =>
                {
                    ++shown;
                    throw expected;
                };
                EditorUi.ProgressClearedForTesting = () => ++cleared;

                if (windowType == typeof(SpriteSheetExtractor))
                {
                    ExpectError(LogType.Error, "Error during reference replacement");
                    Assert.DoesNotThrow(() => operation());
                }
                else
                {
                    InvalidOperationException actual = Assert.Throws<InvalidOperationException>(
                        () =>
                            operation()
                    );
                    Assert.AreSame(
                        expected,
                        actual,
                        "Cleanup must preserve the processing exception."
                    );
                }
                Assert.AreEqual(1, shown, "The operation must reach a progress display.");
                Assert.AreEqual(
                    1,
                    cleared,
                    "A failed operation must clear its progress exactly once."
                );
            }
            finally
            {
                EditorUi.ProgressForTesting = previousProgress;
                EditorUi.ProgressClearedForTesting = previousClear;
            }
        }

        [TestCase(typeof(SpriteSettingsApplierWindow))]
        [TestCase(typeof(TextureSettingsApplierWindow))]
        [TestCase(typeof(PrefabChecker))]
        [TestCase(typeof(TextureResizerWizard))]
        [TestCase(typeof(SpriteSheetExtractor))]
        public void CancellationClearsProgress(Type windowType)
        {
            Action operation = PrepareOperation(windowType, calculateStats: false);
            int shown = 0;
            int cleared = 0;
            Func<bool> previousProgress = EditorUi.ProgressForTesting;
            Action previousClear = EditorUi.ProgressClearedForTesting;
            try
            {
                EditorUi.ProgressForTesting = () =>
                {
                    ++shown;
                    return true;
                };
                EditorUi.ProgressClearedForTesting = () => ++cleared;

                Assert.DoesNotThrow(() => operation());

                Assert.AreEqual(
                    1,
                    shown,
                    "Cancellation must stop before another asset is processed."
                );
                Assert.AreEqual(1, cleared, "Cancellation must clear its progress exactly once.");
            }
            finally
            {
                EditorUi.ProgressForTesting = previousProgress;
                EditorUi.ProgressClearedForTesting = previousClear;
            }
        }

        private Action PrepareOperation(Type windowType, bool calculateStats)
        {
            EnsureFolder(Root);
            if (windowType == typeof(PrefabChecker))
            {
                string prefabPath = Root + "/subject.prefab";
                TrackAssetPath(prefabPath);
                GameObject source = Track(new GameObject("ProgressCleanupSubject"));
                PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
                PrefabChecker window = Track(ScriptableObject.CreateInstance<PrefabChecker>());
                window._assetPaths.Clear();
                window._assetPaths.Add(Root);
                return window.RunChecksImproved;
            }

            if (windowType == typeof(ScriptableSpriteAtlasEditor) && !calculateStats)
            {
                string configPath = Root + "/subject.asset";
                TrackAssetPath(configPath);
                ScriptableSpriteAtlas config = Track(
                    ScriptableObject.CreateInstance<ScriptableSpriteAtlas>()
                );
                AssetDatabase.CreateAsset(config, configPath);
                ScriptableSpriteAtlasEditor window = Track(
                    ScriptableObject.CreateInstance<ScriptableSpriteAtlasEditor>()
                );
                window.LoadAtlasConfigs();
                return window.GenerateAllAtlases;
            }

            string texturePath = Root + "/subject.png";
            TrackAssetPath(texturePath);
            Texture2D sourceTexture = Track(new Texture2D(2, 2));
            File.WriteAllBytes(texturePath, sourceTexture.EncodeToPNG());
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            Assert.IsTrue(importer != null, "The fixture texture must have an importer.");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();

            if (windowType == typeof(SpriteSheetExtractor))
            {
                string extractedPath = Root + "/subject_000.png";
                TrackAssetPath(extractedPath);
                Assert.IsTrue(AssetDatabase.CopyAsset(texturePath, extractedPath));
                AssetDatabase.ImportAsset(extractedPath, ImportAssetOptions.ForceSynchronousImport);
                Sprite original = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
                Sprite extracted = AssetDatabase.LoadAssetAtPath<Sprite>(extractedPath);
                Assert.IsTrue(original != null, "The original sprite must be imported.");
                Assert.IsTrue(extracted != null, "The replacement sprite must be imported.");
                string prefabPath = Root + "/subject.prefab";
                TrackAssetPath(prefabPath);
                GameObject source = Track(new GameObject("ProgressCleanupSubject"));
                PrefabUtility.SaveAsPrefabAsset(source, prefabPath);

                SpriteSheetExtractor window = Track(
                    ScriptableObject.CreateInstance<SpriteSheetExtractor>()
                );
                window._outputDirectory = AssetDatabase.LoadAssetAtPath<DefaultAsset>(Root);
                window._discoveredSheets = new List<SpriteSheetExtractor.SpriteSheetEntry>
                {
                    new()
                    {
                        _assetPath = texturePath,
                        _isSelected = true,
                        _sprites = new List<SpriteSheetExtractor.SpriteEntryData>
                        {
                            new() { _originalName = original.name, _isSelected = true },
                        },
                    },
                };
                return window.ReplaceSpriteReferences;
            }

            if (windowType == typeof(ScriptableSpriteAtlasEditor))
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
                Assert.IsTrue(sprite != null, "The fixture must contain an imported sprite.");
                ScriptableSpriteAtlas config = Track(
                    ScriptableObject.CreateInstance<ScriptableSpriteAtlas>()
                );
                config.spritesToPack.Add(sprite);
                ScriptableSpriteAtlasEditor window = Track(
                    ScriptableObject.CreateInstance<ScriptableSpriteAtlasEditor>()
                );
                return () => window.ForceUncompressedSourceSprites(config);
            }

            if (windowType == typeof(SpriteSettingsApplierWindow))
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
                Assert.IsTrue(sprite != null, "The fixture must contain an imported sprite.");
                SpriteSettingsApplierWindow window = Track(
                    ScriptableObject.CreateInstance<SpriteSettingsApplierWindow>()
                );
                window.sprites.Add(sprite);
                window.SerializedStateForTesting.Update();
                return calculateStats ? window.CalculateStats : window.ApplySettings;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            Assert.IsTrue(texture != null, "The fixture must contain an imported texture.");
            if (windowType == typeof(TextureResizerWizard))
            {
                TextureResizerWizard window = Track(
                    ScriptableObject.CreateInstance<TextureResizerWizard>()
                );
                window.textures.Add(texture);
                return window.OnWizardCreate;
            }

            Assert.AreEqual(typeof(TextureSettingsApplierWindow), windowType);
            TextureSettingsApplierWindow textureWindow = Track(
                ScriptableObject.CreateInstance<TextureSettingsApplierWindow>()
            );
            textureWindow.textures.Add(texture);
            textureWindow.requireChangesBeforeApply = false;
            return calculateStats ? textureWindow.CalculateStats : textureWindow.ApplySettings;
        }
    }
#endif
}
