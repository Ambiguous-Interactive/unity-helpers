// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*
    WUH010 is suppressed for this file: its subject is the dictionary extension surface, whose indexer reads are the behaviour being asserted.
    Rewriting those reads through TryGetValue would delete what they assert. Everywhere the
    indexer is incidental, tests read through DictionaryAssertions.ValueFor instead (#653).
*/
#pragma warning disable WUH010

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class DictionaryExtensionTests : CommonTestBase
    {
        [Test]
        public void GetOrAddValueProducer()
        {
            Dictionary<string, int> dictionary = new();
            int value = dictionary.GetOrAdd("test", () => 100);
            Assert.AreEqual(100, value);
            Assert.AreEqual(value, dictionary["test"]);

            int newValue = dictionary.GetOrAdd(
                "test",
                () =>
                {
                    Assert.Fail("Value Producer should not have been called!");
                    return 200;
                }
            );
            Assert.AreEqual(100, newValue);

            newValue = dictionary.GetOrAdd("test2", () => 300);
            Assert.AreEqual(300, newValue);
            Assert.AreEqual(100, dictionary["test"]);
        }

        [Test]
        public void GetOrAddKeyValueProducer()
        {
            Dictionary<string, int> dictionary = new();
            int value = dictionary.GetOrAdd(
                "test",
                key =>
                {
                    Assert.AreEqual("test", key);
                    return 100;
                }
            );
            Assert.AreEqual(100, value);
            Assert.AreEqual(value, dictionary["test"]);

            int newValue = dictionary.GetOrAdd(
                "test",
                key =>
                {
                    Assert.Fail("Value Producer should not have been called!");
                    return 200;
                }
            );
            Assert.AreEqual(100, newValue);

            newValue = dictionary.GetOrAdd(
                "test2",
                key =>
                {
                    Assert.AreEqual("test2", key);
                    return 300;
                }
            );
            Assert.AreEqual(300, newValue);
            Assert.AreEqual(100, dictionary["test"]);
        }

        [Test]
        public void GetOrElseValue()
        {
            Dictionary<string, int> dictionary = new();
            int value = dictionary.GetOrElse("test", 100);
            Assert.AreEqual(100, value);
            Assert.IsFalse(dictionary.ContainsKey("test"));
            dictionary["test"] = 150;
            value = dictionary.GetOrElse("test", 100);
            Assert.AreEqual(150, value);
            Assert.AreEqual(150, dictionary["test"]);
        }

        [Test]
        public void GetOrElseValueProducer()
        {
            Dictionary<string, int> dictionary = new();
            int value = dictionary.GetOrElse("test", () => 100);
            Assert.AreEqual(100, value);
            Assert.IsFalse(dictionary.ContainsKey("test"));
            dictionary["test"] = 150;
            value = dictionary.GetOrElse(
                "test",
                () =>
                {
                    Assert.Fail("Producer should not be called.");
                    return 100;
                }
            );
            Assert.AreEqual(150, value);
            Assert.AreEqual(150, dictionary["test"]);
        }

        [Test]
        public void GetOrElseKeyValueProducer()
        {
            Dictionary<string, int> dictionary = new();
            int value = dictionary.GetOrElse(
                "test",
                key =>
                {
                    Assert.AreEqual("test", key);
                    return 100;
                }
            );
            Assert.AreEqual(100, value);
            Assert.IsFalse(dictionary.ContainsKey("test"));
            dictionary["test"] = 150;
            value = dictionary.GetOrElse(
                "test",
                () =>
                {
                    Assert.Fail("Producer should not be called.");
                    return 100;
                }
            );
            Assert.AreEqual(150, value);
            Assert.AreEqual(150, dictionary["test"]);
        }

        [Test]
        public void GetOrAddNew()
        {
            Dictionary<string, List<int>> dictionary = new();
            List<int> value = dictionary.GetOrAdd("test");
            Assert.IsTrue(value != null);
            Assert.AreEqual(value, dictionary["test"]);
            value.Add(1);

            List<int> newValue = dictionary.GetOrAdd("test");
            Assert.AreEqual(value, newValue);
        }

        [Test]
        public void GetOrElse()
        {
            IReadOnlyDictionary<string, int> dictionary = new Dictionary<string, int>();
            int value = dictionary.GetOrElse("test", 100);
            Assert.AreEqual(100, value);
            Assert.AreEqual(0, dictionary.Count);
        }

        [Test]
        public void AddOrUpdate()
        {
            Dictionary<string, int> dictionary = new();
            int value = dictionary.AddOrUpdate("test", key => 100, (key, existing) => existing + 1);
            Assert.AreEqual(100, value);
            Assert.AreEqual(100, dictionary["test"]);
            int expected = value;
            for (int i = 0; i < 100; ++i)
            {
                value = dictionary.AddOrUpdate("test", key => 100, (key, existing) => existing + 1);
                Assert.AreEqual(++expected, value);
                Assert.AreEqual(value, dictionary["test"]);
            }

            value = dictionary.AddOrUpdate("test2", key => 150, (key, existing) => existing + 1);
            Assert.AreEqual(150, value);
            Assert.AreEqual(150, dictionary["test2"]);
            Assert.AreEqual(expected, dictionary["test"]);
        }

        [Test]
        public void TryAdd()
        {
            Dictionary<string, int> dictionary = new();
            int value = dictionary.TryAdd(
                "test",
                key =>
                {
                    Assert.AreEqual("test", key);
                    return 150;
                }
            );
            Assert.AreEqual(150, value);
            Assert.AreEqual(value, dictionary["test"]);

            value = dictionary.TryAdd(
                "test",
                key =>
                {
                    Assert.Fail("Creator should not have been called.");
                    return 200;
                }
            );
            Assert.AreEqual(150, value);
            Assert.AreEqual(value, dictionary["test"]);

            value = dictionary.TryAdd(
                "test2",
                key =>
                {
                    Assert.AreEqual("test2", key);
                    return 350;
                }
            );
            Assert.AreEqual(350, value);
            Assert.AreEqual(value, dictionary["test2"]);
            Assert.AreEqual(150, dictionary["test"]);
        }

        [Test]
        public void Merge()
        {
            IReadOnlyDictionary<string, int> left = new Dictionary<string, int>()
            {
                ["only-on-left"] = 1,
                ["both"] = 1,
            };

            IReadOnlyDictionary<string, int> right = new Dictionary<string, int>()
            {
                ["only-on-right"] = 3,
                ["both"] = 2,
            };

            Dictionary<string, int> merged = left.Merge(right);
            Assert.AreEqual(3, merged.Count);
            Assert.AreEqual(1, merged["only-on-left"]);
            Assert.AreEqual(2, merged["both"]);
            Assert.AreEqual(3, merged["only-on-right"]);

            merged = right.Merge(left);
            Assert.AreEqual(3, merged.Count);
            Assert.AreEqual(1, merged["only-on-left"]);
            Assert.AreEqual(1, merged["both"]);
            Assert.AreEqual(3, merged["only-on-right"]);

            merged = left.Merge(left);
            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual(1, merged["only-on-left"]);
            Assert.AreEqual(1, merged["both"]);

            merged = right.Merge(right);
            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual(3, merged["only-on-right"]);
            Assert.AreEqual(2, merged["both"]);
        }

        [Test]
        public void Difference()
        {
            IReadOnlyDictionary<string, int> left = new Dictionary<string, int>()
            {
                ["only-on-left"] = 1,
                ["both"] = 1,
            };

            IReadOnlyDictionary<string, int> right = new Dictionary<string, int>()
            {
                ["only-on-right"] = 3,
                ["both"] = 2,
            };

            Dictionary<string, int> difference = left.Difference(right);
            Assert.AreEqual(2, difference.Count);
            Assert.AreEqual(2, difference["both"]);
            Assert.AreEqual(3, difference["only-on-right"]);

            difference = right.Difference(left);
            Assert.AreEqual(2, difference.Count);
            Assert.AreEqual(1, difference["both"]);
            Assert.AreEqual(1, difference["only-on-left"]);

            difference = left.Difference(left);
            Assert.AreEqual(0, difference.Count);

            difference = right.Difference(right);
            Assert.AreEqual(0, difference.Count);
        }

        [Test]
        public void Reverse()
        {
            IReadOnlyDictionary<string, int> initial = new Dictionary<string, int>()
            {
                ["one"] = 1,
                ["one-duplicate"] = 1,
                ["two"] = 2,
                ["three"] = 3,
            };

            Dictionary<int, string> reversed = initial.Reverse();
            Assert.AreEqual(3, reversed.Count);
            Assert.AreEqual("one-duplicate", reversed[1]);
            Assert.AreEqual("two", reversed[2]);
            Assert.AreEqual("three", reversed[3]);
        }

        [Test]
        public void ToDictionaryFromDictionary()
        {
            IReadOnlyDictionary<string, int> initial = new Dictionary<string, int>()
            {
                ["test"] = 1,
                ["test2"] = 2,
            };

            Dictionary<string, int> copy = initial.ToDictionary();
            Assert.AreEqual(2, copy.Count);
            Assert.AreEqual(1, copy["test"]);
            Assert.AreEqual(2, copy["test2"]);
        }

        [Test]
        public void ToDictionaryFromEnumerableKeyValuePair()
        {
            IEnumerable<KeyValuePair<string, int>> initial = new Dictionary<string, int>()
            {
                ["test"] = 1,
                ["test2"] = 2,
            };

            Dictionary<string, int> copy = initial.ToDictionary();
            Assert.AreEqual(2, copy.Count);
            Assert.AreEqual(1, copy["test"]);
            Assert.AreEqual(2, copy["test2"]);
        }

        [Test]
        public void ToDictionaryFromEnumerableValueTuple()
        {
            IEnumerable<(string, int)> initial = new Dictionary<string, int>()
            {
                ["test"] = 1,
                ["test2"] = 2,
            }.Select(kvp => (kvp.Key, kvp.Value));

            Dictionary<string, int> copy = initial.ToDictionary();
            Assert.AreEqual(2, copy.Count);
            Assert.AreEqual(1, copy["test"]);
            Assert.AreEqual(2, copy["test2"]);
        }

        [Test]
        public void ContentEqualsSame()
        {
            IReadOnlyDictionary<string, int> left = new Dictionary<string, int>()
            {
                ["one"] = 1,
                ["two"] = 2,
            };

            IReadOnlyDictionary<string, int> right = new Dictionary<string, int>()
            {
                ["one"] = 1,
                ["two"] = 2,
            };

            Assert.IsTrue(left.ContentEquals(right));
            Assert.IsTrue(left.ContentEquals(left));
            Assert.IsTrue(right.ContentEquals(left));
            Assert.IsTrue(right.ContentEquals(right));
        }

        [Test]
        public void ContentEqualsDifferentCount()
        {
            IReadOnlyDictionary<string, int> left = new Dictionary<string, int>()
            {
                ["one"] = 1,
                ["two"] = 2,
            };

            IReadOnlyDictionary<string, int> right = new Dictionary<string, int>()
            {
                ["one"] = 1,
                ["two"] = 2,
                ["three"] = 3,
            };

            Assert.IsFalse(left.ContentEquals(right));
            Assert.IsTrue(left.ContentEquals(left));
            Assert.IsFalse(right.ContentEquals(left));
            Assert.IsTrue(right.ContentEquals(right));
        }

        [Test]
        public void ContentEqualsDifferentValuesSameSize()
        {
            IReadOnlyDictionary<string, int> left = new Dictionary<string, int>()
            {
                ["one"] = 1,
                ["two"] = 2,
            };

            IReadOnlyDictionary<string, int> right = new Dictionary<string, int>()
            {
                ["one"] = 1,
                ["two"] = 20_000,
            };

            Assert.IsFalse(left.ContentEquals(right));
            Assert.IsTrue(left.ContentEquals(left));
            Assert.IsFalse(right.ContentEquals(left));
            Assert.IsTrue(right.ContentEquals(right));
        }

        [Test]
        public void ContentEqualsDifferentKeysSameSize()
        {
            IReadOnlyDictionary<string, int> left = new Dictionary<string, int>()
            {
                ["one"] = 1,
                ["two"] = 2,
            };

            IReadOnlyDictionary<string, int> right = new Dictionary<string, int>()
            {
                ["one"] = 1,
                ["three"] = 2,
            };

            Assert.IsFalse(left.ContentEquals(right));
            Assert.IsTrue(left.ContentEquals(left));
            Assert.IsFalse(right.ContentEquals(left));
            Assert.IsTrue(right.ContentEquals(right));
        }

        [Test]
        public void Deconstruct()
        {
            new KeyValuePair<string, int>("one", 1).Deconstruct(out string key, out int value);
            Assert.AreEqual("one", key);
            Assert.AreEqual(1, value);
        }

        [Test]
        public void TryRemoveFromDictionary()
        {
            Dictionary<string, int> dictionary = new()
            {
                ["one"] = 1,
                ["two"] = 2,
                ["three"] = 3,
            };

            bool removed = dictionary.TryRemove("two", out int value);
            Assert.IsTrue(removed);
            Assert.AreEqual(2, value);
            Assert.AreEqual(2, dictionary.Count);
            Assert.IsFalse(dictionary.ContainsKey("two"));

            removed = dictionary.TryRemove("nonexistent", out value);
            Assert.IsFalse(removed);
            Assert.AreEqual(default(int), value);
            Assert.AreEqual(2, dictionary.Count);
        }

        [Test]
        public void TryRemoveFromConcurrentDictionary()
        {
            ConcurrentDictionary<string, int> dictionary = new()
            {
                ["one"] = 1,
                ["two"] = 2,
                ["three"] = 3,
            };

            bool removed = dictionary.TryRemove("two", out int value);
            Assert.IsTrue(removed);
            Assert.AreEqual(2, value);
            Assert.AreEqual(2, dictionary.Count);
            Assert.IsFalse(dictionary.ContainsKey("two"));

            removed = dictionary.TryRemove("nonexistent", out value);
            Assert.IsFalse(removed);
            Assert.AreEqual(default(int), value);
            Assert.AreEqual(2, dictionary.Count);
        }

        [Test]
        public void TryRemoveEmptyDictionary()
        {
            Dictionary<string, int> dictionary = new();
            bool removed = dictionary.TryRemove("key", out int value);
            Assert.IsFalse(removed);
            Assert.AreEqual(default(int), value);
        }

        [Test]
        public void ToDictionaryFromReadOnlyDictionaryWithComparer()
        {
            IReadOnlyDictionary<string, int> initial = new Dictionary<string, int>()
            {
                ["Test"] = 1,
                ["test2"] = 2,
            };

            Dictionary<string, int> copy = initial.ToDictionary(StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual(2, copy.Count);
            Assert.AreEqual(1, copy["test"]);
            Assert.AreEqual(2, copy["TEST2"]);
            Assert.IsTrue(copy.ContainsKey("TEST"));
            Assert.IsTrue(copy.ContainsKey("Test2"));
        }

        [Test]
        public void ToDictionaryFromEnumerableKeyValuePairWithComparer()
        {
            IEnumerable<KeyValuePair<string, int>> initial = new Dictionary<string, int>()
            {
                ["Test"] = 1,
                ["test2"] = 2,
            };

            Dictionary<string, int> copy = initial.ToDictionary(StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual(2, copy.Count);
            Assert.AreEqual(1, copy["test"]);
            Assert.AreEqual(2, copy["TEST2"]);
            Assert.IsTrue(copy.ContainsKey("TEST"));
            Assert.IsTrue(copy.ContainsKey("Test2"));
        }

        [Test]
        public void ToDictionaryFromEnumerableValueTupleWithComparer()
        {
            IEnumerable<(string, int)> initial = new Dictionary<string, int>()
            {
                ["Test"] = 1,
                ["test2"] = 2,
            }.Select(kvp => (kvp.Key, kvp.Value));

            Dictionary<string, int> copy = initial.ToDictionary(StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual(2, copy.Count);
            Assert.AreEqual(1, copy["test"]);
            Assert.AreEqual(2, copy["TEST2"]);
            Assert.IsTrue(copy.ContainsKey("TEST"));
            Assert.IsTrue(copy.ContainsKey("Test2"));
        }

        [Test]
        public void ToDictionaryComparerPreservesEqualityBehavior()
        {
            IReadOnlyDictionary<string, int> initial = new Dictionary<string, int>()
            {
                ["key1"] = 1,
                ["key2"] = 2,
            };

            Dictionary<string, int> withDefault = initial.ToDictionary();
            Assert.IsFalse(withDefault.ContainsKey("KEY1"));

            Dictionary<string, int> withIgnoreCase = initial.ToDictionary(
                StringComparer.OrdinalIgnoreCase
            );
            Assert.IsTrue(withIgnoreCase.ContainsKey("KEY1"));
            Assert.IsTrue(withIgnoreCase.ContainsKey("KEY2"));
        }

        [Test]
        public void MergeEmptyDictionaries()
        {
            IReadOnlyDictionary<string, int> empty1 = new Dictionary<string, int>();
            IReadOnlyDictionary<string, int> empty2 = new Dictionary<string, int>();

            Dictionary<string, int> merged = empty1.Merge(empty2);
            Assert.AreEqual(0, merged.Count);
        }

        [Test]
        public void MergeWithCustomCreator()
        {
            IReadOnlyDictionary<string, int> left = new Dictionary<string, int>() { ["a"] = 1 };
            IReadOnlyDictionary<string, int> right = new Dictionary<string, int>() { ["b"] = 2 };

            Dictionary<string, int> merged = left.Merge(
                right,
                () => new Dictionary<string, int>(10)
            );
            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual(1, merged["a"]);
            Assert.AreEqual(2, merged["b"]);
        }

        [Test]
        public void DifferenceEmptyDictionaries()
        {
            IReadOnlyDictionary<string, int> empty1 = new Dictionary<string, int>();
            IReadOnlyDictionary<string, int> empty2 = new Dictionary<string, int>();

            Dictionary<string, int> diff = empty1.Difference(empty2);
            Assert.AreEqual(0, diff.Count);
        }

        [Test]
        public void DifferenceWithCustomCreator()
        {
            IReadOnlyDictionary<string, int> left = new Dictionary<string, int>() { ["a"] = 1 };
            IReadOnlyDictionary<string, int> right = new Dictionary<string, int>()
            {
                ["a"] = 2,
                ["b"] = 3,
            };

            Dictionary<string, int> diff = left.Difference(
                right,
                () => new Dictionary<string, int>(10)
            );
            Assert.AreEqual(2, diff.Count);
            Assert.AreEqual(2, diff["a"]);
            Assert.AreEqual(3, diff["b"]);
        }

        [Test]
        public void ReverseEmptyDictionary()
        {
            IReadOnlyDictionary<string, int> empty = new Dictionary<string, int>();
            Dictionary<int, string> reversed = empty.Reverse();
            Assert.AreEqual(0, reversed.Count);
        }

        [Test]
        public void ReverseWithCustomCreator()
        {
            IReadOnlyDictionary<string, int> initial = new Dictionary<string, int>()
            {
                ["one"] = 1,
                ["two"] = 2,
            };

            Dictionary<int, string> reversed = initial.Reverse(() =>
                new Dictionary<int, string>(10)
            );
            Assert.AreEqual(2, reversed.Count);
            Assert.AreEqual("one", reversed[1]);
            Assert.AreEqual("two", reversed[2]);
        }

        [Test]
        public void ContentEqualsEmptyDictionaries()
        {
            IReadOnlyDictionary<string, int> empty1 = new Dictionary<string, int>();
            IReadOnlyDictionary<string, int> empty2 = new Dictionary<string, int>();

            Assert.IsTrue(empty1.ContentEquals(empty2));
        }

        [Test]
        public void ContentEqualsNullDictionaries()
        {
            IReadOnlyDictionary<string, int> dict = new Dictionary<string, int>() { ["a"] = 1 };

            Assert.IsFalse(dict.ContentEquals(null));
            Assert.IsFalse(((IReadOnlyDictionary<string, int>)null).ContentEquals(dict));
            Assert.IsTrue(((IReadOnlyDictionary<string, int>)null).ContentEquals(null));
        }

        [Test]
        public void GetOrAddConcurrentDictionary()
        {
            ConcurrentDictionary<string, int> dict = new();

            int value = dict.GetOrAdd("test", () => 100);
            Assert.AreEqual(100, value);
            Assert.AreEqual(100, dict["test"]);

            int existing = dict.GetOrAdd("test", () => 200);
            Assert.AreEqual(100, existing);
        }

        [Test]
        public void GetOrAddKeyConcurrentDictionary()
        {
            ConcurrentDictionary<string, int> dict = new();

            int value = dict.GetOrAdd("test", key => key.Length);
            Assert.AreEqual(4, value);
            Assert.AreEqual(4, dict["test"]);

            int existing = dict.GetOrAdd("test", key => key.Length * 2);
            Assert.AreEqual(4, existing);
        }

        [Test]
        public void GetOrAddNewConcurrentDictionary()
        {
            ConcurrentDictionary<string, List<int>> dict = new();

            List<int> value = dict.GetOrAdd("test");
            Assert.IsTrue(value != null);
            Assert.AreEqual(0, value.Count);
            value.Add(42);

            List<int> existing = dict.GetOrAdd("test");
            Assert.AreSame(value, existing);
            Assert.AreEqual(1, existing.Count);
            Assert.AreEqual(42, existing[0]);
        }

        [Test]
        public void AddOrUpdateConcurrentDictionary()
        {
            ConcurrentDictionary<string, int> dict = new();

            int value = dict.AddOrUpdate("test", key => 100, (key, existing) => existing + 1);
            Assert.AreEqual(100, value);

            int updated = dict.AddOrUpdate("test", key => 200, (key, existing) => existing + 1);
            Assert.AreEqual(101, updated);
        }

        [Test]
        public void TryAddConcurrentDictionary()
        {
            ConcurrentDictionary<string, int> dict = new();

            int value = dict.TryAdd("test", key => 100);
            Assert.AreEqual(100, value);

            int existing = dict.TryAdd("test", key => 200);
            Assert.AreEqual(100, existing);
        }

        [Test]
        public void ConcurrentDictionaryAddOrUpdateHandlesParallelWrites()
        {
            ConcurrentDictionary<string, int> dictionary = new();
            Parallel.For(
                0,
                1000,
                _ => dictionary.AddOrUpdate("counter", _ => 1, (_, existing) => existing + 1)
            );

            Assert.IsTrue(dictionary.TryGetValue("counter", out int value));
            Assert.AreEqual(1000, value);
        }

        [Test]
        public void ToDictionaryLastWinsOnDuplicateTupleKeys()
        {
            IEnumerable<(string, int)> tuples = new List<(string, int)> { ("dup", 1), ("dup", 2) };

            Dictionary<string, int> result = tuples.ToDictionary();
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(2, result["dup"]);
        }
    }
}
