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
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class PeriodicJobExtensionsTests : CommonTestBase
    {
        [Test]
        public void InvalidOwnersAndCallbacksDoNotStartWork()
        {
            int calls = 0;
            Assert.Throws<ArgumentNullException>(() =>
                Assert.That(((MonoBehaviour)null).StartPeriodicJob(() => ++calls, 0f), Is.Null)
            );
            CoroutineHost host = CreateHost();
            Assert.Throws<ArgumentNullException>(() =>
                Assert.That(host.StartPeriodicJob(null, 0f), Is.Null)
            );
            host.gameObject.SetActive(false);
            Assert.That(host.StartPeriodicJob(() => ++calls, 0f), Is.Null);
            DestroyTrackedObjects();
            Assert.Throws<ArgumentNullException>(() =>
                Assert.That(host.StartPeriodicJob(() => ++calls, 0f), Is.Null)
            );
            Assert.That(calls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator NonfiniteAndNonpositiveIntervalsInvokeAtMostOncePerFrame()
        {
            CoroutineHost host = CreateHost();
            foreach (
                float interval in new[]
                {
                    0f,
                    -1f,
                    float.NaN,
                    float.NegativeInfinity,
                    float.PositiveInfinity,
                }
            )
            {
                int calls = 0;
                Coroutine job = host.StartPeriodicJob(() => ++calls, interval, true, true);
                try
                {
                    Assert.That(job, Is.Not.Null);
                    Assert.That(calls, Is.EqualTo(1));
                    for (int frame = 0; frame < 3; ++frame)
                    {
                        int before = calls;
                        yield return null;
                        Assert.That(calls - before, Is.InRange(0, 1));
                    }
                    Assert.That(calls, Is.GreaterThan(1));
                }
                finally
                {
                    host.StopCoroutine(job);
                }
            }
        }

        [UnityTest]
        public IEnumerator DisabledBehaviourCanHostWorkAndExplicitCancellationStopsIt()
        {
            CoroutineHost host = CreateHost();
            host.enabled = false;
            int calls = 0;
            Coroutine job = host.StartPeriodicJob(() => ++calls, 0f);
            Assert.That(job, Is.Not.Null);
            Assert.That(calls, Is.EqualTo(1));
            yield return null;
            host.StopCoroutine(job);
            int stoppedAt = calls;
            yield return null;
            yield return null;
            Assert.That(calls, Is.EqualTo(stoppedAt));
        }

        [UnityTest]
        public IEnumerator FailureIsReportedOnceAndLaterTicksContinue()
        {
            CoroutineHost host = CreateHost();
            GameObject context = Track(new GameObject("Periodic work context"));
            int calls = 0;
            LogAssert.Expect(
                LogType.Error,
                new Regex("A repeating job threw; it will keep running")
            );
            Coroutine job = host.StartPeriodicJob(
                () =>
                {
                    ++calls;
                    throw new InvalidOperationException("Controlled periodic failure");
                },
                0f,
                context: context
            );
            try
            {
                for (int frame = 0; frame < 10 && calls < 3; ++frame)
                {
                    yield return null;
                }
                Assert.That(calls, Is.GreaterThanOrEqualTo(3));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                host.StopCoroutine(job);
            }
        }

        [UnityTest]
        public IEnumerator DestroyingTheHostStopsWorkEvenWhenContextSurvives()
        {
            CoroutineHost host = CreateHost();
            GameObject context = Track(new GameObject("Surviving periodic context"));
            int calls = 0;
            Coroutine job = host.StartPeriodicJob(() => ++calls, 0f, context: context);
            Assert.That(job, Is.Not.Null);
            Assert.That(calls, Is.EqualTo(1));
            UnityEngine.Object.DestroyImmediate(host.gameObject); // UNH-SUPPRESS: The context must survive host teardown.
            yield return null;
            yield return null;
            Assert.That(context != null, Is.True);
            Assert.That(calls, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TimedWaitsYieldFramesWithoutAllocatingNestedIterators(bool waitBefore)
        {
            int calls = 0;
            IEnumerator job = Helpers.FunctionAsCoroutine(
                null,
                () => ++calls,
                float.MaxValue,
                false,
                waitBefore,
                null
            );
            try
            {
                for (int step = 0; step < 32; ++step)
                {
                    Assert.That(job.MoveNext(), Is.True);
                    Assert.That(job.Current, Is.Null);
                }
                Assert.That(calls, Is.EqualTo(waitBefore ? 0 : 1));
            }
            finally
            {
                ((IDisposable)job).Dispose();
            }
        }

        [UnityTest]
        public IEnumerator TimedJobsContinueAcrossIntervalsWithoutNestedWaits()
        {
            int calls = 0;
            IEnumerator job = Helpers.FunctionAsCoroutine(
                null,
                () => ++calls,
                float.Epsilon,
                false,
                false,
                null
            );
            try
            {
                for (int frame = 0; frame < 10 && calls < 3; ++frame)
                {
                    Assert.That(job.MoveNext(), Is.True);
                    Assert.That(job.Current, Is.Null);
                    yield return null;
                }
                Assert.That(calls, Is.GreaterThanOrEqualTo(3));
            }
            finally
            {
                ((IDisposable)job).Dispose();
            }
        }

        private CoroutineHost CreateHost()
        {
            return Track(new GameObject(nameof(PeriodicJobExtensionsTests), typeof(CoroutineHost)))
                .GetComponent<CoroutineHost>();
        }
    }
}
