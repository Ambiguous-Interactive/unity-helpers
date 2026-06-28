// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializableTypeTests
    {
        private const string MissingTypeName = "Missing.Type, MissingAssembly";

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
            Assert.IsTrue(serializable.Equals(null));
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
            Assert.IsTrue(implicitNone.Equals(null));
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
        public void CatalogExcludesCompilerGeneratedAndAnonymousTypes()
        {
            string[] names = SerializableTypeCatalog.GetAssemblyQualifiedNames();
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
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
    }
}
