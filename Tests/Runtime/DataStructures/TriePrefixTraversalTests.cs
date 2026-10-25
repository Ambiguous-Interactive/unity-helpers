// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [Category("Fast")]
    public sealed class TriePrefixTraversalTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(50000)]
        [TestCase(100000)]
        public void DeepWordSearchPreservesBranchesAndLimits(int prefixLength)
        {
            string stem = new('x', 100000);
            string[] words = { stem, stem + "a", stem + "ab", stem + "b", "z" };
            Trie trie = new(words);
            List<string> results = new() { "stale" };
            string prefix = stem.Substring(0, prefixLength);
            Assert.AreSame(results, trie.GetWordsWithPrefix(prefix, results));
            CollectionAssert.AreEqual(
                prefixLength == 0 ? words : new[] { words[0], words[1], words[2], words[3] },
                results
            );
            trie.GetWordsWithPrefix(prefix, results, 2);
            CollectionAssert.AreEqual(new[] { words[0], words[1] }, results);
            trie.GetWordsWithPrefix(stem + "missing", results);
            CollectionAssert.IsEmpty(results);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(50000)]
        [TestCase(100000)]
        public void DeepValueSearchPreservesBranchesAndLimits(int prefixLength)
        {
            string stem = new('x', 100000);
            Dictionary<string, int> items = new()
            {
                [stem] = 1,
                [stem + "a"] = 2,
                [stem + "ab"] = 3,
                [stem + "b"] = 4,
                ["z"] = 5,
            };
            Trie<int> trie = new(items);
            List<int> results = new() { -1 };
            string prefix = stem.Substring(0, prefixLength);
            Assert.AreSame(results, trie.GetValuesWithPrefix(prefix, results));
            CollectionAssert.AreEqual(
                prefixLength == 0 ? new[] { 1, 2, 3, 4, 5 } : new[] { 1, 2, 3, 4 },
                results
            );
            trie.GetValuesWithPrefix(prefix, results, 2);
            CollectionAssert.AreEqual(new[] { 1, 2 }, results);
            trie.GetValuesWithPrefix(stem + "missing", results);
            CollectionAssert.IsEmpty(results);
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(int.MaxValue)]
        public void WordSearchRetainsDepthFirstSiblingOrder(int maximum)
        {
            string[] input = { "cat", "dog", "car", "carbon", "do", "cape", "z", "" };
            string[] expected = { "", "cat", "car", "carbon", "cape", "do", "dog", "z" };
            Trie trie = new(input);
            List<string> results = new() { "stale" };
            trie.GetWordsWithPrefix("", results, maximum);
            CollectionAssert.AreEqual(
                new ArraySegment<string>(
                    expected,
                    0,
                    Math.Min(expected.Length, Math.Max(0, maximum))
                ),
                results
            );
            trie.GetWordsWithPrefix("car", results);
            CollectionAssert.AreEqual(new[] { "car", "carbon" }, results);
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(int.MaxValue)]
        public void ValueSearchRetainsDepthFirstSiblingOrder(int maximum)
        {
            Dictionary<string, string> items = new()
            {
                ["cat"] = "cat",
                ["dog"] = "dog",
                ["car"] = "car",
                ["carbon"] = "carbon",
                ["do"] = "do",
                ["cape"] = "cape",
                ["z"] = "z",
                [""] = "",
            };
            string[] expected = { "", "cat", "car", "carbon", "cape", "do", "dog", "z" };
            Trie<string> trie = new(items);
            List<string> results = new() { "stale" };
            trie.GetValuesWithPrefix("", results, maximum);
            CollectionAssert.AreEqual(
                new ArraySegment<string>(
                    expected,
                    0,
                    Math.Min(expected.Length, Math.Max(0, maximum))
                ),
                results
            );
            trie.GetValuesWithPrefix("car", results);
            CollectionAssert.AreEqual(new[] { "car", "carbon" }, results);
        }
    }
}
