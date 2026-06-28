// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
    public sealed class ProtoEqualityExtensionsTests : CommonTestBase
    {
        [ProtoContract]
        private sealed class SimpleMessage
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }
        }

        [ProtoContract]
        private sealed class ComplexMessage
        {
            [ProtoMember(1)]
            public int Value { get; set; }

            [ProtoMember(2)]
            public string Text { get; set; }

            [ProtoMember(3)]
            public List<int> Numbers { get; set; }

            [ProtoMember(4)]
            public SimpleMessage Nested { get; set; }
        }

        [ProtoContract]
        private sealed class CollectionMessage
        {
            [ProtoMember(1)]
            public List<int> IntList { get; set; }

            [ProtoMember(2)]
            public List<string> StringList { get; set; }

            [ProtoMember(3)]
            public byte[] ByteArray { get; set; }
        }

        [ProtoContract]
        private struct ValueTypeMessage
        {
            [ProtoMember(1)]
            public int X { get; set; }

            [ProtoMember(2)]
            public int Y { get; set; }
        }

        [ProtoContract]
        private sealed class NumericMessage
        {
            [ProtoMember(1)]
            public int IntValue { get; set; }

            [ProtoMember(2)]
            public long LongValue { get; set; }

            [ProtoMember(3)]
            public float FloatValue { get; set; }

            [ProtoMember(4)]
            public double DoubleValue { get; set; }

            [ProtoMember(5)]
            public bool BoolValue { get; set; }
        }

        [Test]
        public void ProtoEqualsIdenticalSimpleObjectsReturnsTrue()
        {
            SimpleMessage obj1 = new() { Id = 42, Name = "Test" };
            SimpleMessage obj2 = new() { Id = 42, Name = "Test" };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsTrue(
                result,
                "ProtoEquals should return true for objects with identical values"
            );
        }

        [Test]
        public void ProtoEqualsDifferentSimpleObjectsReturnsFalse()
        {
            SimpleMessage obj1 = new() { Id = 42, Name = "Test" };
            SimpleMessage obj2 = new() { Id = 43, Name = "Test" };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsFalse(
                result,
                "ProtoEquals should return false for objects with different values"
            );
        }

        [Test]
        public void ProtoEqualsSameReferenceReturnsTrue()
        {
            SimpleMessage obj = new() { Id = 42, Name = "Test" };

            bool result = obj.ProtoEquals(obj);

            Assert.IsTrue(result, "ProtoEquals should return true for same reference");
        }

        [Test]
        public void ProtoEqualsBothNullReturnsTrue()
        {
            SimpleMessage obj1 = null;
            SimpleMessage obj2 = null;

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsTrue(result, "ProtoEquals should return true when both objects are null");
        }

        [Test]
        public void ProtoEqualsOneNullReturnsFalse()
        {
            SimpleMessage obj1 = new() { Id = 42, Name = "Test" };
            SimpleMessage obj2 = null;

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsFalse(result, "ProtoEquals should return false when one object is null");
        }

        [Test]
        public void ProtoEqualsNullFirstReturnsFalse()
        {
            SimpleMessage obj1 = null;
            SimpleMessage obj2 = new() { Id = 42, Name = "Test" };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsFalse(result, "ProtoEquals should return false when first object is null");
        }

        [Test]
        public void ProtoEqualsComplexObjectsIdenticalReturnsTrue()
        {
            ComplexMessage obj1 = new()
            {
                Value = 100,
                Text = "Complex",
                Numbers = new List<int> { 1, 2, 3 },
                Nested = new SimpleMessage { Id = 1, Name = "Nested" },
            };
            ComplexMessage obj2 = new()
            {
                Value = 100,
                Text = "Complex",
                Numbers = new List<int> { 1, 2, 3 },
                Nested = new SimpleMessage { Id = 1, Name = "Nested" },
            };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsTrue(
                result,
                "ProtoEquals should return true for complex objects with identical values"
            );
        }

        [Test]
        public void ProtoEqualsComplexObjectsDifferentNestedReturnsFalse()
        {
            ComplexMessage obj1 = new()
            {
                Value = 100,
                Text = "Complex",
                Numbers = new List<int> { 1, 2, 3 },
                Nested = new SimpleMessage { Id = 1, Name = "Nested" },
            };
            ComplexMessage obj2 = new()
            {
                Value = 100,
                Text = "Complex",
                Numbers = new List<int> { 1, 2, 3 },
                Nested = new SimpleMessage { Id = 2, Name = "Nested" },
            };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsFalse(result, "ProtoEquals should return false when nested objects differ");
        }

        [Test]
        public void ProtoEqualsComplexObjectsDifferentListReturnsFalse()
        {
            ComplexMessage obj1 = new()
            {
                Value = 100,
                Text = "Complex",
                Numbers = new List<int> { 1, 2, 3 },
                Nested = new SimpleMessage { Id = 1, Name = "Nested" },
            };
            ComplexMessage obj2 = new()
            {
                Value = 100,
                Text = "Complex",
                Numbers = new List<int> { 1, 2, 4 },
                Nested = new SimpleMessage { Id = 1, Name = "Nested" },
            };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsFalse(result, "ProtoEquals should return false when lists differ");
        }

        [Test]
        public void ProtoEqualsEmptyCollectionsReturnsTrue()
        {
            CollectionMessage obj1 = new()
            {
                IntList = new List<int>(),
                StringList = new List<string>(),
                ByteArray = Array.Empty<byte>(),
            };
            CollectionMessage obj2 = new()
            {
                IntList = new List<int>(),
                StringList = new List<string>(),
                ByteArray = Array.Empty<byte>(),
            };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsTrue(
                result,
                "ProtoEquals should return true for objects with empty collections"
            );
        }

        [Test]
        public void ProtoEqualsNullVsEmptyCollectionMatches()
        {
            CollectionMessage obj1 = new()
            {
                IntList = null,
                StringList = null,
                ByteArray = null,
            };
            CollectionMessage obj2 = new()
            {
                IntList = null,
                StringList = null,
                ByteArray = null,
            };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsTrue(
                result,
                "ProtoEquals should return true for objects with null collections"
            );
        }

        [Test]
        public void ProtoEqualsLargeCollectionsReturnsTrue()
        {
            List<int> largeList = new();
            for (int i = 0; i < 10000; i++)
            {
                largeList.Add(i);
            }

            CollectionMessage obj1 = new() { IntList = largeList };
            CollectionMessage obj2 = new() { IntList = new List<int>(largeList) };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsTrue(result, "ProtoEquals should handle large collections correctly");
        }

        [Test]
        public void ProtoEqualsValueTypesIdenticalReturnsTrue()
        {
            ValueTypeMessage val1 = new() { X = 10, Y = 20 };
            ValueTypeMessage val2 = new() { X = 10, Y = 20 };

            bool result = val1.ProtoEquals(val2);

            Assert.IsTrue(
                result,
                "ProtoEquals should return true for value types with identical values"
            );
        }

        [Test]
        public void ProtoEqualsValueTypesDifferentReturnsFalse()
        {
            ValueTypeMessage val1 = new() { X = 10, Y = 20 };
            ValueTypeMessage val2 = new() { X = 11, Y = 20 };

            bool result = val1.ProtoEquals(val2);

            Assert.IsFalse(
                result,
                "ProtoEquals should return false for value types with different values"
            );
        }

        [Test]
        public void ProtoEqualsNumericBoundariesIdenticalReturnsTrue()
        {
            NumericMessage obj1 = new()
            {
                IntValue = int.MaxValue,
                LongValue = long.MaxValue,
                FloatValue = float.MaxValue,
                DoubleValue = double.MaxValue,
                BoolValue = true,
            };
            NumericMessage obj2 = new()
            {
                IntValue = int.MaxValue,
                LongValue = long.MaxValue,
                FloatValue = float.MaxValue,
                DoubleValue = double.MaxValue,
                BoolValue = true,
            };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsTrue(result, "ProtoEquals should handle numeric boundaries correctly");
        }

        [Test]
        public void ProtoEqualsSpecialFloatValuesReturnsTrue()
        {
            NumericMessage obj1 = new()
            {
                FloatValue = float.NaN,
                DoubleValue = double.PositiveInfinity,
            };
            NumericMessage obj2 = new()
            {
                FloatValue = float.NaN,
                DoubleValue = double.PositiveInfinity,
            };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsTrue(
                result,
                "ProtoEquals should handle special float values (NaN, Infinity) correctly"
            );
        }

        [Test]
        public void ProtoEqualsStringDifferencesReturnsFalse()
        {
            SimpleMessage obj1 = new() { Id = 1, Name = "Test" };
            SimpleMessage obj2 = new() { Id = 1, Name = "test" };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsFalse(result, "ProtoEquals should be case-sensitive for strings");
        }

        [Test]
        public void ProtoEqualsEmptyStringsReturnsTrue()
        {
            SimpleMessage obj1 = new() { Id = 1, Name = "" };
            SimpleMessage obj2 = new() { Id = 1, Name = "" };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsTrue(result, "ProtoEquals should return true for objects with empty strings");
        }

        [Test]
        public void ProtoEqualsUnicodeStringsReturnsTrue()
        {
            SimpleMessage obj1 = new() { Id = 1, Name = "Hello 世界 🌍" };
            SimpleMessage obj2 = new() { Id = 1, Name = "Hello 世界 🌍" };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsTrue(result, "ProtoEquals should handle unicode strings correctly");
        }

        [Test]
        public void GetProtoComparerIdenticalObjectsReturnsTrue()
        {
            IEqualityComparer<SimpleMessage> comparer =
                ProtoEqualityExtensions.GetProtoComparer<SimpleMessage>();
            SimpleMessage obj1 = new() { Id = 42, Name = "Test" };
            SimpleMessage obj2 = new() { Id = 42, Name = "Test" };

            bool result = comparer.Equals(obj1, obj2);

            Assert.IsTrue(result, "ProtoComparer should return true for identical objects");
        }

        [Test]
        public void GetProtoComparerDifferentObjectsReturnsFalse()
        {
            IEqualityComparer<SimpleMessage> comparer =
                ProtoEqualityExtensions.GetProtoComparer<SimpleMessage>();
            SimpleMessage obj1 = new() { Id = 42, Name = "Test" };
            SimpleMessage obj2 = new() { Id = 43, Name = "Test" };

            bool result = comparer.Equals(obj1, obj2);

            Assert.IsFalse(result, "ProtoComparer should return false for different objects");
        }

        [Test]
        public void GetProtoComparerGetHashCodeIdenticalObjectsSameHash()
        {
            IEqualityComparer<SimpleMessage> comparer =
                ProtoEqualityExtensions.GetProtoComparer<SimpleMessage>();
            SimpleMessage obj1 = new() { Id = 42, Name = "Test" };
            SimpleMessage obj2 = new() { Id = 42, Name = "Test" };

            int hash1 = comparer.GetHashCode(obj1);
            int hash2 = comparer.GetHashCode(obj2);

            Assert.AreEqual(
                hash1,
                hash2,
                "ProtoComparer should return same hash for identical objects"
            );
        }

        [Test]
        public void GetProtoComparerGetHashCodeDifferentObjectsDifferentHash()
        {
            IEqualityComparer<SimpleMessage> comparer =
                ProtoEqualityExtensions.GetProtoComparer<SimpleMessage>();
            SimpleMessage obj1 = new() { Id = 42, Name = "Test" };
            SimpleMessage obj2 = new() { Id = 43, Name = "Test" };

            int hash1 = comparer.GetHashCode(obj1);
            int hash2 = comparer.GetHashCode(obj2);

            Assert.AreNotEqual(
                hash1,
                hash2,
                "ProtoComparer should return different hashes for different objects"
            );
        }

        [Test]
        public void GetProtoComparerUsedInDictionaryWorksCorrectly()
        {
            IEqualityComparer<SimpleMessage> comparer =
                ProtoEqualityExtensions.GetProtoComparer<SimpleMessage>();
            Dictionary<SimpleMessage, string> dict = new(comparer);

            SimpleMessage key1 = new() { Id = 1, Name = "Key1" };
            SimpleMessage key2 = new() { Id = 1, Name = "Key1" };
            SimpleMessage key3 = new() { Id = 2, Name = "Key2" };

            dict[key1] = "Value1";
            dict[key3] = "Value2";

            Assert.AreEqual(
                "Value1",
                dict[key2],
                "Dictionary with ProtoComparer should find equivalent key"
            );
            Assert.AreEqual(2, dict.Count, "Dictionary should have 2 entries");
        }

        [Test]
        public void GetProtoComparerUsedInHashSetWorksCorrectly()
        {
            IEqualityComparer<SimpleMessage> comparer =
                ProtoEqualityExtensions.GetProtoComparer<SimpleMessage>();
            HashSet<SimpleMessage> set = new(comparer);

            SimpleMessage obj1 = new() { Id = 1, Name = "Test" };
            SimpleMessage obj2 = new() { Id = 1, Name = "Test" };
            SimpleMessage obj3 = new() { Id = 2, Name = "Test" };

            set.Add(obj1);
            set.Add(obj2);
            set.Add(obj3);

            Assert.AreEqual(
                2,
                set.Count,
                "HashSet with ProtoComparer should treat equivalent objects as duplicates"
            );
            Assert.IsTrue(set.Contains(obj2), "HashSet should contain equivalent object");
        }

        [Test]
        public void ProtoEqualsDefaultValuesReturnsTrue()
        {
            SimpleMessage obj1 = new();
            SimpleMessage obj2 = new();

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsTrue(result, "ProtoEquals should return true for objects with default values");
        }

        [Test]
        public void ProtoEqualsComplexObjectsWithNullNestedReturnsTrue()
        {
            ComplexMessage obj1 = new()
            {
                Value = 100,
                Text = "Test",
                Numbers = new List<int> { 1, 2 },
                Nested = null,
            };
            ComplexMessage obj2 = new()
            {
                Value = 100,
                Text = "Test",
                Numbers = new List<int> { 1, 2 },
                Nested = null,
            };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsTrue(
                result,
                "ProtoEquals should return true when both have null nested objects"
            );
        }

        [Test]
        public void ProtoEqualsComplexObjectsOneNullNestedReturnsFalse()
        {
            ComplexMessage obj1 = new()
            {
                Value = 100,
                Text = "Test",
                Numbers = new List<int> { 1, 2 },
                Nested = new SimpleMessage { Id = 1, Name = "Test" },
            };
            ComplexMessage obj2 = new()
            {
                Value = 100,
                Text = "Test",
                Numbers = new List<int> { 1, 2 },
                Nested = null,
            };

            bool result = obj1.ProtoEquals(obj2);

            Assert.IsFalse(
                result,
                "ProtoEquals should return false when one has null nested object"
            );
        }
    }
}
