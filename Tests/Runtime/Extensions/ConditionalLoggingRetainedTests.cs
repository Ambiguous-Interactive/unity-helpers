// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*
    A file-scoped symbol keeps these call sites active even in Release players, providing a positive control for
    the stripped fixture.
*/
#define ENABLE_UBERLOGGING

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ConditionalLoggingRetainedTests : CommonTestBase
    {
        private bool _previousGlobalLogging;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _previousGlobalLogging = WallstopStudiosLogger.IsGlobalLoggingEnabled();
            /*
                Suppress emitted logs so the fixture measures argument evaluation without failing on unhandled
                Unity messages.
            */
            WallstopStudiosLogger.SetGlobalLoggingEnabled(false);
            LoggingCallSiteProbe.Reset(Track(new GameObject("ConditionalLoggingRetained")));
        }

        [TearDown]
        public override void TearDown()
        {
            WallstopStudiosLogger.SetGlobalLoggingEnabled(_previousGlobalLogging);
            LoggingCallSiteProbe.Reset(null);
            base.TearDown();
        }

        [Test]
        public void LogEvaluatesReceiverAndArguments()
        {
            LoggingCallSiteProbe.Receiver.Log($"retained {LoggingCallSiteProbe.Argument}");

            AssertCallSiteRetained();
        }

        [Test]
        public void LogDebugEvaluatesReceiverAndArguments()
        {
            LoggingCallSiteProbe.Receiver.LogDebug($"retained {LoggingCallSiteProbe.Argument}");

            AssertCallSiteRetained();
        }

        [Test]
        public void LogWarnEvaluatesReceiverAndArguments()
        {
            LoggingCallSiteProbe.Receiver.LogWarn($"retained {LoggingCallSiteProbe.Argument}");

            AssertCallSiteRetained();
        }

        [Test]
        public void LogErrorEvaluatesReceiverAndArguments()
        {
            LoggingCallSiteProbe.Receiver.LogError($"retained {LoggingCallSiteProbe.Argument}");

            AssertCallSiteRetained();
        }

        [Test]
        public void LogNotAssignedEvaluatesReceiverAndArguments()
        {
            LoggingCallSiteProbe.Receiver.LogNotAssigned(
                $"retained {LoggingCallSiteProbe.Argument}"
            );

            AssertCallSiteRetained();
        }

        [Test]
        public void ValidateAssignmentsEvaluatesReceiver()
        {
            LoggingCallSiteProbe.Receiver.ValidateAssignments();

            Assert.That(
                LoggingCallSiteProbe.ReceiverEvaluations,
                Is.EqualTo(1),
                "ValidateAssignments must still run where Unity defines the enabling symbols."
            );
        }

        private static void AssertCallSiteRetained()
        {
            Assert.That(
                LoggingCallSiteProbe.ReceiverEvaluations,
                Is.EqualTo(1),
                "Logging must still run where Unity defines the enabling symbols."
            );
            Assert.That(
                LoggingCallSiteProbe.ArgumentEvaluations,
                Is.EqualTo(1),
                "Logging must still build its message where Unity defines the enabling symbols."
            );
        }
    }
}
