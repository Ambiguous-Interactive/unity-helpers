// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [Category("Fast")]
    public sealed class CacheAdmissionOwnershipTests
    {
        [Test]
        public void FailedAdmissionPreservesOwnership(
            [Values(
                EvictionPolicy.Lru,
                EvictionPolicy.Slru,
                EvictionPolicy.Lfu,
                EvictionPolicy.Fifo,
                EvictionPolicy.Random
            )]
                EvictionPolicy policy,
            [Values] bool lifetimeFailure,
            [Values] bool useLoader,
            [Values] bool retainedReference
        )
        {
            using MemoryStream retained = new();
            using MemoryStream distinct = new();
            MemoryStream incoming = retainedReference ? retained : distinct;
            InvalidOperationException sentinel = new("admission failed");
            int factories = 0;
            int sets = 0;
            int incomingReleases = 0;
            int retainedReleases = 0;
            int reentrantChecks = 0;
            Cache<string, MemoryStream> cache = null;
            Func<string, MemoryStream> factory = _ =>
            {
                ++factories;
                return incoming;
            };
            cache = CacheBuilder<string, MemoryStream>
                .NewBuilder()
                .MaximumWeight(10)
                .EvictionPolicy(policy)
                .TimeProvider(() => 1)
                .Weigher(
                    (key, _) =>
                    {
                        if (string.Equals(key, "new", StringComparison.Ordinal) && !lifetimeFailure)
                        {
                            throw sentinel;
                        }
                        return 1;
                    }
                )
                .ExpireAfter(
                    (key, _) =>
                    {
                        if (string.Equals(key, "new", StringComparison.Ordinal) && lifetimeFailure)
                        {
                            throw sentinel;
                        }
                        return 100;
                    }
                )
                .OnSet((_, _) => ++sets)
                .OnEviction(
                    (_, value, reason) =>
                    {
                        if (ReferenceEquals(value, retained))
                        {
                            ++retainedReleases;
                        }
                        else
                        {
                            ++incomingReleases;
                            Assert.AreEqual(EvictionReason.Replaced, reason);
                            Assert.IsTrue(cache.TryGet("old", out MemoryStream current));
                            Assert.AreSame(retained, current);
                            ++reentrantChecks;
                        }
                        value.Dispose();
                    }
                )
                .Build(factory);
            using (cache)
            {
                cache.Set("old", retained);
                Assert.AreSame(
                    sentinel,
                    Assert.Throws<InvalidOperationException>(() =>
                        cache.GetOrAdd("new", useLoader ? null : factory)
                    )
                );
                Assert.AreEqual(1, factories);
                Assert.AreEqual(1, sets);
                Assert.AreEqual(1, cache.Count);
                Assert.IsTrue(cache.TryGet("old", out MemoryStream current));
                Assert.AreSame(retained, current);
                Assert.IsFalse(cache.ContainsKey("new"));
                Assert.AreEqual(retainedReference ? 0 : 1, incomingReleases);
                Assert.AreEqual(retainedReference ? 0 : 1, reentrantChecks);
                Assert.AreEqual(0, retainedReleases);
                Assert.AreEqual(retainedReference, incoming.CanRead);
                Assert.IsTrue(retained.CanRead);
            }
            Assert.AreEqual(1, retainedReleases);
            Assert.AreEqual(retainedReference ? 0 : 1, incomingReleases);
            Assert.IsFalse(incoming.CanRead);
        }

        [Test]
        public void FailedDirectSetKeepsCallerOwnership([Values] bool lifetimeFailure)
        {
            using MemoryStream incoming = new();
            InvalidOperationException sentinel = new("admission failed");
            int releases = 0;
            using (
                Cache<string, MemoryStream> cache = CacheBuilder<string, MemoryStream>
                    .NewBuilder()
                    .MaximumWeight(10)
                    .Weigher(
                        (_, _) =>
                        {
                            if (!lifetimeFailure)
                            {
                                throw sentinel;
                            }
                            return 1;
                        }
                    )
                    .ExpireAfter((_, _) => throw sentinel)
                    .OnEviction(
                        (_, value, _) =>
                        {
                            ++releases;
                            value.Dispose();
                        }
                    )
                    .Build()
            )
            {
                Assert.AreSame(
                    sentinel,
                    Assert.Throws<InvalidOperationException>(() => cache.Set("new", incoming))
                );
                Assert.AreEqual(0, cache.Count);
            }
            Assert.AreEqual(0, releases);
            Assert.IsTrue(incoming.CanRead);
        }

        [Test]
        public void ThrowingRetirementCallbackPreservesAdmissionFailure()
        {
            using MemoryStream incoming = new();
            InvalidOperationException sentinel = new("admission failed");
            int releases = 0;
            Cache<string, MemoryStream> cache = null;
            cache = CacheBuilder<string, MemoryStream>
                .NewBuilder()
                .Weigher((_, _) => throw sentinel)
                .OnEviction(
                    (_, value, _) =>
                    {
                        ++releases;
                        value.Dispose();
                        cache.Dispose();
                        throw new InvalidOperationException("retirement failed");
                    }
                )
                .Build(_ => incoming);
            using (cache)
            {
                Assert.AreSame(
                    sentinel,
                    Assert.Throws<InvalidOperationException>(() => cache.GetOrAdd("new", null))
                );
            }
            Assert.AreEqual(1, releases);
            Assert.IsFalse(incoming.CanRead);
        }

        [Test]
        public void ConcurrentAdmissionRetiresOnlyTheFailedResource([Values] bool lifetimeFailure)
        {
            using MemoryStream failed = new();
            using MemoryStream accepted = new();
            using Barrier barrier = new(2);
            InvalidOperationException sentinel = new("admission failed");
            int failedReleases = 0;
            int acceptedReleases = 0;
            using Cache<string, MemoryStream> cache = CacheBuilder<string, MemoryStream>
                .NewBuilder()
                .MaximumWeight(10)
                .Weigher(
                    (_, value) =>
                    {
                        if (ReferenceEquals(value, failed) && !lifetimeFailure)
                        {
                            throw sentinel;
                        }
                        return 1;
                    }
                )
                .ExpireAfter(
                    (_, value) =>
                    {
                        if (ReferenceEquals(value, failed) && lifetimeFailure)
                        {
                            throw sentinel;
                        }
                        return 100;
                    }
                )
                .OnEviction(
                    (_, value, _) =>
                    {
                        if (ReferenceEquals(value, failed))
                        {
                            Interlocked.Increment(ref failedReleases);
                        }
                        else
                        {
                            Interlocked.Increment(ref acceptedReleases);
                        }
                        value.Dispose();
                    }
                )
                .Build();
            Task<MemoryStream> failing = Task.Run(() =>
            {
                try
                {
                    return cache.GetOrAdd(
                        "key",
                        _ =>
                        {
                            Assert.IsTrue(barrier.SignalAndWait(TimeSpan.FromSeconds(5)));
                            return failed;
                        }
                    );
                }
                catch (InvalidOperationException exception)
                {
                    Assert.AreSame(sentinel, exception);
                    return null;
                }
            });
            Task<MemoryStream> succeeding = Task.Run(() =>
                cache.GetOrAdd(
                    "key",
                    _ =>
                    {
                        Assert.IsTrue(barrier.SignalAndWait(TimeSpan.FromSeconds(5)));
                        return accepted;
                    }
                )
            );
            Assert.IsTrue(
                Task.WaitAll(new Task[] { failing, succeeding }, TimeSpan.FromSeconds(10))
            );
            Assert.AreSame(accepted, succeeding.Result);
            Assert.IsTrue(failing.Result == null || ReferenceEquals(failing.Result, accepted));
            Assert.IsTrue(cache.TryGet("key", out MemoryStream current));
            Assert.AreSame(accepted, current);
            Assert.AreEqual(1, cache.Count);
            Assert.AreEqual(1, failedReleases);
            Assert.AreEqual(0, acceptedReleases);
            Assert.IsFalse(failed.CanRead);
            Assert.IsTrue(accepted.CanRead);
            cache.Dispose();
            Assert.AreEqual(1, failedReleases);
            Assert.AreEqual(1, acceptedReleases);
        }
    }
}
