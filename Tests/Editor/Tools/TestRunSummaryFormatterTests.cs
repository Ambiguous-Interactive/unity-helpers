// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using NUnit.Framework;
    using UnityEditor.TestTools.TestRunner.Api;
    using WallstopStudios.UnityHelpers.Editor.Tools;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class TestRunSummaryFormatterTests : CommonTestBase
    {
        private static readonly DateTime StartedUtc = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime FinishedUtc = new(2026, 9, 2, 10, 1, 30, DateTimeKind.Utc);

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
        }

        [Test]
        [TestCase(TestMode.EditMode, TestName = "Mode.EditMode")]
        [TestCase(TestMode.PlayMode, TestName = "Mode.PlayMode")]
        public void RunningMarkerNamesTheModeAndTheStartTime(TestMode mode)
        {
            string marker = TestRunSummaryFormatter.FormatRunningMarker(mode, StartedUtc);

            Assert.IsTrue(
                marker.EndsWith(TestRunSummaryFormatter.LineSeparator, StringComparison.Ordinal),
                "The marker should be a single terminated line."
            );

            string line = FirstLineOf(marker);
            Assert.AreEqual("SUMMARY running started=2026-09-02T10:00:00.000Z mode=" + mode, line);
            Assert.IsTrue(TestRunSummaryFormatter.IsRunningLine(line));
        }

        [Test]
        public void RunningMarkerStartTimeRoundTrips()
        {
            string line = FirstLineOf(
                TestRunSummaryFormatter.FormatRunningMarker(TestMode.EditMode, StartedUtc)
            );

            Assert.IsTrue(
                TestRunSummaryFormatter.TryParseStartedUtc(line, out DateTime parsed),
                "The started field should parse back."
            );
            Assert.AreEqual(StartedUtc, parsed);
            Assert.AreEqual(DateTimeKind.Utc, parsed.Kind);
        }

        [Test]
        public void SummaryCountsEveryLeafOutcomeAcrossAssemblies()
        {
            TestRunResultNode root = BuildTwoAssemblyTree();

            string[] lines = LinesOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    FinishedUtc,
                    root
                )
            );

            Assert.AreEqual(
                "SUMMARY pass=3 fail=2 skip=1 inconclusive=1 seconds=90.000 mode=EditMode started=2026-09-02T10:00:00.000Z finished=2026-09-02T10:01:30.000Z",
                lines[0]
            );
        }

        [Test]
        public void SummaryWritesOneAssemblyLinePerTopLevelChild()
        {
            TestRunResultNode root = BuildTwoAssemblyTree();

            string[] lines = LinesOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    FinishedUtc,
                    root
                )
            );

            Assert.AreEqual(
                "ASSEMBLY name=First.Tests.dll pass=2 fail=1 skip=1 inconclusive=0 seconds=1.500 built=2026-09-01T08:30:00.000Z",
                lines[1]
            );
            Assert.AreEqual(
                "ASSEMBLY name=Second.Tests.dll pass=1 fail=1 skip=0 inconclusive=1 seconds=0.250 built=",
                lines[2]
            );
        }

        [Test]
        public void SummaryWritesOneFailureLinePerFailingLeafWithItsMessageAndLocation()
        {
            TestRunResultNode root = BuildTwoAssemblyTree();

            string[] lines = LinesOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    FinishedUtc,
                    root
                )
            );

            Assert.AreEqual(
                @"FAILURE assembly=First.Tests.dll name=First.Suite.FailingCase location=/repo/Tests/First.cs:42 message=Expected\strue\sbut\swas\sfalse",
                lines[3]
            );
            Assert.AreEqual(
                @"FAILURE assembly=Second.Tests.dll name=Second.Suite.OtherFailure location= message=",
                lines[4]
            );
            Assert.AreEqual(5, lines.Length);
        }

        [Test]
        public void FailureMessageSurvivesTheRoundTripThroughTryGetField()
        {
            const string message = "Expected: True\r\n  But was:  False\tat\\end";
            TestRunResultNode root = new();
            TestRunResultNode assembly = new() { fullName = "Only.Tests.dll" };
            root.children.Add(assembly);
            assembly.children.Add(
                new TestRunResultNode
                {
                    fullName = "Only.Suite.Case(\"a b\")",
                    status = TestStatus.Failed,
                    message = message,
                }
            );

            string[] lines = LinesOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    FinishedUtc,
                    root
                )
            );
            string failureLine = lines[2];

            Assert.IsTrue(
                TestRunSummaryFormatter.TryGetField(
                    failureLine,
                    TestRunSummaryFormatter.MessageKey,
                    out string readMessage
                )
            );
            Assert.AreEqual(message, readMessage);

            Assert.IsTrue(
                TestRunSummaryFormatter.TryGetField(
                    failureLine,
                    TestRunSummaryFormatter.NameKey,
                    out string readName
                )
            );
            Assert.AreEqual("Only.Suite.Case(\"a b\")", readName);
        }

        [Test]
        [TestCase("", TestName = "Escape.Empty")]
        [TestCase("plain", TestName = "Escape.Plain")]
        [TestCase("with space", TestName = "Escape.Space")]
        [TestCase("with\ttab", TestName = "Escape.Tab")]
        [TestCase("with\nnewline", TestName = "Escape.Newline")]
        [TestCase("with\r\ncrlf", TestName = "Escape.CarriageReturn")]
        [TestCase("with\\backslash", TestName = "Escape.Backslash")]
        [TestCase("trailing\\", TestName = "Escape.TrailingBackslash")]
        [TestCase("\\s literal", TestName = "Escape.LiteralEscapeSequence")]
        public void EscapeAndUnescapeRoundTrip(string value)
        {
            string escaped = TestRunSummaryFormatter.Escape(value);

            Assert.IsFalse(escaped.Contains(" "), "An escaped value must remain a single token.");
            Assert.IsFalse(escaped.Contains("\n"));
            Assert.IsFalse(escaped.Contains("\r"));
            Assert.IsFalse(escaped.Contains("\t"));
            Assert.AreEqual(value, TestRunSummaryFormatter.Unescape(escaped));
        }

        [Test]
        public void EscapeHandlesNullAsEmpty()
        {
            Assert.AreEqual(string.Empty, TestRunSummaryFormatter.Escape(null));
            Assert.AreEqual(string.Empty, TestRunSummaryFormatter.Unescape(null));
        }

        [Test]
        public void IsRunningLineRejectsAFinishedSummaryAndMalformedInput()
        {
            string finished = FirstLineOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    FinishedUtc,
                    new TestRunResultNode()
                )
            );

            Assert.IsFalse(TestRunSummaryFormatter.IsRunningLine(finished));
            Assert.IsFalse(TestRunSummaryFormatter.IsRunningLine(null));
            Assert.IsFalse(TestRunSummaryFormatter.IsRunningLine(string.Empty));
            Assert.IsFalse(TestRunSummaryFormatter.IsRunningLine("SUMMARY"));
            Assert.IsFalse(TestRunSummaryFormatter.IsRunningLine("summary running"));
            Assert.IsFalse(TestRunSummaryFormatter.IsRunningLine("ASSEMBLY running"));
        }

        [Test]
        public void TryGetFieldReportsAnAbsentKey()
        {
            string line = FirstLineOf(
                TestRunSummaryFormatter.FormatRunningMarker(TestMode.EditMode, StartedUtc)
            );

            Assert.IsFalse(
                TestRunSummaryFormatter.TryGetField(line, "nosuchkey", out string value)
            );
            Assert.AreEqual(string.Empty, value);
            Assert.IsFalse(TestRunSummaryFormatter.TryGetField(null, "mode", out _));
            Assert.IsFalse(TestRunSummaryFormatter.TryGetField(line, null, out _));
        }

        [Test]
        public void TryParseStartedUtcRejectsAnUnparseableTimestamp()
        {
            Assert.IsFalse(
                TestRunSummaryFormatter.TryParseStartedUtc(
                    "SUMMARY running started=yesterday",
                    out _
                )
            );
            Assert.IsFalse(TestRunSummaryFormatter.TryParseStartedUtc("SUMMARY running", out _));
            Assert.IsFalse(TestRunSummaryFormatter.TryParseStartedUtc(string.Empty, out _));
        }

        [Test]
        [TestCase(
            "  at Ns.Type.Method () [0x00000] in /repo/Tests/File.cs:42 ",
            "/repo/Tests/File.cs:42",
            TestName = "Location.MonoFrame"
        )]
        [TestCase(
            "at Ns.Type.Method() in C:\\repo\\File.cs:line 7",
            "C:\\repo\\File.cs:line 7",
            TestName = "Location.WindowsFrame"
        )]
        [TestCase("at Ns.Type.Method()", "", TestName = "Location.NoFileInformation")]
        [TestCase("", "", TestName = "Location.Empty")]
        [TestCase(null, "", TestName = "Location.Null")]
        [TestCase("at Ns.Type.Method () in ", "", TestName = "Location.EmptyAfterSeparator")]
        public void ExtractLocationReadsTheFirstFrameThatNamesAFile(
            string stackTrace,
            string expected
        )
        {
            Assert.AreEqual(expected, TestRunSummaryFormatter.ExtractLocation(stackTrace));
        }

        [Test]
        public void ExtractLocationSkipsFramesWithoutFileInformation()
        {
            const string stackTrace =
                "at Ns.Type.Outer()\nat Ns.Type.Inner () [0x0] in /repo/Inner.cs:9\n";

            Assert.AreEqual(
                "/repo/Inner.cs:9",
                TestRunSummaryFormatter.ExtractLocation(stackTrace)
            );
        }

        [Test]
        public void SummaryOfANullTreeIsASingleZeroedLine()
        {
            string[] lines = LinesOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.PlayMode,
                    StartedUtc,
                    StartedUtc,
                    null
                )
            );

            Assert.AreEqual(1, lines.Length);
            Assert.AreEqual(
                "SUMMARY pass=0 fail=0 skip=0 inconclusive=0 seconds=0.000 mode=PlayMode started=2026-09-02T10:00:00.000Z finished=2026-09-02T10:00:00.000Z",
                lines[0]
            );
        }

        [Test]
        public void SummaryTreatsARootWithNoChildrenAsASingleLeaf()
        {
            TestRunResultNode root = new() { status = TestStatus.Passed };

            string[] lines = LinesOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    StartedUtc,
                    root
                )
            );

            Assert.AreEqual(1, lines.Length);
            StringAssert.StartsWith("SUMMARY pass=1 fail=0 skip=0 inconclusive=0", lines[0]);
        }

        [Test]
        public void SummaryIgnoresSuiteStatusAndCountsOnlyLeaves()
        {
            TestRunResultNode root = new() { status = TestStatus.Failed };
            TestRunResultNode assembly = new()
            {
                fullName = "Suite.Tests.dll",
                status = TestStatus.Failed,
            };
            root.children.Add(assembly);
            TestRunResultNode suite = new() { fullName = "Suite", status = TestStatus.Failed };
            assembly.children.Add(suite);
            suite.children.Add(
                new TestRunResultNode { fullName = "Suite.One", status = TestStatus.Passed }
            );
            suite.children.Add(
                new TestRunResultNode { fullName = "Suite.Two", status = TestStatus.Passed }
            );

            string[] lines = LinesOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    StartedUtc,
                    root
                )
            );

            StringAssert.StartsWith("SUMMARY pass=2 fail=0 skip=0 inconclusive=0", lines[0]);
            Assert.AreEqual(2, lines.Length);
        }

        [Test]
        public void SummarySkipsNullChildren()
        {
            TestRunResultNode root = new();
            root.children.Add(null);
            root.children.Add(
                new TestRunResultNode { fullName = "Real.dll", status = TestStatus.Passed }
            );

            string[] lines = LinesOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    StartedUtc,
                    root
                )
            );

            Assert.AreEqual(2, lines.Length);
            StringAssert.StartsWith("ASSEMBLY name=Real.dll", lines[1]);
        }

        [Test]
        public void SummaryIgnoresAnOutcomeOutsideTheKnownStatuses()
        {
            TestRunResultNode root = new();
            TestRunResultNode assembly = new() { fullName = "Odd.dll" };
            root.children.Add(assembly);
            assembly.children.Add(
                new TestRunResultNode { fullName = "Odd.Case", status = (TestStatus)999 }
            );

            string[] lines = LinesOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    StartedUtc,
                    root
                )
            );

            StringAssert.StartsWith("SUMMARY pass=0 fail=0 skip=0 inconclusive=0", lines[0]);
        }

        [Test]
        public void SummaryStopsWalkingBeyondTheDepthLimit()
        {
            TestRunResultNode root = new();
            TestRunResultNode current = root;
            for (int i = 0; i < 4 * TestRunSummaryFormatter.MaximumTreeDepth; i++)
            {
                TestRunResultNode child = new()
                {
                    fullName = "Level" + i.ToString(CultureInfo.InvariantCulture),
                    status = TestStatus.Passed,
                };
                current.children.Add(child);
                current = child;
            }

            string[] lines = LinesOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    StartedUtc,
                    root
                )
            );

            StringAssert.StartsWith("SUMMARY pass=0 fail=0 skip=0 inconclusive=0", lines[0]);
            Assert.AreEqual(2, lines.Length);
        }

        [Test]
        public void SummaryHandlesATreeWithManyLeaves()
        {
            const int leafCount = 10000;
            TestRunResultNode root = new();
            TestRunResultNode assembly = new() { fullName = "Large.dll" };
            root.children.Add(assembly);
            for (int i = 0; i < leafCount; i++)
            {
                assembly.children.Add(
                    new TestRunResultNode
                    {
                        fullName = "Large.Case" + i.ToString(CultureInfo.InvariantCulture),
                        status = TestStatus.Passed,
                    }
                );
            }

            string[] lines = LinesOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    StartedUtc,
                    root
                )
            );

            StringAssert.StartsWith(
                "SUMMARY pass=" + leafCount.ToString(CultureInfo.InvariantCulture) + " fail=0",
                lines[0]
            );
            Assert.AreEqual(2, lines.Length);
        }

        [Test]
        [TestCase(double.NaN, TestName = "Seconds.NotANumber")]
        [TestCase(double.PositiveInfinity, TestName = "Seconds.Infinite")]
        [TestCase(-5d, TestName = "Seconds.Negative")]
        public void AssemblySecondsFallsBackToZeroForAnUnusableDuration(double duration)
        {
            TestRunResultNode root = new();
            root.children.Add(
                new TestRunResultNode { fullName = "Odd.dll", durationSeconds = duration }
            );

            string[] lines = LinesOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    StartedUtc,
                    root
                )
            );

            StringAssert.Contains("seconds=0.000", lines[1]);
        }

        [Test]
        public void RunSecondsIsTheWallClockBetweenStartAndFinish()
        {
            string[] lines = LinesOf(
                TestRunSummaryFormatter.FormatSummary(
                    TestMode.EditMode,
                    StartedUtc,
                    StartedUtc.AddMilliseconds(1250),
                    new TestRunResultNode()
                )
            );

            StringAssert.Contains("seconds=1.250", lines[0]);
        }

        [Test]
        public void LocalStartTimesAreNormalizedToUtc()
        {
            DateTime local = StartedUtc.ToLocalTime();

            string line = FirstLineOf(
                TestRunSummaryFormatter.FormatRunningMarker(TestMode.EditMode, local)
            );

            StringAssert.Contains("started=2026-09-02T10:00:00.000Z", line);
        }

        private static TestRunResultNode BuildTwoAssemblyTree()
        {
            TestRunResultNode root = new() { fullName = "Root" };

            TestRunResultNode first = new()
            {
                fullName = "First.Tests.dll",
                durationSeconds = 1.5d,
                assemblyBuiltUtc = new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc),
            };
            root.children.Add(first);
            TestRunResultNode firstSuite = new() { fullName = "First.Suite" };
            first.children.Add(firstSuite);
            firstSuite.children.Add(
                new TestRunResultNode
                {
                    fullName = "First.Suite.PassingCase",
                    status = TestStatus.Passed,
                }
            );
            firstSuite.children.Add(
                new TestRunResultNode
                {
                    fullName = "First.Suite.AlsoPassing",
                    status = TestStatus.Passed,
                }
            );
            firstSuite.children.Add(
                new TestRunResultNode
                {
                    fullName = "First.Suite.SkippedCase",
                    status = TestStatus.Skipped,
                }
            );
            firstSuite.children.Add(
                new TestRunResultNode
                {
                    fullName = "First.Suite.FailingCase",
                    status = TestStatus.Failed,
                    message = "Expected true but was false",
                    stackTrace = "at First.Suite.FailingCase () [0x0] in /repo/Tests/First.cs:42",
                }
            );

            TestRunResultNode second = new()
            {
                fullName = "Second.Tests.dll",
                durationSeconds = 0.25d,
            };
            root.children.Add(second);
            second.children.Add(
                new TestRunResultNode
                {
                    fullName = "Second.Suite.PassingCase",
                    status = TestStatus.Passed,
                }
            );
            second.children.Add(
                new TestRunResultNode
                {
                    fullName = "Second.Suite.Unknown",
                    status = TestStatus.Inconclusive,
                }
            );
            second.children.Add(
                new TestRunResultNode
                {
                    fullName = "Second.Suite.OtherFailure",
                    status = TestStatus.Failed,
                }
            );

            return root;
        }

        private static string FirstLineOf(string content)
        {
            return LinesOf(content)[0];
        }

        private static string[] LinesOf(string content)
        {
            List<string> lines = new();
            foreach (
                string line in content.Split(
                    new[] { TestRunSummaryFormatter.LineSeparator },
                    StringSplitOptions.None
                )
            )
            {
                if (0 < line.Length)
                {
                    lines.Add(line);
                }
            }

            return lines.ToArray();
        }
    }
}
