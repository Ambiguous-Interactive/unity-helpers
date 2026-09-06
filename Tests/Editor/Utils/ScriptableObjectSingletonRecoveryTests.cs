// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// The cached singleton has two states it used to serve wrongly: a <c>Lazy</c> poisoned by a
    /// throwing first load, which reports <c>IsValueCreated</c> as false and so survived the one
    /// recovery path, and a cached asset destroyed under it, which it handed back as a live
    /// reference while <see cref="ScriptableObjectSingleton{T}.HasInstance"/> called it absent.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ScriptableObjectSingletonRecoveryTests : CommonTestBase
    {
        [TearDown]
        public void ResetSingletons()
        {
            PoisonedLoadSingleton.ClearInstance();
            Lazy<DestroyedAssetSingleton> destroyedLazy = DestroyedAssetSingleton._lazyInstance;
            if (destroyedLazy.IsValueCreated)
            {
                DestroyedAssetSingleton asset = destroyedLazy.Value;
                if (asset != null)
                {
                    asset.clearing = null;
                }
            }
            DestroyedAssetSingleton.ClearInstance();
            AbsentAssetSingleton.ClearInstance();
            Lazy<LifecycleScriptableSingleton> lifecycleLazy =
                LifecycleScriptableSingleton._lazyInstance;
            if (lifecycleLazy.IsValueCreated)
            {
                LifecycleScriptableSingleton asset = lifecycleLazy.Value;
                if (asset != null)
                {
                    asset.clearing = null;
                }
            }
            LifecycleScriptableSingleton.ClearInstance();
        }

        [Test]
        public void ClearInstanceRejectsWorkerBeforeInvokingConsumerOrChangingCache()
        {
            UnityMainThreadGuard.Capture(Thread.CurrentThread);
            LifecycleScriptableSingleton asset =
                CreateScriptableObject<LifecycleScriptableSingleton>();
            LifecycleScriptableSingleton._lazyInstance = new Lazy<LifecycleScriptableSingleton>(
                () =>
                    asset
            );
            Assert.AreSame(asset, LifecycleScriptableSingleton.Instance);
            int callbacks = 0;
            asset.clearing = () => Interlocked.Increment(ref callbacks);

            Exception exception = Task.Run(() =>
                {
                    try
                    {
                        LifecycleScriptableSingleton.ClearInstance();
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
            Assert.AreEqual(0, callbacks);
            Assert.IsTrue(LifecycleScriptableSingleton.HasInstance);
            Assert.AreSame(asset, LifecycleScriptableSingleton.Instance);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ClearInstanceFinishesOnceDespiteConsumerReentryOrFailure(
            bool reenter,
            bool throwFromCallback
        )
        {
            LifecycleScriptableSingleton asset =
                CreateScriptableObject<LifecycleScriptableSingleton>();
            LifecycleScriptableSingleton._lazyInstance = new Lazy<LifecycleScriptableSingleton>(
                () =>
                    asset
            );
            Assert.AreSame(asset, LifecycleScriptableSingleton.Instance);
            int callbacks = 0;
            asset.clearing = () =>
            {
                callbacks++;
                Assert.AreSame(asset, LifecycleScriptableSingleton.Instance);
                if (reenter && callbacks == 1)
                {
                    LifecycleScriptableSingleton.ClearInstance();
                }
                if (throwFromCallback)
                {
                    throw new InvalidOperationException("consumer reset failure");
                }
            };
            if (throwFromCallback)
            {
                LogAssert.Expect(
                    LogType.Exception,
                    new Regex("InvalidOperationException: consumer reset failure")
                );
            }

            Assert.DoesNotThrow(LifecycleScriptableSingleton.ClearInstance);

            Assert.AreEqual(1, callbacks);
            Assert.IsFalse(LifecycleScriptableSingleton.HasInstance);
            Assert.IsTrue(asset != null);
            asset.clearing = null;
            LifecycleScriptableSingleton._lazyInstance = new Lazy<LifecycleScriptableSingleton>(
                () =>
                    asset
            );
            Assert.AreSame(asset, LifecycleScriptableSingleton.Instance);
            LifecycleScriptableSingleton.ClearInstance();
            Assert.IsFalse(LifecycleScriptableSingleton.HasInstance);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClearInstanceBreaksCrossSingletonCyclesAndCompletesBothResets(
            bool throwFromNestedCallback
        )
        {
            LifecycleScriptableSingleton first =
                CreateScriptableObject<LifecycleScriptableSingleton>();
            DestroyedAssetSingleton second = CreateScriptableObject<DestroyedAssetSingleton>();
            LifecycleScriptableSingleton._lazyInstance = new Lazy<LifecycleScriptableSingleton>(
                () =>
                    first
            );
            DestroyedAssetSingleton._lazyInstance = new Lazy<DestroyedAssetSingleton>(() => second);
            Assert.AreSame(first, LifecycleScriptableSingleton.Instance);
            Assert.AreSame(second, DestroyedAssetSingleton.Instance);
            int firstCallbacks = 0;
            int secondCallbacks = 0;
            first.clearing = () =>
            {
                firstCallbacks++;
                if (firstCallbacks == 1)
                {
                    DestroyedAssetSingleton.ClearInstance();
                }
            };
            second.clearing = () =>
            {
                secondCallbacks++;
                Assert.AreSame(first, LifecycleScriptableSingleton.Instance);
                Assert.AreSame(second, DestroyedAssetSingleton.Instance);
                if (secondCallbacks == 1)
                {
                    LifecycleScriptableSingleton.ClearInstance();
                }
                if (throwFromNestedCallback)
                {
                    throw new InvalidOperationException("nested consumer reset failure");
                }
            };
            if (throwFromNestedCallback)
            {
                LogAssert.Expect(
                    LogType.Exception,
                    new Regex("InvalidOperationException: nested consumer reset failure")
                );
            }

            Assert.DoesNotThrow(LifecycleScriptableSingleton.ClearInstance);

            Assert.AreEqual(1, firstCallbacks);
            Assert.AreEqual(1, secondCallbacks);
            Assert.IsFalse(LifecycleScriptableSingleton.HasInstance);
            Assert.IsFalse(DestroyedAssetSingleton.HasInstance);
            Assert.IsTrue(first != null);
            Assert.IsTrue(second != null);
        }

        [Test]
        public void ClearInstanceReplacesALazyPoisonedByAThrowingLoad()
        {
            PoisonedLoadSingleton._lazyInstance = new Lazy<PoisonedLoadSingleton>(() =>
                throw new InvalidOperationException("poisoned load")
            );

            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = PoisonedLoadSingleton.Instance;
            });

            /*
                Lazy caches a factory exception while IsValueCreated remains false, which previously prevented
                recovery.
            */
            Assert.IsFalse(PoisonedLoadSingleton._lazyInstance.IsValueCreated);

            PoisonedLoadSingleton.ClearInstance();

            Assert.DoesNotThrow(() =>
            {
                _ = PoisonedLoadSingleton.Instance;
            });
        }

        [Test]
        public void InstanceDoesNotServeAnAssetDestroyedUnderIt()
        {
            DestroyedAssetSingleton asset = Track(
                ScriptableObject.CreateInstance<DestroyedAssetSingleton>()
            );
            DestroyedAssetSingleton._lazyInstance = new Lazy<DestroyedAssetSingleton>(() => asset);
            Assert.AreSame(asset, DestroyedAssetSingleton.Instance);

            Object.DestroyImmediate(asset); // UNH-SUPPRESS: destroying it IS the subject here

            Assert.IsFalse(ReferenceEquals(asset, null));
            Assert.IsTrue(asset == null);

            DestroyedAssetSingleton resolved = DestroyedAssetSingleton.Instance;

            Assert.IsFalse(ReferenceEquals(resolved, asset));
            Assert.IsTrue(resolved == null);
            Assert.IsFalse(DestroyedAssetSingleton.HasInstance);
        }

        [Test]
        public void WorkerReadOfDestroyedAssetDoesNotReloadBeforeMainThreadRepair()
        {
            DestroyedAssetSingleton asset = Track(
                ScriptableObject.CreateInstance<DestroyedAssetSingleton>()
            );
            Lazy<DestroyedAssetSingleton> cached = new(() => asset);
            DestroyedAssetSingleton._lazyInstance = cached;
            Assert.AreSame(asset, DestroyedAssetSingleton.Instance);

            Object.DestroyImmediate(asset); // UNH-SUPPRESS: destroyed cached state is the subject

            DestroyedAssetSingleton background = Task.Run(() => DestroyedAssetSingleton.Instance)
                .GetAwaiter()
                .GetResult();

            Assert.IsTrue(ReferenceEquals(asset, background));
            Assert.AreSame(cached, DestroyedAssetSingleton._lazyInstance);

            DestroyedAssetSingleton resolved = DestroyedAssetSingleton.Instance;

            Assert.IsTrue(resolved == null);
            Assert.AreNotSame(cached, DestroyedAssetSingleton._lazyInstance);
            Assert.IsTrue(DestroyedAssetSingleton._lazyInstance.IsValueCreated);
        }

        [Test]
        public void ShutdownDoesNotReloadADestroyedCachedAsset()
        {
            DestroyedAssetSingleton asset = Track(
                ScriptableObject.CreateInstance<DestroyedAssetSingleton>()
            );
            DestroyedAssetSingleton._lazyInstance = new Lazy<DestroyedAssetSingleton>(() => asset);
            Assert.AreSame(asset, DestroyedAssetSingleton.Instance);

            Object.DestroyImmediate(asset); // UNH-SUPPRESS: destroyed cached state is the subject

            RuntimeSingletonRegistry.NotifyApplicationQuittingForTesting();
            try
            {
                DestroyedAssetSingleton resolved = DestroyedAssetSingleton.Instance;

                Assert.IsTrue(resolved == null);
                Assert.IsFalse(DestroyedAssetSingleton._lazyInstance.IsValueCreated);
                Assert.IsFalse(DestroyedAssetSingleton.HasInstance);
            }
            finally
            {
                RuntimeSingletonRegistry.PrepareForSceneLoadForTesting();
            }
        }

        [Test]
        public void InstanceDoesNotReloadWhenTheAssetIsGenuinelyAbsent()
        {
            AbsentAssetSingleton.ClearInstance();

            Assert.IsTrue(AbsentAssetSingleton.Instance == null);

            Lazy<AbsentAssetSingleton> afterFirstLoad = AbsentAssetSingleton._lazyInstance;
            Assert.IsTrue(afterFirstLoad.IsValueCreated);

            _ = AbsentAssetSingleton.Instance;
            _ = AbsentAssetSingleton.Instance;

            /*
                A resolved null is a valid missing-asset result; rebuilding it would repeat Resources lookup on
                every access.
            */
            Assert.AreSame(afterFirstLoad, AbsentAssetSingleton._lazyInstance);
        }
    }
}
#endif
