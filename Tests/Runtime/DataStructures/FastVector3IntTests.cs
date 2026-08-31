// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class FastVector3IntTests
    {
        [Test]
        public void ConstructorWithThreeParametersSetsXYZ()
        {
            FastVector3Int vector = new(3, 4, 5);
            Assert.AreEqual(3, vector.x);
            Assert.AreEqual(4, vector.y);
            Assert.AreEqual(5, vector.z);
        }

        [Test]
        public void ConstructorWithTwoParametersSetsZToZero()
        {
            FastVector3Int vector = new(7, 11);
            Assert.AreEqual(7, vector.x);
            Assert.AreEqual(11, vector.y);
            Assert.AreEqual(0, vector.z);
        }

        [Test]
        public void ConstructorFromVector3IntCopiesValues()
        {
            Vector3Int unityVector = new(9, 12, 15);
            FastVector3Int fastVector = new(unityVector);
            Assert.AreEqual(9, fastVector.x);
            Assert.AreEqual(12, fastVector.y);
            Assert.AreEqual(15, fastVector.z);
        }

        [Test]
        public void ZeroConstantIsZeroVector()
        {
            Assert.AreEqual(0, FastVector3Int.zero.x);
            Assert.AreEqual(0, FastVector3Int.zero.y);
            Assert.AreEqual(0, FastVector3Int.zero.z);
        }

        [Test]
        public void GetHashCodeIsConsistent()
        {
            FastVector3Int vector1 = new(10, 20, 30);
            FastVector3Int vector2 = new(10, 20, 30);
            Assert.AreEqual(vector1.GetHashCode(), vector2.GetHashCode());
        }

        [Test]
        public void GetHashCodeDifferentForDifferentVectors()
        {
            FastVector3Int vector1 = new(10, 20, 30);
            FastVector3Int vector2 = new(30, 20, 10);
            Assert.AreNotEqual(vector1.GetHashCode(), vector2.GetHashCode());
        }

        [Test]
        public void EqualsSameVectorReturnsTrue()
        {
            FastVector3Int vector1 = new(5, 7, 9);
            FastVector3Int vector2 = new(5, 7, 9);
            Assert.IsTrue(vector1.Equals(vector2));
            Assert.IsTrue(vector1 == vector2);
        }

        [Test]
        public void EqualsDifferentVectorReturnsFalse()
        {
            FastVector3Int vector1 = new(5, 7, 9);
            FastVector3Int vector2 = new(9, 7, 5);
            Assert.IsFalse(vector1.Equals(vector2));
            Assert.IsTrue(vector1 != vector2);
        }

        [Test]
        public void EqualsVector3IntWhenCoordinatesMatch()
        {
            FastVector3Int fastVector = new(8, 12, 16);
            Vector3Int unityVector = new(8, 12, 16);
            Assert.IsTrue(fastVector.Equals(unityVector));
        }

        [Test]
        public void HasSameXYIgnoresVector2IntZ()
        {
            FastVector3Int vector3 = new(3, 4, 100);
            Vector2Int vector2 = new(3, 4);
            Assert.IsTrue(vector3.HasSameXY(vector2));
        }

        [Test]
        public void HasSameXYIgnoresFastVector2IntZ()
        {
            FastVector3Int vector3 = new(5, 6, 999);
            FastVector2Int vector2 = new(5, 6);
            Assert.IsTrue(vector3.HasSameXY(vector2));
        }

        [Test]
        public void EqualsObjectAcceptsOnlyAnotherFastVector3Int()
        {
            FastVector3Int vector = new(1, 2, 3);
            Assert.IsTrue(vector.Equals((object)new FastVector3Int(1, 2, 3)));
            Assert.IsFalse(vector.Equals((object)new Vector3Int(1, 2, 3)));
            Assert.IsFalse(vector.Equals((object)new FastVector2Int(1, 2)));
            Assert.IsFalse(vector.Equals((object)new Vector2Int(1, 2)));
            Assert.IsFalse(vector.Equals(null));
            Assert.IsFalse(vector.Equals("not a vector"));
        }

        [Test]
        public void HasSameXYIsNotEqualityAcrossDimensions()
        {
            FastVector3Int deep = new(1, 2, 3);
            FastVector2Int planar = new(1, 2);
            FastVector3Int shallow = new(1, 2, 9);

            Assert.IsTrue(deep.HasSameXY(planar));
            Assert.IsTrue(shallow.HasSameXY(planar));

            /*
                Sharing X and Y is not equality: it is not transitive, and the two three-dimensional
                vectors hash apart. Equals(object) refusing the planar vector is what keeps the
                relation the boxed path exposes an equivalence.
            */
            Assert.IsFalse(deep.Equals(shallow));
            Assert.IsFalse(deep.Equals((object)planar));
            Assert.IsFalse(shallow.Equals((object)planar));
        }

        [Test]
        public void CompareToReturnsZeroForEqualVectors()
        {
            FastVector3Int vector1 = new(10, 20, 30);
            FastVector3Int vector2 = new(10, 20, 30);
            Assert.AreEqual(0, vector1.CompareTo(vector2));
        }

        [Test]
        public void CompareToComparesByXThenYThenZ()
        {
            FastVector3Int smallest = new(1, 100, 100);
            FastVector3Int largest = new(2, 1, 1);
            Assert.Less(smallest.CompareTo(largest), 0);
            Assert.Greater(largest.CompareTo(smallest), 0);

            FastVector3Int smallerY = new(5, 5, 100);
            FastVector3Int largerY = new(5, 10, 1);
            Assert.Less(smallerY.CompareTo(largerY), 0);

            FastVector3Int smallerZ = new(5, 5, 5);
            FastVector3Int largerZ = new(5, 5, 10);
            Assert.Less(smallerZ.CompareTo(largerZ), 0);
        }

        [Test]
        public void CompareToHandlesVector3Int()
        {
            FastVector3Int fastVector = new(3, 4, 5);
            Vector3Int smaller = new(2, 10, 10);
            Vector3Int larger = new(4, 1, 1);
            Assert.Greater(fastVector.CompareTo(smaller), 0);
            Assert.Less(fastVector.CompareTo(larger), 0);
        }

        [Test]
        public void CompareToObjectHandlesMultipleTypes()
        {
            FastVector3Int vector = new(5, 5, 5);
            Assert.AreEqual(0, vector.CompareTo((object)new FastVector3Int(5, 5, 5)));
            Assert.AreEqual(0, vector.CompareTo((object)new Vector3Int(5, 5, 5)));
            Assert.Less(vector.CompareTo((object)new FastVector2Int(6, 0)), 0);
            Assert.AreEqual(-1, vector.CompareTo("invalid"));
        }

        [Test]
        public void AdditionWithFastVector3IntCombinesCoordinates()
        {
            FastVector3Int a = new(10, 20, 30);
            FastVector3Int b = new(5, 3, 7);
            FastVector3Int result = a + b;
            Assert.AreEqual(15, result.x);
            Assert.AreEqual(23, result.y);
            Assert.AreEqual(37, result.z);
        }

        [Test]
        public void AdditionWithVector3IntWorks()
        {
            FastVector3Int a = new(1, 2, 3);
            Vector3Int b = new(4, 5, 6);
            FastVector3Int result = a + b;
            Assert.AreEqual(5, result.x);
            Assert.AreEqual(7, result.y);
            Assert.AreEqual(9, result.z);
        }

        [Test]
        public void AdditionWithVector2IntKeepsZ()
        {
            FastVector3Int a = new(10, 20, 30);
            Vector2Int b = new(5, 3);
            FastVector3Int result = a + b;
            Assert.AreEqual(15, result.x);
            Assert.AreEqual(23, result.y);
            Assert.AreEqual(30, result.z);
        }

        [Test]
        public void AdditionWithFastVector2IntKeepsZ()
        {
            FastVector3Int a = new(10, 20, 30);
            FastVector2Int b = new(5, 3);
            FastVector3Int result = a + b;
            Assert.AreEqual(15, result.x);
            Assert.AreEqual(23, result.y);
            Assert.AreEqual(30, result.z);
        }

        [Test]
        public void SubtractionWithFastVector3IntSubtractsCoordinates()
        {
            FastVector3Int a = new(10, 20, 30);
            FastVector3Int b = new(3, 7, 5);
            FastVector3Int result = a - b;
            Assert.AreEqual(7, result.x);
            Assert.AreEqual(13, result.y);
            Assert.AreEqual(25, result.z);
        }

        [Test]
        public void SubtractionWithVector3IntWorks()
        {
            FastVector3Int a = new(10, 10, 10);
            Vector3Int b = new(1, 2, 3);
            FastVector3Int result = a - b;
            Assert.AreEqual(9, result.x);
            Assert.AreEqual(8, result.y);
            Assert.AreEqual(7, result.z);
        }

        [Test]
        public void SubtractionWithVector2IntKeepsZ()
        {
            FastVector3Int a = new(10, 20, 30);
            Vector2Int b = new(5, 3);
            FastVector3Int result = a - b;
            Assert.AreEqual(5, result.x);
            Assert.AreEqual(17, result.y);
            Assert.AreEqual(30, result.z);
        }

        [Test]
        public void SubtractionWithFastVector2IntKeepsZ()
        {
            FastVector3Int a = new(10, 20, 30);
            FastVector2Int b = new(5, 3);
            FastVector3Int result = a - b;
            Assert.AreEqual(5, result.x);
            Assert.AreEqual(17, result.y);
            Assert.AreEqual(30, result.z);
        }

        [Test]
        public void ImplicitConversionToVector3Int()
        {
            FastVector3Int fastVector = new(6, 9, 12);
            Vector3Int unityVector = fastVector;
            Assert.AreEqual(6, unityVector.x);
            Assert.AreEqual(9, unityVector.y);
            Assert.AreEqual(12, unityVector.z);
        }

        [Test]
        public void ImplicitConversionFromVector3Int()
        {
            Vector3Int unityVector = new(12, 15, 18);
            FastVector3Int fastVector = unityVector;
            Assert.AreEqual(12, fastVector.x);
            Assert.AreEqual(15, fastVector.y);
            Assert.AreEqual(18, fastVector.z);
        }

        [Test]
        public void ImplicitConversionToVector2IntDropsZ()
        {
            FastVector3Int vector3 = new(7, 11, 99);
            Vector2Int vector2 = vector3;
            Assert.AreEqual(7, vector2.x);
            Assert.AreEqual(11, vector2.y);
        }

        [Test]
        public void ToStringFormatsCorrectly()
        {
            FastVector3Int vector = new(42, -17, 0);
            Assert.AreEqual("(42, -17, 0)", vector.ToString());
        }

        [Test]
        public void FastVector2IntCreatesVector2()
        {
            FastVector3Int vector3 = new(5, 8, 13);
            FastVector2Int vector2 = vector3.FastVector2Int();
            Assert.AreEqual(5, vector2.x);
            Assert.AreEqual(8, vector2.y);
        }

        [Test]
        public void AsVector2ConvertsToFloatVector()
        {
            FastVector3Int intVector = new(10, 20, 30);
            Vector2 floatVector = intVector.AsVector2();
            Assert.AreEqual(10f, floatVector.x);
            Assert.AreEqual(20f, floatVector.y);
        }

        [Test]
        public void AsVector3ConvertsToFloatVector()
        {
            FastVector3Int intVector = new(3, 7, 11);
            Vector3 floatVector = intVector.AsVector3();
            Assert.AreEqual(3f, floatVector.x);
            Assert.AreEqual(7f, floatVector.y);
            Assert.AreEqual(11f, floatVector.z);
        }

        [Test]
        public void WorksAsHashSetKey()
        {
            HashSet<FastVector3Int> set = new()
            {
                new FastVector3Int(1, 2, 3),
                new FastVector3Int(4, 5, 6),
                new FastVector3Int(1, 2, 3),
            };

            Assert.AreEqual(2, set.Count);
            Assert.IsTrue(set.Contains(new FastVector3Int(1, 2, 3)));
            Assert.IsFalse(set.Contains(new FastVector3Int(7, 8, 9)));
        }

        [Test]
        public void WorksAsDictionaryKey()
        {
            Dictionary<FastVector3Int, string> dict = new()
            {
                [new FastVector3Int(1, 1, 1)] = "ones",
                [new FastVector3Int(2, 2, 2)] = "twos",
            };

            Assert.AreEqual("ones", dict[new FastVector3Int(1, 1, 1)]);
            Assert.IsTrue(dict.ContainsKey(new FastVector3Int(2, 2, 2)));
            Assert.IsFalse(dict.ContainsKey(new FastVector3Int(3, 3, 3)));
        }

        [Test]
        public void NegativeCoordinatesWorkCorrectly()
        {
            FastVector3Int negative = new(-5, -10, -15);
            FastVector3Int positive = new(5, 10, 15);

            Assert.AreNotEqual(negative.GetHashCode(), positive.GetHashCode());
            Assert.IsFalse(negative.Equals(positive));

            FastVector3Int sum = negative + positive;
            Assert.AreEqual(0, sum.x);
            Assert.AreEqual(0, sum.y);
            Assert.AreEqual(0, sum.z);
        }

        [Test]
        public void ZeroCoordinatesWork()
        {
            FastVector3Int zero = new(0, 0, 0);
            Assert.AreEqual(0, zero.x);
            Assert.AreEqual(0, zero.y);
            Assert.AreEqual(0, zero.z);
            Assert.IsTrue(zero.Equals(FastVector3Int.zero));

            HashSet<FastVector3Int> set = new() { zero };
            Assert.IsTrue(set.Contains(new FastVector3Int(0, 0, 0)));
        }

        [Test]
        public void LargeCoordinatesWork()
        {
            FastVector3Int large = new(int.MaxValue, int.MaxValue, int.MaxValue);
            FastVector3Int alsoLarge = new(int.MaxValue, int.MaxValue, int.MaxValue);
            Assert.AreEqual(large.GetHashCode(), alsoLarge.GetHashCode());
            Assert.IsTrue(large.Equals(alsoLarge));
        }

        [Test]
        public void HashCodePrecomputedForPerformance()
        {
            FastVector3Int vector = new(100, 200, 300);
            int hash1 = vector.GetHashCode();
            int hash2 = vector.GetHashCode();
            int hash3 = vector.GetHashCode();

            // Hash should be the same every time (pre-computed)
            Assert.AreEqual(hash1, hash2);
            Assert.AreEqual(hash2, hash3);
        }

        [Test]
        public void EqualityOperatorWorks()
        {
            FastVector3Int a = new(1, 2, 3);
            FastVector3Int b = new(1, 2, 3);
            FastVector3Int c = new(3, 2, 1);

            Assert.IsTrue(a == b);
            Assert.IsFalse(a == c);
        }

        [Test]
        public void InequalityOperatorWorks()
        {
            FastVector3Int a = new(1, 2, 3);
            FastVector3Int b = new(1, 2, 3);
            FastVector3Int c = new(3, 2, 1);

            Assert.IsFalse(a != b);
            Assert.IsTrue(a != c);
        }
    }
}
