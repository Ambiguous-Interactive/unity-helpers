// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor.TestTools.TestRunner.Api;
    using WallstopStudios.UnityHelpers.Editor.Tools;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using PackageInfo = UnityEditor.PackageManager.PackageInfo;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class TestRunReporterTests : CommonTestBase
    {
        private static readonly DateTime StartedUtc = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

        private string _playModePath;
        private string _savedContent;
        private bool _hadSavedContent;

        [TestCase(false, false, "current")]
        [TestCase(true, false, "stale")]
        [TestCase(false, true, "unknown")]
        [TestCase(true, true, "stale")]
        public void FreshnessWalksTransitiveReferencesAndReportsMissingInputs(
            bool stale,
            bool missing,
            string expected
        )
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                $"assembly-freshness-{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(directory);
            try
            {
                string testDll = Path.Combine(directory, "Tests.dll");
                string dependencyDll = Path.Combine(directory, "Runtime.dll");
                string source = Path.Combine(directory, "Runtime.cs");
                File.WriteAllText(testDll, "compiled tests");
                File.WriteAllText(dependencyDll, "compiled dependency");
                File.WriteAllText(source, "source");
                File.SetLastWriteTimeUtc(testDll, StartedUtc);
                File.SetLastWriteTimeUtc(dependencyDll, StartedUtc);
                File.SetLastWriteTimeUtc(source, StartedUtc.AddMinutes(stale ? 1 : -1));
                TestRunResultNode root = new();
                TestRunResultNode assembly = new() { fullName = "Tests.dll" };
                root.children.Add(assembly);
                TestRunAssemblyInput[] inputs =
                {
                    new("Tests", testDll, Array.Empty<string>(), new[] { "Middle" }),
                    new("Middle", dependencyDll, Array.Empty<string>(), new[] { "Runtime" }),
                    new(
                        "Runtime",
                        dependencyDll,
                        missing ? new[] { source, source + ".missing" } : new[] { source },
                        new[] { "Tests" }
                    ),
                };
                TestRunAssemblyFreshness.Populate(root, inputs);
                Assert.AreEqual(stale ? 1 : 0, assembly.staleSources.Count);
                Assert.AreEqual(missing ? 1 : 0, assembly.freshnessErrors.Count);
                Assert.AreEqual(StartedUtc, assembly.assemblyBuiltUtc);
                string report = TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    StartedUtc.AddSeconds(1),
                    root
                );
                StringAssert.Contains($"freshness={expected}", report);
                if (stale)
                {
                    StringAssert.Contains("Runtime:", assembly.staleSources[0]);
                    StringAssert.Contains(source, assembly.staleSources[0]);
                    StringAssert.Contains("staleSource=", report);
                }
                if (missing)
                {
                    StringAssert.Contains("freshnessError=", report);
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestCase("Product.Tests", "Product.Tests")]
        [TestCase("Product.Tests.dll", "Product.Tests")]
        [TestCase("Product.Tests.DLL", "Product.Tests")]
        public void FreshnessKeepsAssemblyNameDotsAndOnlyRemovesDllSuffix(
            string name,
            string inputName
        )
        {
            string path = Path.GetTempFileName();
            try
            {
                TestRunResultNode root = new();
                TestRunResultNode assembly = new() { fullName = name };
                root.children.Add(assembly);
                TestRunAssemblyFreshness.Populate(
                    root,
                    new[]
                    {
                        new TestRunAssemblyInput(
                            inputName,
                            path,
                            new[] { path },
                            Array.Empty<string>()
                        ),
                    }
                );
                Assert.IsTrue(assembly.assemblyBuiltUtc.HasValue);
                CollectionAssert.IsEmpty(assembly.freshnessErrors);
                CollectionAssert.IsEmpty(assembly.staleSources);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ABuildTimestampWithoutInspectedSourcesIsUnknown()
        {
            string path = Path.GetTempFileName();
            try
            {
                TestRunResultNode root = new();
                TestRunResultNode assembly = new()
                {
                    fullName = "Empty.dll",
                    assemblyBuiltUtc = StartedUtc,
                };
                root.children.Add(assembly);
                StringAssert.Contains(
                    "freshness=unknown",
                    TestRunSummaryFormatter.FormatSummary(
                        TestMode.EditMode,
                        StartedUtc,
                        StartedUtc,
                        root
                    )
                );
                TestRunAssemblyFreshness.Populate(
                    root,
                    new[]
                    {
                        new TestRunAssemblyInput(
                            "Empty",
                            path,
                            Array.Empty<string>(),
                            Array.Empty<string>()
                        ),
                    }
                );
                Assert.IsFalse(assembly.sourceFilesInspected);
                Assert.AreEqual(1, assembly.freshnessErrors.Count);
                StringAssert.Contains("No readable source files", assembly.freshnessErrors[0]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void FreshnessCannotCallAnUnknownOrMissingAssemblyCurrent()
        {
            TestRunResultNode root = new();
            TestRunResultNode unknown = new() { fullName = "Unknown.dll" };
            TestRunResultNode missing = new() { fullName = "Missing.dll" };
            root.children.Add(unknown);
            root.children.Add(missing);
            TestRunAssemblyFreshness.Populate(
                root,
                new[]
                {
                    new TestRunAssemblyInput(
                        "Missing",
                        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                        Array.Empty<string>(),
                        Array.Empty<string>()
                    ),
                }
            );
            Assert.IsTrue(unknown.assemblyBuiltUtc == null);
            Assert.IsTrue(missing.assemblyBuiltUtc == null);
            Assert.AreEqual(1, unknown.freshnessErrors.Count);
            Assert.AreEqual(1, missing.freshnessErrors.Count);
        }

        [Test]
        public void CompilationPathsResolveProjectAndRegistrySources()
        {
            Dictionary<string, string> packages = new(StringComparer.Ordinal);
            const string RelativeOutput = "Library/ScriptAssemblies/Example.dll";
            Assert.AreEqual(
                AuthoredAssetPaths.ToFileSystemPath(RelativeOutput),
                TestRunCompilationPaths.Resolve(RelativeOutput, packages)
            );
            PackageInfo package = PackageInfo.FindForAssembly(typeof(TestRunnerApi).Assembly);
            Assert.IsTrue(package != null);
            string source = package.assetPath + "/package.json";
            string resolved = TestRunCompilationPaths.Resolve(source, packages);
            Assert.AreEqual(Path.Combine(package.resolvedPath, "package.json"), resolved);
            Assert.IsTrue(File.Exists(resolved));
            Assert.AreEqual(resolved, TestRunCompilationPaths.Resolve(source, packages));
            Assert.AreEqual(1, packages.Count);
            Assert.AreEqual(resolved, TestRunCompilationPaths.Resolve(resolved, packages));
        }

        [Test]
        public void ReporterChecksTheRealCompilationReferenceClosure()
        {
            TestRunResultNode root = new();
            TestRunResultNode assembly = new()
            {
                fullName = typeof(TestRunReporterTests).Assembly.GetName().Name + ".dll",
            };
            root.children.Add(assembly);
            TestRunReporter.PopulateAssemblyBuildTimes(root);
            Assert.IsTrue(assembly.assemblyBuiltUtc.HasValue);
            Assert.IsTrue(assembly.sourceFilesInspected);
            CollectionAssert.IsEmpty(assembly.freshnessErrors);
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            TestRunReporterTestAccess.ClearRunSession();
            _hadSavedContent = false;
            _savedContent = string.Empty;
            if (!TestRunSummaryFile.TryGetSummaryPath(TestMode.PlayMode, out _playModePath))
            {
                _playModePath = string.Empty;
                return;
            }

            if (!File.Exists(_playModePath))
            {
                return;
            }

            _savedContent = File.ReadAllText(_playModePath);
            _hadSavedContent = true;
        }

        [TearDown]
        public override void TearDown()
        {
            TestRunReporterTestAccess.ClearRunSession();
            if (!string.IsNullOrEmpty(_playModePath))
            {
                if (_hadSavedContent)
                {
                    File.WriteAllText(_playModePath, _savedContent);
                }
                else if (File.Exists(_playModePath))
                {
                    File.Delete(_playModePath);
                }
            }

            base.TearDown();
        }

        /*
            The summary file survives domain reload and determines whether runner callbacks must be registered
            again.
        */
        [Test]
        public void InFlightQueryFollowsTheSummaryFileOnDisk()
        {
            Assert.IsFalse(
                string.IsNullOrEmpty(_playModePath),
                "PlayMode must resolve to a summary path."
            );
            Assert.IsFalse(
                TestRunReporter.IsAnyRunInFlight(),
                "No run can be in flight while EditMode tests are executing."
            );

            Assert.IsTrue(
                TestRunSummaryFile.TryBeginRun(
                    _playModePath,
                    TestMode.PlayMode,
                    StartedUtc,
                    out string owner
                )
            );
            Assert.IsTrue(
                TestRunReporter.IsAnyRunInFlight(),
                "A summary file holding the running marker is what survives the domain reload."
            );

            Assert.IsTrue(TestRunSummaryFile.TryDiscardRun(_playModePath, owner));
            Assert.IsFalse(TestRunReporter.IsAnyRunInFlight());
        }

        /// <summary>
        ///     Pins that registration is driven by the summary file rather than by a tick.
        /// </summary>
        /// <remarks>
        /// Registration moved out of <c>EditorApplication.delayCall</c> and into the
        /// <c>[InitializeOnLoadMethod]</c> body itself: the Test Runner can broadcast
        /// <c>RunFinished</c> while the domain is still loading, and `WProtoSubtypeTagAutoAssign`
        /// measured that an editor nobody is interacting with may never pump <c>delayCall</c> at
        /// all -- naming "a CI editor driven over a socket", which is what this type serves.
        /// Only the refusal is asserted here. The accepting branch registers real Test Runner
        /// callbacks, and this fixture runs inside a Test Runner run: the reporter would observe
        /// the suite that is executing it and write a summary for a run it never claimed. That
        /// branch belongs to a real PlayMode run, and is stated as uncovered rather than faked.
        /// </remarks>
        [Test]
        public void RegistrationIsRefusedWhileNoRunHoldsASummaryFile()
        {
            Assert.IsFalse(
                TestRunReporter.IsAnyRunInFlight(),
                "No run can be in flight while EditMode tests are executing."
            );

            Assert.IsFalse(
                TestRunReporter.TryRegisterForRunInFlight(),
                "Registering with no run in flight would leave callbacks listening for a run that "
                    + "nothing here claimed."
            );

            Assert.IsTrue(
                TestRunSummaryFile.TryBeginRun(
                    _playModePath,
                    TestMode.PlayMode,
                    StartedUtc,
                    out string staleOwner
                )
            );
            Assert.IsFalse(
                TestRunReporter.TryRegisterForRunInFlight(),
                "A marker without matching editor-session ownership belongs to a lost run."
            );
            Assert.IsTrue(TestRunSummaryFile.TryDiscardRun(_playModePath, staleOwner));
        }

        [Test]
        public void StartRunIsRefusedWhileAnotherModeHoldsItsSummaryFile()
        {
            Assert.IsTrue(
                TestRunSummaryFile.TryBeginRun(_playModePath, TestMode.PlayMode, StartedUtc, out _)
            );

            Assert.IsFalse(
                TestRunReporter.StartRun(TestMode.EditMode),
                "Two concurrent runs writing one summary is the failure this refusal prevents."
            );

            Assert.IsTrue(
                TestRunSummaryFile.TryGetSummaryPath(TestMode.EditMode, out string editModePath)
            );
            Assert.IsFalse(
                TestRunSummaryFile.IsMarkedRunning(editModePath),
                "A refused run must not claim the other mode's summary file."
            );
        }

        [Test]
        public void StartRunIsRefusedForAModeWithNoSummaryFile()
        {
            Assert.IsFalse(TestRunReporter.StartRun(TestMode.EditMode | TestMode.PlayMode));
            Assert.IsFalse(TestRunReporter.StartRun((TestMode)0));
        }
    }
}
