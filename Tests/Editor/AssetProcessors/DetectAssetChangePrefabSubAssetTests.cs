// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Regression tests for <see href="https://github.com/wallstop/unity-helpers/issues/280">#280</see>.
    /// <c>AssetDatabase.GetMainAssetTypeAtPath</c> reports <see cref="GameObject"/> for a prefab, so
    /// a watcher on any other type used to fall through to
    /// <c>AssetDatabase.LoadAllAssetsAtPath</c> — which deserializes the whole prefab and runs every
    /// component's <c>OnValidate</c>, producing the reporter's
    /// "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate" one editor tick
    /// after each import.
    ///
    /// <para>The primary test counts loads rather than warnings: a prefab that was never
    /// deserialized cannot warn, whatever the consumer's <c>OnValidate</c> does, so the counter
    /// proves the fix where a quiet console only fails to disprove it.</para>
    ///
    /// <para>The remaining tests guard what the sub-asset probe still has to do: a nested
    /// ScriptableObject and a texture's sprites must keep matching watchers on their own types.</para>
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class DetectAssetChangePrefabSubAssetTests : BatchedEditorTestBase
    {
        // Owned by the sprite handler because that handler filters on it; see its XML doc.
        private const string TestRoot = TestSpriteSubAssetChangeHandler.WatchedFolder;

        private const string SendMessageMessagePrefix =
            "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate";

        private AssetChangeDetectionEnabledScope _watcherScope;

        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            EnsureTestFolder();
            TrackFolder(TestRoot);
            // EnsureTestFolder may call AssetDatabase.CreateFolder + Refresh, which schedules
            // drains. Flush explicitly so the first test's BaseSetUp does not inherit a pending
            // drain.
            AssetPostprocessorDeferral.FlushForTesting();
        }

        [SetUp]
        public override void BaseSetUp()
        {
            // Canonical cross-fixture pollution tripwire, placed BEFORE base.BaseSetUp() to match
            // the placement convention enforced by
            // AssetContextFixturesCallCrossFixturePollutionTripwire.
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();
            base.BaseSetUp();
            EnsureTestFolder();
            // EnsureTestFolder may mutate the AssetDatabase and schedule a drain. Flush+clear now,
            // against the still-unconfigured processor, so the drain cannot land during the test
            // body and populate handler statics.
            AssetPostprocessorTestHandlers.FlushAndClearAll();
            DetectAssetChangeProcessor.ResetForTesting();
            // The watcher declines to initialize in batch mode, which is where CI runs EditMode.
            // This fixture exists to exercise the watcher, so force it on.
            _watcherScope = AssetChangeDetectionUtility.EnabledScope(true);
            DetectAssetChangeProcessor.IncludeTestAssets = true;
            // Declare this fixture's folder as the only path the processor may react to, so assets
            // created by any other fixture are structurally excluded.
            DetectAssetChangeProcessor.TestAssetFolderAllowlist = new[] { TestRoot + "/" };
            TestOnValidateCountingComponent.Clear();
        }

        [TearDown]
        public override void TearDown()
        {
            TestOnValidateCountingComponent.Clear();
            DetectAssetChangeProcessor.TestAssetFolderAllowlist = null;
            DetectAssetChangeProcessor.ResetForTesting();
            _watcherScope?.Dispose();
            _watcherScope = null;
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            base.TearDown();
            // Flush AFTER every asset-mutating operation (Refresh above, plus any asset deletion
            // performed by base.TearDown) so drains scheduled by those ops run before we clear
            // handler statics.
            AssetPostprocessorDeferral.FlushForTesting();
            AssetPostprocessorTestHandlers.FlushAndClearAll();
        }

        /// <summary>
        /// The load-count regression. A prefab carrying a component whose <c>OnValidate</c>
        /// increments a counter is fed to the watcher-matching pass; the counter must stay at zero,
        /// which is only possible if the prefab was never deserialized.
        /// </summary>
        [Test]
        public void PrefabIsNeverDeserializedWhenMatchedAgainstWatchers()
        {
            string prefabPath = TestRoot + "/LoadCountProbe.prefab";
            CreateProbePrefab(prefabPath);

            // The premise of #280: the prefab's main type is GameObject, so every watcher on some
            // other type reaches the sub-asset probe. Without this the test could pass because
            // nothing asked the question at all.
            Assert.AreEqual(
                typeof(GameObject),
                AssetDatabase.GetMainAssetTypeAtPath(prefabPath),
                $"Prefab '{prefabPath}' should report GameObject as its main asset type"
            );

            DetectAssetChangeProcessor.EnsureInitializedForTesting();
            Dictionary<Type, DetectAssetChangeProcessor.AssetWatcher> watchers =
                DetectAssetChangeProcessor.GetSettingsForTesting().WatchersByAssetType;
            Assert.IsTrue(
                watchers.ContainsKey(typeof(TestDetectableAsset)),
                "Expected a registered watcher on a non-GameObject type, otherwise no watcher "
                    + "would consult the sub-asset probe and this test would be vacuous"
            );

            // Creating and importing the prefab legitimately runs OnValidate. Drain first, then
            // zero the counter, so it measures only the watcher-matching pass below.
            AssetPostprocessorDeferral.FlushForTesting();
            TestOnValidateCountingComponent.Clear();

            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { prefabPath },
                null,
                null,
                null
            );
            AssetPostprocessorDeferral.FlushForTesting();

            Assert.AreEqual(
                0,
                TestOnValidateCountingComponent.OnValidateCount,
                $"Matching '{prefabPath}' against watchers deserialized the prefab and ran "
                    + "OnValidate. The decision is a Type predicate, and every object the load "
                    + "produces is discarded, so the load is pure waste that costs the consumer a "
                    + "console warning on every import (#280)."
            );
        }

        /// <summary>
        /// Pins the reporter's exact symptom: with the probe's <c>OnValidate</c> armed to send a
        /// message, the watcher-matching pass must produce no
        /// "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate".
        /// </summary>
        [Test]
        public void PrefabMatchedAgainstWatchersEmitsNoSendMessageWarnings()
        {
            string prefabPath = TestRoot + "/SendMessageProbe.prefab";
            CreateProbePrefab(prefabPath);

            DetectAssetChangeProcessor.EnsureInitializedForTesting();
            AssetPostprocessorDeferral.FlushForTesting();
            TestOnValidateCountingComponent.Clear();
            TestOnValidateCountingComponent.EmitSendMessageDuringValidate = true;

            using EditorLogScope logScope = new();

            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { prefabPath },
                null,
                null,
                null
            );
            AssetPostprocessorDeferral.FlushForTesting();

            logScope.AssertNoSendMessageWarnings();
            AssertNoSendMessageErrors(logScope);
        }

        /// <summary>
        /// Preservation guard: an <c>.asset</c> whose main type no watcher matches must still match
        /// a watcher on the type of a ScriptableObject nested into it with
        /// <c>AssetDatabase.AddObjectToAsset</c>. This is the case the sub-asset probe exists for,
        /// and the prefab guard must not break it.
        /// </summary>
        [Test]
        public void NestedScriptableObjectSubAssetStillMatchesWatcherOnNestedType()
        {
            string containerPath = TestRoot + "/NestedSubAssetContainer.asset";
            TrackAssetPath(containerPath);

            ExecuteWithImmediateImport(
                () =>
                {
                    AssetDatabaseBatchHelper.EnsureAssetParentFolder(containerPath);
                    TestSubAssetContainerAsset container = Track(
                        ScriptableObject.CreateInstance<TestSubAssetContainerAsset>()
                    );
                    container.name = "NestedSubAssetContainer";
                    AssetDatabase.CreateAsset(container, containerPath);

                    TestDetectableAsset nested = Track(
                        ScriptableObject.CreateInstance<TestDetectableAsset>()
                    );
                    nested.name = "NestedDetectable";
                    AssetDatabase.AddObjectToAsset(nested, container);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.ImportAsset(
                        containerPath,
                        ImportAssetOptions.ForceSynchronousImport
                    );
                },
                refreshAfter: true
            );

            Assert.AreEqual(
                typeof(TestSubAssetContainerAsset),
                AssetDatabase.GetMainAssetTypeAtPath(containerPath),
                $"'{containerPath}' should report the container as its main asset type, otherwise "
                    + "the watcher would match on the main type and never reach the sub-asset probe"
            );
            Assert.IsTrue(
                HasSubAssetOfType<TestDetectableAsset>(containerPath),
                $"'{containerPath}' should carry a nested {nameof(TestDetectableAsset)} sub-asset"
            );

            AssetPostprocessorDeferral.FlushForTesting();
            AssetPostprocessorTestHandlers.FlushAndClearAll();

            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { containerPath },
                null,
                null,
                null
            );
            AssetPostprocessorDeferral.FlushForTesting();

            Assert.IsTrue(
                RecordedCreatedPathsContain(
                    TestDetectAssetChangeHandler.RecordedContexts,
                    containerPath
                ),
                $"A watcher on {nameof(TestDetectableAsset)} should still match '{containerPath}' "
                    + "through its nested sub-asset"
            );
        }

        /// <summary>
        /// Preservation guard for the other case the sub-asset probe exists for: sprites are never
        /// a texture's main asset type, so a <see cref="Sprite"/> watcher can only match a texture
        /// through its sub-assets.
        /// </summary>
        [Test]
        public void TextureSpriteSubAssetStillMatchesSpriteWatcher()
        {
            string texturePath = TestRoot + "/SpriteSubAsset.png";
            TrackAssetPath(texturePath);

            ExecuteWithImmediateImport(
                () =>
                {
                    AssetDatabaseBatchHelper.EnsureAssetParentFolder(texturePath);
                    WriteSolidColorTexture(texturePath, 8, 8, Color.white);
                    AssetDatabase.ImportAsset(
                        texturePath,
                        ImportAssetOptions.ForceSynchronousImport
                    );

                    TextureImporter importer =
                        AssetImporter.GetAtPath(texturePath) as TextureImporter;
                    if (importer != null)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        // Single, not Multiple: Multiple without explicit sprite rects generates
                        // ZERO sprite sub-assets, which would leave this test unable to fail.
                        importer.spriteImportMode = SpriteImportMode.Single;
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                    }
                },
                refreshAfter: true
            );

            if (!HasSubAssetOfType<Sprite>(texturePath))
            {
                Assert.Inconclusive(
                    $"Skipping: importing '{texturePath}' produced no Sprite sub-asset in this "
                        + "environment, so there is nothing for the sub-asset probe to match."
                );
            }

            Assert.AreEqual(
                typeof(Texture2D),
                AssetDatabase.GetMainAssetTypeAtPath(texturePath),
                $"'{texturePath}' should report Texture2D as its main asset type, otherwise the "
                    + "Sprite watcher would match on the main type and never reach the sub-asset probe"
            );

            AssetPostprocessorDeferral.FlushForTesting();
            AssetPostprocessorTestHandlers.FlushAndClearAll();

            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { texturePath },
                null,
                null,
                null
            );
            AssetPostprocessorDeferral.FlushForTesting();

            Assert.IsTrue(
                RecordedCreatedPathsContain(
                    TestSpriteSubAssetChangeHandler.RecordedContexts,
                    texturePath
                ),
                $"A watcher on {nameof(Sprite)} should still match '{texturePath}' through its "
                    + "sprite sub-assets"
            );
        }

        private void CreateProbePrefab(string prefabPath)
        {
            TrackAssetPath(prefabPath);
            ExecuteWithImmediateImport(() =>
            {
                AssetDatabaseBatchHelper.EnsureAssetParentFolder(prefabPath);
                GameObject prefabSource = new("OnValidateProbe");
                try
                {
                    prefabSource.AddComponent<TestOnValidateCountingComponent>();
                    GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(
                        prefabSource,
                        prefabPath,
                        out bool success
                    );
                    Track(savedPrefab);
                    Assert.IsTrue(success, $"Prefab save to '{prefabPath}' should succeed");
                }
                finally
                {
                    Object.DestroyImmediate(prefabSource); // UNH-SUPPRESS: Test cleanup
                }
            });
        }

        private static bool HasSubAssetOfType<T>(string assetPath)
            where T : Object
        {
            Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (allAssets == null)
            {
                return false;
            }

            Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            for (int i = 0; i < allAssets.Length; i++)
            {
                Object candidate = allAssets[i];
                if (candidate != null && candidate != mainAsset && candidate is T)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RecordedCreatedPathsContain(
            IReadOnlyList<AssetChangeContext> recordedContexts,
            string assetPath
        )
        {
            if (recordedContexts == null)
            {
                return false;
            }

            for (int i = 0; i < recordedContexts.Count; i++)
            {
                AssetChangeContext context = recordedContexts[i];
                if (context == null)
                {
                    continue;
                }

                IReadOnlyList<string> createdPaths = context.CreatedAssetPaths;
                for (int j = 0; j < createdPaths.Count; j++)
                {
                    if (
                        string.Equals(
                            createdPaths[j],
                            assetPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // EditorLogScope only classifies the #280 message as a warning. Unity's own log level for
        // it has varied, and an errored classification would make the warning assertion silently
        // vacuous, so check both buckets.
        private static void AssertNoSendMessageErrors(EditorLogScope logScope)
        {
            IReadOnlyList<EditorLogScope.LogRecord> errors = logScope.Errors;
            for (int i = 0; i < errors.Count; i++)
            {
                EditorLogScope.LogRecord record = errors[i];
                Assert.IsFalse(
                    record.Condition.StartsWith(SendMessageMessagePrefix, StringComparison.Ordinal),
                    $"Expected no SendMessage diagnostics, but an error was logged: {record.Condition}"
                );
            }
        }

        private static void EnsureTestFolder()
        {
            // Route through the single batch-safe helper: it pauses the fixture-wide batch and
            // creates the folder through the AssetDatabase synchronously, avoiding the raw
            // Directory.CreateDirectory path that leaves the AssetDatabase out of sync.
            if (!AssetDatabaseBatchHelper.EnsureAssetFolder(TestRoot))
            {
                Debug.LogWarning(
                    $"EnsureTestFolder: Failed to register folder '{TestRoot}' in the AssetDatabase."
                );
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
