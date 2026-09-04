// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// The eviction callback exists so a cache whose value owns something -- a native texture, an
    /// event listener -- can be bounded at all. Dropping the managed reference alone would leak the
    /// native allocation or leave the listener subscribed forever, which is strictly worse than
    /// retaining the entry.
    /// </summary>
    /// <remarks>
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/701">#701</see>.
    /// The callback contract is exactly four rules: a bound eviction releases, a replacing
    /// <c>Set</c> releases what it replaced, <c>Clear</c> releases everything, and <c>TryRemove</c>
    /// releases nothing because it hands the value to its caller. Each has a case here, and
    /// <see cref="TheCallbackSeesTheCompletedInsert"/> pins the one that is a deadlock hazard
    /// rather than a leak.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class BoundedLruCacheEvictionTests
    {
        [Test]
        public void ABoundEvictionReleasesTheLeastRecentlyUsedEntry()
        {
            List<KeyValuePair<string, string>> released = new();
            BoundedLruCache<string, string> cache = new(
                static () => 3,
                onEvicted: (key, value) =>
                    released.Add(new KeyValuePair<string, string>(key, value))
            );

            cache.Set("a", "valueA");
            cache.Set("b", "valueB");
            cache.Set("c", "valueC");
            Assert.AreEqual(
                3,
                cache.Count,
                "The probe must fill the bound before it can cross it."
            );
            Assert.IsTrue(cache.TryGet("a", out _), "Renewing 'a' leaves 'b' least recently used.");
            Assert.IsEmpty(released, "Filling to the bound evicts nothing.");

            cache.Set("d", "valueD");

            Assert.AreEqual(1, released.Count, "Exactly the entry over the bound is released.");
            Assert.AreEqual("b", released[0].Key, "'b' is least recently used; 'a' was renewed.");
            Assert.AreEqual("valueB", released[0].Value);
            Assert.IsFalse(cache.Contains("b"));
            Assert.IsTrue(cache.Contains("a"));
            Assert.IsTrue(cache.Contains("d"));
        }

        /// <summary>
        /// The callback runs after the monitor is released, so by the time it sees the cache the
        /// insert that triggered it has completed. A callback invoked from inside
        /// <c>EvictToBound</c> would observe the new key as absent, and would hold the monitor
        /// across consumer code that destroys a Unity object or unsubscribes a listener.
        /// </summary>
        [Test]
        public void TheCallbackSeesTheCompletedInsert()
        {
            BoundedLruCache<string, string> cache = null;
            List<bool> insertVisible = new();
            cache = new BoundedLruCache<string, string>(
                static () => 2,
                onEvicted: (_, _) => insertVisible.Add(cache.Contains("c"))
            );

            cache.Set("a", "valueA");
            cache.Set("b", "valueB");
            cache.Set("c", "valueC");

            Assert.AreEqual(1, insertVisible.Count, "The probe must have had a subject.");
            Assert.IsTrue(insertVisible[0], "The insert that caused the eviction had completed.");
        }

        [Test]
        public void ReplacingAValueReleasesTheOneItReplaced()
        {
            List<string> released = new();
            BoundedLruCache<string, string> cache = new(
                static () => 8,
                onEvicted: (_, value) => released.Add(value)
            );

            cache.Set("a", "first");
            cache.Set("a", "second");

            Assert.AreEqual(new[] { "first" }, released);
            Assert.IsTrue(cache.TryGet("a", out string current));
            Assert.AreEqual("second", current);
        }

        /// <summary>
        /// Re-storing the value already cached must not release it: the caller would be handed a
        /// destroyed texture that the cache still answers with.
        /// </summary>
        [Test]
        public void ReplacingAValueWithItselfReleasesNothing()
        {
            List<string> released = new();
            BoundedLruCache<string, string> cache = new(
                static () => 8,
                onEvicted: (_, value) => released.Add(value)
            );

            cache.Set("a", "only");
            cache.Set("a", "only");

            Assert.IsEmpty(released);
            Assert.IsTrue(cache.TryGet("a", out string current));
            Assert.AreEqual("only", current);
        }

        [Test]
        public void ClearReleasesEveryEntry()
        {
            List<string> released = new();
            BoundedLruCache<string, string> cache = new(
                static () => 8,
                onEvicted: (_, value) => released.Add(value)
            );

            cache.Set("a", "valueA");
            cache.Set("b", "valueB");
            Assert.AreEqual(2, cache.Count, "The probe must have had subjects to release.");
            cache.Clear();

            Assert.AreEqual(2, released.Count);
            Assert.Contains("valueA", released);
            Assert.Contains("valueB", released);
            Assert.AreEqual(0, cache.Count);
        }

        /// <summary>
        /// <c>TryRemove</c> hands the value to its caller, so releasing it would destroy something
        /// the caller is about to use.
        /// </summary>
        [Test]
        public void TryRemoveReleasesNothing()
        {
            List<string> released = new();
            BoundedLruCache<string, string> cache = new(
                static () => 8,
                onEvicted: (_, value) => released.Add(value)
            );

            cache.Set("a", "valueA");
            Assert.IsTrue(cache.TryRemove("a", out string removed), "The probe must have had one.");

            Assert.AreEqual("valueA", removed);
            Assert.IsEmpty(released);
            Assert.AreEqual(0, cache.Count);
        }

        /// <summary>
        /// The <c>class</c> constraints blocked every struct-keyed and struct-valued editor cache;
        /// a miss must yield <c>default</c> rather than requiring a reference type to be null.
        /// </summary>
        [Test]
        public void StructKeysAndStructValuesAreCacheable()
        {
            List<float> released = new();
            BoundedLruCache<(long instanceId, string propertyPath), float> cache = new(
                static () => 2,
                onEvicted: (_, value) => released.Add(value)
            );

            Assert.IsFalse(cache.TryGet((1L, "path"), out float missing));
            Assert.AreEqual(0f, missing, "A miss yields default, not an uninitialized read.");

            cache.Set((1L, "first"), 1.5f);
            cache.Set((2L, "second"), 2.5f);
            cache.Set((3L, "third"), 3.5f);

            Assert.AreEqual(new[] { 1.5f }, released);
            Assert.IsFalse(cache.Contains((1L, "first")));
            Assert.IsTrue(cache.TryGet((3L, "third"), out float newest));
            Assert.AreEqual(3.5f, newest);
        }

        /// <summary>
        /// A cache configured without a callback keeps the behaviour every existing caller relies
        /// on: it drops entries and releases nothing.
        /// </summary>
        [Test]
        public void ACacheWithoutACallbackStillEvicts()
        {
            BoundedLruCache<string, string> cache = new(static () => 2);

            cache.Set("a", "valueA");
            cache.Set("b", "valueB");
            cache.Set("c", "valueC");

            Assert.AreEqual(2, cache.Count);
            Assert.IsFalse(cache.Contains("a"));
            Assert.IsTrue(cache.Contains("c"));
        }
    }
}
