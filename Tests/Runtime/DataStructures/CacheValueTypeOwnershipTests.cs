// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [Category("Fast")]
    public sealed class CacheValueTypeOwnershipTests
    {
        private static void AssertValueReplacement<T>(T retained, T incoming, bool equivalent)
        {
            List<T> released = new();
            using Cache<string, T> cache = CacheBuilder<string, T>
                .NewBuilder()
                .MaximumSize(2)
                .TimeProvider(static () => 1)
                .OnEviction((_, value, _) => released.Add(value))
                .Build();
            cache.Set("key", retained);
            cache.Set("key", incoming);
            Assert.AreEqual(equivalent ? 0 : 1, released.Count);
            if (!equivalent)
            {
                Assert.AreEqual(retained, released[0]);
            }
            Assert.IsTrue(cache.TryGet("key", out T current));
            Assert.AreEqual(incoming, current);
        }

        [Test]
        public void TypeTraitsFollowDeclaredConsumerType()
        {
            Assert.IsTrue(ObjectsTypeTraits<int>.IsValueType);
            Assert.IsTrue(ObjectsTypeTraits<int?>.IsValueType);
            Assert.IsTrue(ObjectsTypeTraits<EvictionReason>.IsValueType);
            Assert.IsTrue(ObjectsTypeTraits<KeyValuePair<string, object>>.IsValueType);
            Assert.IsFalse(ObjectsTypeTraits<object>.IsValueType);
            Assert.IsFalse(ObjectsTypeTraits<IComparable>.IsValueType);
            Assert.IsFalse(ObjectsTypeTraits<string>.IsValueType);
            Assert.IsFalse(ObjectsTypeTraits<UnityEngine.GameObject>.IsValueType);
        }

        [Test]
        public void ValueReplacementUsesEquality([Values] bool equivalent)
        {
            AssertValueReplacement(7, equivalent ? 7 : 8, equivalent);
            AssertValueReplacement<int?>(7, equivalent ? 7 : 8, equivalent);
            AssertValueReplacement<int?>(null, equivalent ? null : 8, equivalent);
            AssertValueReplacement(
                EvictionReason.Replaced,
                equivalent ? EvictionReason.Replaced : EvictionReason.Capacity,
                equivalent
            );
            AssertValueReplacement(
                new KeyValuePair<string, int>("key", 7),
                new KeyValuePair<string, int>("key", equivalent ? 7 : 8),
                equivalent
            );
        }

        [Test]
        public void ReferenceReplacementUsesIdentity(
            [Values] bool sameReference,
            [Values] bool boxedValue
        )
        {
            object retained = boxedValue
                ? (object)7
                : new string(new[] { 'v', 'a', 'l', 'u', 'e' });
            object incoming =
                sameReference ? retained
                : boxedValue ? (object)7
                : new string(new[] { 'v', 'a', 'l', 'u', 'e' });
            Assert.AreEqual(retained, incoming);
            List<object> released = new();
            using Cache<string, object> cache = CacheBuilder<string, object>
                .NewBuilder()
                .MaximumSize(2)
                .TimeProvider(static () => 1)
                .OnEviction((_, value, _) => released.Add(value))
                .Build();
            cache.Set("key", retained);
            cache.Set("key", incoming);
            Assert.AreEqual(sameReference ? 0 : 1, released.Count);
            if (!sameReference)
            {
                Assert.AreSame(retained, released[0]);
            }
            Assert.IsTrue(cache.TryGet("key", out object current));
            Assert.AreSame(incoming, current);
        }

        [Test]
        public void FailedReferenceAdmissionPreservesIdentityOwnership(
            [Values] bool sameReference,
            [Values] bool boxedValue
        )
        {
            object retained = boxedValue
                ? (object)7
                : new string(new[] { 'v', 'a', 'l', 'u', 'e' });
            object incoming =
                sameReference ? retained
                : boxedValue ? (object)7
                : new string(new[] { 'v', 'a', 'l', 'u', 'e' });
            List<object> released = new();
            InvalidOperationException sentinel = new("admission refused");
            using Cache<string, object> cache = CacheBuilder<string, object>
                .NewBuilder()
                .MaximumSize(2)
                .TimeProvider(static () => 1)
                .ExpireAfter(
                    (key, _) =>
                    {
                        if (string.Equals(key, "incoming", StringComparison.Ordinal))
                        {
                            throw sentinel;
                        }
                        return 100;
                    }
                )
                .OnEviction((_, value, _) => released.Add(value))
                .Build();
            cache.Set("retained", retained);
            Assert.AreSame(
                sentinel,
                Assert.Throws<InvalidOperationException>(() =>
                    cache.GetOrAdd("incoming", _ => incoming)
                )
            );
            Assert.AreEqual(sameReference ? 0 : 1, released.Count);
            if (!sameReference)
            {
                Assert.AreSame(incoming, released[0]);
            }
            Assert.AreEqual(1, cache.Count);
            Assert.IsTrue(cache.TryGet("retained", out object current));
            Assert.AreSame(retained, current);
        }

        [Test]
        public void FailedValueAdmissionRetiresEvenEqualValues([Values] bool nullValue)
        {
            int? retained = nullValue ? null : 7;
            List<int?> released = new();
            InvalidOperationException sentinel = new("admission refused");
            using Cache<string, int?> cache = CacheBuilder<string, int?>
                .NewBuilder()
                .MaximumSize(2)
                .TimeProvider(static () => 1)
                .ExpireAfter(
                    (key, _) =>
                    {
                        if (string.Equals(key, "incoming", StringComparison.Ordinal))
                        {
                            throw sentinel;
                        }
                        return 100;
                    }
                )
                .OnEviction((_, value, _) => released.Add(value))
                .Build();
            cache.Set("retained", retained);
            Assert.AreSame(
                sentinel,
                Assert.Throws<InvalidOperationException>(() =>
                    cache.GetOrAdd("incoming", _ => retained)
                )
            );
            CollectionAssert.AreEqual(new[] { retained }, released);
            Assert.AreEqual(1, cache.Count);
            Assert.IsTrue(cache.TryGet("retained", out int? current));
            Assert.AreEqual(retained, current);
        }

        [Test]
        public void NullReferenceReplacementKeepsOwnership()
        {
            int releases = 0;
            using Cache<string, object> cache = CacheBuilder<string, object>
                .NewBuilder()
                .MaximumSize(2)
                .TimeProvider(static () => 1)
                .OnEviction((_, _, _) => ++releases)
                .Build();
            cache.Set("key", null);
            cache.Set("key", null);
            Assert.AreEqual(0, releases);
            Assert.IsTrue(cache.TryGet("key", out object current));
            Assert.IsTrue(current is null);
        }
    }
}
