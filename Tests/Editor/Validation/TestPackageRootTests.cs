// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.IO;
    using NUnit.Framework;

    [TestFixture]
    public sealed class TestPackageRootTests
    {
        private string _temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            _temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityHelpersRoot",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_temporaryRoot);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_temporaryRoot, true);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("{}")]
        [TestCase("not json")]
        [TestCase("{\"name\":42}")]
        [TestCase("{\"name\":\"other\",\"description\":\"com.wallstop-studios.unity-helpers\"}")]
        public void RejectsMissingMalformedAndWrongManifest(string manifest)
        {
            Directory.CreateDirectory(Path.Combine(_temporaryRoot, "Tests"));
            if (manifest != null)
            {
                File.WriteAllText(Path.Combine(_temporaryRoot, "package.json"), manifest);
            }

            Assert.IsFalse(TestPackageRoot.IsPackageRoot(_temporaryRoot));
        }

        [Test]
        public void RequiresTestsDirectory()
        {
            WriteManifest(_temporaryRoot);
            Assert.IsFalse(TestPackageRoot.IsPackageRoot(_temporaryRoot));
        }

        [TestCase("node_modules")]
        [TestCase("NODE_MODULES")]
        public void RejectsDependencyEvenWhenManifestClaimsPackageIdentity(string dependencyFolder)
        {
            string root = Path.Combine(_temporaryRoot, dependencyFolder, "dependency");
            Directory.CreateDirectory(Path.Combine(root, "Tests"));
            WriteManifest(root);

            Assert.IsFalse(TestPackageRoot.IsPackageRoot(root.Replace('\\', '/')));
            Assert.IsFalse(TestPackageRoot.IsPackageRoot(root.Replace('/', '\\')));
        }

        [Test]
        public void DependencyManifestFirstDoesNotHideActualRoot()
        {
            Directory.CreateDirectory(Path.Combine(_temporaryRoot, "Tests"));
            WriteManifest(_temporaryRoot);
            string dependency = Path.Combine(_temporaryRoot, "node_modules", "dependency");
            Directory.CreateDirectory(Path.Combine(dependency, "Tests"));
            WriteManifest(dependency);

            string[] candidates = { dependency, _temporaryRoot };
            string selected = Array.Find(candidates, TestPackageRoot.IsPackageRoot);

            Assert.AreEqual(_temporaryRoot, selected);
        }

        [Test]
        public void ResolvedRootContainsThisFixtureAndPackageIdentity()
        {
            string root = TestPackageRoot.Resolve(typeof(TestPackageRootTests).Assembly);
            Assert.IsFalse(string.IsNullOrWhiteSpace(root));
            Assert.IsTrue(TestPackageRoot.IsPackageRoot(root));
            Assert.IsTrue(
                File.Exists(
                    Path.Combine(
                        root,
                        "Tests",
                        "Editor",
                        "Validation",
                        nameof(TestPackageRootTests) + ".cs"
                    )
                )
            );
        }

        [Test]
        public void SourceFallbackRejectsNestedDependencyAndFindsOwningRoot()
        {
            Directory.CreateDirectory(Path.Combine(_temporaryRoot, "Tests"));
            WriteManifest(_temporaryRoot);
            string dependency = Path.Combine(_temporaryRoot, "node_modules", "dependency");
            Directory.CreateDirectory(Path.Combine(dependency, "Tests"));
            WriteManifest(dependency);
            string sourcePath = Path.Combine(dependency, "Tests", "Source.cs");

            Assert.AreEqual(_temporaryRoot, TestPackageRoot.Resolve(null, sourcePath));
        }

        [Test]
        public void SourceFallbackCannotSubstituteWrongManifest()
        {
            Directory.CreateDirectory(Path.Combine(_temporaryRoot, "Tests"));
            File.WriteAllText(Path.Combine(_temporaryRoot, "package.json"), "{\"name\":\"other\"}");
            string sourcePath = Path.Combine(_temporaryRoot, "Tests", "Source.cs");

            Assert.IsTrue(TestPackageRoot.Resolve(null, sourcePath) == null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SourceScanRejectsEmptyAndUnrelatedTrees(bool containsUnrelatedSource)
        {
            string tests = Path.Combine(_temporaryRoot, "Tests");
            Directory.CreateDirectory(tests);
            if (containsUnrelatedSource)
            {
                File.WriteAllText(Path.Combine(tests, "Other.cs"), "class Other { }");
            }

            Assert.Throws<AssertionException>(() =>
                TestNamingConventionTests.ScanTestFiles(_temporaryRoot, (_, _, _) => { })
            );
        }

        [Test]
        public void SourceScanDoesNotHideUnreadableFixture()
        {
            string tests = Path.Combine(_temporaryRoot, "Tests");
            Directory.CreateDirectory(tests);
            string source = Path.Combine(tests, nameof(TestNamingConventionTests) + ".cs");
            File.WriteAllText(source, "class Source { }");
            using FileStream locked = new(
                source,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );

            Assert.Throws<IOException>(() =>
                TestNamingConventionTests.ScanTestFiles(_temporaryRoot, (_, _, _) => { })
            );
        }

        private void WriteManifest(string root)
        {
            File.WriteAllText(
                Path.Combine(root, "package.json"),
                "{\"name\":\"" + TestPackageRoot.PackageName + "\"}"
            );
        }
    }
}
