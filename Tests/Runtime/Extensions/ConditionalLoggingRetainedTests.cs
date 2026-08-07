// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// The control for ConditionalLoggingStrippedTests: identical call sites, kept rather than stripped.
// Without it the stripped fixture would still pass if the [Conditional] attributes were deleted and
// a typo had quietly broken its #undef list.
//
// The symbol is defined HERE rather than relied on from the build, because the standalone tier
// builds a genuine Release player -- scripts/unity/run-ci-tests.ps1 clears BuildOptions.Development
// unconditionally -- so UNITY_EDITOR, DEVELOPMENT_BUILD and DEBUG are all absent there and every
// assertion below would fail. A file-scoped #define is exactly what [Conditional] reads at the call
// site, so this fixture means the same thing in the editor, in PlayMode, and in a Release player.
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
            // The call sites here are kept, so the logs would reach Unity and fail the run as
            // unhandled messages. Only the call-site evaluation is under test, and that happens
            // before the method body decides whether to emit anything. The cores are not
            // [Conditional], so this matters in a Release player too.
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
