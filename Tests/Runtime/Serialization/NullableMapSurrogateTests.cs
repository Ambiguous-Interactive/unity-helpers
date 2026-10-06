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
    public sealed class NullableMapSurrogateTests
    {
        private const int Interpreted = 0;
        private const int CompiledInPlace = 1;
        private const int StandaloneCompiled = 2;
        private const int AutoCompiled = 3;

        private static IEnumerable<TestCaseData> ColdCases()
        {
            foreach (bool scalar in new[] { false, true })
            {
                for (int mode = Interpreted; mode <= StandaloneCompiled; ++mode)
                {
                    foreach (bool nested in new[] { false, true })
                    {
                        foreach (bool keys in new[] { false, true })
                        {
                            yield return new TestCaseData(scalar, mode, nested, keys).SetName(
                                $"NullableMap.Surrogate.Scalar{scalar}.Mode{mode}.Nested{nested}.Keys{keys}"
                            );
                        }
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> WarmCases()
        {
            foreach (bool scalar in new[] { false, true })
            {
                foreach (bool nested in new[] { false, true })
                {
                    foreach (bool keys in new[] { false, true })
                    {
                        yield return new TestCaseData(scalar, nested, keys).SetName(
                            $"NullableMap.Surrogate.Warm.Scalar{scalar}.Nested{nested}.Keys{keys}"
                        );
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> AutoCases()
        {
            foreach (bool scalar in new[] { false, true })
            {
                foreach (bool nested in new[] { false, true })
                {
                    foreach (bool keys in new[] { false, true })
                    {
                        yield return new TestCaseData(scalar, nested, keys).SetName(
                            $"NullableMap.Surrogate.Auto.Scalar{scalar}.Nested{nested}.Keys{keys}"
                        );
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> OldValueCases()
        {
            foreach (bool scalar in new[] { false, true })
            {
                for (int mode = Interpreted; mode <= AutoCompiled; ++mode)
                {
                    foreach (bool present in new[] { false, true })
                    {
                        yield return new TestCaseData(scalar, mode, present).SetName(
                            $"NullableMap.Surrogate.OldValue.Scalar{scalar}.Mode{mode}.Present{present}"
                        );
                    }
                }
            }
        }

        private static RuntimeTypeModel CreateModel<T>(bool nested, bool keys)
            where T : struct
        {
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            model.AutoCompile = false;
            Type root = keys
                ? nested
                    ? typeof(NullableMapContainer<Dictionary<T?, int>>)
                    : typeof(Dictionary<T?, int>)
                : nested
                    ? typeof(NullableMapContainer<Dictionary<int, T?>>)
                    : typeof(Dictionary<int, T?>);
            model.Add(root, true);
            return model;
        }

        private static byte[] Verify<T>(
            TypeModel model,
            bool scalar,
            bool nested,
            bool keys,
            T nondefault
        )
            where T : struct
        {
            using MemoryStream stream = new MemoryStream();
            if (keys)
            {
                Dictionary<T?, int> input = new Dictionary<T?, int>
                {
                    { default(T), 11 },
                    { nondefault, 19 },
                };
                if (nested)
                {
                    model.Serialize(
                        stream,
                        new NullableMapContainer<Dictionary<T?, int>> { Entries = input }
                    );
                }
                else
                {
                    model.Serialize(stream, input);
                }
                stream.Position = 0;
                Dictionary<T?, int> restored = nested
                    ? model.Deserialize<NullableMapContainer<Dictionary<T?, int>>>(stream).Entries
                    : model.Deserialize<Dictionary<T?, int>>(stream);
                Assert.AreEqual(2, restored.Count);
                Assert.IsTrue(restored.TryGetValue(default(T), out int defaultValue));
                Assert.AreEqual(11, defaultValue);
                Assert.IsTrue(restored.TryGetValue(nondefault, out int nondefaultValue));
                Assert.AreEqual(19, nondefaultValue);
            }
            else
            {
                Dictionary<int, T?> input = new Dictionary<int, T?>
                {
                    { 1, null },
                    { 2, default(T) },
                    { 3, nondefault },
                };
                if (nested)
                {
                    model.Serialize(
                        stream,
                        new NullableMapContainer<Dictionary<int, T?>> { Entries = input }
                    );
                }
                else
                {
                    model.Serialize(stream, input);
                }
                stream.Position = 0;
                Dictionary<int, T?> restored = nested
                    ? model.Deserialize<NullableMapContainer<Dictionary<int, T?>>>(stream).Entries
                    : model.Deserialize<Dictionary<int, T?>>(stream);
                Assert.AreEqual(3, restored.Count);
                Assert.IsTrue(restored.TryGetValue(1, out T? absent));
                Assert.IsFalse(absent.HasValue);
                Assert.IsTrue(restored.TryGetValue(2, out T? presentDefault));
                Assert.IsTrue(presentDefault.HasValue);
                Assert.AreEqual(default(T), presentDefault.Value);
                Assert.IsTrue(restored.TryGetValue(3, out T? presentNondefault));
                Assert.IsTrue(presentNondefault.HasValue);
                Assert.AreEqual(nondefault, presentNondefault.Value);
            }
            string literal = keys
                ? scalar
                    ? "CgQIABALCgQIBxAT"
                    : "CgQKABALCgYKAggHEBM="
                : scalar
                    ? "CgIIAQoECAIQAAoECAMQBw=="
                    : "CgIIAQoECAISAAoGCAMSAggH";
            byte[] expected = Convert.FromBase64String(literal);
            if (nested)
            {
                for (int offset = 0; offset < expected.Length; offset += expected[offset + 1] + 2)
                {
                    expected[offset] = 58;
                }
            }
            byte[] bytes = stream.ToArray();
            CollectionAssert.AreEqual(expected, bytes);
            return bytes;
        }

        private static void CheckCold<T>(
            bool scalar,
            int mode,
            bool nested,
            bool keys,
            T nondefault
        )
            where T : struct
        {
            RuntimeTypeModel runtime = CreateModel<T>(nested, keys);
            runtime.AutoCompile = mode == AutoCompiled;
            TypeModel model = runtime;
#if ENABLE_IL2CPP
            if (mode != Interpreted)
            {
                Assert.Ignore("Compiled models emit dynamic IL and are unsupported by IL2CPP.");
            }
#else
            if (mode == CompiledInPlace)
            {
                runtime.CompileInPlace();
            }
            else if (mode == StandaloneCompiled)
            {
                model = runtime.Compile();
            }
#endif
            Verify(model, scalar, nested, keys, nondefault);
        }

        private static void CheckWarm<T>(bool scalar, bool nested, bool keys, T nondefault)
            where T : struct
        {
#if ENABLE_IL2CPP
            Assert.Ignore("CompileInPlace emits dynamic IL and is unsupported by IL2CPP.");
#else
            RuntimeTypeModel model = CreateModel<T>(nested, keys);
            byte[] before = Verify(model, scalar, nested, keys, nondefault);
            model.CompileInPlace();
            CollectionAssert.AreEqual(before, Verify(model, scalar, nested, keys, nondefault));
#endif
        }

        private static void CheckOldValue<T>(int mode, bool present, T existing)
            where T : struct
        {
            RuntimeTypeModel runtime = CreateModel<T>(true, false);
            runtime.AutoCompile = mode == AutoCompiled;
            TypeModel model = runtime;
#if ENABLE_IL2CPP
            if (mode != Interpreted)
            {
                Assert.Ignore("Compiled models emit dynamic IL and are unsupported by IL2CPP.");
            }
#else
            if (mode == CompiledInPlace)
            {
                runtime.CompileInPlace();
            }
            else if (mode == StandaloneCompiled)
            {
                model = runtime.Compile();
            }
#endif
            using MemoryStream encodedDefault = new MemoryStream();
            model.Serialize(encodedDefault, default(T));
            byte[] bytes = encodedDefault.ToArray();
            if (bytes.Length == 0)
            {
                bytes = new byte[] { 120, 0 };
            }
            using MemoryStream ordinaryInput = new MemoryStream(bytes);
            T ordinary = model.Deserialize<T>(ordinaryInput, present ? existing : default(T));
            using MemoryStream nullableInput = new MemoryStream(bytes);
            T? nullable = model.Deserialize<T?>(nullableInput, present ? existing : (T?)null);
            Assert.IsTrue(nullable.HasValue);
            Assert.AreEqual(ordinary, nullable.Value);
        }

        [TestCaseSource(nameof(AutoCases))]
        public void AutoCompiledNullableSurrogatesPreservePresenceAndWireCategory(
            bool scalar,
            bool nested,
            bool keys
        )
        {
            if (scalar)
            {
                CheckCold(
                    scalar,
                    AutoCompiled,
                    nested,
                    keys,
                    new NullableMapScalarSurrogateValue { Number = 7 }
                );
            }
            else
            {
                CheckCold(
                    scalar,
                    AutoCompiled,
                    nested,
                    keys,
                    new NullableMapMessageSurrogateValue { Number = 7 }
                );
            }
        }

        [TestCaseSource(nameof(OldValueCases))]
        public void NullableSurrogateReadsMatchUnderlyingOldValueSemantics(
            bool scalar,
            int mode,
            bool present
        )
        {
            if (scalar)
            {
                CheckOldValue(mode, present, new NullableMapScalarSurrogateValue { Number = 19 });
            }
            else
            {
                CheckOldValue(mode, present, new NullableMapMessageSurrogateValue { Number = 19 });
            }
        }

        [TestCaseSource(nameof(ColdCases))]
        public void NullableSurrogatesPreservePresenceAndUnderlyingWireCategory(
            bool scalar,
            int mode,
            bool nested,
            bool keys
        )
        {
            if (scalar)
            {
                CheckCold(
                    scalar,
                    mode,
                    nested,
                    keys,
                    new NullableMapScalarSurrogateValue { Number = 7 }
                );
            }
            else
            {
                CheckCold(
                    scalar,
                    mode,
                    nested,
                    keys,
                    new NullableMapMessageSurrogateValue { Number = 7 }
                );
            }
        }

        [TestCaseSource(nameof(WarmCases))]
        public void WarmNullableSurrogatesPreservePresenceAfterCompilation(
            bool scalar,
            bool nested,
            bool keys
        )
        {
            if (scalar)
            {
                CheckWarm(scalar, nested, keys, new NullableMapScalarSurrogateValue { Number = 7 });
            }
            else
            {
                CheckWarm(
                    scalar,
                    nested,
                    keys,
                    new NullableMapMessageSurrogateValue { Number = 7 }
                );
            }
        }
    }
}
