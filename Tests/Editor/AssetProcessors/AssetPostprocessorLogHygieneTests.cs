// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Regression tests for <see href="https://github.com/wallstop/unity-helpers/pull/234">#234</see>.
    /// Exercises the asset-import phase paths that used to emit
    /// "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate"
    /// warnings, and asserts the processors' deferral machinery suppresses them.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class AssetPostprocessorLogHygieneTests : BatchedEditorTestBase
    {
        private const string TestRoot = "Assets/__AssetPostprocessorHygieneTests__";

        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            EnsureTestFolder();
            TrackFolder(TestRoot);
            // EnsureTestFolder may call AssetDatabase.CreateFolder + Refresh, which
            // schedules drains. Flush explicitly so the first test's BaseSetUp does
            // not inherit a pending drain. The OneTime flush contract enforces this
            // at the source-scan level for files in asset postprocessor context.
            AssetPostprocessorDeferral.FlushForTesting();
        }

        [SetUp]
        public override void BaseSetUp()
        {
            // Canonical cross-fixture pollution tripwire. See
            // AssetPostprocessorTestHandlers.AssertCleanAndClearAll XML doc for
            // the rationale (why this runs FIRST, before any asset mutation or
            // processor configuration in this SetUp). Note: also runs BEFORE
            // SkipIfDeferralDisabled so leaked-in pollution is surfaced even
            // when this fixture's tests are going to skip, rather than rolling
            // forward to whatever fixture runs next. Placed BEFORE
            // base.BaseSetUp() to match the placement convention enforced by
            // AssetContextFixturesCallCrossFixturePollutionTripwire.
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();
            base.BaseSetUp();
            SkipIfDeferralDisabled();
            EnsureTestFolder();
            // EnsureTestFolder may mutate the AssetDatabase (CreateFolder +
            // Refresh) and schedule a drain. Flush+clear now (the helper
            // internally flushes then clears), against the still-unconfigured
            // processor, so the drain doesn't land during the test body and
            // populate handler statics. Mirrors the post-mutation flush pattern
            // in DetectAssetChangeProcessorTests.BaseSetUp.
            AssetPostprocessorTestHandlers.FlushAndClearAll();
            DetectAssetChangeProcessor.ResetForTesting();
            DetectAssetChangeProcessor.IncludeTestAssets = true;
            // Declare this fixture's folder as the only path the processor may react
            // to. Prevents cross-fixture pollution: assets created under this fixture's
            // TestRoot trigger handlers, but assets created under any other fixture's
            // TestRoot are structurally excluded even when IncludeTestAssets is true.
            DetectAssetChangeProcessor.TestAssetFolderAllowlist = new[] { TestRoot + "/" };
        }

        [TearDown]
        public override void TearDown()
        {
            DetectAssetChangeProcessor.TestAssetFolderAllowlist = null;
            DetectAssetChangeProcessor.ResetForTesting();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            base.TearDown();
            // Flush AFTER every asset-mutating operation (Refresh above, plus any
            // asset deletion performed by base.TearDown) so drains scheduled by
            // those ops run before we clear handler statics.
            AssetPostprocessorDeferral.FlushForTesting();
            AssetPostprocessorTestHandlers.FlushAndClearAll();
        }

        /// <summary>
        /// Exercises the exact #234 code path: a DetectAssetChanged subscriber with
        /// SearchPrefabs, a prefab carrying that subscriber plus a SpriteRenderer (which
        /// fires Unity's internal sprite-lifecycle relays during deserialization), and a
        /// matching asset import that forces EnumeratePrefabComponents ->
        /// GetComponentsInChildren to run. Pre-fix this combination emitted
        /// "SendMessage cannot be called..." warnings.
        /// </summary>
        [Test]
        public void DetectAssetChangedPrefabSubscriberDoesNotEmitSendMessageWarnings()
        {
            string prefabPath = TestRoot + "/HygienePrefabSubscriber.prefab";
            string payloadPath = TestRoot + "/HygienePayload.asset";
            TrackAssetPath(prefabPath);
            TrackAssetPath(payloadPath);

            // Set up a prefab that (a) carries the TestPrefabAssetChangeHandler
            // MonoBehaviour (subscriber to TestDetectableAsset via SearchPrefabs), and
            // (b) has a SpriteRenderer so deserialization triggers Unity's internal
            // sprite-lifecycle SendMessage relays — the exact setup reproducing #234.
            ExecuteWithImmediateImport(() =>
            {
                GameObject prefabSource = new("HygienePrefabSubscriber");
                try
                {
                    prefabSource.AddComponent<TestPrefabAssetChangeHandler>();
                    SpriteRenderer renderer = prefabSource.AddComponent<SpriteRenderer>();
                    renderer.drawMode = SpriteDrawMode.Tiled;
                    renderer.size = new Vector2(2f, 2f);

                    GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(
                        prefabSource,
                        prefabPath,
                        out bool success
                    );
                    Track(savedPrefab);
                    Assert.IsTrue(success, "Prefab save should succeed");
                }
                finally
                {
                    Object.DestroyImmediate(prefabSource); // UNH-SUPPRESS: Test cleanup
                }
            });

            using EditorLogScope logScope = new();

            // Now create the matching asset. This triggers OnPostprocessAllAssets ->
            // EnqueueAssetChanges -> AssetPostprocessorDeferral.Schedule. After the
            // flush, the drain runs EnumeratePrefabComponents + GetComponentsInChildren
            // on the prefab above, and MethodInfo.Invoke on the subscriber — the exact
            // sequence that emitted SendMessage warnings pre-fix.
            ExecuteWithImmediateImport(() =>
            {
                TestDetectableAsset payload = Track(
                    ScriptableObject.CreateInstance<TestDetectableAsset>()
                );
                AssetDatabase.CreateAsset(payload, payloadPath);
            });

            AssetPostprocessorDeferral.FlushForTesting();

            logScope.AssertNoSendMessageWarnings();
        }

        /// <summary>
        /// Directly drives the sub-asset path (HasMatchingSubAsset -> LoadAllAssetsAtPath)
        /// via ProcessChangesForTesting to ensure that code path also does not emit
        /// SendMessage warnings when routed through the deferral helper. This
        /// complements the prefab test above by covering the second #234 trigger site.
        /// </summary>
        [Test]
        public void SubAssetLoadDoesNotEmitSendMessageWarnings()
        {
            string texturePath = TestRoot + "/HygieneSpriteTexture.png";
            string prefabPath = TestRoot + "/HygieneSpriteConsumerPrefab.prefab";
            TrackAssetPath(texturePath);
            TrackAssetPath(prefabPath);

            // Prepare a prefab that carries a SpriteRenderer so prefab enumeration has
            // work to do during the drain (forcing deserialization of sprite-bearing
            // prefabs — another #234 trigger path).
            ExecuteWithImmediateImport(() =>
            {
                GameObject prefabSource = new("HygieneSpriteConsumerPrefab");
                try
                {
                    prefabSource.AddComponent<SpriteRenderer>();
                    GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(
                        prefabSource,
                        prefabPath,
                        out bool success
                    );
                    Track(savedPrefab);
                    Assert.IsTrue(success, "Prefab save should succeed");
                }
                finally
                {
                    Object.DestroyImmediate(prefabSource); // UNH-SUPPRESS: Test cleanup
                }
            });

            using EditorLogScope logScope = new();

            ExecuteWithImmediateImport(() =>
            {
                WriteSolidColorTexture(texturePath, 32, 32, Color.white);
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);

                TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Multiple;
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
            });

            AssetPostprocessorDeferral.FlushForTesting();

            logScope.AssertNoSendMessageWarnings();
        }

        private static void SkipIfDeferralDisabled()
        {
            if (!UnityHelpersSettings.GetDeferAssetPostprocessorCallbacks())
            {
                Assert.Inconclusive(
                    "Skipping: UnityHelpersSettings.GetDeferAssetPostprocessorCallbacks() is false. "
                        + "This test only exercises the deferred path; re-enable the setting to run it."
                );
            }
        }

        private static void EnsureTestFolder()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string absolute = Path.Combine(projectRoot, TestRoot);
                if (!Directory.Exists(absolute))
                {
                    Directory.CreateDirectory(absolute);
                }
            }

            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.CreateFolder(
                    "Assets",
                    Path.GetFileName(TestRoot) ?? "__AssetPostprocessorHygieneTests__"
                );
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static void WriteSolidColorTexture(string path, int width, int height, Color color)
        {
            Texture2D texture = new(width, height, TextureFormat.RGBA32, mipChain: false);
            try
            {
                Color[] pixels = new Color[width * height];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = color;
                }

                texture.SetPixels(pixels);
                texture.Apply();

                byte[] encoded = texture.EncodeToPNG();
                string absolutePath = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                    path
                );
                File.WriteAllBytes(absolutePath, encoded);
            }
            finally
            {
                Object.DestroyImmediate(texture); // UNH-SUPPRESS: Test cleanup
            }
        }
    }
}
