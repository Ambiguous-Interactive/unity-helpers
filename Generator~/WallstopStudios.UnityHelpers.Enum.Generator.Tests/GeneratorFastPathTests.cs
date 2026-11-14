// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Enum.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;

    [TestFixture]
    public sealed class GeneratorFastPathTests
    {
        private delegate bool UnsignedConversion<T>(T value, out ulong result);

        private delegate bool SignedConversion<T>(T value, out long result);

        private static readonly Type[] ContractTypes =
        {
            typeof(SmallEnum),
            typeof(ByteEnum),
            typeof(SignedByteEnum),
            typeof(ShortEnum),
            typeof(UnsignedShortEnum),
            typeof(UnsignedIntEnum),
            typeof(LongEnum),
            typeof(UnsignedLongEnum),
            typeof(DenseFlags),
            typeof(WideFlags),
            typeof(SignedFlags),
            typeof(AliasedEnum),
            typeof(EmptyEnum),
            typeof(DisplayEnum),
            typeof(ObsoleteMemberEnum),
            typeof(InternalVisibilityEnum),
            typeof(PublicVisibilityEnum),
            typeof(NestedEnumHolder.NestedEnum),
        };

        private static void AssertNames<T>(Func<T, string> generated)
            where T : unmanaged, Enum
        {
            T[] values = (T[])Enum.GetValues(typeof(T));
            foreach (T value in values)
            {
                Assert.That(
                    generated(value),
                    Is.EqualTo(EnumNameCache<T>.ToCachedName(value)),
                    typeof(T).Name + "." + value
                );
            }

            Assert.That(generated(default), Is.EqualTo(EnumNameCache<T>.ToCachedName(default)));
        }

        private static void AssertCombinationNames<T>(Func<T, string> generated)
            where T : unmanaged, Enum
        {
            T[] values = (T[])Enum.GetValues(typeof(T));
            foreach (T left in values)
            {
                foreach (T right in values)
                {
                    ulong leftBits = left.TryConvertToUInt64(out ulong leftValue) ? leftValue : 0;
                    ulong rightBits = right.TryConvertToUInt64(out ulong rightValue)
                        ? rightValue
                        : 0;
                    T combined = (T)Enum.ToObject(typeof(T), leftBits | rightBits);
                    Assert.That(
                        generated(combined),
                        Is.EqualTo(EnumNameCache<T>.ToCachedName(combined)),
                        typeof(T).Name + " combination " + combined
                    );
                }
            }
        }

        private static void AssertDisplay<T>(Func<T, string> generated)
            where T : unmanaged, Enum
        {
            T[] values = (T[])Enum.GetValues(typeof(T));
            foreach (T value in values)
            {
                Assert.That(
                    generated(value),
                    Is.EqualTo(EnumDisplayNameCache<T>.ToDisplayName(value)),
                    typeof(T).Name + "." + value
                );
            }
            Assert.That(
                generated(default),
                Is.EqualTo(EnumDisplayNameCache<T>.ToDisplayName(default))
            );
        }

        private static void AssertFlags<T>(Func<T, T, bool> generated)
            where T : unmanaged, Enum
        {
            T[] values = (T[])Enum.GetValues(typeof(T));
            foreach (T value in values)
            {
                foreach (T flag in values)
                {
                    Assert.That(
                        generated(value, flag),
                        Is.EqualTo(value.HasFlag(flag)),
                        typeof(T).Name + "." + value + " / " + flag
                    );
                }
            }
        }

        private static void AssertConversion<T>(
            UnsignedConversion<T> unsignedConversion,
            SignedConversion<T> signedConversion
        )
            where T : unmanaged, Enum
        {
            T[] values = (T[])Enum.GetValues(typeof(T));
            foreach (T value in values)
            {
                Enum boxed = value;
                Assert.That(unsignedConversion(value, out ulong unsigned), Is.True);
                Assert.That(boxed.TryConvertToUInt64(out ulong expectedUnsigned), Is.True);
                Assert.That(unsigned, Is.EqualTo(expectedUnsigned));

                Assert.That(signedConversion(value, out long signed), Is.True);
                Assert.That(boxed.TryConvertToInt64(out long expectedSigned), Is.True);
                Assert.That(signed, Is.EqualTo(expectedSigned));
            }
        }

        private static string InvokeGenericName(Type enumType, object value)
        {
            Type cacheType = typeof(EnumNameCache<>).MakeGenericType(enumType);
            return (string)cacheType.GetMethod("ToCachedName").Invoke(null, new[] { value });
        }

        [Test]
        public void GeneratedNamesMatchTheGeneralizedPathForEveryDeclaredValue()
        {
            AssertNames<SmallEnum>(SmallEnumWEnumHelpers.ToCachedName);
            AssertNames<ByteEnum>(ByteEnumWEnumHelpers.ToCachedName);
            AssertNames<SignedByteEnum>(SignedByteEnumWEnumHelpers.ToCachedName);
            AssertNames<ShortEnum>(ShortEnumWEnumHelpers.ToCachedName);
            AssertNames<UnsignedShortEnum>(UnsignedShortEnumWEnumHelpers.ToCachedName);
            AssertNames<UnsignedIntEnum>(UnsignedIntEnumWEnumHelpers.ToCachedName);
            AssertNames<LongEnum>(LongEnumWEnumHelpers.ToCachedName);
            AssertNames<UnsignedLongEnum>(UnsignedLongEnumWEnumHelpers.ToCachedName);
            AssertNames<DenseFlags>(DenseFlagsWEnumHelpers.ToCachedName);
            AssertNames<WideFlags>(WideFlagsWEnumHelpers.ToCachedName);
            AssertNames<SignedFlags>(SignedFlagsWEnumHelpers.ToCachedName);
            AssertNames<AliasedEnum>(AliasedEnumWEnumHelpers.ToCachedName);
            AssertNames<EmptyEnum>(EmptyEnumWEnumHelpers.ToCachedName);
            AssertNames<DisplayEnum>(DisplayEnumWEnumHelpers.ToCachedName);
            AssertNames<ObsoleteMemberEnum>(ObsoleteMemberEnumWEnumHelpers.ToCachedName);
            AssertNames<InternalVisibilityEnum>(InternalVisibilityEnumWEnumHelpers.ToCachedName);
            AssertNames<PublicVisibilityEnum>(PublicVisibilityEnumWEnumHelpers.ToCachedName);
            AssertNames<NestedEnumHolder.NestedEnum>(
                NestedEnumHolder_NestedEnumWEnumHelpers.ToCachedName
            );
        }

        [Test]
        public void GeneratedDisplayNamesMatchTheGeneralizedPathForEveryDeclaredValue()
        {
            AssertDisplay<DisplayEnum>(DisplayEnumWEnumHelpers.ToDisplayName);
            AssertDisplay<SmallEnum>(SmallEnumWEnumHelpers.ToDisplayName);
            AssertDisplay<SignedFlags>(SignedFlagsWEnumHelpers.ToDisplayName);
            AssertDisplay<AliasedEnum>(AliasedEnumWEnumHelpers.ToDisplayName);
            AssertDisplay<AllAliasedDisplayEnum>(AllAliasedDisplayEnumWEnumHelpers.ToDisplayName);
        }

        [Test]
        public void GeneratedCombinationNamesDelegateToTheGeneralizedPath()
        {
            AssertCombinationNames<DenseFlags>(DenseFlagsWEnumHelpers.ToCachedName);
            AssertCombinationNames<WideFlags>(WideFlagsWEnumHelpers.ToCachedName);
            AssertCombinationNames<SignedFlags>(SignedFlagsWEnumHelpers.ToCachedName);
        }

        [Test]
        public void GeneratedFlagChecksMatchEnumHasFlagForEveryPair()
        {
            AssertFlags<SmallEnum>(SmallEnumWEnumHelpers.HasFlagNoAlloc);
            AssertFlags<DenseFlags>(DenseFlagsWEnumHelpers.HasFlagNoAlloc);
            AssertFlags<WideFlags>(WideFlagsWEnumHelpers.HasFlagNoAlloc);
            AssertFlags<SignedFlags>(SignedFlagsWEnumHelpers.HasFlagNoAlloc);
            AssertFlags<LongEnum>(LongEnumWEnumHelpers.HasFlagNoAlloc);
            AssertFlags<UnsignedLongEnum>(UnsignedLongEnumWEnumHelpers.HasFlagNoAlloc);
        }

        [Test]
        public void GeneratedConversionsMatchTheBoxedGeneralizedPath()
        {
            AssertConversion<SmallEnum>(
                SmallEnumWEnumHelpers.TryConvertToUInt64,
                SmallEnumWEnumHelpers.TryConvertToInt64
            );
            AssertConversion<ByteEnum>(
                ByteEnumWEnumHelpers.TryConvertToUInt64,
                ByteEnumWEnumHelpers.TryConvertToInt64
            );
            AssertConversion<SignedByteEnum>(
                SignedByteEnumWEnumHelpers.TryConvertToUInt64,
                SignedByteEnumWEnumHelpers.TryConvertToInt64
            );
            AssertConversion<ShortEnum>(
                ShortEnumWEnumHelpers.TryConvertToUInt64,
                ShortEnumWEnumHelpers.TryConvertToInt64
            );
            AssertConversion<UnsignedShortEnum>(
                UnsignedShortEnumWEnumHelpers.TryConvertToUInt64,
                UnsignedShortEnumWEnumHelpers.TryConvertToInt64
            );
            AssertConversion<UnsignedIntEnum>(
                UnsignedIntEnumWEnumHelpers.TryConvertToUInt64,
                UnsignedIntEnumWEnumHelpers.TryConvertToInt64
            );
            AssertConversion<LongEnum>(
                LongEnumWEnumHelpers.TryConvertToUInt64,
                LongEnumWEnumHelpers.TryConvertToInt64
            );
            AssertConversion<UnsignedLongEnum>(
                UnsignedLongEnumWEnumHelpers.TryConvertToUInt64,
                UnsignedLongEnumWEnumHelpers.TryConvertToInt64
            );
        }

        [Test]
        public void GeneratedFormattingMatchesTheGeneratedName()
        {
            Span<char> buffer = stackalloc char[64];
            Assert.That(
                SmallEnumWEnumHelpers.TryFormatCachedName(
                    SmallEnum.Second,
                    buffer,
                    out int written
                ),
                Is.True
            );
            Assert.That(buffer.Slice(0, written).ToString(), Is.EqualTo("Second"));

            Assert.That(
                SmallEnumWEnumHelpers.TryFormatCachedName(
                    SmallEnum.Second,
                    new char[2],
                    out written
                ),
                Is.False
            );
            Assert.That(written, Is.Zero);
        }

        [Test]
        public void GeneratedNamesAreAllocationFreeAfterWarmup()
        {
            string warm = SmallEnumWEnumHelpers.ToCachedName(SmallEnum.Second);
            string warmDisplay = DisplayEnumWEnumHelpers.ToDisplayName(DisplayEnum.A);
            Assert.That(warm, Is.Not.Empty);
            Assert.That(warmDisplay, Is.Not.Empty);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 64; ++iteration)
            {
                _ = SmallEnumWEnumHelpers.ToCachedName(SmallEnum.Second);
                _ = DisplayEnumWEnumHelpers.ToDisplayName(DisplayEnum.A);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void EveryGeneratedNameMatchesTheRuntimeFormatting()
        {
            foreach (Type type in ContractTypes)
            {
                Array values = Enum.GetValues(type);
                foreach (object value in values)
                {
                    string expected = value.ToString();
                    string generalized = InvokeGenericName(type, value);
                    Assert.That(generalized, Is.EqualTo(expected), type.Name + "." + value);
                }
            }
        }
    }
}
