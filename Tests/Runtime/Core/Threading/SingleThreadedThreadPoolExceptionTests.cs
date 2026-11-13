// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.Threading
{
#if !SINGLE_THREADED
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Threading;

    [TestFixture]
    [Category("Fast")]
    public sealed class SingleThreadedThreadPoolExceptionTests
    {
        private const int TimeoutMilliseconds = 10_000;

        [Test]
        public void OriginalDefaultLiteralConstructorCallsRemainUnambiguous()
        {
            using SingleThreadedThreadPool oneArgument = new(default);
            using SingleThreadedThreadPool twoArguments = new(default, default);
            Assert.IsTrue(oneArgument.IsAcceptingWork);
            Assert.IsTrue(twoArguments.IsAcceptingWork);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CallbackReceivesEveryDelegateFailureWithoutRetainingHandledErrors(
            bool background
        )
        {
            Exception[] expected =
            {
                new InvalidOperationException("action"),
                new InvalidOperationException("task"),
                new InvalidOperationException("value task"),
            };
            List<Exception> observed = new();
            Action<Exception> callback = observed.Add;
            using SingleThreadedThreadPool pool = new(
                runInBackground: background,
                noWorkWaitTime: null,
                onException: callback
            );
            Assert.IsTrue(pool.TryEnqueue((Action)(() => throw expected[0])));
            Assert.IsTrue(pool.TryEnqueue((Func<Task>)(() => Task.FromException(expected[1]))));
            Assert.IsTrue(
                pool.TryEnqueue(
                    (Func<ValueTask>)(() => new ValueTask(Task.FromException(expected[2])))
                )
            );
            bool continued = false;
            Assert.IsTrue(pool.TryEnqueue((Action)(() => continued = true)));
            Task<bool> drain = pool.DrainAsync().AsTask();
            Assert.IsTrue(drain.Wait(TimeoutMilliseconds));
            Assert.IsTrue(drain.Result);
            Assert.IsTrue(continued);
            CollectionAssert.AreEqual(expected, observed);
            Assert.That(pool.Exceptions, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ThrowingCallbacksRetainBothFailuresAndBoundTheFallbackQueue(bool background)
        {
            Exception callbackError = new InvalidOperationException(
                nameof(ThrowingCallbacksRetainBothFailuresAndBoundTheFallbackQueue)
            );
            Action<Exception> callback = _ => throw callbackError;
            using SingleThreadedThreadPool pool = new(
                runInBackground: background,
                noWorkWaitTime: null,
                onException: callback
            );
            Exception[] expected = new Exception[100];
            for (int index = 0; index < expected.Length; ++index)
            {
                string message = index.ToString(CultureInfo.InvariantCulture);
                Exception failure = new InvalidOperationException(message);
                expected[index] = failure;
                Assert.IsTrue(pool.TryEnqueue((Action)(() => throw failure)));
            }
            Task<bool> drain = pool.DrainAsync().AsTask();
            Assert.IsTrue(drain.Wait(TimeoutMilliseconds));
            Assert.IsTrue(drain.Result);
            Assert.That(pool.Exceptions.Count, Is.EqualTo(64));
            for (int index = 36; index < expected.Length; ++index)
            {
                Assert.IsTrue(pool.Exceptions.TryDequeue(out Exception retained));
                Assert.That(retained, Is.InstanceOf<AggregateException>());
                AggregateException combined = (AggregateException)retained;
                Assert.That(combined.InnerExceptions.Count, Is.EqualTo(2));
                Assert.That(combined.InnerExceptions[0], Is.SameAs(expected[index]));
                Assert.That(combined.InnerExceptions[1], Is.SameAs(callbackError));
            }
            Assert.That(pool.Exceptions, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CallbackCanSubmitFollowupWorkBeforeDrainClosesTheQueue(bool background)
        {
            using ManualResetEventSlim submitted = new();
            SingleThreadedThreadPool pool = null;
            bool accepted = false;
            bool completed = false;
            Action<Exception> callback = _ =>
            {
                accepted = pool.TryEnqueue((Action)(() => completed = true));
                submitted.Set();
            };
            pool = new SingleThreadedThreadPool(
                runInBackground: background,
                noWorkWaitTime: null,
                onException: callback
            );
            using (pool)
            {
                Assert.IsTrue(
                    pool.TryEnqueue(
                        (Action)(
                            () =>
                                throw new InvalidOperationException(
                                    nameof(CallbackCanSubmitFollowupWorkBeforeDrainClosesTheQueue)
                                )
                        )
                    )
                );
                Assert.IsTrue(submitted.Wait(TimeoutMilliseconds));
                Assert.IsTrue(accepted);
                Task<bool> drain = pool.DrainAsync().AsTask();
                Assert.IsTrue(drain.Wait(TimeoutMilliseconds));
                Assert.IsTrue(drain.Result);
                Assert.IsTrue(completed);
                Assert.That(pool.Exceptions, Is.Empty);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NullCallbackPreservesTheOriginalExceptionQueue(bool background)
        {
            using SingleThreadedThreadPool pool = new(
                runInBackground: background,
                noWorkWaitTime: null,
                onException: null
            );
            Exception expected = new InvalidOperationException(
                nameof(NullCallbackPreservesTheOriginalExceptionQueue)
            );
            Assert.IsTrue(pool.TryEnqueue((Action)(() => throw expected)));
            Task<bool> drain = pool.DrainAsync().AsTask();
            Assert.IsTrue(drain.Wait(TimeoutMilliseconds));
            Assert.IsTrue(drain.Result);
            Assert.IsTrue(pool.Exceptions.TryDequeue(out Exception retained));
            Assert.That(retained, Is.SameAs(expected));
        }
    }
#endif
}
