// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [Category("Fast")]
    public sealed class EnumStrictLookupTests
    {
        private static void VerifyDeclaredNames<T>()
            where T : unmanaged, Enum
        {
            string[] names = Enum.GetNames(typeof(T));
            foreach (string name in names)
            {
                T expected = (T)Enum.Parse(typeof(T), name);
                Assert.That(EnumNameCache<T>.TryParse(name.AsSpan(), out T actual), Is.True, name);
                Assert.That(actual, Is.EqualTo(expected), name);
                Assert.That(EnumValues<T>.IsDefined(actual), Is.True, name);
            }
        }

        private static void VerifyValue<T>(T value)
            where T : struct, Enum
        {
            Assert.That(
                EnumValues<T>.IsDefined(value),
                Is.EqualTo(Enum.IsDefined(typeof(T), value)),
                value.ToString()
            );
        }

        [Test]
        public void DeclaredNamesAndMembershipCoverEveryUnderlyingWidth()
        {
            VerifyDeclaredNames<ByteValues>();
            VerifyDeclaredNames<SByteValues>();
            VerifyDeclaredNames<ShortValues>();
            VerifyDeclaredNames<UShortValues>();
            VerifyDeclaredNames<IntValues>();
            VerifyDeclaredNames<UIntValues>();
            VerifyDeclaredNames<LongValues>();
            VerifyDeclaredNames<ULongValues>();
            VerifyDeclaredNames<EmptyValues>();
            VerifyDeclaredNames<AliasedValues>();
            VerifyDeclaredNames<FlagsValues>();
            for (int value = -512; value < 512; ++value)
            {
                VerifyValue((ByteValues)value);
                VerifyValue((SByteValues)value);
                VerifyValue((ShortValues)value);
                VerifyValue((UShortValues)value);
                VerifyValue((IntValues)value);
                VerifyValue((UIntValues)value);
                VerifyValue((LongValues)value);
                VerifyValue((ULongValues)value);
                VerifyValue((EmptyValues)value);
                VerifyValue((AliasedValues)value);
                VerifyValue((FlagsValues)value);
            }
            VerifyValue((ULongValues)ulong.MaxValue);
            VerifyValue((ULongValues)(1UL << 63));
            VerifyValue((LongValues)long.MinValue);
            VerifyValue((LongValues)long.MaxValue);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("+1")]
        [TestCase("0x1")]
        [TestCase("First, Second")]
        [TestCase(" First")]
        [TestCase("First ")]
        [TestCase("Unknown")]
        public void ParsingRejectsAnythingExceptADeclaredName(string text)
        {
            Assert.That(
                EnumNameCache<FlagsValues>.TryParse(text.AsSpan(), out FlagsValues value),
                Is.False
            );
            Assert.That(value, Is.EqualTo(default(FlagsValues)));
        }

        [Test]
        public void ParsingRejectsEmbeddedNullCharacters()
        {
            string text = nameof(FlagsValues.First) + '\0';
            Assert.That(text.Length, Is.EqualTo(6));
            Assert.That(text[5], Is.EqualTo('\0'));
            Assert.That(
                EnumNameCache<FlagsValues>.TryParse(text.AsSpan(), out FlagsValues value),
                Is.False
            );
            Assert.That(value, Is.EqualTo(default(FlagsValues)));
        }

        [Test]
        public void ParsingSlicesUsesOrdinalPolicyAndAliases()
        {
            ReadOnlySpan<char> slice = "--First++".AsSpan(2, 5);
            Assert.That(
                EnumNameCache<AliasedValues>.TryParse(slice, out AliasedValues value),
                Is.True
            );
            Assert.That(value, Is.EqualTo(AliasedValues.First));
            Assert.That(
                EnumNameCache<AliasedValues>.TryParse("alias".AsSpan(), out value),
                Is.False
            );
            Assert.That(
                EnumNameCache<AliasedValues>.TryParse(
                    "alias".AsSpan(),
                    out value,
                    ignoreCase: true
                ),
                Is.True
            );
            Assert.That(value, Is.EqualTo(AliasedValues.Alias));
            Assert.That(
                EnumNameCache<CaseValues>.TryParse("First".AsSpan(), out CaseValues exact),
                Is.True
            );
            Assert.That(exact, Is.EqualTo(CaseValues.First));
            Assert.That(
                EnumNameCache<CaseValues>.TryParse("FIRST".AsSpan(), out exact, true),
                Is.False
            );
            Assert.That(exact, Is.EqualTo(default(CaseValues)));
            Assert.That(EnumNameCache<EmptyValues>.TryParse("First".AsSpan(), out _), Is.False);
        }

        [Test]
        public void MembershipDoesNotAcceptCachedUndeclaredNames()
        {
            EnumNameCache<IntValues>.ToCachedName((IntValues)3);
            Assert.That(EnumValues<IntValues>.IsDefined((IntValues)3), Is.False);
            Assert.That(EnumValues<FlagsValues>.IsDefined((FlagsValues)3), Is.False);
            Assert.That(EnumValues<FlagsValues>.IsDefined(FlagsValues.All), Is.True);
        }

        [Test]
        public void WarmLookupsDoNotAllocate()
        {
            EnumValues<IntValues>.IsDefined(IntValues.First);
            EnumValues<ULongValues>.IsDefined(ULongValues.High);
            EnumNameCache<AliasedValues>.TryParse("Alias".AsSpan(), out _);
            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            long controlBefore = GC.GetAllocatedBytesForCurrentThread();
            byte[] control = new byte[128];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - controlBefore;
            GC.KeepAlive(control);
            if (controlBytes == 0)
            {
                Assert.Ignore("The allocation counter did not observe a retained allocation.");
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool matched = true;
            for (int i = 0; i < 256; ++i)
            {
                bool dense = EnumValues<IntValues>.IsDefined(IntValues.First);
                bool sparse = EnumValues<ULongValues>.IsDefined(ULongValues.High);
                bool parsed = EnumNameCache<AliasedValues>.TryParse("Alias".AsSpan(), out _);
                matched = matched && dense && sparse && parsed;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(matched, Is.True);
            Assert.That(allocated, Is.Zero);
        }

        private enum ByteValues : byte
        {
            First = 1,
            Last = 255,
        }

        private enum SByteValues : sbyte
        {
            Negative = -128,
            First = 1,
            Last = 127,
        }

        private enum ShortValues : short
        {
            Negative = -32768,
            First = 1,
            Last = 32767,
        }

        private enum UShortValues : ushort
        {
            First = 1,
            Last = 65535,
        }

        private enum IntValues
        {
            Negative = -2,
            First = 1,
            Last = 2,
        }

        private enum UIntValues : uint
        {
            First = 1,
            Last = uint.MaxValue,
        }

        private enum LongValues : long
        {
            Negative = long.MinValue,
            First = 1,
            Last = long.MaxValue,
        }

        private enum ULongValues : ulong
        {
            First = 1,
            High = 1UL << 63,
            Last = ulong.MaxValue,
        }

        private enum EmptyValues { }

        private enum AliasedValues
        {
            Negative = -1,
            First = 1,
            Alias = 1,
        }

        private enum CaseValues
        {
            First = 1,
            first = 2,
        }

        [Flags]
        private enum FlagsValues
        {
            First = 1,
            Second = 2,
            All = 7,
        }
    }
}
