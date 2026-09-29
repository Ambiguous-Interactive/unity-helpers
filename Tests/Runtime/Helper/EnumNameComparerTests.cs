// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Globalization;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [Category("Fast")]
    public sealed class EnumNameComparerTests
    {
        [TestCase(DayOfWeek.Monday, DayOfWeek.Friday)]
        [TestCase(DayOfWeek.Friday, DayOfWeek.Monday)]
        [TestCase(DayOfWeek.Sunday, DayOfWeek.Sunday)]
        [TestCase((DayOfWeek)(-1), (DayOfWeek)100)]
        public void ComparisonMatchesOrdinalCachedNames(DayOfWeek left, DayOfWeek right)
        {
            Assert.That(
                EnumNameComparer<DayOfWeek>.Instance.Compare(left, right),
                Is.EqualTo(
                    string.Compare(
                        left.ToCachedName(),
                        right.ToCachedName(),
                        StringComparison.Ordinal
                    )
                )
            );
        }

        [Test]
        public void SortingUsesNamesRatherThanNumericValues()
        {
            DayOfWeek[] days = EnumValues<DayOfWeek>.Values.ToArray();
            Array.Sort(days, EnumNameComparer<DayOfWeek>.Instance);
            CollectionAssert.AreEqual(
                new[]
                {
                    DayOfWeek.Friday,
                    DayOfWeek.Monday,
                    DayOfWeek.Saturday,
                    DayOfWeek.Sunday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                },
                days
            );
        }

        [Test]
        public void FlagsCombinationsUseCachedFormattedNames()
        {
            AttributeTargets left = AttributeTargets.Class | AttributeTargets.Method;
            AttributeTargets right = AttributeTargets.Field;
            Assert.That(
                EnumNameComparer<AttributeTargets>.Instance.Compare(left, right),
                Is.EqualTo(
                    string.Compare(
                        left.ToCachedName(),
                        right.ToCachedName(),
                        StringComparison.Ordinal
                    )
                )
            );
        }

        [TestCase("en-US")]
        [TestCase("tr-TR")]
        public void ComparisonDoesNotDependOnCurrentCulture(string cultureName)
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                ComparisonMatchesOrdinalCachedNames(DayOfWeek.Friday, DayOfWeek.Monday);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
