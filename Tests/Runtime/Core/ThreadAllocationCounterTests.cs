// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using NUnit.Framework;

    public sealed class ThreadAllocationCounterTests
    {
        [Test]
        public void CounterPolicyMatchesTheExecutingBackend()
        {
            TestContext.Out.WriteLine(
                $"Thread counter retained-control bytes: {ThreadAllocationCounter.RetainedControlBytes}"
            );
#if ENABLE_IL2CPP && !UNITY_EDITOR
            Assert.IsTrue(ThreadAllocationCounter.RetainedControlBytes == null);
            Assert.IsFalse(ThreadAllocationCounter.IsAvailable);
#endif
            if (!ThreadAllocationCounter.IsAvailable)
            {
                Assert.Throws<IgnoreException>(() => ThreadAllocationCounter.Read());
                return;
            }
            long before = ThreadAllocationCounter.Read();
            byte[] retained = new byte[4096];
            retained[0] = 1;
            long after = ThreadAllocationCounter.Read();
            GC.KeepAlive(retained);
            Assert.LessOrEqual(4096, after - before);
        }

        [Test]
        public void AvailableCounterReadsDoNotAllocate()
        {
            ThreadAllocationCounter.IgnoreWhenUnavailable();
            ThreadAllocationCounter.Read();
            long before = ThreadAllocationCounter.Read();
            long after = ThreadAllocationCounter.Read();
            Assert.AreEqual(before, after);
        }
    }
}
