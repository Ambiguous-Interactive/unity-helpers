// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf.Meta;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using PackageSerializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [Category("Serialization")]
    public sealed class NullableMapSerializationTests
    {
        private const int Interpreted = 0;
        private const int AutoCompiled = 1;
        private const int CompiledInPlace = 2;
        private const int StandaloneCompiled = 3;
        private const int ForeignSerializer = 4;
        private const int PackageFacade = 5;
        private const int ClrDictionary = 4;

        internal static IEnumerable<TestCaseData> ValueCases()
        {
            return PresenceCases(false);
        }

        internal static IEnumerable<TestCaseData> KeyCases()
        {
            return PresenceCases(true);
        }

        internal static IEnumerable<TestCaseData> AmbiguousCases()
        {
            return HistoricalCases(nameof(HistoricalMissingValueCannotRecoverPresentDefault));
        }

        internal static IEnumerable<TestCaseData> ScalarCases()
        {
            return HistoricalCases(nameof(NonnullableScalarDefaultRetainsHistoricalBytes));
        }

        internal static IEnumerable<TestCaseData> IntrinsicCases()
        {
            for (int mode = Interpreted; mode <= PackageFacade; ++mode)
            {
                for (int valueType = 1; valueType < 16; ++valueType)
                {
                    foreach (bool nested in new[] { false, true })
                    {
                        yield return new TestCaseData(mode, valueType, nested).SetName(
                            $"NullableMap.Intrinsic.Mode{mode}.Value{valueType}.Nested{nested}"
                        );
                    }
                }
            }
        }

        internal static IEnumerable<TestCaseData> StandaloneBoundaryCases()
        {
            for (int family = 0; family < ClrDictionary; ++family)
            {
                foreach (bool nested in new[] { false, true })
                {
                    yield return new TestCaseData(family, nested).SetName(
                        $"NullableMap.StandaloneBoundary.Family{family}.Nested{nested}"
                    );
                }
            }
        }

        internal static IEnumerable<TestCaseData> SortedKeyCases()
        {
            foreach (int family in new[] { 1, 3 })
            {
                for (int mode = Interpreted; mode <= PackageFacade; ++mode)
                {
                    if (mode == StandaloneCompiled)
                    {
                        continue;
                    }
                    foreach (bool nested in new[] { false, true })
                    {
                        yield return new TestCaseData(family, mode, nested).SetName(
                            $"NullableMap.SortedDefaultKey.Family{family}.Mode{mode}.Nested{nested}"
                        );
                    }
                }
            }
        }

        internal static IEnumerable<TestCaseData> EdgeCases()
        {
            for (int family = 0; family <= ClrDictionary; ++family)
            {
                for (int mode = Interpreted; mode <= AutoCompiled; ++mode)
                {
                    foreach (bool nested in new[] { false, true })
                    {
                        foreach (int count in new[] { 0, 1, 10000 })
                        {
                            yield return new TestCaseData(family, mode, nested, count).SetName(
                                $"NullableMap.Edge.Family{family}.Mode{mode}.Nested{nested}.Count{count}"
                            );
                        }
                    }
                }
            }
        }

        internal static IEnumerable<TestCaseData> FacadeCases()
        {
            for (int family = 0; family <= ClrDictionary; ++family)
            {
                for (int valueType = 0; valueType < 3; ++valueType)
                {
                    yield return new TestCaseData(family, valueType).SetName(
                        $"NullableMap.EqualityHash.Family{family}.Value{valueType}"
                    );
                }
            }
        }

        private static IEnumerable<TestCaseData> PresenceCases(bool keys)
        {
            for (int family = 0; family <= ClrDictionary; ++family)
            {
                if (keys && (family == 1 || family == 3))
                {
                    continue;
                }
                for (int mode = Interpreted; mode <= PackageFacade; ++mode)
                {
                    if (family != ClrDictionary && mode == StandaloneCompiled)
                    {
                        continue;
                    }
                    for (int valueType = 0; valueType < 3; ++valueType)
                    {
                        foreach (bool nested in new[] { false, true })
                        {
                            yield return new TestCaseData(family, mode, valueType, nested).SetName(
                                $"NullableMap.{(keys ? "Keys" : "Values")}.Family{family}.Mode{mode}.Value{valueType}.Nested{nested}"
                            );
                        }
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> HistoricalCases(string operation)
        {
            for (int mode = Interpreted; mode <= PackageFacade; ++mode)
            {
                foreach (bool nested in new[] { false, true })
                {
                    yield return new TestCaseData(mode, nested).SetName(
                        $"NullableMap.{operation}.Mode{mode}.Nested{nested}"
                    );
                }
            }
        }

        private static TypeModel CreateModel<TMap>(int mode, bool nested)
            where TMap : new()
        {
#if ENABLE_IL2CPP
            if (mode == AutoCompiled || mode == CompiledInPlace || mode == StandaloneCompiled)
            {
                Assert.Ignore("This model mode emits dynamic IL and is unsupported by IL2CPP.");
            }
#endif
            if (mode == ForeignSerializer || mode == PackageFacade)
            {
                return null;
            }
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            model.AutoCompile = mode == AutoCompiled;
            model.Add(nested ? typeof(NullableMapContainer<TMap>) : typeof(TMap), true);
            if (mode == CompiledInPlace)
            {
                model.CompileInPlace();
            }
            return mode == StandaloneCompiled ? model.Compile() : model;
        }

        private static byte[] Encode<T>(TypeModel model, int mode, T value)
        {
            if (mode == PackageFacade)
            {
                return PackageSerializer.ProtoSerialize(value);
            }
            using MemoryStream stream = new MemoryStream();
            if (mode == ForeignSerializer)
            {
                ProtoBuf.Serializer.NonGeneric.Serialize(stream, value);
            }
            else
            {
                model.Serialize(stream, value);
            }
            return stream.ToArray();
        }

        private static T Decode<T>(TypeModel model, int mode, byte[] bytes)
        {
            if (mode == PackageFacade)
            {
                return PackageSerializer.ProtoDeserialize<T>(bytes);
            }
            using MemoryStream stream = new MemoryStream(bytes);
            if (mode == ForeignSerializer)
            {
                return (T)ProtoBuf.Serializer.NonGeneric.Deserialize(typeof(T), stream);
            }
            return (T)model.Deserialize(stream, null, typeof(T));
        }

        private static byte[] EncodeMap<TMap>(TypeModel model, int mode, bool nested, TMap map)
            where TMap : new()
        {
            return nested
                ? Encode(model, mode, new NullableMapContainer<TMap> { Entries = map })
                : Encode(model, mode, map);
        }

        private static TMap DecodeMap<TMap>(TypeModel model, int mode, bool nested, byte[] bytes)
            where TMap : new()
        {
            return nested
                ? Decode<NullableMapContainer<TMap>>(model, mode, bytes).Entries
                : Decode<TMap>(model, mode, bytes);
        }

        private static void AssertValues<TMap, TValue>(int mode, bool nested, TValue nondefault)
            where TMap : IDictionary<int, TValue?>, new()
            where TValue : struct
        {
            TypeModel model = CreateModel<TMap>(mode, nested);
            TMap input = new TMap();
            input.Add(2, null);
            input.Add(3, default(TValue));
            input.Add(4, nondefault);
            byte[] bytes = EncodeMap(model, mode, nested, input);
            if (typeof(TValue) == typeof(NullableMapValue))
            {
                TMap nondefaultOnly = new TMap();
                nondefaultOnly.Add(4, nondefault);
                bool rootStructWrapper =
                    mode == PackageFacade
                    && !nested
                    && (
                        typeof(TMap) == typeof(SerializableDictionary<int, TValue?>)
                        || typeof(TMap) == typeof(SerializableSortedDictionary<int, TValue?>)
                    );
                byte[] fixedBytes = Convert.FromBase64String(
                    rootStructWrapper ? "CAQSAggH"
                    : nested ? "OgYIBBICCAc="
                    : "CgYIBBICCAc="
                );
                CollectionAssert.AreEqual(
                    fixedBytes,
                    EncodeMap(model, mode, nested, nondefaultOnly)
                );
                TMap fixedRestored = DecodeMap<TMap>(model, mode, nested, fixedBytes);
                Assert.IsTrue(fixedRestored.TryGetValue(4, out TValue? fixedValue));
                Assert.IsTrue(fixedValue.HasValue);
                Assert.AreEqual(
                    ((NullableMapValue)(object)nondefault).Number,
                    ((NullableMapValue)(object)fixedValue.Value).Number
                );
            }
            TMap restored = DecodeMap<TMap>(model, mode, nested, bytes);
            Assert.AreEqual(3, restored.Count);
            Assert.IsTrue(restored.TryGetValue(2, out TValue? absent));
            Assert.IsFalse(absent.HasValue);
            Assert.IsTrue(restored.TryGetValue(3, out TValue? presentDefault));
            Assert.IsTrue(presentDefault.HasValue);
            Assert.AreEqual(default(TValue), presentDefault.Value);
            Assert.IsTrue(restored.TryGetValue(4, out TValue? presentNondefault));
            Assert.IsTrue(presentNondefault.HasValue);
            Assert.AreEqual(nondefault, presentNondefault.Value);
            TMap absentOnly = new TMap();
            absentOnly.Add(3, null);
            TMap defaultOnly = new TMap();
            defaultOnly.Add(3, default(TValue));
            byte[] absentBytes = EncodeMap(model, mode, nested, absentOnly);
            byte[] presentBytes = EncodeMap(model, mode, nested, defaultOnly);
            CollectionAssert.AreNotEqual(absentBytes, presentBytes);
            bool rootWrapper =
                mode == PackageFacade
                && !nested
                && (
                    typeof(TMap) == typeof(SerializableDictionary<int, TValue?>)
                    || typeof(TMap) == typeof(SerializableSortedDictionary<int, TValue?>)
                );
            if (typeof(TValue) == typeof(int) || typeof(TValue) == typeof(NullableMapEnum))
            {
                CollectionAssert.AreEqual(
                    Convert.FromBase64String(
                        rootWrapper ? "CAMQAA=="
                        : nested ? "OgQIAxAA"
                        : "CgQIAxAA"
                    ),
                    presentBytes
                );
                CollectionAssert.AreEqual(
                    Convert.FromBase64String(
                        rootWrapper ? "CAMaAQA="
                        : nested ? "OgIIAw=="
                        : "CgIIAw=="
                    ),
                    absentBytes
                );
            }
            else if (typeof(TValue) == typeof(NullableMapValue))
            {
                CollectionAssert.AreEqual(
                    Convert.FromBase64String(
                        rootWrapper ? "CAMSAA=="
                        : nested ? "OgQIAxIA"
                        : "CgQIAxIA"
                    ),
                    presentBytes
                );
            }
        }

        private static void AssertKeys<TMap, TKey>(int mode, bool nested, TKey nondefault)
            where TMap : IDictionary<TKey?, int>, new()
            where TKey : struct
        {
            TypeModel model = CreateModel<TMap>(mode, nested);
            TMap input = new TMap();
            input.Add(default(TKey), 11);
            input.Add(nondefault, 19);
            if (typeof(TKey) == typeof(NullableMapValue))
            {
                TMap nondefaultOnly = new TMap();
                nondefaultOnly.Add(nondefault, 19);
                bool rootStructWrapper =
                    mode == PackageFacade
                    && !nested
                    && typeof(TMap) == typeof(SerializableDictionary<TKey?, int>);
                byte[] fixedBytes = Convert.FromBase64String(
                    rootStructWrapper ? "CgIIBxAT"
                    : nested ? "OgYKAggHEBM="
                    : "CgYKAggHEBM="
                );
                CollectionAssert.AreEqual(
                    fixedBytes,
                    EncodeMap(model, mode, nested, nondefaultOnly)
                );
                TMap fixedRestored = DecodeMap<TMap>(model, mode, nested, fixedBytes);
                Assert.AreEqual(1, fixedRestored.Count);
                foreach (KeyValuePair<TKey?, int> entry in fixedRestored)
                {
                    Assert.IsTrue(entry.Key.HasValue);
                    Assert.AreEqual(
                        ((NullableMapValue)(object)nondefault).Number,
                        ((NullableMapValue)(object)entry.Key.Value).Number
                    );
                    Assert.AreEqual(19, entry.Value);
                }
            }
            TMap restored = DecodeMap<TMap>(
                model,
                mode,
                nested,
                EncodeMap(model, mode, nested, input)
            );
            Assert.AreEqual(2, restored.Count);
            Assert.IsTrue(restored.TryGetValue(default(TKey), out int defaultValue));
            Assert.AreEqual(11, defaultValue);
            Assert.IsTrue(restored.TryGetValue(nondefault, out int nondefaultValue));
            Assert.AreEqual(19, nondefaultValue);
            TMap defaultOnly = new TMap();
            defaultOnly.Add(default(TKey), 11);
            string expected =
                typeof(TKey) == typeof(NullableMapValue)
                    ? nested
                        ? "OgQKABAL"
                        : "CgQKABAL"
                    : nested
                        ? "OgQIABAL"
                        : "CgQIABAL";
            if (
                mode == PackageFacade
                && !nested
                && typeof(TMap) == typeof(SerializableDictionary<TKey?, int>)
            )
            {
                expected = typeof(TKey) == typeof(NullableMapValue) ? "CgAQCw==" : "CAAQCw==";
            }
            CollectionAssert.AreEqual(
                Convert.FromBase64String(expected),
                EncodeMap(model, mode, nested, defaultOnly)
            );
        }

        private static void AssertEquality<TMap, TValue>()
            where TMap : IDictionary<int, TValue?>, new()
            where TValue : struct
        {
            TMap absent = new TMap();
            absent.Add(3, null);
            TMap present = new TMap();
            present.Add(3, default(TValue));
            TMap equalPresent = new TMap();
            equalPresent.Add(3, default(TValue));
            Assert.IsFalse(absent.ProtoEquals(present));
            Assert.IsTrue(present.ProtoEquals(equalPresent));
            ProtoEqualityComparer<TMap> comparer = ProtoEqualityComparer<TMap>.Instance;
            Assert.AreEqual(comparer.GetHashCode(present), comparer.GetHashCode(equalPresent));
            Assert.AreNotEqual(comparer.GetHashCode(absent), comparer.GetHashCode(present));
        }

        private static void AssertEdgeMap<TMap>(int mode, bool nested, int count)
            where TMap : IDictionary<int, int?>, new()
        {
            TypeModel model = CreateModel<TMap>(mode, nested);
            TMap input = new TMap();
            for (int i = 0; i < count; ++i)
            {
                input.Add(
                    i,
                    i % 3 == 0 ? null
                        : i % 3 == 1 ? 0
                        : -i
                );
            }
            TMap restored = DecodeMap<TMap>(
                model,
                mode,
                nested,
                EncodeMap(model, mode, nested, input)
            );
            Assert.IsTrue(restored != null);
            Assert.AreEqual(count, restored.Count);
            foreach (KeyValuePair<int, int?> pair in input)
            {
                Assert.IsTrue(restored.TryGetValue(pair.Key, out int? actual));
                Assert.AreEqual(pair.Value.HasValue, actual.HasValue);
                Assert.AreEqual(pair.Value, actual);
            }
        }

        private static void AssertSortedDefaultKey<TMap>(int mode, bool nested)
            where TMap : IDictionary<int, int>, new()
        {
            TypeModel model = CreateModel<TMap>(mode, nested);
            TMap input = new TMap();
            input.Add(0, 11);
            byte[] bytes = EncodeMap(model, mode, nested, input);
            string expected = nested ? "OgIQCw==" : "CgIQCw==";
            if (
                mode == PackageFacade
                && !nested
                && typeof(TMap) == typeof(SerializableSortedDictionary<int, int>)
            )
            {
                expected = "CgEAEgEL";
            }
            CollectionAssert.AreEqual(Convert.FromBase64String(expected), bytes);
            TMap restored = DecodeMap<TMap>(model, mode, nested, bytes);
            Assert.AreEqual(1, restored.Count);
            Assert.IsTrue(restored.TryGetValue(0, out int actual));
            Assert.AreEqual(11, actual);
        }

        private static void AssertStandaloneBoundary<TMap>(bool nested)
            where TMap : new()
        {
#if ENABLE_IL2CPP
            Assert.Ignore("Standalone Compile emits dynamic IL and is unsupported by IL2CPP.");
#endif
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            {
                CreateModel<TMap>(StandaloneCompiled, nested);
            });
            StringAssert.Contains("non-public", exception.Message.ToLowerInvariant());
        }

        private static void RunValues<TValue>(int family, int mode, bool nested, TValue nondefault)
            where TValue : struct
        {
            switch (family)
            {
                case 0:
                    AssertValues<SerializableDictionary<int, TValue?>, TValue>(
                        mode,
                        nested,
                        nondefault
                    );
                    break;
                case 1:
                    AssertValues<SerializableSortedDictionary<int, TValue?>, TValue>(
                        mode,
                        nested,
                        nondefault
                    );
                    break;
                case 2:
                    AssertValues<
                        SerializableDictionary<int, TValue?, SerializableDictionary.Cache<TValue?>>,
                        TValue
                    >(mode, nested, nondefault);
                    break;
                case 3:
                    AssertValues<
                        SerializableSortedDictionary<
                            int,
                            TValue?,
                            SerializableDictionary.Cache<TValue?>
                        >,
                        TValue
                    >(mode, nested, nondefault);
                    break;
                case 4:
                    AssertValues<Dictionary<int, TValue?>, TValue>(mode, nested, nondefault);
                    break;
                default:
                    Assert.Fail("Unknown dictionary family.");
                    break;
            }
        }

        private static void RunKeys<TKey>(int family, int mode, bool nested, TKey nondefault)
            where TKey : struct
        {
            switch (family)
            {
                case 0:
                    AssertKeys<SerializableDictionary<TKey?, int>, TKey>(mode, nested, nondefault);
                    break;
                case 2:
                    AssertKeys<
                        SerializableDictionary<TKey?, int, SerializableDictionary.Cache<int>>,
                        TKey
                    >(mode, nested, nondefault);
                    break;
                case 4:
                    AssertKeys<Dictionary<TKey?, int>, TKey>(mode, nested, nondefault);
                    break;
                default:
                    Assert.Fail("Unknown dictionary family.");
                    break;
            }
        }

        private static void RunEquality<TValue>(int family)
            where TValue : struct
        {
            switch (family)
            {
                case 0:
                    AssertEquality<SerializableDictionary<int, TValue?>, TValue>();
                    break;
                case 1:
                    AssertEquality<SerializableSortedDictionary<int, TValue?>, TValue>();
                    break;
                case 2:
                    AssertEquality<
                        SerializableDictionary<int, TValue?, SerializableDictionary.Cache<TValue?>>,
                        TValue
                    >();
                    break;
                case 3:
                    AssertEquality<
                        SerializableSortedDictionary<
                            int,
                            TValue?,
                            SerializableDictionary.Cache<TValue?>
                        >,
                        TValue
                    >();
                    break;
                case 4:
                    AssertEquality<Dictionary<int, TValue?>, TValue>();
                    break;
                default:
                    Assert.Fail("Unknown dictionary family.");
                    break;
            }
        }

        private static void AssertWrapped<TValue>(
            int mode,
            TValue empty,
            TValue nonempty,
            string historical
        )
            where TValue : class
        {
            TypeModel model = CreateModel<WrappedNullableMapContainer<TValue>>(mode, false);
            WrappedNullableMapContainer<TValue> input = new WrappedNullableMapContainer<TValue>
            {
                Entries = new Dictionary<int, TValue>
                {
                    { 2, null },
                    { 3, empty },
                    { 4, nonempty },
                },
            };
            byte[] bytes = Encode(model, mode, input);
            CollectionAssert.AreEqual(Convert.FromBase64String(historical), bytes);
            WrappedNullableMapContainer<TValue> restored = Decode<
                WrappedNullableMapContainer<TValue>
            >(model, mode, bytes);
            Assert.AreEqual(3, restored.Entries.Count);
            Assert.IsTrue(restored.Entries.TryGetValue(2, out TValue absent));
            Assert.IsTrue(absent == null);
            Assert.IsTrue(restored.Entries.TryGetValue(3, out TValue actualEmpty));
            Assert.IsTrue(restored.Entries.TryGetValue(4, out TValue actualNonempty));
            if (empty is byte[] expectedEmpty && nonempty is byte[] expectedNonempty)
            {
                CollectionAssert.AreEqual(expectedEmpty, actualEmpty as byte[]);
                CollectionAssert.AreEqual(expectedNonempty, actualNonempty as byte[]);
            }
            else
            {
                Assert.AreEqual(empty, actualEmpty);
                Assert.AreEqual(nonempty, actualNonempty);
            }
            CollectionAssert.AreEqual(bytes, Encode(model, mode, restored));
        }

        [TestCaseSource(nameof(IntrinsicCases))]
        public void IntrinsicNullableValuesPreserveAllThreeStates(
            int mode,
            int valueType,
            bool nested
        )
        {
            switch (valueType)
            {
                case 1:
                    RunValues(ClrDictionary, mode, nested, (uint)7);
                    break;
                case 2:
                    RunValues(ClrDictionary, mode, nested, (long)-7);
                    break;
                case 3:
                    RunValues(ClrDictionary, mode, nested, (ulong)7);
                    break;
                case 4:
                    RunValues(ClrDictionary, mode, nested, (short)-7);
                    break;
                case 5:
                    RunValues(ClrDictionary, mode, nested, (ushort)7);
                    break;
                case 6:
                    RunValues(ClrDictionary, mode, nested, (byte)7);
                    break;
                case 7:
                    RunValues(ClrDictionary, mode, nested, (sbyte)-7);
                    break;
                case 8:
                    RunValues(ClrDictionary, mode, nested, 'x');
                    break;
                case 9:
                    RunValues(ClrDictionary, mode, nested, true);
                    break;
                case 10:
                    RunValues(ClrDictionary, mode, nested, -7.25f);
                    break;
                case 11:
                    RunValues(ClrDictionary, mode, nested, -7.25d);
                    break;
                case 12:
                    RunValues(ClrDictionary, mode, nested, -7.25m);
                    break;
                case 13:
                    RunValues(
                        ClrDictionary,
                        mode,
                        nested,
                        Guid.Parse("00000000-0000-0000-0000-000000000007")
                    );
                    break;
                case 14:
                    RunValues(ClrDictionary, mode, nested, new DateTime(2020, 1, 2));
                    break;
                case 15:
                    RunValues(ClrDictionary, mode, nested, TimeSpan.FromSeconds(7));
                    break;
                default:
                    Assert.Fail("Unknown intrinsic value type.");
                    break;
            }
        }

        [TestCaseSource(nameof(ValueCases))]
        public void NullableValuesPreserveAbsentPresentDefaultAndNondefault(
            int family,
            int mode,
            int valueType,
            bool nested
        )
        {
            switch (valueType)
            {
                case 0:
                    RunValues(family, mode, nested, -7);
                    break;
                case 1:
                    RunValues(family, mode, nested, NullableMapEnum.Nondefault);
                    break;
                default:
                    RunValues(family, mode, nested, new NullableMapValue { Number = 7 });
                    break;
            }
        }

        [TestCaseSource(nameof(KeyCases))]
        public void NullableDefaultKeysRemainPresent(
            int family,
            int mode,
            int valueType,
            bool nested
        )
        {
            switch (valueType)
            {
                case 0:
                    RunKeys(family, mode, nested, -7);
                    break;
                case 1:
                    RunKeys(family, mode, nested, NullableMapEnum.Nondefault);
                    break;
                default:
                    RunKeys(family, mode, nested, new NullableMapValue { Number = 7 });
                    break;
            }
        }

        [TestCaseSource(nameof(EdgeCases))]
        public void EmptySingleAndLargeMapsPreserveNullableValues(
            int family,
            int mode,
            bool nested,
            int count
        )
        {
            switch (family)
            {
                case 0:
                    AssertEdgeMap<SerializableDictionary<int, int?>>(mode, nested, count);
                    break;
                case 1:
                    AssertEdgeMap<SerializableSortedDictionary<int, int?>>(mode, nested, count);
                    break;
                case 2:
                    AssertEdgeMap<
                        SerializableDictionary<int, int?, SerializableDictionary.Cache<int?>>
                    >(mode, nested, count);
                    break;
                case 3:
                    AssertEdgeMap<
                        SerializableSortedDictionary<int, int?, SerializableDictionary.Cache<int?>>
                    >(mode, nested, count);
                    break;
                case 4:
                    AssertEdgeMap<Dictionary<int, int?>>(mode, nested, count);
                    break;
                default:
                    Assert.Fail("Unknown dictionary family.");
                    break;
            }
        }

        [TestCaseSource(nameof(SortedKeyCases))]
        public void SortedNonnullableDefaultKeysRetainHistoricalOmission(
            int family,
            int mode,
            bool nested
        )
        {
            if (family == 1)
            {
                AssertSortedDefaultKey<SerializableSortedDictionary<int, int>>(mode, nested);
            }
            else
            {
                AssertSortedDefaultKey<
                    SerializableSortedDictionary<int, int, SerializableDictionary.Cache<int>>
                >(mode, nested);
            }
        }

        [TestCaseSource(nameof(StandaloneBoundaryCases))]
        public void OwnedStandaloneCompilationRetainsPrivateMemberBoundary(int family, bool nested)
        {
            switch (family)
            {
                case 0:
                    AssertStandaloneBoundary<SerializableDictionary<int, int?>>(nested);
                    break;
                case 1:
                    AssertStandaloneBoundary<SerializableSortedDictionary<int, int?>>(nested);
                    break;
                case 2:
                    AssertStandaloneBoundary<
                        SerializableDictionary<int, int?, SerializableDictionary.Cache<int?>>
                    >(nested);
                    break;
                case 3:
                    AssertStandaloneBoundary<
                        SerializableSortedDictionary<int, int?, SerializableDictionary.Cache<int?>>
                    >(nested);
                    break;
                default:
                    Assert.Fail("Unknown owned dictionary family.");
                    break;
            }
        }

        [TestCaseSource(nameof(FacadeCases))]
        public void EqualityAndHashDistinguishNullablePresence(int family, int valueType)
        {
            switch (valueType)
            {
                case 0:
                    RunEquality<int>(family);
                    break;
                case 1:
                    RunEquality<NullableMapEnum>(family);
                    break;
                default:
                    RunEquality<NullableMapValue>(family);
                    break;
            }
        }

        [TestCaseSource(nameof(AmbiguousCases))]
        public void HistoricalMissingValueCannotRecoverPresentDefault(int mode, bool nested)
        {
            TypeModel model = CreateModel<Dictionary<int, int?>>(mode, nested);
            byte[] ambiguous = Convert.FromBase64String(nested ? "OgIIAw==" : "CgIIAw==");
            Dictionary<int, int?> restored = DecodeMap<Dictionary<int, int?>>(
                model,
                mode,
                nested,
                ambiguous
            );
            Assert.AreEqual(1, restored.Count);
            Assert.IsTrue(restored.TryGetValue(3, out int? value));
            Assert.IsFalse(value.HasValue);
        }

        [TestCaseSource(nameof(ScalarCases))]
        public void NonnullableScalarDefaultRetainsHistoricalBytes(int mode, bool nested)
        {
            TypeModel model = CreateModel<Dictionary<int, int>>(mode, nested);
            Dictionary<int, int> input = new Dictionary<int, int> { { 3, 0 } };
            byte[] bytes = EncodeMap(model, mode, nested, input);
            CollectionAssert.AreEqual(
                Convert.FromBase64String(nested ? "OgIIAw==" : "CgIIAw=="),
                bytes
            );
            Dictionary<int, int> restored = DecodeMap<Dictionary<int, int>>(
                model,
                mode,
                nested,
                bytes
            );
            Assert.AreEqual(1, restored.Count);
            Assert.IsTrue(restored.TryGetValue(3, out int value));
            Assert.AreEqual(0, value);
        }

        [TestCase(0, false, TestName = "NullableMap.WrappedReference.Mode0.String")]
        [TestCase(1, false, TestName = "NullableMap.WrappedReference.Mode1.String")]
        [TestCase(2, false, TestName = "NullableMap.WrappedReference.Mode2.String")]
        [TestCase(3, false, TestName = "NullableMap.WrappedReference.Mode3.String")]
        [TestCase(4, false, TestName = "NullableMap.WrappedReference.Mode4.String")]
        [TestCase(5, false, TestName = "NullableMap.WrappedReference.Mode5.String")]
        [TestCase(0, true, TestName = "NullableMap.WrappedReference.Mode0.Bytes")]
        [TestCase(1, true, TestName = "NullableMap.WrappedReference.Mode1.Bytes")]
        [TestCase(2, true, TestName = "NullableMap.WrappedReference.Mode2.Bytes")]
        [TestCase(3, true, TestName = "NullableMap.WrappedReference.Mode3.Bytes")]
        [TestCase(4, true, TestName = "NullableMap.WrappedReference.Mode4.Bytes")]
        [TestCase(5, true, TestName = "NullableMap.WrappedReference.Mode5.Bytes")]
        public void WrappedReferenceValuesRetainHistoricalBytes(int mode, bool bytesValue)
        {
            if (bytesValue)
            {
                AssertWrapped(
                    mode,
                    Array.Empty<byte>(),
                    new byte[] { 7 },
                    "OgIIAjoGCAMSAgoAOgcIBBIDCgEH"
                );
            }
            else
            {
                AssertWrapped(
                    mode,
                    string.Empty,
                    "nonempty",
                    "OgIIAjoGCAMSAgoAOg4IBBIKCghub25lbXB0eQ=="
                );
            }
        }
    }
}
