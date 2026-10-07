// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf.Meta;

    [TestFixture]
    [Category("Serialization")]
    public sealed class NullableMapExternalSerializerTests
    {
        private const int UnderlyingProvider = 0;
        private const int DualProvider = 1;
        private const int FactoryProvider = 2;
        private const int Interpreted = 0;
        private const int AutoCompiled = 1;
        private const int CompiledInPlace = 2;
        private const int StandaloneCompiled = 3;

        private static IEnumerable<TestCaseData> ExternalCases()
        {
            for (int provider = UnderlyingProvider; provider <= FactoryProvider; ++provider)
            {
                for (int mode = Interpreted; mode <= StandaloneCompiled; ++mode)
                {
                    foreach (bool nested in new[] { false, true })
                    {
                        foreach (bool keys in new[] { false, true })
                        {
                            yield return new TestCaseData(provider, mode, nested, keys).SetName(
                                $"NullableMap.External.Provider{provider}.Mode{mode}.Nested{nested}.Keys{keys}"
                            );
                        }
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> WarmCases()
        {
            for (int provider = UnderlyingProvider; provider <= FactoryProvider; ++provider)
            {
                foreach (bool nested in new[] { false, true })
                {
                    foreach (bool keys in new[] { false, true })
                    {
                        yield return new TestCaseData(provider, nested, keys).SetName(
                            $"NullableMap.External.Warm.Provider{provider}.Nested{nested}.Keys{keys}"
                        );
                    }
                }
            }
        }

        private static RuntimeTypeModel CreateModel(int provider, int mode, bool nested, bool keys)
        {
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            model.AutoCompile = mode == AutoCompiled;
            Type serializerType;
            switch (provider)
            {
                case UnderlyingProvider:
                    serializerType = typeof(NullableMapUnderlyingProvider);
                    break;
                case DualProvider:
                    serializerType = typeof(NullableMapDualProvider);
                    break;
                default:
                    serializerType = typeof(NullableMapFactoryProvider);
                    break;
            }
            model.Add(typeof(NullableMapValue), false).SerializerType = serializerType;
            Type rootType;
            if (keys)
            {
                rootType = nested
                    ? typeof(NullableMapContainer<Dictionary<NullableMapValue?, int>>)
                    : typeof(Dictionary<NullableMapValue?, int>);
            }
            else
            {
                rootType = nested
                    ? typeof(NullableMapContainer<Dictionary<int, NullableMapValue?>>)
                    : typeof(Dictionary<int, NullableMapValue?>);
            }
            model.Add(rootType, true);
            return model;
        }

        private static void ResetCounts()
        {
            NullableMapUnderlyingProvider.Writes = 0;
            NullableMapDualService.UnderlyingWrites = 0;
            NullableMapDualService.NullableWrites = 0;
        }

        private static byte[] VerifyValues(TypeModel model, int provider, bool nested)
        {
            Dictionary<int, NullableMapValue?> input = new Dictionary<int, NullableMapValue?>
            {
                { 1, null },
                { 2, default(NullableMapValue) },
                {
                    3,
                    new NullableMapValue { Number = 7 }
                },
            };
            ResetCounts();
            using MemoryStream stream = new MemoryStream();
            if (nested)
            {
                model.Serialize(
                    stream,
                    new NullableMapContainer<Dictionary<int, NullableMapValue?>> { Entries = input }
                );
            }
            else
            {
                model.Serialize(stream, input);
            }
            AssertCounts(provider);
            stream.Position = 0;
            Dictionary<int, NullableMapValue?> restored = nested
                ? model
                    .Deserialize<NullableMapContainer<Dictionary<int, NullableMapValue?>>>(stream)
                    .Entries
                : model.Deserialize<Dictionary<int, NullableMapValue?>>(stream);
            Assert.AreEqual(3, restored.Count);
            Assert.IsTrue(restored.TryGetValue(1, out NullableMapValue? nullValue));
            Assert.IsFalse(nullValue.HasValue);
            Assert.IsTrue(restored.TryGetValue(2, out NullableMapValue? defaultValue));
            Assert.IsTrue(defaultValue.HasValue);
            Assert.AreEqual(default(NullableMapValue), defaultValue.Value);
            Assert.IsTrue(restored.TryGetValue(3, out NullableMapValue? nondefaultValue));
            Assert.IsTrue(nondefaultValue.HasValue);
            Assert.AreEqual(7, nondefaultValue.Value.Number);
            return stream.ToArray();
        }

        private static byte[] VerifyKeys(TypeModel model, int provider, bool nested)
        {
            Dictionary<NullableMapValue?, int> input = new Dictionary<NullableMapValue?, int>
            {
                { default(NullableMapValue), 11 },
                {
                    new NullableMapValue { Number = 7 },
                    19
                },
            };
            ResetCounts();
            using MemoryStream stream = new MemoryStream();
            if (nested)
            {
                model.Serialize(
                    stream,
                    new NullableMapContainer<Dictionary<NullableMapValue?, int>> { Entries = input }
                );
            }
            else
            {
                model.Serialize(stream, input);
            }
            AssertCounts(provider);
            stream.Position = 0;
            Dictionary<NullableMapValue?, int> restored = nested
                ? model
                    .Deserialize<NullableMapContainer<Dictionary<NullableMapValue?, int>>>(stream)
                    .Entries
                : model.Deserialize<Dictionary<NullableMapValue?, int>>(stream);
            Assert.AreEqual(2, restored.Count);
            Assert.IsTrue(restored.TryGetValue(default(NullableMapValue), out int defaultValue));
            Assert.AreEqual(11, defaultValue);
            Assert.IsTrue(
                restored.TryGetValue(new NullableMapValue { Number = 7 }, out int nondefaultValue)
            );
            Assert.AreEqual(19, nondefaultValue);
            return stream.ToArray();
        }

        private static void AssertCounts(int provider)
        {
            if (provider == UnderlyingProvider)
            {
                Assert.AreEqual(2, NullableMapUnderlyingProvider.Writes);
            }
            else
            {
                Assert.AreEqual(0, NullableMapDualService.UnderlyingWrites);
                Assert.AreEqual(2, NullableMapDualService.NullableWrites);
            }
        }

        [TearDown]
        public void ResetProviderCounts()
        {
            ResetCounts();
        }

        [TestCaseSource(nameof(ExternalCases))]
        public void NullableExternalProvidersPreservePresenceAndSuppliedServices(
            int provider,
            int mode,
            bool nested,
            bool keys
        )
        {
#if ENABLE_IL2CPP
            if (mode != Interpreted)
            {
                Assert.Ignore("Compiled models emit dynamic IL and are unsupported by IL2CPP.");
            }
#endif
            RuntimeTypeModel runtime = CreateModel(provider, mode, nested, keys);
            if (mode == CompiledInPlace)
            {
                runtime.CompileInPlace();
            }
            TypeModel model = mode == StandaloneCompiled ? runtime.Compile() : runtime;
            if (keys)
            {
                VerifyKeys(model, provider, nested);
            }
            else
            {
                VerifyValues(model, provider, nested);
            }
        }

        [TestCaseSource(nameof(WarmCases))]
        public void WarmNullableExternalProvidersPreservePresenceAndSuppliedServices(
            int provider,
            bool nested,
            bool keys
        )
        {
#if ENABLE_IL2CPP
            Assert.Ignore("CompileInPlace emits dynamic IL and is unsupported by IL2CPP.");
#endif
            RuntimeTypeModel model = CreateModel(provider, Interpreted, nested, keys);
            byte[] before = keys
                ? VerifyKeys(model, provider, nested)
                : VerifyValues(model, provider, nested);
            model.CompileInPlace();
            byte[] after = keys
                ? VerifyKeys(model, provider, nested)
                : VerifyValues(model, provider, nested);
            CollectionAssert.AreEqual(before, after);
        }
    }
}
