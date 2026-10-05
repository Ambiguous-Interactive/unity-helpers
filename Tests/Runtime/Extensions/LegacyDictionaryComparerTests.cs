// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Tests.Runtime.Serialization;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class LegacyDictionaryComparerTests
    {
        private static IDictionary<string, TValue> CreateMap<TValue>(int family)
        {
            if (family == 0 || family == 2)
            {
                Dictionary<string, TValue> backing = new(StringComparer.OrdinalIgnoreCase);
                return family == 0
                    ? new SerializableDictionary<string, TValue>(backing)
                    : new SerializableDictionary<
                        string,
                        TValue,
                        SerializableDictionary.Cache<TValue>
                    >(backing);
            }

            SortedDictionary<string, TValue> sorted = new(
                Comparer<string>.Create(
                    (left, right) => StringComparer.OrdinalIgnoreCase.Compare(right, left)
                )
            );
            return family == 1
                ? new SerializableSortedDictionary<string, TValue>(sorted)
                : new SerializableSortedDictionary<
                    string,
                    TValue,
                    SerializableDictionary.Cache<TValue>
                >(sorted);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void CustomComparersAndEnumerationOrderRetainLegacyEquality(int family)
        {
            IDictionary<string, int> first = CreateMap<int>(family);
            first.Add("b", 2);
            first.Add("a", 1);
            IDictionary<string, int> reverse = CreateMap<int>(family);
            reverse.Add("a", 1);
            reverse.Add("b", 2);
            Assert.Throws<ArgumentException>(() => first.Add("A", 3), "Custom key comparer.");
            Assert.AreEqual(2, first.Count);
            bool sameOrder = family == 1 || family == 3;
            Assert.AreEqual(sameOrder, first.ProtoEquals(reverse));
            Assert.AreEqual(
                sameOrder,
                ProtoEqualityExtensions
                    .GetProtoComparer<IDictionary<string, int>>()
                    .Equals(first, reverse)
            );
            using MemoryStream stream = new();
            Assert.IsTrue(((ILegacyProtobufMap)first).TrySerialize(stream));
            CollectionAssert.AreEqual(
                Convert.FromBase64String("CgUKAWIQAgoFCgFhEAE="),
                stream.ToArray()
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void DefaultAndNegativeIntegersRetainBundledOracleBytes(int family)
        {
            IDictionary<int, int> first;
            switch (family)
            {
                case 0:
                    first = new SerializableDictionary<int, int>();
                    break;
                case 1:
                    first = new SerializableSortedDictionary<int, int>();
                    break;
                case 2:
                    first =
                        new SerializableDictionary<int, int, SerializableDictionary.Cache<int>>();
                    break;
                default:
                    first =
                        new SerializableSortedDictionary<
                            int,
                            int,
                            SerializableDictionary.Cache<int>
                        >();
                    break;
            }
            first.Add(0, 0);
            first.Add(-1, 0);
            first.Add(7, -42);
            using MemoryStream stream = new();
            Assert.IsTrue(((ILegacyProtobufMap)first).TrySerialize(stream));
            string expected =
                family == 0 || family == 2
                    ? "CgAKCwj///////////8BCg0IBxDW//////////8B"
                    : "CgsI////////////AQoACg0IBxDW//////////8B";
            CollectionAssert.AreEqual(Convert.FromBase64String(expected), stream.ToArray());
            int originalHash = ProtoEqualityExtensions
                .GetProtoComparer<IDictionary<int, int>>()
                .GetHashCode(first);
            first[0] = 1;
            Assert.AreNotEqual(
                originalHash,
                ProtoEqualityExtensions.GetProtoComparer<IDictionary<int, int>>().GetHashCode(first)
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void FailedValueSerializationDoesNotContaminateTheNextComparison(int family)
        {
            IDictionary<string, Action> unsupported = CreateMap<Action>(family);
            unsupported.Add("key", () => { });
            Assert.Throws<InvalidOperationException>(() =>
                ProtoEqualityExtensions
                    .GetProtoComparer<IDictionary<string, Action>>()
                    .GetHashCode(unsupported)
            );
            SerializableDictionary<string, int> valid = new() { [" "] = 7, ["key"] = 42 };
            Assert.AreEqual(
                60268007,
                ProtoEqualityExtensions
                    .GetProtoComparer<SerializableDictionary<string, int>>()
                    .GetHashCode(valid)
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void NullAndEmptyMapValuesRemainDistinct(int family)
        {
            IDictionary<string, string> first = CreateMap<string>(family);
            IDictionary<string, string> empty = CreateMap<string>(family);
            first.Add("", null);
            empty.Add("", "");
            Assert.IsFalse(first.ProtoEquals(empty));
            IEqualityComparer<IDictionary<string, string>> comparer =
                ProtoEqualityExtensions.GetProtoComparer<IDictionary<string, string>>();
            Assert.IsFalse(comparer.Equals(first, empty));
            Assert.AreNotEqual(comparer.GetHashCode(first), comparer.GetHashCode(empty));
            using MemoryStream stream = new();
            Assert.IsTrue(((ILegacyProtobufMap)first).TrySerialize(stream));
            CollectionAssert.AreEqual(new byte[] { 10, 2, 10, 0 }, stream.ToArray());
            first[""] = "";
            Assert.IsTrue(first.ProtoEquals(empty));
            first.Add(" ", "saved");
            first.Add("\u00a0", "different");
            Assert.AreEqual(3, first.Count);
            Assert.IsTrue(first.TryGetValue(" ", out string spaceValue));
            Assert.IsTrue(first.TryGetValue("\u00a0", out string nonbreakingSpaceValue));
            Assert.AreEqual("saved", spaceValue);
            Assert.AreEqual("different", nonbreakingSpaceValue);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void NestedMessageValuesParticipateInEquality(int family)
        {
            IDictionary<string, WProtoExplicitDefaultContract> first =
                CreateMap<WProtoExplicitDefaultContract>(family);
            IDictionary<string, WProtoExplicitDefaultContract> same =
                CreateMap<WProtoExplicitDefaultContract>(family);
            first.Add("value", new WProtoExplicitDefaultContract { Int32 = 17 });
            same.Add("value", new WProtoExplicitDefaultContract { Int32 = 17 });
            Assert.IsTrue(first.ProtoEquals(same));
            Assert.IsTrue(same.TryGetValue("value", out WProtoExplicitDefaultContract changed));
            changed.Int32 = 18;
            Assert.IsFalse(first.ProtoEquals(same));
            Assert.AreNotEqual(
                ProtoEqualityExtensions
                    .GetProtoComparer<IDictionary<string, WProtoExplicitDefaultContract>>()
                    .GetHashCode(first),
                ProtoEqualityExtensions
                    .GetProtoComparer<IDictionary<string, WProtoExplicitDefaultContract>>()
                    .GetHashCode(same)
            );
        }

        [Test]
        public void ConsumerSubclassRetainsItsOwnContractAcrossDeclaredRoots()
        {
            LegacyComparerContractDictionary first = new() { Revision = 17, ["key"] = 1 };
            LegacyComparerContractDictionary same = new() { Revision = 17, ["key"] = 2 };
            LegacyComparerContractDictionary different = new() { Revision = 18, ["key"] = 1 };
            using MemoryStream stream = new();
            Assert.IsFalse(((ILegacyProtobufMap)first).TrySerialize(stream));
            Assert.AreEqual(0, stream.Length);
            Assert.IsTrue(first.ProtoEquals(same));
            Assert.IsFalse(first.ProtoEquals(different));
            Assert.IsTrue(ProtoEqualityExtensions.ProtoEquals<object>(first, same));
            Assert.IsFalse(ProtoEqualityExtensions.ProtoEquals<object>(first, different));
            Assert.IsTrue(
                ProtoEqualityExtensions.ProtoEquals<SerializableDictionary<string, int>>(
                    first,
                    same
                )
            );
            Assert.IsFalse(
                ProtoEqualityExtensions.ProtoEquals<SerializableDictionary<string, int>>(
                    first,
                    different
                )
            );
            IEqualityComparer<IDictionary<string, int>> comparer =
                ProtoEqualityExtensions.GetProtoComparer<IDictionary<string, int>>();
            Assert.IsTrue(comparer.Equals(first, same));
            Assert.IsFalse(comparer.Equals(first, different));
            Assert.AreEqual(comparer.GetHashCode(first), comparer.GetHashCode(same));
            Assert.AreNotEqual(comparer.GetHashCode(first), comparer.GetHashCode(different));
            ProtoBuf.Serializer.Serialize(stream, first);
            CollectionAssert.AreEqual(new byte[] { 56, 17 }, stream.ToArray());
        }
    }
}
