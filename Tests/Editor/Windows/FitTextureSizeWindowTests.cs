// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Windows
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.AssetProcessors;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestUtils;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestAssets;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class FitTextureSizeWindowTests : BatchedEditorTestBase
    {
        private const string Root = "Assets/Temp/FitTextureSizeTests";

        // Shared fixture paths - using pre-committed static assets from SharedTextureTestFixtures
        // Note: These paths point to static assets that are shared across all tests
        private static string _shared300x100Path;
        private static string _shared128x128Path;
        private static string _shared256x256Path;
        private static string _shared64x64Path;
        private static string _shared384x10Path;

        // Shared window instance - reused across tests to reduce CreateInstance overhead
        private static FitTextureSizeWindow _sharedWindow;

        [SetUp]
        public override void BaseSetUp()
        {
            // Canonical cross-fixture pollution tripwire: pins leaked handler
            // state to its true source rather than rolling it forward invisibly
            // into this fixture. Must precede base.BaseSetUp() to match the
            // placement contract enforced by
            // AssetContextFixturesCallCrossFixturePollutionTripwire.
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();
            base.BaseSetUp();
            // Reset the DetectAssetChangeProcessor to avoid triggering loop protection
            // when running many texture-related tests in succession
            DetectAssetChangeProcessor.ResetForTesting();
            EnsureFolder(Root);
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            // Clean up only tracked folders/assets that this test created
            CleanupTrackedFoldersAndAssets();
        }

        public override void CommonOneTimeSetUp()
        {
            if (Application.isPlaying)
            {
                return;
            }
            base.CommonOneTimeSetUp();

            // Acquire shared texture fixtures from pre-committed static assets
            SharedTextureTestFixtures.AcquireFixtures();

            // Map shared fixture paths for backward compatibility with existing tests
            _shared300x100Path = SharedTextureTestFixtures.Solid300x100Path;
            _shared128x128Path = SharedTextureTestFixtures.Solid128x128Path;
            _shared256x256Path = SharedTextureTestFixtures.Solid256x256Path;
            _shared64x64Path = SharedTextureTestFixtures.Solid64x64Path;
            _shared384x10Path = SharedTextureTestFixtures.Solid384x10Path;

            // Create shared window instance for reuse across tests
            _sharedWindow = ScriptableObject.CreateInstance<FitTextureSizeWindow>();
            Track(_sharedWindow);
            _trackedObjects.Remove(_sharedWindow); // Managed manually in one-time teardown

            EnsureFolderStatic(Root);
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            // Clear shared fixture path references (actual assets remain as static files)
            _shared300x100Path = null;
            _shared128x128Path = null;
            _shared256x256Path = null;
            _shared64x64Path = null;
            _shared384x10Path = null;

            // Destroy the shared window instance
            if (_sharedWindow != null)
            {
                _trackedObjects.Remove(_sharedWindow);
                Object.DestroyImmediate(_sharedWindow); // UNH-SUPPRESS: Shared window cleanup
                _sharedWindow = null;
            }

            // Release shared texture fixtures
            SharedTextureTestFixtures.ReleaseFixtures();

            base.OneTimeTearDown();
        }

        /// <summary>
        /// Clones a shared texture to a per-test path for tests that need to modify importer settings.
        /// Wraps copy and import in ExecuteWithImmediateImport to ensure asset is fully imported when batching.
        /// </summary>
        private string CloneSharedTexture(string sharedPath, string testName)
        {
            string fileName = Path.GetFileNameWithoutExtension(sharedPath);
            string destPath = Path.Combine(Root, testName + "_" + fileName + ".png").SanitizePath();

            bool success = false;
            ExecuteWithImmediateImport(() =>
            {
                if (TryCopyAssetSilent(sharedPath, destPath))
                {
                    TrackAssetPath(destPath);
                    success = true;
                    return;
                }

                // Fallback: create a new texture if copy fails
                Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sharedPath);
                if (source != null)
                {
                    CreatePng(destPath, source.width, source.height, Color.white);
                    AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ForceSynchronousImport);
                    success = true;
                }
            });

            return success ? destPath : null;
        }

        /// <summary>
        /// Resets the shared window to a clean state between tests.
        /// </summary>
        private FitTextureSizeWindow GetResetWindow()
        {
            if (_sharedWindow == null)
            {
                _sharedWindow = ScriptableObject.CreateInstance<FitTextureSizeWindow>();
                Track(_sharedWindow);
                _trackedObjects.Remove(_sharedWindow); // Managed manually in one-time teardown
            }
            _sharedWindow._fitMode = FitMode.GrowAndShrink;
            _sharedWindow._textureSourcePaths = new List<Object>();
            _sharedWindow._onlySprites = false;
            _sharedWindow._nameFilter = string.Empty;
            _sharedWindow._useRegexForName = false;
            _sharedWindow._labelFilterCsv = string.Empty;
            _sharedWindow._caseSensitiveNameFilter = false;
            _sharedWindow._useSelectionOnly = false;
            _sharedWindow._applyToAndroid = false;
            _sharedWindow._applyToiOS = false;
            _sharedWindow._applyToStandalone = false;
            _sharedWindow._minAllowedTextureSize = 32;
            _sharedWindow._maxAllowedTextureSize = 8192;
            _sharedWindow._hasLastRunSummary = false;
            return _sharedWindow;
        }

        [Test]
        public void GrowOnlyRaisesToNextPowerOfTwo()
        {
            string path = CloneSharedTexture(_shared300x100Path, "grow");
            Assert.IsTrue(
                path != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null, "Importer should exist");
            imp.maxTextureSize = 128;
            ExecuteWithImmediateImport(() => imp.SaveAndReimport());

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.GrowOnly;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            int count = window.CalculateTextureChanges(true);
            Assert.That(count, Is.GreaterThanOrEqualTo(1), "Expected at least one change");

            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            Assert.That(
                imp.maxTextureSize,
                Is.EqualTo(512),
                "Max size should increase to next POT >= largest dimension"
            );
        }

        [Test]
        public void ShrinkOnlyReducesToTightPowerOfTwo()
        {
            string path = CloneSharedTexture(_shared300x100Path, "shrink");
            Assert.IsTrue(
                path != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null, "Importer should exist");
            imp.maxTextureSize = 2048;
            ExecuteWithImmediateImport(() => imp.SaveAndReimport());

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.ShrinkOnly;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            int count = window.CalculateTextureChanges(true);
            Assert.That(count, Is.GreaterThanOrEqualTo(1), "Expected at least one change");

            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            // 300 pixels requires 512 POT to fit; shrink from 2048 to 512
            Assert.That(
                imp.maxTextureSize,
                Is.EqualTo(512),
                "Max size should shrink to tight POT that fits the source (512 >= 300)"
            );
        }

        [Test]
        public void ShrinkOnlyKeepsExactPowerOfTwo()
        {
            string path = CloneSharedTexture(_shared256x256Path, "shrinkExact");
            Assert.IsTrue(
                path != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            ExecuteWithImmediateImport(() =>
            {
                TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsTrue(imp != null, "Importer should exist after cloning");
                imp.maxTextureSize = 1024;
                imp.SaveAndReimport();
            });

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.ShrinkOnly;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };
            int count = window.CalculateTextureChanges(true);
            Assert.That(count, Is.GreaterThanOrEqualTo(1), "Expected at least one change");

            TextureImporter verifyImp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(verifyImp != null, "Importer should exist for verification");
            Assert.That(verifyImp.maxTextureSize, Is.EqualTo(256), "Should keep exact POT");
        }

        [Test]
        public void ShrinkOnlyShrinksFromSlightlyOverPot()
        {
            string path = Path.Combine(Root, "shrinkOver.png").SanitizePath();
            CreatePngAndImport(path, 257, 64, Color.gray);

            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null, "Importer should exist");
            imp.maxTextureSize = 2048;
            ExecuteWithImmediateImport(() => imp.SaveAndReimport());

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.ShrinkOnly;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };
            int count = window.CalculateTextureChanges(true);
            Assert.That(count, Is.GreaterThanOrEqualTo(1), "Expected at least one change");

            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            // 257 pixels requires 512 POT to fit; shrink from 2048 to 512
            Assert.That(
                imp.maxTextureSize,
                Is.EqualTo(512),
                "Should shrink to 512 (smallest POT that fits 257)"
            );
        }

        [Test]
        public void GrowOnlyDoesNotShrinkWhenAlreadyLarge()
        {
            string path = CloneSharedTexture(_shared300x100Path, "growNoChange");
            Assert.IsTrue(
                path != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            imp.maxTextureSize = 2048;
            ExecuteWithImmediateImport(() => imp.SaveAndReimport());

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.GrowOnly;
            // Target only this specific texture file to avoid interference from other tests' textures.
            // Load the texture inside ExecuteWithImmediateImport AND call CalculateTextureChanges
            // there to ensure the Object reference is valid and the calculation happens while
            // the asset database is in a consistent state.
            int count = 0;
            ExecuteWithImmediateImport(() =>
            {
                Object textureObj = AssetDatabase.LoadAssetAtPath<Object>(path);
                Assert.IsTrue(textureObj != null, $"Failed to load texture at {path}");
                window._textureSourcePaths = new List<Object> { textureObj };
                count = window.CalculateTextureChanges(true);
            });

            // Expect no change because it's already large enough (GrowOnly)
            Assert.That(count, Is.EqualTo(0));
            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            Assert.That(imp.maxTextureSize, Is.EqualTo(2048));
        }

        [Test]
        public void ClampMinRaisesToMinimum()
        {
            string path = CloneSharedTexture(_shared64x64Path, "clampMin");
            Assert.IsTrue(
                path != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            imp.maxTextureSize = 32;
            ExecuteWithImmediateImport(() => imp.SaveAndReimport());

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.RoundToNearest;
            window._minAllowedTextureSize = 256;
            window._maxAllowedTextureSize = 8192;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            int count = window.CalculateTextureChanges(true);
            Assert.That(count, Is.GreaterThanOrEqualTo(1));

            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            Assert.That(imp.maxTextureSize, Is.EqualTo(256));
        }

        // NOTE: the >8192 clamp-to-cap path (formerly the ClampMaxCapsOversize integration
        // test that created a 9001px graphics Texture2D the headless CI null-graphics device
        // rejects with "Failed to create texture because of invalid parameters") is covered
        // deterministically by FitTextureSizeMathTests' pure ComputeFit case
        // "GrowOnly.9001x10.Current128.ClampsToMax8192". The window's application of a computed
        // size to importer.maxTextureSize is covered by the other integration tests below.

        [Test]
        public void PlatformOverrideAndroidApplied()
        {
            string path = CloneSharedTexture(_shared300x100Path, "android");
            Assert.IsTrue(
                path != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            imp.maxTextureSize = 128;
            ExecuteWithImmediateImport(() => imp.SaveAndReimport());

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.RoundToNearest;
            window._applyToAndroid = true;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            _ = window.CalculateTextureChanges(true);

            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            TextureImporterPlatformSettings android = imp.GetPlatformTextureSettings("Android");
            Assert.IsTrue(android.overridden);
            Assert.That(android.maxTextureSize, Is.EqualTo(256));
        }

        [Test]
        public void OnlySpritesFiltersNonSprites()
        {
            string spritePath = CloneSharedTexture(_shared300x100Path, "sprite");
            Assert.IsTrue(
                spritePath != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );
            string texPath = CloneSharedTexture(_shared300x100Path, "tex");
            Assert.IsTrue(
                texPath != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.ImportAsset(spritePath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);
            });

            TextureImporter spriteImp = AssetImporter.GetAtPath(spritePath) as TextureImporter;
            TextureImporter texImp = AssetImporter.GetAtPath(texPath) as TextureImporter;
            Assert.IsTrue(spriteImp != null, "Sprite texture importer should exist");
            Assert.IsTrue(texImp != null, "Tex texture importer should exist");
            spriteImp.textureType = TextureImporterType.Sprite;
            spriteImp.maxTextureSize = 1024;
            texImp.textureType = TextureImporterType.Default;
            texImp.maxTextureSize = 1024;
            ExecuteWithImmediateImport(() =>
            {
                spriteImp.SaveAndReimport();
                texImp.SaveAndReimport();
            });

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.ShrinkOnly;
            window._onlySprites = true;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            _ = window.CalculateTextureChanges(true);

            spriteImp = AssetImporter.GetAtPath(spritePath) as TextureImporter;
            texImp = AssetImporter.GetAtPath(texPath) as TextureImporter;
            // 300 pixels requires 512 POT to fit; shrink from 1024 to 512
            Assert.That(spriteImp.maxTextureSize, Is.EqualTo(512));
            Assert.That(texImp.maxTextureSize, Is.EqualTo(1024));
        }

        [Test]
        public void NameFilterContainsOnlyMatches()
        {
            string heroPath = CloneSharedTexture(_shared300x100Path, "hero_idle");
            Assert.IsTrue(
                heroPath != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );
            string villPath = CloneSharedTexture(_shared300x100Path, "villain_idle");
            Assert.IsTrue(
                villPath != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            TextureImporter heroImp = AssetImporter.GetAtPath(heroPath) as TextureImporter;
            TextureImporter villImp = AssetImporter.GetAtPath(villPath) as TextureImporter;
            heroImp.maxTextureSize = 128;
            villImp.maxTextureSize = 128;
            ExecuteWithImmediateImport(() =>
            {
                heroImp.SaveAndReimport();
                villImp.SaveAndReimport();
            });

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.GrowOnly;
            window._nameFilter = "hero";
            window._useRegexForName = false;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            _ = window.CalculateTextureChanges(true);

            heroImp = AssetImporter.GetAtPath(heroPath) as TextureImporter;
            villImp = AssetImporter.GetAtPath(villPath) as TextureImporter;
            Assert.That(heroImp.maxTextureSize, Is.EqualTo(512));
            Assert.That(villImp.maxTextureSize, Is.EqualTo(128));
        }

        [Test]
        public void NameFilterRegexMatches()
        {
            string aPath = Path.Combine(Root, "item01.png").SanitizePath();
            string bPath = Path.Combine(Root, "itemABC.png").SanitizePath();
            CreatePngAndImport(aPath, 300, 100, Color.white);
            CreatePngAndImport(bPath, 300, 100, Color.white);

            TextureImporter aImp = AssetImporter.GetAtPath(aPath) as TextureImporter;
            TextureImporter bImp = AssetImporter.GetAtPath(bPath) as TextureImporter;
            aImp.maxTextureSize = 128;
            bImp.maxTextureSize = 128;
            ExecuteWithImmediateImport(() =>
            {
                aImp.SaveAndReimport();
                bImp.SaveAndReimport();
            });

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.GrowOnly;
            window._nameFilter = "^item\\d{2}$";
            window._useRegexForName = true;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            _ = window.CalculateTextureChanges(true);

            aImp = AssetImporter.GetAtPath(aPath) as TextureImporter;
            bImp = AssetImporter.GetAtPath(bPath) as TextureImporter;
            Assert.That(aImp.maxTextureSize, Is.EqualTo(512));
            Assert.That(bImp.maxTextureSize, Is.EqualTo(128));
        }

        [Test]
        public void LabelFilterMatchesOnlyLabeled()
        {
            string labeledPath = CloneSharedTexture(_shared300x100Path, "labeled");
            Assert.IsTrue(
                labeledPath != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );
            string unlabeledPath = CloneSharedTexture(_shared300x100Path, "unlabeled");
            Assert.IsTrue(
                unlabeledPath != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.ImportAsset(labeledPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(unlabeledPath, ImportAssetOptions.ForceSynchronousImport);
            });

            Object labeledObj = AssetDatabase.LoadAssetAtPath<Object>(labeledPath);
            AssetDatabase.SetLabels(labeledObj, new[] { "FitMe", "TagA" });
            ExecuteWithImmediateImport(() => AssetDatabase.SaveAssets());

            TextureImporter labImp = AssetImporter.GetAtPath(labeledPath) as TextureImporter;
            TextureImporter unlabImp = AssetImporter.GetAtPath(unlabeledPath) as TextureImporter;
            Assert.IsTrue(labImp != null, "Labeled texture importer should exist");
            Assert.IsTrue(unlabImp != null, "Unlabeled texture importer should exist");
            labImp.maxTextureSize = 128;
            unlabImp.maxTextureSize = 128;
            ExecuteWithImmediateImport(() =>
            {
                labImp.SaveAndReimport();
                unlabImp.SaveAndReimport();
            });

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.GrowOnly;
            window._labelFilterCsv = "FitMe";
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            _ = window.CalculateTextureChanges(true);

            labImp = AssetImporter.GetAtPath(labeledPath) as TextureImporter;
            unlabImp = AssetImporter.GetAtPath(unlabeledPath) as TextureImporter;
            Assert.That(labImp.maxTextureSize, Is.EqualTo(512));
            Assert.That(unlabImp.maxTextureSize, Is.EqualTo(128));
        }

        [Test]
        public void SelectionOnlyProcessesOnlySelectedAsset()
        {
            string aPath = CloneSharedTexture(_shared300x100Path, "sel_a");
            Assert.IsTrue(
                aPath != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );
            string bPath = CloneSharedTexture(_shared300x100Path, "sel_b");
            Assert.IsTrue(
                bPath != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            TextureImporter aImp = AssetImporter.GetAtPath(aPath) as TextureImporter;
            TextureImporter bImp = AssetImporter.GetAtPath(bPath) as TextureImporter;
            aImp.maxTextureSize = 128;
            bImp.maxTextureSize = 128;
            ExecuteWithImmediateImport(() =>
            {
                aImp.SaveAndReimport();
                bImp.SaveAndReimport();
            });

            Object aObj = AssetDatabase.LoadAssetAtPath<Object>(aPath);
            Selection.objects = new[] { aObj };

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.GrowOnly;
            window._useSelectionOnly = true;

            int count = window.CalculateTextureChanges(true);
            Assert.That(count, Is.EqualTo(1));

            aImp = AssetImporter.GetAtPath(aPath) as TextureImporter;
            bImp = AssetImporter.GetAtPath(bPath) as TextureImporter;
            Assert.That(aImp.maxTextureSize, Is.EqualTo(512));
            Assert.That(bImp.maxTextureSize, Is.EqualTo(128));
        }

        [Test]
        public void NameFilterCaseSensitivityHonored()
        {
            string path = Path.Combine(Root, "Hero.png").SanitizePath();
            CreatePngAndImport(path, 300, 100, Color.white);

            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            imp.maxTextureSize = 128;
            ExecuteWithImmediateImport(() => imp.SaveAndReimport());

            FitTextureSizeWindow window = GetResetWindow();
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            // Case-sensitive search for lower-case 'hero' should not match 'Hero'
            window._fitMode = FitMode.GrowOnly;
            window._nameFilter = "hero";
            window._caseSensitiveNameFilter = true;
            _ = window.CalculateTextureChanges(true);
            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(imp.maxTextureSize, Is.EqualTo(128));

            // Case-insensitive should match
            window._caseSensitiveNameFilter = false;
            _ = window.CalculateTextureChanges(true);
            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(imp.maxTextureSize, Is.EqualTo(512));
        }

        [Test]
        public void LabelFilterCaseSensitivityHonored()
        {
            string path = CloneSharedTexture(_shared300x100Path, "labelCase");
            Assert.IsTrue(
                path != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            Object obj = AssetDatabase.LoadAssetAtPath<Object>(path);
            AssetDatabase.SetLabels(obj, new[] { "FitMe" });
            ExecuteWithImmediateImport(() => AssetDatabase.SaveAssets());

            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            imp.maxTextureSize = 128;
            ExecuteWithImmediateImport(() => imp.SaveAndReimport());

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.GrowOnly;
            window._labelFilterCsv = "fitme";
            window._caseSensitiveNameFilter = true;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            // Case-sensitive 'fitme' should not match 'FitMe'
            _ = window.CalculateTextureChanges(true);
            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(imp.maxTextureSize, Is.EqualTo(128));

            // Case-insensitive should match
            window._caseSensitiveNameFilter = false;
            _ = window.CalculateTextureChanges(true);
            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(imp.maxTextureSize, Is.EqualTo(512));
        }

        [Test]
        public void PlatformOverrideStandaloneApplied()
        {
            string path = CloneSharedTexture(_shared300x100Path, "standalone");
            Assert.IsTrue(
                path != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            imp.maxTextureSize = 128;
            ExecuteWithImmediateImport(() => imp.SaveAndReimport());

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.RoundToNearest;
            window._applyToStandalone = true;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            _ = window.CalculateTextureChanges(true);

            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            TextureImporterPlatformSettings st = imp.GetPlatformTextureSettings("Standalone");
            Assert.IsTrue(st.overridden);
            Assert.That(st.maxTextureSize, Is.EqualTo(256));
        }

        [Test]
        public void PlatformOverrideIOSApplied()
        {
            string path = CloneSharedTexture(_shared300x100Path, "ios");
            Assert.IsTrue(
                path != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            ExecuteWithImmediateImport(() =>
            {
                TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsTrue(imp != null, "Importer should exist after cloning");
                imp.maxTextureSize = 128;
                imp.SaveAndReimport();
            });

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.RoundToNearest;
            window._applyToiOS = true;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            _ = window.CalculateTextureChanges(true);

            TextureImporter verifyImp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(verifyImp != null, "Importer should exist for verification");
            TextureImporterPlatformSettings ios = verifyImp.GetPlatformTextureSettings("iPhone");
            Assert.IsTrue(ios.overridden);
            Assert.That(ios.maxTextureSize, Is.EqualTo(256));
        }

        [Test]
        public void MixedSelectionFoldersAndFilesWithLabelCsvOnlyLabelsFromFoldersAreProcessed()
        {
            // Prepare: one labeled texture under a folder, one unlabeled file selected directly
            string folder = Path.Combine(Root, "Sub").SanitizePath();
            EnsureFolder(folder);
            string labeledUnderFolder = CloneSharedTexture(_shared300x100Path, "Sub/inFolder");
            Assert.IsTrue(
                labeledUnderFolder != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );
            string directFile = CloneSharedTexture(_shared300x100Path, "direct");
            Assert.IsTrue(
                directFile != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            Object labeledObj = AssetDatabase.LoadAssetAtPath<Object>(labeledUnderFolder);
            AssetDatabase.SetLabels(labeledObj, new[] { "OnlyMe" });
            ExecuteWithImmediateImport(() => AssetDatabase.SaveAssets());

            TextureImporter folderImp =
                AssetImporter.GetAtPath(labeledUnderFolder) as TextureImporter;
            TextureImporter directImp = AssetImporter.GetAtPath(directFile) as TextureImporter;
            folderImp.maxTextureSize = 128;
            directImp.maxTextureSize = 128;
            ExecuteWithImmediateImport(() =>
            {
                folderImp.SaveAndReimport();
                directImp.SaveAndReimport();
            });

            // Select folder and the direct file simultaneously
            Object folderObj = AssetDatabase.LoadAssetAtPath<Object>(folder);
            Object directObj = AssetDatabase.LoadAssetAtPath<Object>(directFile);
            Selection.objects = new[] { folderObj, directObj };

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.GrowOnly;
            window._useSelectionOnly = true;
            window._labelFilterCsv = "OnlyMe"; // case-insensitive path used by l: query
            window._caseSensitiveNameFilter = false;

            _ = window.CalculateTextureChanges(true);

            folderImp = AssetImporter.GetAtPath(labeledUnderFolder) as TextureImporter;
            directImp = AssetImporter.GetAtPath(directFile) as TextureImporter;
            // Only labeled under folder changes; direct file with no label should not change
            Assert.That(folderImp.maxTextureSize, Is.EqualTo(512));
            Assert.That(directImp.maxTextureSize, Is.EqualTo(128));
        }

        [Test]
        public void LastRunSummaryReflectsCounts()
        {
            string aPath = CloneSharedTexture(_shared300x100Path, "sumA");
            Assert.IsTrue(
                aPath != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );
            string bPath = CloneSharedTexture(_shared128x128Path, "sumB");
            Assert.IsTrue(
                bPath != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.ImportAsset(aPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(bPath, ImportAssetOptions.ForceSynchronousImport);
            });

            TextureImporter aImp = AssetImporter.GetAtPath(aPath) as TextureImporter;
            TextureImporter bImp = AssetImporter.GetAtPath(bPath) as TextureImporter;
            Assert.IsTrue(aImp != null, "Texture importer A should exist");
            Assert.IsTrue(bImp != null, "Texture importer B should exist");
            aImp.maxTextureSize = 128;
            bImp.maxTextureSize = 128;
            ExecuteWithImmediateImport(() =>
            {
                aImp.SaveAndReimport();
                bImp.SaveAndReimport();
            });

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.GrowOnly;
            // Target only these specific texture files to avoid interference from other tests' textures.
            // Load the textures inside ExecuteWithImmediateImport AND call CalculateTextureChanges
            // there to ensure the Object references are valid and the calculation happens while
            // the asset database is in a consistent state.
            int changed = 0;
            ExecuteWithImmediateImport(() =>
            {
                Object aObj = AssetDatabase.LoadAssetAtPath<Object>(aPath);
                Object bObj = AssetDatabase.LoadAssetAtPath<Object>(bPath);
                Assert.IsTrue(aObj != null, $"Failed to load texture at {aPath}");
                Assert.IsTrue(bObj != null, $"Failed to load texture at {bPath}");
                window._textureSourcePaths = new List<Object> { aObj, bObj };
                changed = window.CalculateTextureChanges(true);
            });

            Assert.That(changed, Is.EqualTo(1));
            Assert.IsTrue(window._hasLastRunSummary);
            Assert.That(window._lastRunTotal, Is.EqualTo(2));
            Assert.That(window._lastRunChanged, Is.EqualTo(1));
            Assert.That(window._lastRunGrows, Is.EqualTo(1));
            Assert.That(window._lastRunShrinks, Is.EqualTo(0));
            Assert.That(window._lastRunUnchanged, Is.EqualTo(1));
        }

        [Test]
        public void RoundToNearestChoosesLowerWhenCloser()
        {
            string path = CloneSharedTexture(_shared300x100Path, "roundLower");
            Assert.IsTrue(
                path != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            imp.maxTextureSize = 2048;
            ExecuteWithImmediateImport(() => imp.SaveAndReimport());

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.RoundToNearest;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            int count = window.CalculateTextureChanges(true);
            Assert.That(count, Is.GreaterThanOrEqualTo(1));

            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            Assert.That(imp.maxTextureSize, Is.EqualTo(256));
        }

        [Test]
        public void RoundToNearestRoundsUpOnTie()
        {
            string path = CloneSharedTexture(_shared384x10Path, "roundUpTie");
            Assert.IsTrue(
                path != null,
                "CloneSharedTexture failed - source fixture may be missing"
            );

            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            imp.maxTextureSize = 128;
            ExecuteWithImmediateImport(() => imp.SaveAndReimport());

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.RoundToNearest;
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            int count = window.CalculateTextureChanges(true);
            Assert.That(count, Is.GreaterThanOrEqualTo(1));

            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            Assert.That(imp.maxTextureSize, Is.EqualTo(512));
        }

        [Test]
        public void DefaultMinClampingPreventsVerySmallSizes()
        {
            string path = Path.Combine(Root, "defaultClamp_1x1.png").SanitizePath();
            CreatePngAndImport(path, 1, 1, Color.white);

            TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null, "Importer should exist");
            imp.maxTextureSize = 2048;
            ExecuteWithImmediateImport(() => imp.SaveAndReimport());

            FitTextureSizeWindow window = GetResetWindow();
            window._fitMode = FitMode.GrowAndShrink;
            // Intentionally NOT setting _minAllowedTextureSize to verify default clamping behavior
            window._textureSourcePaths = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            _ = window.CalculateTextureChanges(true);

            imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(imp != null);
            Assert.That(
                imp.maxTextureSize,
                Is.EqualTo(32),
                "Default _minAllowedTextureSize=32 should clamp 1x1 texture to 32, not 1"
            );
        }

        private void CreatePng(string relPath, int w, int h, Color c)
        {
            string dir = Path.GetDirectoryName(relPath).SanitizePath();
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

        /// <summary>
        /// Creates a PNG and immediately imports it into the AssetDatabase.
        /// Use this instead of CreatePng + RefreshIfNotBatching when batching is active.
        /// </summary>
        private void CreatePngAndImport(string relPath, int w, int h, Color c)
        {
            ExecuteWithImmediateImport(() =>
            {
                CreatePng(relPath, w, h, c);
                AssetDatabase.ImportAsset(relPath, ImportAssetOptions.ForceSynchronousImport);
            });
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
