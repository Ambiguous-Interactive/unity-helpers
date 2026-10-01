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
    using Object = UnityEngine.Object;

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
            // Pool teardown must reach checked-out objects as well as idle storage.
            TrackedObjectPool<Texture2D> pool = NewPool(out List<string> destroyed);
            Assert.IsTrue(pool.TryTake(out Texture2D inFlight));
            Assert.IsTrue(pool.TryTake(out Texture2D pooled));
            Assert.IsTrue(pool.Release(pooled));

            /*
                Capture unique names before destruction; Unity destroyed-object equality loses identity and
                instance-ID APIs vary across supported versions.
            */
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
            // Destruction re-enters Release after draining; the removed entry must remain absent.
            TrackedObjectPool<Texture2D> pool = NewPool(out List<string> destroyed);
            Assert.IsTrue(pool.TryTake(out Texture2D taken));

            pool.Dispose();

            Assert.IsFalse(pool.Release(taken));
            Assert.AreEqual(1, destroyed.Count);
        }

        [Test]
        public void AnItemDestroyedInFlightIsStillRemovedFromTheTrackingList()
        {
            // Unity-null entries still need removal or each release leaks a dead reference.
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

        [TestCase(0, TestName = "Disposal.Producer.RefusesTakeAndDestroysItem")]
        [TestCase(1, TestName = "Disposal.Take.RefusesTakeAndDestroysItem")]
        [TestCase(2, TestName = "Disposal.Release.DestroysItemWithoutPooling")]
        public void CallbackDisposalDoesNotStrandAnItem(int callbackPhase)
        {
            TrackedObjectPool<Texture2D> pool = null;
            Texture2D created = null;
            int destroyCount = 0;
            pool = new TrackedObjectPool<Texture2D>(
                producer: () =>
                {
                    created = NewTexture();
                    if (callbackPhase == 0)
                    {
                        pool.Dispose();
                    }
                    return created;
                },
                onTake: texture =>
                {
                    if (callbackPhase == 1)
                    {
                        pool.Dispose();
                    }
                },
                onRelease: texture =>
                {
                    if (callbackPhase == 2)
                    {
                        pool.Dispose();
                    }
                },
                onDestroy: texture =>
                {
                    ++destroyCount;
                    Object.DestroyImmediate(texture); // UNH-SUPPRESS: the pool's teardown callback is the subject
                }
            );

            bool took = pool.TryTake(out Texture2D taken);
            if (callbackPhase == 2)
            {
                Assert.IsTrue(took);
                Assert.IsTrue(pool.Release(taken));
            }
            else
            {
                Assert.IsFalse(took);
                Assert.IsTrue(ReferenceEquals(taken, null));
            }

            Assert.IsTrue(pool.IsDisposed);
            Assert.AreEqual(0, pool.InFlightCount);
            Assert.AreEqual(0, pool.IdleCount);
            Assert.AreEqual(1, destroyCount);
            Assert.IsTrue(created == null);
            pool.Dispose();
            Assert.AreEqual(1, destroyCount);
        }

        [TestCase(true, TestName = "Destruction.Take.RefusesTakeAndRemovesTracking")]
        [TestCase(false, TestName = "Destruction.Release.DoesNotPoolDestroyedItem")]
        public void CallbackDestructionDoesNotRetainAnItem(bool destroyOnTake)
        {
            TrackedObjectPool<Texture2D> pool = new(
                producer: NewTexture,
                onTake: texture =>
                {
                    if (destroyOnTake)
                    {
                        Object.DestroyImmediate(texture); // UNH-SUPPRESS: callback destruction is the subject
                    }
                },
                onRelease: texture =>
                {
                    if (!destroyOnTake)
                    {
                        Object.DestroyImmediate(texture); // UNH-SUPPRESS: callback destruction is the subject
                    }
                }
            );

            bool took = pool.TryTake(out Texture2D taken);
            if (destroyOnTake)
            {
                Assert.IsFalse(took);
                Assert.IsTrue(ReferenceEquals(taken, null));
            }
            else
            {
                Assert.IsTrue(took);
                Assert.IsTrue(pool.Release(taken));
            }

            Assert.AreEqual(0, pool.InFlightCount);
            Assert.AreEqual(0, pool.IdleCount);
            pool.Dispose();
        }

        [TestCase(false, TestName = "Ownership.ReleaseDuringTake.RefusesOuterTake")]
        [TestCase(true, TestName = "Ownership.ReacquireDuringTake.RefusesOuterTake")]
        public void TakeCallbackCannotHandOutAnItemWhoseOwnershipChanged(bool reacquire)
        {
            TrackedObjectPool<Texture2D> pool = null;
            Texture2D nestedTaken = null;
            bool callbackRan = false;
            pool = new TrackedObjectPool<Texture2D>(
                producer: NewTexture,
                onTake: texture =>
                {
                    if (callbackRan)
                    {
                        return;
                    }
                    callbackRan = true;
                    Assert.IsTrue(pool.Release(texture));
                    if (reacquire)
                    {
                        Assert.IsTrue(pool.TryTake(out nestedTaken));
                        Assert.AreSame(texture, nestedTaken);
                    }
                }
            );

            Assert.IsFalse(pool.TryTake(out Texture2D outerTaken));
            Assert.IsTrue(ReferenceEquals(outerTaken, null));
            Assert.AreEqual(reacquire ? 1 : 0, pool.InFlightCount);
            Assert.AreEqual(reacquire ? 0 : 1, pool.IdleCount);
            if (reacquire)
            {
                Assert.IsTrue(nestedTaken != null);
                Assert.IsTrue(pool.Release(nestedTaken));
            }
            pool.Dispose();
        }

        [TestCase(true, TestName = "Ownership.ReleaseOtherDuringTake.PreservesOuterTake")]
        [TestCase(false, TestName = "Ownership.TakeOtherDuringTake.PreservesOuterTake")]
        public void TakeCallbackCanChangeAnotherItemsOwnership(bool releaseEarlierItem)
        {
            TrackedObjectPool<Texture2D> pool = null;
            Texture2D earlierTaken = null;
            Texture2D nestedTaken = null;
            bool changeOwnership = false;
            pool = new TrackedObjectPool<Texture2D>(
                producer: NewTexture,
                onTake: texture =>
                {
                    if (!changeOwnership)
                    {
                        return;
                    }
                    changeOwnership = false;
                    if (releaseEarlierItem)
                    {
                        Assert.IsTrue(pool.Release(earlierTaken));
                    }
                    else
                    {
                        Assert.IsTrue(pool.TryTake(out nestedTaken));
                    }
                }
            );
            Assert.IsTrue(pool.TryTake(out earlierTaken));
            changeOwnership = true;

            Assert.IsTrue(pool.TryTake(out Texture2D outerTaken));

            Assert.IsTrue(outerTaken != null);
            Assert.AreNotSame(earlierTaken, outerTaken);
            Assert.AreEqual(releaseEarlierItem ? 1 : 3, pool.InFlightCount);
            Assert.AreEqual(releaseEarlierItem ? 1 : 0, pool.IdleCount);
            if (!releaseEarlierItem)
            {
                Assert.IsTrue(nestedTaken != null);
                Assert.AreNotSame(outerTaken, nestedTaken);
                Assert.IsTrue(pool.Release(nestedTaken));
                Assert.IsTrue(pool.Release(earlierTaken));
            }
            Assert.IsTrue(pool.Release(outerTaken));
            Assert.AreEqual(0, pool.InFlightCount);
            pool.Dispose();
        }

        [TestCase(0, TestName = "Exception.Producer.RefusesTake")]
        [TestCase(1, TestName = "Exception.Take.RetiresItem")]
        [TestCase(2, TestName = "Exception.Release.RetiresItem")]
        public void CallbackExceptionDoesNotStrandAnItem(int callbackPhase)
        {
            int destroyCount = 0;
            TrackedObjectPool<Texture2D> pool = new(
                producer: () =>
                {
                    if (callbackPhase == 0)
                    {
                        throw new InvalidOperationException("PoolCallbackProbe");
                    }
                    return NewTexture();
                },
                onTake: texture =>
                {
                    if (callbackPhase == 1)
                    {
                        throw new InvalidOperationException("PoolCallbackProbe");
                    }
                },
                onRelease: texture =>
                {
                    if (callbackPhase == 2)
                    {
                        throw new InvalidOperationException("PoolCallbackProbe");
                    }
                },
                onDestroy: texture =>
                {
                    ++destroyCount;
                    Object.DestroyImmediate(texture); // UNH-SUPPRESS: retirement after callback failure is the subject
                }
            );
            LogAssert.Expect(LogType.Exception, new Regex("PoolCallbackProbe"));

            bool took = pool.TryTake(out Texture2D taken);
            if (callbackPhase == 2)
            {
                Assert.IsTrue(took);
                Assert.IsTrue(pool.Release(taken));
            }
            else
            {
                Assert.IsFalse(took);
                Assert.IsTrue(ReferenceEquals(taken, null));
            }
            Assert.AreEqual(0, pool.InFlightCount);
            Assert.AreEqual(0, pool.IdleCount);
            Assert.AreEqual(callbackPhase == 0 ? 0 : 1, destroyCount);
            foreach (Texture2D texture in _created)
            {
                Assert.IsTrue(texture == null);
            }
            pool.Dispose();
        }

        [Test]
        public void DisposalContinuesAfterADestructionCallbackThrows()
        {
            int destroyCount = 0;
            TrackedObjectPool<Texture2D> pool = new(
                producer: NewTexture,
                onDestroy: texture =>
                {
                    ++destroyCount;
                    if (destroyCount == 1)
                    {
                        throw new InvalidOperationException("PoolDestroyProbe");
                    }
                    Object.DestroyImmediate(texture); // UNH-SUPPRESS: continued destruction after a callback failure is the subject
                }
            );
            Assert.IsTrue(pool.TryTake(out Texture2D first));
            Assert.IsTrue(pool.TryTake(out Texture2D second));
            Assert.IsTrue(pool.Release(second));
            LogAssert.Expect(LogType.Exception, new Regex("PoolDestroyProbe"));

            pool.Dispose();

            Assert.AreEqual(2, destroyCount);
            Assert.IsTrue(first != null);
            Assert.IsTrue(second == null);
            Assert.AreEqual(0, pool.InFlightCount);
            Assert.AreEqual(0, pool.IdleCount);
            pool.Dispose();
            Assert.AreEqual(2, destroyCount);
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
            // Capture a distinct name before destruction so identity remains comparable afterward.
            texture.name = "TrackedObjectPoolProbe" + _created.Count;
            _created.Add(texture);
            return texture;
        }
    }
}
