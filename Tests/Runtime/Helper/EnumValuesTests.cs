// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [Category("Fast")]
    public sealed class EnumValuesTests
    {
        private static IEnumerable<TestCaseData> RuntimeEnumCases()
        {
            Type[] types =
            {
                typeof(DayOfWeek),
                typeof(AttributeTargets),
                typeof(EmptyEnum),
                typeof(SingletonEnum),
                typeof(AliasedEnum),
                typeof(ByteEnum),
                typeof(SignedByteEnum),
                typeof(ShortEnum),
                typeof(UnsignedShortEnum),
                typeof(UnsignedIntEnum),
                typeof(UnsignedLongEnum),
            };
            foreach (Type type in types)
            {
                yield return new TestCaseData(type).SetName(
                    $"RuntimeEnum.{type.Name}.MatchesFramework"
                );
            }
        }

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

        [TestCaseSource(nameof(RuntimeEnumCases))]
        public void RuntimeTypesPreserveFrameworkValuesAndCacheIdentity(Type enumType)
        {
            Array expected = Enum.GetValues(enumType);
            Array actual = EnumValues.Of(enumType);
            CollectionAssert.AreEqual(expected, actual);
            Assert.That(actual.GetType(), Is.EqualTo(expected.GetType()));
            Assert.That(EnumValues.Of(enumType), Is.SameAs(actual));
            Assert.That(EnumValues.TryGetValues(enumType, out Array found), Is.True);
            Assert.That(found, Is.SameAs(actual));
        }

        [Test]
        public void NullRuntimeTypeThrowsWithItsArgumentNameAndTryFailsSoft()
        {
            Type enumType = null;
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
                EnumValues.Of(enumType)
            );
            Assert.That(exception.ParamName, Is.EqualTo(nameof(enumType)));
            Assert.That(EnumValues.TryGetValues(null, out Array values), Is.False);
            Assert.That(values, Is.Null);
        }

        [TestCase(typeof(int))]
        [TestCase(typeof(object))]
        [TestCase(typeof(Enum))]
        [TestCase(typeof(DayOfWeek[]))]
        [TestCase(typeof(DayOfWeek?))]
        [TestCase(typeof(List<>))]
        public void NonEnumRuntimeTypesAreRejectedWithoutPoisoningValidReads(Type enumType)
        {
            for (int attempt = 0; attempt < 2; ++attempt)
            {
                Assert.Throws<ArgumentException>(() => EnumValues.Of(enumType));
                Assert.That(EnumValues.TryGetValues(enumType, out Array invalid), Is.False);
                Assert.That(invalid, Is.Null);
            }
            Assert.That(EnumValues.TryGetValues(typeof(DayOfWeek), out Array valid), Is.True);
            Assert.That(valid, Is.SameAs(EnumValues.Of(typeof(DayOfWeek))));
        }

        [Test]
        public void OpenGenericEnumReadsFollowTheFrameworkContract()
        {
            Type openType = typeof(EnumValuesOpenType<>.Nested);
            Assert.That(openType.IsEnum, Is.True);
            Exception frameworkFailure = null;
            Array expected = null;
            try
            {
                expected = Enum.GetValues(openType);
            }
            catch (Exception exception)
            {
                frameworkFailure = exception;
            }
            if (frameworkFailure == null)
            {
                CollectionAssert.AreEqual(expected, EnumValues.Of(openType));
                Assert.That(EnumValues.TryGetValues(openType, out Array cached), Is.True);
                Assert.That(cached, Is.SameAs(EnumValues.Of(openType)));
                return;
            }
            for (int attempt = 0; attempt < 2; ++attempt)
            {
                Assert.Throws(frameworkFailure.GetType(), () => EnumValues.Of(openType));
                Assert.That(EnumValues.TryGetValues(openType, out Array values), Is.False);
                Assert.That(values, Is.Null);
            }
        }

        [Test]
        public void RuntimeCachePublishesOneArrayAcrossWorkerReads()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Assert.Ignore("WebGL does not provide managed worker threads.");
#else
            Task<Array>[] workers = new Task<Array>[8];
            for (int index = 0; index < workers.Length; ++index)
            {
                workers[index] = Task.Run(static () =>
                {
                    Array first = EnumValues.Of(typeof(ConcurrentEnum));
                    for (int iteration = 0; iteration < 1000; ++iteration)
                    {
                        if (!ReferenceEquals(first, EnumValues.Of(typeof(ConcurrentEnum))))
                        {
                            throw new InvalidOperationException(
                                "Concurrent reads returned different enum arrays."
                            );
                        }
                    }
                    return first;
                });
            }
            Assert.That(Task.WaitAll(workers, 10000), Is.True);
            Array shared = workers[0].Result;
            foreach (Task<Array> worker in workers)
            {
                Assert.That(worker.Result, Is.SameAs(shared));
            }
            CollectionAssert.AreEqual(Enum.GetValues(typeof(ConcurrentEnum)), shared);
#endif
        }

        [Test]
        public void WarmRuntimeTypeReadsDoNotAllocate()
        {
            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            Type enumType = typeof(DayOfWeek);
            Array warm = EnumValues.Of(enumType);
            Assert.That(EnumValues.TryGetValues(enumType, out Array cached), Is.True);
            Assert.That(cached, Is.SameAs(warm));
            long controlStart = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new byte[1024]);
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - controlStart;
            if (controlBytes == 0)
            {
                Assert.Ignore("The runtime cannot measure managed allocations.");
            }
            int total = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 1000; ++iteration)
            {
                total += EnumValues.Of(enumType).Length;
                if (EnumValues.TryGetValues(enumType, out Array values))
                {
                    total += values.Length;
                }
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(bytes, Is.Zero);
            Assert.That(total, Is.EqualTo(warm.Length * 2000));
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

        private enum ByteEnum : byte
        {
            [Obsolete("Reserved zero value.")]
            Unknown = 0,
            First = 1,
            Alias = 1,
            Extreme = byte.MaxValue,
        }

        private enum SignedByteEnum : sbyte
        {
            [Obsolete("Reserved zero value.")]
            Unknown = 0,
            First = 1,
            Alias = 1,
            Extreme = sbyte.MinValue,
        }

        private enum ShortEnum : short
        {
            [Obsolete("Reserved zero value.")]
            Unknown = 0,
            First = 1,
            Alias = 1,
            Extreme = short.MinValue,
        }

        private enum UnsignedShortEnum : ushort
        {
            [Obsolete("Reserved zero value.")]
            Unknown = 0,
            First = 1,
            Alias = 1,
            Extreme = ushort.MaxValue,
        }

        private enum UnsignedIntEnum : uint
        {
            [Obsolete("Reserved zero value.")]
            Unknown = 0,
            First = 1,
            Alias = 1,
            Extreme = uint.MaxValue,
        }

        private enum UnsignedLongEnum : ulong
        {
            [Obsolete("Reserved zero value.")]
            Unknown = 0,
            First = 1,
            Alias = 1,
            Extreme = ulong.MaxValue,
        }

        private enum ConcurrentEnum : long
        {
            [Obsolete("Reserved zero value.")]
            Unknown = 0,
            First = 1,
            Alias = 1,
            Extreme = long.MinValue,
        }
    }
}
