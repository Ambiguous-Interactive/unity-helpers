// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Helper;
    using Object = UnityEngine.Object;

    [TestFixture]
    [Category("Fast")]
    public sealed class HoldSetTests : CommonTestBase
    {
        [Test]
        public void IndependentHoldsReportOnlyOccupancyEdges()
        {
            List<string> edges = new();
            using HoldSet set = new(() => edges.Add("first"), () => edges.Add("last"));
            object owner = new();
            HoldSet.Handle first = set.Acquire(owner);
            HoldSet.Handle second = set.Acquire(owner);
            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(set.IsHeld, Is.True);
            second.Dispose();
            CollectionAssert.AreEqual(new[] { "first" }, edges);
            first.Dispose();
            Assert.That(set.Count, Is.Zero);
            Assert.That(set.IsHeld, Is.False);
            CollectionAssert.AreEqual(new[] { "first", "last" }, edges);
        }

        [Test]
        public void CopiedBoxedAndDefaultHandlesReleaseOnce()
        {
            int releases = 0;
            using HoldSet set = new(onLastReleased: () => ++releases);
            HoldSet.Handle first = set.Acquire(new object());
            HoldSet.Handle copy = first;
#pragma warning disable WUH022 // A boxed copy must preserve once-only release ownership.
            IDisposable boxed = copy;
#pragma warning restore WUH022
            boxed.Dispose();
            first.Dispose();
            copy.Dispose();
            default(HoldSet.Handle).Dispose();
            Assert.That(first.IsHeld, Is.False);
            Assert.That(default(HoldSet.Handle).IsHeld, Is.False);
            Assert.That(releases, Is.EqualTo(1));
        }

        [Test]
        public void ClearInvalidatesOldHandlesWithoutReleasingNewAcquisitions()
        {
            using HoldSet set = new();
            HoldSet.Handle previous = set.Acquire(new object());
            set.Clear();
            HoldSet.Handle next = set.Acquire(new object());
            previous.Dispose();
            Assert.That(previous.IsHeld, Is.False);
            Assert.That(next.IsHeld, Is.True);
            Assert.That(set.Count, Is.EqualTo(1));
        }

        [Test]
        public void DisposalIsPermanentAndReportsTheLastReleaseOnce()
        {
            int releases = 0;
            HoldSet set = new(onLastReleased: () => ++releases);
            HoldSet.Handle handle = set.Acquire(new object());
            set.Dispose();
            set.Dispose();
            set.Clear();
            handle.Dispose();
            Assert.That(set.IsDisposed, Is.True);
            Assert.That(set.Acquire(new object()).IsHeld, Is.False);
            Assert.That(releases, Is.EqualTo(1));
        }

        [Test]
        public void NullAndDestroyedOwnersCannotAcquire()
        {
            using HoldSet set = new();
            GameObject owner = Track(new GameObject(nameof(NullAndDestroyedOwnersCannotAcquire)));
            Object.DestroyImmediate(owner); // UNH-SUPPRESS: exercise a destroyed owner passed through object.
            object boxedOwner = owner;
            Assert.That(set.Acquire(null).IsHeld, Is.False);
            Assert.That(set.Acquire(boxedOwner).IsHeld, Is.False);
            Assert.That(set.Count, Is.Zero);
        }

        [Test]
        public void SweepReleasesOnlyDestroyedUnityOwners()
        {
            int releases = 0;
            using HoldSet set = new(onLastReleased: () => ++releases);
            GameObject destroyed = Track(new GameObject("destroyed"));
            GameObject alive = Track(new GameObject("alive"));
            HoldSet.Handle deadFirst = set.Acquire(destroyed);
            HoldSet.Handle deadSecond = set.Acquire(destroyed);
            HoldSet.Handle live = set.Acquire(alive);
            HoldSet.Handle ordinary = set.Acquire(new object());
            Assert.That(set.SweepDestroyedOwners(), Is.Zero);
            Object.DestroyImmediate(destroyed); // UNH-SUPPRESS: sweep must recognize destroyed wrappers.
            Assert.That(set.Count, Is.EqualTo(4));
            Assert.That(set.SweepDestroyedOwners(), Is.EqualTo(2));
            Assert.That(set.SweepDestroyedOwners(), Is.Zero);
            Assert.That(deadFirst.IsHeld || deadSecond.IsHeld, Is.False);
            Assert.That(live.IsHeld && ordinary.IsHeld, Is.True);
            Assert.That(releases, Is.Zero);
            live.Dispose();
            ordinary.Dispose();
            Assert.That(releases, Is.EqualTo(1));
        }

        [Test]
        public void SweepCommitsAllRemovalsBeforeReentrantAcquisition()
        {
            HoldSet set = null;
            HoldSet.Handle replacement = default;
            int releases = 0;
            set = new HoldSet(onLastReleased: () =>
            {
                ++releases;
                Assert.That(set.Count, Is.Zero);
                replacement = set.Acquire(new object());
                Assert.That(set.SweepDestroyedOwners(), Is.Zero);
            });
            GameObject owner = Track(new GameObject("owner"));
            set.Acquire(owner);
            set.Acquire(owner);
            Object.DestroyImmediate(owner); // UNH-SUPPRESS: both acquisitions must be removed before the callback.
            Assert.That(set.SweepDestroyedOwners(), Is.EqualTo(2));
            Assert.That(replacement.IsHeld, Is.True);
            Assert.That(set.Count, Is.EqualTo(1));
            Assert.That(releases, Is.EqualTo(1));
            set.Dispose();
        }

        [Test]
        public void FirstCallbackMayClearOrDisposeBeforeAcquireReturns()
        {
            HoldSet cleared = null;
            cleared = new HoldSet(() => cleared.Clear());
            Assert.That(cleared.Acquire(new object()).IsHeld, Is.False);
            Assert.That(cleared.Count, Is.Zero);
            HoldSet disposed = null;
            disposed = new HoldSet(() => disposed.Dispose());
            Assert.That(disposed.Acquire(new object()).IsHeld, Is.False);
            Assert.That(disposed.IsDisposed, Is.True);
        }

        [Test]
        public void FirstCallbackMayAcquireAnotherIndependentHold()
        {
            HoldSet set = null;
            HoldSet.Handle nested = default;
            int acquisitions = 0;
            set = new HoldSet(() =>
            {
                ++acquisitions;
                Assert.That(set.Count, Is.EqualTo(1));
                nested = set.Acquire(new object());
            });
            HoldSet.Handle first = set.Acquire(new object());
            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(acquisitions, Is.EqualTo(1));
            first.Dispose();
            Assert.That(nested.IsHeld, Is.True);
            set.Dispose();
        }

        [Test]
        public void FirstCallbackFailureCannotLeakAnUnreturnedHold()
        {
            int releases = 0;
            using HoldSet set = new(
                () => throw new InvalidOperationException("acquire"),
                () => ++releases
            );
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: acquire"));
            Assert.That(set.Acquire(new object()).IsHeld, Is.False);
            Assert.That(set.Count, Is.Zero);
            Assert.That(releases, Is.EqualTo(1));
        }

        [Test]
        public void LastCallbackFailureStillCommitsReleaseAndDisposal()
        {
            HoldSet set = new(onLastReleased: () => throw new InvalidOperationException("release"));
            HoldSet.Handle handle = set.Acquire(new object());
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: release"));
            Assert.DoesNotThrow(() => handle.Dispose());
            Assert.That(handle.IsHeld, Is.False);
            Assert.DoesNotThrow(() => handle.Dispose());
            set.Acquire(new object());
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: release"));
            Assert.DoesNotThrow(() => set.Dispose());
            Assert.That(set.IsDisposed, Is.True);
            Assert.That(set.Count, Is.Zero);
            Assert.DoesNotThrow(() => set.Dispose());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CallbackAndLogHandlerFailuresCannotEscapeOrLeak(bool failOnAcquire)
        {
            Action callback = () => throw new InvalidOperationException("callback failure");
            using HoldSet set = failOnAcquire
                ? new HoldSet(onFirstAcquired: callback)
                : new HoldSet(onLastReleased: callback);
            ILogHandler original = Debug.unityLogger.logHandler;
            RefusingJobLogHandler refusing = new();
            object owner = new();
            try
            {
                Debug.unityLogger.logHandler = refusing;
                if (failOnAcquire)
                {
                    HoldSet.Handle handle = default;
                    Assert.DoesNotThrow(() => handle = set.Acquire(owner));
                    Assert.That(handle.IsHeld, Is.False);
                }
                else
                {
                    HoldSet.Handle handle = set.Acquire(owner);
                    Assert.DoesNotThrow(() => set.Dispose());
                    Assert.That(set.IsDisposed, Is.True);
                    Assert.That(handle.IsHeld, Is.False);
                }
                Assert.That(set.Count, Is.Zero);
                Assert.That(refusing.Attempts, Is.EqualTo(1));
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
            }
        }

        [Test]
        public void FailedFirstCallbackRollsBackOnlyItsOwnAcquisition()
        {
            HoldSet set = null;
            HoldSet.Handle nested = default;
            int releases = 0;
            set = new HoldSet(
                () =>
                {
                    nested = set.Acquire(new object());
                    throw new InvalidOperationException("acquire");
                },
                () => ++releases
            );
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: acquire"));
            HoldSet.Handle refused = set.Acquire(new object());
            Assert.That(refused.IsHeld, Is.False);
            Assert.That(nested.IsHeld, Is.True);
            Assert.That(set.Count, Is.EqualTo(1));
            Assert.That(releases, Is.Zero);
            nested.Dispose();
            Assert.That(releases, Is.EqualTo(1));
            set.Dispose();
        }

        [Test]
        public void SweepCallbackFailureCannotLeaveDestroyedHoldsOutstanding()
        {
            using HoldSet set = new(onLastReleased: () =>
                throw new InvalidOperationException("sweep")
            );
            GameObject owner = Track(new GameObject("owner"));
            HoldSet.Handle first = set.Acquire(owner);
            HoldSet.Handle second = set.Acquire(owner);
            Object.DestroyImmediate(owner); // UNH-SUPPRESS: exercise final-release failure during an owner sweep.
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: sweep"));
            Assert.That(set.SweepDestroyedOwners(), Is.EqualTo(2));
            Assert.That(first.IsHeld || second.IsHeld, Is.False);
            Assert.That(set.Count, Is.Zero);
            Assert.That(set.SweepDestroyedOwners(), Is.Zero);
        }

        [Test]
        public void HandlesCompareBySetAndAcquisitionRatherThanOwner()
        {
            using HoldSet firstSet = new();
            using HoldSet secondSet = new();
            object owner = new();
            HoldSet.Handle first = firstSet.Acquire(owner);
            HoldSet.Handle copy = first;
            HoldSet.Handle second = firstSet.Acquire(owner);
            HoldSet.Handle foreign = secondSet.Acquire(owner);
            Assert.That(first.Equals(copy), Is.True);
            Assert.That(first.GetHashCode(), Is.EqualTo(copy.GetHashCode()));
#pragma warning disable WUH022 // Exercise object equality through its actual boxed boundary.
            Assert.That(first.Equals((object)copy), Is.True);
#pragma warning restore WUH022
            Assert.That(first.Equals(second) || first.Equals(foreign), Is.False);
            Assert.That(first.Equals(null), Is.False);
            Assert.That(default(HoldSet.Handle).Equals(default(HoldSet.Handle)), Is.True);
        }
    }
}
