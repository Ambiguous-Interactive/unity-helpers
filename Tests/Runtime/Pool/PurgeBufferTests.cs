// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class PurgeBufferTests
    {
        private readonly List<object>[] _held = new List<object>[
            PurgeBuffer<object>.MaximumRetainedBuffers
        ];

        [SetUp]
        public void SetUp()
        {
            for (int i = 0; i < _held.Length; ++i)
            {
                _held[i] = PurgeBuffer<object>.Rent();
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _held.Length; ++i)
            {
                PurgeBuffer<object>.Return(_held[i]);
                _held[i] = null;
            }
        }

        [Test]
        public void DefaultAndNullLeaseDoNotReturnAnOutstandingSnapshot()
        {
            using PurgeBufferLease<object> owner = PurgeBuffer<object>.Get(out List<object> buffer);
            object item = new();
            buffer.Add(item);
            PurgeBufferLease<object> empty = default;
            empty.Dispose();
            new PurgeBufferLease<object>(null).Dispose();
            using PurgeBufferLease<object> other = PurgeBuffer<object>.Get(
                out List<object> otherBuffer
            );
            Assert.AreNotSame(buffer, otherBuffer);
            Assert.AreEqual(1, buffer.Count);
            Assert.AreSame(item, buffer[0]);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CopiedLeaseReturnsOnceAndCannotRetireANewOwner(bool disposeCopyFirst)
        {
            PurgeBufferLease<object> original = PurgeBuffer<object>.Get(out List<object> buffer);
            PurgeBufferLease<object> copy = original;
            buffer.Add(new object());
            if (disposeCopyFirst)
            {
                copy.Dispose();
            }
            else
            {
                original.Dispose();
            }
            Assert.IsEmpty(buffer);
            using PurgeBufferLease<object> current = PurgeBuffer<object>.Get(
                out List<object> currentBuffer
            );
            Assert.AreSame(buffer, currentBuffer);
            object item = new();
            currentBuffer.Add(item);
            original.Dispose();
            copy.Dispose();
            original.Dispose();
            using PurgeBufferLease<object> nested = PurgeBuffer<object>.Get(
                out List<object> nestedBuffer
            );
            Assert.AreNotSame(currentBuffer, nestedBuffer);
            Assert.AreEqual(1, currentBuffer.Count);
            Assert.AreSame(item, currentBuffer[0]);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UsingLeaseReturnsSnapshotOnEarlyReturnAndException(bool throwFromBody)
        {
            List<object> returned = null;
            void LeaveScope()
            {
                using PurgeBufferLease<object> owner = PurgeBuffer<object>.Get(
                    out List<object> buffer
                );
                returned = buffer;
                buffer.Add(new object());
                if (throwFromBody)
                {
                    throw new InvalidOperationException(
                        nameof(UsingLeaseReturnsSnapshotOnEarlyReturnAndException)
                    );
                }
                return;
            }

            if (throwFromBody)
            {
                Assert.Throws<InvalidOperationException>(LeaveScope);
            }
            else
            {
                LeaveScope();
            }
            Assert.IsTrue(returned != null);
            Assert.IsEmpty(returned);
            using PurgeBufferLease<object> reused = PurgeBuffer<object>.Get(
                out List<object> reusedBuffer
            );
            Assert.AreSame(returned, reusedBuffer);
            Assert.IsEmpty(reusedBuffer);
        }

        [Test]
        public void NestedRentKeepsOuterSnapshotExclusiveAndReusesBothBuffers()
        {
            List<object> outer = PurgeBuffer<object>.Rent();
            List<object> inner = PurgeBuffer<object>.Rent();
            object outerItem = new();
            object innerItem = new();
            outer.Add(outerItem);
            inner.Add(innerItem);
            Assert.AreNotSame(outer, inner);
            PurgeBuffer<object>.Return(inner);
            Assert.AreSame(outerItem, outer[0]);
            PurgeBuffer<object>.Return(outer);
            Assert.IsEmpty(outer);
            Assert.IsEmpty(inner);

            List<object> nextOuter = PurgeBuffer<object>.Rent();
            List<object> nextInner = PurgeBuffer<object>.Rent();
            try
            {
                Assert.AreSame(outer, nextOuter);
                Assert.AreSame(inner, nextInner);
                Assert.AreNotSame(nextOuter, nextInner);
                Assert.IsEmpty(nextOuter);
                Assert.IsEmpty(nextInner);
            }
            finally
            {
                PurgeBuffer<object>.Return(nextInner);
                PurgeBuffer<object>.Return(nextOuter);
            }
        }

        [TestCase(0)]
        [TestCase(4096)]
        [TestCase(4097)]
        public void ReturnClearsBufferAndRetainsOnlyBoundedCapacity(int capacity)
        {
            List<object> original = PurgeBuffer<object>.Rent();
            original.Capacity = capacity;
            original.Add(new object());
            PurgeBuffer<object>.Return(original);
            Assert.IsEmpty(original);
            List<object> next = PurgeBuffer<object>.Rent();
            try
            {
                if (capacity <= PurgeBuffer<object>.MaximumRetainedCapacity)
                {
                    Assert.AreSame(original, next);
                }
                else
                {
                    Assert.AreNotSame(original, next);
                }
                Assert.IsEmpty(next);
            }
            finally
            {
                PurgeBuffer<object>.Return(next);
            }
        }

        [Test]
        public void ReturnLimitsFreeBuffersWithoutReusingOutstandingOwners()
        {
            int count = PurgeBuffer<object>.MaximumRetainedBuffers + 1;
            List<object>[] original = new List<object>[count];
            for (int i = 0; i < count; ++i)
            {
                original[i] = PurgeBuffer<object>.Rent();
                original[i].Add(new object());
            }
            for (int i = 0; i < count; ++i)
            {
                PurgeBuffer<object>.Return(original[i]);
                Assert.IsEmpty(original[i]);
            }
            List<object>[] next = new List<object>[count];
            try
            {
                for (int i = 0; i < count; ++i)
                {
                    next[i] = PurgeBuffer<object>.Rent();
                    Assert.IsEmpty(next[i]);
                    if (i < PurgeBuffer<object>.MaximumRetainedBuffers)
                    {
                        Assert.AreSame(
                            original[PurgeBuffer<object>.MaximumRetainedBuffers - i - 1],
                            next[i]
                        );
                    }
                    else
                    {
                        for (int originalIndex = 0; originalIndex < count; ++originalIndex)
                        {
                            Assert.AreNotSame(original[originalIndex], next[i]);
                        }
                    }
                    for (int earlier = 0; earlier < i; ++earlier)
                    {
                        Assert.AreNotSame(next[earlier], next[i]);
                    }
                }
            }
            finally
            {
                for (int i = 0; i < count; ++i)
                {
                    PurgeBuffer<object>.Return(next[i]);
                }
            }
        }

        [Test]
        public void ReturnNullDoesNotChangeAvailableBuffer()
        {
            List<object> original = PurgeBuffer<object>.Rent();
            PurgeBuffer<object>.Return(original);
            Assert.DoesNotThrow(() => PurgeBuffer<object>.Return(null));
            List<object> next = PurgeBuffer<object>.Rent();
            try
            {
                Assert.AreSame(original, next);
            }
            finally
            {
                PurgeBuffer<object>.Return(next);
            }
        }

        [Test]
        public void ScratchBuffersRemainIsolatedAcrossThreads()
        {
            List<object> mainBuffer = PurgeBuffer<object>.Rent();
            PurgeBuffer<object>.Return(mainBuffer);
            List<object> workerBuffer = null;
            List<object> workerReused = null;
            Exception workerFailure = null;
            Thread worker = new(() =>
            {
                try
                {
                    workerBuffer = PurgeBuffer<object>.Rent();
                    workerBuffer.Add(new object());
                    PurgeBuffer<object>.Return(workerBuffer);
                    workerReused = PurgeBuffer<object>.Rent();
                    PurgeBuffer<object>.Return(workerReused);
                }
                catch (Exception exception)
                {
                    workerFailure = exception;
                }
            })
            {
                IsBackground = true,
            };
            worker.Start();
            Assert.IsTrue(
                worker.Join(TimeSpan.FromSeconds(5)),
                "Worker must finish its pure managed operations."
            );
            Assert.IsTrue(workerFailure == null);
            Assert.AreNotSame(mainBuffer, workerBuffer);
            Assert.AreSame(workerBuffer, workerReused);
            Assert.IsEmpty(workerReused);
            List<object> mainReused = PurgeBuffer<object>.Rent();
            try
            {
                Assert.AreSame(mainBuffer, mainReused);
            }
            finally
            {
                PurgeBuffer<object>.Return(mainReused);
            }
        }
    }
}
