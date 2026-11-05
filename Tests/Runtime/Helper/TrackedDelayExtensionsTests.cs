// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class TrackedDelayExtensionsTests : CommonTestBase
    {
        private CoroutineHost _host;
        private readonly List<Coroutine> _pending = new();

        [SetUp]
        public void CreateOwner()
        {
            GameObject owner = Track(
                new GameObject(nameof(TrackedDelayExtensionsTests), typeof(CoroutineHost))
            );
            _host = owner.GetComponent<CoroutineHost>();
        }

        [TearDown]
        public void StopPendingDelays()
        {
            _host.StopTrackedDelays(_pending);
        }

        [Test]
        public void InvalidInputsDoNotScheduleOrInvokeCallbacks()
        {
            Assert.IsFalse(
                ((MonoBehaviour)null).TryExecuteTrackedAfterDelay(_pending, _host.Increment, 0f)
            );
            Assert.IsFalse(_host.TryExecuteTrackedAfterDelay(null, _host.Increment, 0f));
            Assert.IsFalse(_host.TryExecuteTrackedAfterDelay(_pending, null, 0f));
            _host.gameObject.SetActive(false);
            Assert.IsFalse(_host.TryExecuteTrackedAfterDelay(_pending, _host.Increment, 0f));
            _host.ExecuteTrackedAfterDelay(_pending, _host.Increment, 0f);
            Assert.That(_host.InvocationCount, Is.Zero);
            Assert.That(_pending, Is.Empty);
        }

        [UnityTest]
        public IEnumerator NonpositiveAndNonfiniteDelaysCompleteWithoutRetainingHandles()
        {
            foreach (
                float delay in new[]
                {
                    0f,
                    -1f,
                    float.NaN,
                    float.NegativeInfinity,
                    float.PositiveInfinity,
                }
            )
            {
                Assert.IsTrue(_host.TryExecuteTrackedAfterDelay(_pending, _host.Increment, delay));
            }

            Assert.That(_host.InvocationCount, Is.Zero);
            Assert.That(_pending.Count, Is.EqualTo(5));
            for (int frame = 0; frame < 30 && _host.InvocationCount < 5; ++frame)
            {
                yield return null;
            }
            Assert.That(_host.InvocationCount, Is.EqualTo(5));
            Assert.That(_pending, Is.Empty);
        }

        [UnityTest]
        public IEnumerator PendingHandleIsRemovedBeforeCallbackAndOnlyRunsOnce()
        {
            int observedPending = -1;
            Assert.IsTrue(
                _host.TryExecuteTrackedAfterDelay(
                    _pending,
                    () =>
                    {
                        observedPending = _pending.Count;
                        _host.Increment();
                    },
                    float.Epsilon
                )
            );
            Assert.That(_pending.Count, Is.EqualTo(1));
            for (int frame = 0; frame < 30 && _host.InvocationCount == 0; ++frame)
            {
                yield return null;
            }

            Assert.That(_host.InvocationCount, Is.EqualTo(1));
            Assert.That(observedPending, Is.Zero);
            Assert.That(_pending, Is.Empty);
            yield return null;
            Assert.That(_host.InvocationCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CancellationClearsAllHandlesAndPreventsCallbacks()
        {
            _host.ExecuteTrackedAfterDelay(_pending, _host.Increment, float.Epsilon);
            _host.ExecuteTrackedAfterDelay(_pending, _host.Increment, float.Epsilon);
            _pending.Add(null);
            Assert.That(_pending.Count, Is.EqualTo(3));
            _host.StopTrackedDelays(_pending);
            _host.StopTrackedDelays(_pending);
            Assert.That(_pending, Is.Empty);
            yield return null;
            yield return null;
            Assert.That(_host.InvocationCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CallbackCanScheduleANewTrackedDelay()
        {
            bool invoked = false;
            _host.ExecuteTrackedAfterDelay(
                _pending,
                () =>
                {
                    invoked = true;
                    Assert.That(_pending, Is.Empty);
                    _host.ExecuteTrackedAfterDelay(_pending, _host.Increment, 1000f);
                },
                float.Epsilon
            );
            for (int frame = 0; frame < 30 && !invoked; ++frame)
            {
                yield return null;
            }

            Assert.IsTrue(invoked);
            Assert.That(_pending.Count, Is.EqualTo(1));
            Assert.That(_host.InvocationCount, Is.Zero);
            _host.StopTrackedDelays(_pending);
            Assert.That(_pending, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ThrowingCallbackStillRemovesItsHandle()
        {
            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: Controlled tracked delay failure")
            );
            bool invoked = false;
            _host.ExecuteTrackedAfterDelay(
                _pending,
                () =>
                {
                    invoked = true;
                    Assert.That(_pending, Is.Empty);
                    throw new InvalidOperationException("Controlled tracked delay failure");
                },
                float.Epsilon
            );
            for (int frame = 0; frame < 30 && !invoked; ++frame)
            {
                yield return null;
            }

            Assert.IsTrue(invoked);
            Assert.That(_pending, Is.Empty);
        }

        [Test]
        public void MissingOrDestroyedOwnerStillForgetsHandles()
        {
            _pending.Add(null);
            ((MonoBehaviour)null).StopTrackedDelays(_pending);
            Assert.That(_pending, Is.Empty);
            CoroutineHost destroyed = _host;
            DestroyTrackedObjects();
            Assert.IsTrue(destroyed == null);
            _pending.Add(null);
            destroyed.StopTrackedDelays(_pending);
            destroyed.StopTrackedDelays(null);
            Assert.That(_pending, Is.Empty);
        }
    }
}
