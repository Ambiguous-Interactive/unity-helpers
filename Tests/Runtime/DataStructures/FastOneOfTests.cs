// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.OneOf;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class FastOneOf3Tests
    {
        [Test]
        public void ImplicitConversionFromT0Works()
        {
            FastOneOf<int, string, bool> oneOf = 42;

            Assert.IsTrue(oneOf.IsT0);
            Assert.IsFalse(oneOf.IsT1);
            Assert.IsFalse(oneOf.IsT2);
            Assert.AreEqual(0, oneOf.Index);
        }

        [Test]
        public void ImplicitConversionFromT1Works()
        {
            FastOneOf<int, string, bool> oneOf = "hello";

            Assert.IsFalse(oneOf.IsT0);
            Assert.IsTrue(oneOf.IsT1);
            Assert.IsFalse(oneOf.IsT2);
            Assert.AreEqual(1, oneOf.Index);
        }

        [Test]
        public void ImplicitConversionFromT2Works()
        {
            FastOneOf<int, string, bool> oneOf = true;

            Assert.IsFalse(oneOf.IsT0);
            Assert.IsFalse(oneOf.IsT1);
            Assert.IsTrue(oneOf.IsT2);
            Assert.AreEqual(2, oneOf.Index);
        }

        [Test]
        public void AsT0ReturnsValueWhenT0()
        {
            FastOneOf<int, string, bool> oneOf = 42;
            Assert.AreEqual(42, oneOf.AsT0);
        }

        [Test]
        public void AsT0ThrowsWhenNotT0()
        {
            FastOneOf<int, string, bool> oneOf = "hello";
            Assert.Throws<InvalidOperationException>(() =>
            {
                int _ = oneOf.AsT0;
            });
        }

        [Test]
        public void AsT1ReturnsValueWhenT1()
        {
            FastOneOf<int, string, bool> oneOf = "hello";
            Assert.AreEqual("hello", oneOf.AsT1);
        }

        [Test]
        public void AsT1ThrowsWhenNotT1()
        {
            FastOneOf<int, string, bool> oneOf = 42;
            Assert.Throws<InvalidOperationException>(() =>
            {
                string _ = oneOf.AsT1;
            });
        }

        [Test]
        public void AsT2ReturnsValueWhenT2()
        {
            FastOneOf<int, string, bool> oneOf = true;
            Assert.AreEqual(true, oneOf.AsT2);
        }

        [Test]
        public void AsT2ThrowsWhenNotT2()
        {
            FastOneOf<int, string, bool> oneOf = 42;
            Assert.Throws<InvalidOperationException>(() =>
            {
                bool _ = oneOf.AsT2;
            });
        }

        [Test]
        public void TryGetT0ReturnsTrueWhenT0()
        {
            FastOneOf<int, string, bool> oneOf = 42;
            bool success = oneOf.TryGetT0(out int value);

            Assert.IsTrue(success);
            Assert.AreEqual(42, value);
        }

        [Test]
        public void TryGetT0ReturnsFalseWhenNotT0()
        {
            FastOneOf<int, string, bool> oneOf = "hello";
            bool success = oneOf.TryGetT0(out int value);

            Assert.IsFalse(success);
            Assert.AreEqual(default(int), value);
        }

        [Test]
        public void TryGetT1ReturnsTrueWhenT1()
        {
            FastOneOf<int, string, bool> oneOf = "hello";
            bool success = oneOf.TryGetT1(out string value);

            Assert.IsTrue(success);
            Assert.AreEqual("hello", value);
        }

        [Test]
        public void TryGetT1ReturnsFalseWhenNotT1()
        {
            FastOneOf<int, string, bool> oneOf = 42;
            bool success = oneOf.TryGetT1(out string value);

            Assert.IsFalse(success);
            Assert.AreEqual(default(string), value);
        }

        [Test]
        public void TryGetT2ReturnsTrueWhenT2()
        {
            FastOneOf<int, string, bool> oneOf = true;
            bool success = oneOf.TryGetT2(out bool value);

            Assert.IsTrue(success);
            Assert.AreEqual(true, value);
        }

        [Test]
        public void TryGetT2ReturnsFalseWhenNotT2()
        {
            FastOneOf<int, string, bool> oneOf = 42;
            bool success = oneOf.TryGetT2(out bool value);

            Assert.IsFalse(success);
            Assert.AreEqual(default(bool), value);
        }

        [Test]
        public void SwitchInvokesCorrectActionForT0()
        {
            FastOneOf<int, string, bool> oneOf = 42;
            int called = -1;

            oneOf.Switch(i => called = 0, _ => called = 1, _ => called = 2);

            Assert.AreEqual(0, called);
        }

        [Test]
        public void SwitchInvokesCorrectActionForT1()
        {
            FastOneOf<int, string, bool> oneOf = "hello";
            int called = -1;

            oneOf.Switch(i => called = 0, _ => called = 1, _ => called = 2);

            Assert.AreEqual(1, called);
        }

        [Test]
        public void SwitchInvokesCorrectActionForT2()
        {
            FastOneOf<int, string, bool> oneOf = true;
            int called = -1;

            oneOf.Switch(i => called = 0, _ => called = 1, _ => called = 2);

            Assert.AreEqual(2, called);
        }

        [Test]
        public void SwitchHandlesNullAction()
        {
            FastOneOf<int, string, bool> oneOf = 42;
            Assert.DoesNotThrow(() => oneOf.Switch(null, s => { }, b => { }));
        }

        [Test]
        public void MatchReturnsCorrectResultForT0()
        {
            FastOneOf<int, string, bool> oneOf = 42;
            string result = oneOf.Match(i => $"int: {i}", s => $"string: {s}", b => $"bool: {b}");

            Assert.AreEqual("int: 42", result);
        }

        [Test]
        public void MatchReturnsCorrectResultForT1()
        {
            FastOneOf<int, string, bool> oneOf = "hello";
            string result = oneOf.Match(i => $"int: {i}", s => $"string: {s}", b => $"bool: {b}");

            Assert.AreEqual("string: hello", result);
        }

        [Test]
        public void MatchReturnsCorrectResultForT2()
        {
            FastOneOf<int, string, bool> oneOf = true;
            string result = oneOf.Match(i => $"int: {i}", s => $"string: {s}", b => $"bool: {b}");

            Assert.AreEqual("bool: True", result);
        }

        [Test]
        public void MapTransformsT0Correctly()
        {
            FastOneOf<int, string, bool> oneOf = 42;
            FastOneOf<string, int, double> mapped = oneOf.Map(
                i => i.ToString(),
                s => s.Length,
                b => b ? 1.0 : 0.0
            );

            Assert.IsTrue(mapped.IsT0);
            Assert.AreEqual("42", mapped.AsT0);
        }

        [Test]
        public void MapTransformsT1Correctly()
        {
            FastOneOf<int, string, bool> oneOf = "hello";
            FastOneOf<string, int, double> mapped = oneOf.Map(
                i => i.ToString(),
                s => s.Length,
                b => b ? 1.0 : 0.0
            );

            Assert.IsTrue(mapped.IsT1);
            Assert.AreEqual(5, mapped.AsT1);
        }

        [Test]
        public void MapTransformsT2Correctly()
        {
            FastOneOf<int, string, bool> oneOf = true;
            FastOneOf<string, int, double> mapped = oneOf.Map(
                i => i.ToString(),
                s => s.Length,
                b => b ? 1.0 : 0.0
            );

            Assert.IsTrue(mapped.IsT2);
            Assert.AreEqual(1.0, mapped.AsT2);
        }

        [Test]
        public void EqualsReturnsTrueForEqualT0Values()
        {
            FastOneOf<int, string, bool> oneOf1 = 42;
            FastOneOf<int, string, bool> oneOf2 = 42;

            Assert.IsTrue(oneOf1.Equals(oneOf2));
            Assert.IsTrue(oneOf1 == oneOf2);
            Assert.IsFalse(oneOf1 != oneOf2);
        }

        [Test]
        public void EqualsReturnsTrueForEqualT1Values()
        {
            FastOneOf<int, string, bool> oneOf1 = "hello";
            FastOneOf<int, string, bool> oneOf2 = "hello";

            Assert.IsTrue(oneOf1.Equals(oneOf2));
            Assert.IsTrue(oneOf1 == oneOf2);
            Assert.IsFalse(oneOf1 != oneOf2);
        }

        [Test]
        public void EqualsReturnsTrueForEqualT2Values()
        {
            FastOneOf<int, string, bool> oneOf1 = true;
            FastOneOf<int, string, bool> oneOf2 = true;

            Assert.IsTrue(oneOf1.Equals(oneOf2));
            Assert.IsTrue(oneOf1 == oneOf2);
            Assert.IsFalse(oneOf1 != oneOf2);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentValues()
        {
            FastOneOf<int, string, bool> oneOf1 = 42;
            FastOneOf<int, string, bool> oneOf2 = 43;

            Assert.IsFalse(oneOf1.Equals(oneOf2));
            Assert.IsFalse(oneOf1 == oneOf2);
            Assert.IsTrue(oneOf1 != oneOf2);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentTypes()
        {
            FastOneOf<int, string, bool> oneOf1 = 42;
            FastOneOf<int, string, bool> oneOf2 = "42";

            Assert.IsFalse(oneOf1.Equals(oneOf2));
            Assert.IsFalse(oneOf1 == oneOf2);
            Assert.IsTrue(oneOf1 != oneOf2);
        }

        [Test]
        public void GetHashCodeIsConsistentForSameValue()
        {
            FastOneOf<int, string, bool> oneOf1 = 42;
            FastOneOf<int, string, bool> oneOf2 = 42;

            Assert.AreEqual(oneOf1.GetHashCode(), oneOf2.GetHashCode());
        }

        [Test]
        public void GetHashCodeDiffersForDifferentTypes()
        {
            FastOneOf<int, string, bool> oneOf1 = 42;
            FastOneOf<int, string, bool> oneOf2 = "hello";

            Assert.AreNotEqual(oneOf1.GetHashCode(), oneOf2.GetHashCode());
        }

        [Test]
        public void GetHashCodeOnlyHashesActiveValue()
        {
            FastOneOf<int, string, bool> oneOf1 = 42;
            FastOneOf<int, string, bool> oneOf2 = 42;

            int hash1 = oneOf1.GetHashCode();
            int hash2 = oneOf2.GetHashCode();

            Assert.AreEqual(hash1, hash2);
        }

        [Test]
        public void ToStringFormatsT0Correctly()
        {
            FastOneOf<int, string, bool> oneOf = 42;
            string result = oneOf.ToString();

            Assert.AreEqual("T0(42)", result);
        }

        [Test]
        public void ToStringFormatsT1Correctly()
        {
            FastOneOf<int, string, bool> oneOf = "hello";
            string result = oneOf.ToString();

            Assert.AreEqual("T1(hello)", result);
        }

        [Test]
        public void ToStringFormatsT2Correctly()
        {
            FastOneOf<int, string, bool> oneOf = true;
            string result = oneOf.ToString();

            Assert.AreEqual("T2(True)", result);
        }

        [Test]
        public void ToStringHandlesNull()
        {
            FastOneOf<int, string, bool> oneOf = null;
            string result = oneOf.ToString();

            Assert.AreEqual("T1()", result);
        }

        [Test]
        public void WorksWithValueTypes()
        {
            FastOneOf<int, double, bool> oneOf = 3.14;

            Assert.IsTrue(oneOf.IsT1);
            Assert.AreEqual(3.14, oneOf.AsT1);
        }

        [Test]
        public void WorksWithReferenceTypes()
        {
            FastOneOf<string, object, int[]> oneOf = "test";

            Assert.IsTrue(oneOf.IsT0);
            Assert.AreEqual("test", oneOf.AsT0);
        }

        [Test]
        public void WorksWithNullableTypes()
        {
            FastOneOf<int?, string, bool> oneOf = (int?)null;

            Assert.IsTrue(oneOf.IsT0);
            Assert.IsTrue(oneOf.AsT0 == null);
        }

        [Test]
        public void EqualsObjectReturnsTrueForBoxedEqual()
        {
            FastOneOf<int, string, bool> oneOf1 = 42;
            object oneOf2 = (FastOneOf<int, string, bool>)42;

            Assert.IsTrue(oneOf1.Equals(oneOf2));
        }

        [Test]
        public void EqualsObjectReturnsFalseForDifferentType()
        {
            FastOneOf<int, string, bool> oneOf = 42;
            object other = 42;

            Assert.IsFalse(oneOf.Equals(other));
        }

        [Test]
        public void EqualsObjectReturnsFalseForNull()
        {
            FastOneOf<int, string, bool> oneOf = 42;
            Assert.IsFalse(oneOf.Equals(null));
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class FastOneOf2Tests
    {
        [Test]
        public void ImplicitConversionFromT0Works()
        {
            FastOneOf<int, string> oneOf = 42;

            Assert.IsTrue(oneOf.IsT0);
            Assert.IsFalse(oneOf.IsT1);
            Assert.AreEqual(0, oneOf.Index);
        }

        [Test]
        public void ImplicitConversionFromT1Works()
        {
            FastOneOf<int, string> oneOf = "hello";

            Assert.IsFalse(oneOf.IsT0);
            Assert.IsTrue(oneOf.IsT1);
            Assert.AreEqual(1, oneOf.Index);
        }

        [Test]
        public void AsT0ReturnsValueWhenT0()
        {
            FastOneOf<int, string> oneOf = 42;
            Assert.AreEqual(42, oneOf.AsT0);
        }

        [Test]
        public void AsT0ThrowsWhenNotT0()
        {
            FastOneOf<int, string> oneOf = "hello";
            Assert.Throws<InvalidOperationException>(() =>
            {
                int _ = oneOf.AsT0;
            });
        }

        [Test]
        public void AsT1ReturnsValueWhenT1()
        {
            FastOneOf<int, string> oneOf = "hello";
            Assert.AreEqual("hello", oneOf.AsT1);
        }

        [Test]
        public void AsT1ThrowsWhenNotT1()
        {
            FastOneOf<int, string> oneOf = 42;
            Assert.Throws<InvalidOperationException>(() =>
            {
                string _ = oneOf.AsT1;
            });
        }

        [Test]
        public void TryGetT0ReturnsTrueWhenT0()
        {
            FastOneOf<int, string> oneOf = 42;
            bool success = oneOf.TryGetT0(out int value);

            Assert.IsTrue(success);
            Assert.AreEqual(42, value);
        }

        [Test]
        public void TryGetT0ReturnsFalseWhenNotT0()
        {
            FastOneOf<int, string> oneOf = "hello";
            bool success = oneOf.TryGetT0(out int value);

            Assert.IsFalse(success);
            Assert.AreEqual(default(int), value);
        }

        [Test]
        public void TryGetT1ReturnsTrueWhenT1()
        {
            FastOneOf<int, string> oneOf = "hello";
            bool success = oneOf.TryGetT1(out string value);

            Assert.IsTrue(success);
            Assert.AreEqual("hello", value);
        }

        [Test]
        public void TryGetT1ReturnsFalseWhenNotT1()
        {
            FastOneOf<int, string> oneOf = 42;
            bool success = oneOf.TryGetT1(out string value);

            Assert.IsFalse(success);
            Assert.AreEqual(default(string), value);
        }

        [Test]
        public void SwitchInvokesCorrectActionForT0()
        {
            FastOneOf<int, string> oneOf = 42;
            int called = -1;

            oneOf.Switch(_ => called = 0, _ => called = 1);

            Assert.AreEqual(0, called);
        }

        [Test]
        public void SwitchInvokesCorrectActionForT1()
        {
            FastOneOf<int, string> oneOf = "hello";
            int called = -1;

            oneOf.Switch(_ => called = 0, _ => called = 1);

            Assert.AreEqual(1, called);
        }

        [Test]
        public void MatchReturnsCorrectResultForT0()
        {
            FastOneOf<int, string> oneOf = 42;
            string result = oneOf.Match(i => $"int: {i}", s => $"string: {s}");

            Assert.AreEqual("int: 42", result);
        }

        [Test]
        public void MatchReturnsCorrectResultForT1()
        {
            FastOneOf<int, string> oneOf = "hello";
            string result = oneOf.Match(i => $"int: {i}", s => $"string: {s}");

            Assert.AreEqual("string: hello", result);
        }

        [Test]
        public void MapTransformsT0Correctly()
        {
            FastOneOf<int, string> oneOf = 42;
            FastOneOf<string, int> mapped = oneOf.Map(i => i.ToString(), s => s.Length);

            Assert.IsTrue(mapped.IsT0);
            Assert.AreEqual("42", mapped.AsT0);
        }

        [Test]
        public void MapTransformsT1Correctly()
        {
            FastOneOf<int, string> oneOf = "hello";
            FastOneOf<string, int> mapped = oneOf.Map(i => i.ToString(), s => s.Length);

            Assert.IsTrue(mapped.IsT1);
            Assert.AreEqual(5, mapped.AsT1);
        }

        [Test]
        public void EqualsReturnsTrueForEqualValues()
        {
            FastOneOf<int, string> oneOf1 = 42;
            FastOneOf<int, string> oneOf2 = 42;

            Assert.IsTrue(oneOf1.Equals(oneOf2));
            Assert.IsTrue(oneOf1 == oneOf2);
            Assert.IsFalse(oneOf1 != oneOf2);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentValues()
        {
            FastOneOf<int, string> oneOf1 = 42;
            FastOneOf<int, string> oneOf2 = "hello";

            Assert.IsFalse(oneOf1.Equals(oneOf2));
            Assert.IsFalse(oneOf1 == oneOf2);
            Assert.IsTrue(oneOf1 != oneOf2);
        }

        [Test]
        public void GetHashCodeIsConsistentForSameValue()
        {
            FastOneOf<int, string> oneOf1 = 42;
            FastOneOf<int, string> oneOf2 = 42;

            Assert.AreEqual(oneOf1.GetHashCode(), oneOf2.GetHashCode());
        }

        [Test]
        public void ToStringFormatsT0Correctly()
        {
            FastOneOf<int, string> oneOf = 42;
            string result = oneOf.ToString();

            Assert.AreEqual("T0(42)", result);
        }

        [Test]
        public void ToStringFormatsT1Correctly()
        {
            FastOneOf<int, string> oneOf = "hello";
            string result = oneOf.ToString();

            Assert.AreEqual("T1(hello)", result);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class FastOneOf4Tests
    {
        [Test]
        public void ImplicitConversionFromT0Works()
        {
            FastOneOf<int, string, bool, double> oneOf = 42;

            Assert.IsTrue(oneOf.IsT0);
            Assert.IsFalse(oneOf.IsT1);
            Assert.IsFalse(oneOf.IsT2);
            Assert.IsFalse(oneOf.IsT3);
            Assert.AreEqual(0, oneOf.Index);
        }

        [Test]
        public void ImplicitConversionFromT1Works()
        {
            FastOneOf<int, string, bool, double> oneOf = "hello";

            Assert.IsFalse(oneOf.IsT0);
            Assert.IsTrue(oneOf.IsT1);
            Assert.IsFalse(oneOf.IsT2);
            Assert.IsFalse(oneOf.IsT3);
            Assert.AreEqual(1, oneOf.Index);
        }

        [Test]
        public void ImplicitConversionFromT2Works()
        {
            FastOneOf<int, string, bool, double> oneOf = true;

            Assert.IsFalse(oneOf.IsT0);
            Assert.IsFalse(oneOf.IsT1);
            Assert.IsTrue(oneOf.IsT2);
            Assert.IsFalse(oneOf.IsT3);
            Assert.AreEqual(2, oneOf.Index);
        }

        [Test]
        public void ImplicitConversionFromT3Works()
        {
            FastOneOf<int, string, bool, double> oneOf = 3.14;

            Assert.IsFalse(oneOf.IsT0);
            Assert.IsFalse(oneOf.IsT1);
            Assert.IsFalse(oneOf.IsT2);
            Assert.IsTrue(oneOf.IsT3);
            Assert.AreEqual(3, oneOf.Index);
        }

        [Test]
        public void AsT0ReturnsValueWhenT0()
        {
            FastOneOf<int, string, bool, double> oneOf = 42;
            Assert.AreEqual(42, oneOf.AsT0);
        }

        [Test]
        public void AsT3ReturnsValueWhenT3()
        {
            FastOneOf<int, string, bool, double> oneOf = 3.14;
            Assert.AreEqual(3.14, oneOf.AsT3);
        }

        [Test]
        public void AsT3ThrowsWhenNotT3()
        {
            FastOneOf<int, string, bool, double> oneOf = 42;
            Assert.Throws<InvalidOperationException>(() =>
            {
                double _ = oneOf.AsT3;
            });
        }

        [Test]
        public void TryGetT3ReturnsTrueWhenT3()
        {
            FastOneOf<int, string, bool, double> oneOf = 3.14;
            bool success = oneOf.TryGetT3(out double value);

            Assert.IsTrue(success);
            Assert.AreEqual(3.14, value);
        }

        [Test]
        public void TryGetT3ReturnsFalseWhenNotT3()
        {
            FastOneOf<int, string, bool, double> oneOf = 42;
            bool success = oneOf.TryGetT3(out double value);

            Assert.IsFalse(success);
            Assert.AreEqual(default(double), value);
        }

        [Test]
        public void SwitchInvokesCorrectActionForT3()
        {
            FastOneOf<int, string, bool, double> oneOf = 3.14;
            int called = -1;

            oneOf.Switch(_ => called = 0, _ => called = 1, _ => called = 2, _ => called = 3);

            Assert.AreEqual(3, called);
        }

        [Test]
        public void MatchReturnsCorrectResultForT3()
        {
            FastOneOf<int, string, bool, double> oneOf = 3.14;
            string result = oneOf.Match(
                i => $"int: {i}",
                s => $"string: {s}",
                b => $"bool: {b}",
                d => $"double: {d}"
            );

            Assert.AreEqual("double: 3.14", result);
        }

        [Test]
        public void MapTransformsT3Correctly()
        {
            FastOneOf<int, string, bool, double> oneOf = 3.14;
            FastOneOf<string, int, double, bool> mapped = oneOf.Map(
                i => i.ToString(),
                s => s.Length,
                b => b ? 1.0 : 0.0,
                d => 0 < d
            );

            Assert.IsTrue(mapped.IsT3);
            Assert.AreEqual(true, mapped.AsT3);
        }

        [Test]
        public void EqualsReturnsTrueForEqualT3Values()
        {
            FastOneOf<int, string, bool, double> oneOf1 = 3.14;
            FastOneOf<int, string, bool, double> oneOf2 = 3.14;

            Assert.IsTrue(oneOf1.Equals(oneOf2));
            Assert.IsTrue(oneOf1 == oneOf2);
            Assert.IsFalse(oneOf1 != oneOf2);
        }

        [Test]
        public void ToStringFormatsT3Correctly()
        {
            FastOneOf<int, string, bool, double> oneOf = 3.14;
            string result = oneOf.ToString();

            Assert.AreEqual("T3(3.14)", result);
        }

        [Test]
        public void GetHashCodeIsConsistentForT3()
        {
            FastOneOf<int, string, bool, double> oneOf1 = 3.14;
            FastOneOf<int, string, bool, double> oneOf2 = 3.14;

            Assert.AreEqual(oneOf1.GetHashCode(), oneOf2.GetHashCode());
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class NoneTests
    {
        [Test]
        public void DefaultNoneIsValid()
        {
            None none = default;
            Assert.IsTrue(none != null);
        }

        [Test]
        public void NoneDefaultSingletonIsValid()
        {
            None none = None.Default;
            Assert.IsTrue(none != null);
        }

        [Test]
        public void NoneEqualsOtherNone()
        {
            None none1 = default;
            None none2 = default;

            Assert.IsTrue(none1.Equals(none2));
        }

        [Test]
        public void NoneEqualsDefaultSingleton()
        {
            None none = default;
            Assert.IsTrue(none.Equals(None.Default));
        }

        [Test]
        public void NoneEqualityOperatorReturnsTrue()
        {
            None none1 = default;
            None none2 = None.Default;

            Assert.IsTrue(none1 == none2);
        }

        [Test]
        public void NoneInequalityOperatorReturnsFalse()
        {
            None none1 = default;
            None none2 = None.Default;

            Assert.IsFalse(none1 != none2);
        }

        [Test]
        public void NoneEqualsObjectReturnsTrueForNone()
        {
            None none1 = default;
            object none2 = default(None);

            Assert.IsTrue(none1.Equals(none2));
        }

        [Test]
        public void NoneEqualsObjectReturnsFalseForNonNone()
        {
            None none = default;
            object other = 42;

            Assert.IsFalse(none.Equals(other));
        }

        [Test]
        public void NoneEqualsObjectReturnsFalseForNull()
        {
            None none = default;
            Assert.IsFalse(none.Equals(null));
        }

        [Test]
        public void NoneGetHashCodeReturnsZero()
        {
            None none1 = default;
            None none2 = None.Default;

            Assert.AreEqual(0, none1.GetHashCode());
            Assert.AreEqual(0, none2.GetHashCode());
        }

        [Test]
        public void NoneGetHashCodeIsConsistent()
        {
            None none1 = default;
            None none2 = None.Default;

            Assert.AreEqual(none1.GetHashCode(), none2.GetHashCode());
        }

        [Test]
        public void NoneToStringReturnsNone()
        {
            None none = default;
            Assert.AreEqual("None", none.ToString());
        }

        [Test]
        public void NoneDefaultToStringReturnsNone()
        {
            Assert.AreEqual("None", None.Default.ToString());
        }

        [Test]
        public void NoneCanBeUsedInCollections()
        {
            HashSet<None> set = new() { default, None.Default, default };

            Assert.AreEqual(1, set.Count);
        }

        [Test]
        public void NoneCanBeUsedInDictionary()
        {
            Dictionary<None, int> dict = new() { [default] = 1 };

            Assert.AreEqual(1, dict[None.Default]);
        }

        [Test]
        public void NoneWithFastOneOfRepresentsAbsence()
        {
            FastOneOf<int, None> maybeInt = None.Default;

            Assert.IsTrue(maybeInt.IsT1);
            Assert.IsFalse(maybeInt.IsT0);
        }

        [Test]
        public void NoneWithFastOneOfCanCheckForValue()
        {
            FastOneOf<int, None> maybeInt = 42;

            bool hasValue = maybeInt.TryGetT0(out int value);

            Assert.IsTrue(hasValue);
            Assert.AreEqual(42, value);
        }

        [Test]
        public void NoneWithFastOneOfCanCheckForAbsence()
        {
            FastOneOf<int, None> maybeInt = None.Default;

            bool hasValue = maybeInt.TryGetT0(out _);
            bool hasNone = maybeInt.TryGetT1(out None _);

            Assert.IsFalse(hasValue);
            Assert.IsTrue(hasNone);
        }
    }
}
