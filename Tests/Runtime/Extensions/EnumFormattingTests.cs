// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Globalization;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [Category("Fast")]
    public sealed class EnumFormattingTests
    {
        private static void AssertFormat<T>(T value)
            where T : unmanaged, Enum
        {
            string expected = value.ToCachedName();
            char[] buffer = new char[expected.Length];
            Assert.That(value.TryFormatCachedName(buffer, out int written), Is.True);
            Assert.That(new string(buffer, 0, written), Is.EqualTo(expected));
        }

        [Test]
        public void DeclaredNameWritesWithExactAndLargerBuffers()
        {
            char[] exact = new char[nameof(FormatEnum.Second).Length];
            Assert.That(FormatEnum.Second.TryFormatCachedName(exact, out int written), Is.True);
            Assert.That(written, Is.EqualTo(exact.Length));
            Assert.That(new string(exact), Is.EqualTo("Second"));

            char[] larger = new char[64];
            Assert.That(FormatEnum.Second.TryFormatCachedName(larger, out written), Is.True);
            Assert.That(written, Is.EqualTo(nameof(FormatEnum.Second).Length));
            Assert.That(new string(larger, 0, written), Is.EqualTo("Second"));
        }

        [Test]
        public void ShortBufferFailsWithoutWriting()
        {
            char[] buffer = { 'x', 'y' };
            Assert.That(FormatEnum.Second.TryFormatCachedName(buffer, out int written), Is.False);
            Assert.That(written, Is.Zero);
            Assert.That(buffer, Is.EqualTo(new[] { 'x', 'y' }));
        }

        [Test]
        public void FlagsCombinationMatchesTheCachedName()
        {
            FormatFlags value = FormatFlags.First | FormatFlags.Third;
            string expected = value.ToCachedName();
            char[] buffer = new char[expected.Length];
            Assert.That(value.TryFormatCachedName(buffer, out int written), Is.True);
            Assert.That(new string(buffer, 0, written), Is.EqualTo(expected));
        }

        [Test]
        public void UndefinedValueMatchesTheRuntimeFormat()
        {
            FormatEnum value = (FormatEnum)12345;
            string expected = value.ToString("G");
            char[] buffer = new char[expected.Length];
            Assert.That(value.TryFormatCachedName(buffer, out int written), Is.True);
            Assert.That(new string(buffer, 0, written), Is.EqualTo(expected));
        }

        [Test]
        public void EveryUnderlyingWidthMatchesTheCachedName()
        {
            AssertFormat(ByteFormat.First);
            AssertFormat(SByteFormat.First);
            AssertFormat(ShortFormat.First);
            AssertFormat(UShortFormat.First);
            AssertFormat(IntFormat.First);
            AssertFormat(UIntFormat.First);
            AssertFormat(LongFormat.First);
            AssertFormat(ULongFormat.First);
        }

        [Test]
        public void FormattingIsCultureInvariant()
        {
            TextInfo original = CultureInfo.CurrentCulture.TextInfo;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                AssertFormat(FormatFlags.First | FormatFlags.Second);

                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
                AssertFormat(FormatFlags.First | FormatFlags.Second);
            }
            finally
            {
                CultureInfo.CurrentCulture = new CultureInfo(original.CultureName);
            }
        }

        [Test]
        public void DeclaredValuesDoNotAllocateAfterWarmup()
        {
            char[] buffer = new char[64];
            char[] displayBuffer = new char[64];
            for (int iteration = 0; iteration < 8; ++iteration)
            {
                FormatEnum.Second.TryFormatCachedName(buffer, out _);
                FormatDisplayEnum.Alpha.TryFormatCachedName(displayBuffer, out _);
            }

            GCAssert.DoesNotAllocate(() =>
            {
                FormatEnum.Second.TryFormatCachedName(buffer, out _);
            });
            GCAssert.DoesNotAllocate(() =>
            {
                FormatDisplayEnum.Alpha.TryFormatCachedName(displayBuffer, out _);
            });
        }

        private enum FormatEnum
        {
            None = 0,
            First = 1,
            Second = 2,
            Third = 3,
        }

        [Flags]
        private enum FormatFlags
        {
            None = 0,
            First = 1,
            Second = 2,
            Third = 4,
        }

        private enum FormatDisplayEnum
        {
            None = 0,
            Alpha = 1,
        }

        private enum ByteFormat : byte
        {
            None = 0,
            First = 1,
        }

        private enum SByteFormat : sbyte
        {
            None = 0,
            First = 1,
        }

        private enum ShortFormat : short
        {
            None = 0,
            First = 1,
        }

        private enum UShortFormat : ushort
        {
            None = 0,
            First = 1,
        }

        private enum IntFormat
        {
            None = 0,
            First = 1,
        }

        private enum UIntFormat : uint
        {
            None = 0,
            First = 1,
        }

        private enum LongFormat : long
        {
            None = 0,
            First = 1,
        }

        private enum ULongFormat : ulong
        {
            None = 0,
            First = 1,
        }
    }
}
