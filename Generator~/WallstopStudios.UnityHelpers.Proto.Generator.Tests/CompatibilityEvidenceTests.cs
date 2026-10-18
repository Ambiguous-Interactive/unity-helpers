// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tests.Serialization;

    [TestFixture]
    public sealed class CompatibilityEvidenceTests
    {
        private static IEnumerable<TestCaseData> Witnesses()
        {
            using JsonDocument ledger = JsonDocument.Parse(
                File.ReadAllText(
                    Path.Combine(
                        ContractMirrorTests.RepositoryRoot(),
                        "Generator~",
                        "WallstopStudios.UnityHelpers.Proto.Generator.Tests",
                        "CompatibilityEvidence.v1.json"
                    )
                )
            );
            foreach (
                JsonElement entry in ledger.RootElement.GetProperty("capabilities").EnumerateArray()
            )
            {
                List<string> oracles = new List<string>();
                foreach (JsonElement oracle in entry.GetProperty("oracles").EnumerateArray())
                {
                    oracles.Add(oracle.GetString());
                }
                CollectionAssert.AreEquivalent(
                    new[] { "v2", "v3" },
                    oracles,
                    "Every current witness must run in both isolated oracle lanes."
                );
                Assert.Less(0, entry.GetProperty("readerDirections").GetArrayLength());
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.GetProperty("claim").GetString()));
                yield return new TestCaseData(
                    entry.GetProperty("id").GetString(),
                    entry.GetProperty("witness").GetString(),
                    entry.GetProperty("classification").GetString()
                ).SetName("CompatibilityEvidence." + entry.GetProperty("id").GetString());
            }
        }

        private static Dictionary<string, Action> RegisteredWitnesses()
        {
            return new Dictionary<string, Action>(StringComparer.Ordinal)
            {
                [
                    nameof(DiagnosticTests)
                        + "."
                        + nameof(DiagnosticTests.ANonPartialContractIsAnError)
                ] = new DiagnosticTests().ANonPartialContractIsAnError,
                [
                    nameof(DiagnosticTests)
                        + "."
                        + nameof(DiagnosticTests.AContractNestedInsideAGenericTypeIsARefusalToo)
                ] = new DiagnosticTests().AContractNestedInsideAGenericTypeIsARefusalToo,
                [
                    nameof(PackageContractShapeTests)
                        + "."
                        + nameof(
                            PackageContractShapeTests.ReadonlySurrogatedMembersEncodeAsTheOracleEncodesThem
                        )
                ] =
                    new PackageContractShapeTests().ReadonlySurrogatedMembersEncodeAsTheOracleEncodesThem,
                [
                    nameof(PackageContractShapeTests)
                        + "."
                        + nameof(
                            PackageContractShapeTests.EveryRangeClosureEncodesAsTheOracleEncodesIt
                        )
                ] = new PackageContractShapeTests().EveryRangeClosureEncodesAsTheOracleEncodesIt,
                [
                    nameof(PackageContractShapeTests)
                        + "."
                        + nameof(
                            PackageContractShapeTests.ANullableShapeEncodesAsTheOracleEncodesIt
                        )
                ] = new PackageContractShapeTests().ANullableShapeEncodesAsTheOracleEncodesIt,
                [
                    nameof(PackageContractShapeTests)
                        + "."
                        + nameof(
                            PackageContractShapeTests.AListShapedContractInteroperatesInBothDirections
                        )
                ] =
                    new PackageContractShapeTests().AListShapedContractInteroperatesInBothDirections,
                [
                    nameof(PackageContractShapeTests)
                        + "."
                        + nameof(
                            PackageContractShapeTests.ASubtypeEncodesTheSameUnderItsOwnDeclaredTypeAsUnderItsBase
                        )
                ] =
                    new PackageContractShapeTests().ASubtypeEncodesTheSameUnderItsOwnDeclaredTypeAsUnderItsBase,
                [
                    nameof(RootMarshalTests)
                        + "."
                        + nameof(RootMarshalTests.TheMarshalReadsWhatTheShippedWrapperPathWrote)
                ] = new RootMarshalTests().TheMarshalReadsWhatTheShippedWrapperPathWrote,
                [
                    nameof(RootMarshalTests)
                        + "."
                        + nameof(RootMarshalTests.TheOracleDecodesWhatTheMarshalWrote)
                ] = new RootMarshalTests().TheOracleDecodesWhatTheMarshalWrote,
                [
                    nameof(WProtoConsumerMigrationTests)
                        + "."
                        + nameof(
                            WProtoConsumerMigrationTests.CombinedMigrationReadsRetainedGoldenBytes
                        )
                ] = new WProtoConsumerMigrationTests().CombinedMigrationReadsRetainedGoldenBytes,
                [
                    nameof(RuntimeModelConsumerMigrationTests)
                        + "."
                        + nameof(
                            RuntimeModelConsumerMigrationTests.RuntimeDefaultsHaveGoldenBytesAndAChangedDefaultIsDetectable
                        )
                ] =
                    new RuntimeModelConsumerMigrationTests().RuntimeDefaultsHaveGoldenBytesAndAChangedDefaultIsDetectable,
                [
                    nameof(PackingOptOutTests)
                        + "."
                        + nameof(PackingOptOutTests.UnpackedEnumWritePolicyReadsMixedRuns)
                ] = new PackingOptOutTests().UnpackedEnumWritePolicyReadsMixedRuns,
                [
                    nameof(MapDifferentialTests)
                        + "."
                        + nameof(MapDifferentialTests.AValueEqualToItsDefaultIsOmittedFromTheEntry)
                ] = new MapDifferentialTests().AValueEqualToItsDefaultIsOmittedFromTheEntry,
                [
                    nameof(FacadeTests)
                        + "."
                        + nameof(
                            FacadeTests.ACorruptPayloadIsReportedRatherThanHandedBackToProtobufNet
                        )
                ] = new FacadeTests().ACorruptPayloadIsReportedRatherThanHandedBackToProtobufNet,
                [
                    nameof(RootMarshalTests)
                        + "."
                        + nameof(
                            RootMarshalTests.RegisterAfterGenericResolutionReplacesTheCachedFormatter
                        )
                ] = new RootMarshalTests().RegisterAfterGenericResolutionReplacesTheCachedFormatter,
                [
                    nameof(MapDifferentialTests)
                        + "."
                        + nameof(MapDifferentialTests.EnumDoubleMapMatchesPinnedV3Bytes)
                ] = () =>
                    new MapDifferentialTests().EnumDoubleMapMatchesPinnedV3Bytes(
                        (ZeroMapKeyKind)0,
                        0d,
                        "32020800"
                    ),
            };
        }

        private static bool IsSelectedMapCase(AttributeData attribute)
        {
            if (
                !string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    typeof(TestCaseAttribute).FullName,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }
            List<string> values = new List<string>();
            foreach (TypedConstant argument in attribute.ConstructorArguments)
            {
                if (argument.Kind == TypedConstantKind.Array)
                {
                    foreach (TypedConstant item in argument.Values)
                    {
                        values.Add(Convert.ToString(item.Value, CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    values.Add(Convert.ToString(argument.Value, CultureInfo.InvariantCulture));
                }
            }
            return values.SequenceEqual(new[] { "0", "0", "32020800" }, StringComparer.Ordinal);
        }

        private static void AssertNoExecutionConstraints(
            INamedTypeSymbol type,
            string selectedMethod
        )
        {
            for (INamedTypeSymbol current = type; current != null; current = current.BaseType)
            {
                foreach (AttributeData attribute in current.GetAttributes())
                {
                    AssertAllowedExecutionAttribute(attribute, true);
                }
                foreach (ISymbol member in current.GetMembers())
                {
                    bool selected = string.Equals(
                        member.Name,
                        selectedMethod,
                        StringComparison.Ordinal
                    );
                    foreach (AttributeData attribute in member.GetAttributes())
                    {
                        bool selectedCase = selected;
                        if (
                            selected
                            && string.Equals(
                                attribute.AttributeClass?.Name,
                                nameof(TestCaseAttribute),
                                StringComparison.Ordinal
                            )
                        )
                        {
                            selectedCase = IsSelectedMapCase(attribute);
                        }
                        AssertAllowedExecutionAttribute(attribute, selectedCase);
                    }
                }
            }
        }

        private static void AssertAllowedExecutionAttribute(AttributeData attribute, bool selected)
        {
            if (
                !string.Equals(
                    attribute.AttributeClass?.ContainingNamespace.ToDisplayString(),
                    typeof(TestAttribute).Namespace,
                    StringComparison.Ordinal
                )
            )
            {
                return;
            }
            string[] lifecycle =
            {
                nameof(SetUpAttribute),
                nameof(TearDownAttribute),
                nameof(OneTimeSetUpAttribute),
                nameof(OneTimeTearDownAttribute),
            };
            Assert.IsFalse(
                lifecycle.Contains(attribute.AttributeClass.Name, StringComparer.Ordinal),
                "Direct evidence execution cannot bypass " + attribute.AttributeClass.Name
            );
            if (!selected)
            {
                return;
            }
            Assert.IsFalse(
                string.Equals(
                    attribute.AttributeClass.Name,
                    nameof(IgnoreAttribute),
                    StringComparison.Ordinal
                )
                    || string.Equals(
                        attribute.AttributeClass.Name,
                        nameof(ExplicitAttribute),
                        StringComparison.Ordinal
                    ),
                "Direct evidence execution cannot bypass a disabled witness."
            );
            foreach (KeyValuePair<string, TypedConstant> option in attribute.NamedArguments)
            {
                Assert.IsFalse(
                    string.Equals(option.Key, "Ignore", StringComparison.Ordinal)
                        || string.Equals(option.Key, "IgnoreReason", StringComparison.Ordinal)
                        || (
                            string.Equals(option.Key, "Explicit", StringComparison.Ordinal)
                            && Equals(option.Value.Value, true)
                        ),
                    "Direct evidence execution cannot bypass a disabled NUnit case."
                );
            }
        }

        private static void AssertWitnessIsTest(string witness)
        {
            int separator = witness.IndexOf('.');
            Assert.Less(0, separator);
            string fixture = witness.Substring(0, separator);
            string method = witness.Substring(separator + 1);
            CSharpCompilation compilation = CompatibilityInventory.Compile(
                Array.Empty<SyntaxTree>()
            );
            INamedTypeSymbol type =
                compilation.GetTypeByMetadataName(
                    typeof(CompatibilityEvidenceTests).Namespace + "." + fixture
                )
                ?? compilation.GetTypeByMetadataName(
                    typeof(WProtoConsumerMigrationTests).Namespace + "." + fixture
                );
            Assert.IsTrue(type != null, "Missing fixture " + fixture);
            AssertNoExecutionConstraints(type, method);
            ISymbol[] methods = type.GetMembers(method).ToArray();
            Assert.AreEqual(1, methods.Length, "Missing or ambiguous witness " + witness);
            IMethodSymbol executable = (IMethodSymbol)methods[0];
            if (executable.Parameters.Length != 0)
            {
                Assert.AreEqual(
                    nameof(MapDifferentialTests)
                        + "."
                        + nameof(MapDifferentialTests.EnumDoubleMapMatchesPinnedV3Bytes),
                    witness
                );
                Assert.IsTrue(
                    executable.GetAttributes().Any(attribute => IsSelectedMapCase(attribute)),
                    "The directly invoked arguments must still be a declared NUnit case."
                );
            }
            Assert.IsTrue(
                methods[0]
                    .GetAttributes()
                    .Any(attribute =>
                        string.Equals(
                            attribute.AttributeClass?.ToDisplayString(),
                            typeof(TestAttribute).FullName,
                            StringComparison.Ordinal
                        )
                        || string.Equals(
                            attribute.AttributeClass?.ToDisplayString(),
                            typeof(TestCaseAttribute).FullName,
                            StringComparison.Ordinal
                        )
                    ),
                witness + " is not an executable NUnit test."
            );
        }

        private static IEnumerable<TestCaseData> BoundFixtureSources()
        {
            string path = Path.Combine(
                ContractMirrorTests.RepositoryRoot(),
                "Generator~",
                "WallstopStudios.UnityHelpers.Proto.Generator.Tests",
                "CompatibilityCaseBindings.v1.json"
            );
            using JsonDocument bindings = JsonDocument.Parse(File.ReadAllText(path));
            Dictionary<string, List<string>> selectors = new Dictionary<string, List<string>>(
                StringComparer.Ordinal
            );
            foreach (
                JsonElement selector in bindings
                    .RootElement.GetProperty("selectors")
                    .EnumerateArray()
            )
            {
                string fixture = selector.GetProperty("fixture").GetString();
                if (!selectors.TryGetValue(fixture, out List<string> methods))
                {
                    methods = new List<string>();
                    selectors.Add(fixture, methods);
                }
                string id = selector.GetProperty("id").GetString();
                int separator = id.LastIndexOf('.');
                Assert.Less(0, separator, id);
                methods.Add(id.Substring(separator + 1));
            }
            int fixtureCount = 0;
            foreach (
                JsonElement fixture in bindings.RootElement.GetProperty("fixtures").EnumerateArray()
            )
            {
                string name = fixture.GetProperty("fixture").GetString();
                Assert.IsTrue(selectors.TryGetValue(name, out List<string> methods), name);
                yield return new TestCaseData(
                    fixture.GetProperty("sourcePath").GetString(),
                    name,
                    methods.ToArray()
                ).SetName("CompatibilitySource." + name);
                ++fixtureCount;
            }
            Assert.Less(0, fixtureCount);
            Assert.AreEqual(selectors.Count, fixtureCount);
        }

        private static string SourceDeclarationName(SyntaxNode declaration)
        {
            List<string> parts = new List<string>();
            foreach (SyntaxNode node in declaration.AncestorsAndSelf())
            {
                if (node is TypeDeclarationSyntax parentType)
                {
                    int arity =
                        parentType.TypeParameterList == null
                            ? 0
                            : parentType.TypeParameterList.Parameters.Count;
                    parts.Insert(
                        0,
                        parentType.Identifier.ValueText
                            + (
                                arity == 0
                                    ? string.Empty
                                    : "`" + arity.ToString(CultureInfo.InvariantCulture)
                            )
                    );
                }
                else if (node is NamespaceDeclarationSyntax containingNamespace)
                {
                    parts.Insert(0, containingNamespace.Name.ToString());
                }
            }
            return string.Join(".", parts);
        }

        private static HashSet<string> LocalSourceDeclarations(SyntaxNode root)
        {
            HashSet<string> declarations = new HashSet<string>(StringComparer.Ordinal);
            foreach (SyntaxNode node in root.DescendantNodes())
            {
                if (!(node is TypeDeclarationSyntax) && !(node is NamespaceDeclarationSyntax))
                {
                    continue;
                }
                string name = SourceDeclarationName(node);
                declarations.Add(name);
                int separator = name.LastIndexOf('.');
                while (0 < separator)
                {
                    name = name.Substring(0, separator);
                    declarations.Add(name);
                    separator = name.LastIndexOf('.');
                }
            }
            return declarations;
        }

        private static bool SourceNameIsShadowed(
            SyntaxNode context,
            string name,
            HashSet<string> declarations
        )
        {
            bool absolute = name.StartsWith("global::", StringComparison.Ordinal);
            string normalized = absolute ? name.Substring("global::".Length) : name;
            int separator = normalized.IndexOf('.');
            string first = separator < 0 ? normalized : normalized.Substring(0, separator);
            List<string> scopes = new List<string> { string.Empty };
            if (!absolute)
            {
                foreach (SyntaxNode ancestor in context.Ancestors())
                {
                    if (ancestor is TypeDeclarationSyntax || ancestor is NamespaceDeclarationSyntax)
                    {
                        string scope = SourceDeclarationName(ancestor);
                        scopes.Add(scope + ".");
                        if (ancestor is NamespaceDeclarationSyntax)
                        {
                            int scopeSeparator = scope.LastIndexOf('.');
                            while (0 < scopeSeparator)
                            {
                                scope = scope.Substring(0, scopeSeparator);
                                scopes.Add(scope + ".");
                                scopeSeparator = scope.LastIndexOf('.');
                            }
                        }
                    }
                }
            }
            foreach (string scope in scopes)
            {
                if (
                    declarations.Contains(scope + first)
                    || (separator < 0 && declarations.Contains(scope + first + "Attribute"))
                )
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsTestAttributeName(string name)
        {
            string normalized = name.Replace("global::", string.Empty);
            if (!normalized.EndsWith("Attribute", StringComparison.Ordinal))
            {
                normalized += "Attribute";
            }
            return string.Equals(
                    normalized,
                    typeof(TestAttribute).FullName,
                    StringComparison.Ordinal
                )
                || string.Equals(
                    normalized,
                    typeof(TestCaseAttribute).FullName,
                    StringComparison.Ordinal
                )
                || string.Equals(
                    normalized,
                    typeof(TestCaseSourceAttribute).FullName,
                    StringComparison.Ordinal
                )
                || string.Equals(
                    normalized,
                    "UnityEngine.TestTools.UnityTestAttribute",
                    StringComparison.Ordinal
                );
        }

        private static bool HasSourceTestAttribute(
            MethodDeclarationSyntax method,
            HashSet<string> declarations
        )
        {
            List<UsingDirectiveSyntax> imports = new List<UsingDirectiveSyntax>();
            foreach (SyntaxNode ancestor in method.Ancestors())
            {
                if (ancestor is NamespaceDeclarationSyntax containingNamespace)
                {
                    imports.AddRange(containingNamespace.Usings);
                }
                else if (ancestor is CompilationUnitSyntax unit)
                {
                    imports.AddRange(unit.Usings);
                }
            }
            foreach (AttributeListSyntax list in method.AttributeLists)
            {
                foreach (AttributeSyntax attribute in list.Attributes)
                {
                    string name = attribute.Name.ToString();
                    bool aliasMatched = false;
                    foreach (UsingDirectiveSyntax import in imports)
                    {
                        if (import.Alias == null)
                        {
                            continue;
                        }
                        string alias = import.Alias.Name.Identifier.ValueText;
                        bool suffixedAlias =
                            name.IndexOf('.') < 0
                            && string.Equals(alias, name + "Attribute", StringComparison.Ordinal);
                        if (
                            string.Equals(name, alias, StringComparison.Ordinal)
                            || name.StartsWith(alias + ".", StringComparison.Ordinal)
                            || suffixedAlias
                        )
                        {
                            string suffix = suffixedAlias
                                ? string.Empty
                                : name.Substring(alias.Length);
                            if (
                                IsTestAttributeName(import.Name + suffix)
                                && !SourceNameIsShadowed(attribute, name, declarations)
                                && !SourceNameIsShadowed(
                                    import,
                                    import.Name.ToString(),
                                    declarations
                                )
                            )
                            {
                                return true;
                            }
                            aliasMatched = true;
                            break;
                        }
                    }
                    if (aliasMatched)
                    {
                        continue;
                    }
                    if (
                        IsTestAttributeName(name)
                        && !SourceNameIsShadowed(attribute, name, declarations)
                    )
                    {
                        return true;
                    }
                    foreach (UsingDirectiveSyntax import in imports)
                    {
                        if (
                            import.Alias == null
                            && IsTestAttributeName(import.Name + "." + name)
                            && !SourceNameIsShadowed(attribute, name, declarations)
                            && !SourceNameIsShadowed(import, import.Name.ToString(), declarations)
                        )
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static void AssertSourceFixture(
            string source,
            string fixture,
            string[] methods,
            string[][] profiles
        )
        {
            Assert.Less(0, methods.Length, fixture);
            Assert.Less(0, profiles.Length, fixture);
            Assert.AreEqual(
                methods.Length,
                new HashSet<string>(methods, StringComparer.Ordinal).Count
            );
            HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);
            foreach (string[] profile in profiles)
            {
                SyntaxTree tree = CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: profile)
                );
                Assert.IsFalse(
                    tree.GetDiagnostics()
                        .Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                    fixture
                );
                TypeDeclarationSyntax[] types = tree.GetRoot()
                    .DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Where(type =>
                        string.Equals(
                            SourceDeclarationName(type),
                            fixture,
                            StringComparison.Ordinal
                        )
                    )
                    .ToArray();
                Assert.LessOrEqual(types.Length, 1, "Ambiguous source fixture " + fixture);
                if (types.Length == 0)
                {
                    continue;
                }
                HashSet<string> declarations = LocalSourceDeclarations(tree.GetRoot());
                foreach (string method in methods)
                {
                    MethodDeclarationSyntax[] candidates = types[0]
                        .Members.OfType<MethodDeclarationSyntax>()
                        .Where(member =>
                            string.Equals(
                                member.Identifier.ValueText,
                                method,
                                StringComparison.Ordinal
                            )
                        )
                        .ToArray();
                    Assert.LessOrEqual(candidates.Length, 1, fixture + "." + method);
                    if (candidates.Length == 0)
                    {
                        continue;
                    }
                    Assert.IsTrue(
                        candidates[0].Modifiers.Any(SyntaxKind.PublicKeyword),
                        fixture + "." + method
                    );
                    Assert.IsTrue(
                        HasSourceTestAttribute(candidates[0], declarations),
                        fixture + "." + method + " is not a declared NUnit or Unity test."
                    );
                    found.Add(method);
                }
            }
            CollectionAssert.AreEquivalent(
                methods,
                found,
                "Source declarations only; no case execution or backend qualification: " + fixture
            );
        }

        [TestCaseSource(nameof(BoundFixtureSources))]
        public void BoundFixtureSelectorsNameDeclaredSourceTests(
            string sourcePath,
            string fixture,
            string[] methods
        )
        {
            string[][] profiles =
            {
                Array.Empty<string>(),
                new[] { "PROTOBUF_NET_ORACLE_V2" },
                new[]
                {
                    "UNITY_5_3_OR_NEWER",
                    "UNITY_EDITOR",
                    "UNITY_INCLUDE_TESTS",
                    "WALLSTOP_PROTO",
                },
                new[]
                {
                    "UNITY_5_3_OR_NEWER",
                    "UNITY_EDITOR",
                    "UNITY_INCLUDE_TESTS",
                    "WALLSTOP_PROTO",
                    "WALLSTOP_PROTO_ONLY",
                },
            };
            AssertSourceFixture(
                File.ReadAllText(Path.Combine(ContractMirrorTests.RepositoryRoot(), sourcePath)),
                fixture,
                methods,
                profiles
            );
        }

        [TestCase(
            "namespace Claims { public class Other { [NUnit.Framework.Test] public void Witness() {} } }"
        )]
        [TestCase(
            "namespace Other { public class Evidence { [NUnit.Framework.Test] public void Witness() {} } }"
        )]
        [TestCase(
            "namespace Claims { public class Evidence { [NUnit.Framework.Test] public void Other() {} } }"
        )]
        [TestCase("namespace Claims { public class Evidence { public void Witness() {} } }")]
        [TestCase(
            "namespace Claims { public class Evidence { [Other.Test] public void Witness() {} } }"
        )]
        [TestCase(
            "namespace Claims { public class Evidence { [NUnit.Framework.Test] private void Witness() {} } }"
        )]
        [TestCase(
            "namespace Claims { public class Evidence { [NUnit.Framework.Test] public void Witness() {} [NUnit.Framework.TestCase(1)] public void Witness(int value) {} } }"
        )]
        [TestCase(
            "using NUnit.Framework; using Test = Other.TestAttribute; namespace Claims { public class Evidence { [Test] public void Witness() {} } }"
        )]
        [TestCase(
            "using NUnit.Framework; namespace Claims { public sealed class TestAttribute : System.Attribute {} public class Evidence { [Test] public void Witness() {} } }"
        )]
        [TestCase(
            "namespace Claims { namespace NUnit.Framework { public sealed class TestAttribute : System.Attribute {} } public class Evidence { [NUnit.Framework.Test] public void Witness() {} } }"
        )]
        [TestCase(
            "namespace Claims { public class NUnit { public class Framework { public sealed class TestAttribute : System.Attribute {} } } public class Evidence { [NUnit.Framework.Test] public void Witness() {} } }"
        )]
        [TestCase(
            "namespace Claims { public class Evidence<T> { [NUnit.Framework.Test] public void Witness() {} } }"
        )]
        [TestCase(
            "using Tests = NUnit.Framework; namespace Claims { public class Tests { public sealed class TestAttribute : System.Attribute {} } public class Evidence { [Tests.Test] public void Witness() {} } }"
        )]
        [TestCase(
            "using NUnit.Framework; using TestAttribute = Other.TestAttribute; namespace Claims { public class Evidence { [Test] public void Witness() {} } }"
        )]
        public void SourceSelectorsRejectMissingAmbiguousAndNonTestDeclarations(string source)
        {
            Assert.Throws<AssertionException>(() =>
                AssertSourceFixture(
                    source,
                    "Claims.Evidence",
                    new[] { "Witness" },
                    new[] { Array.Empty<string>() }
                )
            );
        }

        [Test]
        public void SourceSelectorsRejectAnEnclosingNamespaceAttributeShadow()
        {
            const string Source =
                "namespace Claims.NUnit.Framework { public sealed class TestAttribute : System.Attribute {} } namespace Claims.Nested { public class Evidence { [NUnit.Framework.Test] public void Witness() {} } }";
            Assert.Throws<AssertionException>(() =>
                AssertSourceFixture(
                    Source,
                    "Claims.Nested.Evidence",
                    new[] { "Witness" },
                    new[] { Array.Empty<string>() }
                )
            );
        }

        [TestCase("using NUnit.Framework;", "Test")]
        [TestCase("using NUnit.Framework;", "TestCase(1)")]
        [TestCase("using NUnit.Framework;", "TestCaseSource(\"Cases\")")]
        [TestCase("using Case = NUnit.Framework.TestAttribute;", "Case")]
        [TestCase("using Tests = NUnit.Framework;", "Tests.Test")]
        [TestCase("using TestAttribute = NUnit.Framework.TestAttribute;", "Test")]
        [TestCase("using UnityEngine.TestTools;", "UnityTest")]
        [TestCase(
            "using NUnit.Framework; namespace Other { public sealed class TestAttribute : System.Attribute {} }",
            "Test"
        )]
        [TestCase(
            "namespace Claims.NUnit.Framework { public sealed class TestAttribute : System.Attribute {} }",
            "global::NUnit.Framework.Test"
        )]
        public void SourceSelectorsRecognizeTestAttributesWithoutExecutingMethods(
            string imports,
            string attribute
        )
        {
            string source =
                imports
                + " namespace Claims { public class Evidence { [NUnit.Framework.SetUp] public void Prepare() { throw new System.Exception(); } ["
                + attribute
                + "] public void Witness() { throw new System.Exception(); } } }";
            AssertSourceFixture(
                source,
                "Claims.Evidence",
                new[] { "Witness" },
                new[] { Array.Empty<string>() }
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SourceSelectorsRespectConditionalProfiles(bool enabled)
        {
            const string Source =
                "namespace Claims { public class Evidence {\n#if WALLSTOP_PROTO_ONLY\n[NUnit.Framework.Test] public void Witness() {}\n#endif\n} }";
            string[] profile = enabled ? new[] { "WALLSTOP_PROTO_ONLY" } : Array.Empty<string>();
            if (enabled)
            {
                AssertSourceFixture(
                    Source,
                    "Claims.Evidence",
                    new[] { "Witness" },
                    new[] { profile }
                );
            }
            else
            {
                Assert.Throws<AssertionException>(() =>
                    AssertSourceFixture(
                        Source,
                        "Claims.Evidence",
                        new[] { "Witness" },
                        new[] { profile }
                    )
                );
            }
        }

        [Test]
        public void UnrelatedDisabledTestsDoNotInvalidateAWitness()
        {
            CSharpCompilation compilation = CompatibilityInventory.Compile(
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        "public class Evidence { [NUnit.Framework.Test, NUnit.Framework.Ignore(\"reason\")] public void Other() {} [NUnit.Framework.Test] public void Witness() {} }"
                    ),
                }
            );
            AssertNoExecutionConstraints(compilation.GetTypeByMetadataName("Evidence"), "Witness");
        }

        [TestCaseSource(nameof(Witnesses))]
        public void DeclaredEvidenceExecutesItsRegression(
            string id,
            string witness,
            string classification
        )
        {
            Assert.Contains(
                classification,
                new[]
                {
                    "identical-bytes",
                    "readable-alternate",
                    "known-oracle-defect",
                    "behavior",
                    "unsupported",
                },
                id
            );
            Dictionary<string, Action> registry = RegisteredWitnesses();
            Assert.IsTrue(registry.TryGetValue(witness, out Action execute), witness);
            AssertWitnessIsTest(witness);
            execute();
        }

        [Test]
        public void EvidenceAndExecutableRegistryHaveExactlyTheSameSubjects()
        {
            List<string> declared = new List<string>();
            foreach (TestCaseData data in Witnesses())
            {
                declared.Add((string)data.Arguments[1]);
            }
            Assert.Less(0, declared.Count);
            CollectionAssert.AreEquivalent(RegisteredWitnesses().Keys, declared);
            Assert.AreEqual(
                declared.Count,
                new HashSet<string>(declared, StringComparer.Ordinal).Count
            );
        }

        [TestCase("MissingFixture.MissingWitness")]
        [TestCase("ContractMirrorTests.RepositoryRoot")]
        public void MissingAndNonTestWitnessesAreRejected(string witness)
        {
            Assert.Throws<AssertionException>(() => AssertWitnessIsTest(witness));
        }

        [TestCase("[NUnit.Framework.SetUp] public void Prepare() {}")]
        [TestCase("[NUnit.Framework.TearDown] public void Cleanup() {}")]
        [TestCase("[NUnit.Framework.OneTimeSetUp] public void Prepare() {}")]
        [TestCase("[NUnit.Framework.OneTimeTearDown] public void Cleanup() {}")]
        [TestCase(
            "[NUnit.Framework.Test, NUnit.Framework.Ignore(\"reason\")] public void Witness() {}"
        )]
        [TestCase("[NUnit.Framework.Test, NUnit.Framework.Explicit] public void Witness() {}")]
        public void DirectWitnessesRejectNUnitLifecycleAndDisabledCases(string member)
        {
            CSharpCompilation compilation = CompatibilityInventory.Compile(
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        "public class EvidenceBase { "
                            + member
                            + " } public class EvidenceChild : EvidenceBase {}"
                    ),
                }
            );
            INamedTypeSymbol type = compilation.GetTypeByMetadataName("EvidenceChild");
            Assert.IsTrue(type != null);
            Assert.Throws<AssertionException>(() => AssertNoExecutionConstraints(type, "Witness"));
        }
    }
}
