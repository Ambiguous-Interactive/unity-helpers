// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class RepeatingJobFailureTests : CommonTestBase
    {
        [UnityTest]
        public IEnumerator UnformattableActionFailureDoesNotStopTheJob()
        {
            yield return VerifyContinuingJob(0);
        }

        [UnityTest]
        public IEnumerator UnformattableHandlerFailureDoesNotStopTheJob()
        {
            yield return VerifyContinuingJob(1);
        }

        [UnityTest]
        public IEnumerator UnformattableActionWithThrowingHandlerDoesNotStopTheJob()
        {
            yield return VerifyContinuingJob(2);
        }

        [UnityTest]
        public IEnumerator UnformattableSpreadFailureDoesNotCancelRemainingInvocations()
        {
            int calls = 0;
            LogAssert.Expect(
                LogType.Error,
                new Regex("A repeating job threw; its diagnostic could not be written")
            );
            yield return Helpers.ExecuteOverTime(
                () =>
                {
                    ++calls;
                    throw new UnformattableJobException();
                },
                3,
                0f
            );
            Assert.That(calls, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator ThrowingLogHandlerCannotStopTheJobOrRepeatDiagnosticAttempts()
        {
            GameObject owner = Track(
                new GameObject(nameof(RepeatingJobFailureTests), typeof(CoroutineHost))
            );
            CoroutineHost host = owner.GetComponent<CoroutineHost>();
            ILogHandler original = Debug.unityLogger.logHandler;
            RefusingJobLogHandler refusing = new();
            Coroutine job = null;
            int calls = 0;
            try
            {
                Debug.unityLogger.logHandler = refusing;
                job = host.StartFunctionAsCoroutine(
                    () =>
                    {
                        ++calls;
                        throw new InvalidOperationException("Controlled action failure");
                    },
                    0f
                );
                for (int frame = 0; frame < 30 && calls < 3; ++frame)
                {
                    yield return null;
                }

                Assert.That(calls, Is.GreaterThanOrEqualTo(3));
                Assert.That(refusing.Attempts, Is.EqualTo(2));
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
                if (job != null && host != null)
                {
                    host.StopCoroutine(job);
                }
            }
        }

        private IEnumerator VerifyContinuingJob(int mode)
        {
            GameObject owner = Track(
                new GameObject(nameof(RepeatingJobFailureTests), typeof(CoroutineHost))
            );
            CoroutineHost host = owner.GetComponent<CoroutineHost>();
            int calls = 0;
            int handled = 0;
            LogAssert.Expect(
                LogType.Error,
                new Regex("A repeating job threw; its diagnostic could not be written")
            );
            Action tick = () =>
            {
                ++calls;
                if (mode == 1)
                {
                    throw new InvalidOperationException("Controlled action failure");
                }

                throw new UnformattableJobException();
            };
            Action<ulong, UnityEngine.Object, Exception> handler =
                mode == 0
                    ? null
                    : (_, _, _) =>
                    {
                        ++handled;
                        if (mode == 1)
                        {
                            throw new UnformattableJobException();
                        }

                        throw new InvalidOperationException("Controlled handler failure");
                    };
            Coroutine job = host.StartFunctionAsCoroutine(tick, 0f, false, false, host, handler);
            try
            {
                for (int frame = 0; frame < 30 && calls < 3; ++frame)
                {
                    yield return null;
                }

                Assert.That(calls, Is.GreaterThanOrEqualTo(3));
                Assert.That(handled, mode == 0 ? Is.EqualTo(0) : Is.EqualTo(calls));
            }
            finally
            {
                if (job != null && host != null)
                {
                    host.StopCoroutine(job);
                }
            }
        }
    }
}
