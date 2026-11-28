// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [Category("Fast")]
    public sealed class EnumValuesTypeTests
    {
        [Test]
        public void TryParseFindsADeclaredName()
        {
            Assert.IsTrue(
                EnumValues.TryParse(typeof(Sample), "Second".AsSpan(), false, out object value)
            );
            Assert.AreEqual(Sample.Second, value);
        }

        [Test]
        public void TryParseHonorsCaseFolding()
        {
            Assert.IsFalse(
                EnumValues.TryParse(typeof(Sample), "second".AsSpan(), false, out object _)
            );
            Assert.IsTrue(
                EnumValues.TryParse(typeof(Sample), "second".AsSpan(), true, out object value)
            );
            Assert.AreEqual(Sample.Second, value);
        }

        [Test]
        public void TryParseRejectsAnAmbiguousCaseInsensitiveMatch()
        {
            Assert.IsTrue(
                EnumValues.TryParse(typeof(CaseVariant), "Foo".AsSpan(), false, out object exact)
            );
            Assert.AreEqual(CaseVariant.Foo, exact);
            Assert.IsFalse(
                EnumValues.TryParse(typeof(CaseVariant), "foo".AsSpan(), true, out object value)
            );
            Assert.IsTrue(value == null);
        }

        [Test]
        public void TryParseAcceptsAliasesOfOneValue()
        {
            Assert.IsTrue(
                EnumValues.TryParse(
                    typeof(SameValueAlias),
                    "Alias".AsSpan(),
                    false,
                    out object value
                )
            );
            Assert.AreEqual(SameValueAlias.First, value);
        }

        [Test]
        public void TryParseRejectsUnknownNamesAndNonEnumTypes()
        {
            Assert.IsFalse(
                EnumValues.TryParse(typeof(Sample), "Missing".AsSpan(), false, out object value)
            );
            Assert.IsTrue(value == null);
            Assert.IsFalse(
                EnumValues.TryParse(typeof(int), "1".AsSpan(), false, out object notEnum)
            );
            Assert.IsTrue(notEnum == null);
            Assert.IsFalse(EnumValues.TryParse(null, "First".AsSpan(), false, out object nullType));
            Assert.IsTrue(nullType == null);
        }

        [Test]
        public void TryFromIntegerAcceptsRepresentableValues()
        {
            Assert.IsTrue(EnumValues.TryFromInteger(typeof(Sample), 2, out object second));
            Assert.AreEqual(Sample.Second, second);
            Assert.IsTrue(EnumValues.TryFromInteger(typeof(Sample), 99, out object undefined));
            Assert.AreEqual((Sample)99, undefined);
            Assert.IsTrue(EnumValues.TryFromInteger(typeof(ByteSample), 255, out object max));
            Assert.AreEqual(ByteSample.Max, max);
        }

        [Test]
        public void TryFromIntegerRejectsOutOfRangeValues()
        {
            Assert.IsFalse(EnumValues.TryFromInteger(typeof(ByteSample), 256, out object tooLarge));
            Assert.IsTrue(tooLarge == null);
            Assert.IsFalse(EnumValues.TryFromInteger(typeof(ByteSample), -1, out object negative));
            Assert.IsTrue(negative == null);
            Assert.IsFalse(EnumValues.TryFromInteger(typeof(ULongSample), -1, out object unsigned));
            Assert.IsTrue(unsigned == null);
            Assert.IsFalse(EnumValues.TryFromInteger(typeof(int), 1, out object notEnum));
            Assert.IsTrue(notEnum == null);
            Assert.IsFalse(EnumValues.TryFromInteger(null, 1, out object nullType));
            Assert.IsTrue(nullType == null);
        }

        private enum Sample
        {
            Unknown = 0,
            First = 1,
            Second = 2,
        }

        private enum CaseVariant
        {
            Foo = 1,
            FOO = 2,
        }

        private enum SameValueAlias
        {
            First = 1,
            Alias = 1,
        }

        private enum ByteSample : byte
        {
            Zero = 0,
            Max = 255,
        }

        private enum ULongSample : ulong
        {
            Zero = 0,
            Max = ulong.MaxValue,
        }
    }
}
