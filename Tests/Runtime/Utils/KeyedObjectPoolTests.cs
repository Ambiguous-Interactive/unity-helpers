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
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [Category("Fast")]
    public sealed class KeyedObjectPoolTests : CommonTestBase
    {
        [Test]
        public void KeysOwnSeparatePoolsAndReturnsFindTheirOwner()
        {
            List<string> produced = new();
            using KeyedObjectPool<string, Texture2D> pool = new(key =>
            {
                produced.Add(key);
                return Track(new Texture2D(1, 1));
            });
            Assert.That(pool.TryTake("first", out Texture2D first), Is.True);
            Assert.That(pool.TryTake("second", out Texture2D second), Is.True);
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(pool.PoolCount, Is.EqualTo(2));
            Assert.That(pool.Release(second), Is.True);
            Assert.That(pool.Release(first), Is.True);
            Assert.That(pool.Release(first), Is.False);
            Assert.That(pool.TryTake("first", out Texture2D again), Is.True);
            Assert.That(again, Is.SameAs(first));
            CollectionAssert.AreEqual(new[] { "first", "second" }, produced);
        }

        [Test]
        public void DisposalDestroysIdleAndInFlightItemsExactlyOnce()
        {
            HashSet<Texture2D> destroyed = new();
            KeyedObjectPool<int, Texture2D> pool = new(
                _ => Track(new Texture2D(1, 1)),
                onDestroy: item =>
                {
                    Assert.That(destroyed.Add(item), Is.True);
                    Object.DestroyImmediate(item); // UNH-SUPPRESS: destruction must precede the pool assertions.
                }
            );
            Assert.That(pool.TryTake(1, out Texture2D idle), Is.True);
            Assert.That(pool.TryTake(1, out Texture2D flight), Is.True);
            Assert.That(pool.TryTake(2, out Texture2D other), Is.True);
            Assert.That(pool.Release(idle), Is.True);
            pool.Dispose();
            pool.Dispose();
            Assert.That(pool.IsDisposed, Is.True);
            Assert.That(pool.PoolCount, Is.Zero);
            Assert.That(destroyed.Count, Is.EqualTo(3));
            Assert.That(idle == null && flight == null && other == null, Is.True);
            Assert.That(pool.Release(flight), Is.False);
            Assert.That(pool.TryTake(3, out Texture2D refused), Is.False);
            Assert.That(refused == null, Is.True);
        }

        [Test]
        public void NullForeignDuplicateAndDestroyedReturnsAreHandled()
        {
            using KeyedObjectPool<string, Texture2D> pool = new(_ => Track(new Texture2D(1, 1)));
            using KeyedObjectPool<string, Texture2D> foreign = new(_ => Track(new Texture2D(1, 1)));
            Assert.That(pool.TryTake(null, out _), Is.False);
            Assert.That(pool.Release(null), Is.False);
            Assert.That(pool.Release(Track(new Texture2D(1, 1))), Is.False);
            Assert.That(pool.TryTake("key", out Texture2D item), Is.True);
            Assert.That(foreign.Release(item), Is.False);
            Object.DestroyImmediate(item); // UNH-SUPPRESS: exercise a destroyed checked-out wrapper.
            Assert.That(pool.Release(item), Is.True);
            Assert.That(pool.Release(item), Is.False);
            Assert.That(pool.TryTake("key", out Texture2D replacement), Is.True);
            Assert.That(ReferenceEquals(item, replacement), Is.False);
            Assert.That(pool.Release(replacement), Is.True);
            Object.DestroyImmediate(replacement); // UNH-SUPPRESS: exercise a destroyed idle wrapper.
            Assert.That(pool.TryTake("key", out Texture2D next), Is.True);
            Assert.That(next != null, Is.True);
        }

        [Test]
        public void MissingProducerAndNullProducedObjectsAreRefused()
        {
            using KeyedObjectPool<int, Texture2D> noProducer = new(null);
            Assert.That(noProducer.TryTake(1, out _), Is.False);
            Assert.That(noProducer.PoolCount, Is.Zero);
            using KeyedObjectPool<int, Texture2D> nullProducer = new(_ => null);
            Assert.That(nullProducer.TryTake(1, out _), Is.False);
            Assert.That(nullProducer.PoolCount, Is.EqualTo(1));
        }

        [Test]
        public void EquivalentKeysReuseTheFirstProducerKey()
        {
            List<string> produced = new();
            using KeyedObjectPool<string, Texture2D> pool = new(
                key =>
                {
                    produced.Add(key);
                    return Track(new Texture2D(1, 1));
                },
                comparer: StringComparer.OrdinalIgnoreCase
            );
            Assert.That(pool.TryTake("prefab", out Texture2D first), Is.True);
            Assert.That(pool.TryTake("PREFAB", out _), Is.True);
            Assert.That(pool.Release(first), Is.True);
            Assert.That(pool.TryTake("Prefab", out Texture2D again), Is.True);
            Assert.That(again, Is.SameAs(first));
            Assert.That(pool.PoolCount, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { "prefab", "prefab" }, produced);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ProducerCannotShareAnOwnedObjectAcrossKeysOrCheckouts(bool releaseFirst)
        {
            Texture2D shared = Track(new Texture2D(1, 1));
            int destroyed = 0;
            using KeyedObjectPool<int, Texture2D> pool = new(
                _ => shared,
                onDestroy: _ => ++destroyed
            );
            Assert.That(pool.TryTake(1, out Texture2D first), Is.True);
            Assert.That(pool.TryTake(1, out _), Is.False);
            if (releaseFirst)
            {
                Assert.That(pool.Release(first), Is.True);
            }
            Assert.That(pool.TryTake(2, out _), Is.False);
            Assert.That(destroyed, Is.Zero);
            pool.Dispose();
            Assert.That(destroyed, Is.EqualTo(1));
        }

        [Test]
        public void RecursiveProductionAndCallbackReleaseDoNotDuplicateOwnership()
        {
            KeyedObjectPool<int, Texture2D> pool = null;
            bool releaseOnTake = true;
            pool = new KeyedObjectPool<int, Texture2D>(
                key =>
                {
                    Assert.That(pool.TryTake(1, out _), Is.False);
                    return Track(new Texture2D(1, 1));
                },
                onTake: item =>
                {
                    if (releaseOnTake)
                    {
                        Assert.That(pool.Release(item), Is.True);
                    }
                }
            );
            using (pool)
            {
                Assert.That(pool.TryTake(1, out _), Is.False);
                releaseOnTake = false;
                Assert.That(pool.TryTake(1, out Texture2D item), Is.True);
                Assert.That(pool.Release(item), Is.True);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DisposalDuringProducerTakeOrReleaseRetiresTheObject(int phase)
        {
            int destroyed = 0;
            KeyedObjectPool<int, Texture2D> pool = null;
            pool = new KeyedObjectPool<int, Texture2D>(
                _ =>
                {
                    Texture2D item = Track(new Texture2D(1, 1));
                    if (phase == 0)
                    {
                        pool.Dispose();
                    }
                    return item;
                },
                onTake: _ =>
                {
                    if (phase == 1)
                    {
                        pool.Dispose();
                    }
                },
                onRelease: _ => pool.Dispose(),
                onDestroy: _ => ++destroyed
            );
            using (pool)
            {
                bool success = pool.TryTake(1, out Texture2D item);
                Assert.That(success, Is.EqualTo(phase == 2));
                if (success)
                {
                    Assert.That(pool.Release(item), Is.True);
                }
                Assert.That(pool.IsDisposed, Is.True);
                Assert.That(destroyed, Is.EqualTo(1));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void CallbackFailuresAreLoggedAndCleanupContinues(int phase)
        {
            int destroyed = 0;
            using KeyedObjectPool<int, Texture2D> pool = new(
                _ =>
                {
                    if (phase == 0)
                    {
                        throw new InvalidOperationException("producer failure");
                    }
                    return Track(new Texture2D(1, 1));
                },
                onTake: _ =>
                {
                    if (phase == 1)
                    {
                        throw new InvalidOperationException("take failure");
                    }
                },
                onRelease: _ =>
                {
                    if (phase == 2)
                    {
                        throw new InvalidOperationException("release failure");
                    }
                },
                onDestroy: _ =>
                {
                    ++destroyed;
                    if (phase == 3)
                    {
                        throw new InvalidOperationException("destroy failure");
                    }
                }
            );
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: .* failure"));
            bool taken = pool.TryTake(1, out Texture2D item);
            Assert.That(taken, Is.EqualTo(2 <= phase));
            if (taken)
            {
                Assert.That(pool.Release(item), Is.True);
            }
            pool.Dispose();
            Assert.That(destroyed, Is.EqualTo(phase == 0 ? 0 : 1));
        }

        [Test]
        public void IdleLimitAppliesPerKeyAndRetirementCannotReproduceTheSameItem()
        {
            Texture2D retiring = null;
            int destroyed = 0;
            KeyedObjectPool<int, Texture2D> pool = null;
            pool = new KeyedObjectPool<int, Texture2D>(
                _ => retiring != null ? retiring : Track(new Texture2D(1, 1)),
                onDestroy: item =>
                {
                    ++destroyed;
                    retiring = item;
                    Assert.That(pool.TryTake(3, out _), Is.False);
                    retiring = null;
                },
                maxIdleCount: 1
            );
            using (pool)
            {
                Assert.That(pool.TryTake(1, out Texture2D first), Is.True);
                Assert.That(pool.TryTake(1, out Texture2D extra), Is.True);
                Assert.That(pool.TryTake(2, out Texture2D second), Is.True);
                Assert.That(pool.Release(first), Is.True);
                Assert.That(pool.Release(extra), Is.True);
                Assert.That(pool.Release(second), Is.True);
                Assert.That(destroyed, Is.EqualTo(1));
                pool.Dispose();
                Assert.That(destroyed, Is.EqualTo(3));
            }
        }
    }
}
