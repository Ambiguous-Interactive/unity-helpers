// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Pins the scheduler's static state: what it refuses to start, what it clamps, and that a
    /// finished run leaves nothing attached to the editor's update loop.
    /// </summary>
    /// <remarks>
    /// Nothing here waits for an editor tick. <see cref="ValidationScheduler.Stop"/> drives the
    /// whole completion path synchronously, which is what makes the callback re-entry cases --
    /// the ones that would otherwise only show up as a stuck editor -- testable at all.
    /// </remarks>
    [TestFixture]
    public sealed class ValidationSchedulerTests : CommonTestBase
    {
        [TearDown]
        public void StopAnyActiveRun()
        {
            // Stop any leftover scheduler subscription before the next fixture.
            ValidationScheduler.Stop();
        }

        [Test]
        public void NothingIsRunningToBeginWith()
        {
            Assert.IsFalse(ValidationScheduler.IsRunning);
            Assert.IsTrue(ValidationScheduler.Active == null);
            Assert.AreEqual(
                ValidationScheduler.DefaultBudgetMilliseconds,
                ValidationScheduler.BudgetMilliseconds
            );
        }

        [Test]
        public void StoppingWhenNothingRunsIsQuietRatherThanThrown()
        {
            Assert.DoesNotThrow(ValidationScheduler.Stop);
            Assert.IsFalse(ValidationScheduler.IsRunning);
        }

        [Test]
        public void ANullRunIsRefused()
        {
            Assert.IsFalse(ValidationScheduler.TryStart(null));
            Assert.IsFalse(ValidationScheduler.IsRunning);
        }

        [Test]
        public void AnAlreadyCompleteRunIsRefused()
        {
            Assert.IsFalse(ValidationScheduler.TryStart(EmptyRun()));
            Assert.IsFalse(ValidationScheduler.IsRunning);
        }

        [Test]
        public void OnlyOneRunIsDrivenAtATime()
        {
            ValidationRun first = PendingRun();
            ValidationRun second = PendingRun();

            Assert.IsTrue(ValidationScheduler.TryStart(first));
            Assert.IsFalse(
                ValidationScheduler.TryStart(second),
                "Two runs would each take a full budget from every tick."
            );
            Assert.AreSame(first, ValidationScheduler.Active);
        }

        [TestCase(-1.0)]
        [TestCase(0.0)]
        [TestCase(double.NaN)]
        [TestCase(double.NegativeInfinity)]
        public void AnUnusableBudgetFallsBackToTheDefault(double budget)
        {
            // NaN bypasses a nonpositive guard; require a positive budget.
            Assert.IsTrue(ValidationScheduler.TryStart(PendingRun(), budget));
            Assert.AreEqual(
                ValidationScheduler.DefaultBudgetMilliseconds,
                ValidationScheduler.BudgetMilliseconds
            );
        }

        [Test]
        public void AUsableBudgetIsKept()
        {
            Assert.IsTrue(ValidationScheduler.TryStart(PendingRun(), 12.5));
            Assert.AreEqual(12.5, ValidationScheduler.BudgetMilliseconds);
        }

        [Test]
        public void StoppingCancelsTheRunAndReportsItOnce()
        {
            ValidationRun run = PendingRun();
            int callbacks = 0;

            Assert.IsTrue(
                ValidationScheduler.TryStart(
                    run,
                    ValidationScheduler.DefaultBudgetMilliseconds,
                    finished =>
                    {
                        callbacks++;
                        Assert.AreSame(run, finished);
                        Assert.IsTrue(finished.IsCancelled);
                    }
                )
            );

            ValidationScheduler.Stop();

            Assert.AreEqual(1, callbacks);
            Assert.IsFalse(ValidationScheduler.IsRunning);
            Assert.IsTrue(ValidationScheduler.Active == null);

            ValidationScheduler.Stop();
            Assert.AreEqual(1, callbacks, "A second stop must not report the run again.");
        }

        [Test]
        public void TheCallbackCanStopAndStartWithoutReEnteringTheFinishedRun()
        {
            ValidationRun next = PendingRun();
            int callbacks = 0;

            Assert.IsTrue(
                ValidationScheduler.TryStart(
                    PendingRun(),
                    ValidationScheduler.DefaultBudgetMilliseconds,
                    finished =>
                    {
                        callbacks++;
                        // Release scheduler state before invoking the completion callback.
                        Assert.IsFalse(ValidationScheduler.IsRunning);
                        ValidationScheduler.Stop();
                        Assert.IsTrue(ValidationScheduler.TryStart(next));
                    }
                )
            );

            ValidationScheduler.Stop();

            Assert.AreEqual(1, callbacks);
            Assert.AreSame(next, ValidationScheduler.Active);
        }

        [Test]
        public void ACallbackThatThrowsIsLoggedAndLeavesNothingAttached()
        {
            Assert.IsTrue(
                ValidationScheduler.TryStart(
                    PendingRun(),
                    ValidationScheduler.DefaultBudgetMilliseconds,
                    finished => throw new InvalidOperationException("callback is broken")
                )
            );

            // The temporary log handler proves reporting without requiring a Test Runner log scope.
            RecordingLogHandler recorder = new RecordingLogHandler(Debug.unityLogger.logHandler);
            Debug.unityLogger.logHandler = recorder;
            try
            {
                Assert.DoesNotThrow(ValidationScheduler.Stop);
            }
            finally
            {
                Debug.unityLogger.logHandler = recorder.Inner;
            }

            Assert.AreEqual(
                1,
                recorder.Exceptions.Count,
                "A swallowed callback failure is silent."
            );
            Assert.IsInstanceOf<InvalidOperationException>(recorder.Exceptions[0]);
            Assert.IsFalse(ValidationScheduler.IsRunning);
        }

        private static ValidationRun EmptyRun()
        {
            return new ValidationRun(null, null, Never);
        }

        private static ValidationRun PendingRun()
        {
            return new ValidationRun(
                new List<IValidationRule>(),
                new List<ValidationTarget>
                {
                    new ValidationTarget(
                        "00000000000000000000000000000001",
                        "Assets/First.asset",
                        typeof(ScriptableObject)
                    ),
                },
                Never
            );
        }

        private static Object Never(ValidationTarget target)
        {
            return null;
        }

        /// <summary>Captures what was logged, and passes everything else through.</summary>
        private sealed class RecordingLogHandler : ILogHandler
        {
            internal RecordingLogHandler(ILogHandler inner)
            {
                Inner = inner;
            }

            internal ILogHandler Inner { get; }

            internal List<Exception> Exceptions { get; } = new List<Exception>();

            public void LogFormat(
                LogType logType,
                Object context,
                string format,
                params object[] args
            )
            {
                Inner.LogFormat(logType, context, format, args);
            }

            public void LogException(Exception exception, Object context)
            {
                Exceptions.Add(exception);
            }
        }
    }
}
