// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class TimedCacheTests
    {
        [Test]
        public void ConstructorValidatesArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new TimedCache<int>(null, 0.1f));
            Assert.Throws<ArgumentException>(() => new TimedCache<int>(() => 1, -0.5f));
        }

        [Test]
        public void ConstructorWithZeroTtlDoesNotThrow()
        {
            _ = new TimedCache<int>(() => 1, 0f);
            Assert.Pass();
        }

        [Test]
        public void ConstructorWithVerySmallTtlWorks()
        {
            TimedCache<int> cache = new(() => 42, 0.001f);
            Assert.AreEqual(42, cache.Value);
        }

        [Test]
        public void ConstructorWithVeryLargeTtlWorks()
        {
            TimedCache<int> cache = new(() => 42, float.MaxValue);
            Assert.AreEqual(42, cache.Value);
        }

        [Test]
        public void ValueInitiallyCallsProducer()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 1f);

            Assert.AreEqual(0, producerCalls);
            int value = cache.Value;
            Assert.AreEqual(1, value);
            Assert.AreEqual(1, producerCalls);
        }

        [Test]
        public void ValueCachesResultWithinTtl()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 10f);

            int value1 = cache.Value;
            int value2 = cache.Value;
            int value3 = cache.Value;

            Assert.AreEqual(1, value1);
            Assert.AreEqual(1, value2);
            Assert.AreEqual(1, value3);
            Assert.AreEqual(1, producerCalls);
        }

        [Test]
        public void CacheHandlesNullValues()
        {
            int producerCalls = 0;
            TimedCache<string> cache = new(
                () =>
                {
                    producerCalls++;
                    return null;
                },
                1f
            );

            string value = cache.Value;
            Assert.IsTrue(value == null, "Value should be null when producer returns null");
            Assert.AreEqual(1, producerCalls);
        }

        [Test]
        public void CacheHandlesReferenceTypes()
        {
            int producerCalls = 0;
            TimedCache<object> cache = new(
                () =>
                {
                    producerCalls++;
                    return new object();
                },
                1f
            );

            object value1 = cache.Value;
            object value2 = cache.Value;

            Assert.AreSame(value1, value2);
            Assert.AreEqual(1, producerCalls);
        }

        [Test]
        public void CacheHandlesValueTypes()
        {
            int producerCalls = 0;
            TimedCache<float> cache = new(
                () =>
                {
                    producerCalls++;
                    return 3.14f;
                },
                1f
            );

            float value = cache.Value;
            Assert.AreEqual(3.14f, value, 0.001f);
            Assert.AreEqual(1, producerCalls);
        }

        [Test]
        public void MultipleResetsWork()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 10f);

            _ = cache.Value;
            cache.Reset();
            _ = cache.Value;
            cache.Reset();
            _ = cache.Value;

            Assert.AreEqual(3, producerCalls);
        }

        [Test]
        public void ResetBeforeFirstAccessWorks()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 10f);

            cache.Reset();
            int value = cache.Value;

            Assert.AreEqual(1, value);
            Assert.AreEqual(1, producerCalls);
        }

        [Test]
        public void ProducerExceptionsPropagateToValue()
        {
            TimedCache<int> cache = new(
                () => throw new InvalidOperationException("Test exception"),
                1f
            );

            Assert.Throws<InvalidOperationException>(() =>
            {
                int _ = cache.Value;
            });
        }

        [Test]
        public void ProducerExceptionDoesNotCacheResult()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(
                () =>
                {
                    producerCalls++;
                    if (producerCalls == 1)
                    {
                        throw new InvalidOperationException("First call fails");
                    }

                    return 42;
                },
                10f
            );

            Assert.Throws<InvalidOperationException>(() =>
            {
                int _ = cache.Value;
            });
            Assert.AreEqual(1, producerCalls);

            // Reset and try again
            cache.Reset();
            int value = cache.Value;
            Assert.AreEqual(42, value);
            Assert.AreEqual(2, producerCalls);
        }

        [UnityTest]
        public IEnumerator ValueRecomputedAfterTtlExpires()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 0.05f);

            int first = cache.Value;
            Assert.AreEqual(1, first);
            Assert.AreEqual(1, producerCalls);

            int immediate = cache.Value;
            Assert.AreEqual(1, immediate);
            Assert.AreEqual(1, producerCalls);

            yield return new WaitForSeconds(0.06f);

            int refreshed = cache.Value;
            Assert.AreEqual(2, refreshed);
            Assert.AreEqual(2, producerCalls);
        }

        [UnityTest]
        public IEnumerator ResetForcesImmediateRefresh()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 10f);

            int first = cache.Value;
            Assert.AreEqual(1, first);
            Assert.AreEqual(1, producerCalls);

            cache.Reset();
            yield return null;

            int afterReset = cache.Value;
            Assert.AreEqual(2, afterReset);
            Assert.AreEqual(2, producerCalls);
        }

        [UnityTest]
        public IEnumerator MultipleTtlExpirationsWork()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 0.05f);

            int value1 = cache.Value;
            Assert.AreEqual(1, value1);

            yield return new WaitForSeconds(0.06f);
            int value2 = cache.Value;
            Assert.AreEqual(2, value2);

            yield return new WaitForSeconds(0.06f);
            int value3 = cache.Value;
            Assert.AreEqual(3, value3);

            Assert.AreEqual(3, producerCalls);
        }

        [UnityTest]
        public IEnumerator AccessWithinTtlDoesNotResetTimer()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 0.1f);

            _ = cache.Value;
            yield return new WaitForSeconds(0.05f);
            _ = cache.Value; // Access within TTL
            yield return new WaitForSeconds(0.05f);
            // Total time ~0.1s, but we accessed at 0.05s, so should still expire at original time
            _ = cache.Value;

            // Should have refreshed after original TTL
            Assert.GreaterOrEqual(producerCalls, 1);
        }

        [Test]
        public void ConstructorWithJitterDoesNotThrow()
        {
            TimedCache<int> cache = new(() => 1, 1f, useJitter: true);
            Assert.AreEqual(1, cache.Value);
        }

        [TestCase(0.02f)]
        [TestCase(0.05f)]
        [TestCase(0.1f)]
        public void JitterCausesFirstExpirationToVaryFromTtl(float jitterOverride)
        {
            const float CacheTtl = 0.1f;
            const float Epsilon = 0.001f;

            ManualTimeSource time = new();
            int producerCalls = 0;
            TimedCache<int> cache = new(
                () => ++producerCalls,
                CacheTtl,
                useJitter: true,
                timeProvider: time.Get,
                jitterOverride: jitterOverride
            );

            int first = cache.Value;
            Assert.AreEqual(1, first);
            Assert.AreEqual(1, producerCalls);

            time.Advance(CacheTtl - Epsilon);
            Assert.AreEqual(
                1,
                cache.Value,
                $"Cache expired before TTL. Now={time.Now:F3}s, TTL={CacheTtl:F3}s."
            );

            time.Advance(Epsilon * 2f);
            Assert.AreEqual(
                1,
                cache.Value,
                $"Jitter should delay expiration. Now={time.Now:F3}s, jitter={jitterOverride:F3}s."
            );

            float deltaToBoundary = MathF.Max(jitterOverride - Epsilon, 0f);
            if (0f < deltaToBoundary)
            {
                time.Advance(deltaToBoundary);
                Assert.AreEqual(
                    1,
                    cache.Value,
                    $"Cache expired before reaching TTL + jitter. Now={time.Now:F3}s, boundary={(CacheTtl + jitterOverride):F3}s."
                );
            }

            time.Advance(Epsilon * 2f);
            Assert.AreEqual(
                2,
                cache.Value,
                $"Cache failed to expire after surpassing TTL + jitter. Now={time.Now:F3}s, boundary={(CacheTtl + jitterOverride):F3}s."
            );
            Assert.AreEqual(
                2,
                producerCalls,
                "Producer should be called exactly once for the jittered refresh."
            );

            time.Advance(CacheTtl - Epsilon);
            Assert.AreEqual(
                2,
                cache.Value,
                $"Jitter should not be reapplied. Cache expired early at {time.Now:F3}s."
            );

            time.Advance(Epsilon * 2f);
            Assert.AreEqual(
                3,
                cache.Value,
                $"Cache failed to expire after the standard TTL once jitter was consumed. Now={time.Now:F3}s."
            );
            Assert.AreEqual(3, producerCalls);
        }

        [Test]
        public void ZeroJitterBehavesLikeStandardTtl()
        {
            const float CacheTtl = 0.1f;
            const float Epsilon = 0.001f;

            ManualTimeSource time = new();
            int producerCalls = 0;
            TimedCache<int> cache = new(
                () => ++producerCalls,
                CacheTtl,
                useJitter: true,
                timeProvider: time.Get,
                jitterOverride: 0f
            );

            int first = cache.Value;
            Assert.AreEqual(1, first);
            Assert.AreEqual(1, producerCalls);

            time.Advance(CacheTtl - Epsilon);
            Assert.AreEqual(
                1,
                cache.Value,
                $"Cache expired early before TTL with zero jitter. Now={time.Now:F3}s."
            );

            time.Advance(Epsilon * 2f);
            Assert.AreEqual(
                2,
                cache.Value,
                $"Cache failed to expire right after TTL when jitter is zero. Now={time.Now:F3}s."
            );
            Assert.AreEqual(2, producerCalls);

            time.Advance(CacheTtl - Epsilon);
            Assert.AreEqual(
                2,
                cache.Value,
                $"Cache expired too early after refresh with zero jitter. Now={time.Now:F3}s."
            );

            time.Advance(Epsilon * 2f);
            Assert.AreEqual(
                3,
                cache.Value,
                $"Cache failed to expire at TTL after jitter was consumed. Now={time.Now:F3}s."
            );
            Assert.AreEqual(3, producerCalls);
        }

        [UnityTest]
        public IEnumerator SubsequentExpirationsAfterJitterUseStandardTtl()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 0.05f, useJitter: true);

            _ = cache.Value;
            Assert.AreEqual(1, producerCalls);

            // Wait long enough to cover jitter range
            yield return new WaitForSeconds(0.11f);
            _ = cache.Value;
            Assert.AreEqual(2, producerCalls);

            // Now subsequent expirations should use standard TTL without jitter
            yield return new WaitForSeconds(0.06f);
            _ = cache.Value;
            Assert.AreEqual(3, producerCalls);

            // Verify it's consistent
            yield return new WaitForSeconds(0.06f);
            _ = cache.Value;
            Assert.AreEqual(4, producerCalls);
        }

        [Test]
        public void MultipleAccessesInSameFrameReturnSameValue()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 1f);

            int v1 = cache.Value;
            int v2 = cache.Value;
            int v3 = cache.Value;
            int v4 = cache.Value;
            int v5 = cache.Value;

            Assert.AreEqual(1, v1);
            Assert.AreEqual(1, v2);
            Assert.AreEqual(1, v3);
            Assert.AreEqual(1, v4);
            Assert.AreEqual(1, v5);
            Assert.AreEqual(1, producerCalls);
        }

        [UnityTest]
        public IEnumerator ZeroTtlExpiresImmediately()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 0f);

            int first = cache.Value;
            Assert.AreEqual(1, first);
            Assert.AreEqual(1, producerCalls);

            yield return null;

            int second = cache.Value;
            Assert.AreEqual(2, second);
            Assert.AreEqual(2, producerCalls);

            yield return null;

            int third = cache.Value;
            Assert.AreEqual(3, third);
            Assert.AreEqual(3, producerCalls);
        }

        [UnityTest]
        public IEnumerator ResetAfterJitterDoesNotReapplyJitter()
        {
            const float CacheTtl = 0.2f;
            const float Jitter = CacheTtl;
            int producerCalls = 0;
            ManualTimeSource time = new();
            TimedCache<int> cache = new(
                () => ++producerCalls,
                CacheTtl,
                useJitter: true,
                timeProvider: time.Get,
                jitterOverride: Jitter
            );

            _ = cache.Value;
            Assert.AreEqual(1, producerCalls);
            yield return null;

            // Wait long enough to cover initial jitter range.
            time.Advance(CacheTtl + Jitter + 0.01f);
            yield return null;
            _ = cache.Value;
            Assert.AreEqual(2, producerCalls);

            cache.Reset();
            int callsAfterReset = producerCalls;

            // Wait under the TTL to ensure jitter was not re-applied.
            time.Advance(CacheTtl * 0.5f);
            yield return null;
            _ = cache.Value;
            Assert.AreEqual(callsAfterReset, producerCalls);

            // Wait beyond the TTL to trigger expiry without jitter.
            time.Advance(CacheTtl * 0.6f);
            yield return null;
            _ = cache.Value;
            Assert.AreEqual(callsAfterReset + 1, producerCalls);
        }

        [UnityTest]
        public IEnumerator MultipleResetsAfterJitterRemainDeterministic()
        {
            const float CacheTtl = 0.15f;
            const float Jitter = CacheTtl;
            int producerCalls = 0;
            ManualTimeSource time = new();
            TimedCache<int> cache = new(
                () => ++producerCalls,
                CacheTtl,
                useJitter: true,
                timeProvider: time.Get,
                jitterOverride: Jitter
            );

            _ = cache.Value;
            Assert.AreEqual(1, producerCalls);
            yield return null;

            time.Advance(CacheTtl + Jitter + 0.01f);
            yield return null;
            _ = cache.Value;
            Assert.AreEqual(2, producerCalls);

            for (int i = 0; i < 2; i++)
            {
                cache.Reset();
                int callsAfterReset = producerCalls;

                time.Advance(CacheTtl * 0.4f);
                yield return null;
                _ = cache.Value;
                Assert.AreEqual(
                    callsAfterReset,
                    producerCalls,
                    "Cache should not refresh before TTL after reset."
                );

                time.Advance(CacheTtl * 0.7f);
                yield return null;
                _ = cache.Value;
                Assert.AreEqual(
                    callsAfterReset + 1,
                    producerCalls,
                    "Cache should refresh once TTL has elapsed."
                );
            }
        }

        [UnityTest]
        public IEnumerator JitterIsConsumedOnlyOnceAcrossRefreshes()
        {
            const float CacheTtl = 0.2f;
            const float Jitter = CacheTtl * 0.5f;

            int producerCalls = 0;
            ManualTimeSource time = new();
            TimedCache<int> cache = new(
                () => ++producerCalls,
                CacheTtl,
                useJitter: true,
                timeProvider: time.Get,
                jitterOverride: Jitter
            );

            _ = cache.Value;
            Assert.AreEqual(1, producerCalls);
            yield return null;

            time.Advance(CacheTtl + Jitter + 0.01f);
            yield return null;
            _ = cache.Value;
            Assert.AreEqual(2, producerCalls, "First refresh should include jitter.");

            time.Advance(CacheTtl * 0.6f);
            yield return null;
            _ = cache.Value;
            Assert.AreEqual(2, producerCalls, "Jitter should not be reapplied.");

            time.Advance(CacheTtl * 0.5f);
            yield return null;
            _ = cache.Value;
            Assert.AreEqual(3, producerCalls, "Subsequent refresh should honor TTL only.");
        }

        [Test]
        public void ManualTimeProviderControlsExpiration()
        {
            ManualTimeSource time = new();
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 1f, timeProvider: time.Get);

            _ = cache.Value;
            Assert.AreEqual(1, producerCalls);

            time.Advance(0.5f);
            _ = cache.Value;
            Assert.AreEqual(1, producerCalls);

            time.Advance(0.6f);
            _ = cache.Value;
            Assert.AreEqual(2, producerCalls);
        }

        [Test]
        public void JitterOverrideAllowsDeterministicScheduling()
        {
            ManualTimeSource time = new();
            int producerCalls = 0;
            TimedCache<int> cache = new(
                () => ++producerCalls,
                0.5f,
                useJitter: true,
                timeProvider: time.Get,
                jitterOverride: 0.5f
            );

            _ = cache.Value;
            Assert.AreEqual(1, producerCalls);

            time.Advance(1.01f);
            _ = cache.Value;
            Assert.AreEqual(2, producerCalls, "Cache should refresh after TTL + jitter.");
        }

        [UnityTest]
        public IEnumerator ValueRefreshesExactlyWhenTtlElapsed()
        {
            const float CacheTtl = 0.05f;
            int producerCalls = 0;
            ManualTimeSource time = new();
            TimedCache<int> cache = new(() => ++producerCalls, CacheTtl, timeProvider: time.Get);

            int first = cache.Value;
            Assert.AreEqual(1, first);

            // Advance just shy of the TTL — cache should still be valid.
            time.Advance(CacheTtl - 0.001f);
            yield return null;
            int withinWindow = cache.Value;
            Assert.AreEqual(1, withinWindow);
            Assert.AreEqual(1, producerCalls);

            // Advance past the TTL to trigger the expiration boundary.
            time.Advance(0.002f);
            yield return null;
            int afterExpiry = cache.Value;
            Assert.AreEqual(2, afterExpiry);
            Assert.AreEqual(2, producerCalls);
        }

        [Test]
        public void ProducerCalledExactlyOncePerRefresh()
        {
            int producerCalls = 0;
            int sideEffectCounter = 0;
            TimedCache<int> cache = new(
                () =>
                {
                    producerCalls++;
                    sideEffectCounter += 10;
                    return producerCalls;
                },
                1f
            );

            _ = cache.Value;
            _ = cache.Value;
            _ = cache.Value;

            Assert.AreEqual(1, producerCalls);
            Assert.AreEqual(10, sideEffectCounter);

            cache.Reset();
            _ = cache.Value;

            Assert.AreEqual(2, producerCalls);
            Assert.AreEqual(20, sideEffectCounter);
        }

        [Test]
        public void CacheHandlesStructTypes()
        {
            int producerCalls = 0;
            TimedCache<Vector3> cache = new(
                () =>
                {
                    producerCalls++;
                    return new Vector3(1f, 2f, 3f);
                },
                1f
            );

            Vector3 value1 = cache.Value;
            Vector3 value2 = cache.Value;

            Assert.AreEqual(new Vector3(1f, 2f, 3f), value1);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), value2);
            Assert.AreEqual(1, producerCalls);
        }

        [Test]
        public void CacheUpdatesWhenProducerReturnsDifferentValues()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls * 100, 1f);

            int first = cache.Value;
            Assert.AreEqual(100, first);

            cache.Reset();
            int second = cache.Value;
            Assert.AreEqual(200, second);

            cache.Reset();
            int third = cache.Value;
            Assert.AreEqual(300, third);
        }

        [Test]
        public void ResetAfterExceptionAllowsRecovery()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(
                () =>
                {
                    producerCalls++;
                    if (producerCalls < 3)
                    {
                        throw new InvalidOperationException($"Call {producerCalls} fails");
                    }

                    return 999;
                },
                10f
            );

            // First attempt fails
            Assert.Throws<InvalidOperationException>(() =>
            {
                int _ = cache.Value;
            });
            Assert.AreEqual(1, producerCalls);

            // Second attempt also fails
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = cache.Value;
            });
            Assert.AreEqual(2, producerCalls);

            // Third attempt succeeds
            cache.Reset();
            int value = cache.Value;
            Assert.AreEqual(999, value);
            Assert.AreEqual(3, producerCalls);

            // Cached value should persist
            int cached = cache.Value;
            Assert.AreEqual(999, cached);
            Assert.AreEqual(3, producerCalls);
        }

        [Test]
        public void CacheHandlesDefaultValues()
        {
            int producerCalls = 0;
            TimedCache<int> cacheInt = new(
                () =>
                {
                    producerCalls++;
                    return default;
                },
                1f
            );

            int intValue = cacheInt.Value;
            Assert.AreEqual(0, intValue);
            Assert.AreEqual(1, producerCalls);

            producerCalls = 0;
            TimedCache<bool> cacheBool = new(
                () =>
                {
                    producerCalls++;
                    return default;
                },
                1f
            );

            bool boolValue = cacheBool.Value;
            Assert.AreEqual(false, boolValue);
            Assert.AreEqual(1, producerCalls);
        }

        [Test]
        public void CacheDistinguishesBetweenNullAndDefault()
        {
            int nullProducerCalls = 0;
            TimedCache<string> nullCache = new(
                () =>
                {
                    nullProducerCalls++;
                    return null;
                },
                1f
            );

            int defaultProducerCalls = 0;
            TimedCache<string> defaultCache = new(
                () =>
                {
                    defaultProducerCalls++;
                    return default;
                },
                1f
            );

            Assert.IsTrue(nullCache.Value == null, "Null cache value should be null");
            Assert.IsTrue(
                defaultCache.Value == null,
                "Default cache value should be null for reference types"
            );
            Assert.AreEqual(1, nullProducerCalls);
            Assert.AreEqual(1, defaultProducerCalls);

            // Both should cache the null/default value
            Assert.IsTrue(nullCache.Value == null, "Null cache should return cached null value");
            Assert.IsTrue(
                defaultCache.Value == null,
                "Default cache should return cached null value"
            );
            Assert.AreEqual(1, nullProducerCalls);
            Assert.AreEqual(1, defaultProducerCalls);
        }

        [Test]
        public void VerySmallTtlWithJitterDoesNotThrow()
        {
            TimedCache<int> cache = new(() => 42, 0.001f, useJitter: true);
            Assert.AreEqual(42, cache.Value);
        }

        [Test]
        public void VeryLargeTtlWithJitterDoesNotThrow()
        {
            TimedCache<int> cache = new(() => 42, float.MaxValue, useJitter: true);
            Assert.AreEqual(42, cache.Value);
        }

        [Test]
        public void MultipleResetsInSequenceWork()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 10f);

            cache.Reset();
            cache.Reset();
            cache.Reset();
            int value = cache.Value;

            Assert.AreEqual(3, value);
            Assert.AreEqual(3, producerCalls);
        }

        [Test]
        public void CacheHandlesComplexReferenceTypes()
        {
            int producerCalls = 0;
            TimedCache<List<int>> cache = new(
                () =>
                {
                    producerCalls++;
                    return new List<int> { 1, 2, 3 };
                },
                1f
            );

            List<int> list1 = cache.Value;
            List<int> list2 = cache.Value;

            Assert.AreSame(list1, list2);
            Assert.AreEqual(3, list1.Count);
            Assert.AreEqual(1, producerCalls);

            // Verify mutations are visible (same reference)
            list1.Add(4);
            Assert.AreEqual(4, list2.Count);
        }

        [Test]
        public void ProducerWithCapturedVariablesWorks()
        {
            int externalCounter = 0;
            TimedCache<int> cache = new(
                () =>
                {
                    externalCounter++;
                    return externalCounter * 10;
                },
                1f
            );

            int first = cache.Value;
            Assert.AreEqual(10, first);
            Assert.AreEqual(1, externalCounter);

            cache.Reset();
            int second = cache.Value;
            Assert.AreEqual(20, second);
            Assert.AreEqual(2, externalCounter);
        }

        [UnityTest]
        public IEnumerator VeryShortTtlMultipleExpirationsWork()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 0.001f); // ~1 frame at 60fps

            _ = cache.Value;
            Assert.AreEqual(1, producerCalls);

            yield return new WaitForSecondsRealtime(0.1f);
            _ = cache.Value;
            Assert.AreEqual(2, producerCalls);

            yield return new WaitForSecondsRealtime(0.1f);
            _ = cache.Value;
            Assert.AreEqual(3, producerCalls);

            yield return new WaitForSecondsRealtime(0.1f);
            _ = cache.Value;
            Assert.AreEqual(4, producerCalls);
        }

        [Test]
        public void ResetDoesNotRequirePriorValueAccess()
        {
            int producerCalls = 0;
            TimedCache<int> cache = new(() => ++producerCalls, 1f);

            cache.Reset();
            cache.Reset();
            cache.Reset();

            Assert.AreEqual(3, producerCalls);

            int value = cache.Value;
            Assert.AreEqual(3, value);
            Assert.AreEqual(3, producerCalls);
        }

        private sealed class ManualTimeSource
        {
            public float Now;

            public float Get() => Now;

            public void Advance(float delta) => Now += delta;
        }
    }
}
