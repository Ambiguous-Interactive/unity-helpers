// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializableTypeTests
    {
        private const string MissingTypeName = "Missing.Type, MissingAssembly";

        private static string BuildAssemblyQualifiedName(Type type)
        {
            StringBuilder builder = new();
            AppendTypeName(builder, type);
            builder.Append(", ").Append(type.Assembly.FullName);
            return builder.ToString();
        }

        private static void AppendTypeName(StringBuilder builder, Type type)
        {
            if (type.IsArray)
            {
                AppendTypeName(builder, type.GetElementType());
                builder.Append('[');
                for (int rank = 1; rank < type.GetArrayRank(); rank++)
                {
                    builder.Append(',');
                }

                builder.Append(']');
                return;
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                builder.Append(type.GetGenericTypeDefinition().FullName).Append('[');
                Type[] arguments = type.GetGenericArguments();
                for (int index = 0; index < arguments.Length; index++)
                {
                    if (0 < index)
                    {
                        builder.Append(',');
                    }

                    builder
                        .Append('[')
                        .Append(BuildAssemblyQualifiedName(arguments[index]))
                        .Append(']');
                }

                builder.Append(']');
                return;
            }

            builder.Append(type.FullName);
        }

        /// <summary>
        /// The filter cache is keyed by what a user types and each entry is a filtered slice of
        /// every type in the project, so typing a name one character at a time cached one array per
        /// prefix and released none of them
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/694">#694</see>).
        /// </summary>
        [Test]
        public void FilteredDescriptorCacheStopsAtItsBound()
        {
            int originalBound = SerializableTypeCatalog.MaxCachedFilterResults;
            try
            {
                SerializableTypeCatalog.MaxCachedFilterResults = 8;
                const string typed = "SerializableTypeDescriptor";
                for (int length = 1; length <= typed.Length; ++length)
                {
                    _ = SerializableTypeCatalog.GetFilteredDescriptors(typed.Substring(0, length));
                }

                Assert.LessOrEqual(
                    SerializableTypeCatalog.CachedFilterResultCountForTesting,
                    8,
                    "Every prefix of a typed search term must not become a retained entry."
                );
            }
            finally
            {
                SerializableTypeCatalog.MaxCachedFilterResults = originalBound;
            }
        }

        /// <summary>
        /// Eviction may only cost a wider re-scan: the prefix reuse at the top of
        /// <c>GetFilteredDescriptors</c> falls back to the full descriptor set when a parent term is
        /// gone, so a bound must not change which descriptors come back.
        /// </summary>
        [Test]
        public void EvictionDoesNotChangeFilteredResults()
        {
            int originalBound = SerializableTypeCatalog.MaxCachedFilterResults;
            try
            {
                SerializableTypeCatalog.MaxCachedFilterResults = 0;
                IReadOnlyList<SerializableTypeCatalog.SerializableTypeDescriptor> unboundedResult =
                    SerializableTypeCatalog.GetFilteredDescriptors("SerializableType");
                string[] unbounded = unboundedResult
                    .Select(descriptor => descriptor.AssemblyQualifiedName)
                    .ToArray();
                Assert.AreSame(
                    unboundedResult,
                    SerializableTypeCatalog.GetFilteredDescriptors("SerializableType"),
                    "The baseline result must be cached before exercising its eviction."
                );

                SerializableTypeCatalog.MaxCachedFilterResults = 2;
                _ = SerializableTypeCatalog.GetFilteredDescriptors("eviction-probe-a");
                _ = SerializableTypeCatalog.GetFilteredDescriptors("eviction-probe-b");
                Assert.AreEqual(2, SerializableTypeCatalog.CachedFilterResultCountForTesting);

                IReadOnlyList<SerializableTypeCatalog.SerializableTypeDescriptor> afterEvictionResult =
                    SerializableTypeCatalog.GetFilteredDescriptors("SerializableType");
                Assert.AreNotSame(
                    unboundedResult,
                    afterEvictionResult,
                    "Two newer distinct keys at a bound of two must evict the baseline result."
                );
                string[] afterEviction = afterEvictionResult
                    .Select(descriptor => descriptor.AssemblyQualifiedName)
                    .ToArray();

                CollectionAssert.AreEqual(unbounded, afterEviction);
            }
            finally
            {
                SerializableTypeCatalog.MaxCachedFilterResults = originalBound;
            }
        }

        [Test]
        public void LoweringFilterCacheBoundEvictsExistingResultsImmediately()
        {
            int originalBound = SerializableTypeCatalog.MaxCachedFilterResults;
            try
            {
                SerializableTypeCatalog.MaxCachedFilterResults = 0;
                for (int index = 0; index < 3; ++index)
                {
                    _ = SerializableTypeCatalog.GetFilteredDescriptors($"retune-{index}");
                }
                Assert.Greater(
                    SerializableTypeCatalog.CachedFilterResultCountForTesting,
                    2,
                    "The cache must exceed the target bound before it is shrunk."
                );

                SerializableTypeCatalog.MaxCachedFilterResults = 2;

                Assert.AreEqual(2, SerializableTypeCatalog.CachedFilterResultCountForTesting);
            }
            finally
            {
                SerializableTypeCatalog.MaxCachedFilterResults = originalBound;
            }
        }

        [Test]
        public void DefaultWrapperBehavesAsEmpty()
        {
            SerializableType serializable = default;

            Assert.IsTrue(serializable.IsEmpty);
            Assert.IsTrue(
                serializable.Value == null,
                "Value should be null for default SerializableType"
            );
            Assert.IsTrue(
                (Type)serializable == null,
                "Implicit cast to Type should be null for default SerializableType"
            );
            Assert.IsTrue(serializable == null);
            Assert.IsTrue(null == serializable);
            Assert.IsFalse(serializable != null);
            // A boxed value type is an object even when empty; use EqualsType(null) or IsEmpty for emptiness.
            Assert.IsFalse(serializable.Equals(null));
            Assert.IsTrue(serializable.EqualsType(null));
            Assert.AreEqual(string.Empty, serializable.AssemblyQualifiedName);
            Assert.AreEqual(0, serializable.GetHashCode());
        }

        [Test]
        public void AssigningTypePersistsAssemblyQualifiedName()
        {
            SerializableType serializable = SerializableType.FromType(typeof(List<int>));

            Assert.IsFalse(serializable.IsEmpty);
            Assert.AreEqual(typeof(List<int>), serializable.Value);
            Assert.AreEqual(
                typeof(List<int>).AssemblyQualifiedName,
                serializable.AssemblyQualifiedName
            );
            Assert.IsTrue(serializable.EqualsType(typeof(List<int>)));
            Type extracted = serializable;
            Assert.AreEqual(typeof(List<int>), extracted);
            Assert.IsFalse(serializable.Equals(null));
            Assert.IsFalse(
                serializable.Equals(typeof(List<int>)),
                "A bare Type cannot answer true for a boxed SerializableType in return"
            );
            Assert.IsFalse(serializable.EqualsType(null));
            Assert.IsTrue(serializable.EqualsType(typeof(List<int>)));
            Assert.IsFalse(serializable.EqualsType(typeof(float)));
        }

        [Test]
        public void EqualityOperatorsMatchSystemType()
        {
            SerializableType first = SerializableType.FromType(typeof(int));
            SerializableType second = SerializableType.FromType(typeof(int));
            SerializableType different = SerializableType.FromType(typeof(float));

            Assert.IsTrue(first.EqualsType(typeof(int)));
            Assert.IsFalse(first.EqualsType(typeof(float)));
            Assert.IsFalse(first.Equals(different));
            Assert.IsTrue(first.Equals(second));
            Assert.IsTrue(second.Equals(first));
            Assert.IsFalse(first.Equals(different));
        }

        [Test]
        public void ImplicitConversionsRoundTripType()
        {
            SerializableType serializable = SerializableType.FromType(typeof(string));
            Type extracted = serializable;

            Assert.AreEqual(typeof(string), extracted);
            SerializableType reconstructed = SerializableType.FromType(extracted);
            Assert.AreEqual(serializable, reconstructed);
            Assert.AreEqual(
                serializable.AssemblyQualifiedName,
                reconstructed.AssemblyQualifiedName
            );
            Assert.IsTrue(serializable.EqualsType(typeof(string)));
            Assert.IsFalse(serializable.EqualsType(typeof(int)));
        }

        [Test]
        public void FromTypeHandlesNull()
        {
            SerializableType none = SerializableType.FromType(null);
            SerializableType implicitNone = SerializableType.FromType(null);

            Assert.IsTrue(none.IsEmpty);
            Assert.IsTrue(
                none.Value == null,
                "Value should be null for SerializableType from null"
            );
            Assert.AreEqual(string.Empty, none.AssemblyQualifiedName);
            Assert.IsTrue(implicitNone.IsEmpty);
            Assert.IsTrue(
                implicitNone.Value == null,
                "Value should be null for implicit SerializableType from null"
            );
            Assert.AreEqual(string.Empty, implicitNone.AssemblyQualifiedName);
            Assert.IsTrue(none == null);
            Assert.IsTrue(null == none);
            Assert.IsTrue(implicitNone == null);
            Assert.IsTrue(null == implicitNone);
            Assert.IsFalse(implicitNone.Equals(null));
            Assert.IsTrue(implicitNone.EqualsType(null));
            Assert.IsTrue(none.EqualsType(null));
            Assert.IsTrue((Type)none == null, "Implicit cast to Type should be null");
            Assert.IsTrue((Type)implicitNone == null, "Implicit cast to Type should be null");
        }

        [Test]
        public void FromSerializedNameTrimsWhitespace()
        {
            string raw = $"  {typeof(List<int>).AssemblyQualifiedName}  ";
            SerializableType wrapper = SerializableType.FromSerializedName(raw);

            Assert.IsFalse(wrapper.IsEmpty);
            Assert.AreEqual(typeof(List<int>), wrapper.Value);
        }

        [Test]
        public void FromSerializedNameWithMissingAssemblyReturnsPlaceholder()
        {
            SerializableType unresolved = SerializableType.FromSerializedName(
                "Example.MissingType, MissingAssembly"
            );

            Assert.IsFalse(
                unresolved.IsEmpty,
                "Serialized name should be preserved even when unresolved."
            );
            Assert.IsTrue(unresolved.Value == null, "Value should be null for unresolved type");
            Assert.IsFalse(string.IsNullOrEmpty(unresolved.AssemblyQualifiedName));
            StringAssert.Contains("MissingAssembly", unresolved.DisplayName);
        }

        [Test]
        public void FromSerializedNameKeepsOriginalStringWhenUnresolved()
        {
            SerializableType unresolved = SerializableType.FromSerializedName(MissingTypeName);

            Assert.AreEqual(MissingTypeName, unresolved.AssemblyQualifiedName);
            Assert.IsTrue(unresolved.Value == null, "Value should be null for unresolved type");
            Assert.IsFalse(unresolved.EqualsType(typeof(int)));
        }

        [Test]
        public void DisplayNameUsesNamespaceQualifiedType()
        {
            SerializableType serializable = new(typeof(Dictionary<string, int>));
            string displayName = serializable.DisplayName;

            StringAssert.Contains("System.Collections.Generic.Dictionary", displayName);
            Assert.IsFalse(
                displayName.Contains(
                    typeof(Dictionary<string, int>).Assembly.GetName().Name,
                    StringComparison.Ordinal
                ),
                "Assembly name should not be included in the display label."
            );
        }

        [Test]
        public void JsonSerializationRoundTripsType()
        {
            SerializableType serializable = new(typeof(Dictionary<string, int>));
            string json = JsonSerializer.Serialize(serializable);

            SerializableType roundTripped = JsonSerializer.Deserialize<SerializableType>(json);
            Assert.IsTrue(roundTripped.Value != null, "Deserialized type should not be null");
            Assert.AreEqual(typeof(Dictionary<string, int>), roundTripped.Value);

            string nullJson = JsonSerializer.Serialize(default(SerializableType));
            Assert.AreEqual("null", nullJson);

            SerializableType nullRoundTrip = JsonSerializer.Deserialize<SerializableType>("null");
            Assert.IsTrue(nullRoundTrip.IsEmpty);
            Assert.IsTrue(
                nullRoundTrip.Value == null,
                "Null SerializableType should have null Value"
            );
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ProtoSerializationRoundTripsType()
        {
            SerializableType serializable = new(typeof(List<float>));
            using MemoryStream stream = new();
            Serializer.Serialize(stream, serializable);
            stream.Position = 0;

            SerializableType roundTripped = Serializer.Deserialize<SerializableType>(stream);
            Assert.AreEqual(typeof(List<float>), roundTripped.Value);

            stream.SetLength(0);
            stream.Position = 0;
            SerializableType empty = default;
            Serializer.Serialize(stream, empty);
            stream.Position = 0;

            SerializableType roundTrippedEmpty = Serializer.Deserialize<SerializableType>(stream);
            Assert.IsTrue(roundTrippedEmpty.IsEmpty);
            Assert.IsTrue(
                roundTrippedEmpty.Value == null,
                "Empty SerializableType should have null Value after Proto round-trip"
            );
        }

        [Test]
        public void CatalogContainsSerializableType()
        {
            string[] names = SerializableTypeCatalog.GetAssemblyQualifiedNames();
            string expected = typeof(SerializableType).AssemblyQualifiedName;

            CollectionAssert.Contains(names, expected);

            IReadOnlyList<SerializableTypeCatalog.SerializableTypeDescriptor> filtered =
                SerializableTypeCatalog.GetFilteredDescriptors("SerializableType");

            bool found = false;
            for (int index = 0; index < filtered.Count; index++)
            {
                SerializableTypeCatalog.SerializableTypeDescriptor descriptor = filtered[index];
                if (descriptor.Type == typeof(SerializableType))
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found, "SerializableType was not found in the filtered descriptor list.");
        }

        [Test]
        public void ResolvingSimpleTypesDoesNotBuildDescriptorCatalog()
        {
            IReadOnlyList<string> original = SerializableTypeCatalog.GetActiveIgnorePatterns();
            bool wasConfigured = !ReferenceEquals(
                original,
                SerializableTypeCatalog.GetDefaultIgnorePatterns()
            );
            string[] backup = original.ToArray();

            try
            {
                SerializableTypeCatalog.ConfigureTypeNameIgnorePatterns(
                    new[] { "^TypeThatCannotExistForDescriptorCacheTest$" }
                );
                Assert.IsFalse(SerializableTypeCatalog.IsDescriptorCacheInitializedForTesting);

                Type resolved = SerializableTypeCatalog.Resolve(
                    typeof(SerializableType).AssemblyQualifiedName
                );

                Assert.AreSame(typeof(SerializableType), resolved);
                Assert.IsFalse(SerializableTypeCatalog.IsDescriptorCacheInitializedForTesting);

                Type missing = SerializableTypeCatalog.Resolve(MissingTypeName);
                Assert.IsTrue(missing == null);
                Assert.IsFalse(SerializableTypeCatalog.IsDescriptorCacheInitializedForTesting);

                Type moved = SerializableTypeCatalog.Resolve(
                    $"{typeof(SerializableType).FullName}, MissingAssembly"
                );
                Assert.AreSame(typeof(SerializableType), moved);
                Assert.IsFalse(SerializableTypeCatalog.IsDescriptorCacheInitializedForTesting);

                string movedGeneric = BuildAssemblyQualifiedName(typeof(List<SerializableType>))
                    .Replace(typeof(SerializableType).Assembly.FullName, "MissingAssembly");
                Assert.AreSame(
                    typeof(List<SerializableType>),
                    SerializableTypeCatalog.Resolve(movedGeneric)
                );
                Assert.IsFalse(SerializableTypeCatalog.IsDescriptorCacheInitializedForTesting);
            }
            finally
            {
                SerializableTypeCatalog.ConfigureTypeNameIgnorePatterns(
                    wasConfigured ? backup : null
                );
            }
        }

        [Test]
        public void ResolvesNestedGenericAfterArgumentAssemblyMove()
        {
            Type[] expectedTypes =
            {
                typeof(Dictionary<string, List<SerializableType[]>>),
                typeof(List<SerializableType[][]>),
                typeof(List<SerializableType[,]>),
                typeof(List<CompilerGeneratedProbe>),
            };

            foreach (Type expected in expectedTypes)
            {
                string original = BuildAssemblyQualifiedName(expected);
                Assert.AreSame(expected, Type.GetType(original, throwOnError: false));
                string argumentAssembly = expected.GetGenericArguments()[0].Assembly.FullName;
                if (expected == typeof(Dictionary<string, List<SerializableType[]>>))
                {
                    argumentAssembly = typeof(SerializableType).Assembly.FullName;
                }

                string moved = original.Replace(argumentAssembly, "MissingAssembly");
                Assert.AreNotEqual(original, moved);
                Assert.IsTrue(Type.GetType(moved, throwOnError: false) == null);
                Assert.AreSame(expected, SerializableTypeCatalog.Resolve(moved));
                Assert.AreSame(expected, SerializableType.FromSerializedName(moved).Value);
            }
        }

        [Test]
        public void ResolvesGenericAfterOuterAssemblyMove()
        {
            Type expected = typeof(List<SerializableType>);
            string original = BuildAssemblyQualifiedName(expected);
            string outerAssembly = expected.Assembly.FullName;
            Assert.IsTrue(original.EndsWith(outerAssembly, StringComparison.Ordinal));
            string moved =
                original.Substring(0, original.Length - outerAssembly.Length) + "MissingAssembly";

            Assert.IsTrue(Type.GetType(moved, throwOnError: false) == null);
            Assert.AreSame(expected, SerializableTypeCatalog.Resolve(moved));
        }

        [Test]
        public void GenericResolutionRejectsMissingOrMalformedArguments()
        {
            string original = BuildAssemblyQualifiedName(typeof(List<SerializableType>));
            string missing = original.Replace(typeof(SerializableType).FullName, "Missing.Type");
            string malformed = original.Replace("]]", "]");

            Assert.AreNotEqual(original, missing);
            Assert.AreNotEqual(original, malformed);
            Assert.IsTrue(SerializableTypeCatalog.Resolve(missing) == null);
            Assert.IsTrue(SerializableTypeCatalog.Resolve(malformed) == null);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void GenericResolutionRejectsAmbiguousComponentNames()
        {
            const string fullName = "WallstopStudios.UnityHelpers.Tests.AmbiguousTypeComponent";
            AssemblyBuilder firstAssembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("SerializableTypeAmbiguityFirst"),
                AssemblyBuilderAccess.Run
            );
            AssemblyBuilder secondAssembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("SerializableTypeAmbiguitySecond"),
                AssemblyBuilderAccess.Run
            );
            Type first = firstAssembly
                .DefineDynamicModule("First")
                .DefineType(fullName)
                .CreateType();
            Type second = secondAssembly
                .DefineDynamicModule("Second")
                .DefineType(fullName)
                .CreateType();

            Assert.AreEqual(first.FullName, second.FullName);
            Assert.AreSame(
                first,
                SerializableTypeCatalog.ResolveUniqueLoadedType(
                    fullName,
                    new List<Assembly> { firstAssembly }
                )
            );
            Assert.AreSame(
                second,
                SerializableTypeCatalog.ResolveUniqueLoadedType(
                    fullName,
                    new List<Assembly> { secondAssembly }
                )
            );
            Assert.IsTrue(
                SerializableTypeCatalog.ResolveUniqueLoadedType(
                    fullName,
                    new List<Assembly> { firstAssembly, secondAssembly }
                ) == null
            );
        }

        [Test]
        public void SerializationPreservesResolvableStaleGenericName()
        {
            Type expected = typeof(List<SerializableType>);
            string moved = BuildAssemblyQualifiedName(expected)
                .Replace(typeof(SerializableType).Assembly.FullName, "MissingAssembly");
            SerializableType wrapper = SerializableType.FromSerializedName(moved);
            Assert.AreSame(expected, wrapper.Value);

            string json = JsonSerializer.Serialize(wrapper);
            SerializableType fromJson = JsonSerializer.Deserialize<SerializableType>(json);
            Assert.AreEqual(moved, fromJson.AssemblyQualifiedName);
            Assert.AreSame(expected, fromJson.Value);

            object boxed = wrapper;
            UnityEngine.ISerializationCallbackReceiver callbacks =
                (UnityEngine.ISerializationCallbackReceiver)boxed;
            callbacks.OnAfterDeserialize();
            Assert.AreSame(expected, ((SerializableType)boxed).Value);
            callbacks.OnBeforeSerialize();
            SerializableType fromUnity = (SerializableType)boxed;
            Assert.AreEqual(BuildAssemblyQualifiedName(expected), fromUnity.AssemblyQualifiedName);
        }

        [Test]
        public void UnitySerializationNormalizesResolvedGenericAndPreservesMissingName()
        {
            Type expected = typeof(List<SerializableType>);
            string moved = BuildAssemblyQualifiedName(expected)
                .Replace(typeof(SerializableType).Assembly.FullName, "MissingAssembly");
            string fieldName = SerializableType.SerializedPropertyNames.AssemblyQualifiedName;
            string holderName = nameof(SerializableTypeHolder.reference);
            string movedJson = JsonSerializer.Serialize(
                new Dictionary<string, Dictionary<string, string>>
                {
                    [holderName] = new Dictionary<string, string> { [fieldName] = moved },
                }
            );

            SerializableTypeHolder movedHolder =
                UnityEngine.JsonUtility.FromJson<SerializableTypeHolder>(movedJson);
            using JsonDocument normalized = JsonDocument.Parse(
                UnityEngine.JsonUtility.ToJson(movedHolder)
            );
            Assert.AreEqual(
                BuildAssemblyQualifiedName(expected),
                normalized.RootElement.GetProperty(holderName).GetProperty(fieldName).GetString()
            );

            string missingJson = JsonSerializer.Serialize(
                new Dictionary<string, Dictionary<string, string>>
                {
                    [holderName] = new Dictionary<string, string> { [fieldName] = MissingTypeName },
                }
            );
            SerializableTypeHolder missingHolder =
                UnityEngine.JsonUtility.FromJson<SerializableTypeHolder>(missingJson);
            using JsonDocument preserved = JsonDocument.Parse(
                UnityEngine.JsonUtility.ToJson(missingHolder)
            );
            Assert.AreEqual(
                MissingTypeName,
                preserved.RootElement.GetProperty(holderName).GetProperty(fieldName).GetString()
            );
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ProtoSerializationPreservesResolvableStaleGenericName()
        {
            Type expected = typeof(List<SerializableType>);
            string moved = BuildAssemblyQualifiedName(expected)
                .Replace(typeof(SerializableType).Assembly.FullName, "MissingAssembly");
            SerializableType wrapper = SerializableType.FromSerializedName(moved);
            using MemoryStream stream = new();
            Serializer.Serialize(stream, wrapper);
            stream.Position = 0;

            SerializableType fromProto = Serializer.Deserialize<SerializableType>(stream);
            Assert.AreEqual(moved, fromProto.AssemblyQualifiedName);
            Assert.AreSame(expected, fromProto.Value);
        }

        [Test]
        public void CatalogExcludesCompilerGeneratedAndAnonymousTypes()
        {
            string[] names = SerializableTypeCatalog.GetAssemblyQualifiedNames();
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                Assert.IsFalse(
                    name.StartsWith("$", StringComparison.Ordinal),
                    $"Type '{name}' should not start with '$'."
                );
                StringAssert.DoesNotContain("<>", name, $"Type '{name}' should not contain '<>'.");
                StringAssert.DoesNotContain(
                    "AnonymousType",
                    name,
                    $"Type '{name}' should not contain 'AnonymousType'."
                );
                StringAssert.DoesNotContain(
                    "DisplayClass",
                    name,
                    $"Type '{name}' should not contain 'DisplayClass'."
                );
            }
        }

        [Test]
        public void CatalogMatchesPrefixOnly()
        {
            IReadOnlyList<SerializableTypeCatalog.SerializableTypeDescriptor> filtered =
                SerializableTypeCatalog.GetFilteredDescriptors("ictionary");

            Assert.IsEmpty(filtered);
        }

        [Test]
        public void CatalogSearchFindsTypeByTypeName()
        {
            IReadOnlyList<SerializableTypeCatalog.SerializableTypeDescriptor> filtered =
                SerializableTypeCatalog.GetFilteredDescriptors("SerializableType");

            bool found = false;
            for (int index = 0; index < filtered.Count; index++)
            {
                SerializableTypeCatalog.SerializableTypeDescriptor descriptor = filtered[index];
                if (descriptor.Type == typeof(SerializableType))
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found, "Type name prefix search should locate SerializableType.");
        }

        [Test]
        public void CatalogSearchMatchesAssemblyQualifiedNamePrefix()
        {
            string assemblyQualifiedName = typeof(SerializableType).AssemblyQualifiedName;
            Assert.IsFalse(string.IsNullOrEmpty(assemblyQualifiedName));

            IReadOnlyList<SerializableTypeCatalog.SerializableTypeDescriptor> filtered =
                SerializableTypeCatalog.GetFilteredDescriptors(assemblyQualifiedName);

            bool found = false;
            for (int index = 0; index < filtered.Count; index++)
            {
                SerializableTypeCatalog.SerializableTypeDescriptor descriptor = filtered[index];
                if (descriptor.Type == typeof(SerializableType))
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(
                found,
                "Assembly qualified name prefix search should locate SerializableType."
            );
        }

        [Test]
        public void CatalogSearchReflectsUpdatedIgnorePatterns()
        {
            IReadOnlyList<SerializableTypeCatalog.SerializableTypeDescriptor> initial =
                SerializableTypeCatalog.GetFilteredDescriptors("SerializableType");
            bool initiallyFound = false;
            for (int index = 0; index < initial.Count; index++)
            {
                SerializableTypeCatalog.SerializableTypeDescriptor descriptor = initial[index];
                if (descriptor.Type == typeof(SerializableType))
                {
                    initiallyFound = true;
                    break;
                }
            }

            Assert.IsTrue(initiallyFound, "Baseline search should include SerializableType.");

            IReadOnlyList<string> original = SerializableTypeCatalog.GetActiveIgnorePatterns();
            bool wasConfigured = !ReferenceEquals(
                original,
                SerializableTypeCatalog.GetDefaultIgnorePatterns()
            );
            string[] backup = original.ToArray();

            try
            {
                SerializableTypeCatalog.ConfigureTypeNameIgnorePatterns(
                    new[] { "SerializableType" }
                );

                IReadOnlyList<SerializableTypeCatalog.SerializableTypeDescriptor> filtered =
                    SerializableTypeCatalog.GetFilteredDescriptors("SerializableType");

                bool found = false;
                for (int index = 0; index < filtered.Count; index++)
                {
                    SerializableTypeCatalog.SerializableTypeDescriptor descriptor = filtered[index];
                    if (descriptor.Type == typeof(SerializableType))
                    {
                        found = true;
                        break;
                    }
                }

                Assert.IsFalse(
                    found,
                    "Configured ignore pattern should remove SerializableType from results."
                );
            }
            finally
            {
                SerializableTypeCatalog.ConfigureTypeNameIgnorePatterns(
                    wasConfigured ? backup : null
                );
            }

            IReadOnlyList<SerializableTypeCatalog.SerializableTypeDescriptor> restored =
                SerializableTypeCatalog.GetFilteredDescriptors("SerializableType");

            bool restoredFound = false;
            for (int index = 0; index < restored.Count; index++)
            {
                SerializableTypeCatalog.SerializableTypeDescriptor descriptor = restored[index];
                if (descriptor.Type == typeof(SerializableType))
                {
                    restoredFound = true;
                    break;
                }
            }

            Assert.IsTrue(restoredFound, "Restored patterns should reintroduce SerializableType.");
        }

        [Test]
        public void ShouldSkipTypeRecognizesCompilerGeneratedAttribute()
        {
            Assert.IsTrue(
                SerializableTypeCatalog.ShouldSkipType(typeof(CompilerGeneratedProbe)),
                "Compiler generated types should be excluded from catalog results."
            );
        }

        [Test]
        public void TryGetValueFailsForUnknownType()
        {
            SerializableType unresolved = SerializableType.FromSerializedName(MissingTypeName);

            bool resolved = unresolved.TryGetValue(out Type resolvedType);
            Assert.IsFalse(resolved);
            Assert.IsTrue(resolvedType == null, "Resolved type should be null for unknown type");
            Assert.IsTrue(unresolved.Value == null, "Value should be null for unknown type");
            Assert.IsFalse(unresolved.IsEmpty);
        }

        [Test]
        public void JsonSerializationPreservesMissingType()
        {
            SerializableType unresolved = SerializableType.FromSerializedName(MissingTypeName);

            string json = JsonSerializer.Serialize(unresolved);
            SerializableType roundTripped = JsonSerializer.Deserialize<SerializableType>(json);

            Assert.IsFalse(roundTripped.IsEmpty);
            Assert.IsTrue(roundTripped.Value == null);
            Assert.AreEqual(MissingTypeName, roundTripped.AssemblyQualifiedName);
            StringAssert.Contains(MissingTypeName, roundTripped.DisplayName);
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void ProtoSerializationPreservesMissingType()
        {
            SerializableType unresolved = SerializableType.FromSerializedName(MissingTypeName);
            using MemoryStream stream = new();
            Serializer.Serialize(stream, unresolved);
            stream.Position = 0;

            SerializableType roundTripped = Serializer.Deserialize<SerializableType>(stream);
            Assert.IsFalse(roundTripped.IsEmpty);
            Assert.IsTrue(
                roundTripped.Value == null,
                "Value should be null for missing type after Proto round-trip"
            );
            Assert.AreEqual(MissingTypeName, roundTripped.AssemblyQualifiedName);
        }

        [Test]
        public void ConfigureTypeNameIgnorePatternsOverridesDefaults()
        {
            IReadOnlyList<string> original = SerializableTypeCatalog.GetActiveIgnorePatterns();
            bool wasConfigured = !ReferenceEquals(
                original,
                SerializableTypeCatalog.GetDefaultIgnorePatterns()
            );
            string[] backup = original.ToArray();

            try
            {
                SerializableTypeCatalog.ConfigureTypeNameIgnorePatterns(
                    new[] { "^System\\.Int32$" }
                );

                Assert.IsTrue(
                    SerializableTypeCatalog.ShouldSkipType(typeof(int)),
                    "Configured regex should cause System.Int32 to be skipped."
                );
                Assert.IsFalse(
                    SerializableTypeCatalog.ShouldSkipType(typeof(string)),
                    "Other types must remain discoverable when only System.Int32 is ignored."
                );
            }
            finally
            {
                SerializableTypeCatalog.ConfigureTypeNameIgnorePatterns(
                    wasConfigured ? backup : null
                );
            }
        }

        [Test]
        public void PatternStatsReportsInvalidExpressions()
        {
            SerializableTypeCatalog.PatternStats stats = SerializableTypeCatalog.GetPatternStats(
                "["
            );

            Assert.IsFalse(stats.IsValid, "Invalid regex should be marked as invalid.");
            Assert.IsTrue(
                !string.IsNullOrEmpty(stats.ErrorMessage),
                "Invalid regex should provide an explanatory error message."
            );
        }

        [CompilerGenerated]
        private sealed class CompilerGeneratedProbe { }

        [Serializable]
        private sealed class SerializableTypeHolder
        {
            public SerializableType reference;
        }
    }
}
