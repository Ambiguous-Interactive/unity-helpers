// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes;

    /// <summary>
    /// Holds the required-slot gate to two committed assets, one filled and one empty.
    /// </summary>
    /// <remarks>
    /// The assets are text a hand wrote and nothing re-saves, which is the state the gate exists for:
    /// a designer forgot a reference, nobody has the asset open again, and the only reader of the
    /// annotation is a drawer that needs somebody looking at the inspector.
    /// </remarks>
    [TestFixture]
    public sealed class AuthoredRequirementValidatorTests
    {
        private const string TestTypesFolder = "/TestTypes/";

        private string _filled;
        private string _empty;
        private string _inherited;

        private static string Describe(AuthoredRequirementFinding finding)
        {
            return $"{finding.FieldName}@{finding.LineNumber}";
        }

        private static List<AuthoredRequirementFinding> Scan(
            string assetPath,
            out int documentsInspected
        )
        {
            List<AuthoredRequirementFinding> findings = new();
            List<AuthoredRequirementExemption> exemptions = new();
            Assert.IsTrue(
                AuthoredRequirementValidator.TryScan(
                    new[] { assetPath },
                    findings,
                    exemptions,
                    out documentsInspected
                )
            );

            return findings;
        }

        [SetUp]
        public void ResolveFixturePaths()
        {
            MonoScriptIndex.ClearCaches();
            Assert.IsTrue(
                MonoScriptIndex.TryGetScriptPath(
                    typeof(AuthoredRequirementTestAsset),
                    out string scriptPath
                ),
                "The fixture type has no MonoScript, so nothing can locate it in a document."
            );

            int marker = scriptPath.IndexOf(TestTypesFolder, StringComparison.Ordinal);
            Assert.IsTrue(0 <= marker, scriptPath);
            string root = scriptPath.Substring(0, marker);
            _filled = $"{root}/TestAssets/FilledRequirements.asset";
            _empty = $"{root}/TestAssets/EmptyRequirements.asset";
            _inherited = $"{root}/TestAssets/InheritedRequirements.asset";
        }

        [TearDown]
        public void ForgetResolvedPaths()
        {
            MonoScriptIndex.ClearCaches();
            _filled = null;
            _empty = null;
            _inherited = null;
        }

        [TestCase(AuthoredRequirementKind.NotEmpty, "  numbers: []\n  names: []\n  text: ", 3)]
        [TestCase(
            AuthoredRequirementKind.NotEmpty,
            "  numbers:\n  - 0\n  names:\n  - \n  text: filled",
            0
        )]
        [TestCase(AuthoredRequirementKind.NotEmpty, "  numbers: []\n  text: \"\"", 2)]
        [TestCase(
            AuthoredRequirementKind.NotDefault,
            "  count: 0\n  enabledFlag: 0\n  amount: -0.0\n  material: {fileID: 0}",
            4
        )]
        [TestCase(
            AuthoredRequirementKind.NotDefault,
            "  oldCount: 0\n  enabledFlag: false\n  amount: 0e0",
            3
        )]
        [TestCase(
            AuthoredRequirementKind.NotDefault,
            "  count: -1\n  enabledFlag: 1\n  amount: 0.125\n  material: {fileID: 12}",
            0
        )]
        [TestCase(
            AuthoredRequirementKind.NotEmpty,
            "  numbers: 0000000000000000\n  names:\n  - ",
            0
        )]
        [TestCase(
            AuthoredRequirementKind.NotDefault,
            "  direction: {x: 0, y: -0.0, z: 0e-3}\n  tint: {r: 0, g: 0, b: 0, a: 0}",
            2
        )]
        [TestCase(
            AuthoredRequirementKind.NotDefault,
            "  direction: {x: 0, y: 0.00001, z: 0}\n  tint: {r: 0, g: 0, b: 0, a: 1}",
            0
        )]
        [TestCase(
            AuthoredRequirementKind.NotDefault,
            "  bounds:\n    m_Center: {x: 0, y: 0, z: 0}\n    m_Extent: {x: 0, y: 0, z: 0}",
            1
        )]
        [TestCase(
            AuthoredRequirementKind.NotDefault,
            "  bounds:\n    m_Center: {x: 0, y: 1, z: 0}\n    m_Extent: {x: 0, y: 0, z: 0}",
            0
        )]
        [TestCase(
            AuthoredRequirementKind.NotDefault,
            "  composite:\n    values:\n    - filled\n    count: 0",
            0
        )]
        [TestCase(
            AuthoredRequirementKind.NotDefault,
            "  composite:\n    values:\n    - \n    count: 0",
            0
        )]
        [TestCase(
            AuthoredRequirementKind.NotDefault,
            "  composite:\n    values: []\n    count: 0",
            1
        )]
        [TestCase(
            AuthoredRequirementKind.NotDefault,
            "  composite:\n    vectors:\n    - x: 0\n      y: 0\n      z: 0\n    count: 0",
            0
        )]
        [TestCase(
            AuthoredRequirementKind.NotDefault,
            "  composite:\n    vectors: []\n    count: 0",
            1
        )]
        [TestCase(
            AuthoredRequirementKind.NotEmpty,
            "  managedArray: []\n  " + RequirementKindTestAsset.ManagedListFieldName + ": []",
            2
        )]
        [TestCase(
            AuthoredRequirementKind.NotEmpty,
            "  managedArray:\n  - {rid: -2}\n  "
                + RequirementKindTestAsset.ManagedListFieldName
                + ":\n  - {rid: -2}",
            0
        )]
        [TestCase(AuthoredRequirementKind.NotDefault, "  flags: 0", 1)]
        [TestCase(AuthoredRequirementKind.NotDefault, "  flags: 1", 0)]
        public void ExplicitRequirementKindsJudgeTheirOwnConditions(
            AuthoredRequirementKind kind,
            string body,
            int expected
        )
        {
            Assert.IsTrue(
                MonoScriptIndex.TryGetScriptGuid(typeof(RequirementKindTestAsset), out string guid)
            );
            string path = Path.Combine(
                Path.GetTempPath(),
                $"requirement-kind-{Guid.NewGuid():N}.asset"
            );
            try
            {
                File.WriteAllText(
                    path,
                    $"%YAML 1.1\n--- !u!114 &11400000\nMonoBehaviour:\n  m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}\n{body}\n"
                );
                List<AuthoredRequirementFinding> findings = new();
                List<AuthoredRequirementExemption> exemptions = new();
                Type attribute =
                    kind == AuthoredRequirementKind.NotEmpty
                        ? typeof(NonEmptyRequirementTestAttribute)
                        : typeof(NonDefaultRequirementTestAttribute);
                Assert.IsTrue(
                    AuthoredRequirementValidator.TryScan(
                        attribute,
                        kind,
                        new[] { path },
                        findings,
                        exemptions,
                        out int inspected
                    )
                );
                Assert.AreEqual(1, inspected);
                Assert.AreEqual(
                    expected,
                    findings.Count,
                    string.Join(Environment.NewLine, findings)
                );
                foreach (AuthoredRequirementFinding finding in findings)
                {
                    Assert.AreEqual(kind, finding.Kind);
                }
                Assert.IsFalse(
                    exemptions.Any(item => item.DeclaringType == typeof(RequirementKindTestAsset))
                );
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void FindingsRetainRequirementKindAndTheOriginalConstructor()
        {
            AuthoredRequirementFinding original = new(
                "Assets/Value.asset",
                3,
                typeof(RequirementKindTestAsset),
                nameof(RequirementKindTestAsset.count)
            );
            AuthoredRequirementFinding assigned = new(
                "Assets/Value.asset",
                3,
                typeof(RequirementKindTestAsset),
                nameof(RequirementKindTestAsset.count),
                AuthoredRequirementKind.Assigned
            );
            AuthoredRequirementFinding nondefault = new(
                "Assets/Value.asset",
                3,
                typeof(RequirementKindTestAsset),
                nameof(RequirementKindTestAsset.count),
                AuthoredRequirementKind.NotDefault
            );
            Assert.IsTrue(original.Equals(assigned));
            Assert.AreEqual(original.GetHashCode(), assigned.GetHashCode());
            Assert.IsFalse(original.Equals(nondefault));
            StringAssert.Contains("default", nondefault.ToString());
        }

        [TestCase(nameof(RequirementKindTestAsset.nonSerialized))]
        [TestCase(nameof(RequirementKindTestAsset.shared))]
        [TestCase(nameof(RequirementKindTestAsset.fixedValue))]
        public void NonserializedFieldsCannotBeReportedAsSatisfiedRequirements(string name)
        {
            Assert.IsFalse(
                AuthoredRequirementValidator.TryClassify(
                    typeof(RequirementKindTestAsset).GetField(name),
                    out AuthoredRequirementField _,
                    AuthoredRequirementKind.NotDefault
                )
            );
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(4)]
        public void AnUnknownRequirementKindCannotReportACleanScan(int kind)
        {
            Assert.IsFalse(
                AuthoredRequirementValidator.TryScan(
                    typeof(NonDefaultRequirementTestAttribute),
                    (AuthoredRequirementKind)kind,
                    Array.Empty<string>(),
                    new List<AuthoredRequirementFinding>(),
                    new List<AuthoredRequirementExemption>(),
                    out int inspected
                )
            );
            Assert.AreEqual(0, inspected);
        }

        [Test]
        public void EveryEmptySlotIsReportedWithTheLineItIsWrittenOn()
        {
            List<AuthoredRequirementFinding> findings = Scan(_empty, out int documentsInspected);

            Assert.AreEqual(1, documentsInspected);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "requiredMaterial@15",
                    "requiredName@16",
                    "requiredMaterials@18",
                    "icon@19",
                },
                findings.Select(Describe).ToArray(),
                string.Join(Environment.NewLine, findings.Select(finding => finding.ToString()))
            );
        }

        [Test]
        public void AFilledAssetIsNotReported()
        {
            List<AuthoredRequirementFinding> findings = Scan(_filled, out int documentsInspected);

            Assert.AreEqual(1, documentsInspected);
            CollectionAssert.IsEmpty(
                findings.Select(finding => finding.ToString()).ToArray(),
                "A gate that fires on a correct asset is one developers turn off."
            );
        }

        [Test]
        public void ARequirementDeclaredOnABaseIsJudgedOnTheTypeThatWasAuthored()
        {
            List<AuthoredRequirementFinding> findings = Scan(
                _inherited,
                out int documentsInspected
            );

            Assert.AreEqual(
                1,
                documentsInspected,
                "The asset names the derived script, so registering the annotation under its "
                    + "declaring type alone looks at no document at all."
            );
            CollectionAssert.AreEquivalent(
                new[] { "inheritedMaterial@15" },
                findings.Select(Describe).ToArray(),
                string.Join(Environment.NewLine, findings.Select(finding => finding.ToString()))
            );
            Assert.AreEqual(typeof(InheritedRequirementTestAssetBase), findings[0].DeclaringType);
        }

        [Test]
        public void AnUnrequiredSlotIsNeverReportedHoweverEmptyItIs()
        {
            List<AuthoredRequirementFinding> findings = Scan(_empty, out int _);

            CollectionAssert.DoesNotContain(
                findings.Select(finding => finding.FieldName).ToArray(),
                nameof(AuthoredRequirementTestAsset.optionalMaterial)
            );
        }

        [Test]
        public void TheFixtureTypeIsReadableRatherThanExempt()
        {
            List<AuthoredRequirementFinding> findings = new();
            List<AuthoredRequirementExemption> exemptions = new();
            AuthoredRequirementValidator.TryScan(new[] { _empty }, findings, exemptions, out int _);

            CollectionAssert.IsEmpty(
                exemptions
                    .Where(exemption =>
                        exemption.DeclaringType == typeof(AuthoredRequirementTestAsset)
                    )
                    .Select(exemption => exemption.ToString())
                    .ToArray(),
                "A gate that quietly cannot see part of its subject is the failure this guards against."
            );
        }

        [TestCase(nameof(AuthoredRequirementTestAsset.nested), "    material: {fileID: 0}", 1)]
        [TestCase(nameof(AuthoredRequirementTestAsset.nested), "    material: {fileID: 12}", 0)]
        [TestCase(nameof(AuthoredRequirementTestAsset.nested), "    oldMaterial: {fileID: 0}", 1)]
        [TestCase(
            nameof(AuthoredRequirementTestAsset.nested),
            "    material: {fileID: 12}\n    oldMaterial: {fileID: 0}",
            0
        )]
        [TestCase(
            nameof(AuthoredRequirementTestAsset.nested),
            "    child:\n      material: {fileID: 0}",
            1
        )]
        [TestCase(
            nameof(AuthoredRequirementTestAsset.nestedList),
            "  - material: {fileID: 0}\n  - material: {fileID: 12}\n  - material: {fileID: 0}",
            2
        )]
        [TestCase(
            nameof(AuthoredRequirementTestAsset.nestedArray),
            "  - child:\n      material: {fileID: 0}\n    material: {fileID: 12}",
            1
        )]
        [TestCase(
            nameof(AuthoredRequirementTestAsset.nestedDictionary),
            "    _keys:\n    - first\n    _values:\n    - material: {fileID: 0}\n    _boxedValues: []",
            1
        )]
        [TestCase(
            nameof(AuthoredRequirementTestAsset.boxedDictionary),
            "    _keys:\n    - first\n    _boxedValues:\n    - Data:\n      - material: {fileID: 0}\n      - material: {fileID: 12}",
            1
        )]
        [TestCase(
            nameof(AuthoredRequirementTestAsset.nestedList),
            "  - other:\n      material: {fileID: 0}\n    material: {fileID: 12}",
            0
        )]
        [TestCase(
            nameof(AuthoredRequirementTestAsset.ignoredNested),
            "    material: {fileID: 0}",
            0
        )]
        [TestCase(
            nameof(AuthoredRequirementTestAsset.managedNested),
            "    material: {fileID: 0}",
            0
        )]
        public void NestedRequirementsFollowTheirTypedCarrier(
            string field,
            string value,
            int expected
        )
        {
            Assert.IsTrue(
                MonoScriptIndex.TryGetScriptGuid(
                    typeof(AuthoredRequirementTestAsset),
                    out string guid
                )
            );
            string path = Path.Combine(
                Path.GetTempPath(),
                $"nested-requirement-{Guid.NewGuid():N}.asset"
            );
            try
            {
                File.WriteAllText(
                    path,
                    $"%YAML 1.1\n--- !u!114 &11400000\nMonoBehaviour:\n  m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}\n  {field}:\n{value}\n"
                );
                List<AuthoredRequirementFinding> findings = new();
                List<AuthoredRequirementExemption> exemptions = new();
                Assert.IsTrue(
                    AuthoredRequirementValidator.TryScan(
                        new[] { path },
                        findings,
                        exemptions,
                        out int inspected
                    )
                );
                Assert.AreEqual(1, inspected);
                Assert.AreEqual(expected, findings.Count, string.Join("\n", findings));
                Assert.IsFalse(
                    exemptions.Any(exemption =>
                        exemption.DeclaringType == typeof(NestedRequirementTestValue)
                    )
                );
                string[] authored = File.ReadAllLines(path);
                foreach (AuthoredRequirementFinding finding in findings)
                {
                    StringAssert.EndsWith("{fileID: 0}", authored[finding.LineNumber - 1]);
                }
                Assert.IsTrue(
                    findings.All(finding =>
                        finding.DeclaringType == typeof(NestedRequirementTestValue)
                        && string.Equals(
                            finding.FieldName,
                            nameof(NestedRequirementTestValue.material),
                            StringComparison.Ordinal
                        )
                    )
                );
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void DiscoveryResultsSurviveLaterScans()
        {
            List<AuthoredRequirementExemption> exemptions = new();
            Dictionary<string, List<AuthoredRequirementField>> first =
                AuthoredRequirementValidator.FieldsByScriptGuid(
                    typeof(WallstopStudios.UnityHelpers.Core.Attributes.WNotNullAttribute),
                    exemptions
                );
            Dictionary<string, AuthoredRequirementField[]> snapshots = new();
            foreach (KeyValuePair<string, List<AuthoredRequirementField>> pair in first)
            {
                snapshots.Add(pair.Key, pair.Value.ToArray());
            }
            Assert.IsNotEmpty(snapshots);
            for (int iteration = 0; iteration < 3; ++iteration)
            {
                Dictionary<string, List<AuthoredRequirementField>> next =
                    AuthoredRequirementValidator.FieldsByScriptGuid(
                        typeof(WallstopStudios.UnityHelpers.Core.Attributes.WNotNullAttribute),
                        exemptions
                    );
                foreach (KeyValuePair<string, AuthoredRequirementField[]> pair in snapshots)
                {
                    Assert.IsTrue(
                        first.TryGetValue(pair.Key, out List<AuthoredRequirementField> retained)
                    );
                    Assert.IsTrue(
                        next.TryGetValue(pair.Key, out List<AuthoredRequirementField> current)
                    );
                    Assert.AreNotSame(retained, current);
                    CollectionAssert.AreEqual(pair.Value, retained);
                    Assert.AreEqual(pair.Value.Length, current.Count);
                }
            }
        }

        [TestCase(nameof(AuthoredRequirementTestAsset.genericMaterial), false, false)]
        [TestCase(nameof(AuthoredRequirementTestAsset.genericMaterial), true, false)]
        [TestCase(nameof(AuthoredRequirementTestAsset.genericMaterial), false, true)]
        [TestCase(nameof(AuthoredRequirementTestAsset.genericText), false, false)]
        [TestCase(nameof(AuthoredRequirementTestAsset.genericText), true, false)]
        [TestCase(nameof(AuthoredRequirementTestAsset.inheritedGeneric), false, false)]
        [TestCase(nameof(AuthoredRequirementTestAsset.inheritedGeneric), true, false)]
        [TestCase(nameof(AuthoredRequirementTestAsset.inheritedGeneric), false, true)]
        public void ClosedGenericValuesUseTheirConstructedFieldTypes(
            string field,
            bool emptyValue,
            bool emptyMaterial
        )
        {
            Assert.IsTrue(
                MonoScriptIndex.TryGetScriptGuid(
                    typeof(AuthoredRequirementTestAsset),
                    out string guid
                )
            );
            bool text = string.Equals(
                field,
                nameof(AuthoredRequirementTestAsset.genericText),
                StringComparison.Ordinal
            );
            string value = text
                ? (emptyValue ? "''" : "assigned")
                : (emptyValue ? "{fileID: 0}" : "{fileID: 12}");
            string material = emptyMaterial ? "{fileID: 0}" : "{fileID: 12}";
            string path = Path.Combine(
                Path.GetTempPath(),
                $"generic-requirement-{Guid.NewGuid():N}.asset"
            );
            try
            {
                File.WriteAllText(
                    path,
                    $"%YAML 1.1\n--- !u!114 &11400000\nMonoBehaviour:\n  m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}\n  {field}:\n    material: {material}\n    value: {value}\n"
                );
                List<AuthoredRequirementFinding> findings = new();
                List<AuthoredRequirementExemption> exemptions = new();
                Assert.IsTrue(
                    AuthoredRequirementValidator.TryScan(
                        new[] { path },
                        findings,
                        exemptions,
                        out int inspected
                    )
                );
                Assert.AreEqual(1, inspected);
                Assert.AreEqual(emptyValue || emptyMaterial ? 1 : 0, findings.Count);
                Assert.IsFalse(
                    exemptions.Any(exemption =>
                        exemption.DeclaringType == typeof(GenericRequirementTestValue<>)
                    )
                );
                if (findings.Count != 0)
                {
                    Assert.AreEqual(
                        text
                            ? typeof(GenericRequirementTestValue<string>)
                            : typeof(GenericRequirementTestValue<Material>),
                        findings[0].DeclaringType
                    );
                    Assert.AreEqual(
                        emptyMaterial
                            ? nameof(GenericRequirementTestValue<Material>.material)
                            : nameof(GenericRequirementTestValue<Material>.value),
                        findings[0].FieldName
                    );
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void AScanWithNothingToReadIsRefusedRatherThanReportedClean()
        {
            List<AuthoredRequirementFinding> findings = new();
            List<AuthoredRequirementExemption> exemptions = new();

            Assert.IsFalse(
                AuthoredRequirementValidator.TryScan(null, findings, exemptions, out int _)
            );
            Assert.IsFalse(
                AuthoredRequirementValidator.TryScan(
                    null,
                    Array.Empty<string>(),
                    findings,
                    exemptions,
                    out int _
                )
            );
        }
    }
}
