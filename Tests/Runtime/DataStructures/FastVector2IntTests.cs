// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class FastVector2IntTests
    {
        [Test]
        public void ConstructorSetsXAndY()
        {
            FastVector2Int vector = new(3, 4);
            Assert.AreEqual(3, vector.x);
            Assert.AreEqual(4, vector.y);
        }

        [Test]
        public void GetHashCodeIsConsistent()
        {
            FastVector2Int vector1 = new(10, 20);
            FastVector2Int vector2 = new(10, 20);
            Assert.AreEqual(vector1.GetHashCode(), vector2.GetHashCode());
        }

        [Test]
        public void GetHashCodeDifferentForDifferentVectors()
        {
            FastVector2Int vector1 = new(10, 20);
            FastVector2Int vector2 = new(20, 10);
            Assert.AreNotEqual(vector1.GetHashCode(), vector2.GetHashCode());
        }

        [Test]
        public void EqualsSameVectorReturnsTrue()
        {
            FastVector2Int vector1 = new(5, 7);
            FastVector2Int vector2 = new(5, 7);
            Assert.IsTrue(vector1.Equals(vector2));
        }

        [Test]
        public void EqualsDifferentVectorReturnsFalse()
        {
            FastVector2Int vector1 = new(5, 7);
            FastVector2Int vector2 = new(7, 5);
            Assert.IsFalse(vector1.Equals(vector2));
        }

        [Test]
        public void EqualsVector2IntWhenCoordinatesMatch()
        {
            FastVector2Int fastVector = new(8, 12);
            Vector2Int unityVector = new(8, 12);
            Assert.IsTrue(fastVector.Equals(unityVector));
        }

        [Test]
        public void HasSameXYIgnoresVector3IntZ()
        {
            FastVector2Int fastVector = new(3, 4);
            Vector3Int vector3 = new(3, 4, 100);
            Assert.IsTrue(fastVector.HasSameXY(vector3));
        }

        [Test]
        public void HasSameXYIgnoresFastVector3IntZ()
        {
            FastVector2Int vector2 = new(5, 6);
            FastVector3Int vector3 = new(5, 6, 999);
            Assert.IsTrue(vector2.HasSameXY(vector3));
        }

        [Test]
        public void EqualsObjectAcceptsOnlyAnotherFastVector2Int()
        {
            FastVector2Int vector = new(1, 2);
            Assert.IsTrue(vector.Equals((object)new FastVector2Int(1, 2)));

            // Cross-dimensional equality broke symmetry and transitivity while hashing different components.
            Assert.IsFalse(vector.Equals((object)new Vector2Int(1, 2)));
            Assert.IsFalse(vector.Equals((object)new FastVector3Int(1, 2, 0)));
            Assert.IsFalse(vector.Equals((object)new Vector3Int(1, 2, 0)));
            Assert.IsFalse(vector.Equals(null));
            Assert.IsFalse(vector.Equals("not a vector"));
        }

        [Test]
        public void CompareToReturnsZeroForEqualVectors()
        {
            FastVector2Int vector1 = new(10, 20);
            FastVector2Int vector2 = new(10, 20);
            Assert.AreEqual(0, vector1.CompareTo(vector2));
        }

        [Test]
        public void CompareToComparesByXThenY()
        {
            FastVector2Int smaller = new(1, 100);
            FastVector2Int larger = new(2, 1);
            Assert.Less(smaller.CompareTo(larger), 0);
            Assert.Greater(larger.CompareTo(smaller), 0);

            FastVector2Int smallerY = new(5, 5);
            FastVector2Int largerY = new(5, 10);
            Assert.Less(smallerY.CompareTo(largerY), 0);
        }

        [Test]
        public void CompareToHandlesVector2Int()
        {
            FastVector2Int fastVector = new(3, 4);
            Vector2Int smaller = new(2, 10);
            Vector2Int larger = new(4, 1);
            Assert.Greater(fastVector.CompareTo(smaller), 0);
            Assert.Less(fastVector.CompareTo(larger), 0);
        }

        [Test]
        public void CompareToObjectAgreesWithEqualsObject()
        {
            FastVector2Int vector = new(5, 5);
            object[] candidates =
            {
                new FastVector2Int(5, 5),
                new Vector2Int(5, 5),
                new FastVector3Int(5, 5, 0),
                new Vector3Int(5, 5, 0),
                "invalid",
            };

            // Sorted collections require CompareTo and Equals to accept the same types.
            foreach (object candidate in candidates)
            {
                Assert.AreEqual(
                    vector.Equals(candidate),
                    vector.CompareTo(candidate) == 0,
                    $"CompareTo and Equals disagree about {candidate.GetType().Name}"
                );
            }

            Assert.AreEqual(0, vector.CompareTo((object)new FastVector2Int(5, 5)));
            Assert.AreEqual(-1, vector.CompareTo((object)new Vector2Int(5, 5)));
            Assert.AreEqual(-1, vector.CompareTo((object)new FastVector3Int(5, 5, 0)));
            Assert.AreEqual(-1, vector.CompareTo("invalid"));
            Assert.AreEqual(1, vector.CompareTo(null));
        }

        [Test]
        public void AdditionOperatorCombinesCoordinates()
        {
            FastVector2Int a = new(10, 20);
            FastVector2Int b = new(5, 3);
            FastVector2Int result = a + b;
            Assert.AreEqual(15, result.x);
            Assert.AreEqual(23, result.y);
        }

        [Test]
        public void SubtractionOperatorSubtractsCoordinates()
        {
            FastVector2Int a = new(10, 20);
            FastVector2Int b = new(3, 7);
            FastVector2Int result = a - b;
            Assert.AreEqual(7, result.x);
            Assert.AreEqual(13, result.y);
        }

        [Test]
        public void ImplicitConversionToVector2Int()
        {
            FastVector2Int fastVector = new(6, 9);
            Vector2Int unityVector = fastVector;
            Assert.AreEqual(6, unityVector.x);
            Assert.AreEqual(9, unityVector.y);
        }

        [Test]
        public void ImplicitConversionFromVector2Int()
        {
            Vector2Int unityVector = new(12, 15);
            FastVector2Int fastVector = unityVector;
            Assert.AreEqual(12, fastVector.x);
            Assert.AreEqual(15, fastVector.y);
        }

        [Test]
        public void ImplicitConversionFromFastVector3Int()
        {
            FastVector3Int vector3 = new(7, 11, 99);
            FastVector2Int vector2 = vector3;
            Assert.AreEqual(7, vector2.x);
            Assert.AreEqual(11, vector2.y);
        }

        [Test]
        public void ToStringFormatsCorrectly()
        {
            FastVector2Int vector = new(42, -17);
            Assert.AreEqual("(42, -17)", vector.ToString());
        }

        [Test]
        public void AsFastVector3IntCreatesVector3WithZeroZ()
        {
            FastVector2Int vector2 = new(5, 8);
            FastVector3Int vector3 = vector2.AsFastVector3Int();
            Assert.AreEqual(5, vector3.x);
            Assert.AreEqual(8, vector3.y);
            Assert.AreEqual(0, vector3.z);
        }

        [Test]
        public void AsVector2ConvertsToFloatVector()
        {
            FastVector2Int intVector = new(10, 20);
            Vector2 floatVector = intVector.AsVector2();
            Assert.AreEqual(10f, floatVector.x);
            Assert.AreEqual(20f, floatVector.y);
        }

        [Test]
        public void AsVector3ConvertsToFloatVectorWithZeroZ()
        {
            FastVector2Int intVector = new(3, 7);
            Vector3 floatVector = intVector.AsVector3();
            Assert.AreEqual(3f, floatVector.x);
            Assert.AreEqual(7f, floatVector.y);
            Assert.AreEqual(0f, floatVector.z);
        }

        [Test]
        public void WorksAsHashSetKey()
        {
            HashSet<FastVector2Int> set = new()
            {
                new FastVector2Int(1, 2),
                new FastVector2Int(3, 4),
                new FastVector2Int(1, 2),
            };

            Assert.AreEqual(2, set.Count);
            Assert.IsTrue(set.Contains(new FastVector2Int(1, 2)));
            Assert.IsFalse(set.Contains(new FastVector2Int(5, 6)));
        }

        [Test]
        public void WorksAsDictionaryKey()
        {
            Dictionary<FastVector2Int, string> dict = new()
            {
                [new FastVector2Int(1, 1)] = "one-one",
                [new FastVector2Int(2, 2)] = "two-two",
            };

            Assert.AreEqual("one-one", dict.ValueFor(new FastVector2Int(1, 1)));
            Assert.IsTrue(dict.ContainsKey(new FastVector2Int(2, 2)));
            Assert.IsFalse(dict.ContainsKey(new FastVector2Int(3, 3)));
        }

        [Test]
        public void NegativeCoordinatesWorkCorrectly()
        {
            FastVector2Int negative = new(-5, -10);
            FastVector2Int positive = new(5, 10);

            Assert.AreNotEqual(negative.GetHashCode(), positive.GetHashCode());
            Assert.IsFalse(negative.Equals(positive));

            FastVector2Int sum = negative + positive;
            Assert.AreEqual(0, sum.x);
            Assert.AreEqual(0, sum.y);
        }

        [Test]
        public void ZeroCoordinatesWork()
        {
            FastVector2Int zero = new(0, 0);
            Assert.AreEqual(0, zero.x);
            Assert.AreEqual(0, zero.y);

            HashSet<FastVector2Int> set = new() { zero };
            Assert.IsTrue(set.Contains(new FastVector2Int(0, 0)));
        }

        [Test]
        public void LargeCoordinatesWork()
        {
            FastVector2Int large = new(int.MaxValue, int.MaxValue);
            FastVector2Int alsoLarge = new(int.MaxValue, int.MaxValue);
            Assert.AreEqual(large.GetHashCode(), alsoLarge.GetHashCode());
            Assert.IsTrue(large.Equals(alsoLarge));
        }

        [Test]
        public void HashCodePrecomputedForPerformance()
        {
            FastVector2Int vector = new(100, 200);
            int hash1 = vector.GetHashCode();
            int hash2 = vector.GetHashCode();
            int hash3 = vector.GetHashCode();

            Assert.AreEqual(hash1, hash2);
            Assert.AreEqual(hash2, hash3);
        }
    }
}
