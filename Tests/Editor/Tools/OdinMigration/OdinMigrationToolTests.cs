// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;

    [TestFixture]
    [Category("Fast")]
    public sealed class OdinMigrationToolTests
    {
        private static byte[] Combine(byte[] prefix, byte[] content)
        {
            byte[] combined = new byte[prefix.Length + content.Length];
            Buffer.BlockCopy(prefix, 0, combined, 0, prefix.Length);
            Buffer.BlockCopy(content, 0, combined, prefix.Length, content.Length);
            return combined;
        }

        private static OdinMigrationFilePlan CreatePlan(
            string path,
            byte[] original,
            byte[] upgraded
        )
        {
            return new OdinMigrationFilePlan(
                path + ".cs",
                path,
                original,
                upgraded,
                OdinMigrationSourceAnalyzer.Analyze(string.Empty)
            );
        }

        [Test]
        public void AnalyzerRewritesOnlyProvenInspectorEquivalents()
        {
            const string Source =
                "class Target\r\n"
                + "{\r\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly, global::Sirenix.OdinInspector.EnumToggleButtons]\r\n"
                + "    public int value;\r\n"
                + "    [Sirenix.OdinInspector.ShowIf(nameof(enabled))] int shown;\r\n"
                + "    [Sirenix.OdinInspector.HideIf(nameof(enabled))] int hidden;\r\n"
                + "}\r\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(2, analysis.ReplacementCount);
            StringAssert.Contains("Core.Attributes.WReadOnly", analysis.UpgradedSource);
            StringAssert.Contains("Core.Attributes.WEnumToggleButtons", analysis.UpgradedSource);
            StringAssert.Contains(
                "[Sirenix.OdinInspector.ShowIf(nameof(enabled))]",
                analysis.UpgradedSource
            );
            StringAssert.Contains(
                "[Sirenix.OdinInspector.HideIf(nameof(enabled))]",
                analysis.UpgradedSource
            );
            StringAssert.Contains("\r\n", analysis.UpgradedSource);
            Assert.AreEqual(2, analysis.Changes.Count);
            OdinMigrationChange lockedChange = default;
            foreach (OdinMigrationChange change in analysis.Changes)
            {
                if (change.From == "global::Sirenix.OdinInspector.ReadOnly")
                {
                    lockedChange = change;
                    break;
                }
            }
            Assert.AreEqual(3, lockedChange.Line);
            Assert.AreEqual("global::Sirenix.OdinInspector.ReadOnly", lockedChange.From);
            Assert.AreEqual(
                "WallstopStudios.UnityHelpers.Core.Attributes.WReadOnly",
                lockedChange.To
            );
        }

        [Test]
        public void GlobalQualifierAtAttributeListStartIsNotATargetSpecifier()
        {
            const string Source =
                "class Target\n"
                + "{\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] public int value;\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(1, analysis.ReplacementCount);
            StringAssert.Contains(
                "Core.Attributes.WReadOnly] public int value",
                analysis.UpgradedSource
            );
            Assert.AreEqual(0, analysis.ManualReviews.Count);
        }

        [Test]
        public void AnalyzerRewritesAttributesAfterPreprocessorDirectives()
        {
            const string Source =
                "class Target\n"
                + "{\n"
                + "#region Fields\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] public int first;\n"
                + "#endregion\n"
                + "#if UNITY_EDITOR\n"
                + "    [global::Sirenix.OdinInspector.EnumToggleButtons] public int second;\n"
                + "#endif\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(2, analysis.ReplacementCount);
            StringAssert.Contains(
                "Core.Attributes.WReadOnly] public int first",
                analysis.UpgradedSource
            );
            StringAssert.Contains(
                "Core.Attributes.WEnumToggleButtons] public int second",
                analysis.UpgradedSource
            );
        }

        [Test]
        public void AnalyzerClassifiesStateAndUnsupportedShapesWithoutRewritingThem()
        {
            const string Source =
                "using Sirenix.OdinInspector;\n"
                + "class Target : Serialized"
                + "MonoBehaviour\n"
                + "{\n"
                + "    [OdinSerialize] Dictionary<string, int> values;\n"
                + "    [ShowIf(\"left\", Value = 3)] int conditional;\n"
                + "    [ValueDropdown(\"Choices\")] int choice;\n"
                + "    [Button(ButtonSizes.Large)] void Run() {}\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(0, analysis.ReplacementCount);
            Assert.GreaterOrEqual(analysis.Blockers.Count, 3);
            Assert.GreaterOrEqual(analysis.ManualReviews.Count, 4);
            Assert.AreSame(Source, analysis.UpgradedSource);
        }

        [Test]
        public void AnalyzerLeavesUnqualifiedAttributesAndResolverStringsForReview()
        {
            const string Source =
                "using Sirenix.OdinInspector;\n"
                + "class Target { [ReadOnly] int first; "
                + "[Sirenix.OdinInspector.ShowIf(\"enabled\")] int second; "
                + "[Sirenix.OdinInspector.Button] void Run() {} "
                + "[Sirenix.OdinInspector.Required] object value; }\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(0, analysis.ReplacementCount);
            Assert.GreaterOrEqual(analysis.ManualReviews.Count, 5);
            Assert.AreSame(Source, analysis.UpgradedSource);
        }

        [Test]
        public void AnalyzerRewritesOnlyProvenSerializedFieldsAcrossAttributeLists()
        {
            const string Source =
                "[global::Sirenix.OdinInspector.ReadOnly] class DecoratedType {}\n"
                + "class Target\n"
                + "{\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] void Run() {}\n"
                + "    [System.NonSerialized]\n"
                + "    [field: global::Sirenix.OdinInspector.ReadOnly] int field;\n"
                + "    [System.NonSerializedAttribute]\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] public int attributedField;\n"
                + "    [property: global::Sirenix.OdinInspector.EnumToggleButtons] int Property { get; set; }\n"
                + "    [global::UnityEngine.SerializeFieldAttribute]\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] int serialized;\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(1, analysis.ReplacementCount);
            StringAssert.Contains(
                "[global::Sirenix.OdinInspector.ReadOnly] class DecoratedType",
                analysis.UpgradedSource
            );
            StringAssert.Contains(
                "[global::Sirenix.OdinInspector.ReadOnly] void Run()",
                analysis.UpgradedSource
            );
            StringAssert.Contains("[System.NonSerialized]", analysis.UpgradedSource);
            StringAssert.Contains(
                "[field: global::Sirenix.OdinInspector.ReadOnly] int field",
                analysis.UpgradedSource
            );
            StringAssert.Contains(
                "[global::Sirenix.OdinInspector.ReadOnly] public int attributedField",
                analysis.UpgradedSource
            );
            StringAssert.Contains(
                "[property: global::Sirenix.OdinInspector.EnumToggleButtons] int Property",
                analysis.UpgradedSource
            );
            StringAssert.Contains(
                "Core.Attributes.WReadOnly] int serialized",
                analysis.UpgradedSource
            );
            Assert.AreEqual(5, analysis.ManualReviews.Count);
        }

        [Test]
        public void AnalyzerDisablesAllRewritesWhenFileContainsOdinSerializedState()
        {
            const string Source =
                "class Target : Sirenix.OdinInspector.Serialized"
                + "MonoBehaviour\n"
                + "{\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] public int value;\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(0, analysis.ReplacementCount);
            Assert.AreSame(Source, analysis.UpgradedSource);
            Assert.AreEqual(1, analysis.Blockers.Count);
            Assert.AreEqual(1, analysis.ManualReviews.Count);
            StringAssert.Contains("Odin-owned serialized state", analysis.ManualReviews[0].Message);
        }

        [Test]
        public void AnalyzerLeavesFieldsThatUnityWillNotSerializeForReview()
        {
            const string Source =
                "class Target\n"
                + "{\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] static int shared;\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] readonly int immutable;\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] private int hidden;\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] int @public;\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] public int Expression => 1;\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] public int visible;\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(1, analysis.ReplacementCount);
            StringAssert.Contains(
                "Core.Attributes.WReadOnly] public int visible",
                analysis.UpgradedSource
            );
            Assert.AreEqual(5, analysis.ManualReviews.Count);
            foreach (OdinMigrationFinding finding in analysis.ManualReviews)
            {
                StringAssert.Contains("public field", finding.Message);
            }
        }

        [Test]
        public void AnalyzerRequiresAnExactUnconditionalUnitySerializationAttribute()
        {
            const string Source =
                "class Target\n"
                + "{\n"
                + "    [Marker(nameof(SerializeField))]\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] int argumentOnly;\n"
                + "    [SerializeField]\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] int ambiguous;\n"
                + "#if ENABLE_FIELD\n"
                + "    [global::UnityEngine.SerializeField]\n"
                + "#endif\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] int conditional;\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(0, analysis.ReplacementCount);
            Assert.AreSame(Source, analysis.UpgradedSource);
            Assert.AreEqual(3, analysis.ManualReviews.Count);
        }

        [Test]
        public void AnalyzerMasksRawAndInterpolatedRawStringContents()
        {
            const string Source =
                "class Target\n"
                + "{\n"
                + "    string raw = \"\"\"\n"
                + "[global::Sirenix.OdinInspector.ReadOnly] public int falseField;\n"
                + "\"\"\";\n"
                + "    string interpolated = $\"\"\"\n"
                + "[global::Sirenix.OdinInspector.EnumToggleButtons] public int otherFalseField;\n"
                + "\"\"\";\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] public int actual;\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(1, analysis.ReplacementCount);
            StringAssert.Contains("ReadOnly] public int falseField", analysis.UpgradedSource);
            StringAssert.Contains(
                "EnumToggleButtons] public int otherFalseField",
                analysis.UpgradedSource
            );
            StringAssert.Contains(
                "Core.Attributes.WReadOnly] public int actual",
                analysis.UpgradedSource
            );
        }

        [Test]
        public void AnalyzerMasksWiderRawStringDelimiters()
        {
            const string Source =
                "class Target\n"
                + "{\n"
                + "    string singleLine = \"\"\"\"\"\"; "
                + "[global::Sirenix.OdinInspector.ReadOnly] public int falseSingleLine;\"\"\"\"\"\";\n"
                + "    string widerDelimiter = \"\"\"\"\"\"\n"
                + "[global::Sirenix.OdinInspector.ReadOnly] public int falseMultiLine;\n"
                + "\"\"\"\"\"\";\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] public int actual;\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(1, analysis.ReplacementCount);
            StringAssert.Contains("ReadOnly] public int falseSingleLine", analysis.UpgradedSource);
            StringAssert.Contains("ReadOnly] public int falseMultiLine", analysis.UpgradedSource);
            StringAssert.Contains(
                "Core.Attributes.WReadOnly] public int actual",
                analysis.UpgradedSource
            );
        }

        [Test]
        public void AnalyzerBlocksExplicitOdinSerializeAttributeSuffix()
        {
            const string Source =
                "class Target\n"
                + "{\n"
                + "    [global::Sirenix.OdinInspector.OdinSerializeAttribute] private int value;\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(0, analysis.ReplacementCount);
            Assert.AreEqual(1, analysis.Blockers.Count);
            Assert.AreSame(Source, analysis.UpgradedSource);
        }

        [Test]
        public void AnalyzerReportsEveryGloballyQualifiedOdinInspectorAttribute()
        {
            const string Source =
                "class Target\n"
                + "{\n"
                + "    [global::Sirenix.OdinInspector.Title(\"Stats\")] public int value;\n"
                + "    [global::Sirenix.OdinInspector.InfoBox(\"Check this\")] public int other;\n"
                + "    [global::Sirenix.OdinInspector.TableList] public object[] rows;\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(0, analysis.ReplacementCount);
            Assert.AreEqual(3, analysis.ManualReviews.Count);
            StringAssert.Contains("Title has no proven", analysis.ManualReviews[0].Message);
            StringAssert.Contains("InfoBox has no proven", analysis.ManualReviews[1].Message);
            StringAssert.Contains("TableList has no proven", analysis.ManualReviews[2].Message);
        }

        [Test]
        public void AnalyzerLeavesParameterAndLambdaParameterAttributesForReview()
        {
            const string Source =
                "class Target\n"
                + "{\n"
                + "    void Run([global::Sirenix.OdinInspector.ReadOnly] int value) {}\n"
                + "    System.Func<int, int> map = ([global::Sirenix.OdinInspector.ReadOnly] int value) => value;\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(0, analysis.ReplacementCount);
            Assert.AreSame(Source, analysis.UpgradedSource);
            Assert.AreEqual(2, analysis.ManualReviews.Count);
        }

        [Test]
        public void NamespaceBlockAliasesDoNotLeakAcrossNamespaceScopes()
        {
            const string Source =
                "namespace First\n"
                + "{\n"
                + "    using O = Sirenix.OdinInspector;\n"
                + "    using Sirenix.OdinInspector;\n"
                + "    class FirstTarget { [O.ReadOnly] int value; }\n"
                + "}\n"
                + "namespace Second\n"
                + "{\n"
                + "    class SecondTarget { [O.ReadOnly] int value; [ReadOnly] int other; }\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(0, analysis.ReplacementCount);
            Assert.AreSame(Source, analysis.UpgradedSource);
            Assert.AreEqual(2, analysis.ManualReviews.Count);
            foreach (OdinMigrationFinding finding in analysis.ManualReviews)
            {
                StringAssert.DoesNotContain("could not be proven", finding.Message);
            }
        }

        [Test]
        public void CompilationUnitAliasesAreNeverAutomaticallyRewritten()
        {
            const string Source =
                "using O = Sirenix.OdinInspector;\n"
                + "using Locked = Sirenix.OdinInspector.ReadOnlyAttribute;\n"
                + "class Target { [O.ReadOnly] int first; [Locked] int second; }\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(0, analysis.ReplacementCount);
            Assert.AreSame(Source, analysis.UpgradedSource);
            Assert.AreEqual(2, analysis.ManualReviews.Count);
        }

        [Test]
        public void NonGlobalQualificationRemainsManualNearAShadowingNamespace()
        {
            const string Source =
                "namespace Consumer\n"
                + "{\n"
                + "    namespace Sirenix.OdinInspector { class ReadOnlyAttribute {} }\n"
                + "    class Target\n"
                + "    {\n"
                + "        [Sirenix.OdinInspector.ReadOnly] int shadowed;\n"
                + "        [global::Sirenix.OdinInspector.EnumToggleButtons] public int safe;\n"
                + "    }\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(1, analysis.ReplacementCount);
            StringAssert.Contains(
                "[Sirenix.OdinInspector.ReadOnly] int shadowed",
                analysis.UpgradedSource
            );
            StringAssert.Contains(
                "Core.Attributes.WEnumToggleButtons] public int safe",
                analysis.UpgradedSource
            );
            Assert.AreEqual(1, analysis.ManualReviews.Count);
        }

        [Test]
        public void ExplicitReturnTargetBeforePropertyShapeRemainsManual()
        {
            const string Source =
                "class Target\n"
                + "{\n"
                + "    [return: global::Sirenix.OdinInspector.ReadOnly]\n"
                + "    int Property { get; set; }\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(0, analysis.ReplacementCount);
            Assert.AreSame(Source, analysis.UpgradedSource);
            Assert.AreEqual(1, analysis.ManualReviews.Count);
            StringAssert.Contains("return:", analysis.ManualReviews[0].Message);
        }

        [Test]
        public void TypeKeywordsInsideAttributesDoNotMakeAccessorTargetsLookLikeTypeMembers()
        {
            const string Source =
                "class Target\n"
                + "{\n"
                + "    [Marker(nameof(@class))] public int Property\n"
                + "    {\n"
                + "        [global::Sirenix.OdinInspector.ReadOnly] private get;\n"
                + "        set;\n"
                + "    }\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(0, analysis.ReplacementCount);
            Assert.AreSame(Source, analysis.UpgradedSource);
            Assert.AreEqual(1, analysis.ManualReviews.Count);
        }

        [Test]
        public void AnalyzerMasksBothInterpolatedVerbatimStringPrefixOrdersAcrossLines()
        {
            const string Source =
                "class Target\n"
                + "{\n"
                + "    string first = $@\"header\n[Sirenix.OdinInspector.ReadOnly]\ntail\";\n"
                + "    string second = @$\"header\n[Sirenix.OdinInspector.EnumToggleButtons]\ntail\";\n"
                + "    [global::Sirenix.OdinInspector.ReadOnly] public int actual;\n"
                + "}\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(1, analysis.ReplacementCount);
            StringAssert.Contains(
                "\n[Sirenix.OdinInspector.ReadOnly]\ntail",
                analysis.UpgradedSource
            );
            StringAssert.Contains(
                "\n[Sirenix.OdinInspector.EnumToggleButtons]\ntail",
                analysis.UpgradedSource
            );
            StringAssert.Contains(
                "Core.Attributes.WReadOnly] public int actual",
                analysis.UpgradedSource
            );
        }

        [Test]
        public void ChangeLinesCountStandaloneCarriageReturns()
        {
            const string Source =
                "class Target\r"
                + "{\r"
                + "    [global::Sirenix.OdinInspector.ReadOnly] public int value;\r"
                + "}\r";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(1, analysis.ReplacementCount);
            foreach (OdinMigrationChange change in analysis.Changes)
            {
                Assert.AreEqual(3, change.Line);
            }
        }

        [Test]
        public void AnalyzerIgnoresCommentsStringsAndIndexerExpressions()
        {
            const string Source =
                "using Sirenix.OdinInspector;\n"
                + "class Target { string text = \"[ReadOnly]\"; int value = values[ReadOnly]; "
                + "/* [ReadOnly] */ }\n";

            OdinMigrationAnalysis analysis = OdinMigrationSourceAnalyzer.Analyze(Source);

            Assert.AreEqual(0, analysis.ReplacementCount);
            Assert.AreEqual(Source, analysis.UpgradedSource);
        }

        [Test]
        public void GeneratedDetectionCoversDesignerNamesAndMarkersAfterHeaders()
        {
            Assert.IsTrue(OdinMigrationSourceAnalyzer.LooksGenerated("Assets/Foo.designer.cs", ""));
            Assert.IsTrue(
                OdinMigrationSourceAnalyzer.LooksGenerated(
                    "Assets/Foo.cs",
                    "// Copyright\n\n/* tool header */\n// <auto-generated>\nclass Foo {}"
                )
            );
            Assert.IsFalse(
                OdinMigrationSourceAnalyzer.LooksGenerated("Assets/Foo.cs", "class Foo {}")
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EncodingRoundTripPreservesUtf8BomAndNewlines(bool withBom)
        {
            string source = "first\r\nsecond\r\n";
            UTF8Encoding encoding = new UTF8Encoding(withBom, true);
            byte[] content = encoding.GetBytes(source);
            byte[] original = withBom ? Combine(encoding.GetPreamble(), content) : content;

            Assert.IsTrue(
                OdinMigrationEncodedSource.TryDecode(
                    original,
                    out OdinMigrationDecodedSource decoded,
                    out string failure
                ),
                failure
            );
            byte[] roundTrip = OdinMigrationEncodedSource.Encode(decoded, decoded.Source);

            CollectionAssert.AreEqual(original, roundTrip);
        }

        [Test]
        public void EncodingRoundTripPreservesUtf16AndUtf32Variants()
        {
            Encoding[] encodings =
            {
                new UnicodeEncoding(false, true, true),
                new UnicodeEncoding(true, true, true),
                new UTF32Encoding(false, true, true),
                new UTF32Encoding(true, true, true),
            };
            foreach (Encoding encoding in encodings)
            {
                byte[] original = Combine(
                    encoding.GetPreamble(),
                    encoding.GetBytes("first\r\nsecond\r\n")
                );
                Assert.IsTrue(
                    OdinMigrationEncodedSource.TryDecode(
                        original,
                        out OdinMigrationDecodedSource decoded,
                        out string failure
                    ),
                    failure
                );
                CollectionAssert.AreEqual(
                    original,
                    OdinMigrationEncodedSource.Encode(decoded, decoded.Source)
                );
            }
        }

        [Test]
        public void EncodingRejectsInvalidUtf8()
        {
            Assert.IsFalse(
                OdinMigrationEncodedSource.TryDecode(
                    new byte[] { 0xC3, 0x28 },
                    out _,
                    out string failure
                )
            );
            StringAssert.Contains("invalid source encoding", failure);
        }

        [Test]
        public void ScanCancellationStopsBeforeReadingTheCancelledFile()
        {
            string firstPath = "Assets/First.cs";
            string secondPath = "Assets/Second.cs";
            FakeScanContext context = new FakeScanContext { CancelAtIndex = 1 };
            context.Files[firstPath] = Encoding.UTF8.GetBytes(
                "class First { [global::Sirenix.OdinInspector.ReadOnly] public int value; }"
            );
            context.Files[secondPath] = Encoding.UTF8.GetBytes(
                "class Second { [global::Sirenix.OdinInspector.ReadOnly] public int value; }"
            );

            OdinMigrationTool.ScanResult result = OdinMigrationTool.BuildPlans(
                new[] { firstPath, secondPath },
                context
            );

            Assert.IsTrue(result.Cancelled);
            Assert.AreEqual(1, result.AnalyzedFiles);
            Assert.AreEqual(1, result.Plans.Count);
            Assert.AreEqual(1, result.Replacements);
            CollectionAssert.AreEqual(new[] { firstPath }, context.Reads);
        }

        [Test]
        public void ScanClassifiesGeneratedInvalidUnreadableAndPlainFiles()
        {
            string generatedPath = "Assets/Generated/First.cs";
            string invalidPath = "Assets/Invalid.cs";
            string missingPath = "Assets/Missing.cs";
            string unresolvedPath = "Assets/Unresolved.cs";
            string plainPath = "Assets/Plain.cs";
            FakeScanContext context = new FakeScanContext();
            context.Files[generatedPath] = Encoding.UTF8.GetBytes("class Generated {}");
            context.Files[invalidPath] = new byte[] { 0xC3, 0x28 };
            context.Files[plainPath] = Encoding.UTF8.GetBytes("class Plain {}");
            context.FullPathFailures.Add(unresolvedPath);

            OdinMigrationTool.ScanResult result = OdinMigrationTool.BuildPlans(
                new[] { generatedPath, invalidPath, missingPath, unresolvedPath, plainPath },
                context
            );

            Assert.IsFalse(result.Cancelled);
            Assert.AreEqual(1, result.GeneratedFiles);
            Assert.AreEqual(1, result.AnalyzedFiles);
            Assert.AreEqual(3, result.Failures.Count);
            Assert.AreEqual(0, result.Plans.Count);
            CollectionAssert.AreEqual(
                new[] { generatedPath, invalidPath, missingPath, plainPath },
                context.Reads
            );
        }

        [Test]
        public void ScanRejectsMissingInputsWithoutThrowing()
        {
            OdinMigrationTool.ScanResult missingPaths = OdinMigrationTool.BuildPlans(
                null,
                new FakeScanContext()
            );
            OdinMigrationTool.ScanResult missingContext = OdinMigrationTool.BuildPlans(
                Array.Empty<string>(),
                null
            );

            Assert.AreEqual(1, missingPaths.Failures.Count);
            Assert.AreEqual(1, missingContext.Failures.Count);
        }

        [Test]
        public void ApplyEligibilityRequiresACompleteSuccessfulScanWithChanges()
        {
            FakeScanContext context = new FakeScanContext();
            context.Files["Assets/Safe.cs"] = Encoding.UTF8.GetBytes(
                "class Safe { [global::Sirenix.OdinInspector.ReadOnly] public int value; }"
            );

            OdinMigrationTool.ScanResult complete = OdinMigrationTool.BuildPlans(
                new[] { "Assets/Safe.cs" },
                context
            );
            Assert.IsTrue(OdinMigrationTool.CanApply(complete));

            complete.Cancelled = true;
            Assert.IsFalse(OdinMigrationTool.CanApply(complete));
            complete.Cancelled = false;
            complete.Failures.Add("unreadable source");
            Assert.IsFalse(OdinMigrationTool.CanApply(complete));
            Assert.IsFalse(OdinMigrationTool.CanApply(null));
            Assert.IsFalse(OdinMigrationTool.CanApply(new OdinMigrationTool.ScanResult()));
        }

        [Test]
        public void TransactionRejectsConcurrentEditBeforeWriting()
        {
            byte[] original = { 1 };
            byte[] changed = { 9 };
            FakeFileStore store = new FakeFileStore();
            store.Files["one"] = changed;
            OdinMigrationFilePlan plan = CreatePlan("one", original, new byte[] { 2 });

            bool applied = OdinMigrationFileTransaction.TryApply(
                new[] { plan },
                "backup",
                store,
                out string failure
            );

            Assert.IsFalse(applied);
            StringAssert.Contains("changed after preview", failure);
            CollectionAssert.AreEqual(changed, store.ReadAllBytes("one"));
            Assert.AreEqual(0, store.TargetWrites);
        }

        [Test]
        public void TransactionStopsWhenBackupWriteFails()
        {
            FakeFileStore store = new FakeFileStore
            {
                FailBackupPath = Path.Combine("backup", "one.cs"),
            };
            store.Files["one"] = new byte[] { 1 };
            OdinMigrationFilePlan plan = CreatePlan("one", new byte[] { 1 }, new byte[] { 2 });

            Assert.IsFalse(
                OdinMigrationFileTransaction.TryApply(
                    new[] { plan },
                    "backup",
                    store,
                    out string failure
                )
            );
            StringAssert.Contains("backup", failure);
            Assert.AreEqual(0, store.TargetWrites);
            CollectionAssert.AreEqual(new byte[] { 1 }, store.ReadAllBytes("one"));
        }

        [Test]
        public void TransactionRollsBackEarlierWritesWhenLaterWriteFails()
        {
            FakeFileStore store = new FakeFileStore { FailTargetPath = "two" };
            store.Files["one"] = new byte[] { 1 };
            store.Files["two"] = new byte[] { 3 };
            OdinMigrationFilePlan first = CreatePlan("one", new byte[] { 1 }, new byte[] { 2 });
            OdinMigrationFilePlan second = CreatePlan("two", new byte[] { 3 }, new byte[] { 4 });

            bool applied = OdinMigrationFileTransaction.TryApply(
                new[] { first, second },
                "backup",
                store,
                out _
            );

            Assert.IsFalse(applied);
            CollectionAssert.AreEqual(new byte[] { 1 }, store.ReadAllBytes("one"));
            CollectionAssert.AreEqual(new byte[] { 3 }, store.ReadAllBytes("two"));
            Assert.AreEqual(2, store.Backups.Count);
        }

        [Test]
        public void TransactionRefusesRollbackOverAConcurrentEdit()
        {
            FakeFileStore store = new FakeFileStore
            {
                FailTargetPath = "two",
                MutateAfterTargetPath = "one",
            };
            store.Files["one"] = new byte[] { 1 };
            store.Files["two"] = new byte[] { 3 };
            OdinMigrationFilePlan first = CreatePlan("one", new byte[] { 1 }, new byte[] { 2 });
            OdinMigrationFilePlan second = CreatePlan("two", new byte[] { 3 }, new byte[] { 4 });

            bool applied = OdinMigrationFileTransaction.TryApply(
                new[] { first, second },
                "backup",
                store,
                out string failure
            );

            Assert.IsFalse(applied);
            StringAssert.Contains("Rollback refused", failure);
            CollectionAssert.AreEqual(new byte[] { 99 }, store.ReadAllBytes("one"));
        }

        private sealed class FakeFileStore : IOdinMigrationFileStore
        {
            internal readonly Dictionary<string, byte[]> Backups = new Dictionary<string, byte[]>();
            internal readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
            internal string FailBackupPath;
            internal string FailTargetPath;
            internal string MutateAfterTargetPath;
            internal int TargetWrites;

            public byte[] ReadAllBytes(string path)
            {
                if (Files.TryGetValue(path, out byte[] bytes))
                {
                    return bytes;
                }
                throw new FileNotFoundException("Missing fake file.", path);
            }

            public void RestoreTarget(string path, byte[] bytes)
            {
                Files[path] = bytes;
            }

            public void WriteBackup(string path, byte[] bytes)
            {
                if (path == FailBackupPath)
                {
                    throw new IOException("Simulated backup failure.");
                }
                Backups[path] = bytes;
            }

            public void WriteTarget(string path, byte[] bytes)
            {
                TargetWrites++;
                if (path == FailTargetPath)
                {
                    throw new IOException("Simulated write failure.");
                }
                Files[path] = bytes;
                if (path == MutateAfterTargetPath)
                {
                    Files[path] = new byte[] { 99 };
                }
            }
        }

        private sealed class FakeScanContext : OdinMigrationTool.IOdinMigrationScanContext
        {
            internal readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
            internal readonly HashSet<string> FullPathFailures = new HashSet<string>();
            internal readonly List<string> Reads = new List<string>();
            internal int CancelAtIndex = -1;

            public string GetFullPath(string assetPath)
            {
                if (FullPathFailures.Contains(assetPath))
                {
                    throw new IOException("Simulated path resolution failure.");
                }
                return assetPath;
            }

            public byte[] ReadAllBytes(string fullPath)
            {
                Reads.Add(fullPath);
                if (Files.TryGetValue(fullPath, out byte[] bytes))
                {
                    return bytes;
                }
                throw new FileNotFoundException("Missing fake source.", fullPath);
            }

            public bool ShouldCancel(string assetPath, int index, int count)
            {
                return index == CancelAtIndex;
            }
        }
    }
}
