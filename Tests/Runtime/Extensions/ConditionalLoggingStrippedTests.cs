// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// [Conditional] is resolved against the symbols in effect at the CALL SITE, so undefining them for
// the length of this file reproduces a release player build inside an editor test run. Every
// logging call below must therefore compile away entirely, receiver and arguments included.
// ConditionalLoggingRetainedTests is the same file without these lines and asserts the opposite;
// the pair is what proves these directives, and not some unrelated accident, cause the zeroes.
#undef ENABLE_UBERLOGGING
#undef DEVELOPMENT_BUILD
#undef DEBUG
#undef UNITY_EDITOR
#undef DEBUG_LOGGING
#undef WARN_LOGGING
#undef ERROR_LOGGING

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
    public sealed class ConditionalLoggingStrippedTests : CommonTestBase
    {
        private bool _previousGlobalLogging;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _previousGlobalLogging = WallstopStudiosLogger.IsGlobalLoggingEnabled();
            // A regression here should fail on the counter, not on an unhandled Unity log message.
            WallstopStudiosLogger.SetGlobalLoggingEnabled(false);
            LoggingCallSiteProbe.Reset(Track(new GameObject("ConditionalLoggingStripped")));
        }

        [TearDown]
        public override void TearDown()
        {
            WallstopStudiosLogger.SetGlobalLoggingEnabled(_previousGlobalLogging);
            LoggingCallSiteProbe.Reset(null);
            base.TearDown();
        }

        [Test]
        public void LogEvaluatesNothing()
        {
            LoggingCallSiteProbe.Receiver.Log($"stripped {LoggingCallSiteProbe.Argument}");

            AssertCallSiteRemoved();
        }

        [Test]
        public void LogDebugEvaluatesNothing()
        {
            LoggingCallSiteProbe.Receiver.LogDebug($"stripped {LoggingCallSiteProbe.Argument}");

            AssertCallSiteRemoved();
        }

        [Test]
        public void LogWarnEvaluatesNothing()
        {
            LoggingCallSiteProbe.Receiver.LogWarn($"stripped {LoggingCallSiteProbe.Argument}");

            AssertCallSiteRemoved();
        }

        [Test]
        public void LogErrorEvaluatesNothing()
        {
            LoggingCallSiteProbe.Receiver.LogError($"stripped {LoggingCallSiteProbe.Argument}");

            AssertCallSiteRemoved();
        }

        [Test]
        public void LogNotAssignedEvaluatesNothing()
        {
            LoggingCallSiteProbe.Receiver.LogNotAssigned(
                $"stripped {LoggingCallSiteProbe.Argument}"
            );

            AssertCallSiteRemoved();
        }

        [Test]
        public void ValidateAssignmentsEvaluatesNothing()
        {
            LoggingCallSiteProbe.Receiver.ValidateAssignments();

            Assert.That(
                LoggingCallSiteProbe.ReceiverEvaluations,
                Is.Zero,
                "ValidateAssignments must not evaluate its receiver when logging is compiled out."
            );
        }

        private static void AssertCallSiteRemoved()
        {
            Assert.That(
                LoggingCallSiteProbe.ReceiverEvaluations,
                Is.Zero,
                "A [Conditional] logging call must not evaluate its receiver -- a receiver such as "
                    + "Singleton.Instance creates the singleton purely to call a stripped method."
            );
            Assert.That(
                LoggingCallSiteProbe.ArgumentEvaluations,
                Is.Zero,
                "A [Conditional] logging call must not build its FormattableString arguments."
            );
        }
    }
}
