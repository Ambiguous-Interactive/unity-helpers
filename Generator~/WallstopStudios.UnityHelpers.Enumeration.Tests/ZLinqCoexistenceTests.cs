// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Enumeration.Tests
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using ZLinq;

    [TestFixture]
    public sealed class ZLinqCoexistenceTests
    {
        [Test]
        public void InterfaceTypedZLinqQueriesRemainUnambiguousWithBothNamespaces()
        {
            IEnumerable<int> source = new[] { 0, 1, 2, 3 };
            int[] actual = source.AsValueEnumerable().Where(value => 0 < value).ToArray();
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, actual);
        }

        [Test]
        public void InterfaceTypedZLinqForeachRetainsElementType()
        {
            IEnumerable<string> source = new List<string> { "first", "second" };
            List<string> actual = new();
            foreach (string value in source.AsValueEnumerable())
            {
                actual.Add(value);
            }
            CollectionAssert.AreEqual(new[] { "first", "second" }, actual);
        }

        [Test]
        public void DictionaryZLinqQueriesKeepKeyAndValueTypes()
        {
            IEnumerable<KeyValuePair<int, string>> source = new Dictionary<int, string>
            {
                { 1, "first" },
                { 2, "second" },
            };
            string[] actual = source
                .AsValueEnumerable()
                .Where(pair => pair.Key == 2)
                .Select(pair => pair.Value)
                .ToArray();
            CollectionAssert.AreEqual(new[] { "second" }, actual);
        }

        [Test]
        public void OriginalStaticFactoryStillSupportsNullAndConcreteSources()
        {
            List<int> actual = new();
            foreach (int value in IEnumerableExtensions.AsValueEnumerable(new[] { 1, 2 }))
            {
                actual.Add(value);
            }
            CollectionAssert.AreEqual(new[] { 1, 2 }, actual);
            foreach (int value in IEnumerableExtensions.AsValueEnumerable<int>(null))
            {
                Assert.Fail($"Unexpected null-source value: {value}");
            }
        }

        [Test]
        public void PrefixedFactoriesCoexistWithZLinqOnTheSameInterfaceSource()
        {
            IEnumerable<int> source = new List<int> { 1, 2, 3 };
            List<int> actual = new();
            foreach (int value in source.WAsValueEnumerable())
            {
                actual.Add(value);
            }
            CollectionAssert.AreEqual(source.AsValueEnumerable().ToArray(), actual);
        }

        [Test]
        public void OriginalStaticDictionaryFactoryMatchesPrefixedExtension()
        {
            IEnumerable<KeyValuePair<int, string>> source = new Dictionary<int, string>
            {
                { 1, "first" },
                { 2, "second" },
            };
            List<KeyValuePair<int, string>> original = new();
            foreach (
                KeyValuePair<int, string> pair in IEnumerableExtensions.AsValueDictionaryEnumerable(
                    source
                )
            )
            {
                original.Add(pair);
            }
            List<KeyValuePair<int, string>> current = new();
            foreach (KeyValuePair<int, string> pair in source.WAsValueDictionaryEnumerable())
            {
                current.Add(pair);
            }
            CollectionAssert.AreEqual(original, current);
            foreach (
                KeyValuePair<int, string> pair in IEnumerableExtensions.AsValueDictionaryEnumerable<
                    int,
                    string
                >(null)
            )
            {
                Assert.Fail($"Unexpected null-source pair: {pair}");
            }
        }
    }
}
