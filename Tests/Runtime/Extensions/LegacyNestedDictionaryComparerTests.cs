// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class LegacyNestedDictionaryComparerTests
    {
        private static void AssertMemberComparers<TMap>(int count, int operation)
            where TMap : class, IDictionary<string, int>, new()
        {
            TMap entries = new();
            TMap sameEntries = new();
            TMap changedEntries = new();
            for (int index = 0; index < count; ++index)
            {
                string key = index == 0 ? " " : index.ToString(CultureInfo.InvariantCulture);
                entries.Add(key, index);
                sameEntries.Add(key, index);
                changedEntries.Add(key, index);
            }
            changedEntries[" "] = -1;
            LegacyDictionaryMemberContract<TMap> first = new() { Revision = 17, Entries = entries };
            LegacyDictionaryMemberContract<TMap> equivalent = new()
            {
                Revision = 17,
                Entries = sameEntries,
            };
            LegacyDictionaryMemberContract<TMap> different = new()
            {
                Revision = 17,
                Entries = changedEntries,
            };
            switch (operation)
            {
                case 0:
                    Assert.IsTrue(first.ProtoEquals(equivalent));
                    Assert.IsFalse(first.ProtoEquals(different));
                    Assert.IsTrue(ProtoEqualityExtensions.ProtoEquals<object>(first, equivalent));
                    Assert.IsFalse(ProtoEqualityExtensions.ProtoEquals<object>(first, different));
                    break;
                case 1:
                    IEqualityComparer<LegacyDictionaryMemberContract<TMap>> comparer =
                        ProtoEqualityExtensions.GetProtoComparer<
                            LegacyDictionaryMemberContract<TMap>
                        >();
                    Assert.IsTrue(comparer.Equals(first, equivalent));
                    Assert.IsFalse(comparer.Equals(first, different));
                    IEqualityComparer<object> objectComparer =
                        ProtoEqualityExtensions.GetProtoComparer<object>();
                    Assert.IsTrue(objectComparer.Equals(first, equivalent));
                    Assert.IsFalse(objectComparer.Equals(first, different));
                    break;
                default:
                    IEqualityComparer<LegacyDictionaryMemberContract<TMap>> hashComparer =
                        ProtoEqualityExtensions.GetProtoComparer<
                            LegacyDictionaryMemberContract<TMap>
                        >();
                    int hash = hashComparer.GetHashCode(first);
                    Assert.AreEqual(hash, hashComparer.GetHashCode(equivalent));
                    Assert.AreNotEqual(hash, hashComparer.GetHashCode(different));
                    Assert.AreEqual(
                        hash,
                        ProtoEqualityExtensions.GetProtoComparer<object>().GetHashCode(first)
                    );
                    break;
            }
        }

        private static void AssertMemberBytes<TMap>(TMap entries)
            where TMap : class, IDictionary<string, int>, new()
        {
            entries.Add(" ", 7);
            entries.Add("key", 42);
            LegacyDictionaryMemberContract<TMap> message = new()
            {
                Revision = 17,
                Entries = entries,
            };
            using MemoryStream stream = new();
            ProtoBuf.Serializer.Serialize(stream, message);
            CollectionAssert.AreEqual(
                new byte[] { 8, 17, 58, 5, 10, 1, 32, 16, 7, 58, 7, 10, 3, 107, 101, 121, 16, 42 },
                stream.ToArray()
            );
            LegacyDictionaryMemberContract<TMap> nullEntries = new() { Revision = 17 };
            LegacyDictionaryMemberContract<TMap> emptyEntries = new()
            {
                Revision = 17,
                Entries = new TMap(),
            };
            Assert.IsTrue(nullEntries.ProtoEquals(emptyEntries));
            stream.SetLength(0);
            ProtoBuf.Serializer.Serialize(stream, nullEntries);
            CollectionAssert.AreEqual(new byte[] { 8, 17 }, stream.ToArray());
        }

        [Test, Combinatorial]
        public void DictionaryMembersPreserveValuesAcrossComparerEntryPoints(
            [Values(0, 1, 2, 3)] int family,
            [Values(0, 1, 32, 10000)] int count,
            [Values(0, 1, 2)] int operation
        )
        {
            switch (family)
            {
                case 0:
                    AssertMemberComparers<SerializableDictionary<string, int>>(count, operation);
                    break;
                case 1:
                    AssertMemberComparers<SerializableSortedDictionary<string, int>>(
                        count,
                        operation
                    );
                    break;
                case 2:
                    AssertMemberComparers<
                        SerializableDictionary<string, int, SerializableDictionary.Cache<int>>
                    >(count, operation);
                    break;
                default:
                    AssertMemberComparers<
                        SerializableSortedDictionary<string, int, SerializableDictionary.Cache<int>>
                    >(count, operation);
                    break;
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void DictionaryMembersRetainLegacyFieldFramingAndLiteralKeys(int family)
        {
            switch (family)
            {
                case 0:
                    AssertMemberBytes(new SerializableDictionary<string, int>());
                    break;
                case 1:
                    AssertMemberBytes(
                        new SerializableSortedDictionary<string, int>(
                            new SortedDictionary<string, int>(StringComparer.Ordinal)
                        )
                    );
                    break;
                case 2:
                    AssertMemberBytes(
                        new SerializableDictionary<string, int, SerializableDictionary.Cache<int>>()
                    );
                    break;
                default:
                    AssertMemberBytes(
                        new SerializableSortedDictionary<
                            string,
                            int,
                            SerializableDictionary.Cache<int>
                        >(new SortedDictionary<string, int>(StringComparer.Ordinal))
                    );
                    break;
            }
        }
    }
}
