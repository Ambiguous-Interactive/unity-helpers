// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.AssetProcessors;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class TextureSettingsApplierWizardTests : CommonTestBase
    {
        private const string Root = "Assets/Temp/TextureSettingsApplierWizardTests";

        [SetUp]
        public override void BaseSetUp()
        {
            // Must precede base.BaseSetUp(); AssertCleanAndClearAll documents why it runs first.
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();
            base.BaseSetUp();
            EnsureFolder(Root);
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            // Loop protection would otherwise trip as cleanup deletes several assets.
            DetectAssetChangeProcessor.ResetForTesting();
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
        public void AppliesImporterSettingsToTexturesAndDirectories()
        {
            string a = Path.Combine(Root, "a.png").SanitizePath();
            string bdir = Path.Combine(Root, "Dir").SanitizePath();
            string b = Path.Combine(bdir, "b.png").SanitizePath();
            EnsureFolder(bdir);
            CreatePng(a, 16, 16, Color.white);
            CreatePng(b, 32, 32, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureSettingsApplierWindow window = Track(
                ScriptableObject.CreateInstance<TextureSettingsApplierWindow>()
            );

            window.textures = new System.Collections.Generic.List<Texture2D>
            {
                AssetDatabase.LoadAssetAtPath<Texture2D>(a),
            };

            window.directories = new System.Collections.Generic.List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            window.applyReadOnly = true;
            window.isReadOnly = true;
            window.applyMipMaps = true;
            window.generateMipMaps = false;
            window.applyWrapMode = true;
            window.wrapMode = TextureWrapMode.Clamp;
            window.applyFilterMode = true;
            window.filterMode = FilterMode.Bilinear;
            window.maxTextureSize = 128;

            window.ApplySettings();

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            TextureImporter impA = AssetImporter.GetAtPath(a) as TextureImporter;
            TextureImporter impB = AssetImporter.GetAtPath(b) as TextureImporter;
            Assert.IsTrue(impA != null);
            Assert.IsTrue(impB != null);

            Assert.That(impA.isReadable, Is.False);
            Assert.That(impA.mipmapEnabled, Is.False);
            Assert.That(impA.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(impA.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(impA.maxTextureSize, Is.EqualTo(128));

            Assert.That(impB.isReadable, Is.False);
            Assert.That(impB.mipmapEnabled, Is.False);
            Assert.That(impB.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(impB.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(impB.maxTextureSize, Is.EqualTo(128));
        }

        [Test]
        public void ApplySettingsWithEmptyDirectoriesListSucceeds()
        {
            string a = Path.Combine(Root, "solo.png").SanitizePath();
            CreatePng(a, 16, 16, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureSettingsApplierWindow window = Track(
                ScriptableObject.CreateInstance<TextureSettingsApplierWindow>()
            );

            window.textures = new System.Collections.Generic.List<Texture2D>
            {
                AssetDatabase.LoadAssetAtPath<Texture2D>(a),
            };
            window.directories = new System.Collections.Generic.List<Object>();

            window.applyWrapMode = true;
            window.wrapMode = TextureWrapMode.Clamp;

            Assert.DoesNotThrow(() => window.ApplySettings());

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            TextureImporter imp = AssetImporter.GetAtPath(a) as TextureImporter;
            Assert.IsTrue(imp != null, $"Expected importer at path '{a}' to not be null");
            Assert.That(imp.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
        }

        [Test]
        public void ApplySettingsWithMultipleNestedDirectoriesSucceeds()
        {
            string dirA = Path.Combine(Root, "DirA").SanitizePath();
            string dirB = Path.Combine(Root, "DirB").SanitizePath();
            string dirNested = Path.Combine(dirA, "Nested").SanitizePath();

            EnsureFolder(dirA);
            EnsureFolder(dirB);
            EnsureFolder(dirNested);

            string texA = Path.Combine(dirA, "texA.png").SanitizePath();
            string texB = Path.Combine(dirB, "texB.png").SanitizePath();
            string texNested = Path.Combine(dirNested, "texNested.png").SanitizePath();

            CreatePng(texA, 8, 8, Color.red);
            CreatePng(texB, 8, 8, Color.green);
            CreatePng(texNested, 8, 8, Color.blue);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureSettingsApplierWindow window = Track(
                ScriptableObject.CreateInstance<TextureSettingsApplierWindow>()
            );

            window.textures = new System.Collections.Generic.List<Texture2D>();
            window.directories = new System.Collections.Generic.List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(dirA),
                AssetDatabase.LoadAssetAtPath<Object>(dirB),
            };

            window.applyFilterMode = true;
            window.filterMode = FilterMode.Point;

            /*
                The pool may return an array larger than the logical path count; trailing entries must not be
                processed.
            */
            Assert.DoesNotThrow(
                () => window.ApplySettings(),
                "ApplySettings with multiple directories should not throw"
            );

            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureImporter impA = AssetImporter.GetAtPath(texA) as TextureImporter;
            TextureImporter impB = AssetImporter.GetAtPath(texB) as TextureImporter;
            TextureImporter impNested = AssetImporter.GetAtPath(texNested) as TextureImporter;

            Assert.IsTrue(impA != null, $"Expected importer at path '{texA}' to not be null");
            Assert.IsTrue(impB != null, $"Expected importer at path '{texB}' to not be null");
            Assert.IsTrue(
                impNested != null,
                $"Expected importer at path '{texNested}' to not be null"
            );

            Assert.That(impA.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(impB.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(impNested.filterMode, Is.EqualTo(FilterMode.Point));
        }

        [Test]
        public void ApplySettingsWithEmptyDirectorySucceeds()
        {
            string emptyDir = Path.Combine(Root, "EmptyDir").SanitizePath();
            EnsureFolder(emptyDir);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureSettingsApplierWindow window = Track(
                ScriptableObject.CreateInstance<TextureSettingsApplierWindow>()
            );

            window.textures = new System.Collections.Generic.List<Texture2D>();
            window.directories = new System.Collections.Generic.List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(emptyDir),
            };

            window.applyFilterMode = true;
            window.filterMode = FilterMode.Point;

            Assert.DoesNotThrow(
                () => window.ApplySettings(),
                "ApplySettings with empty directory should not throw"
            );
        }

        [Test]
        public void ApplySettingsWithNullDirectoryEntriesIgnoresThem()
        {
            string validDir = Path.Combine(Root, "ValidDir").SanitizePath();
            EnsureFolder(validDir);
            string tex = Path.Combine(validDir, "valid.png").SanitizePath();
            CreatePng(tex, 8, 8, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureSettingsApplierWindow window = Track(
                ScriptableObject.CreateInstance<TextureSettingsApplierWindow>()
            );

            window.textures = new System.Collections.Generic.List<Texture2D>();
            window.directories = new System.Collections.Generic.List<Object>
            {
                null,
                AssetDatabase.LoadAssetAtPath<Object>(validDir),
                null,
            };

            window.applyWrapMode = true;
            window.wrapMode = TextureWrapMode.MirrorOnce;

            Assert.DoesNotThrow(
                () => window.ApplySettings(),
                "ApplySettings with null directory entries should not throw"
            );

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            TextureImporter imp = AssetImporter.GetAtPath(tex) as TextureImporter;
            Assert.IsTrue(imp != null, $"Expected importer at path '{tex}' to not be null");
            Assert.That(imp.wrapMode, Is.EqualTo(TextureWrapMode.MirrorOnce));
        }

        private void CreatePng(string relPath, int w, int h, Color c)
        {
            string dir = Path.GetDirectoryName(relPath).SanitizePath();
            EnsureFolder(dir);
            Texture2D t = new(w, h, TextureFormat.RGBA32, false);
            Color[] pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++)
            {
                pix[i] = c;
            }

            t.SetPixels(pix);
            t.Apply();
            byte[] data = t.EncodeToPNG();
            File.WriteAllBytes(RelToFull(relPath), data);
        }

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
    }
#endif
}
