// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// Guards the tolerance in <see cref="CommonTestBase"/> for engine-emitted logs no test asserts
    /// and no test can prevent. The Unity Test Framework fails a plain <c>[Warning]</c>, and the
    /// native temp allocator emits one on frame count rather than on anything a test does, so the
    /// same commit passed on three editors and failed on a fourth (#393).
    /// </summary>
    public sealed class CommonTestBaseToleratedEngineLogTests : CommonTestBase
    {
        private const string TempAllocatorWarning =
            "Internal: deleting an allocation that is older than its permitted lifetime of 4 frames (age = 6)";

        [Test]
        public void ToleratedEngineLogMatchesTheTempAllocatorWarning()
        {
            Assert.IsTrue(
                IsToleratedEngineLog(LogType.Warning, TempAllocatorWarning),
                "the native temp-allocator lifetime warning must be tolerated"
            );
        }

        [TestCase("A warning a test should catch")]
        [TestCase("Internal: deleting an allocation")]
        [TestCase("permitted lifetime")]
        [TestCase("")]
        [TestCase(null)]
        public void UnrelatedWarningsAreNotTolerated(string message)
        {
            Assert.IsFalse(
                IsToleratedEngineLog(LogType.Warning, message),
                "tolerance must not widen into ignoring failing messages"
            );
        }

        [TestCase(LogType.Error)]
        [TestCase(LogType.Assert)]
        [TestCase(LogType.Exception)]
        [TestCase(LogType.Log)]
        public void TheSameTextAtAnotherLogTypeIsNotTolerated(LogType type)
        {
            Assert.IsFalse(
                IsToleratedEngineLog(type, TempAllocatorWarning),
                "the tolerance is per log type, not per message"
            );
        }

        [UnityTest]
        public IEnumerator ATestSurvivesTheEngineWarningItCannotPrevent()
        {
            if (!Application.isPlaying)
            {
                Assert.Ignore("The teardown reconciliation this proves is PlayMode-only.");
            }

            // Emitted twice on purpose: the reconciliation takes one expectation per occurrence, and
            // a single expectation for two warnings still fails (measured on 6000.4.6f1). If this
            // fixture's teardown goes red, that is the regression.
            Debug.LogWarning(TempAllocatorWarning);
            yield return null;
            Debug.LogWarning(
                "Internal: deleting an allocation that is older than its permitted lifetime of 4 frames (age = 9)"
            );
            yield return null;
        }
    }
}
