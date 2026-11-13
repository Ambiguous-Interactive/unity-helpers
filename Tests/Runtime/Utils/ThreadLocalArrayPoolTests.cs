// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;
#if !UNITY_WEBGL && !SINGLE_THREADED
    using System.Threading;
#endif

    [TestFixture]
    [Category("Fast")]
    public sealed class ThreadLocalArrayPoolTests
    {
        [TestCase(1)]
        [TestCase(4)]
        [TestCase(256)]
        public void RentalsHaveExactLengthAndClearAllValuesOnReturn(int length)
        {
            using ThreadLocalArrayPool<int> pool = new(length);
            int[] first = pool.Rent();
            int[] second = pool.Rent();
            Assert.That(pool.Length, Is.EqualTo(length));
            Assert.That(first.Length, Is.EqualTo(length));
            Assert.AreNotSame(first, second);
            for (int index = 0; index < first.Length; ++index)
            {
                first[index] = index + 1;
                second[index] = index + 2;
            }
            pool.Return(first);
            pool.Return(second);
            Assert.AreSame(second, pool.Rent());
            Assert.AreSame(first, pool.Rent());
            CollectionAssert.AreEqual(new int[length], first);
            CollectionAssert.AreEqual(new int[length], second);
        }

        [Test]
        public void ReturnedReferenceArraysReleaseEveryElementEvenWhenStorageIsFull()
        {
            using ThreadLocalArrayPool<object> pool = new(3, 1);
            object[] first = pool.Rent();
            object[] second = pool.Rent();
            for (int index = 0; index < first.Length; ++index)
            {
                first[index] = new object();
                second[index] = new object();
            }
            pool.Return(first);
            pool.Return(second);
            CollectionAssert.AreEqual(new object[3], first);
            CollectionAssert.AreEqual(new object[3], second);
            Assert.AreSame(first, pool.Rent());
            Assert.AreNotSame(second, pool.Rent());
        }

        [TestCase(int.MinValue)]
        [TestCase(-1)]
        [TestCase(0)]
        public void NonpositiveLengthsUseAnInertEmptyLease(int length)
        {
            using ThreadLocalArrayPool<int> pool = new(length);
            Assert.That(pool.Length, Is.Zero);
            Assert.AreSame(Array.Empty<int>(), pool.Rent());
            PooledResource<int[]> lease = pool.Get(out int[] array);
            Assert.AreSame(Array.Empty<int>(), array);
            Assert.AreSame(array, lease.resource);
            pool.Return(array);
            lease.Dispose();
            lease.Dispose();
            pool.ClearCurrentThread();
            Assert.AreSame(array, pool.Rent());
        }

        [TestCase(-1)]
        [TestCase(0)]
        public void NonpositiveRetentionStillClearsArraysWithoutRetainingThem(int retained)
        {
            using ThreadLocalArrayPool<int> pool = new(2, retained);
            int[] array = pool.Rent();
            array[0] = 12;
            array[1] = 24;
            pool.Return(array);
            CollectionAssert.AreEqual(new[] { 0, 0 }, array);
            Assert.AreNotSame(array, pool.Rent());
        }

        [Test]
        public void NullAndWrongLengthsDoNotEnterTheStackOrChangeForeignContents()
        {
            using ThreadLocalArrayPool<int> pool = new(4);
            int[] correct = pool.Rent();
            pool.Return(correct);
            int[] wrong = { 3, 5 };
            pool.Return(wrong);
            pool.Return(null);
            pool.Return(Array.Empty<int>());
            Assert.AreSame(correct, pool.Rent());
            Assert.That(pool.Rent().Length, Is.EqualTo(4));
            CollectionAssert.AreEqual(new[] { 3, 5 }, wrong);
        }

        [Test]
        public void ClearDropsOnlyIdleArraysAndHeldArraysCanStillReturn()
        {
            using ThreadLocalArrayPool<int> pool = new(4);
            pool.ClearCurrentThread();
            int[] idle = pool.Rent();
            int[] held = pool.Rent();
            pool.Return(idle);
            pool.ClearCurrentThread();
            pool.Return(held);
            Assert.AreSame(held, pool.Rent());
            Assert.AreNotSame(idle, pool.Rent());
        }

        [Test]
        public void LeaseCopiesStayInertAfterAnArrayIsRentedAgain()
        {
            using ThreadLocalArrayPool<int> pool = new(4);
            PooledResource<int[]> lease = pool.Get(out int[] array);
            PooledResource<int[]> copy = lease;
            Assert.AreSame(array, lease.resource);
            array[0] = 42;
            lease.Dispose();
            Assert.AreSame(array, pool.Rent());
            Assert.That(array[0], Is.Zero);
            copy.Dispose();
            Assert.AreNotSame(array, pool.Rent());
        }

        [Test]
        public void DisposedOwnerRefusesRentalsAndReturnsAndDisposesRepeatedly()
        {
            ThreadLocalArrayPool<int> pool = new(4);
            int[] array = pool.Rent();
            PooledResource<int[]> outstanding = pool.Get(out int[] leased);
            array[0] = 91;
            leased[0] = 73;
            pool.Dispose();
            pool.Dispose();
            Assert.IsTrue(pool.IsDisposed);
            Assert.IsTrue(pool.Rent() == null);
            pool.Return(array);
            pool.ClearCurrentThread();
            PooledResource<int[]> lease = pool.Get(out int[] failed);
            Assert.IsTrue(failed == null);
            Assert.IsTrue(lease.resource == null);
            lease.Dispose();
            Assert.DoesNotThrow(outstanding.Dispose);
            Assert.That(array[0], Is.EqualTo(91));
            Assert.That(leased[0], Is.EqualTo(73));
        }

#if !UNITY_WEBGL && !SINGLE_THREADED
        [Test]
        public void ThreadsKeepSeparateIdleStacksAndCanTransferExplicitReturns()
        {
            using ThreadLocalArrayPool<int> pool = new(4);
            int[] main = pool.Rent();
            int[] transferred = pool.Rent();
            pool.Return(main);
            int[] workerArray = null;
            transferred[0] = 44;
            Exception failure = null;
            Thread worker = new(() =>
            {
                try
                {
                    workerArray = pool.Rent();
                    Assert.AreNotSame(main, workerArray);
                    pool.Return(workerArray);
                    Assert.AreSame(workerArray, pool.Rent());
                    pool.Return(transferred);
                    Assert.That(transferred[0], Is.Zero);
                    Assert.AreSame(transferred, pool.Rent());
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            worker.IsBackground = true;
            worker.Start();
            Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(10)), "Worker did not stop.");
            Assert.IsTrue(failure == null, failure?.ToString());
            Assert.IsTrue(workerArray != null);
            Assert.AreSame(main, pool.Rent());
            pool.Return(workerArray);
            Assert.AreSame(workerArray, pool.Rent());
        }
#endif
    }
}
