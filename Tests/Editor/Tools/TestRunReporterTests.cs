// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor.TestTools.TestRunner.Api;
    using WallstopStudios.UnityHelpers.Editor.Tools;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class TestRunReporterTests : CommonTestBase
    {
        private static readonly DateTime StartedUtc = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);

        private string _playModePath;
        private string _savedContent;
        private bool _hadSavedContent;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
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
            The reporter carries nothing across a domain reload in memory: the summary file is the
            state, and this is the predicate `[InitializeOnLoadMethod]` re-registers the Test Runner
            callbacks from after a PlayMode run reloads the domain.
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
                TestRunSummaryFile.TryBeginRun(_playModePath, TestMode.PlayMode, StartedUtc)
            );
            Assert.IsTrue(
                TestRunReporter.IsAnyRunInFlight(),
                "A summary file holding the running marker is what survives the domain reload."
            );

            Assert.IsTrue(TestRunSummaryFile.TryDiscardRun(_playModePath));
            Assert.IsFalse(TestRunReporter.IsAnyRunInFlight());
        }

        [Test]
        public void StartRunIsRefusedWhileAnotherModeHoldsItsSummaryFile()
        {
            Assert.IsTrue(
                TestRunSummaryFile.TryBeginRun(_playModePath, TestMode.PlayMode, StartedUtc)
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
