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
