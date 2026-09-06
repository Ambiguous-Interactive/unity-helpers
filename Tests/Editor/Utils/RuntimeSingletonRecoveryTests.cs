// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RuntimeSingletonRecoveryTests : CommonTestBase
    {
        [SetUp]
        public void PrepareSingletons()
        {
            UnityMainThreadGuard.Capture(Thread.CurrentThread);
            RuntimeSingletonRegistry.PrepareForSceneLoadForTesting();
        }

        [TearDown]
        public void ClearSingletons()
        {
            foreach (
                RuntimeCleanupSingleton instance in UnityObjectExtensions.FindObjectsOfTypeShim<RuntimeCleanupSingleton>(
                    true
                )
            )
            {
                instance.disabling = null;
                instance.destroying = null;
            }
            foreach (
                RuntimeCleanupPartnerSingleton instance in UnityObjectExtensions.FindObjectsOfTypeShim<RuntimeCleanupPartnerSingleton>(
                    true
                )
            )
            {
                instance.disabling = null;
            }
            RuntimeCleanupSingleton.ClearInstance();
            RuntimeCleanupPartnerSingleton.ClearInstance();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClearRejectsWorkerBeforeDiscoveryOrMutation(bool cached)
        {
            RuntimeCleanupSingleton instance = cached ? RuntimeCleanupSingleton.Instance : null;
            if (instance != null)
            {
                Track(instance.gameObject);
            }
            long count = RuntimeCleanupSingleton.InitializeCount;
            Exception exception = Task.Run(() =>
                {
                    try
                    {
                        RuntimeCleanupSingleton.ClearInstance();
                        return null;
                    }
                    catch (Exception failure)
                    {
                        return failure;
                    }
                })
                .GetAwaiter()
                .GetResult();

            Assert.IsInstanceOf<InvalidOperationException>(exception);
            StringAssert.Contains("must be accessed on Unity's main thread", exception.Message);
            Assert.AreEqual(cached, RuntimeCleanupSingleton.HasInstance);
            Assert.AreEqual(count, RuntimeCleanupSingleton.InitializeCount);
            if (cached)
            {
                Assert.AreSame(instance, RuntimeCleanupSingleton.Instance);
            }
        }

        [Test]
        public void SynchronousCleanupCallbacksCannotCreateReplacementSingletons()
        {
            RuntimeCleanupSingleton instance = RuntimeCleanupSingleton.Instance;
            Track(instance.gameObject);
            bool callbackRan = false;
            RuntimeCleanupSingleton selfDuringClear = instance;
            RuntimeCleanupPartnerSingleton partnerDuringClear = null;
            instance.disabling = () =>
            {
                callbackRan = true;
                selfDuringClear = RuntimeCleanupSingleton.Instance;
                partnerDuringClear = RuntimeCleanupPartnerSingleton.Instance;
            };

            RuntimeCleanupSingleton.ClearInstance();

            Assert.IsTrue(callbackRan);
            Assert.IsTrue(ReferenceEquals(selfDuringClear, null));
            Assert.IsTrue(ReferenceEquals(partnerDuringClear, null));
            Assert.IsFalse(RuntimeCleanupSingleton.HasInstance);
            Assert.IsFalse(RuntimeCleanupPartnerSingleton.HasInstance);
            RuntimeCleanupSingleton replacement = RuntimeCleanupSingleton.Instance;
            Track(replacement.gameObject);
            Assert.IsTrue(replacement != null);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ClearIgnoresNestedDestructionAndFinishesAfterCallbackFailure(
            bool duringDisable,
            bool throwFromCallback
        )
        {
            RuntimeCleanupSingleton instance = RuntimeCleanupSingleton.Instance;
            Track(instance.gameObject);
            GameObject duplicateObject = Track(new GameObject("Inactive cleanup duplicate"));
            duplicateObject.SetActive(false);
            duplicateObject.AddComponent<RuntimeCleanupSingleton>();
            int callbacks = 0;
            bool cacheRetainedDuringNestedClear = false;
            Action callback = () =>
            {
                callbacks++;
                if (callbacks != 1)
                {
                    return;
                }
                RuntimeCleanupSingleton.ClearInstance();
                cacheRetainedDuringNestedClear = ReferenceEquals(
                    instance,
                    RuntimeCleanupSingleton._instance
                );
                if (throwFromCallback)
                {
                    throw new InvalidOperationException("Runtime cleanup callback failed");
                }
            };
            if (duringDisable)
            {
                instance.disabling = callback;
            }
            else
            {
                instance.destroying = callback;
            }
            if (throwFromCallback)
            {
                LogAssert.Expect(
                    LogType.Exception,
                    new Regex("InvalidOperationException: Runtime cleanup callback failed")
                );
            }

            Assert.DoesNotThrow(RuntimeCleanupSingleton.ClearInstance);

            Assert.AreEqual(1, callbacks);
            Assert.IsTrue(cacheRetainedDuringNestedClear);
            Assert.IsTrue(instance == null);
            Assert.IsTrue(duplicateObject == null);
            Assert.IsFalse(RuntimeCleanupSingleton.HasInstance);
            Assert.AreEqual(0, RuntimeCleanupSingleton.InitializeCount);
            RuntimeCleanupSingleton replacement = RuntimeCleanupSingleton.Instance;
            Track(replacement.gameObject);
            Assert.IsTrue(replacement != null);
            RuntimeCleanupSingleton.ClearInstance();
            Assert.IsTrue(replacement == null);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ClearCompletesCrossTypeDestructionCycle(int hierarchy)
        {
            RuntimeCleanupSingleton first = RuntimeCleanupSingleton.Instance;
            Track(first.gameObject);
            RuntimeCleanupPartnerSingleton second =
                hierarchy == 1
                    ? first.gameObject.AddComponent<RuntimeCleanupPartnerSingleton>()
                    : RuntimeCleanupPartnerSingleton.Instance;
            Track(second.gameObject);
            if (hierarchy == 2)
            {
                second.transform.SetParent(first.transform);
            }
            else if (hierarchy == 3)
            {
                first.transform.SetParent(second.transform);
            }
            int firstCallbacks = 0;
            int secondCallbacks = 0;
            first.disabling = () =>
            {
                firstCallbacks++;
                if (firstCallbacks == 1)
                {
                    RuntimeCleanupPartnerSingleton.ClearInstance();
                }
            };
            second.disabling = () =>
            {
                secondCallbacks++;
                if (secondCallbacks == 1)
                {
                    RuntimeCleanupSingleton.ClearInstance();
                }
            };

            Assert.DoesNotThrow(RuntimeCleanupSingleton.ClearInstance);

            Assert.AreEqual(1, firstCallbacks);
            Assert.AreEqual(1, secondCallbacks);
            Assert.IsTrue(first == null);
            Assert.IsTrue(second == null);
            Assert.IsFalse(RuntimeCleanupSingleton.HasInstance);
            Assert.IsFalse(RuntimeCleanupPartnerSingleton.HasInstance);
        }

        [Test]
        public void ClearSkipsDuplicateDestroyedByEarlierCallback()
        {
            RuntimeCleanupSingleton first = RuntimeCleanupSingleton.Instance;
            Track(first.gameObject);
            GameObject secondObject = Track(new GameObject("Cleanup sibling"));
            RuntimeCleanupSingleton second = secondObject.AddComponent<RuntimeCleanupSingleton>();
            bool peerDestroyedByCallback = false;
            first.disabling = () => DestroyPeer(second);
            second.disabling = () => DestroyPeer(first);

            Assert.DoesNotThrow(RuntimeCleanupSingleton.ClearInstance);

            Assert.IsTrue(first == null);
            Assert.IsTrue(secondObject == null);
            Assert.IsFalse(RuntimeCleanupSingleton.HasInstance);
            Assert.IsTrue(peerDestroyedByCallback);
            return;

            void DestroyPeer(RuntimeCleanupSingleton peer)
            {
                if (peerDestroyedByCallback)
                {
                    return;
                }
                peerDestroyedByCallback = true;
                Object.DestroyImmediate(peer.gameObject); // UNH-SUPPRESS: Callback must destroy a snapshot peer to exercise stale entries.
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void QueuedClearRunsAfterCallbackAndRemainsReusableAfterFailure(
            bool throwFromCallback
        )
        {
            RuntimeCleanupSingleton first = RuntimeCleanupSingleton.Instance;
            RuntimeCleanupPartnerSingleton second = RuntimeCleanupPartnerSingleton.Instance;
            Track(first.gameObject);
            Track(second.gameObject);
            bool firstCallbackFinished = false;
            bool secondCallbackRanAfterFirst = false;
            second.disabling = () => secondCallbackRanAfterFirst = firstCallbackFinished;
            first.disabling = () =>
            {
                try
                {
                    RuntimeCleanupPartnerSingleton.ClearInstance();
                    if (throwFromCallback)
                    {
                        throw new InvalidOperationException("Queued cleanup callback failed");
                    }
                }
                finally
                {
                    firstCallbackFinished = true;
                }
            };
            if (throwFromCallback)
            {
                LogAssert.Expect(
                    LogType.Exception,
                    new Regex("InvalidOperationException: Queued cleanup callback failed")
                );
            }

            Assert.DoesNotThrow(RuntimeCleanupSingleton.ClearInstance);

            Assert.IsTrue(first == null);
            Assert.IsTrue(second == null);
            Assert.IsTrue(secondCallbackRanAfterFirst);
            RuntimeCleanupPartnerSingleton replacement = RuntimeCleanupPartnerSingleton.Instance;
            Track(replacement.gameObject);
            Assert.IsTrue(replacement != null);
            RuntimeCleanupPartnerSingleton.ClearInstance();
            Assert.IsTrue(replacement == null);
        }
    }
}
