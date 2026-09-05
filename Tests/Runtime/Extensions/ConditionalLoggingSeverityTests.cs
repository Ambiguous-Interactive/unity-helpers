// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*
    One granular symbol detects severity-to-symbol swaps that all-enabled and all-disabled controls miss.
*/
#undef ENABLE_UBERLOGGING
#undef DEVELOPMENT_BUILD
#undef DEBUG
#undef UNITY_EDITOR
#undef DEBUG_LOGGING
#undef ERROR_LOGGING
#define WARN_LOGGING

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
    public sealed class ConditionalLoggingSeverityTests : CommonTestBase
    {
        private bool _previousGlobalLogging;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _previousGlobalLogging = WallstopStudiosLogger.IsGlobalLoggingEnabled();
            WallstopStudiosLogger.SetGlobalLoggingEnabled(false);
            LoggingCallSiteProbe.Reset(Track(new GameObject("ConditionalLoggingSeverity")));
        }

        [TearDown]
        public override void TearDown()
        {
            WallstopStudiosLogger.SetGlobalLoggingEnabled(_previousGlobalLogging);
            LoggingCallSiteProbe.Reset(null);
            base.TearDown();
        }

        [Test]
        public void LogIsStrippedByWarnOnlySymbol()
        {
            LoggingCallSiteProbe.Receiver.Log($"severity {LoggingCallSiteProbe.Argument}");

            AssertStripped(nameof(WallstopStudiosLogger.Log));
        }

        [Test]
        public void LogDebugIsStrippedByWarnOnlySymbol()
        {
            LoggingCallSiteProbe.Receiver.LogDebug($"severity {LoggingCallSiteProbe.Argument}");

            AssertStripped(nameof(WallstopStudiosLogger.LogDebug));
        }

        [Test]
        public void LogErrorIsStrippedByWarnOnlySymbol()
        {
            LoggingCallSiteProbe.Receiver.LogError($"severity {LoggingCallSiteProbe.Argument}");

            AssertStripped(nameof(WallstopStudiosLogger.LogError));
        }

        [Test]
        public void LogWarnIsRetainedByWarnOnlySymbol()
        {
            LoggingCallSiteProbe.Receiver.LogWarn($"severity {LoggingCallSiteProbe.Argument}");

            AssertRetained(nameof(WallstopStudiosLogger.LogWarn));
        }

        [Test]
        public void LogNotAssignedIsRetainedByWarnOnlySymbol()
        {
            LoggingCallSiteProbe.Receiver.LogNotAssigned(
                $"severity {LoggingCallSiteProbe.Argument}"
            );

            AssertRetained(nameof(Helpers.LogNotAssigned));
        }

        [Test]
        public void ValidateAssignmentsIsRetainedByWarnOnlySymbol()
        {
            LoggingCallSiteProbe.Receiver.ValidateAssignments();

            Assert.That(
                LoggingCallSiteProbe.ReceiverEvaluations,
                Is.EqualTo(1),
                $"{nameof(ValidateAssignmentExtensions.ValidateAssignments)} logs warnings, so "
                    + "WARN_LOGGING alone must keep it."
            );
        }

        private static void AssertStripped(string member)
        {
            Assert.That(
                LoggingCallSiteProbe.ReceiverEvaluations,
                Is.Zero,
                $"{member} must not survive when only WARN_LOGGING is defined."
            );
            Assert.That(LoggingCallSiteProbe.ArgumentEvaluations, Is.Zero);
        }

        private static void AssertRetained(string member)
        {
            Assert.That(
                LoggingCallSiteProbe.ReceiverEvaluations,
                Is.EqualTo(1),
                $"{member} is warn-level, so WARN_LOGGING alone must keep it."
            );
            Assert.That(LoggingCallSiteProbe.ArgumentEvaluations, Is.EqualTo(1));
        }
    }
}
