// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Windows
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class PrefabCheckerFolderAdditionTests : CommonTestBase
    {
        private const string Root = "Assets/Temp/PrefabCheckerFolderAdditionTests";
        private const string TempRoot = "Assets/Temp";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            CleanupAllKnownTestFolders();
            CleanupTempFoldersAndDuplicates();
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            EnsureFolder(Root);

            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                UnityEditor.ImportAssetOptions.ForceSynchronousImport
            );
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();

            CleanupTrackedFoldersAndAssets();

            DeleteFolderAndContents(Root);
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            base.OneTimeTearDown();

            DeleteFolderAndContents(Root);

            CleanupTempFoldersAndDuplicates();
            CleanupAllKnownTestFolders();
        }

        /// <summary>
        /// Cleans up the Assets/Temp folder and all its duplicates (Temp 1, Temp 2, etc.).
        /// </summary>
        private static void CleanupTempFoldersAndDuplicates()
        {
            if (UnityEditor.AssetDatabase.IsValidFolder("Assets"))
            {
                string[] subFolders = UnityEditor.AssetDatabase.GetSubFolders("Assets");
                if (subFolders != null)
                {
                    foreach (string folder in subFolders)
                    {
                        string name = Path.GetFileName(folder);
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        if (
                            string.Equals(name, "Temp", System.StringComparison.OrdinalIgnoreCase)
                            || (
                                name.StartsWith("Temp ", System.StringComparison.OrdinalIgnoreCase)
                                && int.TryParse(name.Substring(5), out _)
                            )
                        )
                        {
                            DeleteFolderAndContents(folder);
                        }
                    }
                }
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string assetsOnDisk = Path.Combine(projectRoot, "Assets");
                if (Directory.Exists(assetsOnDisk))
                {
                    try
                    {
                        foreach (string dir in Directory.GetDirectories(assetsOnDisk))
                        {
                            string name = Path.GetFileName(dir);
                            if (string.IsNullOrEmpty(name))
                            {
                                continue;
                            }

                            if (
                                string.Equals(
                                    name,
                                    "Temp",
                                    System.StringComparison.OrdinalIgnoreCase
                                )
                                || (
                                    name.StartsWith(
                                        "Temp ",
                                        System.StringComparison.OrdinalIgnoreCase
                                    ) && int.TryParse(name.Substring(5), out _)
                                )
                            )
                            {
                                try
                                {
                                    Directory.Delete(dir, recursive: true);
                                }
                                catch
                                {
                                    // Ignore - folder may be locked
                                }

                                string metaPath = dir + ".meta";
                                if (File.Exists(metaPath))
                                {
                                    try
                                    {
                                        File.Delete(metaPath);
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
        }

        /// <summary>
        /// Deletes a folder and all its contents.
        /// </summary>
        private static void DeleteFolderAndContents(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return;
            }

            if (UnityEditor.AssetDatabase.IsValidFolder(folderPath))
            {
                string[] guids = UnityEditor.AssetDatabase.FindAssets(
                    string.Empty,
                    new[] { folderPath }
                );
                if (guids != null)
                {
                    foreach (string guid in guids)
                    {
                        string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                        if (
                            !string.IsNullOrEmpty(assetPath)
                            && !UnityEditor.AssetDatabase.IsValidFolder(assetPath)
                        )
                        {
                            UnityEditor.AssetDatabase.DeleteAsset(assetPath);
                        }
                    }
                }

                string[] subFolders = UnityEditor.AssetDatabase.GetSubFolders(folderPath);
                if (subFolders != null)
                {
                    foreach (string sub in subFolders)
                    {
                        DeleteFolderAndContents(sub);
                    }
                }

                UnityEditor.AssetDatabase.DeleteAsset(folderPath);
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string absolutePath = Path.Combine(
                    projectRoot,
                    folderPath.Replace('/', Path.DirectorySeparatorChar)
                );
                if (Directory.Exists(absolutePath))
                {
                    try
                    {
                        Directory.Delete(absolutePath, recursive: true);
                    }
                    catch { }
                }

                string metaPath = absolutePath + ".meta";
                if (File.Exists(metaPath))
                {
                    try
                    {
                        File.Delete(metaPath);
                    }
                    catch { }
                }
            }
        }

        [Test]
        public void TryAddFolderFromAbsoluteAddsAssetsRoot()
        {
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
            bool added = checker.TryAddFolderFromAbsolute(Application.dataPath);
            Assert.IsTrue(added, "Expected adding from Application.dataPath to succeed.");
            CollectionAssert.Contains(checker._assetPaths, "Assets");
        }

        [Test]
        public void AddAssetFolderAddsValidFolder()
        {
            string sub = Path.Combine(Root, "Sub").SanitizePath();
            EnsureFolder(sub);

            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                UnityEditor.ImportAssetOptions.ForceSynchronousImport
            );

            bool folderIsValid = UnityEditor.AssetDatabase.IsValidFolder(sub);
            Assert.IsTrue(
                folderIsValid,
                $"Setup failed: Folder '{sub}' should be valid after EnsureFolder + Refresh. "
                    + $"This indicates a problem with test setup, not the code under test."
            );

            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
            bool added = checker.AddAssetFolder(sub);
            Assert.IsTrue(added, "Expected valid Unity folder to be added.");
            CollectionAssert.Contains(checker._assetPaths, sub);
        }

        [Test]
        public void AddAssetFolderDedupesExisting()
        {
            string sub = Path.Combine(Root, "Dup").SanitizePath();
            EnsureFolder(sub);

            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                UnityEditor.ImportAssetOptions.ForceSynchronousImport
            );

            bool folderIsValid = UnityEditor.AssetDatabase.IsValidFolder(sub);
            Assert.IsTrue(
                folderIsValid,
                $"Setup failed: Folder '{sub}' should be valid after EnsureFolder + Refresh. "
                    + $"This indicates a problem with test setup, not the code under test."
            );

            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
            bool first = checker.AddAssetFolder(sub);
            bool second = checker.AddAssetFolder(sub);
            Assert.IsTrue(first, "First add should succeed.");
            Assert.IsFalse(second, "Second add should be rejected as duplicate.");
            Assert.AreEqual(1, checker._assetPaths.Count, "Only one entry should exist.");
        }

        [Test]
        public void AddAssetFolderRejectsInvalidFolder()
        {
            string invalid = Path.Combine(Root, "DoesNotExist").SanitizePath();
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
            bool added = checker.AddAssetFolder(invalid);
            Assert.IsFalse(added, "Invalid folder should not be added.");
            CollectionAssert.DoesNotContain(checker._assetPaths, invalid);
        }

        /// <summary>
        /// Data source for testing various edge case folder paths.
        /// </summary>
        private static IEnumerable<TestCaseData> EdgeCaseFolderPaths()
        {
            yield return new TestCaseData(null, false)
                .SetName("NullPath")
                .SetDescription("Null path should be rejected");
            yield return new TestCaseData(string.Empty, false)
                .SetName("EmptyPath")
                .SetDescription("Empty path should be rejected");
            yield return new TestCaseData("   ", false)
                .SetName("WhitespacePath")
                .SetDescription("Whitespace-only path should be rejected");
            yield return new TestCaseData("Assets", true)
                .SetName("AssetsRoot")
                .SetDescription("Assets root folder should be accepted");
        }

        [Test]
        [TestCaseSource(nameof(EdgeCaseFolderPaths))]
        public void AddAssetFolderHandlesEdgeCases(string path, bool expectedResult)
        {
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());
            bool result = checker.AddAssetFolder(path);

            Assert.AreEqual(
                expectedResult,
                result,
                $"AddAssetFolder(\"{path ?? "null"}\") should return {expectedResult}."
            );
        }

        [Test]
        public void AddAssetFolderHandlesNestedFolders()
        {
            string level1 = Path.Combine(Root, "Level1").SanitizePath();
            string level2 = Path.Combine(level1, "Level2").SanitizePath();
            string level3 = Path.Combine(level2, "Level3").SanitizePath();

            EnsureFolder(level3);
            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                UnityEditor.ImportAssetOptions.ForceSynchronousImport
            );

            Assert.IsTrue(
                UnityEditor.AssetDatabase.IsValidFolder(level1),
                "Level1 folder should exist"
            );
            Assert.IsTrue(
                UnityEditor.AssetDatabase.IsValidFolder(level2),
                "Level2 folder should exist"
            );
            Assert.IsTrue(
                UnityEditor.AssetDatabase.IsValidFolder(level3),
                "Level3 folder should exist"
            );

            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

            bool addedLevel3 = checker.AddAssetFolder(level3);
            Assert.IsTrue(addedLevel3, "Should be able to add deeply nested folder");

            bool addedLevel1 = checker.AddAssetFolder(level1);
            Assert.IsTrue(addedLevel1, "Should be able to add parent folder");

            Assert.AreEqual(2, checker._assetPaths.Count, "Both folders should be in the list");
        }

        /// <summary>
        /// Data source for testing various paths outside the Unity project.
        /// All of these paths should be rejected with an error log.
        /// </summary>
        private static IEnumerable<TestCaseData> OutsideProjectPaths()
        {
            yield return new TestCaseData(Path.GetTempPath())
                .SetName("SystemTempPath")
                .SetDescription("System temp directory should be rejected");

            // Windows-specific paths (will only be tested on Windows)
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                yield return new TestCaseData(@"C:\Windows")
                    .SetName("WindowsSystemFolder")
                    .SetDescription("Windows system folder should be rejected");

                yield return new TestCaseData(@"C:\Program Files")
                    .SetName("ProgramFilesFolder")
                    .SetDescription("Program Files folder should be rejected");

                yield return new TestCaseData(@"C:\")
                    .SetName("DriveRoot")
                    .SetDescription("Drive root should be rejected");
            }

            // macOS/Linux paths (will only be tested on those platforms)
            if (
                Application.platform == RuntimePlatform.OSXEditor
                || Application.platform == RuntimePlatform.LinuxEditor
            )
            {
                yield return new TestCaseData("/tmp")
                    .SetName("UnixTmpFolder")
                    .SetDescription("Unix /tmp folder should be rejected");

                yield return new TestCaseData("/usr")
                    .SetName("UnixUsrFolder")
                    .SetDescription("Unix /usr folder should be rejected");

                yield return new TestCaseData("/")
                    .SetName("RootFolder")
                    .SetDescription("Root folder should be rejected");
            }

            string userProfile = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.UserProfile
            );
            if (!string.IsNullOrEmpty(userProfile) && Directory.Exists(userProfile))
            {
                yield return new TestCaseData(userProfile)
                    .SetName("UserProfileFolder")
                    .SetDescription("User profile folder should be rejected");
            }
        }

        [Test]
        [TestCaseSource(nameof(OutsideProjectPaths))]
        public void TryAddFolderFromAbsoluteRejectsOutsideProject(string outsidePath)
        {
            string projectDataPath = Application.dataPath;
            string projectRoot = Path.GetDirectoryName(projectDataPath);

            // Skip test if path doesn't exist (platform-specific paths may not exist)
            if (!Directory.Exists(outsidePath))
            {
                Assert.Ignore(
                    $"Skipping test because path does not exist on this system: {outsidePath}"
                );
                return;
            }

            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

            LogAssert.Expect(
                LogType.Error,
                new Regex(@"Selected folder must be inside the Unity project's Assets folder\.")
            );

            bool added = checker.TryAddFolderFromAbsolute(outsidePath);

            Assert.IsFalse(
                added,
                $"Path outside Unity project should be rejected.\n"
                    + $"  Test path: {outsidePath}\n"
                    + $"  Project dataPath: {projectDataPath}\n"
                    + $"  Project root: {projectRoot}"
            );

            CollectionAssert.DoesNotContain(
                checker._assetPaths,
                outsidePath,
                $"Outside path should not be in _assetPaths list: {outsidePath}"
            );
        }

        /// <summary>
        /// Data source for testing edge cases of TryAddFolderFromAbsolute.
        /// </summary>
        private static IEnumerable<TestCaseData> TryAddFolderFromAbsoluteEdgeCases()
        {
            yield return new TestCaseData(null, false, false)
                .SetName("NullPath")
                .SetDescription("Null path should return false without error");

            yield return new TestCaseData(string.Empty, false, false)
                .SetName("EmptyPath")
                .SetDescription("Empty path should return false without error");

            yield return new TestCaseData("   ", false, false)
                .SetName("WhitespacePath")
                .SetDescription("Whitespace-only path should return false without error");

            // "///" is not whitespace, so it reaches path validation and is rejected as outside the project.
            yield return new TestCaseData("///", false, true)
                .SetName("OnlySlashes")
                .SetDescription("Path with only slashes should return false with error");

            yield return new TestCaseData(
                Path.Combine(Path.GetTempPath(), "NonExistentFolder12345"),
                false,
                true
            )
                .SetName("NonExistentAbsolutePath")
                .SetDescription("Non-existent absolute path should return false with error");
        }

        [Test]
        [TestCaseSource(nameof(TryAddFolderFromAbsoluteEdgeCases))]
        public void TryAddFolderFromAbsoluteHandlesEdgeCases(
            string path,
            bool expectedResult,
            bool expectsErrorLog
        )
        {
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

            if (expectsErrorLog)
            {
                LogAssert.Expect(
                    LogType.Error,
                    new Regex(@"Selected folder must be inside the Unity project's Assets folder\.")
                );
            }

            bool result = checker.TryAddFolderFromAbsolute(path);

            Assert.AreEqual(
                expectedResult,
                result,
                $"TryAddFolderFromAbsolute(\"{path ?? "null"}\") should return {expectedResult}."
            );
        }

        [Test]
        public void TryAddFolderFromAbsoluteAcceptsProjectAssetsFolder()
        {
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

            string assetsPath = Application.dataPath;

            bool added = checker.TryAddFolderFromAbsolute(assetsPath);

            Assert.IsTrue(
                added,
                $"Assets folder should be accepted.\n" + $"  Assets path: {assetsPath}"
            );

            CollectionAssert.Contains(
                checker._assetPaths,
                "Assets",
                "Assets should be in _assetPaths list"
            );
        }

        [Test]
        public void TryAddFolderFromAbsoluteAcceptsSubfolderOfAssets()
        {
            string sub = Path.Combine(Root, "AbsolutePathTest").SanitizePath();
            EnsureFolder(sub);
            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                UnityEditor.ImportAssetOptions.ForceSynchronousImport
            );

            Assert.IsTrue(
                UnityEditor.AssetDatabase.IsValidFolder(sub),
                $"Setup failed: Folder '{sub}' should be valid."
            );

            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string absolutePath = Path.Combine(projectRoot, sub)
                .Replace('/', Path.DirectorySeparatorChar);

            bool added = checker.TryAddFolderFromAbsolute(absolutePath);

            Assert.IsTrue(
                added,
                $"Subfolder of Assets should be accepted.\n"
                    + $"  Absolute path: {absolutePath}\n"
                    + $"  Expected relative: {sub}"
            );

            CollectionAssert.Contains(
                checker._assetPaths,
                sub,
                $"'{sub}' should be in _assetPaths list"
            );
        }

        [Test]
        public void TryAddFolderFromAbsoluteHandlesPathWithTrailingSlash()
        {
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

            string assetsPath = Application.dataPath + Path.DirectorySeparatorChar;

            bool added = checker.TryAddFolderFromAbsolute(assetsPath);

            Assert.IsTrue(
                added,
                $"Assets folder with trailing slash should be accepted.\n"
                    + $"  Assets path: {assetsPath}"
            );

            CollectionAssert.Contains(
                checker._assetPaths,
                "Assets",
                "Assets should be in _assetPaths list"
            );
        }

        [Test]
        public void TryAddFolderFromAbsoluteDeduplicatesExisting()
        {
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

            string assetsPath = Application.dataPath;

            bool firstAdd = checker.TryAddFolderFromAbsolute(assetsPath);
            Assert.IsTrue(firstAdd, "First add should succeed");

            LogAssert.Expect(LogType.Warning, new Regex(@"Folder '.*' is already in the list\."));

            bool secondAdd = checker.TryAddFolderFromAbsolute(assetsPath);
            Assert.IsFalse(secondAdd, "Second add should be rejected as duplicate");

            Assert.AreEqual(
                1,
                checker._assetPaths.Count,
                "Only one entry should exist after duplicate attempt"
            );
        }

        [Test]
        public void AddMultipleFoldersInSequence()
        {
            string folder1 = Path.Combine(Root, "Folder1").SanitizePath();
            string folder2 = Path.Combine(Root, "Folder2").SanitizePath();
            string folder3 = Path.Combine(Root, "Folder3").SanitizePath();

            EnsureFolder(folder1);
            EnsureFolder(folder2);
            EnsureFolder(folder3);
            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                UnityEditor.ImportAssetOptions.ForceSynchronousImport
            );

            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

            bool added1 = checker.AddAssetFolder(folder1);
            bool added2 = checker.AddAssetFolder(folder2);
            bool added3 = checker.AddAssetFolder(folder3);

            Assert.IsTrue(added1, "Folder1 should be added");
            Assert.IsTrue(added2, "Folder2 should be added");
            Assert.IsTrue(added3, "Folder3 should be added");
            Assert.AreEqual(
                3,
                checker._assetPaths.Count,
                "All three folders should be in the list"
            );

            CollectionAssert.Contains(checker._assetPaths, folder1);
            CollectionAssert.Contains(checker._assetPaths, folder2);
            CollectionAssert.Contains(checker._assetPaths, folder3);
        }

        /// <summary>
        /// Data source for testing paths with various trailing slash combinations.
        /// </summary>
        private static IEnumerable<TestCaseData> TrailingSlashVariations()
        {
            yield return new TestCaseData("/")
                .SetName("SingleTrailingForwardSlash")
                .SetDescription(
                    "Path with single trailing forward slash should normalize to Assets"
                );

            yield return new TestCaseData("\\")
                .SetName("SingleTrailingBackslash")
                .SetDescription("Path with single trailing backslash should normalize to Assets");

            yield return new TestCaseData("///")
                .SetName("MultipleTrailingForwardSlashes")
                .SetDescription(
                    "Path with multiple trailing forward slashes should normalize to Assets"
                );

            yield return new TestCaseData("/\\")
                .SetName("MixedTrailingSlashes")
                .SetDescription("Path with mixed trailing slashes should normalize to Assets");

            yield return new TestCaseData("\\\\")
                .SetName("DoubleBackslash")
                .SetDescription("Path with double backslash should normalize to Assets");
        }

        [Test]
        [TestCaseSource(nameof(TrailingSlashVariations))]
        public void TryAddFolderFromAbsoluteNormalizesTrailingSlashes(string trailingSuffix)
        {
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

            string assetsPath = Application.dataPath + trailingSuffix;

            bool added = checker.TryAddFolderFromAbsolute(assetsPath);

            Assert.IsTrue(
                added,
                $"Assets folder with trailing '{trailingSuffix}' should be accepted.\n"
                    + $"  Full path: {assetsPath}"
            );

            CollectionAssert.Contains(
                checker._assetPaths,
                "Assets",
                $"Assets should be in _assetPaths list (without trailing slashes). "
                    + $"Actual list: [{string.Join(", ", checker._assetPaths)}]"
            );

            Assert.IsFalse(
                checker._assetPaths.Exists(p => p.EndsWith("/") || p.EndsWith("\\")),
                $"No path in _assetPaths should have trailing slashes. "
                    + $"Actual list: [{string.Join(", ", checker._assetPaths)}]"
            );
        }

        /// <summary>
        /// Data source for testing paths with special characters that should be handled gracefully.
        /// </summary>
        private static IEnumerable<TestCaseData> SpecialCharacterPaths()
        {
            yield return new TestCaseData("\t", false, false)
                .SetName("TabOnlyPath")
                .SetDescription("Tab-only path should return false without error");

            yield return new TestCaseData("\n", false, false)
                .SetName("NewlineOnlyPath")
                .SetDescription("Newline-only path should return false without error");

            yield return new TestCaseData("\r", false, false)
                .SetName("CarriageReturnOnlyPath")
                .SetDescription("Carriage return only path should return false without error");

            yield return new TestCaseData(" \t\n\r ", false, false)
                .SetName("MixedWhitespacePath")
                .SetDescription("Mixed whitespace path should return false without error");

            yield return new TestCaseData("Assets<>*?|", false, true)
                .SetName("PathWithInvalidChars")
                .SetDescription("Path with invalid filesystem chars should be rejected");
        }

        [Test]
        [TestCaseSource(nameof(SpecialCharacterPaths))]
        public void TryAddFolderFromAbsoluteHandlesSpecialCharacters(
            string path,
            bool expectedResult,
            bool expectsErrorLog
        )
        {
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

            if (expectsErrorLog)
            {
                LogAssert.Expect(
                    LogType.Error,
                    new Regex(@"Selected folder must be inside the Unity project's Assets folder\.")
                );
            }

            bool result = checker.TryAddFolderFromAbsolute(path);

            Assert.AreEqual(
                expectedResult,
                result,
                $"TryAddFolderFromAbsolute with special characters should return {expectedResult}.\n"
                    + $"  Input: '{EscapeForDisplay(path)}'"
            );
        }

        /// <summary>
        /// Data source for testing case sensitivity in path handling via TryAddFolderFromAbsolute.
        /// TryAddFolderFromAbsolute normalizes "Assets" casing through TryGetUnityFolderFromAbsolute.
        /// </summary>
        private static IEnumerable<TestCaseData> CaseSensitivityAbsolutePaths()
        {
            yield return new TestCaseData("Assets")
                .SetName("NormalCaseAssets")
                .SetDescription("Normal casing 'Assets' should work");

            /*
                On case-insensitive filesystems the OS controls the root casing; assert only the normalized
                relative suffix.
            */
        }

        [Test]
        [TestCaseSource(nameof(CaseSensitivityAbsolutePaths))]
        public void TryAddFolderFromAbsoluteNormalizesCasing(string expectedNormalized)
        {
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

            bool added = checker.TryAddFolderFromAbsolute(Application.dataPath);

            Assert.IsTrue(added, $"TryAddFolderFromAbsolute should succeed for dataPath");

            CollectionAssert.Contains(
                checker._assetPaths,
                expectedNormalized,
                $"Path should be normalized to '{expectedNormalized}'. "
                    + $"Actual list: [{string.Join(", ", checker._assetPaths)}]"
            );
        }

        /// <summary>
        /// Tests that AddAssetFolder accepts valid relative paths with various casings.
        /// Note: AddAssetFolder does NOT normalize casing - it stores the path as-is.
        /// Unity's AssetDatabase.IsValidFolder is case-insensitive on Windows.
        /// </summary>
        [Test]
        public void AddAssetFolderAcceptsAssetsWithExactCasing()
        {
            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

            bool added = checker.AddAssetFolder("Assets");

            Assert.IsTrue(added, "AddAssetFolder('Assets') should succeed");
            CollectionAssert.Contains(
                checker._assetPaths,
                "Assets",
                "Path 'Assets' should be in _assetPaths list"
            );
        }

        [Test]
        public void TryAddFolderFromAbsoluteHandlesSubfolderWithTrailingSlash()
        {
            string sub = Path.Combine(Root, "TrailingSlashSubTest").SanitizePath();
            EnsureFolder(sub);
            AssetDatabaseBatchHelper.RefreshIfNotBatching(
                UnityEditor.ImportAssetOptions.ForceSynchronousImport
            );

            Assert.IsTrue(
                UnityEditor.AssetDatabase.IsValidFolder(sub),
                $"Setup failed: Folder '{sub}' should be valid."
            );

            PrefabChecker checker = Track(ScriptableObject.CreateInstance<PrefabChecker>());

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string absolutePath =
                Path.Combine(projectRoot, sub).Replace('/', Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            bool added = checker.TryAddFolderFromAbsolute(absolutePath);

            Assert.IsTrue(
                added,
                $"Subfolder with trailing slash should be accepted.\n"
                    + $"  Absolute path: {absolutePath}\n"
                    + $"  Expected relative: {sub}"
            );

            CollectionAssert.Contains(
                checker._assetPaths,
                sub,
                $"'{sub}' should be in _assetPaths list (without trailing slash)"
            );

            Assert.IsFalse(
                checker._assetPaths.Exists(p => p.EndsWith("/") || p.EndsWith("\\")),
                "No path should have trailing slashes"
            );
        }

        /// <summary>
        /// Helper to escape control characters for display in test messages.
        /// </summary>
        private static string EscapeForDisplay(string input)
        {
            if (input == null)
            {
                return "null";
            }

            return input
                .Replace("\t", "\\t")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace(" ", "·");
        }
    }
#endif
}
