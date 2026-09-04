// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
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
            DestroyedAssetSingleton.ClearInstance();
            AbsentAssetSingleton.ClearInstance();
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

            /* The state that made this unrecoverable: Lazy caches the exception and still reports
               the value as never created, which is the early return ClearInstance used to take. */
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
                A guard on the reload above rather than a test of it -- this passed before the
                reload existed too. A resolved managed null is an answer, not a failure, and
                rebuilding here would re-run the whole Resources search on every access for a
                project that simply has no asset.
            */
            Assert.AreSame(afterFirstLoad, AbsentAssetSingleton._lazyInstance);
        }
    }
}
#endif
