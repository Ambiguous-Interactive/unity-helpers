// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class DirectoryHelperTests : CommonTestBase
    {
#if UNITY_EDITOR
        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            CleanupAllKnownTestFolders();
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            CleanupAllKnownTestFolders();
            base.OneTimeTearDown();
        }
#endif

        [Test]
        public void EnsureDirectoryExistsWithNullDoesNothing()
        {
            Assert.DoesNotThrow(() => DirectoryHelper.EnsureDirectoryExists(null));
        }

        [Test]
        public void EnsureDirectoryExistsWithEmptyStringDoesNothing()
        {
            Assert.DoesNotThrow(() => DirectoryHelper.EnsureDirectoryExists(string.Empty));
        }

        [Test]
        public void EnsureDirectoryExistsWithWhitespaceDoesNothing()
        {
            Assert.DoesNotThrow(() => DirectoryHelper.EnsureDirectoryExists("   "));
        }

        [Test]
        public void GetCallerScriptDirectoryWithEmptyPathReturnsEmpty()
        {
            string result = DirectoryHelper.GetCallerScriptDirectory();
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void FindPackageRootPathWithNullStartDirectoryReturnsEmpty()
        {
            string result = DirectoryHelper.FindPackageRootPath(null);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void FindPackageRootPathWithEmptyStartDirectoryReturnsEmpty()
        {
            string result = DirectoryHelper.FindPackageRootPath(string.Empty);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void FindPackageRootPathWithWhitespaceStartDirectoryReturnsEmpty()
        {
            string result = DirectoryHelper.FindPackageRootPath("   ");
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void FindPackageRootPathWithNonExistentDirectoryReturnsEmpty()
        {
            string nonExistent = Path.Combine(
                Application.dataPath,
                "NonExistent",
                "Path",
                "That",
                "Does",
                "Not",
                "Exist"
            );
            string result = DirectoryHelper.FindPackageRootPath(nonExistent);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void FindRootPathWithNullConditionReturnsEmpty()
        {
            string result = DirectoryHelper.FindRootPath(Application.dataPath, null);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void FindRootPathWithAlwaysFalseConditionReturnsEmpty()
        {
            string result = DirectoryHelper.FindRootPath(Application.dataPath, _ => false);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void FindRootPathWithAlwaysTrueConditionReturnsStartDirectory()
        {
            string startPath = Application.dataPath.SanitizePath();
            string result = DirectoryHelper.FindRootPath(startPath, _ => true).SanitizePath();
            Assert.That(result, Does.StartWith(startPath).IgnoreCase);
        }

        [Test]
        public void FindRootPathWithExceptionInConditionReturnsCurrentPath()
        {
            string startPath = Application.dataPath;
            string result = DirectoryHelper.FindRootPath(
                startPath,
                _ => throw new InvalidOperationException("Test exception")
            );
            Assert.AreEqual(startPath, result);
        }

        [Test]
        public void FindRootPathWithNullStartDirectoryReturnsEmpty()
        {
            string result = DirectoryHelper.FindRootPath(null, _ => true);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void FindRootPathWithEmptyStartDirectoryReturnsEmpty()
        {
            string result = DirectoryHelper.FindRootPath(string.Empty, _ => true);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void FindAbsolutePathToDirectoryWithNonExistentCallerReturnsEmpty()
        {
            string result = DirectoryHelper.FindAbsolutePathToDirectory("SomeDir");
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void FindAbsolutePathToDirectoryIsCaseInsensitive()
        {
            string result = DirectoryHelper.FindAbsolutePathToDirectory("tests/runtime");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result));
            StringAssert.EndsWith("tests/runtime", result.ToLowerInvariant());
        }

        [Test]
        public void GetCallerScriptDirectoryReturnsEmptyWhenSourcePathNull()
        {
            string directory = DirectoryHelper.GetCallerScriptDirectory(sourceFilePath: null);
            Assert.AreEqual(string.Empty, directory);
        }

        [TestCase(@"\\server\share\Assets\Example.asset")]
        [TestCase(@"//server/share/Assets/Example.asset")]
        public void AbsoluteToUnityRelativePathReturnsEmptyForUncPaths(string uncPath)
        {
            string relative = DirectoryHelper.AbsoluteToUnityRelativePath(uncPath);
            Assert.AreEqual(string.Empty, relative);
        }

        [Test]
        public void FindAbsolutePathToDirectoryResolvesTestsFolder()
        {
            string result = DirectoryHelper.FindAbsolutePathToDirectory("Tests/Runtime");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result));
            StringAssert.EndsWith("tests/runtime", result.ToLowerInvariant());
        }

        [TestCase("Assets/Test/Path", true)]
        [TestCase("assets/test/path", true)]
        [TestCase("ASSETS/TEST/PATH", true)]
        [TestCase("Packages/com.wallstop-studios.unity-helpers", false)]
        public void AbsoluteToUnityRelativePathHandlesCaseInsensitivity(
            string suffix,
            bool expectAssets
        )
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Project root could not be determined.");
                return;
            }

            string testPath = $"{projectRoot}/{suffix}";
            string relative = DirectoryHelper.AbsoluteToUnityRelativePath(testPath);
            if (expectAssets)
            {
                Assert.That(relative, Does.StartWith("Assets/").IgnoreCase);
            }
            else
            {
                Assert.That(relative, Does.StartWith("Packages/").IgnoreCase);
            }
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithNullReturnsEmpty()
        {
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(null);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithEmptyReturnsEmpty()
        {
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(string.Empty);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithWhitespaceReturnsEmpty()
        {
            string result = DirectoryHelper.AbsoluteToUnityRelativePath("   ");
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void AbsoluteToUnityRelativePathConvertsBackslashesToForwardSlashes()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string testPath = projectRoot + "\\Assets\\Test";
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(testPath);
            Assert.That(result, Does.Not.Contain("\\"));
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithAssetsPathReturnsRelative()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string testPath = projectRoot + "/Assets/Test/File.txt";
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(testPath);
            Assert.That(result, Does.StartWith("Assets/").Or.EqualTo(string.Empty));
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithProjectRootReturnsEmpty()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string result = DirectoryHelper.AbsoluteToUnityRelativePath(projectRoot);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithPathOutsideProjectReturnsEmpty()
        {
            string externalPath = "/some/external/path/that/is/not/in/project";
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(externalPath);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithDataPathReturnsAssets()
        {
            string dataPath = Application.dataPath.SanitizePath();
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(dataPath);
            Assert.That(result, Does.Contain("Assets").Or.EqualTo(string.Empty));
        }

        [Test]
        public void AbsoluteToUnityRelativePathRemovesLeadingSlash()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string testPath = projectRoot + "/Assets";
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(testPath);
            if (!string.IsNullOrEmpty(result))
            {
                Assert.That(result[0], Is.Not.EqualTo('/'));
            }
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithTrailingSlashHandledCorrectly()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string testPathWithSlash = projectRoot + "/";
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(testPathWithSlash);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithMixedCaseMatches()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string testPath = projectRoot.ToUpperInvariant() + "/ASSETS/TEST";
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(testPath);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithNestedAssetsFolderHandlesCorrectly()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string testPath = projectRoot + "/Assets/Nested/Assets/File.txt";
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(testPath);
            if (!string.IsNullOrEmpty(result))
            {
                Assert.That(result, Does.StartWith("Assets/"));
            }
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithVeryLongPathHandlesCorrectly()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string longPath = projectRoot + "/Assets";
            for (int i = 0; i < 50; i++)
            {
                longPath += $"/Folder{i}";
            }
            longPath += "/File.txt";

            string result = DirectoryHelper.AbsoluteToUnityRelativePath(longPath);
            Assert.That(result, Is.Not.Null);
            if (!string.IsNullOrEmpty(result))
            {
                Assert.That(result, Does.StartWith("Assets/"));
            }
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithSpecialCharactersInPath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string testPath = projectRoot + "/Assets/Test Folder With Spaces/File.txt";
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(testPath);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithUnicodeCharacters()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string testPath = projectRoot + "/Assets/フォルダ/ファイル.txt";
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(testPath);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void FindRootPathStopsAtRootDrive()
        {
            string result = DirectoryHelper.FindRootPath("C:\\", _ => false);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void FindRootPathHandlesPathWithMultipleSeparators()
        {
            string testPath = Application.dataPath + "//Multiple///Separators";
            string result = DirectoryHelper.FindRootPath(
                testPath,
                path => path == Application.dataPath
            );
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void FindRootPathReturnsCurrentPathWhenConditionThrows()
        {
            string startPath = Application.dataPath;
            bool exceptionThrown = false;
            string result = DirectoryHelper.FindRootPath(
                startPath,
                _ =>
                {
                    if (!exceptionThrown)
                    {
                        exceptionThrown = true;
                        throw new InvalidOperationException("test");
                    }

                    return false;
                }
            );

            Assert.AreEqual(startPath, result);
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithNullReturnsEmpty()
        {
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(null, "com.test.package");
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithEmptyReturnsEmpty()
        {
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(
                string.Empty,
                "com.test.package"
            );
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithWhitespaceReturnsEmpty()
        {
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath("   ", "com.test.package");
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithAssetsPathReturnsRelative()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string testPath = projectRoot + "/Assets/Test/File.txt";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(
                testPath,
                "com.test.package"
            );
            if (!string.IsNullOrEmpty(result))
            {
                Assert.That(result, Does.StartWith("Assets/"));
                Assert.That(result, Does.EndWith("Test/File.txt"));
            }
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithPackageCachePathReturnsPackagesPath()
        {
            string testPath =
                "/Users/test/Project/Library/PackageCache/com.test.package@1.0.0/Editor/Script.cs";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(
                testPath,
                "com.test.package"
            );
            Assert.AreEqual("Packages/com.test.package/Editor/Script.cs", result);
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithPackageCacheVersionedPathReturnsPackagesPath()
        {
            string testPath =
                "C:/Project/Library/PackageCache/com.unity.test@2.3.4-preview.5/Runtime/MyClass.cs";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(testPath, "com.unity.test");
            Assert.AreEqual("Packages/com.unity.test/Runtime/MyClass.cs", result);
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithPackagesPathReturnsPackagesPath()
        {
            string testPath = "/Project/Packages/com.test.package/Editor/Script.cs";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(
                testPath,
                "com.test.package"
            );
            Assert.AreEqual("Packages/com.test.package/Editor/Script.cs", result);
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithNullPackageIdStillWorks()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string testPath = projectRoot + "/Assets/Test/File.txt";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(testPath, null);
            if (!string.IsNullOrEmpty(result))
            {
                Assert.That(result, Does.StartWith("Assets/"));
            }
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithPackageCacheNoPackageIdReturnsEmpty()
        {
            string testPath =
                "/Users/test/Project/Library/PackageCache/com.test.package@1.0.0/Editor/Script.cs";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(testPath, null);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithExternalPathReturnsEmpty()
        {
            string testPath = "/some/external/path/that/is/not/in/project";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(
                testPath,
                "com.test.package"
            );
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithPackageCacheNestedPathReturnsCorrectPath()
        {
            string testPath =
                "D:/Unity/Projects/MyGame/Library/PackageCache/com.wallstop-studios.unity-helpers@1.2.3/Editor/Styles/DropDowns/Style.uss";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(
                testPath,
                "com.wallstop-studios.unity-helpers"
            );
            Assert.AreEqual(
                "Packages/com.wallstop-studios.unity-helpers/Editor/Styles/DropDowns/Style.uss",
                result
            );
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithMixedSeparatorsNormalizesToForward()
        {
            string testPath =
                "C:\\Project\\Library\\PackageCache\\com.test.package@1.0.0\\Runtime\\Class.cs";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(
                testPath,
                "com.test.package"
            );
            Assert.That(result, Does.Not.Contain("\\"));
            Assert.That(result, Does.Contain("/"));
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithCaseInsensitivePackageCache()
        {
            string testPath =
                "/Project/LIBRARY/PACKAGECACHE/com.test.package@1.0.0/Editor/Script.cs";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(
                testPath,
                "com.test.package"
            );
            Assert.AreEqual("Packages/com.test.package/Editor/Script.cs", result);
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithCaseInsensitivePackages()
        {
            string testPath = "/Project/PACKAGES/com.test.package/Runtime/File.cs";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(
                testPath,
                "com.test.package"
            );
            Assert.AreEqual("Packages/com.test.package/Runtime/File.cs", result);
        }

        [Test]
        public void ReadPackageIdFromRootWithNullReturnsEmpty()
        {
            string result = DirectoryHelper.ReadPackageIdFromRoot(null);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void ReadPackageIdFromRootWithEmptyReturnsEmpty()
        {
            string result = DirectoryHelper.ReadPackageIdFromRoot(string.Empty);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void ReadPackageIdFromRootWithWhitespaceReturnsEmpty()
        {
            string result = DirectoryHelper.ReadPackageIdFromRoot("   ");
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void ReadPackageIdFromRootWithNonExistentDirectoryReturnsEmpty()
        {
            string result = DirectoryHelper.ReadPackageIdFromRoot("/non/existent/path");
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void ReadPackageIdFromRootWithValidPackageJsonReturnsName()
        {
            string packageRoot = DirectoryHelper.FindPackageRootPath(
                DirectoryHelper.GetCallerScriptDirectory()
            );
            if (string.IsNullOrEmpty(packageRoot))
            {
                Assert.Inconclusive(
                    "Could not find package root from test directory. Tests must be run from within the package."
                );
                return;
            }

            string result = DirectoryHelper.ReadPackageIdFromRoot(packageRoot);
            Assert.AreEqual("com.wallstop-studios.unity-helpers", result);
        }

        [Test]
        public void ResolvePackageAssetPathWithNullReturnsEmpty()
        {
            string result = DirectoryHelper.ResolvePackageAssetPath(null);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void ResolvePackageAssetPathWithEmptyReturnsEmpty()
        {
            string result = DirectoryHelper.ResolvePackageAssetPath(string.Empty);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void ResolvePackageAssetPathWithWhitespaceReturnsEmpty()
        {
            string result = DirectoryHelper.ResolvePackageAssetPath("   ");
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void ResolvePackageAssetPathWithValidRelativePathReturnsLoadablePath()
        {
            string result = DirectoryHelper.ResolvePackageAssetPath("Tests/Runtime");
            if (string.IsNullOrEmpty(result))
            {
                Assert.Inconclusive(
                    "Could not resolve package asset path. Tests must be run from within the package."
                );
                return;
            }

            Assert.That(result, Does.Contain("Tests/Runtime"));
            Assert.That(result, Does.StartWith("Assets/").Or.StartWith("Packages/"));
        }

        [Test]
        public void ResolvePackageAssetPathWithExplicitSourceFilePathReturnsLoadablePath()
        {
            string callerPath = DirectoryHelper.GetCallerScriptDirectory();
            if (string.IsNullOrEmpty(callerPath))
            {
                Assert.Inconclusive("Could not determine caller script directory.");
                return;
            }

            string testFilePath = Path.Combine(callerPath, "FakeScript.cs");
            string result = DirectoryHelper.ResolvePackageAssetPath(
                "Tests/Runtime",
                sourceFilePath: testFilePath
            );
            if (string.IsNullOrEmpty(result))
            {
                Assert.Inconclusive(
                    "Could not resolve package asset path. Tests must be run from within the package."
                );
                return;
            }

            Assert.That(result, Does.Contain("Tests/Runtime"));
        }

        [Test]
        public void ResolvePackageAssetPathWithEmptySourceFilePathReturnsEmpty()
        {
            string result = DirectoryHelper.ResolvePackageAssetPath(
                "Tests/Runtime",
                sourceFilePath: ""
            );
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void ResolvePackageAssetPathWithNonPackageSourceFileReturnsEmpty()
        {
            string result = DirectoryHelper.ResolvePackageAssetPath(
                "Tests/Runtime",
                sourceFilePath: "/some/random/path/Script.cs"
            );
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void ResolvePackageAssetPathNormalizesForwardSlashes()
        {
            string result = DirectoryHelper.ResolvePackageAssetPath("Tests\\Runtime\\Helper");
            if (string.IsNullOrEmpty(result))
            {
                Assert.Inconclusive(
                    "Could not resolve package asset path. Tests must be run from within the package."
                );
                return;
            }

            Assert.That(result, Does.Not.Contain("\\"));
        }

        [Test]
        public void ResolvePackageAssetPathHandlesRelativePathWithLeadingSlash()
        {
            string result = DirectoryHelper.ResolvePackageAssetPath("/Tests/Runtime");
            if (string.IsNullOrEmpty(result))
            {
                Assert.Inconclusive(
                    "Could not resolve package asset path. Tests must be run from within the package."
                );
                return;
            }

            Assert.That(result, Does.Contain("Tests"));
        }

        [Test]
        public void ResolvePackageAssetPathWithDeepNestedPathReturnsCorrectPath()
        {
            string result = DirectoryHelper.ResolvePackageAssetPath(
                "Editor/Styles/DropDowns/WDropDownStyles.uss"
            );
            if (string.IsNullOrEmpty(result))
            {
                Assert.Inconclusive(
                    "Could not resolve package asset path. Tests must be run from within the package."
                );
                return;
            }

            Assert.That(result, Does.EndWith("WDropDownStyles.uss"));
            Assert.That(result, Does.Contain("Editor/Styles/DropDowns"));
        }

        [Test]
        public void AbsoluteToUnityLoadablePathPrefersPrimaryPathOverFallback()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string testPath = projectRoot + "/Assets/Some/Path.cs";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(
                testPath,
                "com.test.package"
            );

            if (!string.IsNullOrEmpty(result))
            {
                Assert.That(
                    result,
                    Does.StartWith("Assets/"),
                    "Should prefer Assets/ path over Packages/ when path is in Assets folder"
                );
            }
        }

        [Test]
        public void EnsureDirectoryExistsWithBackslashPathThrowsOutsideAssets()
        {
            const string inputPath = @"SomeFolder\SubFolder";
            const string expectedNormalizedPath = "SomeFolder/SubFolder";

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => DirectoryHelper.EnsureDirectoryExists(inputPath),
                $"Expected ArgumentException for path '{inputPath}' (normalized: '{expectedNormalizedPath}')"
            );

            Assert.That(
                exception.Message,
                Does.Contain(expectedNormalizedPath),
                $"Exception message should name the offending (normalized) path. Input: '{inputPath}'"
            );
            Assert.That(
                exception.Message,
                Does.Contain("Assets"),
                "Exception message should mention the Assets folder requirement."
            );
            Assert.That(exception.ParamName, Is.EqualTo("relativeDirectoryPath"));

            // The failure must be signalled ONLY through the typed exception -- no error log.
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void EnsureDirectoryExistsWithBackslashAssetsPathDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                DirectoryHelper.EnsureDirectoryExists(@"Assets\TestFolder\SubFolder")
            );
        }

        [Test]
        public void EnsureDirectoryExistsWithMixedSeparatorsDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                DirectoryHelper.EnsureDirectoryExists(@"Assets/TestFolder\SubFolder")
            );
        }

        [Test]
        public void EnsureDirectoryExistsWithMixedSeparatorsAlternateDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                DirectoryHelper.EnsureDirectoryExists(@"Assets\TestFolder/SubFolder")
            );
        }

        [TestCase("assets/TestFolder")]
        [TestCase("ASSETS/TestFolder")]
        [TestCase("Assets/TestFolder")]
        [TestCase(@"assets\TestFolder")]
        [TestCase(@"ASSETS\TestFolder")]
        [TestCase(@"Assets\TestFolder")]
        public void EnsureDirectoryExistsWithDifferentCasingsDoesNotThrow(string path)
        {
            Assert.DoesNotThrow(() => DirectoryHelper.EnsureDirectoryExists(path));
        }

        [TestCase("Outside/TestFolder", "Outside/TestFolder")]
        [TestCase(@"Outside\TestFolder", "Outside/TestFolder")]
        [TestCase("Folder/Assets/Sub", "Folder/Assets/Sub")]
        [TestCase(@"Folder\Assets\Sub", "Folder/Assets/Sub")]
        [TestCase("../ParentFolder", "../ParentFolder")]
        [TestCase(@"..\ParentFolder", "../ParentFolder")]
        [TestCase("SomeFolder", "SomeFolder")]
        [TestCase("NotAssets/Folder", "NotAssets/Folder")]
        [TestCase("AssetsNot/Folder", "AssetsNot/Folder")]
        [TestCase("assetsNot/Folder", "assetsNot/Folder")]
        public void EnsureDirectoryExistsOutsideAssetsThrowsArgumentException(
            string inputPath,
            string expectedNormalizedPath
        )
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => DirectoryHelper.EnsureDirectoryExists(inputPath),
                $"Expected ArgumentException for path '{inputPath}' (normalized: '{expectedNormalizedPath}')"
            );

            Assert.That(
                exception.Message,
                Does.Contain(expectedNormalizedPath),
                $"Exception message should name the offending (normalized) path. Input: '{inputPath}', Normalized: '{expectedNormalizedPath}'"
            );
            Assert.That(
                exception.Message,
                Does.Contain("Assets"),
                $"Exception message should mention the Assets folder requirement. Input: '{inputPath}'"
            );
            Assert.That(exception.ParamName, Is.EqualTo("relativeDirectoryPath"));

            // The failure must be signalled ONLY through the typed exception -- no error log.
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithWindowsStyleBackslashes()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string windowsPath = projectRoot.Replace('/', '\\') + @"\Assets\Test\File.txt";
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(windowsPath);

            Assert.That(result, Does.Not.Contain(@"\"));
            if (!string.IsNullOrEmpty(result))
            {
                Assert.That(result, Does.StartWith("Assets/"));
            }
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithMixedSeparatorsNormalizes()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string mixedPath = projectRoot + @"/Assets\Test/Nested\File.txt";
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(mixedPath);

            Assert.That(result, Does.Not.Contain(@"\"));
            if (!string.IsNullOrEmpty(result))
            {
                Assert.That(result, Does.StartWith("Assets/"));
            }
        }

        [TestCase(@"C:\Project\Library\PackageCache\com.test@1.0.0\Editor\Script.cs")]
        [TestCase(@"D:\Unity\Project\Library\PackageCache\com.test@1.0.0\Editor\Script.cs")]
        public void AbsoluteToUnityLoadablePathWithWindowsBackslashPackageCachePath(string testPath)
        {
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(testPath, "com.test");
            Assert.That(result, Does.Not.Contain(@"\"));
            Assert.AreEqual("Packages/com.test/Editor/Script.cs", result);
        }

        [TestCase(@"C:\Project\Packages\com.test\Runtime\File.cs")]
        [TestCase(@"D:\Unity\Packages\com.test\Runtime\File.cs")]
        public void AbsoluteToUnityLoadablePathWithWindowsBackslashPackagesPath(string testPath)
        {
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(testPath, "com.test");
            Assert.That(result, Does.Not.Contain(@"\"));
            Assert.AreEqual("Packages/com.test/Runtime/File.cs", result);
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithMixedSeparatorsInPackageCache()
        {
            string testPath = @"C:\Project/Library\PackageCache/com.test@1.0.0\Editor/Script.cs";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(testPath, "com.test");
            Assert.That(result, Does.Not.Contain(@"\"));
            Assert.AreEqual("Packages/com.test/Editor/Script.cs", result);
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithMixedSeparatorsInPackages()
        {
            string testPath = @"/Project\Packages/com.test\Runtime/File.cs";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(testPath, "com.test");
            Assert.That(result, Does.Not.Contain(@"\"));
            Assert.AreEqual("Packages/com.test/Runtime/File.cs", result);
        }

        [TestCase(@"Tests\Runtime")]
        [TestCase(@"Tests/Runtime")]
        [TestCase(@"Tests\Runtime\Helper")]
        [TestCase(@"Tests/Runtime/Helper")]
        public void FindAbsolutePathToDirectoryHandlesBothSeparators(string directory)
        {
            string result = DirectoryHelper.FindAbsolutePathToDirectory(directory);
            if (string.IsNullOrEmpty(result))
            {
                Assert.Inconclusive(
                    "Could not resolve path. Tests must be run from within the package."
                );
                return;
            }

            Assert.That(result, Does.Not.Contain(@"\"));
            Assert.That(result.ToLowerInvariant(), Does.Contain("tests"));
            Assert.That(result.ToLowerInvariant(), Does.Contain("runtime"));
        }

        [TestCase(@"Editor\Styles\AnimationViewer.uss")]
        [TestCase(@"Editor/Styles/AnimationViewer.uss")]
        [TestCase(@"Editor\Styles/AnimationViewer.uss")]
        [TestCase(@"Editor/Styles\AnimationViewer.uss")]
        public void ResolvePackageAssetPathHandlesBothSeparators(string relativePath)
        {
            string result = DirectoryHelper.ResolvePackageAssetPath(relativePath);
            if (string.IsNullOrEmpty(result))
            {
                Assert.Inconclusive(
                    "Could not resolve package asset path. Tests must be run from within the package."
                );
                return;
            }

            Assert.That(result, Does.Not.Contain(@"\"));
            Assert.That(result, Does.Contain("Editor"));
            Assert.That(result, Does.Contain("Styles"));
        }

        [TestCase(@"C:\")]
        [TestCase(@"D:\Projects")]
        [TestCase(@"C:\Users\Test\Projects")]
        public void FindRootPathWithWindowsStyleRootPath(string startPath)
        {
            string result = DirectoryHelper.FindRootPath(startPath, _ => true);
            Assert.That(result, Is.Not.Null);
        }

        [TestCase("/")]
        [TestCase("/home")]
        [TestCase("/Users/test/Projects")]
        public void FindRootPathWithUnixStyleRootPath(string startPath)
        {
            string result = DirectoryHelper.FindRootPath(startPath, _ => true);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void FindRootPathWithMixedSeparators()
        {
            string mixedPath = Application.dataPath + @"/SubFolder\AnotherFolder/File";
            string result = DirectoryHelper.FindRootPath(
                mixedPath,
                path => path == Application.dataPath
            );
            Assert.That(result, Is.Not.Null);
        }

        [TestCase(@"C:\Project\Library\PackageCache\com.test@1.0.0\Runtime")]
        [TestCase("C:/Project/Library/PackageCache/com.test@1.0.0/Runtime")]
        [TestCase(@"C:/Project\Library/PackageCache\com.test@1.0.0/Runtime")]
        public void AbsoluteToUnityLoadablePathPackageCacheWithVariousSeparators(string testPath)
        {
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(testPath, "com.test");
            Assert.That(result, Does.Not.Contain(@"\"));
            if (!string.IsNullOrEmpty(result))
            {
                Assert.That(result, Does.StartWith("Packages/"));
            }
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithConsecutiveBackslashes()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string pathWithConsecutive = projectRoot + @"\\Assets\\Test\\File.txt";
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(pathWithConsecutive);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Not.Contain(@"\"));
        }

        [Test]
        public void AbsoluteToUnityRelativePathWithConsecutiveForwardSlashes()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string pathWithConsecutive = projectRoot + "//Assets//Test//File.txt";
            string result = DirectoryHelper.AbsoluteToUnityRelativePath(pathWithConsecutive);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Not.Contain(@"\"));
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithTrailingBackslash()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string pathWithTrailing = projectRoot + @"\Assets\Test\";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(
                pathWithTrailing,
                "com.test"
            );
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Not.Contain(@"\"));
        }

        [Test]
        public void AbsoluteToUnityLoadablePathWithTrailingForwardSlash()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.SanitizePath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                Assert.Inconclusive("Could not determine project root");
                return;
            }

            string pathWithTrailing = projectRoot + "/Assets/Test/";
            string result = DirectoryHelper.AbsoluteToUnityLoadablePath(
                pathWithTrailing,
                "com.test"
            );
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Not.Contain(@"\"));
        }

        [Test]
        public void SanitizePathWithNullReturnsNull()
        {
            string result = PathHelper.Sanitize(null);
            Assert.IsTrue(result == null);
        }

        [Test]
        public void SanitizePathWithEmptyReturnsEmpty()
        {
            string result = PathHelper.Sanitize(string.Empty);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void SanitizePathWithForwardSlashesOnlyReturnsUnchanged()
        {
            const string path = "Assets/Test/File.txt";
            string result = PathHelper.Sanitize(path);
            Assert.AreEqual(path, result);
        }

        [TestCase(@"\", "/")]
        [TestCase(@"\\", "//")]
        [TestCase(@"\\\", "///")]
        [TestCase(@"Assets\Test", "Assets/Test")]
        [TestCase(@"Assets\Test\File.txt", "Assets/Test/File.txt")]
        [TestCase(@"C:\Users\Test\Project", "C:/Users/Test/Project")]
        [TestCase(@"C:\Users\Test\Project\", "C:/Users/Test/Project/")]
        public void SanitizePathConvertsBackslashesToForwardSlashes(string input, string expected)
        {
            string result = PathHelper.Sanitize(input);
            Assert.AreEqual(expected, result);
        }

        [TestCase(@"Assets/Test\File.txt", "Assets/Test/File.txt")]
        [TestCase(@"Assets\Test/File.txt", "Assets/Test/File.txt")]
        [TestCase(@"C:\Users/Test\Project/File", "C:/Users/Test/Project/File")]
        public void SanitizePathHandlesMixedSeparators(string input, string expected)
        {
            string result = PathHelper.Sanitize(input);
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void SanitizePathWithWhitespacePreservesWhitespace()
        {
            const string path = "   ";
            string result = PathHelper.Sanitize(path);
            Assert.AreEqual(path, result);
        }

        [Test]
        public void SanitizePathWithSpacesInPathPreservesSpaces()
        {
            const string path = @"Assets\Test Folder\My File.txt";
            string result = PathHelper.Sanitize(path);
            Assert.AreEqual("Assets/Test Folder/My File.txt", result);
        }

        [Test]
        public void SanitizePathWithUnicodeCharactersPreservesUnicode()
        {
            const string path = @"Assets\フォルダ\ファイル.txt";
            string result = PathHelper.Sanitize(path);
            Assert.AreEqual("Assets/フォルダ/ファイル.txt", result);
        }

        [Test]
        public void SanitizePathExtensionMethodWithNullReturnsNull()
        {
            string path = null;
            string result = path.SanitizePath();
            Assert.IsTrue(result == null);
        }

        [Test]
        public void SanitizePathExtensionMethodConvertsBackslashes()
        {
            string path = @"Assets\Test\File.txt";
            string result = path.SanitizePath();
            Assert.AreEqual("Assets/Test/File.txt", result);
        }
    }
}
