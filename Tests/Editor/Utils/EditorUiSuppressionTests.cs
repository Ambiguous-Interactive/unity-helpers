// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.AssetProcessors;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class EditorUiSuppressionTests : CommonTestBase
    {
        private const string TestRoot = "Assets/Temp/EditorUiSuppressionTests";

        [SetUp]
        public override void BaseSetUp()
        {
            // Must precede base.BaseSetUp(); AssertCleanAndClearAll documents why it runs first.
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();
            base.BaseSetUp();
            EnsureFolder(TestRoot);
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            // Loop protection would otherwise trip as cleanup deletes several assets.
            DetectAssetChangeProcessor.ResetForTesting();
            CleanupTrackedFoldersAndAssets();
        }

        [Test]
        public void EditorUiSuppressIsSetByBaseClass()
        {
            Assert.IsTrue(
                EditorUi.Suppress,
                "EditorUi.Suppress should be true during test execution."
            );
        }

        [Test]
        public void ConfirmReturnsTrueWhenSuppressed()
        {
            bool result = EditorUi.Confirm("Test Dialog", "This should not show.", "OK", "Cancel");
            Assert.IsTrue(result, "Confirm should return default (true) when suppressed.");
        }

        [Test]
        public void ConfirmReturnsCustomDefaultWhenSuppressed()
        {
            bool result = EditorUi.Confirm(
                "Test Dialog",
                "This should not show.",
                "OK",
                "Cancel",
                defaultWhenSuppressed: false
            );
            Assert.IsFalse(result, "Confirm should return custom default (false) when suppressed.");
        }

        [Test]
        public void InfoDoesNotThrowWhenSuppressed()
        {
            Assert.DoesNotThrow(() => EditorUi.Info("Test", "Message"));
        }

        [Test]
        public void ShowProgressDoesNotThrowWhenSuppressed()
        {
            Assert.DoesNotThrow(() => EditorUi.ShowProgress("Title", "Info", 0.5f));
            EditorUi.ClearProgress();
        }

        [Test]
        public void CancelableProgressReturnsFalseWhenSuppressed()
        {
            bool cancelled = EditorUi.CancelableProgress("Title", "Info", 0.5f);
            Assert.IsFalse(cancelled, "CancelableProgress should return false when suppressed.");
            EditorUi.ClearProgress();
        }

        [UnityTest]
        public IEnumerator TryCopyAssetSilentCopiesFileWithoutDialog()
        {
            string sourcePath = TestRoot + "/source.txt";
            string destPath = TestRoot + "/dest.txt";

            string absoluteSource = System
                .IO.Path.Combine(
                    Application.dataPath.Substring(
                        0,
                        Application.dataPath.Length - "Assets".Length
                    ),
                    sourcePath
                )
                .SanitizePath();

            System.IO.File.WriteAllText(absoluteSource, "test content");
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
            yield return null;

            bool success = TryCopyAssetSilent(sourcePath, destPath);
            yield return null;

            Assert.IsTrue(success, "TryCopyAssetSilent should succeed.");

            string absoluteDest = System
                .IO.Path.Combine(
                    Application.dataPath.Substring(
                        0,
                        Application.dataPath.Length - "Assets".Length
                    ),
                    destPath
                )
                .SanitizePath();

            Assert.IsTrue(System.IO.File.Exists(absoluteDest), "Destination file should exist.");
            Assert.AreEqual("test content", System.IO.File.ReadAllText(absoluteDest));
        }

        [UnityTest]
        public IEnumerator TryCopyAssetSilentReturnsFalseForMissingSource()
        {
            string sourcePath = TestRoot + "/nonexistent.txt";
            string destPath = TestRoot + "/dest2.txt";

            bool success = TryCopyAssetSilent(sourcePath, destPath);
            yield return null;

            Assert.IsFalse(success, "TryCopyAssetSilent should return false for missing source.");
        }

        [Test]
        public void TryCopyAssetSilentReturnsFalseForNullPaths()
        {
            Assert.IsFalse(TryCopyAssetSilent(null, "dest"));
            Assert.IsFalse(TryCopyAssetSilent("source", null));
            Assert.IsFalse(TryCopyAssetSilent(string.Empty, "dest"));
            Assert.IsFalse(TryCopyAssetSilent("source", string.Empty));
        }
    }
#endif
}
