// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [Category("Fast")]
    public sealed class CacheAdmissionFailurePhaseTests
    {
        [Test]
        public void PostFactoryLookupFailureRetiresOnlyUnretainedResult(
            [Values] bool retainedReference
        )
        {
            using MemoryStream retained = new();
            using MemoryStream winner = new();
            using MemoryStream distinct = new();
            MemoryStream incoming = retainedReference ? retained : distinct;
            InvalidOperationException sentinel = new("lookup failed");
            bool failLookup = false;
            int retainedReleases = 0;
            int winnerReleases = 0;
            int incomingReleases = 0;
            EvictionReason incomingReason = default;
            Cache<string, MemoryStream> cache = null;
            cache = CacheBuilder<string, MemoryStream>
                .NewBuilder()
                .MaximumSize(3)
                .TimeProvider(() => failLookup ? throw sentinel : 1)
                .OnEviction(
                    (_, value, reason) =>
                    {
                        if (ReferenceEquals(value, retained))
                        {
                            ++retainedReleases;
                        }
                        else if (ReferenceEquals(value, winner))
                        {
                            ++winnerReleases;
                        }
                        else
                        {
                            ++incomingReleases;
                            incomingReason = reason;
                        }
                        value.Dispose();
                    }
                )
                .Build();
            using (cache)
            {
                cache.Set("old", retained);
                Assert.AreSame(
                    sentinel,
                    Assert.Throws<InvalidOperationException>(() =>
                        cache.GetOrAdd(
                            "new",
                            _ =>
                            {
                                cache.Set("new", winner);
                                failLookup = true;
                                return incoming;
                            }
                        )
                    )
                );
                failLookup = false;
                Assert.AreEqual(2, cache.Count);
                Assert.IsTrue(cache.TryGet("old", out MemoryStream oldValue));
                Assert.AreSame(retained, oldValue);
                Assert.IsTrue(cache.TryGet("new", out MemoryStream currentWinner));
                Assert.AreSame(winner, currentWinner);
                Assert.AreEqual(0, retainedReleases);
                Assert.AreEqual(0, winnerReleases);
                Assert.AreEqual(retainedReference ? 0 : 1, incomingReleases);
                if (!retainedReference)
                {
                    Assert.AreEqual(EvictionReason.Replaced, incomingReason);
                }
                Assert.AreEqual(retainedReference, incoming.CanRead);
                Assert.IsTrue(retained.CanRead);
                Assert.IsTrue(winner.CanRead);
            }
            Assert.AreEqual(1, retainedReleases);
            Assert.AreEqual(1, winnerReleases);
            Assert.AreEqual(retainedReference ? 0 : 1, incomingReleases);
            Assert.IsFalse(incoming.CanRead);
        }

        [Test]
        public void FailureAfterEvictionRetiresEachReferenceExactlyOnce(
            [Values(
                EvictionPolicy.Lru,
                EvictionPolicy.Slru,
                EvictionPolicy.Lfu,
                EvictionPolicy.Fifo,
                EvictionPolicy.Random
            )]
                EvictionPolicy policy,
            [Values] bool previouslyRetainedReference
        )
        {
            using MemoryStream expired = new();
            using MemoryStream survivor = new();
            using MemoryStream distinct = new();
            MemoryStream incoming = previouslyRetainedReference ? expired : distinct;
            InvalidOperationException sentinel = new("victim selection failed");
            bool failSelection = false;
            int admissionTimeCalls = 0;
            int expiredReleases = 0;
            int survivorReleases = 0;
            int incomingReleases = 0;
            EvictionReason expiredReason = default;
            EvictionReason incomingReason = default;
            using (
                Cache<string, MemoryStream> cache = CacheBuilder<string, MemoryStream>
                    .NewBuilder()
                    .MaximumWeight(2)
                    .EvictionPolicy(policy)
                    .Weigher(
                        (key, _) => string.Equals(key, "new", StringComparison.Ordinal) ? 2 : 1
                    )
                    .ExpireAfter(
                        (key, _) => string.Equals(key, "old", StringComparison.Ordinal) ? 10 : 1000
                    )
                    .TimeProvider(() =>
                    {
                        if (!failSelection)
                        {
                            return 1;
                        }
                        ++admissionTimeCalls;
                        if (admissionTimeCalls == 3)
                        {
                            throw sentinel;
                        }
                        return 100;
                    })
                    .OnEviction(
                        (_, value, reason) =>
                        {
                            if (ReferenceEquals(value, expired))
                            {
                                ++expiredReleases;
                                expiredReason = reason;
                            }
                            else if (ReferenceEquals(value, survivor))
                            {
                                ++survivorReleases;
                            }
                            else
                            {
                                ++incomingReleases;
                                incomingReason = reason;
                            }
                            value.Dispose();
                        }
                    )
                    .Build()
            )
            {
                cache.Set("old", expired);
                cache.Set("survivor", survivor);
                Assert.AreSame(
                    sentinel,
                    Assert.Throws<InvalidOperationException>(() =>
                        cache.GetOrAdd(
                            "new",
                            _ =>
                            {
                                failSelection = true;
                                return incoming;
                            }
                        )
                    )
                );
                failSelection = false;
                Assert.AreEqual(3, admissionTimeCalls);
                Assert.AreEqual(1, cache.Count);
                Assert.AreEqual(1, cache.Size);
                Assert.IsFalse(cache.ContainsKey("old"));
                Assert.IsFalse(cache.ContainsKey("new"));
                Assert.IsTrue(cache.TryGet("survivor", out MemoryStream current));
                Assert.AreSame(survivor, current);
                Assert.AreEqual(1, expiredReleases);
                Assert.AreEqual(EvictionReason.Capacity, expiredReason);
                Assert.AreEqual(0, survivorReleases);
                Assert.AreEqual(previouslyRetainedReference ? 0 : 1, incomingReleases);
                if (!previouslyRetainedReference)
                {
                    Assert.AreEqual(EvictionReason.Replaced, incomingReason);
                }
                Assert.IsFalse(incoming.CanRead);
                Assert.IsTrue(survivor.CanRead);
            }
            Assert.AreEqual(1, expiredReleases);
            Assert.AreEqual(1, survivorReleases);
            Assert.AreEqual(previouslyRetainedReference ? 0 : 1, incomingReleases);
        }
    }
}
