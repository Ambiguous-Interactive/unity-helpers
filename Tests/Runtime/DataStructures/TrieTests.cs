// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class TrieTests
    {
        [Test]
        public void ConstructorWithEmptyListCreatesValidTrie()
        {
            Trie trie = new(Array.Empty<string>());
            Assert.IsFalse(trie.Contains("test"));
        }

        [Test]
        public void ConstructorWithSingleWordCreatesValidTrie()
        {
            Trie trie = new(new[] { "test" });
            Assert.IsTrue(trie.Contains("test"));
        }

        [Test]
        public void ConstructorWithMultipleWordsCreatesValidTrie()
        {
            string[] words = { "apple", "banana", "cherry" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word));
            }
        }

        [Test]
        public void ContainsReturnsFalseForNonExistentWord()
        {
            Trie trie = new(new[] { "test", "testing" });
            Assert.IsFalse(trie.Contains("nonexistent"));
        }

        [Test]
        public void ContainsReturnsFalseForPrefix()
        {
            Trie trie = new(new[] { "testing" });
            Assert.IsFalse(trie.Contains("test"));
        }

        [Test]
        public void ContainsReturnsTrueForExactWord()
        {
            Trie trie = new(new[] { "test", "testing" });
            Assert.IsTrue(trie.Contains("test"));
            Assert.IsTrue(trie.Contains("testing"));
        }

        [Test]
        public void ContainsReturnsFalseForEmptyString()
        {
            Trie trie = new(new[] { "test" });
            Assert.IsFalse(trie.Contains(""));
        }

        [Test]
        public void ContainsHandlesEmptyStringInWordList()
        {
            Trie trie = new(new[] { "", "test" });
            Assert.IsTrue(trie.Contains(""));
            Assert.IsTrue(trie.Contains("test"));
        }

        [Test]
        public void ContainsCaseSensitive()
        {
            Trie trie = new(new[] { "test" });
            Assert.IsTrue(trie.Contains("test"));
            Assert.IsFalse(trie.Contains("TEST"));
            Assert.IsFalse(trie.Contains("Test"));
        }

        [Test]
        public void GetWordsWithPrefixReturnsEmptyForNonMatchingPrefix()
        {
            Trie trie = new(new[] { "apple", "banana", "cherry" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("xyz", results).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetWordsWithPrefixReturnsMatchingWords()
        {
            Trie trie = new(new[] { "apple", "application", "apply", "banana" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("app", results).Count;

            Assert.AreEqual(3, count);
            Assert.AreEqual(3, results.Count);
            CollectionAssert.AreEquivalent(new[] { "apple", "application", "apply" }, results);
        }

        [Test]
        public void GetWordsWithPrefixReturnsAllWordsForEmptyPrefix()
        {
            string[] words = { "apple", "banana", "cherry" };
            Trie trie = new(words);
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("", results).Count;

            Assert.AreEqual(3, count);
            CollectionAssert.AreEquivalent(words, results);
        }

        [Test]
        public void GetWordsWithPrefixRespectsMaxResults()
        {
            Trie trie = new(new[] { "apple", "application", "apply", "approach" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("app", results, maxResults: 2).Count;

            Assert.AreEqual(2, count);
            Assert.AreEqual(2, results.Count);
        }

        [Test]
        public void GetWordsWithPrefixClearsResultsList()
        {
            Trie trie = new(new[] { "apple", "banana" });
            List<string> results = new() { "old1", "old2" };
            trie.GetWordsWithPrefix("app", results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("apple", results[0]);
        }

        [Test]
        public void GetWordsWithPrefixHandlesExactMatch()
        {
            Trie trie = new(new[] { "test", "testing", "tester" });
            List<string> results = new();
            trie.GetWordsWithPrefix("test", results);

            Assert.AreEqual(3, results.Count);
            CollectionAssert.Contains(results, "test");
        }

        [Test]
        public void GetWordsWithPrefixHandlesLongPrefix()
        {
            Trie trie = new(new[] { "testing" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("testing", results).Count;

            Assert.AreEqual(1, count);
            Assert.AreEqual("testing", results[0]);
        }

        [Test]
        public void GetWordsWithPrefixHandlesPrefixLongerThanAnyWord()
        {
            Trie trie = new(new[] { "test" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("testingextra", results).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void TrieHandlesWordsWithCommonPrefixes()
        {
            string[] words = { "car", "card", "care", "careful", "carefully" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word), $"Word '{word}' should be in trie");
            }

            List<string> results = new();
            trie.GetWordsWithPrefix("car", results);
            Assert.AreEqual(5, results.Count);
            CollectionAssert.AreEquivalent(words, results);
        }

        [Test]
        public void TrieHandlesSingleCharacterWords()
        {
            Trie trie = new(new[] { "a", "b", "c" });
            Assert.IsTrue(trie.Contains("a"));
            Assert.IsTrue(trie.Contains("b"));
            Assert.IsTrue(trie.Contains("c"));
            Assert.IsFalse(trie.Contains("d"));
        }

        [Test]
        public void TrieHandlesUnicodeCharacters()
        {
            string[] words = { "世界", "世", "世界和平" };
            Trie trie = new(words);

            Assert.IsTrue(trie.Contains("世界"));
            Assert.IsTrue(trie.Contains("世"));
            Assert.IsTrue(trie.Contains("世界和平"));
        }

        [Test]
        public void TrieHandlesSpecialCharacters()
        {
            string[] words = { "hello-world", "hello_world", "hello.world" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word));
            }
        }

        [Test]
        public void TrieHandlesDuplicateWords()
        {
            Trie trie = new(new[] { "test", "test", "test" });
            Assert.IsTrue(trie.Contains("test"));

            List<string> results = new();
            trie.GetWordsWithPrefix("test", results);
            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void TrieHandlesVeryLongWords()
        {
            string longWord = new('a', 1000);
            Trie trie = new(new[] { longWord });
            Assert.IsTrue(trie.Contains(longWord));
        }

        [Test]
        public void TrieHandlesManyWords()
        {
            string[] words = Enumerable.Range(0, 1000).Select(i => $"word{i}").ToArray();
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word));
            }
        }

        [Test]
        public void ContainsReturnsFalseForSuperstring()
        {
            Trie trie = new(new[] { "test" });
            Assert.IsFalse(trie.Contains("testing"));
            Assert.IsFalse(trie.Contains("test1"));
        }

        [Test]
        public void ContainsWithNullStringThrows()
        {
            Trie trie = new(new[] { "test" });
            Assert.Throws<NullReferenceException>(() => trie.Contains(null));
        }

        [Test]
        public void GetWordsWithPrefixWithNullPrefixThrows()
        {
            Trie trie = new(new[] { "test" });
            List<string> results = new();
            Assert.Throws<NullReferenceException>(() => trie.GetWordsWithPrefix(null, results));
        }

        [Test]
        public void GetWordsWithPrefixWithNullListThrows()
        {
            Trie trie = new(new[] { "test" });
            Assert.Throws<NullReferenceException>(() => trie.GetWordsWithPrefix("test", null));
        }

        [Test]
        public void GetWordsWithPrefixWithZeroMaxResults()
        {
            Trie trie = new(new[] { "apple", "application", "apply" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("app", results, maxResults: 0).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetWordsWithPrefixWithNegativeMaxResults()
        {
            Trie trie = new(new[] { "apple", "application", "apply" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("app", results, maxResults: -1).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetWordsWithPrefixWithOneMaxResult()
        {
            Trie trie = new(new[] { "apple", "application", "apply" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("app", results, maxResults: 1).Count;

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void TrieHandlesWordsWhereOneIsCompleteSubstringOfAnother()
        {
            string[] words = { "a", "ab", "abc", "abcd", "abcde" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word), $"Word '{word}' should be in trie");
            }

            Assert.IsFalse(trie.Contains("abcdef"));
            Assert.IsFalse(trie.Contains(""));
        }

        [Test]
        public void TrieHandlesWordsThatShareNoCommonPrefix()
        {
            string[] words = { "apple", "banana", "cherry", "date", "elderberry" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word));
            }
        }

        [Test]
        public void TrieHandlesBranchingAtEachLevel()
        {
            string[] words = { "a", "b", "aa", "ab", "ba", "bb", "aaa", "aab", "aba", "abb" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word), $"Word '{word}' should be in trie");
            }

            Assert.IsFalse(trie.Contains("baa"));
            Assert.IsFalse(trie.Contains("c"));
        }

        [Test]
        public void GetWordsWithPrefixReturnsWordInCorrectOrderForNestedWords()
        {
            Trie trie = new(new[] { "test", "tester", "testing" });
            List<string> results = new();
            trie.GetWordsWithPrefix("test", results);

            Assert.AreEqual(3, results.Count);
            CollectionAssert.Contains(results, "test");
            CollectionAssert.Contains(results, "tester");
            CollectionAssert.Contains(results, "testing");
        }

        [Test]
        public void GetWordsWithPrefixWithSingleCharacterPrefix()
        {
            Trie trie = new(new[] { "apple", "application", "banana", "apply" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("a", results).Count;

            Assert.AreEqual(3, count);
            CollectionAssert.AreEquivalent(new[] { "apple", "application", "apply" }, results);
        }

        [Test]
        public void GetWordsWithPrefixHandlesDeepNesting()
        {
            string[] words = { "a", "ab", "abc", "abcd", "abcde", "abcdef" };
            Trie trie = new(words);
            List<string> results = new();

            trie.GetWordsWithPrefix("abc", results);
            Assert.AreEqual(4, results.Count);
            CollectionAssert.AreEquivalent(new[] { "abc", "abcd", "abcde", "abcdef" }, results);
        }

        [Test]
        public void ContainsHandlesRepeatingCharacters()
        {
            Trie trie = new(new[] { "aaa", "aaaa", "aaaaa" });
            Assert.IsTrue(trie.Contains("aaa"));
            Assert.IsTrue(trie.Contains("aaaa"));
            Assert.IsTrue(trie.Contains("aaaaa"));
            Assert.IsFalse(trie.Contains("aa"));
            Assert.IsFalse(trie.Contains("aaaaaa"));
        }

        [Test]
        public void TrieHandlesAllSameCharacterWords()
        {
            string[] words = { "a", "aa", "aaa", "aaaa" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word));
            }

            List<string> results = new();
            trie.GetWordsWithPrefix("aa", results);
            Assert.AreEqual(3, results.Count);
            CollectionAssert.AreEquivalent(new[] { "aa", "aaa", "aaaa" }, results);
        }

        [Test]
        public void GetWordsWithPrefixWhenPrefixIsNotAWord()
        {
            Trie trie = new(new[] { "testing", "tester" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("test", results).Count;

            Assert.AreEqual(2, count);
            Assert.IsFalse(trie.Contains("test"));
        }

        [Test]
        public void TrieHandlesWhitespaceInWords()
        {
            string[] words = { "hello world", "hello  world", "hello\tworld", "hello\nworld" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word), $"Word should be in trie: '{word}'");
            }

            Assert.IsFalse(trie.Contains("hello"));
            Assert.IsFalse(trie.Contains("world"));
        }

        [Test]
        public void TrieHandlesNumericStrings()
        {
            string[] words = { "123", "1234", "12345", "234", "345" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word));
            }

            List<string> results = new();
            trie.GetWordsWithPrefix("123", results);
            Assert.AreEqual(3, results.Count);
            CollectionAssert.AreEquivalent(new[] { "123", "1234", "12345" }, results);
        }

        [Test]
        public void TrieHandlesMixedAlphanumeric()
        {
            string[] words = { "a1b2c3", "a1b2", "a1", "1a2b3c" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word));
            }
        }

        [Test]
        public void GetWordsWithPrefixReturnsCorrectCountWhenMaxResultsExceedsAvailable()
        {
            Trie trie = new(new[] { "apple", "application" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("app", results, maxResults: 100).Count;

            Assert.AreEqual(2, count);
            Assert.AreEqual(2, results.Count);
        }

        [Test]
        public void ContainsMultipleCallsAreIndependent()
        {
            Trie trie = new(new[] { "test", "testing" });
            Assert.IsTrue(trie.Contains("test"));
            Assert.IsTrue(trie.Contains("testing"));
            Assert.IsFalse(trie.Contains("tested"));
            Assert.IsTrue(trie.Contains("test"));
        }

        [Test]
        public void GetWordsWithPrefixMultipleCallsAreIndependent()
        {
            Trie trie = new(new[] { "apple", "application", "apply", "banana" });
            List<string> results = new();

            trie.GetWordsWithPrefix("app", results);
            Assert.AreEqual(3, results.Count);

            trie.GetWordsWithPrefix("ban", results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("banana", results[0]);

            trie.GetWordsWithPrefix("app", results);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void TrieHandlesWordsWithOnlySpecialCharacters()
        {
            string[] words = { "!!!", "@@@", "###", "$$$" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word));
            }
        }

        [Test]
        public void GetWordsWithPrefixWithSpecialCharacterPrefix()
        {
            Trie trie = new(new[] { "!test", "!testing", "!tester", "#test" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("!", results).Count;

            Assert.AreEqual(3, count);
            CollectionAssert.AreEquivalent(new[] { "!test", "!testing", "!tester" }, results);
        }

        [Test]
        public void TrieHandlesEmojiCharacters()
        {
            string[] words = { "😀", "😀😀", "😁", "test😀" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word), $"Word should be in trie: '{word}'");
            }
        }

        [Test]
        public void TrieHandlesSurrogatePairs()
        {
            string[] words = { "𝕳𝖊𝖑𝖑𝖔", "𝕳𝖊𝖑", "test" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word));
            }
        }

        [Test]
        public void GetWordsWithPrefixHandlesLargeMaxResults()
        {
            Trie trie = new(new[] { "apple", "application" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("app", results, maxResults: int.MaxValue).Count;

            Assert.AreEqual(2, count);
            Assert.AreEqual(2, results.Count);
        }

        [Test]
        public void TrieHandlesWordsStartingWithSameCharacterButDifferentSecondChar()
        {
            string[] words = { "aa", "ab", "ac", "ad", "ae" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word));
            }

            List<string> results = new();
            trie.GetWordsWithPrefix("a", results);
            Assert.AreEqual(5, results.Count);
        }

        [Test]
        public void ContainsAfterConstructingWithEmptyTrie()
        {
            Trie trie = new(Array.Empty<string>());
            Assert.IsFalse(trie.Contains("anything"));
            Assert.IsFalse(trie.Contains(""));
            Assert.IsFalse(trie.Contains("a"));
        }

        [Test]
        public void GetWordsWithPrefixOnEmptyTrie()
        {
            Trie trie = new(Array.Empty<string>());
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("test", results).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetWordsWithPrefixEmptyPrefixOnEmptyTrie()
        {
            Trie trie = new(Array.Empty<string>());
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("", results).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void TrieHandlesOnlyEmptyString()
        {
            Trie trie = new(new[] { "" });
            Assert.IsTrue(trie.Contains(""));
            Assert.IsFalse(trie.Contains("a"));
        }

        [Test]
        public void GetWordsWithPrefixReturnsEmptyStringWhenPresent()
        {
            Trie trie = new(new[] { "", "test" });
            List<string> results = new();
            trie.GetWordsWithPrefix("", results);

            Assert.AreEqual(2, results.Count);
            CollectionAssert.Contains(results, "");
            CollectionAssert.Contains(results, "test");
        }

        [Test]
        public void TrieHandlesMultipleEmptyStrings()
        {
            Trie trie = new(new[] { "", "", "" });
            Assert.IsTrue(trie.Contains(""));

            List<string> results = new();
            trie.GetWordsWithPrefix("", results);
            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void ContainsDistinguishesBetweenSimilarWords()
        {
            Trie trie = new(new[] { "car", "cart", "card", "care" });
            Assert.IsTrue(trie.Contains("car"));
            Assert.IsTrue(trie.Contains("cart"));
            Assert.IsTrue(trie.Contains("card"));
            Assert.IsTrue(trie.Contains("care"));
            Assert.IsFalse(trie.Contains("ca"));
            Assert.IsFalse(trie.Contains("cars"));
            Assert.IsFalse(trie.Contains("carts"));
        }

        [Test]
        public void TrieConstructorWithNullEnumerableThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new Trie(null));
        }

        [Test]
        public void TrieHandlesWordsWithControlCharacters()
        {
            string[] words = { "test\r\nline", "test\r", "test\n", "test\t" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word), $"Word should be in trie: '{word}'");
            }
        }

        [Test]
        public void GetWordsWithPrefixStopsAtMaxResultsInMiddleOfTraversal()
        {
            string[] words = { "a", "aa", "aaa", "aaaa", "aaaaa", "aaaaaa" };
            Trie trie = new(words);
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("a", results, maxResults: 3).Count;

            Assert.AreEqual(3, count);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void TrieHandlesWordsWithLeadingAndTrailingSpaces()
        {
            string[] words = { " test", "test ", " test " };
            Trie trie = new(words);

            Assert.IsTrue(trie.Contains(" test"));
            Assert.IsTrue(trie.Contains("test "));
            Assert.IsTrue(trie.Contains(" test "));
            Assert.IsFalse(trie.Contains("test"));
        }

        [Test]
        public void GetWordsWithPrefixWithPrefixMatchingCompleteWord()
        {
            Trie trie = new(new[] { "test" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("test", results).Count;

            Assert.AreEqual(1, count);
            Assert.AreEqual("test", results[0]);
        }

        [Test]
        public void TrieHandlesIdenticalWordsInDifferentCase()
        {
            string[] words = { "test", "Test", "TEST", "TeSt" };
            Trie trie = new(words);

            foreach (string word in words)
            {
                Assert.IsTrue(trie.Contains(word));
            }

            List<string> results = new();
            trie.GetWordsWithPrefix("T", results);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void EnumeratorIteratesAllWords()
        {
            string[] words = { "apple", "banana", "cherry" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorWorksWithEmptyTrie()
        {
            Trie trie = new(Array.Empty<string>());

            int count = 0;
            foreach (string word in trie)
            {
                count++;
            }

            Assert.AreEqual(0, count);
        }

        [Test]
        public void EnumeratorHandlesSingleWord()
        {
            Trie trie = new(new[] { "test" });

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(1, enumerated.Count);
            Assert.AreEqual("test", enumerated[0]);
        }

        [Test]
        public void EnumeratorHandlesWordsWithCommonPrefixes()
        {
            string[] words = { "test", "testing", "tester", "tea" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(4, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorCanBeUsedMultipleTimes()
        {
            string[] words = { "apple", "banana", "cherry" };
            Trie trie = new(words);

            List<string> firstEnumeration = new();
            foreach (string word in trie)
            {
                firstEnumeration.Add(word);
            }

            List<string> secondEnumeration = new();
            foreach (string word in trie)
            {
                secondEnumeration.Add(word);
            }

            Assert.AreEqual(3, firstEnumeration.Count);
            Assert.AreEqual(3, secondEnumeration.Count);
            CollectionAssert.AreEquivalent(firstEnumeration, secondEnumeration);
        }

        [Test]
        public void EnumeratorHandlesComplexTrieStructure()
        {
            string[] words = { "a", "ab", "abc", "b", "bc", "c" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(6, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorHandlesUnicodeWords()
        {
            string[] words = { "café", "naïve", "résumé" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorHandlesEmptyString()
        {
            string[] words = { "", "a", "ab" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorHandlesLongWords()
        {
            string longWord = new('a', 1000);
            string[] words = { "short", longWord, "medium" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorHandlesManyWords()
        {
            List<string> words = new();
            for (int i = 0; i < 1000; i++)
            {
                words.Add($"word{i}");
            }
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(1000, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorUsesValueTypeForEfficiency()
        {
            string[] words = { "test" };
            Trie trie = new(words);

            using Trie.Enumerator enumerator = trie.GetEnumerator();
            Assert.IsTrue(enumerator.GetType().IsValueType);
        }

        [Test]
        public void EnumeratorCanBreakEarly()
        {
            string[] words = { "apple", "banana", "cherry", "date", "elderberry" };
            Trie trie = new(words);

            int count = 0;
            foreach (string word in trie)
            {
                count++;
                if (count == 3)
                {
                    break;
                }
            }

            Assert.AreEqual(3, count);
        }

        [Test]
        public void EnumeratorWorksWithLinq()
        {
            string[] words = { "apple", "apricot", "banana", "cherry" };
            Trie trie = new(words);

            List<string> filtered = trie.Where(w => w.StartsWith("a")).ToList();

            Assert.AreEqual(2, filtered.Count);
            Assert.IsTrue(filtered.Contains("apple"));
            Assert.IsTrue(filtered.Contains("apricot"));
        }

        [Test]
        public void EnumeratorHandlesDuplicateWords()
        {
            string[] words = { "test", "test", "unique" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(2, enumerated.Count);
            Assert.IsTrue(enumerated.Contains("test"));
            Assert.IsTrue(enumerated.Contains("unique"));
        }

        [Test]
        public void EnumeratorHandlesSpecialCharacters()
        {
            string[] words = { "hello!", "world?", "test@123" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorHandlesOnlyEmptyString()
        {
            string[] words = { "" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(1, enumerated.Count);
            Assert.AreEqual("", enumerated[0]);
        }

        [Test]
        public void EnumeratorHandlesMultipleEmptyStrings()
        {
            string[] words = { "", "", "" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(1, enumerated.Count);
            Assert.AreEqual("", enumerated[0]);
        }

        [Test]
        public void EnumeratorHandlesWordsAsCompleteSubsets()
        {
            string[] words = { "a", "aa", "aaa", "aaaa" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(4, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void MultipleEnumeratorsCanRunSimultaneously()
        {
            string[] words = { "apple", "banana", "cherry", "date" };
            Trie trie = new(words);

            using Trie.Enumerator enumerator1 = trie.GetEnumerator();
            using Trie.Enumerator enumerator2 = trie.GetEnumerator();

            List<string> list1 = new();
            List<string> list2 = new();

            Assert.IsTrue(enumerator1.MoveNext());
            list1.Add(enumerator1.Current);

            Assert.IsTrue(enumerator2.MoveNext());
            list2.Add(enumerator2.Current);

            Assert.IsTrue(enumerator1.MoveNext());
            list1.Add(enumerator1.Current);

            while (enumerator1.MoveNext())
            {
                list1.Add(enumerator1.Current);
            }

            while (enumerator2.MoveNext())
            {
                list2.Add(enumerator2.Current);
            }

            Assert.AreEqual(4, list1.Count);
            Assert.AreEqual(4, list2.Count);
            CollectionAssert.AreEquivalent(words, list1);
            CollectionAssert.AreEquivalent(words, list2);
        }

        [Test]
        public void EnumeratorHandlesNestedPrefixesWithEmptyString()
        {
            string[] words = { "", "a", "ab", "abc", "abcd" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(5, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorHandlesDeeplyNestedSingleBranch()
        {
            List<string> words = new();
            for (int i = 1; i <= 100; i++)
            {
                words.Add(new string('a', i));
            }
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(100, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorHandlesWideBranching()
        {
            List<string> words = new();
            for (char c = 'a'; c <= 'z'; c++)
            {
                words.Add(c.ToString());
            }
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(26, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorWithIEnumerableInterface()
        {
            string[] words = { "test1", "test2", "test3" };
            Trie trie = new(words);

            System.Collections.IEnumerable enumerable = trie;
            List<string> enumerated = new();

            foreach (object word in enumerable)
            {
                enumerated.Add((string)word);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorHandlesWhitespaceOnlyWords()
        {
            string[] words = { " ", "  ", "   ", "\t", "\n" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(5, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void GetWordsWithPrefixHandlesMaxResultsZero()
        {
            Trie trie = new(new[] { "apple", "application", "apply" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("app", results, maxResults: 0).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetWordsWithPrefixHandlesMaxResultsNegative()
        {
            Trie trie = new(new[] { "apple", "application", "apply" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("app", results, maxResults: -1).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetWordsWithPrefixIncludesEmptyStringWhenPrefixIsEmpty()
        {
            string[] words = { "", "a", "b" };
            Trie trie = new(words);
            List<string> results = new();
            trie.GetWordsWithPrefix("", results);

            Assert.AreEqual(3, results.Count);
            CollectionAssert.AreEquivalent(words, results);
            Assert.IsTrue(results.Contains(""));
        }

        [Test]
        public void GetWordsWithPrefixWithEmptyStringAsPrefix()
        {
            string[] words = { "", "test" };
            Trie trie = new(words);
            List<string> results = new();
            trie.GetWordsWithPrefix("", results);

            Assert.AreEqual(2, results.Count);
            CollectionAssert.Contains(results, "");
            CollectionAssert.Contains(results, "test");
        }

        [Test]
        public void EnumeratorHandlesAlternatingBranches()
        {
            string[] words = { "a", "b", "aa", "bb", "aaa", "bbb" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(6, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorHandlesMixedLengthWords()
        {
            string[] words = { "x", "xx", "xxx", "y", "yy", "z" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(6, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void ContainsHandlesNullCharacterInWord()
        {
            string[] words = { "test\0ing", "normal" };
            Trie trie = new(words);

            Assert.IsTrue(trie.Contains("test\0ing"));
            Assert.IsTrue(trie.Contains("normal"));
            Assert.IsFalse(trie.Contains("testing"));
        }

        [Test]
        public void EnumeratorHandlesNullCharacterInWords()
        {
            string[] words = { "abc\0def", "test\0", "\0start" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void GetWordsWithPrefixHandlesPrefixWithNullCharacter()
        {
            string[] words = { "test\0a", "test\0b", "other" };
            Trie trie = new(words);
            List<string> results = new();
            trie.GetWordsWithPrefix("test\0", results);

            Assert.AreEqual(2, results.Count);
            CollectionAssert.Contains(results, "test\0a");
            CollectionAssert.Contains(results, "test\0b");
        }

        [Test]
        public void EnumeratorHandlesComplexUnicodeMixedWithAscii()
        {
            string[] words = { "test", "测试", "test测试", "🎉emoji", "mixed🎉test" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(5, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorHandlesSurrogatePairs()
        {
            string[] words = { "𝕳𝖊𝖑𝖑𝖔", "𝓦𝓸𝓻𝓵𝓭", "🚀🌟" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void GetWordsWithPrefixStressTestWithManyResults()
        {
            List<string> words = new();
            for (int i = 0; i < 10000; i++)
            {
                words.Add($"prefix{i}");
            }
            Trie trie = new(words);
            List<string> results = new();
            trie.GetWordsWithPrefix("prefix", results);

            Assert.AreEqual(10000, results.Count);
        }

        [Test]
        public void EnumeratorStressTestWithDeeplyNestedAndWideBranching()
        {
            List<string> words = new();

            for (int i = 0; i < 100; i++)
            {
                words.Add(new string('a', i + 1));
                words.Add(new string('b', i + 1));
                words.Add(new string('c', i + 1));
            }
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(300, enumerated.Count);
            CollectionAssert.AreEquivalent(words, enumerated);
        }

        [Test]
        public void EnumeratorHandlesSingleCharacterRepeated()
        {
            string[] words = { "a", "a", "a" };
            Trie trie = new(words);

            List<string> enumerated = new();
            foreach (string word in trie)
            {
                enumerated.Add(word);
            }

            Assert.AreEqual(1, enumerated.Count);
            Assert.AreEqual("a", enumerated[0]);
        }

        [Test]
        public void GetWordsWithPrefixWithMaxResultsLargerThanAvailable()
        {
            Trie trie = new(new[] { "a", "b" });
            List<string> results = new();
            int count = trie.GetWordsWithPrefix("", results, maxResults: 1000).Count;

            Assert.AreEqual(2, count);
            Assert.AreEqual(2, results.Count);
        }

        [Test]
        public void EnumeratorCurrentBeforeMoveNextReturnsDefault()
        {
            Trie trie = new(new[] { "test" });
            using Trie.Enumerator enumerator = trie.GetEnumerator();

            Assert.IsTrue(
                enumerator.Current == null,
                "Enumerator.Current should be null before first MoveNext"
            );
        }

        [Test]
        public void EnumeratorCurrentAfterLastMoveNextReturnsLastValue()
        {
            Trie trie = new(new[] { "single" });
            using Trie.Enumerator enumerator = trie.GetEnumerator();

            Assert.IsTrue(enumerator.MoveNext());
            string lastValue = enumerator.Current;
            Assert.IsFalse(enumerator.MoveNext());

            Assert.AreEqual(lastValue, enumerator.Current);
        }

        [Test]
        public void ContainsWithVeryLongWord()
        {
            string longWord = new('x', 10000);
            Trie trie = new(new[] { longWord });

            Assert.IsTrue(trie.Contains(longWord));
            Assert.IsFalse(trie.Contains(longWord + "y"));
        }

        [Test]
        public void GetWordsWithPrefixWithVeryLongPrefix()
        {
            string longPrefix = new('x', 5000);
            string longerWord = longPrefix + "extra";
            Trie trie = new(new[] { longerWord });
            List<string> results = new();
            trie.GetWordsWithPrefix(longPrefix, results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(longerWord, results[0]);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class GenericTrieTests
    {
        [Test]
        public void ConstructorWithEmptyDictionaryCreatesValidTrie()
        {
            Trie<int> trie = new(new Dictionary<string, int>());
            Assert.IsFalse(trie.TryGetValue("test", out _));
        }

        [Test]
        public void ConstructorWithNullDictionaryThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new Trie<int>(null));
        }

        [Test]
        public void ConstructorWithSingleEntryCreatesValidTrie()
        {
            Dictionary<string, int> dict = new() { { "test", 42 } };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("test", out int value));
            Assert.AreEqual(42, value);
        }

        [Test]
        public void ConstructorWithMultipleEntriesCreatesValidTrie()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "banana", 2 },
                { "cherry", 3 },
            };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("apple", out int value1));
            Assert.AreEqual(1, value1);
            Assert.IsTrue(trie.TryGetValue("banana", out int value2));
            Assert.AreEqual(2, value2);
            Assert.IsTrue(trie.TryGetValue("cherry", out int value3));
            Assert.AreEqual(3, value3);
        }

        [Test]
        public void TryGetValueReturnsFalseForNonExistentKey()
        {
            Dictionary<string, string> dict = new() { { "test", "value" } };
            Trie<string> trie = new(dict);

            Assert.IsFalse(trie.TryGetValue("nonexistent", out string value));
            Assert.IsTrue(value == null, "Value should be null for non-existent key");
        }

        [Test]
        public void TryGetValueReturnsFalseForPrefix()
        {
            Dictionary<string, int> dict = new() { { "testing", 100 } };
            Trie<int> trie = new(dict);

            Assert.IsFalse(trie.TryGetValue("test", out _));
        }

        [Test]
        public void TryGetValueReturnsTrueForExactKey()
        {
            Dictionary<string, string> dict = new()
            {
                { "test", "value1" },
                { "testing", "value2" },
            };
            Trie<string> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("test", out string value1));
            Assert.AreEqual("value1", value1);
            Assert.IsTrue(trie.TryGetValue("testing", out string value2));
            Assert.AreEqual("value2", value2);
        }

        [Test]
        public void TryGetValueReturnsFalseForEmptyString()
        {
            Dictionary<string, int> dict = new() { { "test", 42 } };
            Trie<int> trie = new(dict);

            Assert.IsFalse(trie.TryGetValue("", out _));
        }

        [Test]
        public void TryGetValueHandlesEmptyStringInDictionary()
        {
            Dictionary<string, int> dict = new() { { "", 0 }, { "test", 42 } };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("", out int value1));
            Assert.AreEqual(0, value1);
            Assert.IsTrue(trie.TryGetValue("test", out int value2));
            Assert.AreEqual(42, value2);
        }

        [Test]
        public void TryGetValueCaseSensitive()
        {
            Dictionary<string, int> dict = new() { { "test", 42 } };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("test", out int value));
            Assert.AreEqual(42, value);
            Assert.IsFalse(trie.TryGetValue("TEST", out _));
            Assert.IsFalse(trie.TryGetValue("Test", out _));
        }

        [Test]
        public void GetValuesWithPrefixReturnsEmptyForNonMatchingPrefix()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "banana", 2 },
                { "cherry", 3 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();

            int count = trie.GetValuesWithPrefix("xyz", results).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetValuesWithPrefixReturnsMatchingValues()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "application", 2 },
                { "apply", 3 },
                { "banana", 4 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();

            int count = trie.GetValuesWithPrefix("app", results).Count;

            Assert.AreEqual(3, count);
            Assert.AreEqual(3, results.Count);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, results);
        }

        [Test]
        public void GetValuesWithPrefixReturnsAllValuesForEmptyPrefix()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "banana", 2 },
                { "cherry", 3 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();

            int count = trie.GetValuesWithPrefix("", results).Count;

            Assert.AreEqual(3, count);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, results);
        }

        [Test]
        public void GetValuesWithPrefixRespectsMaxResults()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "application", 2 },
                { "apply", 3 },
                { "approach", 4 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();

            int count = trie.GetValuesWithPrefix("app", results, maxResults: 2).Count;

            Assert.AreEqual(2, count);
            Assert.AreEqual(2, results.Count);
        }

        [Test]
        public void GetValuesWithPrefixClearsResultsList()
        {
            Dictionary<string, int> dict = new() { { "apple", 1 }, { "banana", 2 } };
            Trie<int> trie = new(dict);
            List<int> results = new() { 100, 200 };

            trie.GetValuesWithPrefix("app", results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(1, results[0]);
        }

        [Test]
        public void GetValuesWithPrefixHandlesExactMatch()
        {
            Dictionary<string, int> dict = new()
            {
                { "test", 1 },
                { "testing", 2 },
                { "tester", 3 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();

            trie.GetValuesWithPrefix("test", results);

            Assert.AreEqual(3, results.Count);
            CollectionAssert.Contains(results, 1);
        }

        [Test]
        public void GenericTrieHandlesComplexTypes()
        {
            Dictionary<string, List<int>> dict = new()
            {
                {
                    "key1",
                    new List<int> { 1, 2, 3 }
                },
                {
                    "key2",
                    new List<int> { 4, 5, 6 }
                },
            };
            Trie<List<int>> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("key1", out List<int> value1));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, value1);
            Assert.IsTrue(trie.TryGetValue("key2", out List<int> value2));
            CollectionAssert.AreEqual(new[] { 4, 5, 6 }, value2);
        }

        [Test]
        public void GenericTrieHandlesNullableValues()
        {
            Dictionary<string, int?> dict = new() { { "null", null }, { "value", 42 } };
            Trie<int?> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("null", out int? value1));
            Assert.IsTrue(value1 == null, "Value should be null for key storing null");
            Assert.IsTrue(trie.TryGetValue("value", out int? value2));
            Assert.AreEqual(42, value2);
        }

        [Test]
        public void GenericTrieHandlesReferenceTypes()
        {
            Dictionary<string, string> dict = new()
            {
                { "key1", "value1" },
                { "key2", "value2" },
                { "key3", null },
            };
            Trie<string> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("key1", out string value1));
            Assert.AreEqual("value1", value1);
            Assert.IsTrue(trie.TryGetValue("key3", out string value3));
            Assert.IsTrue(value3 == null, "Value should be null for key storing null string");
        }

        [Test]
        public void GenericTrieHandlesWordsWithCommonPrefixes()
        {
            Dictionary<string, int> dict = new()
            {
                { "car", 1 },
                { "card", 2 },
                { "care", 3 },
                { "careful", 4 },
                { "carefully", 5 },
            };
            Trie<int> trie = new(dict);

            foreach (KeyValuePair<string, int> kvp in dict)
            {
                Assert.IsTrue(trie.TryGetValue(kvp.Key, out int value));
                Assert.AreEqual(kvp.Value, value);
            }

            List<int> results = new();
            trie.GetValuesWithPrefix("car", results);
            Assert.AreEqual(5, results.Count);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5 }, results);
        }

        [Test]
        public void GenericTrieHandlesSingleCharacterKeys()
        {
            Dictionary<string, int> dict = new()
            {
                { "a", 1 },
                { "b", 2 },
                { "c", 3 },
            };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("a", out int value1));
            Assert.AreEqual(1, value1);
            Assert.IsTrue(trie.TryGetValue("b", out int value2));
            Assert.AreEqual(2, value2);
            Assert.IsTrue(trie.TryGetValue("c", out int value3));
            Assert.AreEqual(3, value3);
            Assert.IsFalse(trie.TryGetValue("d", out _));
        }

        [Test]
        public void GenericTrieHandlesUnicodeCharacters()
        {
            Dictionary<string, string> dict = new()
            {
                { "世界", "world" },
                { "世", "generation" },
                { "世界和平", "world peace" },
            };
            Trie<string> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("世界", out string value1));
            Assert.AreEqual("world", value1);
            Assert.IsTrue(trie.TryGetValue("世", out string value2));
            Assert.AreEqual("generation", value2);
        }

        [Test]
        public void GenericTrieHandlesVeryLongKeys()
        {
            string longKey = new('a', 1000);
            Dictionary<string, int> dict = new() { { longKey, 42 } };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue(longKey, out int value));
            Assert.AreEqual(42, value);
        }

        [Test]
        public void GenericTrieHandlesManyEntries()
        {
            Dictionary<string, int> dict = Enumerable
                .Range(0, 1000)
                .ToDictionary(i => $"key{i}", i => i);
            Trie<int> trie = new(dict);

            foreach (KeyValuePair<string, int> kvp in dict)
            {
                Assert.IsTrue(trie.TryGetValue(kvp.Key, out int value));
                Assert.AreEqual(kvp.Value, value);
            }
        }

        [Test]
        public void TryGetValueReturnsFalseForSuperstring()
        {
            Dictionary<string, int> dict = new() { { "test", 42 } };
            Trie<int> trie = new(dict);
            Assert.IsFalse(trie.TryGetValue("testing", out _));
            Assert.IsFalse(trie.TryGetValue("test1", out _));
        }

        [Test]
        public void TryGetValueWithNullKeyThrows()
        {
            Dictionary<string, int> dict = new() { { "test", 42 } };
            Trie<int> trie = new(dict);
            Assert.Throws<NullReferenceException>(() => trie.TryGetValue(null, out _));
        }

        [Test]
        public void GetValuesWithPrefixWithNullPrefixThrows()
        {
            Dictionary<string, int> dict = new() { { "test", 42 } };
            Trie<int> trie = new(dict);
            List<int> results = new();
            Assert.Throws<NullReferenceException>(() => trie.GetValuesWithPrefix(null, results));
        }

        [Test]
        public void GetValuesWithPrefixWithNullListThrows()
        {
            Dictionary<string, int> dict = new() { { "test", 42 } };
            Trie<int> trie = new(dict);
            Assert.Throws<NullReferenceException>(() => trie.GetValuesWithPrefix("test", null));
        }

        [Test]
        public void GetValuesWithPrefixWithZeroMaxResults()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "application", 2 },
                { "apply", 3 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("app", results, maxResults: 0).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetValuesWithPrefixWithNegativeMaxResults()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "application", 2 },
                { "apply", 3 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("app", results, maxResults: -1).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetValuesWithPrefixWithOneMaxResult()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "application", 2 },
                { "apply", 3 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("app", results, maxResults: 1).Count;

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void GenericTrieHandlesKeysWhereOneIsCompleteSubstringOfAnother()
        {
            Dictionary<string, int> dict = new()
            {
                { "a", 1 },
                { "ab", 2 },
                { "abc", 3 },
                { "abcd", 4 },
                { "abcde", 5 },
            };
            Trie<int> trie = new(dict);

            foreach (KeyValuePair<string, int> kvp in dict)
            {
                Assert.IsTrue(trie.TryGetValue(kvp.Key, out int value));
                Assert.AreEqual(kvp.Value, value);
            }

            Assert.IsFalse(trie.TryGetValue("abcdef", out _));
            Assert.IsFalse(trie.TryGetValue("", out _));
        }

        [Test]
        public void GenericTrieHandlesKeysThatShareNoCommonPrefix()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "banana", 2 },
                { "cherry", 3 },
                { "date", 4 },
                { "elderberry", 5 },
            };
            Trie<int> trie = new(dict);

            foreach (KeyValuePair<string, int> kvp in dict)
            {
                Assert.IsTrue(trie.TryGetValue(kvp.Key, out int value));
                Assert.AreEqual(kvp.Value, value);
            }
        }

        [Test]
        public void GenericTrieHandlesBranchingAtEachLevel()
        {
            Dictionary<string, int> dict = new()
            {
                { "a", 1 },
                { "b", 2 },
                { "aa", 3 },
                { "ab", 4 },
                { "ba", 5 },
                { "bb", 6 },
            };
            Trie<int> trie = new(dict);

            foreach (KeyValuePair<string, int> kvp in dict)
            {
                Assert.IsTrue(trie.TryGetValue(kvp.Key, out int value));
                Assert.AreEqual(kvp.Value, value);
            }

            Assert.IsFalse(trie.TryGetValue("baa", out _));
            Assert.IsFalse(trie.TryGetValue("c", out _));
        }

        [Test]
        public void GetValuesWithPrefixWithSingleCharacterPrefix()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "application", 2 },
                { "banana", 3 },
                { "apply", 4 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("a", results).Count;

            Assert.AreEqual(3, count);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 4 }, results);
        }

        [Test]
        public void GetValuesWithPrefixHandlesDeepNesting()
        {
            Dictionary<string, int> dict = new()
            {
                { "a", 1 },
                { "ab", 2 },
                { "abc", 3 },
                { "abcd", 4 },
                { "abcde", 5 },
                { "abcdef", 6 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();

            trie.GetValuesWithPrefix("abc", results);
            Assert.AreEqual(4, results.Count);
            CollectionAssert.AreEquivalent(new[] { 3, 4, 5, 6 }, results);
        }

        [Test]
        public void TryGetValueHandlesRepeatingCharacters()
        {
            Dictionary<string, int> dict = new()
            {
                { "aaa", 1 },
                { "aaaa", 2 },
                { "aaaaa", 3 },
            };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("aaa", out int value1));
            Assert.AreEqual(1, value1);
            Assert.IsTrue(trie.TryGetValue("aaaa", out int value2));
            Assert.AreEqual(2, value2);
            Assert.IsTrue(trie.TryGetValue("aaaaa", out int value3));
            Assert.AreEqual(3, value3);
            Assert.IsFalse(trie.TryGetValue("aa", out _));
            Assert.IsFalse(trie.TryGetValue("aaaaaa", out _));
        }

        [Test]
        public void GenericTrieHandlesAllSameCharacterKeys()
        {
            Dictionary<string, int> dict = new()
            {
                { "a", 1 },
                { "aa", 2 },
                { "aaa", 3 },
                { "aaaa", 4 },
            };
            Trie<int> trie = new(dict);

            foreach (KeyValuePair<string, int> kvp in dict)
            {
                Assert.IsTrue(trie.TryGetValue(kvp.Key, out int value));
                Assert.AreEqual(kvp.Value, value);
            }

            List<int> results = new();
            trie.GetValuesWithPrefix("aa", results);
            Assert.AreEqual(3, results.Count);
            CollectionAssert.AreEquivalent(new[] { 2, 3, 4 }, results);
        }

        [Test]
        public void GetValuesWithPrefixWhenPrefixIsNotAKey()
        {
            Dictionary<string, int> dict = new() { { "testing", 1 }, { "tester", 2 } };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("test", results).Count;

            Assert.AreEqual(2, count);
            Assert.IsFalse(trie.TryGetValue("test", out _));
        }

        [Test]
        public void GenericTrieHandlesWhitespaceInKeys()
        {
            Dictionary<string, int> dict = new()
            {
                { "hello world", 1 },
                { "hello  world", 2 },
                { "hello\tworld", 3 },
                { "hello\nworld", 4 },
            };
            Trie<int> trie = new(dict);

            foreach (KeyValuePair<string, int> kvp in dict)
            {
                Assert.IsTrue(trie.TryGetValue(kvp.Key, out int value));
                Assert.AreEqual(kvp.Value, value);
            }

            Assert.IsFalse(trie.TryGetValue("hello", out _));
            Assert.IsFalse(trie.TryGetValue("world", out _));
        }

        [Test]
        public void GenericTrieHandlesNumericKeys()
        {
            Dictionary<string, string> dict = new()
            {
                { "123", "a" },
                { "1234", "b" },
                { "12345", "c" },
                { "234", "d" },
                { "345", "e" },
            };
            Trie<string> trie = new(dict);

            foreach (KeyValuePair<string, string> kvp in dict)
            {
                Assert.IsTrue(trie.TryGetValue(kvp.Key, out string value));
                Assert.AreEqual(kvp.Value, value);
            }

            List<string> results = new();
            trie.GetValuesWithPrefix("123", results);
            Assert.AreEqual(3, results.Count);
            CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, results);
        }

        [Test]
        public void GetValuesWithPrefixReturnsCorrectCountWhenMaxResultsExceedsAvailable()
        {
            Dictionary<string, int> dict = new() { { "apple", 1 }, { "application", 2 } };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("app", results, maxResults: 100).Count;

            Assert.AreEqual(2, count);
            Assert.AreEqual(2, results.Count);
        }

        [Test]
        public void TryGetValueMultipleCallsAreIndependent()
        {
            Dictionary<string, int> dict = new() { { "test", 1 }, { "testing", 2 } };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("test", out int value1));
            Assert.AreEqual(1, value1);
            Assert.IsTrue(trie.TryGetValue("testing", out int value2));
            Assert.AreEqual(2, value2);
            Assert.IsFalse(trie.TryGetValue("tested", out _));
            Assert.IsTrue(trie.TryGetValue("test", out int value3));
            Assert.AreEqual(1, value3);
        }

        [Test]
        public void GetValuesWithPrefixMultipleCallsAreIndependent()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "application", 2 },
                { "apply", 3 },
                { "banana", 4 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();

            trie.GetValuesWithPrefix("app", results);
            Assert.AreEqual(3, results.Count);

            trie.GetValuesWithPrefix("ban", results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(4, results[0]);

            trie.GetValuesWithPrefix("app", results);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void GenericTrieHandlesKeysWithOnlySpecialCharacters()
        {
            Dictionary<string, int> dict = new()
            {
                { "!!!", 1 },
                { "@@@", 2 },
                { "###", 3 },
                { "$$$", 4 },
            };
            Trie<int> trie = new(dict);

            foreach (KeyValuePair<string, int> kvp in dict)
            {
                Assert.IsTrue(trie.TryGetValue(kvp.Key, out int value));
                Assert.AreEqual(kvp.Value, value);
            }
        }

        [Test]
        public void GetValuesWithPrefixWithSpecialCharacterPrefix()
        {
            Dictionary<string, int> dict = new()
            {
                { "!test", 1 },
                { "!testing", 2 },
                { "!tester", 3 },
                { "#test", 4 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("!", results).Count;

            Assert.AreEqual(3, count);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, results);
        }

        [Test]
        public void GenericTrieHandlesEmojiKeys()
        {
            Dictionary<string, string> dict = new()
            {
                { "😀", "happy" },
                { "😀😀", "very happy" },
                { "😁", "grin" },
                { "test😀", "test happy" },
            };
            Trie<string> trie = new(dict);

            foreach (KeyValuePair<string, string> kvp in dict)
            {
                Assert.IsTrue(trie.TryGetValue(kvp.Key, out string value));
                Assert.AreEqual(kvp.Value, value);
            }
        }

        [Test]
        public void GetValuesWithPrefixHandlesLargeMaxResults()
        {
            Dictionary<string, int> dict = new() { { "apple", 1 }, { "application", 2 } };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("app", results, maxResults: int.MaxValue).Count;

            Assert.AreEqual(2, count);
            Assert.AreEqual(2, results.Count);
        }

        [Test]
        public void GenericTrieHandlesKeysStartingWithSameCharacterButDifferentSecondChar()
        {
            Dictionary<string, int> dict = new()
            {
                { "aa", 1 },
                { "ab", 2 },
                { "ac", 3 },
                { "ad", 4 },
                { "ae", 5 },
            };
            Trie<int> trie = new(dict);

            foreach (KeyValuePair<string, int> kvp in dict)
            {
                Assert.IsTrue(trie.TryGetValue(kvp.Key, out int value));
                Assert.AreEqual(kvp.Value, value);
            }

            List<int> results = new();
            trie.GetValuesWithPrefix("a", results);
            Assert.AreEqual(5, results.Count);
        }

        [Test]
        public void TryGetValueAfterConstructingWithEmptyDictionary()
        {
            Trie<int> trie = new(new Dictionary<string, int>());
            Assert.IsFalse(trie.TryGetValue("anything", out _));
            Assert.IsFalse(trie.TryGetValue("", out _));
            Assert.IsFalse(trie.TryGetValue("a", out _));
        }

        [Test]
        public void GetValuesWithPrefixOnEmptyTrie()
        {
            Trie<int> trie = new(new Dictionary<string, int>());
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("test", results).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetValuesWithPrefixEmptyPrefixOnEmptyTrie()
        {
            Trie<int> trie = new(new Dictionary<string, int>());
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("", results).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GenericTrieHandlesOnlyEmptyStringKey()
        {
            Dictionary<string, int> dict = new() { { "", 42 } };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("", out int value));
            Assert.AreEqual(42, value);
            Assert.IsFalse(trie.TryGetValue("a", out _));
        }

        [Test]
        public void GetValuesWithPrefixReturnsEmptyStringValueWhenPresent()
        {
            Dictionary<string, int> dict = new() { { "", 0 }, { "test", 42 } };
            Trie<int> trie = new(dict);
            List<int> results = new();
            trie.GetValuesWithPrefix("", results);

            Assert.AreEqual(2, results.Count);
            CollectionAssert.Contains(results, 0);
            CollectionAssert.Contains(results, 42);
        }

        [Test]
        public void TryGetValueDistinguishesBetweenSimilarKeys()
        {
            Dictionary<string, int> dict = new()
            {
                { "car", 1 },
                { "cart", 2 },
                { "card", 3 },
                { "care", 4 },
            };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("car", out int value1));
            Assert.AreEqual(1, value1);
            Assert.IsTrue(trie.TryGetValue("cart", out int value2));
            Assert.AreEqual(2, value2);
            Assert.IsTrue(trie.TryGetValue("card", out int value3));
            Assert.AreEqual(3, value3);
            Assert.IsTrue(trie.TryGetValue("care", out int value4));
            Assert.AreEqual(4, value4);
            Assert.IsFalse(trie.TryGetValue("ca", out _));
            Assert.IsFalse(trie.TryGetValue("cars", out _));
            Assert.IsFalse(trie.TryGetValue("carts", out _));
        }

        [Test]
        public void GenericTrieHandlesControlCharactersInKeys()
        {
            Dictionary<string, int> dict = new()
            {
                { "test\r\nline", 1 },
                { "test\r", 2 },
                { "test\n", 3 },
                { "test\t", 4 },
            };
            Trie<int> trie = new(dict);

            foreach (KeyValuePair<string, int> kvp in dict)
            {
                Assert.IsTrue(trie.TryGetValue(kvp.Key, out int value));
                Assert.AreEqual(kvp.Value, value);
            }
        }

        [Test]
        public void GetValuesWithPrefixStopsAtMaxResultsInMiddleOfTraversal()
        {
            Dictionary<string, int> dict = new()
            {
                { "a", 1 },
                { "aa", 2 },
                { "aaa", 3 },
                { "aaaa", 4 },
                { "aaaaa", 5 },
                { "aaaaaa", 6 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("a", results, maxResults: 3).Count;

            Assert.AreEqual(3, count);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void GenericTrieHandlesKeysWithLeadingAndTrailingSpaces()
        {
            Dictionary<string, int> dict = new()
            {
                { " test", 1 },
                { "test ", 2 },
                { " test ", 3 },
            };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue(" test", out int value1));
            Assert.AreEqual(1, value1);
            Assert.IsTrue(trie.TryGetValue("test ", out int value2));
            Assert.AreEqual(2, value2);
            Assert.IsTrue(trie.TryGetValue(" test ", out int value3));
            Assert.AreEqual(3, value3);
            Assert.IsFalse(trie.TryGetValue("test", out _));
        }

        [Test]
        public void GetValuesWithPrefixWithPrefixMatchingCompleteKey()
        {
            Dictionary<string, int> dict = new() { { "test", 42 } };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("test", results).Count;

            Assert.AreEqual(1, count);
            Assert.AreEqual(42, results[0]);
        }

        [Test]
        public void GenericTrieHandlesIdenticalKeysInDifferentCase()
        {
            Dictionary<string, int> dict = new()
            {
                { "test", 1 },
                { "Test", 2 },
                { "TEST", 3 },
                { "TeSt", 4 },
            };
            Trie<int> trie = new(dict);

            foreach (KeyValuePair<string, int> kvp in dict)
            {
                Assert.IsTrue(trie.TryGetValue(kvp.Key, out int value));
                Assert.AreEqual(kvp.Value, value);
            }

            List<int> results = new();
            trie.GetValuesWithPrefix("T", results);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void GenericTrieHandlesDefaultValues()
        {
            Dictionary<string, int> dict = new() { { "zero", 0 }, { "one", 1 } };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("zero", out int value));
            Assert.AreEqual(0, value);
        }

        [Test]
        public void GenericTrieHandlesStructValues()
        {
            Dictionary<string, DateTime> dict = new()
            {
                { "date1", new DateTime(2024, 1, 1) },
                { "date2", new DateTime(2024, 12, 31) },
            };
            Trie<DateTime> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("date1", out DateTime value1));
            Assert.AreEqual(new DateTime(2024, 1, 1), value1);
            Assert.IsTrue(trie.TryGetValue("date2", out DateTime value2));
            Assert.AreEqual(new DateTime(2024, 12, 31), value2);
        }

        [Test]
        public void GetValuesWithPrefixLongPrefixThatDoesNotExist()
        {
            Dictionary<string, int> dict = new() { { "test", 42 } };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("testingextra", results).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GenericTriePreservesInsertionValueForDuplicateKeys()
        {
            Dictionary<string, int> dict = new() { { "test", 42 } };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("test", out int value));
            Assert.AreEqual(42, value);

            List<int> results = new();
            trie.GetValuesWithPrefix("test", results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(42, results[0]);
        }

        [Test]
        public void GenericEnumeratorIteratesAllValues()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "banana", 2 },
                { "cherry", 3 },
            };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorWorksWithEmptyTrie()
        {
            Trie<string> trie = new(new Dictionary<string, string>());

            int count = 0;
            foreach (string value in trie)
            {
                count++;
            }

            Assert.AreEqual(0, count);
        }

        [Test]
        public void GenericEnumeratorHandlesSingleValue()
        {
            Dictionary<string, string> dict = new() { { "test", "value" } };
            Trie<string> trie = new(dict);

            List<string> enumerated = new();
            foreach (string value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(1, enumerated.Count);
            Assert.AreEqual("value", enumerated[0]);
        }

        [Test]
        public void GenericEnumeratorHandlesKeysWithCommonPrefixes()
        {
            Dictionary<string, int> dict = new()
            {
                { "test", 1 },
                { "testing", 2 },
                { "tester", 3 },
                { "tea", 4 },
            };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(4, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorCanBeUsedMultipleTimes()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "banana", 2 },
                { "cherry", 3 },
            };
            Trie<int> trie = new(dict);

            List<int> firstEnumeration = new();
            foreach (int value in trie)
            {
                firstEnumeration.Add(value);
            }

            List<int> secondEnumeration = new();
            foreach (int value in trie)
            {
                secondEnumeration.Add(value);
            }

            Assert.AreEqual(3, firstEnumeration.Count);
            Assert.AreEqual(3, secondEnumeration.Count);
            CollectionAssert.AreEquivalent(firstEnumeration, secondEnumeration);
        }

        [Test]
        public void GenericEnumeratorHandlesComplexTypes()
        {
            Dictionary<string, (int, string)> dict = new()
            {
                { "one", (1, "first") },
                { "two", (2, "second") },
                { "three", (3, "third") },
            };
            Trie<(int, string)> trie = new(dict);

            List<(int, string)> enumerated = new();
            foreach ((int, string) value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorHandlesNullValues()
        {
            Dictionary<string, string> dict = new() { { "null", null }, { "notNull", "value" } };
            Trie<string> trie = new(dict);

            List<string> enumerated = new();
            foreach (string value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(2, enumerated.Count);
            Assert.IsTrue(enumerated.Contains(null));
            Assert.IsTrue(enumerated.Contains("value"));
        }

        [Test]
        public void GenericEnumeratorHandlesReferenceTypes()
        {
            List<string> list1 = new() { "a", "b" };
            List<string> list2 = new() { "c", "d" };
            Dictionary<string, List<string>> dict = new() { { "key1", list1 }, { "key2", list2 } };
            Trie<List<string>> trie = new(dict);

            List<List<string>> enumerated = new();
            foreach (List<string> value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(2, enumerated.Count);
            Assert.IsTrue(enumerated.Contains(list1));
            Assert.IsTrue(enumerated.Contains(list2));
        }

        [Test]
        public void GenericEnumeratorUsesValueTypeForEfficiency()
        {
            Dictionary<string, int> dict = new() { { "test", 42 } };
            Trie<int> trie = new(dict);

            Trie<int>.Enumerator enumerator = trie.GetEnumerator();
            Assert.IsTrue(enumerator.GetType().IsValueType);
        }

        [Test]
        public void GenericEnumeratorCanBreakEarly()
        {
            Dictionary<string, int> dict = new()
            {
                { "a", 1 },
                { "b", 2 },
                { "c", 3 },
                { "d", 4 },
                { "e", 5 },
            };
            Trie<int> trie = new(dict);

            int count = 0;
            foreach (int value in trie)
            {
                count++;
                if (count == 3)
                {
                    break;
                }
            }

            Assert.AreEqual(3, count);
        }

        [Test]
        public void GenericEnumeratorWorksWithLinq()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 10 },
                { "banana", 20 },
                { "cherry", 30 },
                { "date", 40 },
            };
            Trie<int> trie = new(dict);

            List<int> filtered = trie.Where(v => 15 < v).ToList();

            Assert.AreEqual(3, filtered.Count);
            Assert.IsTrue(filtered.Contains(20));
            Assert.IsTrue(filtered.Contains(30));
            Assert.IsTrue(filtered.Contains(40));
        }

        [Test]
        public void GenericEnumeratorHandlesDefaultValues()
        {
            Dictionary<string, int> dict = new()
            {
                { "zero", 0 },
                { "one", 1 },
                { "minusOne", -1 },
            };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorHandlesStructValues()
        {
            Dictionary<string, DateTime> dict = new()
            {
                { "date1", new DateTime(2024, 1, 1) },
                { "date2", new DateTime(2024, 6, 15) },
                { "date3", new DateTime(2024, 12, 31) },
            };
            Trie<DateTime> trie = new(dict);

            List<DateTime> enumerated = new();
            foreach (DateTime value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorHandlesManyEntries()
        {
            Dictionary<string, int> dict = new();
            for (int i = 0; i < 1000; i++)
            {
                dict[$"key{i}"] = i;
            }
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(1000, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorHandlesUnicodeKeys()
        {
            Dictionary<string, int> dict = new()
            {
                { "café", 1 },
                { "naïve", 2 },
                { "résumé", 3 },
            };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorHandlesEmptyStringKey()
        {
            Dictionary<string, int> dict = new()
            {
                { "", 0 },
                { "a", 1 },
                { "ab", 2 },
            };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorHandlesOnlyEmptyStringKey()
        {
            Dictionary<string, int> dict = new() { { "", 42 } };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(1, enumerated.Count);
            Assert.AreEqual(42, enumerated[0]);
        }

        [Test]
        public void GenericEnumeratorHandlesKeysAsCompleteSubsets()
        {
            Dictionary<string, int> dict = new()
            {
                { "a", 1 },
                { "aa", 2 },
                { "aaa", 3 },
                { "aaaa", 4 },
            };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(4, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericMultipleEnumeratorsCanRunSimultaneously()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "banana", 2 },
                { "cherry", 3 },
                { "date", 4 },
            };
            Trie<int> trie = new(dict);

            Trie<int>.Enumerator enumerator1 = trie.GetEnumerator();
            Trie<int>.Enumerator enumerator2 = trie.GetEnumerator();

            List<int> list1 = new();
            List<int> list2 = new();

            Assert.IsTrue(enumerator1.MoveNext());
            list1.Add(enumerator1.Current);

            Assert.IsTrue(enumerator2.MoveNext());
            list2.Add(enumerator2.Current);

            Assert.IsTrue(enumerator1.MoveNext());
            list1.Add(enumerator1.Current);

            while (enumerator1.MoveNext())
            {
                list1.Add(enumerator1.Current);
            }

            while (enumerator2.MoveNext())
            {
                list2.Add(enumerator2.Current);
            }

            Assert.AreEqual(4, list1.Count);
            Assert.AreEqual(4, list2.Count);
            CollectionAssert.AreEquivalent(dict.Values, list1);
            CollectionAssert.AreEquivalent(dict.Values, list2);
        }

        [Test]
        public void GenericEnumeratorHandlesNestedPrefixesWithEmptyStringKey()
        {
            Dictionary<string, int> dict = new()
            {
                { "", 0 },
                { "a", 1 },
                { "ab", 2 },
                { "abc", 3 },
                { "abcd", 4 },
            };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(5, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorHandlesDeeplyNestedSingleBranch()
        {
            Dictionary<string, int> dict = new();
            for (int i = 1; i <= 100; i++)
            {
                dict[new string('a', i)] = i;
            }
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(100, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorHandlesWideBranching()
        {
            Dictionary<string, int> dict = new();
            for (char c = 'a'; c <= 'z'; c++)
            {
                dict[c.ToString()] = c - 'a';
            }
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(26, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorWithIEnumerableInterface()
        {
            Dictionary<string, int> dict = new()
            {
                { "test1", 1 },
                { "test2", 2 },
                { "test3", 3 },
            };
            Trie<int> trie = new(dict);

            System.Collections.IEnumerable enumerable = trie;
            List<int> enumerated = new();

            foreach (object value in enumerable)
            {
                enumerated.Add((int)value);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorHandlesWhitespaceOnlyKeys()
        {
            Dictionary<string, int> dict = new()
            {
                { " ", 1 },
                { "  ", 2 },
                { "   ", 3 },
                { "\t", 4 },
                { "\n", 5 },
            };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(5, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GetValuesWithPrefixHandlesMaxResultsZero()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "application", 2 },
                { "apply", 3 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("app", results, maxResults: 0).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetValuesWithPrefixHandlesMaxResultsNegative()
        {
            Dictionary<string, int> dict = new()
            {
                { "apple", 1 },
                { "application", 2 },
                { "apply", 3 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("app", results, maxResults: -1).Count;

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetValuesWithPrefixIncludesEmptyStringKeyWhenPrefixIsEmpty()
        {
            Dictionary<string, int> dict = new()
            {
                { "", 0 },
                { "a", 1 },
                { "b", 2 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();
            trie.GetValuesWithPrefix("", results);

            Assert.AreEqual(3, results.Count);
            CollectionAssert.AreEquivalent(dict.Values, results);
            Assert.IsTrue(results.Contains(0));
        }

        [Test]
        public void GetValuesWithPrefixWithEmptyStringAsPrefix()
        {
            Dictionary<string, int> dict = new() { { "", 0 }, { "test", 1 } };
            Trie<int> trie = new(dict);
            List<int> results = new();
            trie.GetValuesWithPrefix("", results);

            Assert.AreEqual(2, results.Count);
            CollectionAssert.Contains(results, 0);
            CollectionAssert.Contains(results, 1);
        }

        [Test]
        public void GenericEnumeratorHandlesAlternatingBranches()
        {
            Dictionary<string, int> dict = new()
            {
                { "a", 1 },
                { "b", 2 },
                { "aa", 3 },
                { "bb", 4 },
                { "aaa", 5 },
                { "bbb", 6 },
            };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(6, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorHandlesMixedLengthKeys()
        {
            Dictionary<string, int> dict = new()
            {
                { "x", 1 },
                { "xx", 2 },
                { "xxx", 3 },
                { "y", 4 },
                { "yy", 5 },
                { "z", 6 },
            };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(6, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void TryGetValueHandlesNullCharacterInKey()
        {
            Dictionary<string, int> dict = new() { { "test\0ing", 1 }, { "normal", 2 } };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("test\0ing", out int value1));
            Assert.AreEqual(1, value1);
            Assert.IsTrue(trie.TryGetValue("normal", out int value2));
            Assert.AreEqual(2, value2);
            Assert.IsFalse(trie.TryGetValue("testing", out _));
        }

        [Test]
        public void GenericEnumeratorHandlesNullCharacterInKeys()
        {
            Dictionary<string, int> dict = new()
            {
                { "abc\0def", 1 },
                { "test\0", 2 },
                { "\0start", 3 },
            };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GetValuesWithPrefixHandlesPrefixWithNullCharacter()
        {
            Dictionary<string, int> dict = new()
            {
                { "test\0a", 1 },
                { "test\0b", 2 },
                { "other", 3 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();
            trie.GetValuesWithPrefix("test\0", results);

            Assert.AreEqual(2, results.Count);
            CollectionAssert.Contains(results, 1);
            CollectionAssert.Contains(results, 2);
        }

        [Test]
        public void GenericEnumeratorHandlesComplexUnicodeMixedWithAscii()
        {
            Dictionary<string, int> dict = new()
            {
                { "test", 1 },
                { "测试", 2 },
                { "test测试", 3 },
                { "🎉emoji", 4 },
                { "mixed🎉test", 5 },
            };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(5, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorHandlesSurrogatePairs()
        {
            Dictionary<string, int> dict = new()
            {
                { "𝕳𝖊𝖑𝖑𝖔", 1 },
                { "𝓦𝓸𝓻𝓵𝓭", 2 },
                { "🚀🌟", 3 },
            };
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GetValuesWithPrefixStressTestWithManyResults()
        {
            Dictionary<string, int> dict = new();
            for (int i = 0; i < 10000; i++)
            {
                dict[$"prefix{i}"] = i;
            }
            Trie<int> trie = new(dict);
            List<int> results = new();
            trie.GetValuesWithPrefix("prefix", results);

            Assert.AreEqual(10000, results.Count);
        }

        [Test]
        public void GenericEnumeratorStressTestWithDeeplyNestedAndWideBranching()
        {
            Dictionary<string, int> dict = new();
            int counter = 0;

            for (int i = 0; i < 100; i++)
            {
                dict[new string('a', i + 1)] = counter++;
                dict[new string('b', i + 1)] = counter++;
                dict[new string('c', i + 1)] = counter++;
            }
            Trie<int> trie = new(dict);

            List<int> enumerated = new();
            foreach (int value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(300, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GetValuesWithPrefixWithMaxResultsLargerThanAvailable()
        {
            Dictionary<string, int> dict = new() { { "a", 1 }, { "b", 2 } };
            Trie<int> trie = new(dict);
            List<int> results = new();
            int count = trie.GetValuesWithPrefix("", results, maxResults: 1000).Count;

            Assert.AreEqual(2, count);
            Assert.AreEqual(2, results.Count);
        }

        [Test]
        public void GenericEnumeratorCurrentBeforeMoveNextReturnsDefault()
        {
            Dictionary<string, int> dict = new() { { "test", 42 } };
            Trie<int> trie = new(dict);
            Trie<int>.Enumerator enumerator = trie.GetEnumerator();

            Assert.AreEqual(default(int), enumerator.Current);
        }

        [Test]
        public void GenericEnumeratorCurrentAfterLastMoveNextReturnsLastValue()
        {
            Dictionary<string, int> dict = new() { { "single", 42 } };
            Trie<int> trie = new(dict);
            Trie<int>.Enumerator enumerator = trie.GetEnumerator();

            Assert.IsTrue(enumerator.MoveNext());
            int lastValue = enumerator.Current;
            Assert.IsFalse(enumerator.MoveNext());

            Assert.AreEqual(lastValue, enumerator.Current);
        }

        [Test]
        public void TryGetValueWithVeryLongKey()
        {
            string longKey = new('x', 10000);
            Dictionary<string, int> dict = new() { { longKey, 42 } };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue(longKey, out int value));
            Assert.AreEqual(42, value);
            Assert.IsFalse(trie.TryGetValue(longKey + "y", out _));
        }

        [Test]
        public void GetValuesWithPrefixWithVeryLongPrefix()
        {
            string longPrefix = new('x', 5000);
            string longerKey = longPrefix + "extra";
            Dictionary<string, int> dict = new() { { longerKey, 99 } };
            Trie<int> trie = new(dict);
            List<int> results = new();
            trie.GetValuesWithPrefix(longPrefix, results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(99, results[0]);
        }

        [Test]
        public void GenericEnumeratorHandlesLargeValueTypes()
        {
            Dictionary<string, (long, long, long, long)> dict = new()
            {
                { "a", (long.MaxValue, long.MinValue, 0, 12345) },
                { "b", (1, 2, 3, 4) },
                { "c", (-1, -2, -3, -4) },
            };
            Trie<(long, long, long, long)> trie = new(dict);

            List<(long, long, long, long)> enumerated = new();
            foreach ((long, long, long, long) value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GenericEnumeratorHandlesNestedCollections()
        {
            Dictionary<string, List<List<int>>> dict = new()
            {
                {
                    "a",
                    new List<List<int>>
                    {
                        new() { 1, 2 },
                        new() { 3, 4 },
                    }
                },
                {
                    "b",
                    new List<List<int>> { new() { 5 } }
                },
            };
            Trie<List<List<int>>> trie = new(dict);

            List<List<List<int>>> enumerated = new();
            foreach (List<List<int>> value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(2, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void GetValuesWithPrefixHandlesEmptyDictionary()
        {
            Trie<int> trie = new(new Dictionary<string, int>());
            List<int> results = new();
            trie.GetValuesWithPrefix("anything", results);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GenericEnumeratorHandlesBoolValues()
        {
            Dictionary<string, bool> dict = new()
            {
                { "true1", true },
                { "false1", false },
                { "true2", true },
            };
            Trie<bool> trie = new(dict);

            List<bool> enumerated = new();
            foreach (bool value in trie)
            {
                enumerated.Add(value);
            }

            Assert.AreEqual(3, enumerated.Count);
            CollectionAssert.AreEquivalent(dict.Values, enumerated);
        }

        [Test]
        public void TryGetValueHandlesCaseSensitivityForSimilarKeys()
        {
            Dictionary<string, int> dict = new()
            {
                { "test", 1 },
                { "Test", 2 },
                { "TEST", 3 },
            };
            Trie<int> trie = new(dict);

            Assert.IsTrue(trie.TryGetValue("test", out int value1));
            Assert.AreEqual(1, value1);
            Assert.IsTrue(trie.TryGetValue("Test", out int value2));
            Assert.AreEqual(2, value2);
            Assert.IsTrue(trie.TryGetValue("TEST", out int value3));
            Assert.AreEqual(3, value3);
        }

        [Test]
        public void GetValuesWithPrefixHandlesPartialUnicodePrefix()
        {
            Dictionary<string, int> dict = new()
            {
                { "café", 1 },
                { "cafeteria", 2 },
                { "cat", 3 },
            };
            Trie<int> trie = new(dict);
            List<int> results = new();
            trie.GetValuesWithPrefix("caf", results);

            Assert.AreEqual(2, results.Count);
            CollectionAssert.Contains(results, 1);
            CollectionAssert.Contains(results, 2);
        }
    }
}
