// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ObjectsTests : CommonTestBase
    {
        [UnityTest]
        public IEnumerator NullReturnsTrueForNullUnityObject()
        {
            GameObject nullObject = null;

            bool result = Objects.Null(nullObject);

            Assert.IsTrue(result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NullReturnsFalseForValidUnityObject()
        {
            GameObject gameObject = Track(new GameObject("Test"));

            bool result = Objects.Null(gameObject);

            Assert.IsFalse(result);
            Object.Destroy(gameObject); // UNH-SUPPRESS: Test cleanup
            yield return null;
        }

        [UnityTest]
        public IEnumerator NullReturnsTrueForDestroyedUnityObject()
        {
            GameObject gameObject = Track(new GameObject("Test"));
            Object.Destroy(gameObject); // UNH-SUPPRESS: Test verifies null detection after destruction
            yield return null;

            bool result = Objects.Null(gameObject);

            Assert.IsTrue(result);
        }

        [UnityTest]
        public IEnumerator NullDetectsDestroyedUnityObjectWhenBoxed()
        {
            GameObject gameObject = Track(new GameObject("Test"));
            object boxed = gameObject;
            Object.Destroy(gameObject); // UNH-SUPPRESS: Test verifies null detection after destruction when boxed
            yield return null;

            bool result = Objects.Null(boxed);

            Assert.IsTrue(result);
        }

        [Test]
        public void NullReturnsTrueForNullSystemObject()
        {
            object nullObject = null;

            bool result = Objects.Null(nullObject);

            Assert.IsTrue(result);
        }

        [Test]
        public void NullReturnsFalseForValidSystemObject()
        {
            object obj = new();

            bool result = Objects.Null(obj);

            Assert.IsFalse(result);
        }

        [Test]
        public void NullReturnsFalseForBoxedValueType()
        {
            object boxedInt = 42;

            bool result = Objects.Null(boxedInt);

            Assert.IsFalse(result);
        }

        [UnityTest]
        public IEnumerator NotNullReturnsFalseForNullUnityObject()
        {
            GameObject nullObject = null;

            bool result = Objects.NotNull(nullObject);

            Assert.IsFalse(result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NotNullReturnsTrueForValidUnityObject()
        {
            GameObject gameObject = Track(new GameObject("Test"));

            bool result = Objects.NotNull(gameObject);

            Assert.IsTrue(result);
            Object.Destroy(gameObject); // UNH-SUPPRESS: Test cleanup
            yield return null;
        }

        [UnityTest]
        public IEnumerator NotNullReturnsFalseForDestroyedUnityObject()
        {
            GameObject gameObject = Track(new GameObject("Test"));
            Object.Destroy(gameObject); // UNH-SUPPRESS: Test verifies NotNull detection after destruction
            yield return null;

            bool result = Objects.NotNull(gameObject);

            Assert.IsFalse(result);
        }

        [UnityTest]
        public IEnumerator NotNullDetectsDestroyedUnityObjectWhenBoxed()
        {
            GameObject gameObject = Track(new GameObject("Test"));
            object boxed = gameObject;
            Object.Destroy(gameObject); // UNH-SUPPRESS: Test verifies NotNull detection after destruction when boxed
            yield return null;

            bool result = Objects.NotNull(boxed);

            Assert.IsFalse(result);
        }

        [Test]
        public void NotNullReturnsFalseForNullSystemObject()
        {
            object nullObject = null;
            bool result = Objects.NotNull(nullObject);
            Assert.IsFalse(result);
        }

        [Test]
        public void NotNullReturnsTrueForValidSystemObject()
        {
            object obj = new();
            bool result = Objects.NotNull(obj);
            Assert.IsTrue(result);
        }

        [Test]
        public void HashCodeReturnsZeroForNullReference()
        {
            int result = Objects.HashCode((object)null);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void HashCodeReturnsZeroForNullReferenceUnityObject()
        {
            int result = Objects.HashCode<object>(null);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void HashCodeReturnsZeroForNullReferenceUnityObject2()
        {
            int result = Objects.HashCode((Object)null);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void HashCodeDistinguishesDifferentObjects()
        {
            CustomClass a = new() { Value = 1 };
            CustomClass b = new() { Value = 2 };

            int first = Objects.HashCode(a, b);
            int second = Objects.HashCode(b, a);

            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void HashCodeSpanMatchesVariadic()
        {
            CustomClass a = new() { Value = 1 };
            CustomClass b = new() { Value = 2 };
            CustomClass c = new() { Value = 3 };
            CustomClass[] array = { a, b, c };
            ReadOnlySpan<CustomClass> span = array;

            int spanHash = Objects.SpanHashCode(span);
            int variadicHash = Objects.HashCode(a, b, c);

            Assert.AreEqual(variadicHash, spanHash);
        }

        [Test]
        public void HashCodeReturnsDeterministicValueForIntegers()
        {
            const int expected = 1456420779;

            int first = Objects.HashCode(1, 2, 3);
            int second = Objects.HashCode(1, 2, 3);

            Assert.AreEqual(expected, first);
            Assert.AreEqual(expected, second);
        }

        [Test]
        public void HashCodeIncludesNullOrderingDeterministically()
        {
            const int expected = 1175002007;
            int hash = Objects.HashCode<object, int, int>(null, 1, 2);
            Assert.AreEqual(expected, hash);
        }

        [Test]
        public void HashCodeSupportsUpToTwentyParameters()
        {
            int[] values =
            {
                1,
                2,
                3,
                4,
                5,
                6,
                7,
                8,
                9,
                10,
                11,
                12,
                13,
                14,
                15,
                16,
                17,
                18,
                19,
                20,
            };

            int spanHash = Objects.SpanHashCode<int>(values);
            int variadicHash = Objects.HashCode(
                1,
                2,
                3,
                4,
                5,
                6,
                7,
                8,
                9,
                10,
                11,
                12,
                13,
                14,
                15,
                16,
                17,
                18,
                19,
                20
            );

            Assert.AreEqual(spanHash, variadicHash);
        }

        [Test]
        public void EnumerableHashCodeMatchesDeterministicHash()
        {
            const int expected = 1456420779;
            int enumerableHash = Objects.EnumerableHashCode(new[] { 1, 2, 3 });
            Assert.AreEqual(expected, enumerableHash);
        }

        [Test]
        public void EnumerableHashCodeReturnsZeroForNull()
        {
            Assert.AreEqual(0, Objects.EnumerableHashCode<int>(null));
        }

        [Test]
        public void EnumerableHashCodeRespectsOrder()
        {
            CustomEnumerable ascending = new(1, 2, 3);
            CustomEnumerable descending = new(3, 2, 1);

            int ascHash = Objects.EnumerableHashCode(ascending);
            int descHash = Objects.EnumerableHashCode(descending);

            Assert.AreNotEqual(ascHash, descHash);
        }

        [Test]
        public void EnumerableHashCodeDisposesEnumerator()
        {
            DisposableEnumerable enumerable = new(1, 2, 3);

            int result = Objects.EnumerableHashCode(enumerable);

            Assert.AreNotEqual(0, result);
            Assert.IsTrue(enumerable.LastEnumerator.WasDisposed);
        }

        [UnityTest]
        public IEnumerator EnumerableHashCodeHandlesDestroyedUnityObjects()
        {
            GameObject go = Track(new GameObject("HashDestroyed"));
            List<GameObject> objects = new() { go };

            int hashBefore = Objects.EnumerableHashCode(objects);
            Assert.AreNotEqual(0, hashBefore);

            Object.Destroy(go); // UNH-SUPPRESS: Test verifies hash behavior after destruction
            yield return null;

            int hashAfter = Objects.EnumerableHashCode(objects);
            Assert.AreEqual(0, hashAfter, "Destroyed Unity objects should hash as null.");
        }

        [Test]
        public void EnumerableHashCodeSupportsCommonCollections()
        {
            SortedSet<int> sorted = new() { 1, 2, 3 };
            Queue<int> queue = new(new[] { 1, 2, 3 });
            Stack<int> stack = new(new[] { 1, 2, 3 });

            int sortedHash = Objects.EnumerableHashCode(sorted);
            int queueHash = Objects.EnumerableHashCode(queue);
            int stackHash = Objects.EnumerableHashCode(stack);

            Assert.AreNotEqual(0, sortedHash);
            Assert.AreNotEqual(0, queueHash);
            Assert.AreNotEqual(0, stackHash);
        }

        [Test]
        public void SpanHashCodeReturnsZeroForEmptySpan()
        {
            ReadOnlySpan<int> empty = ReadOnlySpan<int>.Empty;
            Assert.AreEqual(0, Objects.SpanHashCode(empty));
        }

        [Test]
        public void SpanHashCodeRespectsCustomStructHashCodes()
        {
            CustomStruct[] values = { new(1), new(2), new(3) };
            int hash = Objects.SpanHashCode<CustomStruct>(values.AsSpan());
            Assert.AreNotEqual(0, hash);
        }

        [Test]
        [TestCase(new byte[] { }, 2166136261u, 2166136261u, TestName = "Stable.Empty.ReturnsSeed")]
        [TestCase(new byte[] { }, 0u, 0u, TestName = "Stable.Empty.ZeroSeed.ReturnsSeed")]
        [TestCase(new byte[] { 0 }, 2166136261u, 84696351u, TestName = "Stable.SingleZeroByte")]
        [TestCase(new byte[] { 97 }, 2166136261u, 3826002220u, TestName = "Stable.SingleByte")]
        [TestCase(new byte[] { 97 }, 0u, 1627429043u, TestName = "Stable.SingleByte.ZeroSeed")]
        [TestCase(new byte[] { 97 }, 12345u, 1481382536u, TestName = "Stable.SingleByte.OtherSeed")]
        [TestCase(
            new byte[] { 102, 111, 111, 98, 97, 114 },
            2166136261u,
            3214735720u,
            TestName = "Stable.SixBytes"
        )]
        [TestCase(
            new byte[] { 102, 111, 111, 98, 97, 114 },
            1u,
            2790836652u,
            TestName = "Stable.SixBytes.OtherSeed"
        )]
        public void StableHash32V1MatchesPinnedVectors(byte[] bytes, uint seed, uint expected)
        {
            // Literal expectations pin hash stability across implementation changes.
            Assert.AreEqual(expected, Objects.StableHash32V1(bytes, seed));
        }

        [Test]
        public void StableHash32V1CoversEveryByteValue()
        {
            byte[] everyByte = new byte[256];
            for (int i = 0; i < everyByte.Length; ++i)
            {
                everyByte[i] = (byte)i;
            }

            Assert.AreEqual(
                2426689733u,
                Objects.StableHash32V1(everyByte, Objects.Fnv32OffsetBasis)
            );
        }

        [Test]
        public void StableHash32V1FoldsChunksThroughTheSeed()
        {
            byte[] whole = { 102, 111, 111, 98, 97, 114 };
            byte[] head = { 102, 111, 111 };
            byte[] tail = { 98, 97, 114 };

            uint folded = Objects.StableHash32V1(
                tail,
                Objects.StableHash32V1(head, Objects.Fnv32OffsetBasis)
            );

            Assert.AreEqual(Objects.StableHash32V1(whole, Objects.Fnv32OffsetBasis), folded);
        }

        [Test]
        public void StableHash32V1IgnoresTheRestOfTheBuffer()
        {
            byte[] buffer = { 102, 111, 111, 98, 97, 114, 255, 255 };

            Assert.AreEqual(
                3214735720u,
                Objects.StableHash32V1(buffer.AsSpan(0, 6), Objects.Fnv32OffsetBasis)
            );
        }

        private readonly struct CustomStruct
        {
            private readonly int _value;

            public CustomStruct(int value)
            {
                _value = value;
            }

            public override int GetHashCode()
            {
                return _value;
            }
        }

        private sealed class CustomClass
        {
            public int Value { get; set; }

            public override int GetHashCode()
            {
                return Value;
            }

            public override bool Equals(object obj)
            {
                return obj is CustomClass other && Value == other.Value;
            }
        }

        private sealed class CustomEnumerable : IEnumerable<int>
        {
            private readonly List<int> _items;

            public CustomEnumerable(params int[] items)
            {
                _items = new List<int>(items);
            }

            public IEnumerator<int> GetEnumerator()
            {
                return _items.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private sealed class DisposableEnumerator : IEnumerator<int>
        {
            private readonly List<int> _items;
            private int _position = -1;

            public DisposableEnumerator(List<int> items)
            {
                _items = items;
            }

            public int Current => _items[_position];

            object IEnumerator.Current => Current;

            public bool WasDisposed { get; private set; }

            public bool MoveNext()
            {
                _position++;
                return _position < _items.Count;
            }

            public void Reset()
            {
                _position = -1;
            }

            public void Dispose()
            {
                WasDisposed = true;
            }
        }

        private sealed class DisposableEnumerable : IEnumerable<int>
        {
            private readonly List<int> _items;

            public DisposableEnumerable(params int[] items)
            {
                _items = new List<int>(items);
            }

            public DisposableEnumerator LastEnumerator { get; private set; }

            public IEnumerator<int> GetEnumerator()
            {
                LastEnumerator = new DisposableEnumerator(_items);
                return LastEnumerator;
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
