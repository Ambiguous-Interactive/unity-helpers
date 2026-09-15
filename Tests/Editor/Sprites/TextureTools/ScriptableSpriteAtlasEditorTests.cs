// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.U2D;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ScriptableSpriteAtlasEditorTests : CommonTestBase
    {
        private const string Root = "Assets/Temp/ScriptableSpriteAtlasEditorTests";

        private static string RelToFull(string rel)
        {
            return Path.Combine(
                    Application.dataPath.Substring(
                        0,
                        Application.dataPath.Length - "Assets".Length
                    ),
                    rel
                )
                .SanitizePath();
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            EnsureFolder(Root);
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();

            CleanupTrackedFoldersAndAssets();
        }

        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            DeferAssetCleanupToOneTimeTearDown = true;
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            CleanupDeferredAssetsAndFolders();
            base.OneTimeTearDown();
        }

        [Test]
        public void GeneratesSpriteAtlasAssetFromConfig()
        {
            string spritePath = Path.Combine(Root, "icon.png").SanitizePath();
            CreatePng(spritePath, 8, 8, Color.red);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            ScriptableSpriteAtlas config = ScriptableObject.CreateInstance<ScriptableSpriteAtlas>(); // UNH-SUPPRESS: Asset becomes persistent via CreateAsset below
            config.name = "TestAtlasConfig";
            config.spritesToPack.Add(AssetDatabase.LoadAssetAtPath<Sprite>(spritePath));
            config.outputSpriteAtlasDirectory = Root;
            config.outputSpriteAtlasName = "TestAtlas";
            string configPath = Path.Combine(Root, "TestAtlasConfig.asset").SanitizePath();
            AssetDatabase.CreateAsset(config, configPath);
            TrackAssetPath(configPath);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            ScriptableSpriteAtlasEditor window = Track(
                ScriptableObject.CreateInstance<ScriptableSpriteAtlasEditor>()
            );
            window.LoadAtlasConfigs();
            window.GenerateAllAtlases();

            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string atlasPath = Path.Combine(Root, "TestAtlas.spriteatlas").SanitizePath();
            TrackAssetPath(atlasPath);
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            Assert.IsTrue(atlas != null, ".spriteatlas should be generated");
        }

        [Test]
        public void SpriteCollectionHelpersPreserveValidSpriteOrder()
        {
            Texture2D texture = Track(new Texture2D(2, 2));
            Sprite first = Track(Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), Vector2.zero));
            Sprite second = Track(Sprite.Create(texture, new Rect(1f, 1f, 1f, 1f), Vector2.zero));
            List<Sprite> sprites = new() { null, first, null, second };
            List<Sprite> destination = new() { second };

            Assert.AreEqual(0, ScriptableSpriteAtlasEditor.CountValidSprites(null));
            Assert.AreEqual(2, ScriptableSpriteAtlasEditor.CountValidSprites(sprites));

            ScriptableSpriteAtlasEditor.AppendSpritesWithTextures(null, destination);
            ScriptableSpriteAtlasEditor.AppendSpritesWithTextures(sprites, null);
            ScriptableSpriteAtlasEditor.AppendSpritesWithTextures(sprites, destination);

            CollectionAssert.AreEqual(new[] { second, first, second }, destination);
            CollectionAssert.AreEqual(
                new Object[] { null, first, null, second },
                ScriptableSpriteAtlasEditor.ToObjectArray(sprites)
            );
            CollectionAssert.IsEmpty(ScriptableSpriteAtlasEditor.ToObjectArray(null));

            Object.DestroyImmediate(second); // UNH-SUPPRESS: Exercises destroyed Sprite filtering.

            Assert.AreEqual(1, ScriptableSpriteAtlasEditor.CountValidSprites(sprites));
            destination.Clear();
            ScriptableSpriteAtlasEditor.AppendSpritesWithTextures(sprites, destination);
            CollectionAssert.AreEqual(new[] { first }, destination);
        }

        [Test]
        public void AtlasConfigSortPreservesEqualNameOrder()
        {
            ScriptableSpriteAtlas secondSame = Track(
                ScriptableObject.CreateInstance<ScriptableSpriteAtlas>()
            );
            ScriptableSpriteAtlas firstSame = Track(
                ScriptableObject.CreateInstance<ScriptableSpriteAtlas>()
            );
            ScriptableSpriteAtlas alpha = Track(
                ScriptableObject.CreateInstance<ScriptableSpriteAtlas>()
            );
            secondSame.name = "Same";
            firstSame.name = "Same";
            alpha.name = "Alpha";
            List<ScriptableSpriteAtlas> configs = new() { secondSame, firstSame, alpha };

            ScriptableSpriteAtlasEditor.SortAtlasConfigs(configs);

            CollectionAssert.AreEqual(new[] { alpha, secondSame, firstSame }, configs);
            Assert.DoesNotThrow(() => ScriptableSpriteAtlasEditor.SortAtlasConfigs(null));
        }

        private void CreatePng(string relPath, int w, int h, Color c)
        {
            string dir = Path.GetDirectoryName(relPath)?.SanitizePath();
            EnsureFolder(dir);
            Texture2D t = new(w, h, TextureFormat.RGBA32, false);
            try
            {
                Color[] pix = new Color[w * h];
                for (int i = 0; i < pix.Length; i++)
                {
                    pix[i] = c;
                }

                t.SetPixels(pix);
                t.Apply();
                byte[] data = t.EncodeToPNG();
                File.WriteAllBytes(RelToFull(relPath), data);
                TrackAssetPath(relPath);
            }
            finally
            {
                Object.DestroyImmediate(t); // UNH-SUPPRESS: Cleanup temporary texture in finally block
            }
        }
    }
#endif
}
