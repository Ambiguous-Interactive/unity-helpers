// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor.TestTools.TestRunner.Api;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Tools;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class TestRunSummaryFileTests : CommonTestBase
    {
        private static readonly DateTime StartedUtc = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime FinishedUtc = new(2026, 9, 2, 10, 1, 30, DateTimeKind.Utc);

        private string _workingDirectory;
        private string _summaryPath;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _workingDirectory = Path.Combine(
                Application.temporaryCachePath,
                "test-run-summary-file-tests",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_workingDirectory);
            _summaryPath = Path.Combine(_workingDirectory, "summary.txt");
        }

        [TearDown]
        public override void TearDown()
        {
            if (Directory.Exists(_workingDirectory))
            {
                Directory.Delete(_workingDirectory, true);
            }

            base.TearDown();
        }

        [Test]
        public void EditModeAndPlayModeResolveToDifferentPathsUnderTemp()
        {
            Assert.IsTrue(
                TestRunSummaryFile.TryGetSummaryPath(TestMode.EditMode, out string editModePath)
            );
            Assert.IsTrue(
                TestRunSummaryFile.TryGetSummaryPath(TestMode.PlayMode, out string playModePath)
            );

            Assert.AreNotEqual(editModePath, playModePath);
            Assert.AreEqual(
                TestRunSummaryFile.EditModeFileName,
                Path.GetFileName(editModePath),
                "EditMode must not share a path with PlayMode."
            );
            Assert.AreEqual(TestRunSummaryFile.PlayModeFileName, Path.GetFileName(playModePath));
            Assert.AreEqual(
                TestRunSummaryFile.SummaryDirectoryName,
                Path.GetFileName(Path.GetDirectoryName(editModePath))
            );
            Assert.AreEqual(
                TestRunSummaryFile.SummaryDirectoryName,
                Path.GetFileName(Path.GetDirectoryName(playModePath))
            );
        }

        [Test]
        public void SummaryPathsSitAtTheProjectRoot()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            Assert.IsTrue(TestRunSummaryFile.TryGetSummaryPath(TestMode.EditMode, out string path));

            Assert.AreEqual(
                Path.GetFullPath(projectRoot),
                Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), ".."))
            );
        }

        [Test]
        [TestCase(TestMode.EditMode | TestMode.PlayMode, TestName = "Mode.Both")]
        [TestCase((TestMode)0, TestName = "Mode.None")]
        [TestCase((TestMode)64, TestName = "Mode.Unknown")]
        public void SummaryPathIsRefusedForAnythingButASingleKnownMode(TestMode mode)
        {
            Assert.IsFalse(TestRunSummaryFile.TryGetSummaryPath(mode, out string path));
            Assert.AreEqual(string.Empty, path);
        }

        [Test]
        public void BeginRunWritesTheRunningMarkerBeforeAnythingElse()
        {
            Assert.IsTrue(
                TestRunSummaryFile.TryBeginRun(_summaryPath, TestMode.EditMode, StartedUtc)
            );

            Assert.IsTrue(File.Exists(_summaryPath));
            Assert.AreEqual(
                "SUMMARY running started=2026-09-02T10:00:00.000Z mode=EditMode",
                File.ReadAllLines(_summaryPath)[0]
            );
            Assert.IsTrue(TestRunSummaryFile.IsMarkedRunning(_summaryPath));
        }

        [Test]
        public void BeginRunRefusesWhileARunAlreadyHoldsTheFile()
        {
            Assert.IsTrue(
                TestRunSummaryFile.TryBeginRun(_summaryPath, TestMode.EditMode, StartedUtc)
            );

            Assert.IsFalse(
                TestRunSummaryFile.TryBeginRun(
                    _summaryPath,
                    TestMode.EditMode,
                    StartedUtc.AddMinutes(5)
                ),
                "A second run must not be allowed to write over a summary still in flight."
            );

            Assert.AreEqual(
                "SUMMARY running started=2026-09-02T10:00:00.000Z mode=EditMode",
                File.ReadAllLines(_summaryPath)[0],
                "The refused run must leave the first run's marker untouched."
            );
        }

        [Test]
        public void BeginRunSucceedsAgainOnceTheRunHasFinished()
        {
            Assert.IsTrue(
                TestRunSummaryFile.TryBeginRun(_summaryPath, TestMode.EditMode, StartedUtc)
            );
            Assert.IsTrue(
                TestRunSummaryFile.TryFinishRun(
                    _summaryPath,
                    TestMode.EditMode,
                    FinishedUtc,
                    new TestRunResultNode()
                )
            );

            Assert.IsFalse(TestRunSummaryFile.IsMarkedRunning(_summaryPath));
            Assert.IsTrue(
                TestRunSummaryFile.TryBeginRun(
                    _summaryPath,
                    TestMode.EditMode,
                    FinishedUtc.AddMinutes(1)
                )
            );
        }

        [Test]
        public void BeginRunCreatesTheSummaryDirectory()
        {
            string nestedPath = Path.Combine(_workingDirectory, "made", "up", "summary.txt");

            Assert.IsTrue(
                TestRunSummaryFile.TryBeginRun(nestedPath, TestMode.PlayMode, StartedUtc)
            );

            Assert.IsTrue(File.Exists(nestedPath));
        }

        [Test]
        public void FinishRunReplacesTheMarkerWithTheFullSummary()
        {
            TestRunSummaryFile.TryBeginRun(_summaryPath, TestMode.EditMode, StartedUtc);

            TestRunResultNode root = new();
            TestRunResultNode assembly = new() { fullName = "Some.Tests.dll" };
            root.children.Add(assembly);
            assembly.children.Add(
                new TestRunResultNode { fullName = "Some.Passing", status = TestStatus.Passed }
            );
            assembly.children.Add(
                new TestRunResultNode
                {
                    fullName = "Some.Failing",
                    status = TestStatus.Failed,
                    message = "boom",
                    stackTrace = "at Some.Failing () [0x0] in /repo/Some.cs:3",
                }
            );

            Assert.IsTrue(
                TestRunSummaryFile.TryFinishRun(_summaryPath, TestMode.EditMode, FinishedUtc, root)
            );

            string[] lines = File.ReadAllLines(_summaryPath);
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual(
                "SUMMARY pass=1 fail=1 skip=0 inconclusive=0 seconds=90.000 mode=EditMode started=2026-09-02T10:00:00.000Z finished=2026-09-02T10:01:30.000Z",
                lines[0],
                "The elapsed seconds must come from the marker's start time."
            );
            StringAssert.StartsWith("ASSEMBLY name=Some.Tests.dll", lines[1]);
            Assert.AreEqual(
                "FAILURE assembly=Some.Tests.dll name=Some.Failing location=/repo/Some.cs:3 message=boom",
                lines[2]
            );
            Assert.IsFalse(TestRunSummaryFile.IsMarkedRunning(_summaryPath));
        }

        [Test]
        public void FinishRunWithoutAMarkerReportsNoElapsedTime()
        {
            Assert.IsTrue(
                TestRunSummaryFile.TryFinishRun(
                    _summaryPath,
                    TestMode.PlayMode,
                    FinishedUtc,
                    new TestRunResultNode()
                )
            );

            StringAssert.Contains("seconds=0.000", File.ReadAllLines(_summaryPath)[0]);
        }

        [Test]
        public void ReadStartedUtcRecoversTheMarkerTimestamp()
        {
            TestRunSummaryFile.TryBeginRun(_summaryPath, TestMode.PlayMode, StartedUtc);

            Assert.IsTrue(TestRunSummaryFile.TryReadStartedUtc(_summaryPath, out DateTime started));
            Assert.AreEqual(StartedUtc, started);
        }

        [Test]
        public void DiscardRunReleasesTheFileForTheNextRun()
        {
            TestRunSummaryFile.TryBeginRun(_summaryPath, TestMode.EditMode, StartedUtc);

            Assert.IsTrue(TestRunSummaryFile.TryDiscardRun(_summaryPath));

            Assert.IsFalse(File.Exists(_summaryPath));
            Assert.IsFalse(TestRunSummaryFile.IsMarkedRunning(_summaryPath));
            Assert.IsTrue(
                TestRunSummaryFile.TryBeginRun(_summaryPath, TestMode.EditMode, FinishedUtc)
            );
        }

        [Test]
        public void DiscardRunOnAMissingFileIsNotAFailure()
        {
            Assert.IsTrue(TestRunSummaryFile.TryDiscardRun(_summaryPath));
        }

        [Test]
        public void IsMarkedRunningIsFalseForAMissingFile()
        {
            Assert.IsFalse(TestRunSummaryFile.IsMarkedRunning(_summaryPath));
        }

        [Test]
        [TestCase("", TestName = "Content.Empty")]
        [TestCase("\n", TestName = "Content.BlankLine")]
        [TestCase("garbage", TestName = "Content.Garbage")]
        [TestCase("SUMMARY", TestName = "Content.PrefixOnly")]
        [TestCase(
            "SUMMARY pass=0 fail=0\nSUMMARY running started=x",
            TestName = "Content.RunningOnALaterLine"
        )]
        public void IsMarkedRunningOnlyTrustsTheFirstLine(string content)
        {
            File.WriteAllText(_summaryPath, content);

            Assert.IsFalse(TestRunSummaryFile.IsMarkedRunning(_summaryPath));
        }

        [Test]
        [TestCase(null, TestName = "Path.Null")]
        [TestCase("", TestName = "Path.Empty")]
        public void EveryEntryPointRefusesAnAbsentPath(string path)
        {
            Assert.IsFalse(TestRunSummaryFile.IsMarkedRunning(path));
            Assert.IsFalse(TestRunSummaryFile.TryBeginRun(path, TestMode.EditMode, StartedUtc));
            Assert.IsFalse(
                TestRunSummaryFile.TryFinishRun(
                    path,
                    TestMode.EditMode,
                    FinishedUtc,
                    new TestRunResultNode()
                )
            );
            Assert.IsFalse(TestRunSummaryFile.TryReadStartedUtc(path, out _));
            Assert.IsFalse(TestRunSummaryFile.TryDiscardRun(path));
        }

        [Test]
        public void ReadStartedUtcIsFalseForAFileWithoutAParseableTimestamp()
        {
            File.WriteAllText(_summaryPath, "SUMMARY running started=nonsense mode=EditMode\n");

            Assert.IsFalse(TestRunSummaryFile.TryReadStartedUtc(_summaryPath, out _));
        }
    }
}
