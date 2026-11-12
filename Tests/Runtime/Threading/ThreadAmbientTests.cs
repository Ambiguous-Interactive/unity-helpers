// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Threading
{
    using System;
    using System.Threading;
    using NUnit.Framework;
    using UnityEngine.TestTools.Constraints;
    using WallstopStudios.UnityHelpers.Core.Threading;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Is = UnityEngine.TestTools.Constraints.Is;

    [TestFixture]
    [Category("Fast")]
    public sealed class ThreadAmbientTests
    {
        private static int _observed;

        [Test]
        public void DefaultScopeIsInactiveAndSafeToDispose()
        {
            ThreadAmbient<int>.Scope scope = default;
            Assert.That(scope.IsCurrent, Is.False);
            scope.Dispose();
            scope.Dispose();
            Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(0));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("context")]
        public void ReferenceValuesIncludingNullAreRestored(string value)
        {
            using (ThreadAmbient<string>.Scope outer = ThreadAmbient<string>.Push("outer"))
            {
                using (ThreadAmbient<string>.Scope inner = ThreadAmbient<string>.Push(value))
                {
                    Assert.That(ThreadAmbient<string>.Current, Is.EqualTo(value));
                    Assert.That(inner.IsCurrent, Is.True);
                    Assert.That(outer.IsCurrent, Is.False);
                }
                Assert.That(outer.IsCurrent, Is.True);
                Assert.That(ThreadAmbient<string>.Current, Is.EqualTo("outer"));
            }
            Assert.That(ThreadAmbient<string>.Current, Is.Null);
        }

        [Test]
        public void CopiedAndRepeatedDisposalCannotOverwriteANewerScope()
        {
            using ThreadAmbient<int>.Scope outer = ThreadAmbient<int>.Push(10);
            ThreadAmbient<int>.Scope copy = outer;
            Assert.That(copy.Equals(outer), Is.True);
            Assert.That(copy.GetHashCode(), Is.EqualTo(outer.GetHashCode()));
            outer.Dispose();
            Assert.That(outer.IsCurrent, Is.False);
            using ThreadAmbient<int>.Scope newer = ThreadAmbient<int>.Push(20);
            copy.Dispose();
            outer.Dispose();
            Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(20));
            Assert.That(newer.Equals(outer), Is.False);
            Assert.That(outer.Equals(default), Is.False);
        }

        [Test]
        public void BoxedCopiesReleaseOnlyTheirOriginalScope()
        {
            using ThreadAmbient<int>.Scope scope = ThreadAmbient<int>.Push(10);
#pragma warning disable WUH022 // Boxing deliberately verifies disposal and equality through object and interface copies.
            IDisposable boxed = scope;
            object copy = scope;
#pragma warning restore WUH022
            Assert.That(scope.Equals(copy), Is.True);
            Assert.That(scope.Equals((object)null), Is.False);
            Assert.That(scope.Equals("different type"), Is.False);
            boxed.Dispose();
            using ThreadAmbient<int>.Scope newer = ThreadAmbient<int>.Push(20);
            boxed.Dispose();
            Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(20));
        }

        [Test]
        public void EarlyDisposalIsIgnoredAndCanBeRetriedAfterTheChild()
        {
            using ThreadAmbient<int>.Scope outer = ThreadAmbient<int>.Push(10);
            using ThreadAmbient<int>.Scope inner = ThreadAmbient<int>.Push(20);
            outer.Dispose();
            Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(20));
            inner.Dispose();
            Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(10));
            outer.Dispose();
            Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(0));
            inner.Dispose();
            Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(0));
        }

        [Test]
        public void RepeatedEqualValuesStillHaveIndependentScopes()
        {
            using ThreadAmbient<int>.Scope outer = ThreadAmbient<int>.Push(7);
            using ThreadAmbient<int>.Scope inner = ThreadAmbient<int>.Push(7);
            Assert.That(inner.Equals(outer), Is.False);
            inner.Dispose();
            Assert.That(outer.IsCurrent, Is.True);
            inner.Dispose();
            Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(7));
        }

        [Test]
        public void ClosedGenericTypesHaveIndependentValues()
        {
            using ThreadAmbient<int>.Scope number = ThreadAmbient<int>.Push(10);
            using ThreadAmbient<string>.Scope text = ThreadAmbient<string>.Push("context");
            Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(10));
            Assert.That(ThreadAmbient<string>.Current, Is.EqualTo("context"));
            number.Dispose();
            Assert.That(ThreadAmbient<string>.Current, Is.EqualTo("context"));
        }

        [Test]
        public void UsingRestoresThePreviousValueWhenTheBodyThrows()
        {
            using ThreadAmbient<int>.Scope outer = ThreadAmbient<int>.Push(10);
            Assert.Throws<InvalidOperationException>(() =>
            {
                using ThreadAmbient<int>.Scope inner = ThreadAmbient<int>.Push(20);
                throw new InvalidOperationException("body failure");
            });
            Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(10));
        }

        [Test]
        public void DeepNestingRestoresEveryPreviousValue()
        {
            ThreadAmbient<int>.Scope[] scopes = new ThreadAmbient<int>.Scope[64];
            try
            {
                for (int index = 0; index < scopes.Length; ++index)
                {
                    scopes[index] = ThreadAmbient<int>.Push(index + 1);
                }
                for (int index = scopes.Length - 1; 0 <= index; --index)
                {
                    Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(index + 1));
                    scopes[index].Dispose();
                    scopes[index].Dispose();
                }
                Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(0));
            }
            finally
            {
                for (int index = scopes.Length - 1; 0 <= index; --index)
                {
                    scopes[index].Dispose();
                }
            }
        }

        [Test]
        public void WarmNestedScopesAllocateNoManagedMemory()
        {
            for (int warm = 0; warm < 16; ++warm)
            {
                using ThreadAmbient<int>.Scope scope = ThreadAmbient<int>.Push(warm);
            }
            AllocationProbe.IgnoreWhenUnmeasurable();
            Assert.That(
                () =>
                {
                    for (int index = 0; index < AllocationProbe.Iterations; ++index)
                    {
                        using ThreadAmbient<int>.Scope outer = ThreadAmbient<int>.Push(index);
                        using ThreadAmbient<int>.Scope inner = ThreadAmbient<int>.Push(-index);
                        _observed += ThreadAmbient<int>.Current;
                    }
                },
                Is.Not.AllocatingGCMemory()
            );
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void ConcurrentThreadsKeepIndependentValuesAndRefuseForeignDisposal(int count)
        {
#if SINGLE_THREADED || (UNITY_WEBGL && !UNITY_EDITOR)
            Assert.Ignore("This player does not support worker threads.");
#else
            using ThreadAmbient<int>.Scope main = ThreadAmbient<int>.Push(77);
            using CountdownEvent ready = new(count);
            using CountdownEvent inside = new(count);
            using ManualResetEventSlim start = new(false);
            using ManualResetEventSlim release = new(false);
            Thread[] workers = new Thread[count];
            Exception failure = null;
            int completed = 0;
            int timeouts = 0;
            for (int index = 0; index < count; ++index)
            {
                int value = index + 1;
                workers[index] = new Thread(() =>
                {
                    try
                    {
                        ready.Signal();
                        if (!start.Wait(5000))
                        {
                            Interlocked.Increment(ref timeouts);
                            return;
                        }
                        Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(0));
                        using (ThreadAmbient<int>.Scope outer = ThreadAmbient<int>.Push(value))
                        {
                            main.Dispose();
                            Assert.That(main.IsCurrent, Is.False);
                            Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(value));
                            inside.Signal();
                            if (!release.Wait(5000))
                            {
                                Interlocked.Increment(ref timeouts);
                                return;
                            }
                            using (ThreadAmbient<int>.Scope inner = ThreadAmbient<int>.Push(-value))
                            {
                                Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(-value));
                            }
                            Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(value));
                        }
                        Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(0));
                        Interlocked.Increment(ref completed);
                    }
                    catch (Exception exception)
                    {
                        Interlocked.CompareExchange(ref failure, exception, null);
                    }
                });
                workers[index].IsBackground = true;
                workers[index].Start();
            }
            bool allReady = ready.Wait(5000);
            start.Set();
            bool allInside = inside.Wait(5000);
            release.Set();
            bool joined = true;
            foreach (Thread worker in workers)
            {
                bool workerJoined = worker.Join(5000);
                joined = joined && workerJoined;
            }
            Assert.That(allReady && allInside && joined, Is.True);
            Assert.That(failure, Is.Null);
            Assert.That(timeouts, Is.EqualTo(0));
            Assert.That(completed, Is.EqualTo(count));
            Assert.That(ThreadAmbient<int>.Current, Is.EqualTo(77));
#endif
        }
    }
}
