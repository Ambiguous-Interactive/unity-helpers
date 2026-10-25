// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class TrieEnumerationTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(32)]
        [TestCase(256)]
        public void EnumerationHoldsOneBuilderRegardlessOfDepth(int length)
        {
            string word = new('x', length);
            Trie trie = new(new[] { word });
            PoolStatistics before = Buffers.StringBuilder.GetStatistics();
            using (Trie.Enumerator enumerator = trie.GetEnumerator())
            {
                Assert.IsTrue(enumerator.MoveNext());
                Assert.AreEqual(word, enumerator.Current);
                Assert.IsFalse(enumerator.MoveNext());
                Assert.AreEqual(
                    1,
                    Buffers.StringBuilder.GetStatistics().RentCount - before.RentCount
                );
            }
            Assert.AreEqual(
                1,
                Buffers.StringBuilder.GetStatistics().ReturnCount - before.ReturnCount
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(32)]
        [TestCase(256)]
        public void EnumerationHoldsOneBuilderRegardlessOfWidth(int width)
        {
            string[] words = new string[width];
            for (int index = 0; index < width; ++index)
            {
                words[index] = "stem" + (char)index;
            }
            Trie trie = new(words);
            PoolStatistics before = Buffers.StringBuilder.GetStatistics();
            using (Trie.Enumerator enumerator = trie.GetEnumerator())
            {
                for (int index = width - 1; 0 <= index; --index)
                {
                    Assert.IsTrue(enumerator.MoveNext());
                    Assert.AreEqual(words[index], enumerator.Current);
                }
                Assert.IsFalse(enumerator.MoveNext());
                Assert.AreEqual(
                    1,
                    Buffers.StringBuilder.GetStatistics().RentCount - before.RentCount
                );
            }
            Assert.AreEqual(
                1,
                Buffers.StringBuilder.GetStatistics().ReturnCount - before.ReturnCount
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EnumerationPreservesReverseSiblingOrderAndExactCharacters(int shape)
        {
            string[] words =
            {
                "cat",
                "dog",
                "car",
                "carbon",
                "do",
                "cape",
                "z",
                "",
                "c\0",
                "c\ud83d\ude00",
                "c\ud800",
                "c\udc00",
            };
            string[] expected =
            {
                "",
                "z",
                "do",
                "dog",
                "c\udc00",
                "c\ud800",
                "c\ud83d\ude00",
                "c\0",
                "cape",
                "car",
                "carbon",
                "cat",
            };
            Trie trie = new(words);
            List<string> actual = new();
            if (shape == 0)
            {
                foreach (string word in trie)
                {
                    actual.Add(word);
                }
            }
            else if (shape == 1)
            {
                foreach (string word in (IEnumerable<string>)trie)
                {
                    actual.Add(word);
                }
            }
            else
            {
                foreach (string word in (IEnumerable)trie)
                {
                    actual.Add(word);
                }
            }
            CollectionAssert.AreEqual(expected, actual);
        }

        [TestCase(4096)]
        [TestCase(100000)]
        public void DeepEnumerationRestoresPrefixesAcrossBranches(int length)
        {
            string stem = new('x', length);
            string[] words = { stem, stem + "a", stem + "ab", stem + "b", "z", "" };
            string[] expected = { "", "z", stem, stem + "b", stem + "a", stem + "ab" };
            Trie trie = new(words);
            List<string> actual = new();
            foreach (string word in trie)
            {
                actual.Add(word);
            }
            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void NestedEnumerationKeepsIndependentPrefixes()
        {
            Trie trie = new(new[] { "a", "ab", "ac", "b", "bc", "" });
            string[] expected = { "", "b", "bc", "a", "ac", "ab" };
            List<string> outer = new();
            foreach (string word in trie)
            {
                outer.Add(word);
                List<string> inner = new();
                foreach (string nested in trie)
                {
                    inner.Add(nested);
                }
                CollectionAssert.AreEqual(expected, inner);
            }
            CollectionAssert.AreEqual(expected, outer);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisposalReleasesOnceAndPreventsUseOfReturnedScratch(bool advance)
        {
            Trie trie = new(new[] { "a", "ab", "b" });
            PoolStatistics before = Buffers.StringBuilder.GetStatistics();
            Trie.Enumerator enumerator = trie.GetEnumerator();
            Trie.Enumerator copy = enumerator;
            try
            {
                if (advance)
                {
                    Assert.IsTrue(enumerator.MoveNext());
                }
            }
            finally
            {
                enumerator.Dispose();
            }
            copy.Dispose();
            enumerator.Dispose();
            Assert.AreEqual(
                1,
                Buffers.StringBuilder.GetStatistics().ReturnCount - before.ReturnCount
            );
            using PooledResource<StringBuilder> lease = Buffers.StringBuilder.Get(
                out StringBuilder builder
            );
            builder.Append("borrowed");
            Assert.IsFalse(enumerator.MoveNext());
            Assert.IsFalse(copy.MoveNext());
            Assert.AreEqual("borrowed", builder.ToString());
        }

        [Test]
        public void DefaultEnumeratorIsInert()
        {
            Trie.Enumerator enumerator = default;
            Assert.IsTrue(enumerator.Current == null);
            Assert.DoesNotThrow(() => enumerator.Dispose());
            Assert.IsFalse(enumerator.MoveNext());
            enumerator.Dispose();
            Assert.IsTrue(enumerator.Equals(default(Trie.Enumerator)));
        }

        [Test]
        public void DefaultGenericEnumeratorIsInert()
        {
            Trie<int>.Enumerator enumerator = default;
            Assert.AreEqual(0, enumerator.Current);
            enumerator.Dispose();
            Assert.IsFalse(enumerator.MoveNext());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GenericDisposalPreventsUseOfReturnedScratch(bool advance)
        {
            Trie<int> trie = new(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
            Trie<int>.Enumerator enumerator = trie.GetEnumerator();
            Trie<int>.Enumerator copy = enumerator;
            try
            {
                if (advance)
                {
                    Assert.IsTrue(enumerator.MoveNext());
                }
            }
            finally
            {
                enumerator.Dispose();
            }
            copy.Dispose();
            using PooledResource<Stack<int>> lease = Buffers<int>.Stack.Get(out Stack<int> stack);
            stack.Push(int.MaxValue);
            Assert.IsFalse(enumerator.MoveNext());
            Assert.IsFalse(copy.MoveNext());
            Assert.IsTrue(stack.TryPeek(out int value));
            Assert.AreEqual(int.MaxValue, value);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(256)]
        public void PoolStatisticsObserveSimultaneouslyHeldBuilders(int count)
        {
            PooledResource<StringBuilder>[] leases = new PooledResource<StringBuilder>[count];
            PoolStatistics before = Buffers.StringBuilder.GetStatistics();
            try
            {
                for (int index = 0; index < count; ++index)
                {
                    leases[index] = Buffers.StringBuilder.Get();
                }
                Assert.AreEqual(
                    count,
                    Buffers.StringBuilder.GetStatistics().RentCount - before.RentCount
                );
                Assert.AreEqual(
                    0,
                    Buffers.StringBuilder.GetStatistics().ReturnCount - before.ReturnCount
                );
            }
            finally
            {
                foreach (PooledResource<StringBuilder> lease in leases)
                {
                    lease.Dispose();
                }
            }
            Assert.AreEqual(
                count,
                Buffers.StringBuilder.GetStatistics().ReturnCount - before.ReturnCount
            );
        }
    }
}
