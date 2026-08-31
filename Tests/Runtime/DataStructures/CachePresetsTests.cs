// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*
    WUH010 is suppressed for this file: its subject is the cache presets, read through the cache's own indexer.
    Rewriting those reads through TryGetValue would delete what they assert. Everywhere the
    indexer is incidental, tests read through DictionaryAssertions.ValueFor instead (#653).
*/
#pragma warning disable WUH010

namespace WallstopStudios.UnityHelpers.Tests.Runtime.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CachePresetsShortLivedTests
    {
        [Test]
        public void ShortLivedCreatesCache()
        {
            using Cache<string, int> cache = CachePresets.ShortLived<string, int>().Build();

            Assert.IsTrue(cache != null);
        }

        [Test]
        public void ShortLivedHasCorrectCapacity()
        {
            using Cache<string, int> cache = CachePresets.ShortLived<string, int>().Build();

            Assert.AreEqual(100, cache.Capacity);
        }

        [Test]
        public void ShortLivedSetAndTryGetWork()
        {
            using Cache<string, int> cache = CachePresets.ShortLived<string, int>().Build();

            cache.Set("key1", 42);

            Assert.IsTrue(cache.TryGet("key1", out int value));
            Assert.AreEqual(42, value);
        }

        [Test]
        public void ShortLivedSetMultipleValuesWork()
        {
            using Cache<string, int> cache = CachePresets.ShortLived<string, int>().Build();

            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3);

            Assert.AreEqual(3, cache.Count);
            Assert.IsTrue(cache.TryGet("a", out int va) && va == 1);
            Assert.IsTrue(cache.TryGet("b", out int vb) && vb == 2);
            Assert.IsTrue(cache.TryGet("c", out int vc) && vc == 3);
        }

        [Test]
        public void ShortLivedUpdateExistingValue()
        {
            using Cache<string, int> cache = CachePresets.ShortLived<string, int>().Build();

            cache.Set("key", 10);
            cache.Set("key", 20);

            Assert.AreEqual(1, cache.Count);
            Assert.IsTrue(cache.TryGet("key", out int value));
            Assert.AreEqual(20, value);
        }

        [Test]
        public void ShortLivedTryRemoveWorks()
        {
            using Cache<string, int> cache = CachePresets.ShortLived<string, int>().Build();

            cache.Set("key", 42);
            Assert.IsTrue(cache.TryRemove("key"));
            Assert.IsFalse(cache.TryGet("key", out _));
        }

        [Test]
        public void ShortLivedClearWorks()
        {
            using Cache<string, int> cache = CachePresets.ShortLived<string, int>().Build();

            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Clear();

            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void ShortLivedWithIntKeyAndStringValue()
        {
            using Cache<int, string> cache = CachePresets.ShortLived<int, string>().Build();

            cache.Set(1, "one");
            cache.Set(2, "two");

            Assert.IsTrue(cache.TryGet(1, out string v1));
            Assert.AreEqual("one", v1);
            Assert.IsTrue(cache.TryGet(2, out string v2));
            Assert.AreEqual("two", v2);
        }

        [Test]
        public void ShortLivedWithGuidKeyAndObjectValue()
        {
            using Cache<Guid, object> cache = CachePresets.ShortLived<Guid, object>().Build();

            Guid key = Guid.NewGuid();
            object value = new object();
            cache.Set(key, value);

            Assert.IsTrue(cache.TryGet(key, out object retrieved));
            Assert.AreSame(value, retrieved);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CachePresetsLongLivedTests
    {
        [Test]
        public void LongLivedCreatesCache()
        {
            using Cache<string, int> cache = CachePresets.LongLived<string, int>().Build();

            Assert.IsTrue(cache != null);
        }

        [Test]
        public void LongLivedHasCorrectCapacity()
        {
            using Cache<string, int> cache = CachePresets.LongLived<string, int>().Build();

            Assert.AreEqual(1000, cache.Capacity);
        }

        [Test]
        public void LongLivedSetAndTryGetWork()
        {
            using Cache<string, int> cache = CachePresets.LongLived<string, int>().Build();

            cache.Set("key1", 100);

            Assert.IsTrue(cache.TryGet("key1", out int value));
            Assert.AreEqual(100, value);
        }

        [Test]
        public void LongLivedSetMultipleValuesWork()
        {
            using Cache<string, int> cache = CachePresets.LongLived<string, int>().Build();

            for (int i = 0; i < 50; i++)
            {
                cache.Set($"key{i}", i);
            }

            Assert.AreEqual(50, cache.Count);
            for (int i = 0; i < 50; i++)
            {
                Assert.IsTrue(cache.TryGet($"key{i}", out int value));
                Assert.AreEqual(i, value);
            }
        }

        [Test]
        public void LongLivedUpdateExistingValue()
        {
            using Cache<string, int> cache = CachePresets.LongLived<string, int>().Build();

            cache.Set("key", 100);
            cache.Set("key", 200);

            Assert.AreEqual(1, cache.Count);
            Assert.IsTrue(cache.TryGet("key", out int value));
            Assert.AreEqual(200, value);
        }

        [Test]
        public void LongLivedTryRemoveWorks()
        {
            using Cache<string, int> cache = CachePresets.LongLived<string, int>().Build();

            cache.Set("key", 42);
            Assert.IsTrue(cache.TryRemove("key"));
            Assert.IsFalse(cache.TryGet("key", out _));
        }

        [Test]
        public void LongLivedClearWorks()
        {
            using Cache<string, int> cache = CachePresets.LongLived<string, int>().Build();

            for (int i = 0; i < 100; i++)
            {
                cache.Set($"key{i}", i);
            }
            cache.Clear();

            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void LongLivedWithTupleKeyAndListValue()
        {
            using Cache<(int, int), List<string>> cache = CachePresets
                .LongLived<(int, int), List<string>>()
                .Build();

            List<string> list = new() { "a", "b", "c" };
            cache.Set((1, 2), list);

            Assert.IsTrue(cache.TryGet((1, 2), out List<string> retrieved));
            Assert.AreSame(list, retrieved);
            Assert.AreEqual(3, retrieved.Count);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CachePresetsSessionCacheTests
    {
        [Test]
        public void SessionCacheCreatesCache()
        {
            using Cache<string, int> cache = CachePresets.SessionCache<string, int>().Build();

            Assert.IsTrue(cache != null);
        }

        [Test]
        public void SessionCacheHasCorrectCapacity()
        {
            using Cache<string, int> cache = CachePresets.SessionCache<string, int>().Build();

            Assert.AreEqual(500, cache.Capacity);
        }

        [Test]
        public void SessionCacheSetAndTryGetWork()
        {
            using Cache<string, int> cache = CachePresets.SessionCache<string, int>().Build();

            cache.Set("session_data", 12345);

            Assert.IsTrue(cache.TryGet("session_data", out int value));
            Assert.AreEqual(12345, value);
        }

        [Test]
        public void SessionCacheSetMultipleValuesWork()
        {
            using Cache<string, int> cache = CachePresets.SessionCache<string, int>().Build();

            cache.Set("player_id", 1);
            cache.Set("score", 5000);
            cache.Set("level", 10);

            Assert.AreEqual(3, cache.Count);
            Assert.IsTrue(cache.TryGet("player_id", out int playerId) && playerId == 1);
            Assert.IsTrue(cache.TryGet("score", out int score) && score == 5000);
            Assert.IsTrue(cache.TryGet("level", out int level) && level == 10);
        }

        [Test]
        public void SessionCacheUpdateExistingValue()
        {
            using Cache<string, int> cache = CachePresets.SessionCache<string, int>().Build();

            cache.Set("score", 100);
            cache.Set("score", 200);

            Assert.AreEqual(1, cache.Count);
            Assert.IsTrue(cache.TryGet("score", out int value));
            Assert.AreEqual(200, value);
        }

        [Test]
        public void SessionCacheTryRemoveWorks()
        {
            using Cache<string, int> cache = CachePresets.SessionCache<string, int>().Build();

            cache.Set("key", 42);
            Assert.IsTrue(cache.TryRemove("key"));
            Assert.IsFalse(cache.TryGet("key", out _));
        }

        [Test]
        public void SessionCacheClearWorks()
        {
            using Cache<string, int> cache = CachePresets.SessionCache<string, int>().Build();

            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3);
            cache.Clear();

            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void SessionCacheWithEnumKeyAndDictionaryValue()
        {
            using Cache<DayOfWeek, Dictionary<string, int>> cache = CachePresets
                .SessionCache<DayOfWeek, Dictionary<string, int>>()
                .Build();

            Dictionary<string, int> data = new() { { "count", 5 } };
            cache.Set(DayOfWeek.Monday, data);

            Assert.IsTrue(cache.TryGet(DayOfWeek.Monday, out Dictionary<string, int> retrieved));
            Assert.AreSame(data, retrieved);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CachePresetsHighThroughputTests
    {
        [Test]
        public void HighThroughputCreatesCache()
        {
            using Cache<string, int> cache = CachePresets.HighThroughput<string, int>().Build();

            Assert.IsTrue(cache != null);
        }

        [Test]
        public void HighThroughputHasCorrectCapacity()
        {
            using Cache<string, int> cache = CachePresets.HighThroughput<string, int>().Build();

            Assert.AreEqual(2000, cache.Capacity);
        }

        [Test]
        public void HighThroughputSetAndTryGetWork()
        {
            using Cache<string, int> cache = CachePresets.HighThroughput<string, int>().Build();

            cache.Set("path_result", 999);

            Assert.IsTrue(cache.TryGet("path_result", out int value));
            Assert.AreEqual(999, value);
        }

        [Test]
        public void HighThroughputSetMultipleValuesWork()
        {
            using Cache<int, int> cache = CachePresets.HighThroughput<int, int>().Build();

            for (int i = 0; i < 500; i++)
            {
                cache.Set(i, i * 2);
            }

            Assert.AreEqual(500, cache.Count);
            for (int i = 0; i < 500; i++)
            {
                Assert.IsTrue(cache.TryGet(i, out int value));
                Assert.AreEqual(i * 2, value);
            }
        }

        [Test]
        public void HighThroughputUpdateExistingValue()
        {
            using Cache<string, int> cache = CachePresets.HighThroughput<string, int>().Build();

            cache.Set("key", 100);
            cache.Set("key", 200);

            Assert.AreEqual(1, cache.Count);
            Assert.IsTrue(cache.TryGet("key", out int value));
            Assert.AreEqual(200, value);
        }

        [Test]
        public void HighThroughputTryRemoveWorks()
        {
            using Cache<string, int> cache = CachePresets.HighThroughput<string, int>().Build();

            cache.Set("key", 42);
            Assert.IsTrue(cache.TryRemove("key"));
            Assert.IsFalse(cache.TryGet("key", out _));
        }

        [Test]
        public void HighThroughputClearWorks()
        {
            using Cache<int, int> cache = CachePresets.HighThroughput<int, int>().Build();

            for (int i = 0; i < 1000; i++)
            {
                cache.Set(i, i);
            }
            cache.Clear();

            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void HighThroughputRecordsStatistics()
        {
            using Cache<string, int> cache = CachePresets.HighThroughput<string, int>().Build();

            cache.Set("a", 1);
            cache.TryGet("a", out _);
            cache.TryGet("b", out _);

            CacheStatistics stats = cache.GetStatistics();

            Assert.AreEqual(1, stats.HitCount);
            Assert.AreEqual(1, stats.MissCount);
        }

        [Test]
        public void HighThroughputWithLongKeyAndByteArrayValue()
        {
            using Cache<long, byte[]> cache = CachePresets.HighThroughput<long, byte[]>().Build();

            byte[] data = new byte[] { 1, 2, 3, 4, 5 };
            cache.Set(12345L, data);

            Assert.IsTrue(cache.TryGet(12345L, out byte[] retrieved));
            Assert.AreSame(data, retrieved);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CachePresetsRenderCacheTests
    {
        [Test]
        public void RenderCacheCreatesCache()
        {
            using Cache<string, int> cache = CachePresets.RenderCache<string, int>().Build();

            Assert.IsTrue(cache != null);
        }

        [Test]
        public void RenderCacheHasCorrectCapacity()
        {
            using Cache<string, int> cache = CachePresets.RenderCache<string, int>().Build();

            Assert.AreEqual(200, cache.Capacity);
        }

        [Test]
        public void RenderCacheSetAndTryGetWork()
        {
            using Cache<string, int> cache = CachePresets.RenderCache<string, int>().Build();

            cache.Set("shader_param", 42);

            Assert.IsTrue(cache.TryGet("shader_param", out int value));
            Assert.AreEqual(42, value);
        }

        [Test]
        public void RenderCacheSetMultipleValuesWork()
        {
            using Cache<int, float> cache = CachePresets.RenderCache<int, float>().Build();

            cache.Set(1, 0.5f);
            cache.Set(2, 1.0f);
            cache.Set(3, 1.5f);

            Assert.AreEqual(3, cache.Count);
            Assert.IsTrue(cache.TryGet(1, out float v1) && Math.Abs(v1 - 0.5f) < 0.001f);
            Assert.IsTrue(cache.TryGet(2, out float v2) && Math.Abs(v2 - 1.0f) < 0.001f);
            Assert.IsTrue(cache.TryGet(3, out float v3) && Math.Abs(v3 - 1.5f) < 0.001f);
        }

        [Test]
        public void RenderCacheUpdateExistingValue()
        {
            using Cache<string, int> cache = CachePresets.RenderCache<string, int>().Build();

            cache.Set("key", 100);
            cache.Set("key", 200);

            Assert.AreEqual(1, cache.Count);
            Assert.IsTrue(cache.TryGet("key", out int value));
            Assert.AreEqual(200, value);
        }

        [Test]
        public void RenderCacheTryRemoveWorks()
        {
            using Cache<string, int> cache = CachePresets.RenderCache<string, int>().Build();

            cache.Set("key", 42);
            Assert.IsTrue(cache.TryRemove("key"));
            Assert.IsFalse(cache.TryGet("key", out _));
        }

        [Test]
        public void RenderCacheClearWorks()
        {
            using Cache<string, int> cache = CachePresets.RenderCache<string, int>().Build();

            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Clear();

            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void RenderCacheWithIntKeyAndFloatArrayValue()
        {
            using Cache<int, float[]> cache = CachePresets.RenderCache<int, float[]>().Build();

            float[] color = new float[] { 1.0f, 0.5f, 0.25f, 1.0f };
            cache.Set(42, color);

            Assert.IsTrue(cache.TryGet(42, out float[] retrieved));
            Assert.AreSame(color, retrieved);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CachePresetsNetworkCacheTests
    {
        [Test]
        public void NetworkCacheCreatesCache()
        {
            using Cache<string, int> cache = CachePresets.NetworkCache<string, int>().Build();

            Assert.IsTrue(cache != null);
        }

        [Test]
        public void NetworkCacheHasCorrectCapacity()
        {
            using Cache<string, int> cache = CachePresets.NetworkCache<string, int>().Build();

            Assert.AreEqual(100, cache.Capacity);
        }

        [Test]
        public void NetworkCacheSetAndTryGetWork()
        {
            using Cache<string, int> cache = CachePresets.NetworkCache<string, int>().Build();

            cache.Set("api_response", 200);

            Assert.IsTrue(cache.TryGet("api_response", out int value));
            Assert.AreEqual(200, value);
        }

        [Test]
        public void NetworkCacheSetMultipleValuesWork()
        {
            using Cache<string, string> cache = CachePresets.NetworkCache<string, string>().Build();

            cache.Set("endpoint1", "response1");
            cache.Set("endpoint2", "response2");
            cache.Set("endpoint3", "response3");

            Assert.AreEqual(3, cache.Count);
            Assert.IsTrue(cache.TryGet("endpoint1", out string v1) && v1 == "response1");
            Assert.IsTrue(cache.TryGet("endpoint2", out string v2) && v2 == "response2");
            Assert.IsTrue(cache.TryGet("endpoint3", out string v3) && v3 == "response3");
        }

        [Test]
        public void NetworkCacheUpdateExistingValue()
        {
            using Cache<string, int> cache = CachePresets.NetworkCache<string, int>().Build();

            cache.Set("key", 100);
            cache.Set("key", 200);

            Assert.AreEqual(1, cache.Count);
            Assert.IsTrue(cache.TryGet("key", out int value));
            Assert.AreEqual(200, value);
        }

        [Test]
        public void NetworkCacheTryRemoveWorks()
        {
            using Cache<string, int> cache = CachePresets.NetworkCache<string, int>().Build();

            cache.Set("key", 42);
            Assert.IsTrue(cache.TryRemove("key"));
            Assert.IsFalse(cache.TryGet("key", out _));
        }

        [Test]
        public void NetworkCacheClearWorks()
        {
            using Cache<string, int> cache = CachePresets.NetworkCache<string, int>().Build();

            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3);
            cache.Clear();

            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void NetworkCacheWithUriKeyAndObjectValue()
        {
            using Cache<Uri, object> cache = CachePresets.NetworkCache<Uri, object>().Build();

            Uri key = new Uri("https://example.com/api");
            object response = new { status = 200 };
            cache.Set(key, response);

            Assert.IsTrue(cache.TryGet(key, out object retrieved));
            Assert.AreSame(response, retrieved);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CachePresetsCustomizationTests
    {
        [Test]
        public void ShortLivedCanOverrideMaximumSize()
        {
            using Cache<string, int> cache = CachePresets
                .ShortLived<string, int>()
                .MaximumSize(50)
                .Build();

            Assert.AreEqual(50, cache.Capacity);
        }

        [Test]
        public void LongLivedCanOverrideMaximumSize()
        {
            using Cache<string, int> cache = CachePresets
                .LongLived<string, int>()
                .MaximumSize(500)
                .Build();

            Assert.AreEqual(500, cache.Capacity);
        }

        [Test]
        public void SessionCacheCanOverrideMaximumSize()
        {
            using Cache<string, int> cache = CachePresets
                .SessionCache<string, int>()
                .MaximumSize(250)
                .Build();

            Assert.AreEqual(250, cache.Capacity);
        }

        [Test]
        public void HighThroughputCanOverrideMaximumSize()
        {
            using Cache<string, int> cache = CachePresets
                .HighThroughput<string, int>()
                .MaximumSize(1000)
                .Build();

            Assert.AreEqual(1000, cache.Capacity);
        }

        [Test]
        public void RenderCacheCanOverrideMaximumSize()
        {
            using Cache<string, int> cache = CachePresets
                .RenderCache<string, int>()
                .MaximumSize(100)
                .Build();

            Assert.AreEqual(100, cache.Capacity);
        }

        [Test]
        public void NetworkCacheCanOverrideMaximumSize()
        {
            using Cache<string, int> cache = CachePresets
                .NetworkCache<string, int>()
                .MaximumSize(200)
                .Build();

            Assert.AreEqual(200, cache.Capacity);
        }

        [Test]
        public void ShortLivedCanOverrideEvictionPolicy()
        {
            using Cache<string, int> cache = CachePresets
                .ShortLived<string, int>()
                .EvictionPolicy(EvictionPolicy.Fifo)
                .Build();

            Assert.IsTrue(cache != null);
        }

        [Test]
        public void LongLivedCanOverrideEvictionPolicy()
        {
            using Cache<string, int> cache = CachePresets
                .LongLived<string, int>()
                .EvictionPolicy(EvictionPolicy.Lfu)
                .Build();

            Assert.IsTrue(cache != null);
        }

        [Test]
        public void SessionCacheCanOverrideExpiration()
        {
            using Cache<string, int> cache = CachePresets
                .SessionCache<string, int>()
                .ExpireAfterAccess(600f)
                .Build();

            Assert.IsTrue(cache != null);
        }

        [Test]
        public void NetworkCacheCanOverrideJitter()
        {
            using Cache<string, int> cache = CachePresets
                .NetworkCache<string, int>()
                .WithJitter(5f)
                .Build();

            Assert.IsTrue(cache != null);
        }

        [Test]
        public void PresetCanAddLoader()
        {
            using Cache<string, int> cache = CachePresets
                .ShortLived<string, int>()
                .Build(static key => key.Length);

            int result = cache.GetOrAdd("hello", null);

            Assert.AreEqual(5, result);
        }

        [Test]
        public void PresetCanEnableStatistics()
        {
            using Cache<string, int> cache = CachePresets
                .ShortLived<string, int>()
                .RecordStatistics()
                .Build();

            cache.Set("a", 1);
            cache.TryGet("a", out _);

            CacheStatistics stats = cache.GetStatistics();
            Assert.AreEqual(1, stats.HitCount);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CachePresetsEvictionCallbackTests
    {
        [Test]
        public void ShortLivedEvictionCallbackInvokedOnCapacityEviction()
        {
            List<(string key, int value, EvictionReason reason)> evictions = new();

            using Cache<string, int> cache = CachePresets
                .ShortLived<string, int>()
                .MaximumSize(2)
                .OnEviction((k, v, r) => evictions.Add((k, v, r)))
                .Build();

            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Set("c", 3);

            Assert.AreEqual(1, evictions.Count);
            Assert.AreEqual(EvictionReason.Capacity, evictions[0].reason);
        }

        [Test]
        public void LongLivedEvictionCallbackInvokedOnExplicitRemoval()
        {
            List<(string key, int value, EvictionReason reason)> evictions = new();

            using Cache<string, int> cache = CachePresets
                .LongLived<string, int>()
                .OnEviction((k, v, r) => evictions.Add((k, v, r)))
                .Build();

            cache.Set("key", 42);
            cache.TryRemove("key");

            Assert.AreEqual(1, evictions.Count);
            Assert.AreEqual("key", evictions[0].key);
            Assert.AreEqual(42, evictions[0].value);
            Assert.AreEqual(EvictionReason.Explicit, evictions[0].reason);
        }

        [Test]
        public void SessionCacheEvictionCallbackInvokedOnReplacement()
        {
            List<(string key, int value, EvictionReason reason)> evictions = new();

            using Cache<string, int> cache = CachePresets
                .SessionCache<string, int>()
                .OnEviction((k, v, r) => evictions.Add((k, v, r)))
                .Build();

            cache.Set("key", 100);
            cache.Set("key", 200);

            Assert.AreEqual(1, evictions.Count);
            Assert.AreEqual(100, evictions[0].value);
            Assert.AreEqual(EvictionReason.Replaced, evictions[0].reason);
        }

        [Test]
        public void HighThroughputEvictionCallbackInvokedOnCapacityEviction()
        {
            List<(int key, int value, EvictionReason reason)> evictions = new();

            // Disable growth to get predictable eviction behavior
            using Cache<int, int> cache = CachePresets
                .HighThroughput<int, int>()
                .MaximumSize(5)
                .AllowGrowth(0f, 0) // Disable growth
                .OnEviction((k, v, r) => evictions.Add((k, v, r)))
                .Build();

            for (int i = 0; i < 10; i++)
            {
                cache.Set(i, i * 10);
            }

            Assert.AreEqual(5, evictions.Count);
            Assert.IsTrue(evictions.TrueForAll(e => e.reason == EvictionReason.Capacity));
        }

        [Test]
        public void RenderCacheEvictionCallbackInvokedOnClear()
        {
            List<(string key, int value, EvictionReason reason)> evictions = new();

            using Cache<string, int> cache = CachePresets
                .RenderCache<string, int>()
                .OnEviction((k, v, r) => evictions.Add((k, v, r)))
                .Build();

            cache.Set("a", 1);
            cache.Set("b", 2);
            cache.Clear();

            Assert.AreEqual(2, evictions.Count);
            Assert.IsTrue(evictions.TrueForAll(e => e.reason == EvictionReason.Explicit));
        }

        [Test]
        public void NetworkCacheEvictionCallbackWithMultipleEvictions()
        {
            List<(string key, string value, EvictionReason reason)> evictions = new();

            using Cache<string, string> cache = CachePresets
                .NetworkCache<string, string>()
                .MaximumSize(3)
                .OnEviction((k, v, r) => evictions.Add((k, v, r)))
                .Build();

            cache.Set("a", "1");
            cache.Set("b", "2");
            cache.Set("c", "3");
            cache.Set("d", "4");
            cache.Set("e", "5");

            Assert.AreEqual(2, evictions.Count);
        }

        [Test]
        public void EvictionCallbackReceivesCorrectKeyAndValue()
        {
            (string key, int value, EvictionReason reason)? capturedEviction = null;

            using Cache<string, int> cache = CachePresets
                .ShortLived<string, int>()
                .MaximumSize(1)
                .OnEviction((k, v, r) => capturedEviction = (k, v, r))
                .Build();

            cache.Set("first", 100);
            cache.Set("second", 200);

            Assert.IsTrue(capturedEviction.HasValue);
            Assert.AreEqual("first", capturedEviction.Value.key);
            Assert.AreEqual(100, capturedEviction.Value.value);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CachePresetsDifferentKeyValueTypesTests
    {
        [Test]
        [TestCase(TestName = "KeyValueTypes.IntString")]
        public void IntKeyStringValue()
        {
            using Cache<int, string> cache = CachePresets.ShortLived<int, string>().Build();

            cache.Set(1, "one");
            cache.Set(2, "two");

            Assert.IsTrue(cache.TryGet(1, out string v1));
            Assert.AreEqual("one", v1);
            Assert.IsTrue(cache.TryGet(2, out string v2));
            Assert.AreEqual("two", v2);
        }

        [Test]
        [TestCase(TestName = "KeyValueTypes.StringObject")]
        public void StringKeyObjectValue()
        {
            using Cache<string, object> cache = CachePresets.LongLived<string, object>().Build();

            object obj = new { Name = "Test" };
            cache.Set("key", obj);

            Assert.IsTrue(cache.TryGet("key", out object retrieved));
            Assert.AreSame(obj, retrieved);
        }

        [Test]
        [TestCase(TestName = "KeyValueTypes.GuidInt")]
        public void GuidKeyIntValue()
        {
            using Cache<Guid, int> cache = CachePresets.SessionCache<Guid, int>().Build();

            Guid key1 = Guid.NewGuid();
            Guid key2 = Guid.NewGuid();
            cache.Set(key1, 100);
            cache.Set(key2, 200);

            Assert.IsTrue(cache.TryGet(key1, out int v1));
            Assert.AreEqual(100, v1);
            Assert.IsTrue(cache.TryGet(key2, out int v2));
            Assert.AreEqual(200, v2);
        }

        [Test]
        [TestCase(TestName = "KeyValueTypes.LongDouble")]
        public void LongKeyDoubleValue()
        {
            using Cache<long, double> cache = CachePresets.HighThroughput<long, double>().Build();

            cache.Set(1L, 1.5);
            cache.Set(2L, 2.5);

            Assert.IsTrue(cache.TryGet(1L, out double v1));
            Assert.AreEqual(1.5, v1, 0.001);
            Assert.IsTrue(cache.TryGet(2L, out double v2));
            Assert.AreEqual(2.5, v2, 0.001);
        }

        [Test]
        [TestCase(TestName = "KeyValueTypes.TupleList")]
        public void TupleKeyListValue()
        {
            using Cache<(int, int), List<int>> cache = CachePresets
                .RenderCache<(int, int), List<int>>()
                .Build();

            List<int> list = new() { 1, 2, 3 };
            cache.Set((1, 2), list);

            Assert.IsTrue(cache.TryGet((1, 2), out List<int> retrieved));
            Assert.AreSame(list, retrieved);
        }

        [Test]
        [TestCase(TestName = "KeyValueTypes.EnumByteArray")]
        public void EnumKeyByteArrayValue()
        {
            using Cache<DayOfWeek, byte[]> cache = CachePresets
                .NetworkCache<DayOfWeek, byte[]>()
                .Build();

            byte[] data = new byte[] { 1, 2, 3, 4, 5 };
            cache.Set(DayOfWeek.Monday, data);

            Assert.IsTrue(cache.TryGet(DayOfWeek.Monday, out byte[] retrieved));
            Assert.AreSame(data, retrieved);
        }

        [Test]
        [TestCase(TestName = "KeyValueTypes.StringNullableInt")]
        public void StringKeyNullableIntValue()
        {
            using Cache<string, int?> cache = CachePresets.ShortLived<string, int?>().Build();

            cache.Set("withValue", 42);
            cache.Set("withNull", null);

            Assert.IsTrue(cache.TryGet("withValue", out int? v1));
            Assert.AreEqual(42, v1);
            Assert.IsTrue(cache.TryGet("withNull", out int? v2));
            Assert.IsTrue(v2 == null);
        }

        [Test]
        [TestCase(TestName = "KeyValueTypes.TypeFunc")]
        public void TypeKeyFuncValue()
        {
            using Cache<Type, Func<int>> cache = CachePresets.LongLived<Type, Func<int>>().Build();

            Func<int> func = () => 42;
            cache.Set(typeof(string), func);

            Assert.IsTrue(cache.TryGet(typeof(string), out Func<int> retrieved));
            Assert.AreSame(func, retrieved);
            Assert.AreEqual(42, retrieved());
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CachePresetsEdgeCaseTests
    {
        [Test]
        public void ShortLivedWithEmptyStringKey()
        {
            using Cache<string, int> cache = CachePresets.ShortLived<string, int>().Build();

            cache.Set("", 42);

            Assert.IsTrue(cache.TryGet("", out int value));
            Assert.AreEqual(42, value);
        }

        [Test]
        public void LongLivedWithWhitespaceKey()
        {
            using Cache<string, int> cache = CachePresets.LongLived<string, int>().Build();

            cache.Set(" ", 1);
            cache.Set("  ", 2);
            cache.Set("\t", 3);

            Assert.IsTrue(cache.TryGet(" ", out int v1) && v1 == 1);
            Assert.IsTrue(cache.TryGet("  ", out int v2) && v2 == 2);
            Assert.IsTrue(cache.TryGet("\t", out int v3) && v3 == 3);
        }

        [Test]
        public void SessionCacheWithZeroIntKey()
        {
            using Cache<int, string> cache = CachePresets.SessionCache<int, string>().Build();

            cache.Set(0, "zero");

            Assert.IsTrue(cache.TryGet(0, out string value));
            Assert.AreEqual("zero", value);
        }

        [Test]
        public void HighThroughputWithNegativeIntKey()
        {
            using Cache<int, string> cache = CachePresets.HighThroughput<int, string>().Build();

            cache.Set(-1, "negative");
            cache.Set(int.MinValue, "min");

            Assert.IsTrue(cache.TryGet(-1, out string v1));
            Assert.AreEqual("negative", v1);
            Assert.IsTrue(cache.TryGet(int.MinValue, out string v2));
            Assert.AreEqual("min", v2);
        }

        [Test]
        public void RenderCacheTryGetMissingKeyReturnsFalse()
        {
            using Cache<string, int> cache = CachePresets.RenderCache<string, int>().Build();

            Assert.IsFalse(cache.TryGet("nonexistent", out int value));
            Assert.AreEqual(default(int), value);
        }

        [Test]
        public void NetworkCacheTryRemoveMissingKeyReturnsFalse()
        {
            using Cache<string, int> cache = CachePresets.NetworkCache<string, int>().Build();

            Assert.IsFalse(cache.TryRemove("nonexistent"));
        }

        [Test]
        public void PresetCacheContainsKeyWorks()
        {
            using Cache<string, int> cache = CachePresets.ShortLived<string, int>().Build();

            cache.Set("exists", 42);

            Assert.IsTrue(cache.ContainsKey("exists"));
            Assert.IsFalse(cache.ContainsKey("missing"));
        }

        [Test]
        public void PresetCacheGetOrAddWorks()
        {
            using Cache<string, int> cache = CachePresets.LongLived<string, int>().Build();

            int value1 = cache.GetOrAdd("key", static k => 100);
            int value2 = cache.GetOrAdd("key", static k => 200);

            Assert.AreEqual(100, value1);
            Assert.AreEqual(100, value2);
        }

        [Test]
        public void PresetCacheSetAllWorks()
        {
            using Cache<string, int> cache = CachePresets.SessionCache<string, int>().Build();

            cache.SetAll(
                new[]
                {
                    new KeyValuePair<string, int>("a", 1),
                    new KeyValuePair<string, int>("b", 2),
                    new KeyValuePair<string, int>("c", 3),
                }
            );

            Assert.AreEqual(3, cache.Count);
        }

        [Test]
        public void PresetCacheGetAllWorks()
        {
            using Cache<string, int> cache = CachePresets.HighThroughput<string, int>().Build();

            cache.Set("a", 1);
            cache.Set("b", 2);

            Dictionary<string, int> results = new();
            cache.GetAll(new[] { "a", "b", "c" }, results);

            Assert.AreEqual(2, results.Count);
            Assert.AreEqual(1, results["a"]);
            Assert.AreEqual(2, results["b"]);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CachePresetsCompactAndResizeTests
    {
        [Test]
        public void ShortLivedCompactWorks()
        {
            using Cache<int, int> cache = CachePresets.ShortLived<int, int>().Build();

            for (int i = 0; i < 50; i++)
            {
                cache.Set(i, i);
            }

            cache.Compact(0.5f);

            Assert.AreEqual(25, cache.Count);
        }

        [Test]
        public void LongLivedResizeWorks()
        {
            using Cache<int, int> cache = CachePresets.LongLived<int, int>().Build();

            for (int i = 0; i < 100; i++)
            {
                cache.Set(i, i);
            }

            cache.Resize(50);

            Assert.AreEqual(50, cache.Count);
            Assert.AreEqual(50, cache.Capacity);
        }

        [Test]
        public void SessionCacheCompactRemovesEntriesCorrectly()
        {
            using Cache<int, int> cache = CachePresets.SessionCache<int, int>().Build();

            for (int i = 0; i < 100; i++)
            {
                cache.Set(i, i);
            }

            cache.Compact(0.25f);

            Assert.AreEqual(75, cache.Count);
        }

        [Test]
        public void HighThroughputResizeIncreasesCapacity()
        {
            using Cache<int, int> cache = CachePresets.HighThroughput<int, int>().Build();

            cache.Resize(3000);

            Assert.AreEqual(3000, cache.Capacity);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CachePresetsDisposeTests
    {
        [Test]
        public void ShortLivedDisposeWorks()
        {
            Cache<string, int> cache = CachePresets.ShortLived<string, int>().Build();

            cache.Set("key", 42);
            cache.Dispose();

            Assert.IsFalse(cache.TryGet("key", out _));
        }

        [Test]
        public void LongLivedDoubleDisposeDoesNotThrow()
        {
            Cache<string, int> cache = CachePresets.LongLived<string, int>().Build();

            cache.Dispose();
            cache.Dispose();

            Assert.Pass("No exception thrown");
        }

        [Test]
        public void SessionCacheOperationsAfterDisposeDoNotThrow()
        {
            Cache<string, int> cache = CachePresets.SessionCache<string, int>().Build();

            cache.Dispose();

            cache.Set("key", 42);
            Assert.IsFalse(cache.TryGet("key", out _));
            Assert.IsFalse(cache.TryRemove("key"));
            Assert.IsFalse(cache.ContainsKey("key"));
        }

        [Test]
        public void HighThroughputClearAfterDisposeDoesNotThrow()
        {
            Cache<string, int> cache = CachePresets.HighThroughput<string, int>().Build();

            cache.Dispose();
            cache.Clear();

            Assert.Pass("No exception thrown");
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CachePresetsDataDrivenTests
    {
        [Test]
        [TestCaseSource(nameof(PresetCapacityTestData))]
        public void PresetHasExpectedCapacity(string presetName, int expectedCapacity)
        {
            Cache<string, int> cache = presetName switch
            {
                "ShortLived" => CachePresets.ShortLived<string, int>().Build(),
                "LongLived" => CachePresets.LongLived<string, int>().Build(),
                "SessionCache" => CachePresets.SessionCache<string, int>().Build(),
                "HighThroughput" => CachePresets.HighThroughput<string, int>().Build(),
                "RenderCache" => CachePresets.RenderCache<string, int>().Build(),
                "NetworkCache" => CachePresets.NetworkCache<string, int>().Build(),
                _ => throw new ArgumentException($"Unknown preset: {presetName}"),
            };

            using (cache)
            {
                Assert.AreEqual(expectedCapacity, cache.Capacity);
            }
        }

        private static IEnumerable<TestCaseData> PresetCapacityTestData()
        {
            yield return new TestCaseData("ShortLived", 100).SetName(
                "Preset.ShortLived.Capacity100"
            );
            yield return new TestCaseData("LongLived", 1000).SetName(
                "Preset.LongLived.Capacity1000"
            );
            yield return new TestCaseData("SessionCache", 500).SetName(
                "Preset.SessionCache.Capacity500"
            );
            yield return new TestCaseData("HighThroughput", 2000).SetName(
                "Preset.HighThroughput.Capacity2000"
            );
            yield return new TestCaseData("RenderCache", 200).SetName(
                "Preset.RenderCache.Capacity200"
            );
            yield return new TestCaseData("NetworkCache", 100).SetName(
                "Preset.NetworkCache.Capacity100"
            );
        }

        [Test]
        [TestCase(10, TestName = "SetAndGet.10Entries")]
        [TestCase(50, TestName = "SetAndGet.50Entries")]
        [TestCase(100, TestName = "SetAndGet.100Entries")]
        public void AllPresetsHandleMultipleEntries(int entryCount)
        {
            using Cache<int, int> shortLived = CachePresets.ShortLived<int, int>().Build();
            using Cache<int, int> longLived = CachePresets.LongLived<int, int>().Build();
            using Cache<int, int> sessionCache = CachePresets.SessionCache<int, int>().Build();
            using Cache<int, int> highThroughput = CachePresets.HighThroughput<int, int>().Build();
            using Cache<int, int> renderCache = CachePresets.RenderCache<int, int>().Build();
            using Cache<int, int> networkCache = CachePresets.NetworkCache<int, int>().Build();

            Cache<int, int>[] caches = new[]
            {
                shortLived,
                longLived,
                sessionCache,
                highThroughput,
                renderCache,
                networkCache,
            };

            for (int c = 0; c < caches.Length; c++)
            {
                Cache<int, int> cache = caches[c];
                for (int i = 0; i < entryCount; i++)
                {
                    cache.Set(i, i * 2);
                }
            }

            for (int c = 0; c < caches.Length; c++)
            {
                Cache<int, int> cache = caches[c];
                Assert.AreEqual(entryCount, cache.Count);
                for (int i = 0; i < entryCount; i++)
                {
                    Assert.IsTrue(cache.TryGet(i, out int value));
                    Assert.AreEqual(i * 2, value);
                }
            }
        }
    }
}
