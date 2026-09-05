// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CyclicBufferTests
    {
        private const int NumTries = 100;
        private const int CapacityMultiplier = 3;

        [Test]
        public void InvalidCapacity()
        {
            Assert.Throws<ArgumentException>(() => new CyclicBuffer<int>(-1));
            Assert.Throws<ArgumentException>(() => new CyclicBuffer<int>(int.MinValue));
            for (int i = 0; i < NumTries; i++)
            {
                Assert.Throws<ArgumentException>(() =>
                    new CyclicBuffer<int>(PRNG.Instance.Next(int.MinValue, -1))
                );
            }
        }

        [Test]
        public void CapacityInitializedOk()
        {
            for (int i = 0; i < NumTries; i++)
            {
                int capacity = PRNG.Instance.Next(1, int.MaxValue);
                CyclicBuffer<int> buffer = new(capacity);
                Assert.AreEqual(capacity, buffer.Capacity);
            }
        }

        [Test]
        public void CountInitializedOk()
        {
            for (int i = 0; i < NumTries; i++)
            {
                int capacity = PRNG.Instance.Next(1, int.MaxValue);
                CyclicBuffer<int> buffer = new(capacity);
                Assert.AreEqual(0, buffer.Count);
            }
        }

        [Test]
        public void ZeroCapacityOk()
        {
            CyclicBuffer<int> buffer = new(0);
            CollectionAssert.AreEquivalent(Array.Empty<int>(), buffer);
            for (int i = 0; i < NumTries; ++i)
            {
                buffer.Add(PRNG.Instance.Next());
                CollectionAssert.AreEquivalent(Array.Empty<int>(), buffer);
            }
        }

        [Test]
        public void IntMaxCapacityOk()
        {
            CyclicBuffer<int> buffer = new(int.MaxValue);
            CollectionAssert.AreEquivalent(Array.Empty<int>(), buffer);
            const int tries = 50;
            List<int> expected = new(tries);
            for (int i = 0; i < tries; ++i)
            {
                int value = PRNG.Instance.Next();
                buffer.Add(value);
                expected.Add(value);
                if (!expected.SequenceEqual(buffer))
                {
                    Assert.Fail(
                        $"Failure at iteration {i}, capacity={buffer.Capacity}, "
                            + $"capacityMultiplier={CapacityMultiplier}\n"
                            + $"Expected: [{string.Join(",", expected)}], Actual: [{string.Join(",", buffer)}]"
                    );
                }
            }
        }

        [Test]
        public void OneCapacityOk()
        {
            CyclicBuffer<int> buffer = new(1);
            CollectionAssert.AreEquivalent(Array.Empty<int>(), buffer);
            int[] expected = new int[1];
            for (int i = 0; i < NumTries; ++i)
            {
                int value = PRNG.Instance.Next();
                buffer.Add(value);
                expected[0] = value;
                CollectionAssert.AreEquivalent(expected, buffer);
            }
        }

        [Test]
        public void InitialElementsVariableSize()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                int capacity = PRNG.Instance.Next(100, 1_000);
                int[] elements = Enumerable
                    .Range(0, (int)(capacity * PRNG.Instance.NextFloat(0.5f, 1.5f)))
                    .Select(_ => PRNG.Instance.Next())
                    .ToArray();
                CyclicBuffer<int> buffer = new(capacity, elements);
                if (capacity < elements.Length)
                {
                    Assert.IsTrue(elements.Skip(elements.Length - capacity).SequenceEqual(buffer));
                }
                else
                {
                    Assert.IsTrue(elements.SequenceEqual(buffer));
                }
            }
        }

        [Test]
        public void InitialElementsSizeSameAsCapacity()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                int capacity = PRNG.Instance.Next(100, 1_000);
                int[] elements = Enumerable
                    .Range(0, capacity)
                    .Select(_ => PRNG.Instance.Next())
                    .ToArray();
                CyclicBuffer<int> buffer = new(capacity, elements);
                Assert.IsTrue(elements.SequenceEqual(buffer));
            }
        }

        [Test]
        public void InitialElementsEmptyInput()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                int capacity = PRNG.Instance.Next(100, 1_000);
                CyclicBuffer<int> buffer = new(capacity, Array.Empty<int>());
                Assert.IsTrue(Array.Empty<int>().SequenceEqual(buffer));
            }
        }

        [Test]
        public void NormalAndWrappingBehavior()
        {
            LinkedList<int> expected = new();
            for (int i = 0; i < NumTries; ++i)
            {
                int capacity = PRNG.Instance.Next(100, 1_000);
                CyclicBuffer<int> buffer = new(capacity);
                CollectionAssert.AreEquivalent(Array.Empty<int>(), buffer);
                expected.Clear();
                for (int j = 0; j < capacity * CapacityMultiplier; ++j)
                {
                    int newValue = PRNG.Instance.Next();
                    expected.AddLast(newValue);
                    while (capacity < expected.Count)
                    {
                        expected.RemoveFirst();
                    }
                    buffer.Add(newValue);
                    Assert.AreEqual(expected.Count, buffer.Count);
                    if (!expected.SequenceEqual(buffer))
                    {
                        Assert.Fail(
                            $"Failure at iteration {i}, j={j}, capacity={buffer.Capacity}, "
                                + $"capacityMultiplier={CapacityMultiplier}\n"
                                + $"Expected: [{string.Join(",", expected)}], Actual: [{string.Join(",", buffer)}]"
                        );
                    }
                }

                foreach (int item in expected)
                {
                    Assert.IsTrue(buffer.Contains(item));
                }

                for (int j = 0; j < NumTries; ++j)
                {
                    Assert.IsFalse(buffer.Contains(PRNG.Instance.Next(int.MinValue, -1)));
                }
            }
        }

        [Test]
        public void ClearOk()
        {
            HashSet<int> seen = new();
            for (int i = 0; i < NumTries; ++i)
            {
                int capacity = PRNG.Instance.Next(100, 1_000);
                CyclicBuffer<int> buffer = new(capacity);
                float fillPercent = PRNG.Instance.NextFloat(0.5f, 1.5f);
                seen.Clear();
                for (int j = 0; j < capacity * fillPercent; ++j)
                {
                    int value = PRNG.Instance.Next();
                    seen.Add(value);
                    buffer.Add(value);
                }

                Assert.AreNotEqual(0, buffer.Count);
                Assert.IsFalse(Array.Empty<int>().SequenceEqual(buffer));
                buffer.Clear();

                Assert.AreEqual(0, buffer.Count);
                Assert.AreEqual(capacity, buffer.Capacity);
                Assert.IsTrue(Array.Empty<int>().SequenceEqual(buffer));

                foreach (int value in seen)
                {
                    Assert.IsFalse(buffer.Contains(value));
                }
            }
        }

        [Test]
        public void ResizeFullOk()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                int capacity = PRNG.Instance.Next(100, 1_000);
                CyclicBuffer<int> buffer = new(capacity);
                float fillPercent = PRNG.Instance.NextFloat(1f, 2f);
                float capacityPercent = PRNG.Instance.NextFloat(0.3f, 0.9f);
                for (int j = 0; j < capacity * fillPercent; ++j)
                {
                    int value = PRNG.Instance.Next();
                    buffer.Add(value);
                }

                int[] values = buffer.ToArray();

                int newCapacity = Math.Max(0, (int)(capacity * capacityPercent));
                buffer.Resize(newCapacity);
                int[] newValues = buffer.ToArray();
                Assert.AreEqual(newCapacity, buffer.Capacity);
                Assert.AreEqual(newCapacity, newValues.Length);

                Assert.That(values.Skip(values.Length - newCapacity), Is.EqualTo(newValues));

                buffer.Add(1);
                buffer.Add(2);
                int[] afterAddition = buffer.ToArray();
                Assert.That(afterAddition, Is.EqualTo(newValues.Skip(2).Concat(new[] { 1, 2 })));

                newCapacity = 0;
                buffer.Resize(newCapacity);
                newValues = buffer.ToArray();
                Assert.AreEqual(newCapacity, buffer.Capacity);
                Assert.AreEqual(newCapacity, newValues.Length);
            }
        }

        [Test]
        public void ResizePartialOk()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                int capacity = PRNG.Instance.Next(100, 1_000);
                CyclicBuffer<int> buffer = new(capacity);
                float fillPercent = PRNG.Instance.NextFloat(0.3f, 0.9f);
                float capacityPercent = PRNG.Instance.NextFloat(0.3f, 0.9f);
                int filled = (int)(capacity * fillPercent);
                for (int j = 0; j < filled; ++j)
                {
                    int value = PRNG.Instance.Next();
                    buffer.Add(value);
                }

                int[] values = buffer.ToArray();

                int newCapacity = Math.Max(0, (int)(capacity * capacityPercent));
                buffer.Resize(newCapacity);
                int[] newValues = buffer.ToArray();
                Assert.AreEqual(newCapacity, buffer.Capacity);
                Assert.AreEqual(Math.Min(filled, newCapacity), newValues.Length);

                Assert.That(
                    values.Skip(Math.Max(0, values.Length - newCapacity)),
                    Is.EqualTo(newValues)
                );

                buffer.Add(1);
                buffer.Add(2);
                int[] afterAddition = buffer.ToArray();
                if (newCapacity <= filled)
                {
                    Assert.That(
                        afterAddition,
                        Is.EqualTo(newValues.Skip(2).Concat(new[] { 1, 2 })),
                        $"Resize failed for iteration {i}, fillPercent {fillPercent:0.00}, capacityPercent: {capacityPercent:0.00}. "
                            + $"Capacity: {capacity}, newCapacity: {newCapacity}, filled: {filled}."
                    );
                }
                else if (newCapacity == filled + 1)
                {
                    Assert.That(
                        afterAddition,
                        Is.EqualTo(newValues.Skip(1).Concat(new[] { 1, 2 })),
                        $"Resize failed for iteration {i}, fillPercent {fillPercent:0.00}, capacityPercent: {capacityPercent:0.00}. "
                            + $"Capacity: {capacity}, newCapacity: {newCapacity}, filled: {filled}."
                    );
                }
                else
                {
                    Assert.That(
                        afterAddition,
                        Is.EqualTo(newValues.Concat(new[] { 1, 2 })),
                        $"Resize failed for iteration {i}, fillPercent {fillPercent:0.00}, capacityPercent: {capacityPercent:0.00}. "
                            + $"Capacity: {capacity}, newCapacity: {newCapacity}, filled: {filled}."
                    );
                }

                newCapacity = 0;
                buffer.Resize(newCapacity);
                newValues = buffer.ToArray();
                Assert.AreEqual(newCapacity, buffer.Capacity);
                Assert.AreEqual(newCapacity, newValues.Length);
            }
        }

        [Test]
        public void ResizeInvalidCapacity()
        {
            CyclicBuffer<int> buffer = new(10) { 1, 2 };

            Assert.Throws<ArgumentException>(() => buffer.Resize(-1));
            Assert.Throws<ArgumentException>(() => buffer.Resize(int.MinValue));

            Assert.AreEqual(10, buffer.Capacity);
            Assert.AreEqual(2, buffer.Count);
        }

        [Test]
        public void ResizeToSameCapacity()
        {
            CyclicBuffer<int> buffer = new(10) { 1, 2, 3 };

            buffer.Resize(10);

            Assert.AreEqual(10, buffer.Capacity);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void ResizeUpwardsPreservesData()
        {
            CyclicBuffer<int> buffer = new(5);
            for (int i = 1; i <= 10; i++)
            {
                buffer.Add(i);
            }

            int[] beforeResize = buffer.ToArray();
            Assert.That(beforeResize, Is.EqualTo(new[] { 6, 7, 8, 9, 10 }));

            buffer.Resize(10);

            Assert.AreEqual(10, buffer.Capacity);
            Assert.AreEqual(5, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 6, 7, 8, 9, 10 }));

            buffer.Add(11);
            buffer.Add(12);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 6, 7, 8, 9, 10, 11, 12 }));
        }

        [Test]
        public void IndexerGet()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            Assert.AreEqual(1, buffer[0]);
            Assert.AreEqual(2, buffer[1]);
            Assert.AreEqual(3, buffer[2]);

            buffer.Add(4);
            buffer.Add(5);
            buffer.Add(6);

            Assert.AreEqual(2, buffer[0]);
            Assert.AreEqual(3, buffer[1]);
            Assert.AreEqual(4, buffer[2]);
            Assert.AreEqual(5, buffer[3]);
            Assert.AreEqual(6, buffer[4]);
        }

        [Test]
        public void IndexerSet()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            buffer[0] = 10;
            buffer[1] = 20;
            buffer[2] = 30;

            Assert.AreEqual(10, buffer[0]);
            Assert.AreEqual(20, buffer[1]);
            Assert.AreEqual(30, buffer[2]);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 10, 20, 30 }));

            buffer.Add(4);
            buffer.Add(5);
            buffer.Add(6);

            buffer[0] = 100;
            buffer[4] = 200;

            Assert.AreEqual(100, buffer[0]);
            Assert.AreEqual(200, buffer[4]);
        }

        [Test]
        public void IndexerGetOutOfBounds()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2 };

            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                _ = buffer[-1];
            });
            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                _ = buffer[2];
            });
            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                _ = buffer[5];
            });
            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                _ = buffer[int.MaxValue];
            });
            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                _ = buffer[int.MinValue];
            });
        }

        [Test]
        public void IndexerSetOutOfBounds()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2 };

            Assert.Throws<IndexOutOfRangeException>(() => buffer[-1] = 10);
            Assert.Throws<IndexOutOfRangeException>(() => buffer[2] = 10);
            Assert.Throws<IndexOutOfRangeException>(() => buffer[5] = 10);
            Assert.Throws<IndexOutOfRangeException>(() => buffer[int.MaxValue] = 10);
            Assert.Throws<IndexOutOfRangeException>(() => buffer[int.MinValue] = 10);
        }

        [Test]
        public void IndexerOnEmptyBuffer()
        {
            CyclicBuffer<int> buffer = new(5);

            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                _ = buffer[0];
            });
            Assert.Throws<IndexOutOfRangeException>(() => buffer[0] = 10);
        }

        [Test]
        public void RemoveElement()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            bool removed = buffer.Remove(2);

            Assert.IsTrue(removed);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3 }));
            Assert.IsFalse(buffer.Contains(2));
        }

        [Test]
        public void RemoveFirstElement()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            bool removed = buffer.Remove(1);

            Assert.IsTrue(removed);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 2, 3 }));
        }

        [Test]
        public void RemoveLastElement()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            bool removed = buffer.Remove(3);

            Assert.IsTrue(removed);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void RemoveNonExistentElement()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            bool removed = buffer.Remove(10);

            Assert.IsFalse(removed);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void RemoveFromEmptyBuffer()
        {
            CyclicBuffer<int> buffer = new(5);

            bool removed = buffer.Remove(1);

            Assert.IsFalse(removed);
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void RemoveOnlyElement()
        {
            CyclicBuffer<int> buffer = new(5) { 1 };

            bool removed = buffer.Remove(1);

            Assert.IsTrue(removed);
            Assert.AreEqual(0, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(Array.Empty<int>()));
        }

        [Test]
        public void RemoveWithCustomComparer()
        {
            CyclicBuffer<string> buffer = new(5) { "Hello", "World", "HELLO" };

            bool removed = buffer.Remove("hello", StringComparer.OrdinalIgnoreCase);

            Assert.IsTrue(removed);
            Assert.AreEqual(2, buffer.Count);

            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { "World", "HELLO" }));
        }

        [Test]
        public void RemoveWithNullComparer()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            bool removed = buffer.Remove(2, null);

            Assert.IsTrue(removed);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3 }));
        }

        [Test]
        public void RemoveNullValue()
        {
            CyclicBuffer<string> buffer = new(5) { "A", null, "B" };

            bool removed = buffer.Remove(null);

            Assert.IsTrue(removed);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public void RemoveAtEmptyBufferThrows()
        {
            CyclicBuffer<int> buffer = new(5);

            Assert.Throws<IndexOutOfRangeException>(() => buffer.RemoveAt(0));
            Assert.Throws<IndexOutOfRangeException>(() => buffer.RemoveAt(-1));
        }

        [Test]
        public void RemoveAtOutOfRangeIndices()
        {
            CyclicBuffer<int> buffer = new(4) { 1, 2, 3 };

            Assert.Throws<IndexOutOfRangeException>(() => buffer.RemoveAt(-1));
            Assert.Throws<IndexOutOfRangeException>(() => buffer.RemoveAt(3));
            Assert.Throws<IndexOutOfRangeException>(() => buffer.RemoveAt(int.MaxValue));
        }

        [Test]
        public void RemoveAtSingleElementClearsBuffer()
        {
            CyclicBuffer<int> buffer = new(5) { 42 };

            buffer.RemoveAt(0);

            Assert.AreEqual(0, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(Array.Empty<int>()));
            Assert.Throws<IndexOutOfRangeException>(() => buffer.RemoveAt(0));
        }

        [Test]
        public void RemoveAtRemovesFrontElement()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4 };

            buffer.RemoveAt(0);

            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 2, 3, 4 }));
        }

        [Test]
        public void RemoveAtRemovesBackElement()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4 };

            buffer.RemoveAt(buffer.Count - 1);

            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void RemoveAtRemovesMiddleElement()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4, 5 };

            buffer.RemoveAt(2);

            Assert.AreEqual(4, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 2, 4, 5 }));
        }

        [Test]
        public void RemoveAtWrappedBufferLeftShiftPreservesOrder()
        {
            CyclicBuffer<int> buffer = new(5);
            for (int i = 1; i <= 8; ++i)
            {
                buffer.Add(i);
            }

            buffer.RemoveAt(2);

            Assert.AreEqual(4, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 4, 5, 7, 8 }));

            buffer.Add(9);

            Assert.AreEqual(5, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 4, 5, 7, 8, 9 }));
        }

        [Test]
        public void RemoveAtWrappedBufferRightShiftPreservesOrder()
        {
            CyclicBuffer<int> buffer = new(5);
            for (int i = 1; i <= 6; ++i)
            {
                buffer.Add(i);
            }

            buffer.RemoveAt(3);

            Assert.AreEqual(4, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 2, 3, 4, 6 }));

            buffer.Add(7);

            Assert.AreEqual(5, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 2, 3, 4, 6, 7 }));
        }

        [Test]
        public void RemoveAtRandomizedOperationsMatchList()
        {
            const int capacity = 8;
            CyclicBuffer<int> buffer = new(capacity);
            List<int> expected = new(capacity);

            for (int i = 0; i < 256; ++i)
            {
                bool shouldRemove =
                    0 < expected.Count && (expected.Count == capacity || PRNG.Instance.NextBool());

                if (shouldRemove)
                {
                    int index = PRNG.Instance.Next(0, expected.Count);
                    buffer.RemoveAt(index);
                    expected.RemoveAt(index);
                }
                else
                {
                    int value = PRNG.Instance.Next();
                    buffer.Add(value);
                    if (expected.Count == capacity)
                    {
                        expected.RemoveAt(0);
                    }
                    expected.Add(value);
                }

                Assert.AreEqual(expected.Count, buffer.Count);
                Assert.That(buffer.ToArray(), Is.EqualTo(expected));
            }
        }

        [Test]
        public void RemoveAllMatchingElements()
        {
            CyclicBuffer<int> buffer = new(10) { 1, 2, 3, 2, 4, 2 };

            int removed = buffer.RemoveAll(x => x == 2);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3, 4 }));
        }

        [Test]
        public void RemoveAllNoMatches()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            int removed = buffer.RemoveAll(x => x == 10);

            Assert.AreEqual(0, removed);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void RemoveAllFromEmptyBuffer()
        {
            CyclicBuffer<int> buffer = new(5);

            int removed = buffer.RemoveAll(x => 0 < x);

            Assert.AreEqual(0, removed);
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void RemoveAllElements()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            int removed = buffer.RemoveAll(x => true);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(0, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(Array.Empty<int>()));
        }

        [Test]
        public void RemoveAllWithComplexPredicate()
        {
            CyclicBuffer<int> buffer = new(10);
            for (int i = 1; i <= 10; i++)
            {
                buffer.Add(i);
            }

            int removed = buffer.RemoveAll(x => x % 2 == 0 || 8 < x);

            Assert.AreEqual(6, removed);
            Assert.AreEqual(4, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3, 5, 7 }));
        }

        [Test]
        public void RemoveAllFirstElement()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4 };

            int removed = buffer.RemoveAll(x => x == 1);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 2, 3, 4 }));
        }

        [Test]
        public void RemoveAllLastElement()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4 };

            int removed = buffer.RemoveAll(x => x == 4);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void RemoveAllMiddleElements()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4, 5 };

            int removed = buffer.RemoveAll(x => 2 <= x && x <= 4);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 5 }));
        }

        [Test]
        public void RemoveAllOnlyElement()
        {
            CyclicBuffer<int> buffer = new(5) { 42 };

            int removed = buffer.RemoveAll(x => x == 42);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(0, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(Array.Empty<int>()));
        }

        [Test]
        public void RemoveAllNullPredicate()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2 };

            Assert.Throws<ArgumentNullException>(() => buffer.RemoveAll(null));
        }

        [Test]
        public void RemoveAllWithNullValues()
        {
            CyclicBuffer<string> buffer = new(5) { "A", null, "B", null, "C" };

            int removed = buffer.RemoveAll(x => x == null);

            Assert.AreEqual(2, removed);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { "A", "B", "C" }));
        }

        [Test]
        public void RemoveAllKeepingNullValues()
        {
            CyclicBuffer<string> buffer = new(5) { "A", null, "B", null, "C" };

            int removed = buffer.RemoveAll(x => x != null);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new string[] { null, null }));
        }

        [Test]
        public void RemoveAllAlternatingPattern()
        {
            CyclicBuffer<int> buffer = new(10) { 1, 2, 3, 4, 5, 6 };

            int removed = buffer.RemoveAll(x => x % 2 == 0);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3, 5 }));
        }

        [Test]
        public void RemoveAllPredicateNeverTrue()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            int removed = buffer.RemoveAll(x => 100 < x);

            Assert.AreEqual(0, removed);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void RemoveAllPredicateAlwaysTrue()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4 };

            int removed = buffer.RemoveAll(_ => true);

            Assert.AreEqual(4, removed);
            Assert.AreEqual(0, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(Array.Empty<int>()));
        }

        [Test]
        public void RemoveAllPredicateAlwaysFalse()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            int removed = buffer.RemoveAll(x => false);

            Assert.AreEqual(0, removed);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void RemoveAllFromSingleElementBuffer()
        {
            CyclicBuffer<int> buffer = new(1) { 5 };

            int removed = buffer.RemoveAll(x => x == 5);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(0, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(Array.Empty<int>()));
        }

        [Test]
        public void RemoveAllFromSingleElementBufferNoMatch()
        {
            CyclicBuffer<int> buffer = new(1) { 5 };

            int removed = buffer.RemoveAll(x => x == 10);

            Assert.AreEqual(0, removed);
            Assert.AreEqual(1, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 5 }));
        }

        [Test]
        public void RemoveAllFromFullBuffer()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4, 5 };

            int removed = buffer.RemoveAll(x => 2 < x);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void RemoveAllEntireFullBuffer()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4, 5 };

            int removed = buffer.RemoveAll(x => 0 < x);

            Assert.AreEqual(5, removed);
            Assert.AreEqual(0, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(Array.Empty<int>()));
        }

        [Test]
        public void RemoveAllWithWrappedBufferRemoveFirst()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4, 5, 6 };

            int removed = buffer.RemoveAll(x => x == 2);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(4, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 3, 4, 5, 6 }));
        }

        [Test]
        public void RemoveAllWithWrappedBufferRemoveLast()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4, 5, 6 };

            int removed = buffer.RemoveAll(x => x == 6);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(4, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 2, 3, 4, 5 }));
        }

        [Test]
        public void RemoveAllWithWrappedBufferRemoveMiddle()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4, 5, 6, 7 };

            int removed = buffer.RemoveAll(x => x == 5);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(4, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 3, 4, 6, 7 }));
        }

        [Test]
        public void RemoveAllWithWrappedBufferMultipleElements()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4, 5, 6, 7, 8 };

            int removed = buffer.RemoveAll(x => x == 4 || x == 6 || x == 8);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 5, 7 }));
        }

        [Test]
        public void RemoveAllWithDuplicates()
        {
            CyclicBuffer<int> buffer = new(10) { 1, 2, 2, 3, 2, 4 };

            int removed = buffer.RemoveAll(x => x == 2);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3, 4 }));
        }

        [Test]
        public void RemoveAllWithDuplicatesWrapped()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 2, 3, 2, 4, 2 };

            int removed = buffer.RemoveAll(x => x == 2);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 3, 4 }));
        }

        [Test]
        public void RemoveAllThenAdd()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4 };

            int removed = buffer.RemoveAll(x => x % 2 == 0);
            Assert.AreEqual(2, removed);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3 }));

            buffer.Add(5);
            buffer.Add(6);
            buffer.Add(7);

            Assert.AreEqual(5, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3, 5, 6, 7 }));
        }

        [Test]
        public void RemoveAllMultipleTimes()
        {
            CyclicBuffer<int> buffer = new(10) { 1, 2, 3, 4, 5, 6 };

            int removed1 = buffer.RemoveAll(x => x % 2 == 0);
            Assert.AreEqual(3, removed1);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3, 5 }));

            int removed2 = buffer.RemoveAll(x => 3 < x);
            Assert.AreEqual(1, removed2);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3 }));

            int removed3 = buffer.RemoveAll(x => x == 1);
            Assert.AreEqual(1, removed3);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void RemoveAllWithZeroCapacityBuffer()
        {
            CyclicBuffer<int> buffer = new(0);

            int removed = buffer.RemoveAll(x => true);

            Assert.AreEqual(0, removed);
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void RemoveAllPreservesOrderOfRemainingElements()
        {
            CyclicBuffer<int> buffer = new(10) { 10, 20, 30, 40, 50, 60, 70 };

            int removed = buffer.RemoveAll(x => x == 20 || x == 40 || x == 60);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(4, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 10, 30, 50, 70 }));
        }

        [Test]
        public void RemoveAllWithComplexObjectsPredicate()
        {
            CyclicBuffer<string> buffer = new(10)
            {
                "apple",
                "banana",
                "apricot",
                "cherry",
                "avocado",
            };

            int removed = buffer.RemoveAll(x => x.StartsWith("a"));

            Assert.AreEqual(3, removed);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { "banana", "cherry" }));
        }

        [Test]
        public void RemoveAllAfterMultipleWraparounds()
        {
            CyclicBuffer<int> buffer = new(3) { 1, 2, 3, 4, 5, 6, 7, 8 };

            int removed = buffer.RemoveAll(x => x == 7);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 6, 8 }));
        }

        [Test]
        public void RemoveAllLeavingSingleElement()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4, 5 };

            int removed = buffer.RemoveAll(x => x != 3);

            Assert.AreEqual(4, removed);
            Assert.AreEqual(1, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void RemoveAllWithCapacityOne()
        {
            CyclicBuffer<int> buffer = new(1) { 42 };

            int removed = buffer.RemoveAll(x => x == 42);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(0, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(Array.Empty<int>()));
        }

        [Test]
        public void RemoveAllWithCapacityOneNoMatch()
        {
            CyclicBuffer<int> buffer = new(1) { 42 };

            int removed = buffer.RemoveAll(x => x != 42);

            Assert.AreEqual(0, removed);
            Assert.AreEqual(1, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 42 }));
        }

        [Test]
        public void RemoveAllPartialBufferBeginning()
        {
            CyclicBuffer<int> buffer = new(10) { 1, 2, 3 };

            int removed = buffer.RemoveAll(x => x <= 2);

            Assert.AreEqual(2, removed);
            Assert.AreEqual(1, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void RemoveAllPartialBufferEnd()
        {
            CyclicBuffer<int> buffer = new(10) { 1, 2, 3 };

            int removed = buffer.RemoveAll(x => 2 <= x);

            Assert.AreEqual(2, removed);
            Assert.AreEqual(1, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void RemoveAllConsecutiveElements()
        {
            CyclicBuffer<int> buffer = new(10) { 1, 2, 3, 4, 5, 6, 7 };

            int removed = buffer.RemoveAll(x => 3 <= x && x <= 5);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(4, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 2, 6, 7 }));
        }

        [Test]
        public void RemoveAllNonConsecutiveElements()
        {
            CyclicBuffer<int> buffer = new(10) { 1, 2, 3, 4, 5, 6, 7 };

            int removed = buffer.RemoveAll(x => x == 1 || x == 4 || x == 7);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(4, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 2, 3, 5, 6 }));
        }

        [Test]
        public void EnumerationEmpty()
        {
            CyclicBuffer<int> buffer = new(5);

            int count = 0;
            foreach (int unused in buffer)
            {
                count++;
            }

            Assert.AreEqual(0, count);
        }

        [Test]
        public void EnumerationNormal()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            List<int> items = new();
            foreach (int item in buffer)
            {
                items.Add(item);
            }

            Assert.That(items, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void EnumerationAfterWraparound()
        {
            CyclicBuffer<int> buffer = new(3) { 1, 2, 3, 4, 5 };

            List<int> items = new();
            foreach (int item in buffer)
            {
                items.Add(item);
            }

            Assert.That(items, Is.EqualTo(new[] { 3, 4, 5 }));
        }

        [Test]
        public void EnumerationMultipleTimes()
        {
            CyclicBuffer<int> buffer = new(3) { 1, 2 };

            for (int i = 0; i < 3; i++)
            {
                List<int> items = new();
                foreach (int item in buffer)
                {
                    items.Add(item);
                }
                Assert.That(items, Is.EqualTo(new[] { 1, 2 }));
            }
        }

        [Test]
        public void EnumeratorReset()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            using CyclicBuffer<int>.CyclicBufferEnumerator enumerator = buffer.GetEnumerator();

            List<int> firstPass = new();
            while (enumerator.MoveNext())
            {
                firstPass.Add(enumerator.Current);
            }

            enumerator.Reset();
            List<int> secondPass = new();
            while (enumerator.MoveNext())
            {
                secondPass.Add(enumerator.Current);
            }

            Assert.That(firstPass, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(secondPass, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void ContainsNullValue()
        {
            CyclicBuffer<string> buffer = new(5) { "A", null, "B" };

            Assert.IsTrue(buffer.Contains(null));
            Assert.IsTrue(buffer.Contains("A"));
            Assert.IsTrue(buffer.Contains("B"));
            Assert.IsFalse(buffer.Contains("C"));
        }

        [Test]
        public void ContainsEmptyBuffer()
        {
            CyclicBuffer<int> buffer = new(5);

            Assert.IsFalse(buffer.Contains(1));
            Assert.IsFalse(buffer.Contains(0));
        }

        [Test]
        public void ContainsAfterClear()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            Assert.IsTrue(buffer.Contains(2));

            buffer.Clear();

            Assert.IsFalse(buffer.Contains(1));
            Assert.IsFalse(buffer.Contains(2));
            Assert.IsFalse(buffer.Contains(3));
        }

        [Test]
        public void AddAfterRemove()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            buffer.Remove(2);
            Assert.AreEqual(2, buffer.Count);

            buffer.Add(4);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3, 4 }));

            buffer.Add(5);
            buffer.Add(6);
            Assert.AreEqual(5, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3, 4, 5, 6 }));
        }

        [Test]
        public void AddAfterRemoveAll()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4 };

            buffer.RemoveAll(x => x % 2 == 0);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3 }));

            buffer.Add(5);
            buffer.Add(6);
            buffer.Add(7);
            Assert.AreEqual(5, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 3, 5, 6, 7 }));
        }

        [Test]
        public void InitialContentsNull()
        {
            CyclicBuffer<int> buffer = new(5, null);

            Assert.AreEqual(5, buffer.Capacity);
            Assert.AreEqual(0, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(Array.Empty<int>()));
        }

        [Test]
        public void InitialContentsWithNullElements()
        {
            List<string> initial = new() { "A", null, "B", null, "C" };
            CyclicBuffer<string> buffer = new(10, initial);

            Assert.AreEqual(5, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { "A", null, "B", null, "C" }));
            Assert.IsTrue(buffer.Contains(null));
        }

        [Test]
        public void ResizePreservesOrder()
        {
            CyclicBuffer<int> buffer = new(5);
            for (int i = 1; i <= 8; i++)
            {
                buffer.Add(i);
            }

            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 4, 5, 6, 7, 8 }));

            buffer.Resize(3);

            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 6, 7, 8 }));

            buffer.Resize(7);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 6, 7, 8 }));

            buffer.Add(9);
            buffer.Add(10);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 6, 7, 8, 9, 10 }));
        }

        [Test]
        public void StressTestMixedOperations()
        {
            CyclicBuffer<int> buffer = new(20);
            List<int> expected = new();

            for (int i = 0; i < 100; i++)
            {
                int operation = PRNG.Instance.Next(0, 5);

                switch (operation)
                {
                    case 0:
                        int value = PRNG.Instance.Next(0, 100);
                        buffer.Add(value);
                        expected.Add(value);
                        while (buffer.Capacity < expected.Count)
                        {
                            expected.RemoveAt(0);
                        }
                        break;

                    case 1:
                        if (0 < expected.Count)
                        {
                            int toRemove = expected[PRNG.Instance.Next(0, expected.Count)];
                            bool removedExpected = expected.Remove(toRemove);
                            bool removedActual = buffer.Remove(toRemove);
                            Assert.AreEqual(removedExpected, removedActual);
                        }
                        break;

                    case 2:
                        buffer.Clear();
                        expected.Clear();
                        break;

                    case 3:
                        int expectedRemoved = expected.RemoveAll(x => x % 2 == 0);
                        int actualRemoved = buffer.RemoveAll(x => x % 2 == 0);
                        Assert.AreEqual(expectedRemoved, actualRemoved);
                        break;

                    case 4:
                        if (0 < expected.Count)
                        {
                            int idx = PRNG.Instance.Next(0, expected.Count);
                            int newValue = PRNG.Instance.Next(0, 100);
                            expected[idx] = newValue;
                            buffer[idx] = newValue;
                        }
                        break;
                }

                Assert.AreEqual(expected.Count, buffer.Count);
                Assert.That(
                    buffer.ToArray(),
                    Is.EqualTo(expected.ToArray()),
                    $"Failed at iteration {i}, operation {operation}"
                );
            }
        }

        [Test]
        public void ZeroCapacityOperations()
        {
            CyclicBuffer<int> buffer = new(0);

            Assert.AreEqual(0, buffer.Count);
            Assert.AreEqual(0, buffer.Capacity);

            buffer.Add(1);
            Assert.AreEqual(0, buffer.Count);

            buffer.Clear();
            Assert.AreEqual(0, buffer.Count);

            Assert.IsFalse(buffer.Remove(1));
            Assert.AreEqual(0, buffer.RemoveAll(x => true));
            Assert.IsFalse(buffer.Contains(1));

            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                int x = buffer[0];
            });
            Assert.Throws<IndexOutOfRangeException>(() => buffer[0] = 1);

            int count = 0;
            foreach (int unused in buffer)
            {
                count++;
            }
            Assert.AreEqual(0, count);
        }

        [Test]
        public void FullBufferOperations()
        {
            CyclicBuffer<int> buffer = new(3) { 1, 2, 3 };

            Assert.AreEqual(3, buffer.Count);
            Assert.AreEqual(3, buffer.Capacity);

            buffer.Add(4);
            Assert.AreEqual(3, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 2, 3, 4 }));

            for (int i = 0; i < buffer.Count; i++)
            {
                Assert.DoesNotThrow(() =>
                {
                    int x = buffer[i];
                });
                Assert.DoesNotThrow(() => buffer[i] += 10);
            }

            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 12, 13, 14 }));
        }

        [Test]
        public void RemoveAfterWraparound()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4, 5, 6, 7 };

            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 3, 4, 5, 6, 7 }));
            bool removed = buffer.Remove(5);

            Assert.IsTrue(removed);
            Assert.AreEqual(4, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 3, 4, 6, 7 }));

            buffer.Add(8);
            Assert.AreEqual(5, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 3, 4, 6, 7, 8 }));
        }

        [Test]
        public void RemoveAllAfterWraparound()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3, 4, 5, 6, 7, 8 };

            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 4, 5, 6, 7, 8 }));

            int removed = buffer.RemoveAll(x => x % 2 == 0);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(2, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 5, 7 }));

            buffer.Add(9);
            buffer.Add(10);
            Assert.AreEqual(4, buffer.Count);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 5, 7, 9, 10 }));
        }

        [Test]
        public void MultipleOperationsSequence()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));

            buffer.Add(4);
            buffer.Add(5);
            buffer.Add(6);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 2, 3, 4, 5, 6 }));

            buffer.Remove(4);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 2, 3, 5, 6 }));

            buffer.Add(7);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 2, 3, 5, 6, 7 }));

            buffer.Add(8);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 3, 5, 6, 7, 8 }));

            int removed = buffer.RemoveAll(x => 6 < x);
            Assert.AreEqual(2, removed);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 3, 5, 6 }));

            buffer.Add(9);
            buffer.Add(10);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 3, 5, 6, 9, 10 }));
        }

        [Test]
        public void TryPopFrontExhaustionResetsState()
        {
            CyclicBuffer<int> buffer = new(4) { 1, 2, 3, 4 };

            Assert.That(buffer.Count, Is.EqualTo(4));

            Assert.IsTrue(buffer.TryPopFront(out int first));
            Assert.AreEqual(1, first);

            Assert.IsTrue(buffer.TryPopFront(out int second));
            Assert.AreEqual(2, second);

            Assert.IsTrue(buffer.TryPopFront(out int third));
            Assert.AreEqual(3, third);

            Assert.IsTrue(buffer.TryPopFront(out int fourth));
            Assert.AreEqual(4, fourth);

            Assert.AreEqual(0, buffer.Count);
            Assert.IsFalse(buffer.TryPopFront(out int emptyFront));
            Assert.AreEqual(default(int), emptyFront);

            buffer.Add(99);
            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 99 }));
            Assert.AreEqual(99, buffer[0]);
        }

        [Test]
        public void TryPopBackExhaustionResetsState()
        {
            CyclicBuffer<int> buffer = new(4) { 1, 2, 3, 4 };

            Assert.That(buffer.Count, Is.EqualTo(4));

            Assert.IsTrue(buffer.TryPopBack(out int first));
            Assert.AreEqual(4, first);

            Assert.IsTrue(buffer.TryPopBack(out int second));
            Assert.AreEqual(3, second);

            Assert.IsTrue(buffer.TryPopBack(out int third));
            Assert.AreEqual(2, third);

            Assert.IsTrue(buffer.TryPopBack(out int fourth));
            Assert.AreEqual(1, fourth);

            Assert.AreEqual(0, buffer.Count);
            Assert.IsFalse(buffer.TryPopBack(out int emptyBack));
            Assert.AreEqual(default(int), emptyBack);

            buffer.Add(42);
            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 42 }));
            Assert.AreEqual(42, buffer[0]);
        }

        [Test]
        public void TryPopFrontMaintainsOrderAcrossWraps()
        {
            CyclicBuffer<int> buffer = new(3) { 0, 1, 2, 3, 4 };

            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 2, 3, 4 }));

            Assert.IsTrue(buffer.TryPopFront(out int firstFront));
            Assert.AreEqual(2, firstFront);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 3, 4 }));

            buffer.Add(5);
            buffer.Add(6);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 4, 5, 6 }));

            Assert.IsTrue(buffer.TryPopFront(out int secondFront));
            Assert.AreEqual(4, secondFront);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 5, 6 }));

            buffer.Add(7);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 5, 6, 7 }));

            Assert.IsTrue(buffer.TryPopFront(out int thirdFront));
            Assert.AreEqual(5, thirdFront);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 6, 7 }));
        }

        [Test]
        public void TryPopBackMaintainsTailAcrossWraps()
        {
            CyclicBuffer<int> buffer = new(4) { 10, 11, 12, 13, 14, 15 };

            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 12, 13, 14, 15 }));

            Assert.IsTrue(buffer.TryPopBack(out int firstBack));
            Assert.AreEqual(15, firstBack);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 12, 13, 14 }));

            buffer.Add(16);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 12, 13, 14, 16 }));

            Assert.IsTrue(buffer.TryPopBack(out int secondBack));
            Assert.AreEqual(16, secondBack);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 12, 13, 14 }));

            buffer.Add(17);
            buffer.Add(18);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 13, 14, 17, 18 }));

            Assert.IsTrue(buffer.TryPopBack(out int thirdBack));
            Assert.AreEqual(18, thirdBack);
            Assert.That(buffer.ToArray(), Is.EqualTo(new[] { 13, 14, 17 }));
        }

        [Test]
        public void TryPopOperationsStayInSyncWithReferenceList()
        {
            for (int trial = 0; trial < NumTries; trial++)
            {
                int capacity = PRNG.Instance.Next(0, 10);
                CyclicBuffer<int> buffer = new(capacity);
                List<int> expected = new(0 < capacity ? capacity : 1);

                int operations = PRNG.Instance.Next(30, 90);
                for (int step = 0; step < operations; step++)
                {
                    int action = PRNG.Instance.Next(0, 3);
                    switch (action)
                    {
                        case 0:
                        {
                            int value = PRNG.Instance.Next();
                            buffer.Add(value);
                            if (0 < capacity)
                            {
                                if (expected.Count == capacity)
                                {
                                    expected.RemoveAt(0);
                                }

                                expected.Add(value);
                            }

                            break;
                        }
                        case 1:
                        {
                            bool bufferPopped = buffer.TryPopFront(out int poppedFront);
                            bool expectedPopped = 0 < expected.Count;

                            Assert.AreEqual(expectedPopped, bufferPopped);
                            if (expectedPopped)
                            {
                                Assert.AreEqual(expected[0], poppedFront);
                                expected.RemoveAt(0);
                            }
                            else
                            {
                                Assert.AreEqual(default(int), poppedFront);
                            }

                            break;
                        }
                        default:
                        {
                            bool bufferPopped = buffer.TryPopBack(out int poppedBack);
                            bool expectedPopped = 0 < expected.Count;

                            Assert.AreEqual(expectedPopped, bufferPopped);
                            if (expectedPopped)
                            {
                                int lastIndex = expected.Count - 1;
                                Assert.AreEqual(expected[lastIndex], poppedBack);
                                expected.RemoveAt(lastIndex);
                            }
                            else
                            {
                                Assert.AreEqual(default(int), poppedBack);
                            }

                            break;
                        }
                    }

                    Assert.That(buffer.Count, Is.EqualTo(expected.Count));
                    Assert.That(buffer.ToArray(), Is.EqualTo(expected.ToArray()));
                }
            }
        }

        [Test]
        public void TryPopFrontRemovesFromFront()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            bool success = buffer.TryPopFront(out int result);

            Assert.IsTrue(success);
            Assert.AreEqual(1, result);
            Assert.AreEqual(2, buffer.Count);
            Assert.AreEqual(2, buffer[0]);
        }

        [Test]
        public void TryPopFrontReturnsFalseWhenEmpty()
        {
            CyclicBuffer<int> buffer = new(5);

            bool success = buffer.TryPopFront(out int result);

            Assert.IsFalse(success);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void TryPopBackRemovesFromBack()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2, 3 };

            bool success = buffer.TryPopBack(out int result);

            Assert.IsTrue(success);
            Assert.AreEqual(3, result);
            Assert.AreEqual(2, buffer.Count);
            Assert.AreEqual(2, buffer[1]);
        }

        [Test]
        public void TryPopBackReturnsFalseWhenEmpty()
        {
            CyclicBuffer<int> buffer = new(5);

            bool success = buffer.TryPopBack(out int result);

            Assert.IsFalse(success);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void TryPopFrontMultipleTimes()
        {
            CyclicBuffer<int> buffer = new(10);
            for (int i = 0; i < 5; i++)
            {
                buffer.Add(i);
            }

            Assert.IsTrue(buffer.TryPopFront(out int first));
            Assert.AreEqual(0, first);

            Assert.IsTrue(buffer.TryPopFront(out int second));
            Assert.AreEqual(1, second);

            Assert.AreEqual(3, buffer.Count);
        }

        [Test]
        public void TryPopBackMultipleTimes()
        {
            CyclicBuffer<int> buffer = new(10);
            for (int i = 0; i < 5; i++)
            {
                buffer.Add(i);
            }

            Assert.IsTrue(buffer.TryPopBack(out int first));
            Assert.AreEqual(4, first);

            Assert.IsTrue(buffer.TryPopBack(out int second));
            Assert.AreEqual(3, second);

            Assert.AreEqual(3, buffer.Count);
        }

        [Test]
        public void TryPopFrontAndBackMixed()
        {
            CyclicBuffer<int> buffer = new(10) { 1, 2, 3, 4 };

            Assert.IsTrue(buffer.TryPopFront(out int front1));
            Assert.AreEqual(1, front1);

            Assert.IsTrue(buffer.TryPopBack(out int back1));
            Assert.AreEqual(4, back1);

            Assert.AreEqual(2, buffer.Count);
            Assert.AreEqual(2, buffer[0]);
            Assert.AreEqual(3, buffer[1]);
        }

        [Test]
        public void TryPopUntilEmpty()
        {
            CyclicBuffer<int> buffer = new(5) { 1, 2 };

            Assert.IsTrue(buffer.TryPopFront(out _));
            Assert.IsTrue(buffer.TryPopFront(out _));
            Assert.IsFalse(buffer.TryPopFront(out _));
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void TryPopWithWrappedBuffer()
        {
            CyclicBuffer<int> buffer = new(3) { 1, 2, 3, 4, 5 };

            Assert.IsTrue(buffer.TryPopFront(out int front));
            Assert.AreEqual(3, front);

            Assert.IsTrue(buffer.TryPopBack(out int back));
            Assert.AreEqual(5, back);

            Assert.AreEqual(1, buffer.Count);
        }

        [Test]
        public void AddAndOverwritePreservesChronology()
        {
            CyclicBuffer<int> buf = new(3) { 0, 1, 2 };

            Assert.AreEqual(
                3,
                buf.Count,
                "Count should reflect number of elements added up to capacity."
            );
            Assert.AreEqual(0, buf[0], "Oldest element should be at index 0 before wrap.");
            Assert.AreEqual(1, buf[1], "Next element should be index 1.");
            Assert.AreEqual(2, buf[2], "Newest element should be index 2 before wrap.");

            buf.Add(3);
            Assert.AreEqual(3, buf.Count, "Count should not grow beyond capacity.");
            Assert.AreEqual(1, buf[0], "After overwrite, oldest is dropped.");
            Assert.AreEqual(2, buf[1], "Element order should advance by one.");
            Assert.AreEqual(3, buf[2], "Newest written value should be last.");

            bool removed = buf.Remove(2);
            Assert.IsTrue(removed, "Remove should return true when element existed.");
            Assert.AreEqual(2, buf.Count, "Count should decrease after remove.");
            Assert.AreEqual(1, buf[0], "Remaining first element should be unchanged.");
            Assert.AreEqual(3, buf[1], "Remaining second element should be next in order.");
        }

        [Test]
        public void ResizeTruncatesOrExtends()
        {
            CyclicBuffer<int> buf = new(5);
            for (int i = 0; i < 5; ++i)
            {
                buf.Add(i);
            }
            Assert.AreEqual(5, buf.Count, "Filled buffer should have full count.");

            buf.Resize(3);
            Assert.AreEqual(3, buf.Count, "Count should reflect new capacity after shrink.");
            Assert.AreEqual(2, buf[0], "Shrink should retain most recent entries and drop oldest.");
            Assert.AreEqual(3, buf[1], "Remaining order should be preserved (middle).");
            Assert.AreEqual(4, buf[2], "Remaining order should be preserved (newest).");

            buf.Resize(6);
            Assert.AreEqual(3, buf.Count, "Growing capacity should not change current count.");
            Assert.AreEqual(2, buf[0], "Growing capacity should not alter order (first).");
            Assert.AreEqual(3, buf[1], "Growing capacity should not alter order (second).");
            Assert.AreEqual(4, buf[2], "Growing capacity should not alter order (third).");
        }

        [Test]
        public void ResizeShrinkKeepsMostRecentFromPartialFill()
        {
            CyclicBuffer<int> buf = new(10);
            for (int i = 0; i < 6; ++i)
            {
                buf.Add(i);
            }

            buf.Resize(4);
            Assert.AreEqual(4, buf.Count);
            Assert.That(buf.ToArray(), Is.EqualTo(new[] { 2, 3, 4, 5 }));
        }

        [Test]
        public void ResizeShrinkAfterWrapKeepsMostRecent()
        {
            CyclicBuffer<int> buf = new(5);
            for (int i = 1; i <= 7; ++i)
            {
                buf.Add(i);
            }

            buf.Resize(2);
            Assert.That(buf.ToArray(), Is.EqualTo(new[] { 6, 7 }));
        }

        [Test]
        public void ResizeToZeroClearsAndSubsequentAddsWork()
        {
            CyclicBuffer<int> buf = new(4) { 1, 2, 3 };
            buf.Resize(0);
            Assert.AreEqual(0, buf.Capacity);
            Assert.AreEqual(0, buf.Count);
            Assert.That(buf.ToArray(), Is.EqualTo(Array.Empty<int>()));

            buf.Add(42);
            Assert.AreEqual(0, buf.Count);
        }

        [Test]
        public void ResizeAfterRemoveAllMaintainsMostRecent()
        {
            CyclicBuffer<int> buf = new(6) { 0, 1, 2, 3, 4, 5 };
            int removed = buf.RemoveAll(x => x % 2 == 0);
            Assert.AreEqual(3, removed);
            Assert.That(buf.ToArray(), Is.EqualTo(new[] { 1, 3, 5 }));

            buf.Resize(2);
            Assert.That(buf.ToArray(), Is.EqualTo(new[] { 3, 5 }));
        }

        [Test]
        public void SequentialResizeShrinkAndGrowPreservesOrder()
        {
            CyclicBuffer<int> buf = new(5) { 10, 11, 12, 13, 14 };
            buf.Resize(4);
            Assert.That(buf.ToArray(), Is.EqualTo(new[] { 11, 12, 13, 14 }));

            buf.Resize(2);
            Assert.That(buf.ToArray(), Is.EqualTo(new[] { 13, 14 }));

            buf.Resize(6);
            Assert.That(buf.ToArray(), Is.EqualTo(new[] { 13, 14 }));

            buf.Add(15);
            buf.Add(16);
            Assert.That(buf.ToArray(), Is.EqualTo(new[] { 13, 14, 15, 16 }));
        }
    }
}
