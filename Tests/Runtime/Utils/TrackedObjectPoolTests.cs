// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// The contract is exercised against <see cref="Texture2D"/> rather than a
    /// <see cref="GameObject"/>: every rule this pool exists for is about
    /// <see cref="UnityEngine.Object"/> lifetime, and a texture has that lifetime without needing a
    /// scene to live in or dirtying one that is open.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class TrackedObjectPoolTests
    {
        private readonly List<Texture2D> _created = new();

        [TearDown]
        public void DestroyWhatTheTestsMade()
        {
            foreach (Texture2D texture in _created)
            {
                if (texture != null)
                {
                    Object.DestroyImmediate(texture); // UNH-SUPPRESS: this IS the teardown
                }
            }

            _created.Clear();
        }

        [Test]
        public void ATakenItemIsCountedInFlightAndPooledAgainOnRelease()
        {
            TrackedObjectPool<Texture2D> pool = NewPool(out List<string> destroyed);

            Assert.IsTrue(pool.TryTake(out Texture2D taken));
            Assert.AreEqual(1, pool.InFlightCount);
            Assert.AreEqual(0, pool.IdleCount);

            Assert.IsTrue(pool.Release(taken));
            Assert.AreEqual(0, pool.InFlightCount);
            Assert.AreEqual(1, pool.IdleCount);
            Assert.IsEmpty(destroyed);

            Assert.IsTrue(pool.TryTake(out Texture2D again));
            Assert.AreSame(taken, again, "A pooled item should be reused rather than rebuilt.");
        }

        [Test]
        public void DisposeDestroysWhatIsStillCheckedOut()
        {
            // The defect this type exists for: teardown that only reaches what is IN the pool leaves
            // the checked-out item standing with whatever is driving it still running.
            TrackedObjectPool<Texture2D> pool = NewPool(out List<string> destroyed);
            Assert.IsTrue(pool.TryTake(out Texture2D inFlight));
            Assert.IsTrue(pool.TryTake(out Texture2D pooled));
            Assert.IsTrue(pool.Release(pooled));

            // Identity by name, read while both are alive: two destroyed Unity objects compare
            // equal to each other through the engine's null-faking operator, so the reference is no
            // use afterwards. A name rather than an instance id because GetInstanceID is obsolete
            // from Unity 6000.5 and its replacement does not exist on 2021.3.
            string inFlightName = inFlight.name;
            string pooledName = pooled.name;

            pool.Dispose();

            Assert.AreEqual(2, destroyed.Count);
            CollectionAssert.Contains(destroyed, inFlightName);
            CollectionAssert.Contains(destroyed, pooledName);
            Assert.AreEqual(0, pool.InFlightCount);
            Assert.AreEqual(0, pool.IdleCount);
        }

        [Test]
        public void AReleaseArrivingAfterDisposeIsRefusedRatherThanCountedTwice()
        {
            // A destroyed item's own ending calls Release. Dispose drains before destroying, so the
            // entry is gone by the time that callback runs and the release finds nothing.
            TrackedObjectPool<Texture2D> pool = NewPool(out List<string> destroyed);
            Assert.IsTrue(pool.TryTake(out Texture2D taken));

            pool.Dispose();

            Assert.IsFalse(pool.Release(taken));
            Assert.AreEqual(1, destroyed.Count);
        }

        [Test]
        public void AnItemDestroyedInFlightIsStillRemovedFromTheTrackingList()
        {
            // The line that leaks if it is guarded: the entry is Unity-null but it is still the entry,
            // and a release that returns early on it leaves one dead reference behind per use.
            TrackedObjectPool<Texture2D> pool = NewPool(out List<string> _);
            Assert.IsTrue(pool.TryTake(out Texture2D taken));

            Object.DestroyImmediate(taken); // UNH-SUPPRESS: destroying mid-flight is the subject

            Assert.IsTrue(pool.Release(taken));
            Assert.AreEqual(0, pool.InFlightCount, "The destroyed entry was left in the list.");
            Assert.AreEqual(0, pool.IdleCount, "A destroyed item must not be pooled.");
        }

        [Test]
        public void AnItemDestroyedWhilePooledIsNeverHandedOut()
        {
            // What a scene unload does: it takes the objects and leaves the pool holding them.
            TrackedObjectPool<Texture2D> pool = NewPool(out List<string> _);
            Assert.IsTrue(pool.TryTake(out Texture2D taken));
            Assert.IsTrue(pool.Release(taken));

            Object.DestroyImmediate(taken); // UNH-SUPPRESS: destroying while pooled is the subject

            Assert.IsTrue(pool.TryTake(out Texture2D replacement));
            Assert.IsTrue(replacement != null, "A destroyed husk was handed out.");
            Assert.AreNotSame(taken, replacement);
        }

        [Test]
        public void ReleasingSomethingThisPoolNeverHandedOutIsRefused()
        {
            TrackedObjectPool<Texture2D> pool = NewPool(out List<string> _);
            Texture2D stranger = NewTexture();

            Assert.IsFalse(pool.Release(stranger));
            Assert.IsFalse(pool.Release(null));

            Assert.IsTrue(pool.TryTake(out Texture2D taken));
            Assert.IsTrue(pool.Release(taken));
            Assert.IsFalse(pool.Release(taken), "A double release must not be counted twice.");
            Assert.AreEqual(1, pool.IdleCount);
        }

        [Test]
        public void ItemsBeyondTheIdleLimitAreDestroyedRatherThanKept()
        {
            TrackedObjectPool<Texture2D> pool = NewPool(out List<string> destroyed, maxIdle: 1);

            Assert.IsTrue(pool.TryTake(out Texture2D first));
            Assert.IsTrue(pool.TryTake(out Texture2D second));
            string secondName = second.name;
            Assert.IsTrue(pool.Release(first));
            Assert.IsTrue(pool.Release(second));

            Assert.AreEqual(1, pool.IdleCount);
            CollectionAssert.AreEqual(new[] { secondName }, destroyed);
        }

        [Test]
        public void TakeAndReleaseCallbacksSeeEveryItem()
        {
            List<Texture2D> takenItems = new();
            List<Texture2D> releasedItems = new();
            TrackedObjectPool<Texture2D> pool = new(
                producer: NewTexture,
                onTake: takenItems.Add,
                onRelease: releasedItems.Add,
                onDestroy: texture => Object.DestroyImmediate(texture) // UNH-SUPPRESS: the pool's own teardown hook
            );

            Assert.IsTrue(pool.TryTake(out Texture2D taken));
            Assert.IsTrue(pool.Release(taken));

            CollectionAssert.AreEqual(new[] { taken }, takenItems);
            CollectionAssert.AreEqual(new[] { taken }, releasedItems);
            pool.Dispose();
        }

        [Test]
        public void APoolWithNoProducerHandsOutOnlyWhatItWasGiven()
        {
            TrackedObjectPool<Texture2D> pool = new(producer: null);
            Assert.IsFalse(pool.TryTake(out Texture2D none));
            Assert.IsTrue(none == null);
            Assert.IsFalse(pool.IsDisposed);
            pool.Dispose();
            Assert.IsTrue(pool.IsDisposed);
            Assert.IsFalse(pool.TryTake(out Texture2D _));
        }

        private TrackedObjectPool<Texture2D> NewPool(out List<string> destroyed, int maxIdle = 0)
        {
            List<string> destroyedNames = new();
            destroyed = destroyedNames;
            return new TrackedObjectPool<Texture2D>(
                producer: NewTexture,
                onDestroy: texture =>
                {
                    destroyedNames.Add(texture.name);
                    Object.DestroyImmediate(texture); // UNH-SUPPRESS: the pool's own teardown hook
                },
                maxIdleCount: maxIdle
            );
        }

        private Texture2D NewTexture()
        {
            Texture2D texture = new(1, 1);
            // A distinct name per instance: it is the only identity that can be read before
            // destruction and compared after it.
            texture.name = "TrackedObjectPoolProbe" + _created.Count;
            _created.Add(texture);
            return texture;
        }
    }
}
