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
            // Folder creation can queue drains that would pollute the first test.
            AssetPostprocessorDeferral.FlushForTesting();
        }

        [SetUp]
        public override void BaseSetUp()
        {
            // Check inherited handler pollution before base setup changes its attribution.
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();
            base.BaseSetUp();
            EnsureTestFolder();
            /*
                Flush setup mutations before configuring the processor so queued work cannot repopulate handler
                state during the test.
            */
            AssetPostprocessorTestHandlers.FlushAndClearAll();
            DetectAssetChangeProcessor.ResetForTesting();
            // Force the watcher on because CI runs this fixture in batch mode.
            _watcherScope = AssetChangeDetectionUtility.EnabledScope(true);
            DetectAssetChangeProcessor.IncludeTestAssets = true;
            // Restrict observed paths so other fixtures’ assets cannot invoke this handler.
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
            // Flush after base teardown because tracked-asset destruction can enqueue additional drains.
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

            // A prefab’s GameObject main type forces non-GameObject watchers through the sub-asset probe.
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

            /*
                Prefab import legitimately invokes OnValidate; reset its counter after draining to isolate
                watcher matching.
            */
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
                        // Multiple mode without explicit rects creates no sprite sub-assets and would make this test vacuous.
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
            foreach (Object candidate in allAssets)
            {
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

        /*
            Unity has changed the native log severity; checking both buckets prevents a vacuous warning
            assertion.
        */
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
            // Use batch-safe folder creation so AssetDatabase recognizes the folder immediately.
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
