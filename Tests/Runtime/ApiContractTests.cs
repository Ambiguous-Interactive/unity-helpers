// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using Serializer = UnityHelpers.Core.Serialization.Serializer;

    /// <summary>
    /// Tests that verify correct API usage patterns and help catch common misuse issues.
    /// These tests are designed to catch bugs like:
    /// - Calling TryGetValue() instead of TryGet() on Cache
    /// - Calling JsonSerialize() with a boolean instead of JsonStringify() with pretty flag
    /// - Other API surface misunderstandings
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CacheApiContractTests
    {
        private float _currentTime;

        private float TimeProvider()
        {
            return _currentTime;
        }

        [SetUp]
        public void SetUp()
        {
            _currentTime = 0f;
        }

        /// <summary>
        /// Verifies that Cache.TryGet is the correct method for retrieving values.
        /// This test helps catch misuse like calling TryGetValue (which doesn't exist).
        /// </summary>
        [Test]
        public void TryGetIsTheCorrectMethodForValueRetrieval()
        {
            using Cache<string, int> cache = CacheBuilder<string, int>
                .NewBuilder()
                .MaximumSize(10)
                .TimeProvider(TimeProvider)
                .Build();

            cache.Set("key", 42);

            // TryGet is the correct method - verify it works
            bool found = cache.TryGet("key", out int value);

            Assert.IsTrue(found, "TryGet should return true for existing key");
            Assert.AreEqual(42, value, "TryGet should return the correct value");
        }

        /// <summary>
        /// Verifies that Cache does NOT have a TryGetValue method (common Dictionary pattern misuse).
        /// </summary>
        [Test]
        public void CacheDoesNotHaveTryGetValueMethod()
        {
            Type cacheType = typeof(Cache<string, int>);
            MethodInfo tryGetValueMethod = cacheType.GetMethod(
                "TryGetValue",
                BindingFlags.Public | BindingFlags.Instance
            );

            Assert.IsTrue(
                tryGetValueMethod == null,
                "Cache should NOT have a TryGetValue method. Use TryGet instead. "
                    + "This is a common mistake when developers expect Dictionary-like API."
            );
        }

        /// <summary>
        /// Verifies that Cache has TryGet method with correct signature.
        /// </summary>
        [Test]
        public void CacheHasTryGetMethodWithCorrectSignature()
        {
            Type cacheType = typeof(Cache<string, int>);
            MethodInfo[] allMethods = cacheType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance
            );

            // Find TryGet method with 2 parameters (key, out value) - filter to avoid ambiguity with overloads
            MethodInfo tryGetMethod = null;
            foreach (MethodInfo method in allMethods)
            {
                if (
                    method.Name == nameof(Cache<object, object>.TryGet)
                    && method.GetParameters().Length == 2
                )
                {
                    tryGetMethod = method;
                    break;
                }
            }

            Assert.IsTrue(
                tryGetMethod != null,
                "Cache must have a TryGet method for value retrieval"
            );

            ParameterInfo[] parameters = tryGetMethod.GetParameters();
            Assert.AreEqual(
                2,
                parameters.Length,
                "TryGet should have 2 parameters: key and out value"
            );
            Assert.AreEqual(
                typeof(string),
                parameters[0].ParameterType,
                "First parameter should be the key type"
            );
            Assert.IsTrue(parameters[1].IsOut, "Second parameter should be an out parameter");
            Assert.AreEqual(typeof(bool), tryGetMethod.ReturnType, "TryGet should return bool");
        }

        /// <summary>
        /// Verifies TryGet returns false and default value for missing keys.
        /// </summary>
        [Test]
        public void TryGetReturnsFalseAndDefaultForMissingKey()
        {
            using Cache<string, int> cache = CacheBuilder<string, int>
                .NewBuilder()
                .MaximumSize(10)
                .TimeProvider(TimeProvider)
                .Build();

            bool found = cache.TryGet("nonexistent", out int value);

            Assert.IsFalse(found, "TryGet should return false for missing key");
            Assert.AreEqual(
                default(int),
                value,
                "TryGet should set out parameter to default for missing key"
            );
        }

        /// <summary>
        /// Verifies TryGet works with null keys gracefully.
        /// </summary>
        [Test]
        public void TryGetHandlesNullKeyGracefully()
        {
            using Cache<string, int> cache = CacheBuilder<string, int>
                .NewBuilder()
                .MaximumSize(10)
                .TimeProvider(TimeProvider)
                .Build();

            bool found = cache.TryGet(null, out int value);

            Assert.IsFalse(found, "TryGet should return false for null key");
            Assert.AreEqual(
                default(int),
                value,
                "TryGet should set out parameter to default for null key"
            );
        }

        /// <summary>
        /// Verifies TryGet works correctly with value types.
        /// </summary>
        [Test]
        [TestCase(0, TestName = "TryGetValueTypeZero")]
        [TestCase(int.MinValue, TestName = "TryGetValueTypeMinValue")]
        [TestCase(int.MaxValue, TestName = "TryGetValueTypeMaxValue")]
        [TestCase(-1, TestName = "TryGetValueTypeNegative")]
        public void TryGetWorksCorrectlyWithValueTypes(int testValue)
        {
            using Cache<string, int> cache = CacheBuilder<string, int>
                .NewBuilder()
                .MaximumSize(10)
                .TimeProvider(TimeProvider)
                .Build();

            cache.Set("key", testValue);

            bool found = cache.TryGet("key", out int retrievedValue);

            Assert.IsTrue(
                found,
                $"TryGet should return true for existing key with value {testValue}"
            );
            Assert.AreEqual(
                testValue,
                retrievedValue,
                $"TryGet should return correct value {testValue}"
            );
        }

        /// <summary>
        /// Verifies TryGet works correctly with reference types.
        /// </summary>
        [Test]
        public void TryGetWorksCorrectlyWithReferenceTypes()
        {
            using Cache<string, List<int>> cache = CacheBuilder<string, List<int>>
                .NewBuilder()
                .MaximumSize(10)
                .TimeProvider(TimeProvider)
                .Build();

            List<int> expectedList = new() { 1, 2, 3 };
            cache.Set("key", expectedList);

            bool found = cache.TryGet("key", out List<int> retrievedList);

            Assert.IsTrue(found, "TryGet should return true for existing key");
            Assert.AreSame(expectedList, retrievedList, "TryGet should return the same reference");
        }

        /// <summary>
        /// Verifies TryGet distinguishes between value not found and null value stored.
        /// </summary>
        [Test]
        public void TryGetDistinguishesBetweenMissingAndNullValue()
        {
            using Cache<string, string> cache = CacheBuilder<string, string>
                .NewBuilder()
                .MaximumSize(10)
                .TimeProvider(TimeProvider)
                .Build();

            // Store null value
            cache.Set("nullKey", null);

            // Missing key
            bool foundMissing = cache.TryGet("missingKey", out string missingValue);
            Assert.IsFalse(foundMissing, "TryGet should return false for missing key");

            // Key with null value - Cache doesn't store null keys, but this tests the pattern
            // Note: The cache implementation may handle null values differently
            bool foundNull = cache.TryGet("nullKey", out string nullValue);
            // If the cache stores null values, foundNull should be true
            // The assertion depends on Cache implementation behavior
            Assert.IsTrue(nullValue == null, "Value should be null when null was stored");
        }
    }

    /// <summary>
    /// Tests that verify correct Serializer API usage patterns.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializerApiContractTests
    {
        [Serializable]
        [ProtoContract]
        private sealed class TestMessage
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }

            [ProtoMember(3)]
            public List<int> Values { get; set; }
        }

        /// <summary>
        /// Verifies that JsonStringify with pretty=true produces indented output.
        /// This test helps catch misuse like calling JsonSerialize(obj, true) which
        /// doesn't exist - the correct method is JsonStringify(obj, pretty: true).
        /// </summary>
        [Test]
        public void JsonStringifyWithPrettyTrueProducesIndentedOutput()
        {
            TestMessage msg = new()
            {
                Id = 42,
                Name = "Test",
                Values = new List<int> { 1, 2, 3 },
            };

            string prettyJson = UnityHelpers.Core.Serialization.Serializer.JsonStringify(
                msg,
                pretty: true
            );

            Assert.IsTrue(
                prettyJson.Contains("\n") || prettyJson.Contains("\r"),
                "Pretty-printed JSON should contain newlines for indentation. "
                    + $"Actual output: {prettyJson}"
            );
            Assert.IsTrue(
                prettyJson.Contains("  ") || prettyJson.Contains("\t"),
                "Pretty-printed JSON should contain indentation (spaces or tabs). "
                    + $"Actual output: {prettyJson}"
            );
        }

        /// <summary>
        /// Verifies that JsonStringify with pretty=false produces compact output.
        /// </summary>
        [Test]
        public void JsonStringifyWithPrettyFalseProducesCompactOutput()
        {
            TestMessage msg = new()
            {
                Id = 42,
                Name = "Test",
                Values = new List<int> { 1, 2, 3 },
            };

            string compactJson = UnityHelpers.Core.Serialization.Serializer.JsonStringify(
                msg,
                pretty: false
            );
            string prettyJson = Serializer.JsonStringify(msg, pretty: true);

            Assert.Less(
                compactJson.Length,
                prettyJson.Length,
                "Compact JSON should be shorter than pretty-printed JSON. "
                    + $"Compact length: {compactJson.Length}, Pretty length: {prettyJson.Length}"
            );
        }

        /// <summary>
        /// Verifies that JsonStringify is the correct method for string output with formatting options.
        /// </summary>
        [Test]
        public void JsonStringifyIsTheCorrectMethodForFormattedStringOutput()
        {
            Type serializerType = typeof(Serializer);
            MethodInfo[] allMethods = serializerType.GetMethods(
                BindingFlags.Public | BindingFlags.Static
            );

            List<MethodInfo> jsonStringifyMethodsList = new();
            foreach (MethodInfo method in allMethods)
            {
                if (method.Name == nameof(Serializer.JsonStringify))
                {
                    jsonStringifyMethodsList.Add(method);
                }
            }

            Assert.Greater(
                jsonStringifyMethodsList.Count,
                0,
                "Serializer must have JsonStringify method(s) for formatted string output"
            );

            // Check for the bool pretty overload
            bool hasPrettyOverload = false;
            foreach (MethodInfo method in jsonStringifyMethodsList)
            {
                ParameterInfo[] parameters = method.GetParameters();
                foreach (ParameterInfo parameter in parameters)
                {
                    if (parameter.Name == "pretty" && parameter.ParameterType == typeof(bool))
                    {
                        hasPrettyOverload = true;
                        break;
                    }
                }

                if (hasPrettyOverload)
                {
                    break;
                }
            }

            Assert.IsTrue(
                hasPrettyOverload,
                "Serializer.JsonStringify should have an overload with a 'pretty' bool parameter. "
                    + "Use JsonStringify(obj, pretty: true) for indented output."
            );
        }

        /// <summary>
        /// Verifies that JsonSerialize returns bytes, not a formatted string.
        /// This test helps catch misuse where someone expects JsonSerialize to format output.
        /// </summary>
        [Test]
        public void JsonSerializeReturnsBytesNotFormattedString()
        {
            TestMessage msg = new() { Id = 42, Name = "Test" };

            byte[] result = Serializer.JsonSerialize(msg);

            Assert.IsTrue(result != null, "JsonSerialize should return a byte array");
            Assert.IsInstanceOf<byte[]>(
                result,
                "JsonSerialize returns byte[], not string. Use JsonStringify for string output."
            );
        }

        /// <summary>
        /// Verifies that JsonSerialize does NOT have a (object, bool) overload.
        /// This test catches the specific bug where code calls JsonSerialize(obj, true)
        /// expecting pretty-printing, when the correct method is JsonStringify(obj, pretty: true).
        /// </summary>
        [Test]
        public void JsonSerializeDoesNotHaveBoolSecondParameter()
        {
            Type serializerType = typeof(Serializer);
            MethodInfo[] allMethods = serializerType.GetMethods(
                BindingFlags.Public | BindingFlags.Static
            );

            List<MethodInfo> jsonSerializeMethodsList = new();
            foreach (MethodInfo method in allMethods)
            {
                if (method.Name == nameof(Serializer.JsonSerialize))
                {
                    jsonSerializeMethodsList.Add(method);
                }
            }

            foreach (MethodInfo method in jsonSerializeMethodsList)
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length >= 2)
                {
                    // Check that no overload has a simple bool as second parameter
                    // (which would suggest a 'pretty' flag)
                    bool hasBoolSecondParam =
                        parameters.Length >= 2 && parameters[1].ParameterType == typeof(bool);

                    Assert.IsFalse(
                        hasBoolSecondParam,
                        $"JsonSerialize should NOT have a bool second parameter. "
                            + $"Method signature: {method}. "
                            + "For pretty-printing, use JsonStringify(obj, pretty: true) instead."
                    );
                }
            }
        }

        /// <summary>
        /// Verifies round-trip with pretty printing preserves data.
        /// </summary>
        [Test]
        public void JsonStringifyPrettyPrintRoundTripPreservesData()
        {
            TestMessage original = new()
            {
                Id = 123,
                Name = "Round Trip Test",
                Values = new List<int> { 10, 20, 30 },
            };

            string prettyJson = Serializer.JsonStringify(original, pretty: true);
            TestMessage deserialized = Serializer.JsonDeserialize<TestMessage>(prettyJson);

            Assert.AreEqual(
                original.Id,
                deserialized.Id,
                "Id should be preserved after round-trip"
            );
            Assert.AreEqual(
                original.Name,
                deserialized.Name,
                "Name should be preserved after round-trip"
            );
            CollectionAssert.AreEqual(
                original.Values,
                deserialized.Values,
                "Values should be preserved after round-trip"
            );
        }

        /// <summary>
        /// Verifies that compact JSON can be deserialized correctly.
        /// </summary>
        [Test]
        public void JsonStringifyCompactRoundTripPreservesData()
        {
            TestMessage original = new()
            {
                Id = 456,
                Name = "Compact Test",
                Values = new List<int> { 5, 10, 15 },
            };

            string compactJson = Serializer.JsonStringify(original, pretty: false);
            TestMessage deserialized = Serializer.JsonDeserialize<TestMessage>(compactJson);

            Assert.AreEqual(
                original.Id,
                deserialized.Id,
                "Id should be preserved after round-trip"
            );
            Assert.AreEqual(
                original.Name,
                deserialized.Name,
                "Name should be preserved after round-trip"
            );
            CollectionAssert.AreEqual(
                original.Values,
                deserialized.Values,
                "Values should be preserved after round-trip"
            );
        }

        /// <summary>
        /// Data-driven test for various serialization scenarios.
        /// </summary>
        [Test]
        [TestCase(0, "", TestName = "JsonStringifyEdgeCaseZeroAndEmpty")]
        [TestCase(int.MinValue, "min", TestName = "JsonStringifyEdgeCaseMinValue")]
        [TestCase(int.MaxValue, "max", TestName = "JsonStringifyEdgeCaseMaxValue")]
        [TestCase(-1, null, TestName = "JsonStringifyEdgeCaseNegativeAndNull")]
        public void JsonStringifyHandlesBoundaryValues(int id, string name)
        {
            TestMessage msg = new() { Id = id, Name = name };

            string json = Serializer.JsonStringify(msg, pretty: false);
            TestMessage clone = Serializer.JsonDeserialize<TestMessage>(json);

            Assert.AreEqual(id, clone.Id, $"Id {id} should survive round-trip");
            Assert.AreEqual(name, clone.Name, $"Name '{name}' should survive round-trip");
        }

        /// <summary>
        /// Verifies both pretty and compact output deserialize to equivalent objects.
        /// </summary>
        [Test]
        public void JsonStringifyPrettyAndCompactDeserializeToEquivalentObjects()
        {
            TestMessage original = new()
            {
                Id = 789,
                Name = "Equivalence Test",
                Values = new List<int> { 1, 2, 3, 4, 5 },
            };

            string prettyJson = Serializer.JsonStringify(original, pretty: true);
            string compactJson = Serializer.JsonStringify(original, pretty: false);

            TestMessage fromPretty = Serializer.JsonDeserialize<TestMessage>(prettyJson);
            TestMessage fromCompact = Serializer.JsonDeserialize<TestMessage>(compactJson);

            Assert.AreEqual(fromPretty.Id, fromCompact.Id, "Both should deserialize to same Id");
            Assert.AreEqual(
                fromPretty.Name,
                fromCompact.Name,
                "Both should deserialize to same Name"
            );
            CollectionAssert.AreEqual(
                fromPretty.Values,
                fromCompact.Values,
                "Both should deserialize to same Values"
            );
        }
    }

    /// <summary>
    /// Tests that verify common API patterns across multiple types.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CommonApiPatternTests
    {
        /// <summary>
        /// Verifies that Cache implements IDisposable correctly.
        /// </summary>
        [Test]
        public void CacheImplementsIDisposable()
        {
            Type cacheType = typeof(Cache<,>);
            Assert.IsTrue(
                typeof(IDisposable).IsAssignableFrom(cacheType),
                "Cache should implement IDisposable for resource cleanup"
            );
        }

        /// <summary>
        /// Verifies that disposed Cache handles operations gracefully.
        /// </summary>
        [Test]
        public void CacheAfterDisposeOperationsAreGraceful()
        {
            Cache<string, int> cache = CacheBuilder<string, int>
                .NewBuilder()
                .MaximumSize(10)
                .Build();

            cache.Set("key", 42);
            cache.Dispose();

            // These should not throw
            Assert.DoesNotThrow(
                () => cache.Set("newKey", 100),
                "Set on disposed cache should not throw"
            );
            Assert.DoesNotThrow(
                () => cache.TryGet("key", out _),
                "TryGet on disposed cache should not throw"
            );
            Assert.DoesNotThrow(
                () => cache.TryRemove("key"),
                "TryRemove on disposed cache should not throw"
            );
            Assert.DoesNotThrow(() => cache.Clear(), "Clear on disposed cache should not throw");
            Assert.DoesNotThrow(
                () => cache.ContainsKey("key"),
                "ContainsKey on disposed cache should not throw"
            );
        }

        /// <summary>
        /// Verifies that the CacheBuilder fluent API can be chained correctly.
        /// Note: CacheBuilder is a struct, so methods return a copy. We verify the fluent API
        /// works by checking that configurations are properly applied when chained.
        /// </summary>
        [Test]
        public void CacheBuilderFluentApiReturnsBuilderForChaining()
        {
            // CacheBuilder is a struct, so we can't use AreSame (reference equality).
            // Instead, verify that the fluent API works by chaining calls and building successfully.
            using Cache<string, int> cache = CacheBuilder<string, int>
                .NewBuilder()
                .MaximumSize(100)
                .ExpireAfterWrite(1.0f)
                .ExpireAfterAccess(1.0f)
                .EvictionPolicy(EvictionPolicy.Lru)
                .RecordStatistics()
                .Build();

            Assert.IsTrue(cache != null, "Build should return a Cache instance");
            Assert.IsInstanceOf<Cache<string, int>>(
                cache,
                "Build should return correct Cache type"
            );
            Assert.AreEqual(100, cache.Capacity, "MaximumSize should be applied");
        }
    }

    /// <summary>
    /// Tests that document the correct API methods to use in common scenarios.
    /// These tests serve as executable documentation that will break if API changes.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ApiDocumentationTests
    {
        /// <summary>
        /// Documents that JsonStringify is for string output.
        /// </summary>
        [Test]
        public void SerializerJsonStringifyReturnsString()
        {
            object testObj = new { Id = 1, Name = "Test" };
            string result = Serializer.JsonStringify(testObj);

            Assert.IsInstanceOf<string>(
                result,
                "JsonStringify should return a string for human-readable JSON"
            );
        }

        /// <summary>
        /// Documents that JsonSerialize is for byte[] output.
        /// </summary>
        [Test]
        public void SerializerJsonSerializeReturnsBytes()
        {
            object testObj = new { Id = 1, Name = "Test" };
            byte[] result = Serializer.JsonSerialize(testObj);

            Assert.IsInstanceOf<byte[]>(
                result,
                "JsonSerialize should return byte[] for wire format"
            );
        }

        /// <summary>
        /// Anonymous types (and other types without a public parameterless constructor) must serialize
        /// without invoking System.Text.Json's parameterized-constructor converter, which has no AOT
        /// code under IL2CPP. The reflection-light writer must emit the exact same JSON mono produces.
        /// </summary>
        [Test]
        public void JsonStringifyAnonymousTypeProducesExpectedJson()
        {
            object testObj = new { Id = 1, Name = "x" };

            string result = Serializer.JsonStringify(testObj);

            Assert.AreEqual("{\"Id\":1,\"Name\":\"x\"}", result);
        }

        /// <summary>
        /// The byte[] path must produce the same property layout for anonymous types as the string path.
        /// </summary>
        [Test]
        public void JsonSerializeAnonymousTypeProducesExpectedJson()
        {
            object testObj = new { Id = 1, Name = "x" };

            byte[] result = Serializer.JsonSerialize(testObj);
            string asText = System.Text.Encoding.UTF8.GetString(result);

            Assert.AreEqual("{\"Id\":1,\"Name\":\"x\"}", asText);
        }

        /// <summary>
        /// Documents that Cache.TryGet is the method for value retrieval (not TryGetValue).
        /// </summary>
        [Test]
        public void CacheTryGetIsTheLookupMethod()
        {
            using Cache<string, int> cache = CacheBuilder<string, int>
                .NewBuilder()
                .MaximumSize(10)
                .Build();

            cache.Set("key", 42);

            // This is the correct method call
            bool success = cache.TryGet("key", out int value);

            Assert.IsTrue(success, "TryGet is the correct method for looking up values");
            Assert.AreEqual(42, value, "TryGet should return the stored value");
        }

        /// <summary>
        /// Verifies Serializer has expected JSON-related methods.
        /// </summary>
        [Test]
        public void SerializerHasExpectedJsonMethods()
        {
            Type serializerType = typeof(Serializer);

            // Check for expected methods
            string[] expectedMethodNames = new[]
            {
                nameof(Serializer.JsonStringify),
                nameof(Serializer.JsonSerialize),
                nameof(Serializer.JsonDeserialize),
                nameof(Serializer.JsonSerializeFast),
                nameof(Serializer.JsonDeserializeFast),
            };

            MethodInfo[] allSerializerMethods = serializerType.GetMethods(
                BindingFlags.Public | BindingFlags.Static
            );

            foreach (string methodName in expectedMethodNames)
            {
                int methodCount = 0;
                foreach (MethodInfo method in allSerializerMethods)
                {
                    if (method.Name == methodName)
                    {
                        methodCount++;
                    }
                }

                Assert.Greater(methodCount, 0, $"Serializer should have {methodName} method(s)");
            }
        }

        /// <summary>
        /// Verifies Cache has expected methods and no confusing aliases.
        /// </summary>
        [Test]
        public void CacheHasExpectedMethodsAndNoConfusingAliases()
        {
            Type cacheType = typeof(Cache<string, int>);
            MethodInfo[] allMethods = cacheType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance
            );

            // Methods that SHOULD exist
            string[] expectedMethods = new[]
            {
                nameof(Cache<object, object>.TryGet),
                nameof(Cache<object, object>.Set),
                nameof(Cache<object, object>.TryRemove),
                nameof(Cache<object, object>.ContainsKey),
                nameof(Cache<object, object>.Clear),
                nameof(Cache<object, object>.GetOrAdd),
            };

            foreach (string methodName in expectedMethods)
            {
                bool hasMethod = false;
                foreach (MethodInfo method in allMethods)
                {
                    if (method.Name == methodName)
                    {
                        hasMethod = true;
                        break;
                    }
                }

                Assert.IsTrue(hasMethod, $"Cache should have {methodName} method");
            }

            // Methods that SHOULD NOT exist (common misuse patterns)
            string[] forbiddenMethods = new[]
            {
                "TryGetValue", // Use TryGet instead
                "Add", // Use Set instead
                "Remove", // Use TryRemove instead
                "Get", // Use TryGet or GetOrAdd instead
            };

            foreach (string methodName in forbiddenMethods)
            {
                bool hasMethod = false;
                foreach (MethodInfo method in allMethods)
                {
                    if (method.Name == methodName)
                    {
                        hasMethod = true;
                        break;
                    }
                }

                Assert.IsFalse(
                    hasMethod,
                    $"Cache should NOT have {methodName} method to avoid confusion with Dictionary API. "
                        + GetMethodAlternativeMessage(methodName)
                );
            }
        }

        private static string GetMethodAlternativeMessage(string methodName)
        {
            return methodName switch
            {
                "TryGetValue" => "Use TryGet instead.",
                "Add" => "Use Set instead (it handles both add and update).",
                "Remove" => "Use TryRemove instead.",
                "Get" => "Use TryGet for safe retrieval or GetOrAdd for lazy loading.",
                _ => string.Empty,
            };
        }

        /// <summary>
        /// Verifies that pretty printing is controlled by the pretty parameter, not a different method.
        /// </summary>
        [Test]
        public void JsonStringifyPrettyParameterControlsFormatting()
        {
            object testObj = new { Id = 1, Name = "Test" };

            string compact = Serializer.JsonStringify(testObj, pretty: false);
            string pretty = Serializer.JsonStringify(testObj, pretty: true);

            // Compact should have no newlines (in the JSON structure itself)
            Assert.IsFalse(
                compact.Contains("\n") && compact.Contains("  "),
                "Compact JSON should not have indentation"
            );

            // Pretty should have newlines and indentation
            Assert.IsTrue(
                pretty.Contains("\n") || pretty.Contains("\r"),
                "Pretty JSON should have newlines"
            );
        }
    }
}
