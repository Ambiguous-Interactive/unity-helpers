// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [Category("Fast")]
    public sealed class EnumValuesTests
    {
        private static void AssertRandomSequence<T>(uint seed)
            where T : unmanaged, Enum
        {
            PcgRandom actual = new(seed);
            PcgRandom original = new(seed);
            T[] values = (T[])Enum.GetValues(typeof(T));
            for (int draw = 0; draw < 128; ++draw)
            {
                T expected =
                    values.Length == 2
                        ? original.NextBool()
                            ? values[0]
                            : values[1]
                        : values[original.Next(values.Length)];
                Assert.That(actual.NextEnum<T>(), Is.EqualTo(expected));
            }
        }

        private static void AssertValuesMatchFramework<T>()
            where T : struct, Enum
        {
            CollectionAssert.AreEqual(Enum.GetValues(typeof(T)), EnumValues<T>.Values.ToArray());
        }

        private static AliasedEnum[] CreateAliasedExclusions(int scenario, bool useParamsPath)
        {
            AliasedEnum undefined = (AliasedEnum)12345;
            AliasedEnum[] exclusions = scenario switch
            {
                0 => new[] { AliasedEnum.First, AliasedEnum.Alias, undefined },
                1 => new[] { (AliasedEnum)0, AliasedEnum.Negative, undefined },
                2 => new[] { (AliasedEnum)0, AliasedEnum.First, AliasedEnum.Alias, undefined },
                _ => new[] { (AliasedEnum)0, AliasedEnum.First, AliasedEnum.Negative, undefined },
            };
            if (!useParamsPath)
            {
                return exclusions;
            }
            AliasedEnum[] expanded = new AliasedEnum[exclusions.Length + 2];
            Array.Copy(exclusions, expanded, exclusions.Length);
            expanded[exclusions.Length] = exclusions[0];
            expanded[exclusions.Length + 1] = undefined;
            return expanded;
        }

        [Test]
        public void ValuesRetainAliasesAndUnsignedNumericOrder()
        {
            AssertValuesMatchFramework<AliasedEnum>();
            Assert.That(EnumValues<AliasedEnum>.Values.Length, Is.EqualTo(4));
        }

        [Test]
        public void AliasesCompareEqualByCachedName()
        {
            Assert.That(
                EnumNameComparer<AliasedEnum>.Instance.Compare(
                    AliasedEnum.First,
                    AliasedEnum.Alias
                ),
                Is.Zero
            );
        }

        [Test]
        public void EmptyEnumHasEmptyView()
        {
            Assert.That(EnumValues<EmptyEnum>.Values.IsEmpty, Is.True);
        }

        [Test]
        public void MutatingOwnedCopyDoesNotChangeCachedValues()
        {
            DayOfWeek[] copy = EnumValues<DayOfWeek>.Values.ToArray();
            copy[0] = DayOfWeek.Friday;
            Assert.That(EnumValues<DayOfWeek>.Values[0], Is.EqualTo(DayOfWeek.Sunday));
        }

        [TestCase(1U)]
        [TestCase(42U)]
        [TestCase(12345U)]
        public void RandomEnumSelectionMatchesOriginalArraySelection(uint seed)
        {
            AssertRandomSequence<DayOfWeek>(seed);
            AssertRandomSequence<AliasedEnum>(seed);
            AssertRandomSequence<BinaryEnum>(seed);
        }

        [TestCase(1U)]
        [TestCase(42U)]
        [TestCase(12345U)]
        public void SingletonEnumSelectionDoesNotAdvanceGenerator(uint seed)
        {
            PcgRandom actual = new(seed);
            PcgRandom original = new(seed);
            for (int draw = 0; draw < 128; ++draw)
            {
                Assert.That(actual.NextEnum<SingletonEnum>(), Is.EqualTo(default(SingletonEnum)));
                Assert.That(
                    actual.NextEnumExcept(Array.Empty<SingletonEnum>()),
                    Is.EqualTo(default(SingletonEnum))
                );
            }
            Assert.That(actual.NextUint(), Is.EqualTo(original.NextUint()));
        }

        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        public void AliasedExclusionsPreserveArraySelectionAndGeneratorState(
            int scenario,
            bool useParamsPath
        )
        {
            AliasedEnum[] exclusions = CreateAliasedExclusions(scenario, useParamsPath);
            AliasedEnum[] declaredValues = (AliasedEnum[])Enum.GetValues(typeof(AliasedEnum));
            List<AliasedEnum> allowedValues = new();
            foreach (AliasedEnum value in declaredValues)
            {
                if (Array.IndexOf(exclusions, value) < 0)
                {
                    allowedValues.Add(value);
                }
            }
            Assert.That(allowedValues, Is.Not.Empty);
            PcgRandom actual = new(42U);
            PcgRandom original = new(42U);
            for (int draw = 0; draw < 128; ++draw)
            {
                AliasedEnum expected =
                    allowedValues.Count == 1
                        ? allowedValues[0]
                        : allowedValues[original.Next(allowedValues.Count)];
                Assert.That(actual.NextEnumExcept(exclusions), Is.EqualTo(expected));
                Assert.That(actual.NextUint(), Is.EqualTo(original.NextUint()));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompleteAliasedExclusionsRejectWithoutAdvancingGenerator(bool useParamsPath)
        {
            AliasedEnum[] exclusions = CreateAliasedExclusions(3, useParamsPath);
            foreach (AliasedEnum value in (AliasedEnum[])Enum.GetValues(typeof(AliasedEnum)))
            {
                Assert.That(Array.IndexOf(exclusions, value), Is.AtLeast(0));
            }
            PcgRandom actual = new(42U);
            PcgRandom original = new(42U);
            Assert.Throws<InvalidOperationException>(() => actual.NextEnumExcept(exclusions));
            Assert.That(actual.NextUint(), Is.EqualTo(original.NextUint()));
        }

        [Test]
        public void WarmValuesAndComparerDoNotAllocate()
        {
            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            EnumNameComparer<DayOfWeek> comparer = EnumNameComparer<DayOfWeek>.Instance;
            int total = comparer.Compare(DayOfWeek.Monday, DayOfWeek.Friday);
            total += EnumValues<DayOfWeek>.Values.Length;
            long controlStart = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new byte[1024]);
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - controlStart;
            if (controlBytes == 0)
            {
                Assert.Ignore("The runtime cannot measure managed allocations.");
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 256; ++iteration)
            {
                foreach (DayOfWeek day in EnumValues<DayOfWeek>.Values)
                {
                    total += (int)day;
                }
                total += comparer.Compare(DayOfWeek.Monday, DayOfWeek.Friday);
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(bytes, Is.Zero);
            Assert.That(total, Is.Not.Zero);
        }

        private enum SingletonEnum
        {
            [Obsolete("Reserved zero value.")]
            Unknown = 0,
        }

        private enum BinaryEnum
        {
            [Obsolete("Reserved zero value.")]
            Unknown = 0,
            Second = 1,
        }

        private enum EmptyEnum { }

        private enum AliasedEnum : long
        {
            [Obsolete("Reserved zero value.")]
            Unknown = 0,
            First = 1,
            Alias = 1,
            Negative = long.MinValue,
        }
    }
}
