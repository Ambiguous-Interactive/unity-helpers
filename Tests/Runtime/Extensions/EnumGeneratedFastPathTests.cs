// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [Category("Fast")]
    public sealed class EnumGeneratedFastPathTests
    {
        [Test]
        public void GeneratedNameIsSelectedForConcreteCalls()
        {
            Assert.That(GeneratedEnum.Second.ToCachedName(), Is.EqualTo("Second"));
            Assert.That(
                EnumGeneratedFastPathTests_GeneratedEnumWEnumHelpers.ToCachedName(
                    GeneratedEnum.Second
                ),
                Is.EqualTo("Second")
            );
        }

        [Test]
        public void GeneratedDisplayNameIsSelectedForConcreteCalls()
        {
            Assert.That(GeneratedEnum.First.ToDisplayName(), Is.EqualTo("Generated First"));
        }

        [Test]
        public void GeneratedFlagCheckMatchesTheRuntime()
        {
            GeneratedFlags value = GeneratedFlags.First | GeneratedFlags.Third;
            Assert.That(value.HasFlagNoAlloc(GeneratedFlags.First), Is.True);
            Assert.That(value.HasFlagNoAlloc(GeneratedFlags.Second), Is.False);
            Assert.That(value.HasFlagNoAlloc(GeneratedFlags.First | GeneratedFlags.Third), Is.True);
        }

        [Test]
        public void GeneratedFormattingWritesWithoutAllocating()
        {
            char[] buffer = new char[64];
            Assert.That(GeneratedEnum.Second.TryFormatCachedName(buffer, out int written), Is.True);
            Assert.That(new string(buffer, 0, written), Is.EqualTo("Second"));

            GCAssert.DoesNotAllocate(() =>
            {
                GeneratedEnum.Second.TryFormatCachedName(buffer, out _);
            });
        }

        [Test]
        public void GeneratedAliasesUseTheGeneralizedPath()
        {
            GeneratedAliasedEnum value = GeneratedAliasedEnum.First;
            Assert.That(
                value.ToCachedName(),
                Is.EqualTo(EnumNameCache<GeneratedAliasedEnum>.ToCachedName(value))
            );
        }

        [WEnumHelpers]
        internal enum GeneratedEnum
        {
            None = 0,

            [EnumDisplayName("Generated First")]
            First = 1,

            Second = 2,

            Third = 3,
        }

        [Flags]
        [WEnumHelpers]
        internal enum GeneratedFlags
        {
            None = 0,
            First = 1,
            Second = 2,
            Third = 4,
        }

        [WEnumHelpers]
        internal enum GeneratedAliasedEnum
        {
            None = 0,
            First = 1,
            Alias = 1,
        }
    }
}
