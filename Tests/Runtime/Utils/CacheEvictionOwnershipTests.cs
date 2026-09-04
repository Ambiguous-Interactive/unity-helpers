// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CacheEvictionOwnershipTests
    {
        [Test]
        public void ABoundEvictionReleasesTheLeastRecentlyUsedEntry()
        {
            List<KeyValuePair<string, string>> released = new();
            using Cache<string, string> cache = CreateCache<string, string>(
                3,
                (key, value) => released.Add(new KeyValuePair<string, string>(key, value))
            );
            cache.Set("a", "valueA");
            cache.Set("b", "valueB");
            cache.Set("c", "valueC");
            Assert.IsTrue(cache.TryGet("a", out _));
            cache.Set("d", "valueD");

            Assert.AreEqual(1, released.Count);
            Assert.AreEqual("b", released[0].Key);
            Assert.AreEqual("valueB", released[0].Value);
            Assert.IsFalse(cache.ContainsKey("b"));
            Assert.IsTrue(cache.ContainsKey("a"));
            Assert.IsTrue(cache.ContainsKey("d"));
        }

        [Test]
        public void TheCallbackSeesTheCompletedInsert()
        {
            Cache<string, string> cache = null;
            List<bool> insertVisible = new();
            cache = CreateCache<string, string>(
                2,
                (_, _) => insertVisible.Add(cache.ContainsKey("c"))
            );
            using (cache)
            {
                cache.Set("a", "valueA");
                cache.Set("b", "valueB");
                cache.Set("c", "valueC");

                Assert.AreEqual(1, insertVisible.Count);
                Assert.IsTrue(insertVisible[0]);
            }
        }

        [Test]
        public void ReplacingAValueReleasesTheOneItReplaced()
        {
            List<string> released = new();
            using Cache<string, string> cache = CreateCache<string, string>(
                8,
                (_, value) => released.Add(value)
            );
            cache.Set("a", "first");
            cache.Set("a", "second");

            Assert.AreEqual(new[] { "first" }, released);
            Assert.IsTrue(cache.TryGet("a", out string current));
            Assert.AreEqual("second", current);
        }

        [Test]
        public void ReplacingAReferenceWithItselfReleasesNothing()
        {
            List<EqualResource> released = new();
            using Cache<string, EqualResource> cache = CreateCache<string, EqualResource>(
                8,
                (_, value) => released.Add(value)
            );
            EqualResource only = new("only");
            cache.Set("a", only);
            cache.Set("a", only);

            Assert.IsEmpty(released);
            Assert.IsTrue(cache.TryGet("a", out EqualResource current));
            Assert.AreSame(only, current);
        }

        [Test]
        public void ReplacingADistinctEqualsEqualReferenceReleasesTheOldInstance()
        {
            List<EqualResource> released = new();
            using Cache<string, EqualResource> cache = CreateCache<string, EqualResource>(
                8,
                (_, value) => released.Add(value)
            );
            EqualResource first = new("first");
            EqualResource second = new("second");
            cache.Set("a", first);
            cache.Set("a", second);

            Assert.AreEqual(1, released.Count);
            Assert.AreSame(first, released[0]);
            Assert.IsTrue(cache.TryGet("a", out EqualResource current));
            Assert.AreSame(second, current);
        }

        [Test]
        public void ReplacingAnEqualValueTypeReleasesNothing()
        {
            List<int> released = new();
            using Cache<string, int> cache = CreateCache<string, int>(
                8,
                (_, value) => released.Add(value)
            );
            cache.Set("a", 42);
            cache.Set("a", 42);

            Assert.IsEmpty(released);
            Assert.IsTrue(cache.TryGet("a", out int current));
            Assert.AreEqual(42, current);
        }

        [Test]
        public void ClearReleasesEveryEntry()
        {
            List<string> released = new();
            using Cache<string, string> cache = CreateCache<string, string>(
                8,
                (_, value) => released.Add(value)
            );
            cache.Set("a", "valueA");
            cache.Set("b", "valueB");
            cache.Clear();

            Assert.AreEqual(2, released.Count);
            Assert.Contains("valueA", released);
            Assert.Contains("valueB", released);
            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void TryRemoveReleasesNothing()
        {
            List<string> released = new();
            using Cache<string, string> cache = CreateCache<string, string>(
                8,
                (_, value) => released.Add(value)
            );
            cache.Set("a", "valueA");
            Assert.IsTrue(cache.TryRemove("a", out string removed));

            Assert.AreEqual("valueA", removed);
            Assert.IsEmpty(released);
            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void StructKeysAndStructValuesAreCacheable()
        {
            List<float> released = new();
            using Cache<(long instanceId, string propertyPath), float> cache = CreateCache<
                (long instanceId, string propertyPath),
                float
            >(2, (_, value) => released.Add(value));
            Assert.IsFalse(cache.TryGet((1L, "path"), out float missing));
            Assert.AreEqual(0f, missing);
            cache.Set((1L, "first"), 1.5f);
            cache.Set((2L, "second"), 2.5f);
            cache.Set((3L, "third"), 3.5f);

            Assert.AreEqual(new[] { 1.5f }, released);
            Assert.IsFalse(cache.ContainsKey((1L, "first")));
            Assert.IsTrue(cache.TryGet((3L, "third"), out float newest));
            Assert.AreEqual(3.5f, newest);
        }

        [Test]
        public void ACacheWithoutACallbackStillEvicts()
        {
            using Cache<string, string> cache = CreateCache<string, string>(2, null);
            cache.Set("a", "valueA");
            cache.Set("b", "valueB");
            cache.Set("c", "valueC");

            Assert.AreEqual(2, cache.Count);
            Assert.IsFalse(cache.ContainsKey("a"));
            Assert.IsTrue(cache.ContainsKey("c"));
        }

        [Test]
        public void EvictionCallbackCanDisposeTheCache()
        {
            Cache<string, string> cache = null;
            cache = CreateCache<string, string>(2, (_, _) => cache.Dispose());

            cache.Set("a", "valueA");
            cache.Set("b", "valueB");

            Assert.DoesNotThrow(() => cache.Set("c", "valueC"));
            Assert.AreEqual(0, cache.Count);
            Assert.IsFalse(cache.TryGet("c", out _));
        }

        [Test]
        public void RacingGetOrAddReleasesTheFactoryResultThatLosesInsertion()
        {
            List<EqualResource> released = new();
            using Cache<string, EqualResource> cache = CreateCache<string, EqualResource>(
                8,
                (_, value) =>
                {
                    lock (released)
                    {
                        released.Add(value);
                    }
                }
            );
            using Barrier barrier = new(2);
            EqualResource firstCreated = null;
            EqualResource secondCreated = null;

            Task<EqualResource> first = Task.Run(() =>
                cache.GetOrAdd(
                    "key",
                    _ =>
                    {
                        barrier.SignalAndWait();
                        firstCreated = new EqualResource("first");
                        return firstCreated;
                    }
                )
            );
            Task<EqualResource> second = Task.Run(() =>
                cache.GetOrAdd(
                    "key",
                    _ =>
                    {
                        barrier.SignalAndWait();
                        secondCreated = new EqualResource("second");
                        return secondCreated;
                    }
                )
            );

            Assert.IsTrue(Task.WaitAll(new Task[] { first, second }, TimeSpan.FromSeconds(5)));
            Assert.AreSame(first.Result, second.Result);
            Assert.AreEqual(1, released.Count);
            Assert.AreNotSame(first.Result, released[0]);
            Assert.IsTrue(
                ReferenceEquals(firstCreated, released[0])
                    || ReferenceEquals(secondCreated, released[0])
            );
        }

        [Test]
        public void GetOrAddFactoryCanDisposeCacheAndReleasesItsUnstoredResult()
        {
            List<string> released = new();
            Cache<string, string> cache = CreateCache<string, string>(
                8,
                (_, value) => released.Add(value)
            );

            string result = null;
            Assert.DoesNotThrow(() =>
                result = cache.GetOrAdd(
                    "key",
                    _ =>
                    {
                        cache.Dispose();
                        return "created";
                    }
                )
            );

            Assert.IsTrue(result == null);
            Assert.AreEqual(new[] { "created" }, released);
        }

        [Test]
        public void NestedSameGenericCacheMutationCannotDrainOuterCallbacksUnderItsLock()
        {
            Cache<string, string> outer = null;
            bool outerReplacementVisible = false;
            int innerReleaseCount = 0;
            using Cache<string, string> inner = CacheBuilder<string, string>
                .NewBuilder()
                .MaximumSize(1)
                .OnEviction((_, _, _) => innerReleaseCount++)
                .Build();
            outer = CacheBuilder<string, string>
                .NewBuilder()
                .MaximumSize(1)
                .OnEviction(
                    (_, _, _) => outerReplacementVisible = outer.ContainsKey("outer-replacement")
                )
                .OnSet(
                    (_, value) =>
                    {
                        if (value == "trigger")
                        {
                            inner.Set("inner-replacement", "value");
                        }
                    }
                )
                .Build();
            using (outer)
            {
                inner.Set("inner-original", "value");
                outer.Set("outer-original", "value");
                outer.Set("outer-replacement", "trigger");

                Assert.AreEqual(1, innerReleaseCount);
                Assert.IsTrue(outerReplacementVisible);
            }
        }

        [Test]
        public void DisposeRacingASetDoesNotThrowOrRetainTheEntry()
        {
            for (int iteration = 0; iteration < 100; iteration++)
            {
                Cache<int, int> cache = CacheBuilder<int, int>.NewBuilder().MaximumSize(4).Build();
                using Barrier barrier = new(2);
                Task set = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    cache.Set(1, 1);
                });
                Task dispose = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    cache.Dispose();
                });

                Assert.IsTrue(Task.WaitAll(new[] { set, dispose }, TimeSpan.FromSeconds(5)));
                Assert.AreEqual(0, cache.Count);
            }
        }

        private static Cache<TKey, TValue> CreateCache<TKey, TValue>(
            int maximumSize,
            Action<TKey, TValue> onEvicted
        )
        {
            CacheBuilder<TKey, TValue> builder = CacheBuilder<TKey, TValue>
                .NewBuilder()
                .MaximumSize(maximumSize)
                .InitialCapacity(1)
                .TransferOwnershipOnRemoval();
            if (onEvicted != null)
            {
                builder = builder.OnEviction((key, value, _) => onEvicted(key, value));
            }
            return builder.Build();
        }

        private sealed class EqualResource
        {
            private readonly string _name;

            internal EqualResource(string name)
            {
                _name = name;
            }

            public override bool Equals(object obj)
            {
                return obj is EqualResource;
            }

            public override int GetHashCode()
            {
                return 0;
            }

            public override string ToString()
            {
                return _name;
            }
        }
    }
}
