// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class BuffersWaitInstructionTests
    {
        private IDisposable waitInstructionScope;

        [SetUp]
        public void SetUp()
        {
            waitInstructionScope = Buffers.BeginWaitInstructionTestScope();
        }

        [TearDown]
        public void TearDown()
        {
            waitInstructionScope?.Dispose();
        }

        [Test]
        public void GetWaitForSecondsCachesByValue()
        {
            WaitForSeconds a1 = Buffers.GetWaitForSeconds(0.25f);
            WaitForSeconds a2 = Buffers.GetWaitForSeconds(0.25f);
            WaitForSeconds b = Buffers.GetWaitForSeconds(0.5f);

            Assert.AreSame(a1, a2);
            Assert.AreNotSame(a1, b);
        }

        [Test]
        public void GetWaitForSecondsRealtimeCachesByValue()
        {
            WaitForSecondsRealtime a1 = Buffers.GetWaitForSecondsRealTime(1f);
            WaitForSecondsRealtime a2 = Buffers.GetWaitForSecondsRealTime(1f);
            WaitForSecondsRealtime b = Buffers.GetWaitForSecondsRealTime(2f);

            Assert.AreSame(a1, a2);
            Assert.AreNotSame(a1, b);
        }

        [TestCase(0.14f, 0.1f)]
        [TestCase(0.149f, 0.1f)]
        [TestCase(0.151f, 0.2f)]
        [TestCase(0.16f, 0.2f)]
        [TestCase(0.26f, 0.3f)]
        public void QuantizationRoundsDurationsToNearestStep(
            float requestedSeconds,
            float expectedQuantizedSeconds
        )
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            WaitForSeconds baseline = Buffers.GetWaitForSeconds(expectedQuantizedSeconds);
            WaitForSeconds quantized = Buffers.GetWaitForSeconds(requestedSeconds);

            Assert.AreSame(baseline, quantized);
            Assert.IsTrue(Buffers.WaitForSecondsCacheDiagnostics.IsQuantized);
        }

        [TestCase(0.049f, 0.051f)]
        [TestCase(0.14f, 0.16f)]
        [TestCase(1.249f, 1.251f)]
        public void QuantizationSeparatesDurationsAcrossStepBoundaries(
            float firstSeconds,
            float secondSeconds
        )
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            WaitForSeconds first = Buffers.GetWaitForSeconds(firstSeconds);
            WaitForSeconds second = Buffers.GetWaitForSeconds(secondSeconds);

            Assert.AreNotSame(first, second);
        }

        [TestCase(false, 0.24f, 0.22f)]
        [TestCase(true, 0.36f, 0.39f)]
        public void TryGetQuantizationCollapsesDurations(
            bool useRealtime,
            float firstSeconds,
            float secondSeconds
        )
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;
            Buffers.WaitInstructionMaxDistinctEntries = 8;

            if (useRealtime)
            {
                WaitForSecondsRealtime first = Buffers.TryGetWaitForSecondsRealtimePooled(
                    firstSeconds
                );
                WaitForSecondsRealtime second = Buffers.TryGetWaitForSecondsRealtimePooled(
                    secondSeconds
                );

                Assert.NotNull(first);
                Assert.AreSame(first, second);

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics;
                Assert.IsTrue(diagnostics.IsQuantized);
                Assert.AreEqual(1, diagnostics.DistinctEntries);
            }
            else
            {
                WaitForSeconds first = Buffers.TryGetWaitForSecondsPooled(firstSeconds);
                WaitForSeconds second = Buffers.TryGetWaitForSecondsPooled(secondSeconds);

                Assert.NotNull(first);
                Assert.AreSame(first, second);

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsCacheDiagnostics;
                Assert.IsTrue(diagnostics.IsQuantized);
                Assert.AreEqual(1, diagnostics.DistinctEntries);
            }
        }

        [TestCase(false, 0.24f, 0.26f)]
        [TestCase(true, 0.34f, 0.36f)]
        public void TryGetQuantizationSeparatesDurations(
            bool useRealtime,
            float firstSeconds,
            float secondSeconds
        )
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;
            Buffers.WaitInstructionMaxDistinctEntries = 8;

            if (useRealtime)
            {
                WaitForSecondsRealtime first = Buffers.TryGetWaitForSecondsRealtimePooled(
                    firstSeconds
                );
                WaitForSecondsRealtime second = Buffers.TryGetWaitForSecondsRealtimePooled(
                    secondSeconds
                );

                Assert.NotNull(first);
                Assert.NotNull(second);
                Assert.AreNotSame(first, second);

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics;
                Assert.AreEqual(2, diagnostics.DistinctEntries);
            }
            else
            {
                WaitForSeconds first = Buffers.TryGetWaitForSecondsPooled(firstSeconds);
                WaitForSeconds second = Buffers.TryGetWaitForSecondsPooled(secondSeconds);

                Assert.NotNull(first);
                Assert.NotNull(second);
                Assert.AreNotSame(first, second);

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsCacheDiagnostics;
                Assert.AreEqual(2, diagnostics.DistinctEntries);
            }
        }

        [Test]
        public void CacheLimitPreventsAdditionalEntries()
        {
            Buffers.WaitInstructionMaxDistinctEntries = 1;

            WaitForSeconds first = Buffers.GetWaitForSeconds(0.25f);
            WaitForSeconds second = Buffers.GetWaitForSeconds(0.25f);
            WaitForSeconds third = Buffers.GetWaitForSeconds(0.5f);
            WaitForSeconds fourth = Buffers.GetWaitForSeconds(0.5f);

            Assert.AreSame(first, second, "cached duration should still reuse pooled instance");
            Assert.AreNotSame(
                third,
                fourth,
                "limit hit should prevent caching additional durations"
            );

            WaitInstructionCacheDiagnostics diagnostics = Buffers.WaitForSecondsCacheDiagnostics;
            Assert.AreEqual(1, diagnostics.DistinctEntries);
            Assert.GreaterOrEqual(diagnostics.LimitRefusals, 1);
        }

        [Test]
        public void TryGetWaitForSecondsPooledReturnsNullWhenLimitExceeded()
        {
            Buffers.WaitInstructionMaxDistinctEntries = 1;

            WaitForSeconds cached = Buffers.TryGetWaitForSecondsPooled(0.25f);
            WaitForSeconds shouldBeNull = Buffers.TryGetWaitForSecondsPooled(0.5f);

            Assert.NotNull(cached);
            Assert.IsTrue(shouldBeNull == null, "TryGet should return null when limit exceeded");
        }

        [Test]
        public void TryGetWaitForSecondsRealtimePooledReturnsNullWhenLimitExceeded()
        {
            Buffers.WaitInstructionMaxDistinctEntries = 1;

            WaitForSecondsRealtime cached = Buffers.TryGetWaitForSecondsRealtimePooled(0.25f);
            WaitForSecondsRealtime shouldBeNull = Buffers.TryGetWaitForSecondsRealtimePooled(0.5f);

            Assert.NotNull(cached);
            Assert.IsTrue(shouldBeNull == null, "TryGet should return null when limit exceeded");
        }

        [Test]
        public void LruEvictsOldestDurationWhenEnabled()
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 2;

            WaitForSeconds first = Buffers.GetWaitForSeconds(0.1f);
            WaitForSeconds second = Buffers.GetWaitForSeconds(0.2f);
            WaitForSeconds third = Buffers.GetWaitForSeconds(0.3f); // should evict 0.1

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.NotNull(third);

            WaitForSeconds shouldBeNull = Buffers.TryGetWaitForSecondsPooled(0.1f);
            Assert.IsTrue(
                shouldBeNull == null,
                "Least recently used entry should have been evicted."
            );

            WaitInstructionCacheDiagnostics diagnostics = Buffers.WaitForSecondsCacheDiagnostics;
            Assert.AreEqual(1, diagnostics.Evictions);
            Assert.IsTrue(diagnostics.IsLruEnabled);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TryGetDoesNotTriggerEvictionWhenCacheOnly(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 1;

            if (useRealtime)
            {
                WaitForSecondsRealtime cached = Buffers.GetWaitForSecondsRealTime(0.1f);
                Assert.NotNull(cached);

                WaitForSecondsRealtime shouldBeNull = Buffers.TryGetWaitForSecondsRealtimePooled(
                    0.2f
                );
                Assert.IsTrue(
                    shouldBeNull == null,
                    "TryGet should return null for non-cached duration"
                );

                WaitForSecondsRealtime shouldStillExist =
                    Buffers.TryGetWaitForSecondsRealtimePooled(0.1f);
                Assert.NotNull(shouldStillExist);

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics;
                Assert.AreEqual(0, diagnostics.Evictions);
            }
            else
            {
                WaitForSeconds cached = Buffers.GetWaitForSeconds(0.1f);
                Assert.NotNull(cached);

                WaitForSeconds shouldBeNull = Buffers.TryGetWaitForSecondsPooled(0.2f);
                Assert.IsTrue(
                    shouldBeNull == null,
                    "TryGet should return null for non-cached duration"
                );

                WaitForSeconds shouldStillExist = Buffers.TryGetWaitForSecondsPooled(0.1f);
                Assert.NotNull(shouldStillExist);

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsCacheDiagnostics;
                Assert.AreEqual(0, diagnostics.Evictions);
            }
        }

        [TestCase(false, 0.25f)]
        [TestCase(true, 0.35f)]
        public void TryGetPopulatesCacheWhenCapacityAvailable(bool useRealtime, float seconds)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 4;

            if (useRealtime)
            {
                WaitForSecondsRealtime initial = Buffers.TryGetWaitForSecondsRealtimePooled(
                    seconds
                );
                Assert.NotNull(initial);

                WaitForSecondsRealtime second = Buffers.TryGetWaitForSecondsRealtimePooled(seconds);
                Assert.AreSame(initial, second);

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics;
                Assert.AreEqual(1, diagnostics.DistinctEntries);
                Assert.AreEqual(0, diagnostics.Evictions);
            }
            else
            {
                WaitForSeconds initial = Buffers.TryGetWaitForSecondsPooled(seconds);
                Assert.NotNull(initial);

                WaitForSeconds second = Buffers.TryGetWaitForSecondsPooled(seconds);
                Assert.AreSame(initial, second);

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsCacheDiagnostics;
                Assert.AreEqual(1, diagnostics.DistinctEntries);
                Assert.AreEqual(0, diagnostics.Evictions);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TryGetRefreshesLruOrdering(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 2;

            if (useRealtime)
            {
                WaitForSecondsRealtime first = Buffers.GetWaitForSecondsRealTime(0.1f);
                WaitForSecondsRealtime second = Buffers.GetWaitForSecondsRealTime(0.2f);
                WaitForSecondsRealtime refreshed = Buffers.TryGetWaitForSecondsRealtimePooled(0.1f);

                Assert.AreSame(first, refreshed);

                WaitForSecondsRealtime third = Buffers.GetWaitForSecondsRealTime(0.3f);
                Assert.NotNull(third);

                WaitForSecondsRealtime shouldRemain = Buffers.TryGetWaitForSecondsRealtimePooled(
                    0.1f
                );
                Assert.NotNull(shouldRemain);

                WaitForSecondsRealtime shouldBeEvicted = Buffers.TryGetWaitForSecondsRealtimePooled(
                    0.2f
                );
                Assert.IsTrue(shouldBeEvicted == null, "Entry should have been evicted from cache");

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics;
                Assert.AreEqual(1, diagnostics.Evictions);
            }
            else
            {
                WaitForSeconds first = Buffers.GetWaitForSeconds(0.1f);
                WaitForSeconds second = Buffers.GetWaitForSeconds(0.2f);
                WaitForSeconds refreshed = Buffers.TryGetWaitForSecondsPooled(0.1f);

                Assert.AreSame(first, refreshed);

                WaitForSeconds third = Buffers.GetWaitForSeconds(0.3f);
                Assert.NotNull(third);

                WaitForSeconds shouldRemain = Buffers.TryGetWaitForSecondsPooled(0.1f);
                Assert.NotNull(shouldRemain);

                WaitForSeconds shouldBeEvicted = Buffers.TryGetWaitForSecondsPooled(0.2f);
                Assert.IsTrue(shouldBeEvicted == null, "Entry should have been evicted from cache");

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsCacheDiagnostics;
                Assert.AreEqual(1, diagnostics.Evictions);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LruEvictsMultipleEntriesInCorrectOrder(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 3;

            if (useRealtime)
            {
                WaitForSecondsRealtime first = Buffers.GetWaitForSecondsRealTime(0.1f);
                WaitForSecondsRealtime second = Buffers.GetWaitForSecondsRealTime(0.2f);
                WaitForSecondsRealtime third = Buffers.GetWaitForSecondsRealTime(0.3f);

                Assert.NotNull(first);
                Assert.NotNull(second);
                Assert.NotNull(third);
                Assert.AreEqual(3, Buffers.WaitForSecondsRealtimeCacheDiagnostics.DistinctEntries);

                WaitForSecondsRealtime fourth = Buffers.GetWaitForSecondsRealTime(0.4f);
                Assert.NotNull(fourth);
                Assert.AreEqual(3, Buffers.WaitForSecondsRealtimeCacheDiagnostics.DistinctEntries);
                Assert.AreEqual(1, Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions);

                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsRealtimePooled(0.1f) == null,
                    "Entry 0.1f should have been evicted"
                );
                Assert.NotNull(Buffers.TryGetWaitForSecondsRealtimePooled(0.2f));

                WaitForSecondsRealtime fifth = Buffers.GetWaitForSecondsRealTime(0.5f);
                Assert.NotNull(fifth);
                Assert.AreEqual(2, Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions);

                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsRealtimePooled(0.3f) == null,
                    "Entry 0.3f should have been evicted"
                );
            }
            else
            {
                WaitForSeconds first = Buffers.GetWaitForSeconds(0.1f);
                WaitForSeconds second = Buffers.GetWaitForSeconds(0.2f);
                WaitForSeconds third = Buffers.GetWaitForSeconds(0.3f);

                Assert.NotNull(first);
                Assert.NotNull(second);
                Assert.NotNull(third);
                Assert.AreEqual(3, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);

                WaitForSeconds fourth = Buffers.GetWaitForSeconds(0.4f);
                Assert.NotNull(fourth);
                Assert.AreEqual(3, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);
                Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.Evictions);

                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsPooled(0.1f) == null,
                    "Entry 0.1f should have been evicted"
                );
                Assert.NotNull(Buffers.TryGetWaitForSecondsPooled(0.2f));

                WaitForSeconds fifth = Buffers.GetWaitForSeconds(0.5f);
                Assert.NotNull(fifth);
                Assert.AreEqual(2, Buffers.WaitForSecondsCacheDiagnostics.Evictions);

                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsPooled(0.3f) == null,
                    "Entry 0.3f should have been evicted"
                );
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LruAccessUpdatesOrderingPreventingEviction(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 2;

            if (useRealtime)
            {
                WaitForSecondsRealtime first = Buffers.GetWaitForSecondsRealTime(0.1f);
                WaitForSecondsRealtime second = Buffers.GetWaitForSecondsRealTime(0.2f);

                WaitForSecondsRealtime accessFirst = Buffers.GetWaitForSecondsRealTime(0.1f);
                Assert.AreSame(first, accessFirst);

                WaitForSecondsRealtime third = Buffers.GetWaitForSecondsRealTime(0.3f);
                Assert.NotNull(third);

                Assert.NotNull(Buffers.TryGetWaitForSecondsRealtimePooled(0.1f));
                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsRealtimePooled(0.2f) == null,
                    "Entry 0.2f should have been evicted as least recently used"
                );
            }
            else
            {
                WaitForSeconds first = Buffers.GetWaitForSeconds(0.1f);
                WaitForSeconds second = Buffers.GetWaitForSeconds(0.2f);

                WaitForSeconds accessFirst = Buffers.GetWaitForSeconds(0.1f);
                Assert.AreSame(first, accessFirst);

                WaitForSeconds third = Buffers.GetWaitForSeconds(0.3f);
                Assert.NotNull(third);

                Assert.NotNull(Buffers.TryGetWaitForSecondsPooled(0.1f));
                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsPooled(0.2f) == null,
                    "Entry 0.2f should have been evicted as least recently used"
                );
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LruWithSingleEntryCacheEvictsOnEveryNewValue(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 1;

            if (useRealtime)
            {
                WaitForSecondsRealtime first = Buffers.GetWaitForSecondsRealTime(0.1f);
                Assert.NotNull(first);
                Assert.AreEqual(1, Buffers.WaitForSecondsRealtimeCacheDiagnostics.DistinctEntries);
                Assert.AreEqual(0, Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions);

                WaitForSecondsRealtime second = Buffers.GetWaitForSecondsRealTime(0.2f);
                Assert.NotNull(second);
                Assert.AreNotSame(first, second);
                Assert.AreEqual(1, Buffers.WaitForSecondsRealtimeCacheDiagnostics.DistinctEntries);
                Assert.AreEqual(1, Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions);

                WaitForSecondsRealtime third = Buffers.GetWaitForSecondsRealTime(0.3f);
                Assert.NotNull(third);
                Assert.AreNotSame(second, third);
                Assert.AreEqual(1, Buffers.WaitForSecondsRealtimeCacheDiagnostics.DistinctEntries);
                Assert.AreEqual(2, Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions);
            }
            else
            {
                WaitForSeconds first = Buffers.GetWaitForSeconds(0.1f);
                Assert.NotNull(first);
                Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);
                Assert.AreEqual(0, Buffers.WaitForSecondsCacheDiagnostics.Evictions);

                WaitForSeconds second = Buffers.GetWaitForSeconds(0.2f);
                Assert.NotNull(second);
                Assert.AreNotSame(first, second);
                Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);
                Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.Evictions);

                WaitForSeconds third = Buffers.GetWaitForSeconds(0.3f);
                Assert.NotNull(third);
                Assert.AreNotSame(second, third);
                Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);
                Assert.AreEqual(2, Buffers.WaitForSecondsCacheDiagnostics.Evictions);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LruDisabledDoesNotEvictAndReportsLimitHits(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = false;
            Buffers.WaitInstructionMaxDistinctEntries = 2;

            if (useRealtime)
            {
                WaitForSecondsRealtime first = Buffers.GetWaitForSecondsRealTime(0.1f);
                WaitForSecondsRealtime second = Buffers.GetWaitForSecondsRealTime(0.2f);
                Assert.NotNull(first);
                Assert.NotNull(second);

                WaitForSecondsRealtime third = Buffers.GetWaitForSecondsRealTime(0.3f);
                WaitForSecondsRealtime fourth = Buffers.GetWaitForSecondsRealTime(0.4f);
                Assert.NotNull(third);
                Assert.NotNull(fourth);

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics;
                Assert.AreEqual(2, diagnostics.DistinctEntries);
                Assert.AreEqual(0, diagnostics.Evictions);
                Assert.GreaterOrEqual(diagnostics.LimitRefusals, 2);
                Assert.IsFalse(diagnostics.IsLruEnabled);
            }
            else
            {
                WaitForSeconds first = Buffers.GetWaitForSeconds(0.1f);
                WaitForSeconds second = Buffers.GetWaitForSeconds(0.2f);
                Assert.NotNull(first);
                Assert.NotNull(second);

                WaitForSeconds third = Buffers.GetWaitForSeconds(0.3f);
                WaitForSeconds fourth = Buffers.GetWaitForSeconds(0.4f);
                Assert.NotNull(third);
                Assert.NotNull(fourth);

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsCacheDiagnostics;
                Assert.AreEqual(2, diagnostics.DistinctEntries);
                Assert.AreEqual(0, diagnostics.Evictions);
                Assert.GreaterOrEqual(diagnostics.LimitRefusals, 2);
                Assert.IsFalse(diagnostics.IsLruEnabled);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LruEvictionWithQuantizationEvictsQuantizedValues(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 2;
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            if (useRealtime)
            {
                WaitForSecondsRealtime first = Buffers.GetWaitForSecondsRealTime(0.14f);
                WaitForSecondsRealtime second = Buffers.GetWaitForSecondsRealTime(0.24f);
                Assert.AreEqual(
                    2,
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics.DistinctEntries,
                    "After inserting 0.14f (quantized to 0.1f) and 0.24f (quantized to 0.2f), cache should have 2 entries"
                );

                WaitForSecondsRealtime third = Buffers.GetWaitForSecondsRealTime(0.34f);
                Assert.NotNull(third);
                Assert.AreEqual(
                    2,
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics.DistinctEntries,
                    "After inserting 0.34f (quantized to 0.3f), cache should still have 2 entries due to eviction"
                );
                Assert.AreEqual(
                    1,
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions,
                    "Inserting 0.34f should have triggered exactly 1 eviction of the LRU entry (0.1f)"
                );

                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsRealtimePooled(0.1f) == null,
                    "0.1f should have been evicted as the LRU entry"
                );
                Assert.NotNull(
                    Buffers.TryGetWaitForSecondsRealtimePooled(0.2f),
                    "0.2f should still be cached (was accessed second)"
                );
                Assert.NotNull(
                    Buffers.TryGetWaitForSecondsRealtimePooled(0.3f),
                    "0.3f should be cached (was just inserted)"
                );
            }
            else
            {
                WaitForSeconds first = Buffers.GetWaitForSeconds(0.14f);
                WaitForSeconds second = Buffers.GetWaitForSeconds(0.24f);
                Assert.AreEqual(
                    2,
                    Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries,
                    "After inserting 0.14f (quantized to 0.1f) and 0.24f (quantized to 0.2f), cache should have 2 entries"
                );

                WaitForSeconds third = Buffers.GetWaitForSeconds(0.34f);
                Assert.NotNull(third);
                Assert.AreEqual(
                    2,
                    Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries,
                    "After inserting 0.34f (quantized to 0.3f), cache should still have 2 entries due to eviction"
                );
                Assert.AreEqual(
                    1,
                    Buffers.WaitForSecondsCacheDiagnostics.Evictions,
                    "Inserting 0.34f should have triggered exactly 1 eviction of the LRU entry (0.1f)"
                );

                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsPooled(0.1f) == null,
                    "0.1f should have been evicted as the LRU entry"
                );
                Assert.NotNull(
                    Buffers.TryGetWaitForSecondsPooled(0.2f),
                    "0.2f should still be cached (was accessed second)"
                );
                Assert.NotNull(
                    Buffers.TryGetWaitForSecondsPooled(0.3f),
                    "0.3f should be cached (was just inserted)"
                );
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void QuantizedCacheHitDoesNotTriggerEviction(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 2;
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            if (useRealtime)
            {
                WaitForSecondsRealtime first = Buffers.GetWaitForSecondsRealTime(0.14f);
                WaitForSecondsRealtime second = Buffers.GetWaitForSecondsRealTime(0.24f);
                Assert.AreEqual(
                    2,
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics.DistinctEntries,
                    "After inserting 0.14f->0.1f and 0.24f->0.2f, cache should have 2 entries"
                );

                WaitForSecondsRealtime hitOnFirst = Buffers.GetWaitForSecondsRealTime(0.12f);
                Assert.AreSame(
                    first,
                    hitOnFirst,
                    "0.12f should quantize to 0.1f and return the same cached instance"
                );
                Assert.AreEqual(
                    2,
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics.DistinctEntries,
                    "Cache hit should not change entry count"
                );
                Assert.AreEqual(
                    0,
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions,
                    "Cache hit should not trigger eviction"
                );

                WaitForSecondsRealtime hitOnSecond = Buffers.GetWaitForSecondsRealTime(0.19f);
                Assert.AreSame(
                    second,
                    hitOnSecond,
                    "0.19f should quantize to 0.2f and return the same cached instance"
                );
                Assert.AreEqual(
                    0,
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions,
                    "Cache hit should not trigger eviction"
                );
            }
            else
            {
                WaitForSeconds first = Buffers.GetWaitForSeconds(0.14f);
                WaitForSeconds second = Buffers.GetWaitForSeconds(0.24f);
                Assert.AreEqual(
                    2,
                    Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries,
                    "After inserting 0.14f->0.1f and 0.24f->0.2f, cache should have 2 entries"
                );

                WaitForSeconds hitOnFirst = Buffers.GetWaitForSeconds(0.12f);
                Assert.AreSame(
                    first,
                    hitOnFirst,
                    "0.12f should quantize to 0.1f and return the same cached instance"
                );
                Assert.AreEqual(
                    2,
                    Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries,
                    "Cache hit should not change entry count"
                );
                Assert.AreEqual(
                    0,
                    Buffers.WaitForSecondsCacheDiagnostics.Evictions,
                    "Cache hit should not trigger eviction"
                );

                WaitForSeconds hitOnSecond = Buffers.GetWaitForSeconds(0.19f);
                Assert.AreSame(
                    second,
                    hitOnSecond,
                    "0.19f should quantize to 0.2f and return the same cached instance"
                );
                Assert.AreEqual(
                    0,
                    Buffers.WaitForSecondsCacheDiagnostics.Evictions,
                    "Cache hit should not trigger eviction"
                );
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MultipleValuesInSameQuantizationBucketReturnSameInstance(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 10;
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            float[] valuesInSameBucket = { 0.26f, 0.27f, 0.28f, 0.29f, 0.31f, 0.32f, 0.33f, 0.34f };

            if (useRealtime)
            {
                WaitForSecondsRealtime baseline = Buffers.GetWaitForSecondsRealTime(0.3f);
                Assert.AreEqual(
                    1,
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics.DistinctEntries,
                    "Initial 0.3f should create exactly 1 cache entry"
                );

                foreach (float value in valuesInSameBucket)
                {
                    WaitForSecondsRealtime result = Buffers.GetWaitForSecondsRealTime(value);
                    Assert.AreSame(
                        baseline,
                        result,
                        $"Value {value}f should quantize to 0.3f and return the same cached instance"
                    );
                }

                Assert.AreEqual(
                    1,
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics.DistinctEntries,
                    "All values should have been cache hits, entry count should still be 1"
                );
                Assert.AreEqual(
                    0,
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions,
                    "No evictions should have occurred"
                );
            }
            else
            {
                WaitForSeconds baseline = Buffers.GetWaitForSeconds(0.3f);
                Assert.AreEqual(
                    1,
                    Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries,
                    "Initial 0.3f should create exactly 1 cache entry"
                );

                foreach (float value in valuesInSameBucket)
                {
                    WaitForSeconds result = Buffers.GetWaitForSeconds(value);
                    Assert.AreSame(
                        baseline,
                        result,
                        $"Value {value}f should quantize to 0.3f and return the same cached instance"
                    );
                }

                Assert.AreEqual(
                    1,
                    Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries,
                    "All values should have been cache hits, entry count should still be 1"
                );
                Assert.AreEqual(
                    0,
                    Buffers.WaitForSecondsCacheDiagnostics.Evictions,
                    "No evictions should have occurred"
                );
            }
        }

        [TestCase(0.14f, 0.1f)]
        [TestCase(0.15f, 0.2f)] // 0.15f/0.1f = exactly 1.5f as float32, banker's rounds to 2 (even)
        [TestCase(0.16f, 0.2f)]
        [TestCase(0.24f, 0.2f)]
        [TestCase(0.25f, 0.2f)] // 0.25f/0.1f = exactly 2.5f as float32, banker's rounds to 2 (even)
        [TestCase(0.26f, 0.3f)]
        [TestCase(0.34f, 0.3f)]
        [TestCase(0.35f, 0.4f)] // 0.35f/0.1f = exactly 3.5f as float32, banker's rounds to 4 (even)
        [TestCase(0.36f, 0.4f)]
        public void QuantizationBoundaryValuesWithLru(float inputValue, float expectedQuantized)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 10;
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            WaitForSeconds baseline = Buffers.GetWaitForSeconds(expectedQuantized);
            WaitForSeconds quantized = Buffers.GetWaitForSeconds(inputValue);

            Assert.AreSame(
                baseline,
                quantized,
                $"Input {inputValue}f should quantize to {expectedQuantized}f and return the same cached instance"
            );
            Assert.AreEqual(
                1,
                Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries,
                $"Both {expectedQuantized}f and {inputValue}f should map to the same cache entry"
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LruWithZeroMaxEntriesDisablesLimitEnforcement(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 0;

            if (useRealtime)
            {
                for (int i = 0; i < 100; i++)
                {
                    WaitForSecondsRealtime instance = Buffers.GetWaitForSecondsRealTime(i * 0.01f);
                    Assert.NotNull(instance);
                }

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics;
                Assert.AreEqual(100, diagnostics.DistinctEntries);
                Assert.AreEqual(0, diagnostics.Evictions);
                Assert.AreEqual(0, diagnostics.LimitRefusals);
            }
            else
            {
                for (int i = 0; i < 100; i++)
                {
                    WaitForSeconds instance = Buffers.GetWaitForSeconds(i * 0.01f);
                    Assert.NotNull(instance);
                }

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsCacheDiagnostics;
                Assert.AreEqual(100, diagnostics.DistinctEntries);
                Assert.AreEqual(0, diagnostics.Evictions);
                Assert.AreEqual(0, diagnostics.LimitRefusals);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DiagnosticsReflectCacheStateAccurately(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 5;
            Buffers.WaitInstructionQuantizationStepSeconds = 0.05f;

            if (useRealtime)
            {
                WaitInstructionCacheDiagnostics initial =
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics;
                Assert.AreEqual("WaitForSecondsRealtime", initial.CacheName);
                Assert.AreEqual(0, initial.DistinctEntries);
                Assert.AreEqual(5, initial.MaxDistinctEntries);
                Assert.AreEqual(0, initial.LimitRefusals);
                Assert.AreEqual(0, initial.Evictions);
                Assert.AreEqual(0.05f, initial.QuantizationStepSeconds, 0.001f);
                Assert.IsTrue(initial.IsQuantized);
                Assert.IsTrue(initial.IsLruEnabled);

                Buffers.GetWaitForSecondsRealTime(0.1f);
                Buffers.GetWaitForSecondsRealTime(0.2f);
                Buffers.GetWaitForSecondsRealTime(0.3f);

                WaitInstructionCacheDiagnostics afterInserts =
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics;
                Assert.AreEqual(3, afterInserts.DistinctEntries);
                Assert.AreEqual(0, afterInserts.Evictions);

                string toStringResult = afterInserts.ToString();
                Assert.IsTrue(toStringResult.Contains("WaitForSecondsRealtime"));
                Assert.IsTrue(toStringResult.Contains("entries=3"));
                Assert.IsTrue(toStringResult.Contains("max=5"));
            }
            else
            {
                WaitInstructionCacheDiagnostics initial = Buffers.WaitForSecondsCacheDiagnostics;
                Assert.AreEqual("WaitForSeconds", initial.CacheName);
                Assert.AreEqual(0, initial.DistinctEntries);
                Assert.AreEqual(5, initial.MaxDistinctEntries);
                Assert.AreEqual(0, initial.LimitRefusals);
                Assert.AreEqual(0, initial.Evictions);
                Assert.AreEqual(0.05f, initial.QuantizationStepSeconds, 0.001f);
                Assert.IsTrue(initial.IsQuantized);
                Assert.IsTrue(initial.IsLruEnabled);

                Buffers.GetWaitForSeconds(0.1f);
                Buffers.GetWaitForSeconds(0.2f);
                Buffers.GetWaitForSeconds(0.3f);

                WaitInstructionCacheDiagnostics afterInserts =
                    Buffers.WaitForSecondsCacheDiagnostics;
                Assert.AreEqual(3, afterInserts.DistinctEntries);
                Assert.AreEqual(0, afterInserts.Evictions);

                string toStringResult = afterInserts.ToString();
                Assert.IsTrue(toStringResult.Contains("WaitForSeconds"));
                Assert.IsTrue(toStringResult.Contains("entries=3"));
                Assert.IsTrue(toStringResult.Contains("max=5"));
            }
        }

        [Test]
        public void LruEvictsSeparatelyCachesForWaitForSecondsAndWaitForSecondsRealtime()
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 2;

            WaitForSeconds waitA = Buffers.GetWaitForSeconds(0.1f);
            WaitForSeconds waitB = Buffers.GetWaitForSeconds(0.2f);
            WaitForSecondsRealtime realtimeA = Buffers.GetWaitForSecondsRealTime(0.1f);
            WaitForSecondsRealtime realtimeB = Buffers.GetWaitForSecondsRealTime(0.2f);

            Assert.AreEqual(2, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);
            Assert.AreEqual(2, Buffers.WaitForSecondsRealtimeCacheDiagnostics.DistinctEntries);
            Assert.AreEqual(0, Buffers.WaitForSecondsCacheDiagnostics.Evictions);
            Assert.AreEqual(0, Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions);

            WaitForSeconds waitC = Buffers.GetWaitForSeconds(0.3f);
            Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.Evictions);
            Assert.AreEqual(0, Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions);

            WaitForSecondsRealtime realtimeC = Buffers.GetWaitForSecondsRealTime(0.3f);
            Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.Evictions);
            Assert.AreEqual(1, Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RepeatedAccessToSameKeyDoesNotEvictOrIncreaseEntryCount(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 2;

            if (useRealtime)
            {
                WaitForSecondsRealtime first = Buffers.GetWaitForSecondsRealTime(0.1f);
                for (int i = 0; i < 100; i++)
                {
                    WaitForSecondsRealtime same = Buffers.GetWaitForSecondsRealTime(0.1f);
                    Assert.AreSame(first, same);
                }

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsRealtimeCacheDiagnostics;
                Assert.AreEqual(1, diagnostics.DistinctEntries);
                Assert.AreEqual(0, diagnostics.Evictions);
            }
            else
            {
                WaitForSeconds first = Buffers.GetWaitForSeconds(0.1f);
                for (int i = 0; i < 100; i++)
                {
                    WaitForSeconds same = Buffers.GetWaitForSeconds(0.1f);
                    Assert.AreSame(first, same);
                }

                WaitInstructionCacheDiagnostics diagnostics =
                    Buffers.WaitForSecondsCacheDiagnostics;
                Assert.AreEqual(1, diagnostics.DistinctEntries);
                Assert.AreEqual(0, diagnostics.Evictions);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LruEvictionMaintainsCorrectOrderAfterManyOperations(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 3;

            if (useRealtime)
            {
                Buffers.GetWaitForSecondsRealTime(0.1f);
                Buffers.GetWaitForSecondsRealTime(0.2f);
                Buffers.GetWaitForSecondsRealTime(0.3f);

                Buffers.GetWaitForSecondsRealTime(0.1f);
                Buffers.GetWaitForSecondsRealTime(0.2f);

                Buffers.GetWaitForSecondsRealTime(0.4f);

                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsRealtimePooled(0.3f) == null,
                    "Entry 0.3f should have been evicted"
                );
                Assert.NotNull(Buffers.TryGetWaitForSecondsRealtimePooled(0.1f));
                Assert.NotNull(Buffers.TryGetWaitForSecondsRealtimePooled(0.2f));
                Assert.NotNull(Buffers.TryGetWaitForSecondsRealtimePooled(0.4f));

                Buffers.GetWaitForSecondsRealTime(0.1f);

                Buffers.GetWaitForSecondsRealTime(0.5f);

                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsRealtimePooled(0.2f) == null,
                    "Entry 0.2f should have been evicted"
                );
                Assert.NotNull(Buffers.TryGetWaitForSecondsRealtimePooled(0.4f));
                Assert.NotNull(Buffers.TryGetWaitForSecondsRealtimePooled(0.5f));
                Assert.NotNull(Buffers.TryGetWaitForSecondsRealtimePooled(0.1f));
            }
            else
            {
                Buffers.GetWaitForSeconds(0.1f);
                Buffers.GetWaitForSeconds(0.2f);
                Buffers.GetWaitForSeconds(0.3f);

                Buffers.GetWaitForSeconds(0.1f);
                Buffers.GetWaitForSeconds(0.2f);

                Buffers.GetWaitForSeconds(0.4f);

                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsPooled(0.3f) == null,
                    "Entry 0.3f should have been evicted"
                );
                Assert.NotNull(Buffers.TryGetWaitForSecondsPooled(0.1f));
                Assert.NotNull(Buffers.TryGetWaitForSecondsPooled(0.2f));
                Assert.NotNull(Buffers.TryGetWaitForSecondsPooled(0.4f));

                Buffers.GetWaitForSeconds(0.1f);

                Buffers.GetWaitForSeconds(0.5f);

                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsPooled(0.2f) == null,
                    "Entry 0.2f should have been evicted"
                );
                Assert.NotNull(Buffers.TryGetWaitForSecondsPooled(0.4f));
                Assert.NotNull(Buffers.TryGetWaitForSecondsPooled(0.5f));
                Assert.NotNull(Buffers.TryGetWaitForSecondsPooled(0.1f));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LruEvictionHandlesEdgeCaseDurations(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 3;

            if (useRealtime)
            {
                WaitForSecondsRealtime zero = Buffers.GetWaitForSecondsRealTime(0f);
                WaitForSecondsRealtime negative = Buffers.GetWaitForSecondsRealTime(-1f);
                WaitForSecondsRealtime large = Buffers.GetWaitForSecondsRealTime(1000f);

                Assert.NotNull(zero);
                Assert.NotNull(negative);
                Assert.NotNull(large);
                Assert.AreEqual(3, Buffers.WaitForSecondsRealtimeCacheDiagnostics.DistinctEntries);

                WaitForSecondsRealtime another = Buffers.GetWaitForSecondsRealTime(0.5f);
                Assert.NotNull(another);
                Assert.AreEqual(1, Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions);

                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsRealtimePooled(0f) == null,
                    "Entry 0f should have been evicted"
                );
            }
            else
            {
                WaitForSeconds zero = Buffers.GetWaitForSeconds(0f);
                WaitForSeconds negative = Buffers.GetWaitForSeconds(-1f);
                WaitForSeconds large = Buffers.GetWaitForSeconds(1000f);

                Assert.NotNull(zero);
                Assert.NotNull(negative);
                Assert.NotNull(large);
                Assert.AreEqual(3, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);

                WaitForSeconds another = Buffers.GetWaitForSeconds(0.5f);
                Assert.NotNull(another);
                Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.Evictions);

                Assert.IsTrue(
                    Buffers.TryGetWaitForSecondsPooled(0f) == null,
                    "Entry 0f should have been evicted"
                );
            }
        }

        [Test]
        public void TestScopeRestoresPreviousState()
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 10;
            Buffers.WaitInstructionQuantizationStepSeconds = 0.25f;

            WaitForSeconds outerInstance = Buffers.GetWaitForSeconds(0.5f);
            Assert.NotNull(outerInstance);

            using (IDisposable innerScope = Buffers.BeginWaitInstructionTestScope())
            {
                Assert.AreEqual(0, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);
                Assert.AreEqual(
                    Buffers.WaitInstructionDefaultMaxDistinctEntries,
                    Buffers.WaitInstructionMaxDistinctEntries
                );
                Assert.AreEqual(0f, Buffers.WaitInstructionQuantizationStepSeconds);
                Assert.IsFalse(Buffers.WaitInstructionUseLruEviction);

                Buffers.WaitInstructionUseLruEviction = true;
                Buffers.WaitInstructionMaxDistinctEntries = 5;

                WaitForSeconds innerInstance = Buffers.GetWaitForSeconds(1f);
                Assert.NotNull(innerInstance);
                Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);
            }

            Assert.IsTrue(Buffers.WaitInstructionUseLruEviction);
            Assert.AreEqual(10, Buffers.WaitInstructionMaxDistinctEntries);
            Assert.AreEqual(0.25f, Buffers.WaitInstructionQuantizationStepSeconds, 0.001f);
            Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);

            WaitForSeconds restored = Buffers.TryGetWaitForSecondsPooled(0.5f);
            Assert.AreSame(outerInstance, restored);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LruEvictionCounterIncrementsCorrectly(bool useRealtime)
        {
            Buffers.WaitInstructionUseLruEviction = true;
            Buffers.WaitInstructionMaxDistinctEntries = 1;

            int expectedEvictions = 0;

            if (useRealtime)
            {
                for (int i = 0; i < 10; i++)
                {
                    Buffers.GetWaitForSecondsRealTime(i * 0.1f);
                    if (0 < i)
                    {
                        expectedEvictions++;
                    }
                    Assert.AreEqual(
                        expectedEvictions,
                        Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions
                    );
                }

                Assert.AreEqual(9, Buffers.WaitForSecondsRealtimeCacheDiagnostics.Evictions);
            }
            else
            {
                for (int i = 0; i < 10; i++)
                {
                    Buffers.GetWaitForSeconds(i * 0.1f);
                    if (0 < i)
                    {
                        expectedEvictions++;
                    }
                    Assert.AreEqual(
                        expectedEvictions,
                        Buffers.WaitForSecondsCacheDiagnostics.Evictions
                    );
                }

                Assert.AreEqual(9, Buffers.WaitForSecondsCacheDiagnostics.Evictions);
            }
        }

        [Test]
        public void QuantizationUsesBankersRoundingAtHalfwayPoints()
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            float[] halfwayInputs =
            {
                0.15f,
                0.25f,
                0.35f,
                0.45f,
                0.55f,
                0.65f,
                0.75f,
                0.85f,
                0.95f,
            };
            float[] expectedOutputs = { 0.2f, 0.2f, 0.4f, 0.4f, 0.6f, 0.6f, 0.8f, 0.8f, 1.0f };

            for (int i = 0; i < halfwayInputs.Length; i++)
            {
                float input = halfwayInputs[i];
                float expected = expectedOutputs[i];

                WaitForSeconds baseline = Buffers.GetWaitForSeconds(expected);
                WaitForSeconds result = Buffers.GetWaitForSeconds(input);

                float normalized = input / 0.1f;
                float normalizedAsFloat32 = (float)normalized;

                Assert.AreSame(
                    baseline,
                    result,
                    $"Input {input}f: normalized={normalizedAsFloat32}f, expected to quantize to {expected}f (banker's rounding to even)"
                );
            }
        }

        [TestCase(0.05f, 0.0f)]
        [TestCase(0.15f, 0.2f)]
        [TestCase(0.25f, 0.2f)]
        [TestCase(0.35f, 0.4f)]
        [TestCase(0.45f, 0.4f)]
        [TestCase(0.55f, 0.6f)]
        [TestCase(0.65f, 0.6f)]
        [TestCase(0.75f, 0.8f)]
        [TestCase(0.85f, 0.8f)]
        [TestCase(0.95f, 1.0f)]
        public void QuantizationHalfwayPointsRoundToNearestEven(
            float inputValue,
            float expectedQuantized
        )
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            WaitForSeconds baseline = Buffers.GetWaitForSeconds(expectedQuantized);
            WaitForSeconds quantized = Buffers.GetWaitForSeconds(inputValue);

            float normalized = inputValue / 0.1f;
            int roundedMultiplier = Mathf.RoundToInt(normalized);

            Assert.AreSame(
                baseline,
                quantized,
                $"Input {inputValue}f: normalized={normalized}f, rounded multiplier={roundedMultiplier}, expected {expectedQuantized}f"
            );
        }

        [Test]
        public void QuantizationHandlesNaN()
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            WaitForSeconds nanResult = Buffers.GetWaitForSeconds(float.NaN);

            Assert.IsTrue(
                nanResult != null,
                "GetWaitForSeconds should return non-null for NaN input"
            );
            Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);
        }

        [Test]
        public void QuantizationHandlesPositiveInfinity()
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            WaitForSeconds infResult = Buffers.GetWaitForSeconds(float.PositiveInfinity);

            Assert.IsTrue(
                infResult != null,
                "GetWaitForSeconds should return non-null for PositiveInfinity input"
            );
            Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);
        }

        [Test]
        public void QuantizationHandlesNegativeInfinity()
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            WaitForSeconds negInfResult = Buffers.GetWaitForSeconds(float.NegativeInfinity);

            Assert.IsTrue(
                negInfResult != null,
                "GetWaitForSeconds should return non-null for NegativeInfinity input"
            );
            Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);
        }

        [Test]
        public void QuantizationHandlesNegativeValues()
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            WaitForSeconds negResult = Buffers.GetWaitForSeconds(-0.14f);
            WaitForSeconds negBaseline = Buffers.GetWaitForSeconds(-0.1f);

            Assert.AreSame(negBaseline, negResult);
            Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);
        }

        [Test]
        public void QuantizationHandlesZero()
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            WaitForSeconds zeroResult1 = Buffers.GetWaitForSeconds(0f);
            WaitForSeconds zeroResult2 = Buffers.GetWaitForSeconds(0.04f);

            Assert.AreSame(zeroResult1, zeroResult2);
            Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);
        }

        [Test]
        public void QuantizationWithVerySmallStepPreservesPrecision()
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 0.001f;

            WaitForSeconds result1 = Buffers.GetWaitForSeconds(0.0014f);
            WaitForSeconds result2 = Buffers.GetWaitForSeconds(0.001f);

            Assert.AreSame(result1, result2);
        }

        [Test]
        public void QuantizationWithLargeStepCollapsesValues()
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 1.0f;

            WaitForSeconds result1 = Buffers.GetWaitForSeconds(0.4f);
            WaitForSeconds result2 = Buffers.GetWaitForSeconds(0.0f);

            Assert.AreSame(result1, result2);
            Assert.AreEqual(1, Buffers.WaitForSecondsCacheDiagnostics.DistinctEntries);
        }

        [TestCase(0.1001f, 0.1f)]
        [TestCase(0.0999f, 0.1f)]
        [TestCase(0.1499f, 0.1f)]
        [TestCase(0.1501f, 0.2f)]
        public void QuantizationNearBoundaryValues(float inputValue, float expectedQuantized)
        {
            Buffers.WaitInstructionQuantizationStepSeconds = 0.1f;

            WaitForSeconds baseline = Buffers.GetWaitForSeconds(expectedQuantized);
            WaitForSeconds quantized = Buffers.GetWaitForSeconds(inputValue);

            Assert.AreSame(baseline, quantized);
        }
    }
}
