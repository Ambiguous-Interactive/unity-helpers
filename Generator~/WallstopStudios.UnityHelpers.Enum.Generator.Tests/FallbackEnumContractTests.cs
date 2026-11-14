// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Enum.Generator.Tests
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    public sealed class FallbackEnumContractTests
    {
        [Test]
        public void SparseUniqueNamesMatchTheRuntime()
        {
            SparseUnique[] values = (SparseUnique[])Enum.GetValues(typeof(SparseUnique));
            string[] names = Enum.GetNames(typeof(SparseUnique));
            Assert.That(values.Length, Is.EqualTo(names.Length));
            for (int index = 0; index < values.Length; ++index)
            {
                Assert.That(
                    EnumNameCache<SparseUnique>.ToCachedName(values[index]),
                    Is.EqualTo(names[index])
                );
            }
        }

        [Test]
        public void SparseAliasNamesMatchTheRuntime()
        {
            Assert.That(
                EnumNameCache<SparseAlias>.ToCachedName(SparseAlias.First),
                Is.EqualTo(((SparseAlias)1000).ToString())
            );
            Assert.That(
                EnumNameCache<SparseAlias>.ToCachedName(SparseAlias.Last),
                Is.EqualTo(nameof(SparseAlias.Last))
            );
            Assert.That(
                EnumNameCache<SparseAlias>.ToCachedName((SparseAlias)1500),
                Is.EqualTo(((SparseAlias)1500).ToString())
            );
        }

        [Test]
        public void SparseAliasParsingKeepsEveryDeclaredName()
        {
            Assert.That(
                EnumNameCache<SparseAlias>.TryParse("First".AsSpan(), out SparseAlias first),
                Is.True
            );
            Assert.That(first, Is.EqualTo(SparseAlias.First));
            Assert.That(
                EnumNameCache<SparseAlias>.TryParse("Alias".AsSpan(), out SparseAlias alias),
                Is.True
            );
            Assert.That(alias, Is.EqualTo(SparseAlias.Alias));
        }

        [Test]
        public void SparseMembershipMatchesTheRuntime()
        {
            Assert.That(EnumValues<SparseAlias>.IsDefined(SparseAlias.First), Is.True);
            Assert.That(EnumValues<SparseAlias>.IsDefined((SparseAlias)1500), Is.False);
        }

        [Test]
        public void SparseFormattingMatchesTheCachedName()
        {
            string expected = EnumNameCache<SparseAlias>.ToCachedName(SparseAlias.First);
            char[] buffer = new char[expected.Length];
            Assert.That(SparseAlias.First.TryFormatCachedName(buffer, out int written), Is.True);
            Assert.That(new string(buffer, 0, written), Is.EqualTo(expected));
        }

        private enum SparseUnique
        {
            None = 0,
            First = 1000,
            Second = 2000,
        }

        private enum SparseAlias
        {
            None = 0,
            First = 1000,
            Alias = 1000,
            Last = 2000,
        }
    }
}
