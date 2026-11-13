// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class LocalPoolTests
    {
#if UNITY_5_3_OR_NEWER
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void WarmPoolOperationsDoNotAllocateWhenTheProfilerIsObservable(int shape)
        {
            const int calls = 128;
            const int capacity = 512;
            object[] retained = new object[1];
            object preboxed = 42;
            Action allocate = () => retained[0] = (object)42;
            Action reuse = () => retained[0] = preboxed;
            long positive =
                WallstopStudios.UnityHelpers.Tests.Core.ProfilerAllocationCounter.Measure(
                    allocate,
                    calls,
                    capacity
                );
            if (positive == 0)
            {
                Assert.Ignore("The allocation recorder did not observe retained boxes.");
            }
            Assert.That(positive, Is.EqualTo(calls));
            Assert.That(
                WallstopStudios.UnityHelpers.Tests.Core.ProfilerAllocationCounter.Measure(
                    reuse,
                    calls,
                    capacity
                ),
                Is.Zero
            );
            LocalPool<object> pool = new(static () => new object());
            using ThreadLocalArrayPool<int> arrays = new(64);
            Action operation = shape switch
            {
                0 => () => pool.Return(pool.Rent()),
                1 => () =>
                {
                    using PooledResource<object> lease = pool.Get(out _);
                },
                2 => () => arrays.Return(arrays.Rent()),
                _ => () =>
                {
                    using PooledResource<int[]> lease = arrays.Get(out _);
                },
            };
            for (int trial = 0; trial < 3; ++trial)
            {
                Assert.That(
                    WallstopStudios.UnityHelpers.Tests.Core.ProfilerAllocationCounter.Measure(
                        operation,
                        calls,
                        capacity
                    ),
                    Is.Zero
                );
            }
            GC.KeepAlive(retained);
        }
#endif

        [Test]
        public void NestedRentalsAreDistinctAndReturnedInStackOrder()
        {
            int produced = 0;
            LocalPool<List<int>> pool = new(
                () =>
                {
                    ++produced;
                    return new List<int>();
                },
                static list => list.Clear()
            );
            List<int> first = pool.Rent();
            List<int> second = pool.Rent();
            Assert.AreNotSame(first, second);
            Assert.That(pool.CountInactive, Is.Zero);
            first.Add(1);
            second.Add(2);
            pool.Return(first);
            pool.Return(second);
            Assert.That(pool.CountInactive, Is.EqualTo(2));
            Assert.AreSame(second, pool.Rent());
            Assert.AreSame(first, pool.Rent());
            CollectionAssert.IsEmpty(first);
            CollectionAssert.IsEmpty(second);
            Assert.That(produced, Is.EqualTo(2));
        }

        [TestCase(int.MinValue, 0)]
        [TestCase(-1, 0)]
        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(64, 3)]
        public void IdleLimitIsBoundedAndEveryReturnIsReset(int limit, int retained)
        {
            int resets = 0;
            LocalPool<object> pool = new(static () => new object(), _ => ++resets, limit);
            object[] items = { pool.Rent(), pool.Rent(), pool.Rent() };
            foreach (object item in items)
            {
                pool.Return(item);
            }
            Assert.That(pool.MaxRetained, Is.EqualTo(Math.Max(0, limit)));
            Assert.That(pool.CountInactive, Is.EqualTo(retained));
            Assert.That(resets, Is.EqualTo(3));
            pool.Return(null);
            Assert.That(resets, Is.EqualTo(3));
            pool.Clear();
            Assert.That(pool.CountInactive, Is.Zero);
        }

        [Test]
        public void MissingOrNullProductionHasAnInertLease()
        {
            foreach (Func<object> producer in new Func<object>[] { null, static () => null })
            {
                LocalPool<object> pool = new(producer);
                Assert.IsTrue(pool.Rent() == null);
                PooledResource<object> lease = pool.Get(out object item);
                Assert.IsTrue(item == null);
                Assert.IsTrue(lease.resource == null);
                lease.Dispose();
                Assert.That(pool.CountInactive, Is.Zero);
            }
        }

        [Test]
        public void LeaseCopiesCannotReturnAnObjectAgainAfterRerent()
        {
            LocalPool<object> pool = new(static () => new object());
            PooledResource<object> lease = pool.Get(out object first);
            PooledResource<object> copy = lease;
            Assert.AreSame(first, lease.resource);
            lease.Dispose();
            Assert.AreSame(first, pool.Rent());
            copy.Dispose();
            Assert.That(pool.CountInactive, Is.Zero);
            object second = pool.Rent();
            Assert.AreNotSame(first, second);
            pool.Return(first);
            pool.Return(second);
            Assert.That(pool.CountInactive, Is.EqualTo(2));
        }

        [Test]
        public void ClearDoesNotInvalidateAnOutstandingRental()
        {
            LocalPool<object> pool = new(static () => new object());
            object idle = pool.Rent();
            object held = pool.Rent();
            pool.Return(idle);
            pool.Clear();
            pool.Return(held);
            Assert.AreSame(held, pool.Rent());
            Assert.AreNotSame(idle, pool.Rent());
        }

        [Test]
        public void ProducerFailureIsLoggedAndTheNextRentalCanRecover()
        {
            bool fail = true;
            LocalPool<object> pool = new(() =>
                fail
                    ? throw new InvalidOperationException(
                        nameof(ProducerFailureIsLoggedAndTheNextRentalCanRecover)
                    )
                    : new object()
            );
            LogAssert.Expect(
                LogType.Exception,
                new Regex(nameof(ProducerFailureIsLoggedAndTheNextRentalCanRecover))
            );
            Assert.IsTrue(pool.Rent() == null);
            Assert.That(pool.CountInactive, Is.Zero);
            fail = false;
            Assert.IsTrue(pool.Rent() != null);
        }

        [Test]
        public void ResetFailureDiscardsTheObjectAndRestoresCallbackState()
        {
            bool fail = true;
            LocalPool<object> pool = new(
                static () => new object(),
                _ =>
                {
                    if (fail)
                    {
                        throw new InvalidOperationException(
                            nameof(ResetFailureDiscardsTheObjectAndRestoresCallbackState)
                        );
                    }
                }
            );
            object failed = pool.Rent();
            LogAssert.Expect(
                LogType.Exception,
                new Regex(nameof(ResetFailureDiscardsTheObjectAndRestoresCallbackState))
            );
            pool.Return(failed);
            Assert.That(pool.CountInactive, Is.Zero);
            fail = false;
            object next = pool.Rent();
            Assert.AreNotSame(failed, next);
            pool.Return(next);
            Assert.AreSame(next, pool.Rent());
        }

        [Test]
        public void RecursiveProducerCannotRentReturnOrClearThePool()
        {
            LocalPool<object> pool = null;
            object foreign = new();
            pool = new LocalPool<object>(() =>
            {
                Assert.IsTrue(pool.Rent() == null);
                pool.Return(foreign);
                pool.Clear();
                return new object();
            });
            object item = pool.Rent();
            Assert.IsTrue(item != null);
            Assert.AreNotSame(foreign, item);
            Assert.That(pool.CountInactive, Is.Zero);
            pool.Return(item);
            Assert.AreSame(item, pool.Rent());
        }

        [Test]
        public void RecursiveResetCannotPublishOrTakeTheReturningObject()
        {
            LocalPool<object> pool = null;
            object nested = new();
            pool = new LocalPool<object>(
                static () => new object(),
                item =>
                {
                    Assert.IsTrue(pool.Rent() == null);
                    pool.Return(item);
                    pool.Return(nested);
                    pool.Clear();
                }
            );
            object first = pool.Rent();
            object second = pool.Rent();
            pool.Return(first);
            pool.Return(second);
            Assert.That(pool.CountInactive, Is.EqualTo(2));
            Assert.AreSame(second, pool.Rent());
            Assert.AreSame(first, pool.Rent());
            Assert.AreNotSame(nested, pool.Rent());
        }
    }
}
