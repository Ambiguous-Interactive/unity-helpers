// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.WButton
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using System.Threading;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    public sealed class WButtonCoroutineSchedulerTests
    {
        private readonly List<WButtonCoroutineTicket> _tickets = new();
        private readonly List<CancellationTokenSource> _sources = new();

        private static IEnumerator<object> GenericParent(IEnumerator child, List<string> disposed)
        {
            try
            {
                yield return child;
                yield return null;
            }
            finally
            {
                disposed.Add("parent");
            }
        }

        private static IEnumerator<object> PooledParent(
            IEnumerator child,
            Action<PooledResource<List<int>>, List<int>> onRent
        )
        {
            using PooledResource<List<int>> lease = Buffers<int>.List.Get(out List<int> values);
            values.Add(37);
            onRent(lease, values);
            yield return child;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (WButtonCoroutineTicket ticket in _tickets)
            {
                WButtonCoroutineScheduler.Cancel(ticket);
            }
            WButtonCoroutineScheduler.Update();
            WButtonCoroutineScheduler.Update();
            _tickets.Clear();
            foreach (CancellationTokenSource source in _sources)
            {
                source.Dispose();
            }
            _sources.Clear();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CancellationBeforeFirstTickDisposesUnstartedRoot(bool withTokenSource)
        {
            List<string> disposed = new();
            WButtonDisposableEnumerator root = new("root", disposed);
            int cancelled = 0;
            WButtonCoroutineTicket ticket = Schedule(
                root,
                () => Assert.Fail("Unexpected completion."),
                exception => Assert.Fail(exception.ToString()),
                () => ++cancelled,
                withTokenSource
            );
            WButtonCoroutineScheduler.Cancel(ticket);
            WButtonCoroutineScheduler.Update();
            WButtonCoroutineScheduler.Update();
            Assert.AreEqual(0, root.MoveNextCount);
            Assert.AreEqual(1, root.DisposeCount);
            Assert.AreEqual(1, cancelled);
            CollectionAssert.AreEqual(new[] { "root" }, disposed);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TerminalCleanupReturnsPooledRentHeldAcrossYield(bool nestedFault)
        {
            List<string> disposed = new();
            WButtonDisposableEnumerator child = new("child", disposed)
            {
                FailMoveNext = nestedFault,
            };
            PooledResource<List<int>> capturedLease = default;
            List<int> capturedValues = null;
            Exception failure = null;
            int cancelled = 0;
            int faults = 0;
            WButtonCoroutineTicket ticket = Schedule(
                PooledParent(
                    child,
                    (lease, values) =>
                    {
                        capturedLease = lease;
                        capturedValues = values;
                    }
                ),
                () => Assert.Fail("Unexpected completion."),
                exception =>
                {
                    failure = exception;
                    ++faults;
                },
                () => ++cancelled
            );
            try
            {
                WButtonCoroutineScheduler.Update();
                Assert.IsTrue(capturedLease.IsHeld);
                CollectionAssert.AreEqual(new[] { 37 }, capturedValues);
                if (!nestedFault)
                {
                    WButtonCoroutineScheduler.Cancel(ticket);
                }
                WButtonCoroutineScheduler.Update();
                WButtonCoroutineScheduler.Update();
                Assert.IsFalse(capturedLease.IsHeld);
                CollectionAssert.IsEmpty(capturedValues);
                Assert.AreEqual(nestedFault ? 1 : 0, faults);
                Assert.AreEqual(nestedFault ? 0 : 1, cancelled);
                if (nestedFault)
                {
                    Assert.AreEqual("MoveNext failed.", failure.Message);
                }
                Assert.AreEqual(1, child.DisposeCount);
            }
            finally
            {
                capturedLease.Dispose();
            }
        }

        [Test]
        public void CancellationDisposesNestedLegacyBeforeGenericParent()
        {
            List<string> disposed = new();
            WButtonDisposableEnumerator child = new("child", disposed);
            int cancelled = 0;
            WButtonCoroutineTicket ticket = Schedule(
                GenericParent(child, disposed),
                () => Assert.Fail("Unexpected completion."),
                exception => Assert.Fail(exception.ToString()),
                () => ++cancelled
            );
            WButtonCoroutineScheduler.Update();
            WButtonCoroutineScheduler.Update();
            WButtonCoroutineScheduler.Cancel(ticket);
            WButtonCoroutineScheduler.Update();
            Assert.AreEqual(1, cancelled);
            Assert.AreEqual(1, child.DisposeCount);
            CollectionAssert.AreEqual(new[] { "child", "parent" }, disposed);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NestedFaultDisposesWholeStackAndPreservesPrimaryFailure(bool failCurrent)
        {
            List<string> disposed = new();
            WButtonDisposableEnumerator child = failCurrent
                ? new WButtonGenericDisposableEnumerator("child", disposed)
                : new WButtonDisposableEnumerator("child", disposed);
            child.FailMoveNext = !failCurrent;
            child.FailCurrent = failCurrent;
            child.FailDispose = true;
            Exception failure = null;
            int faults = 0;
            Schedule(
                GenericParent(child, disposed),
                () => Assert.Fail("Unexpected completion."),
                exception =>
                {
                    failure = exception;
                    ++faults;
                },
                () => Assert.Fail("Unexpected cancellation.")
            );
            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: Dispose failed\\.")
            );
            WButtonCoroutineScheduler.Update();
            WButtonCoroutineScheduler.Update();
            WButtonCoroutineScheduler.Update();
            Assert.AreEqual(1, faults);
            Assert.AreEqual(failCurrent ? "Current failed." : "MoveNext failed.", failure.Message);
            Assert.AreEqual(1, child.DisposeCount);
            CollectionAssert.AreEqual(new[] { "child", "parent" }, disposed);
        }

        [Test]
        public void NaturalCompletionDisposesLegacyChildAndGenericParentExactlyOnce()
        {
            List<string> disposed = new();
            WButtonDisposableEnumerator child = new("child", disposed, yieldCount: 0);
            int completed = 0;
            Schedule(
                GenericParent(child, disposed),
                () => ++completed,
                exception => Assert.Fail(exception.ToString()),
                () => Assert.Fail("Unexpected cancellation.")
            );
            for (int tick = 0; tick < 6; ++tick)
            {
                WButtonCoroutineScheduler.Update();
            }
            Assert.AreEqual(1, completed);
            Assert.AreEqual(1, child.DisposeCount);
            CollectionAssert.AreEqual(new[] { "child", "parent" }, disposed);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisposalFailureStillReleasesParentAndReportsFault(bool cancel)
        {
            List<string> disposed = new();
            WButtonDisposableEnumerator child = new("child", disposed, yieldCount: 0)
            {
                FailDispose = true,
            };
            Exception failure = null;
            int faults = 0;
            WButtonCoroutineTicket ticket = Schedule(
                GenericParent(child, disposed),
                () => Assert.Fail("Unexpected completion."),
                exception =>
                {
                    failure = exception;
                    ++faults;
                },
                () => Assert.Fail("Unexpected cancellation.")
            );
            WButtonCoroutineScheduler.Update();
            if (cancel)
            {
                WButtonCoroutineScheduler.Cancel(ticket);
            }
            WButtonCoroutineScheduler.Update();
            WButtonCoroutineScheduler.Update();
            Assert.AreEqual(1, faults);
            Assert.AreEqual("Dispose failed.", failure.Message);
            Assert.AreEqual(1, child.DisposeCount);
            CollectionAssert.AreEqual(new[] { "child", "parent" }, disposed);
        }

        [Test]
        public void ThrowingCompletionCallbackDoesNotBlockOtherRoutine()
        {
            List<string> disposed = new();
            int otherCompleted = 0;
            Schedule(
                new WButtonDisposableEnumerator("other", disposed, yieldCount: 0),
                () => ++otherCompleted,
                exception => Assert.Fail(exception.ToString()),
                () => Assert.Fail("Unexpected cancellation.")
            );
            Schedule(
                new WButtonDisposableEnumerator("throwing", disposed, yieldCount: 0),
                () => throw new InvalidOperationException("Callback failed."),
                exception => Assert.Fail(exception.ToString()),
                () => Assert.Fail("Unexpected cancellation.")
            );
            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: Callback failed\\.")
            );
            Assert.DoesNotThrow(WButtonCoroutineScheduler.Update);
            WButtonCoroutineScheduler.Update();
            Assert.AreEqual(1, otherCompleted);
            CollectionAssert.AreEqual(new[] { "throwing", "other" }, disposed);
        }

        private WButtonCoroutineTicket Schedule(
            IEnumerator routine,
            Action onCompleted,
            Action<Exception> onFaulted,
            Action onCancelled,
            bool withTokenSource = true
        )
        {
            CancellationTokenSource source = null;
            if (withTokenSource)
            {
                source = new CancellationTokenSource();
                _sources.Add(source);
            }
            WButtonCoroutineTicket ticket = WButtonCoroutineScheduler.Schedule(
                routine,
                source,
                onCompleted,
                onFaulted,
                onCancelled
            );
            _tickets.Add(ticket);
            return ticket;
        }
    }
#endif
}
