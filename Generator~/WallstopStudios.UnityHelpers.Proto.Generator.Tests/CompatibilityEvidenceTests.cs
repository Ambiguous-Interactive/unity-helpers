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
