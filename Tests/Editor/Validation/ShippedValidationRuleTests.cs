// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous.Rules;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Pins the rules this package ships: what each claims, what it reports and at what severity,
    /// what it says about an asset it could not read, and that a suppression silences it.
    /// </summary>
    /// <remarks>
    /// The severities are asserted rather than assumed. They are the decision that makes a severity
    /// floor mean anything, and a rule whose severity drifted would still pass every test that only
    /// counted findings.
    /// </remarks>
    [TestFixture]
    public sealed class ShippedValidationRuleTests : CommonTestBase
    {
        private const string TestTypesFolder = "/TestTypes/";
        private const string TestAssetsFolder = "/TestAssets/";
        private const string EmptyRequirementsAsset = "EmptyRequirements.asset";
        private const string FilledRequirementsAsset = "FilledRequirements.asset";
        private const string SpritePropertyName = "m_Sprite";

        [SetUp]
        public void CreateRuleFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), $"shipped-validation-rule-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);

            MonoScriptIndex.ClearCaches();
            Assert.IsTrue(
                MonoScriptIndex.TryGetScriptPath(
                    typeof(AuthoredRequirementTestAsset),
                    out string scriptPath
                ),
                "The fixture type has no MonoScript, so nothing here can locate its assets."
            );

            _scriptPath = scriptPath;
            int marker = scriptPath.IndexOf(TestTypesFolder, StringComparison.Ordinal);
            Assert.IsTrue(0 <= marker, scriptPath);
            string root = scriptPath.Substring(0, marker) + TestAssetsFolder;
            _emptyRequirements = root + EmptyRequirementsAsset;
            _filledRequirements = root + FilledRequirementsAsset;
        }

        [TearDown]
        public void DeleteRuleFixture()
        {
            MonoScriptIndex.ClearCaches();
            if (!string.IsNullOrEmpty(_root) && Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            _root = null;
            _scriptPath = null;
            _emptyRequirements = null;
            _filledRequirements = null;
        }

        /// <summary>
        /// The id is half of every finding's identity and is what a suppression names, so a
        /// collision or a rename is a compatibility break rather than a tidy-up.
        /// </summary>
        [Test]
        public void EveryShippedRuleIsNamedUnderThePackagePrefixAndIsDistinct()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            List<IValidationRule> rules = ShippedRules();

            foreach (IValidationRule rule in rules)
            {
                Assert.IsTrue(
                    rule.RuleId.StartsWith(ValidationRuleIds.Prefix, StringComparison.Ordinal),
                    rule.RuleId
                );
                Assert.IsTrue(ids.Add(rule.RuleId), rule.RuleId + " is not unique");
                Assert.IsFalse(string.IsNullOrEmpty(rule.DisplayName), rule.RuleId);
            }

            Assert.AreEqual(4, ids.Count);
        }

        [TestCase("Assets/Level.unity", true)]
        [TestCase("Assets/Enemy.prefab", true)]
        [TestCase("Assets/Settings.asset", true)]
        [TestCase("Assets/Sprite.png", false)]
        [TestCase("Assets/Clip.anim", false)]
        [TestCase("Assets/Script.cs", false)]
        public void ATextRuleClaimsTheFilesUnityWroteAuthoredDocumentsInto(
            string assetPath,
            bool expected
        )
        {
            ValidationTarget target = Target(assetPath, typeof(ScriptableObject));

            Assert.AreEqual(
                expected,
                new AuthoredRequirementRule().AppliesTo(in target),
                assetPath
            );
            Assert.AreEqual(
                expected,
                new SerializableDictionaryPairingRule().AppliesTo(in target),
                assetPath
            );
        }

        [Test]
        public void ATextRuleRefusesANativeAssetItCouldOnlyReportACoverageHoleFor()
        {
            // Unity writes LightingData.asset as binary even under ForceText; exclude it by imported type.
            ValidationTarget native = Target("Assets/LightingData.asset", typeof(Texture2D));
            ValidationTarget authored = Target(
                "Assets/Settings.asset",
                typeof(AuthoredRequirementTestAsset)
            );
            ValidationTarget unknown = Target("Assets/Unknown.asset", null);

            Assert.IsFalse(new AuthoredRequirementRule().AppliesTo(in native));
            Assert.IsTrue(
                new AuthoredRequirementRule().AppliesTo(in authored),
                "the control has to move, or the refusal above proves nothing"
            );
            Assert.IsTrue(
                new AuthoredRequirementRule().AppliesTo(in unknown),
                "an asset Unity reports no type for is read rather than assumed clean"
            );
        }

        [TestCase("Assets/Clip.anim", true)]
        [TestCase("Assets/Settings.asset", false)]
        public void TheAnimationRuleClaimsClipsOnly(string assetPath, bool expected)
        {
            ValidationTarget target = Target(
                assetPath,
                expected ? typeof(AnimationClip) : typeof(ScriptableObject)
            );

            Assert.AreEqual(expected, new AnimationKeyframeRule().AppliesTo(in target), assetPath);
        }

        [TestCase("Assets/Script.cs", true)]
        [TestCase("Assets/Settings.asset", false)]
        public void TheScriptRuleClaimsScriptAssetsOnly(string assetPath, bool expected)
        {
            ValidationTarget target = Target(
                assetPath,
                expected ? typeof(MonoScript) : typeof(ScriptableObject)
            );

            Assert.AreEqual(expected, new ScriptFileNameRule().AppliesTo(in target), assetPath);
        }

        [Test]
        public void NoRuleClaimsATargetThatNamesNothing()
        {
            ValidationTarget nothing = default;

            foreach (IValidationRule rule in ShippedRules())
            {
                Assert.IsFalse(rule.AppliesTo(in nothing), rule.RuleId);
            }
        }

        [Test]
        public void AnEmptyRequiredSlotIsReportedAsAnErrorAgainstTheRequiredFieldRule()
        {
            AuthoredRequirementRule rule = new AuthoredRequirementRule();
            ValidationTarget target = AssetTarget(_emptyRequirements);

            List<ValidationFinding> findings = Judge(
                rule,
                target,
                LoadMainAsset(_emptyRequirements)
            );

            Assert.IsTrue(0 < findings.Count, _emptyRequirements);
            foreach (ValidationFinding finding in findings)
            {
                Assert.AreEqual(ValidationRuleIds.RequiredFieldEmpty, finding.RuleId);
                Assert.AreEqual(ValidationSeverity.Error, finding.Severity);
                StringAssert.Contains(nameof(AuthoredRequirementTestAsset), finding.Discriminator);
            }
        }

        [Test]
        public void AFilledAssetIsNotReported()
        {
            AuthoredRequirementRule rule = new AuthoredRequirementRule();
            ValidationTarget target = AssetTarget(_filledRequirements);

            List<ValidationFinding> findings = Judge(
                rule,
                target,
                LoadMainAsset(_filledRequirements)
            );

            CollectionAssert.IsEmpty(
                findings,
                "A gate that fires on a correct asset is one developers turn off."
            );
        }

        [Test]
        public void TheRequiredFieldIndexIsBuiltOnceRatherThanPerAsset()
        {
            // The field index must not repeat its TypeCache scan for each asset.
            AuthoredRequirementRule rule = new AuthoredRequirementRule();
            IReadOnlyDictionary<string, List<AuthoredRequirementField>> first =
                rule.FieldsByScriptGuid;

            Judge(rule, AssetTarget(_emptyRequirements), LoadMainAsset(_emptyRequirements));
            Judge(rule, AssetTarget(_filledRequirements), LoadMainAsset(_filledRequirements));

            Assert.AreSame(first, rule.FieldsByScriptGuid);
            Assert.IsTrue(
                0 < first.Count,
                "the index has to have found the fixture's annotated fields, or the identity above "
                    + "is an identity between two empty answers"
            );
        }

        [TestCase(SerializableDictionaryAssetProblem.ValuesDropped, ValidationSeverity.Error)]
        [TestCase(SerializableDictionaryAssetProblem.ValueCountMismatch, ValidationSeverity.Error)]
        [TestCase(
            SerializableDictionaryAssetProblem.NullValueBesideKey,
            ValidationSeverity.Warning
        )]
        public void ADictionarySeverityIsDecidedPerProblem(
            SerializableDictionaryAssetProblem problem,
            ValidationSeverity expected
        )
        {
            Assert.AreEqual(expected, SerializableDictionaryPairingRule.SeverityOf(problem));
        }

        [Test]
        public void EachDictionaryProblemIsReportedAtItsOwnSeverityAndNamedByItsProblem()
        {
            string assetPath = WriteAsset(
                "TwoProblems",
                new[]
                {
                    "  _dropped:",
                    "    _keys:",
                    "    - Idle",
                    "  _paired:",
                    "    _keys:",
                    "    - Idle",
                    "    _values:",
                    "    - {fileID: 0}",
                    "    _boxedValues: []",
                }
            );

            List<ValidationFinding> findings = Judge(
                new SerializableDictionaryPairingRule(),
                Target(assetPath, typeof(ScriptableObject)),
                null
            );

            Assert.AreEqual(2, findings.Count, Describe(findings));
            Assert.AreEqual(ValidationRuleIds.DictionaryPairing, findings[0].RuleId);
            Assert.AreEqual(ValidationSeverity.Error, findings[0].Severity);
            Assert.AreEqual(
                nameof(SerializableDictionaryAssetProblem.ValuesDropped),
                findings[0].Discriminator
            );
            Assert.AreEqual(ValidationSeverity.Warning, findings[1].Severity);
            Assert.AreEqual(
                nameof(SerializableDictionaryAssetProblem.NullValueBesideKey),
                findings[1].Discriminator
            );
        }

        [Test]
        public void TwoOfTheSameProblemOnOneAssetGetDiscriminatorsThatDoNotNameALine()
        {
            // Line numbers move after unrelated edits and cannot identify suppressions.
            string assetPath = WriteAsset(
                "TwiceDropped",
                new[]
                {
                    "  _first:",
                    "    _keys:",
                    "    - A",
                    "  _second:",
                    "    _keys:",
                    "    - B",
                }
            );

            List<ValidationFinding> findings = Judge(
                new SerializableDictionaryPairingRule(),
                Target(assetPath, typeof(ScriptableObject)),
                null
            );

            Assert.AreEqual(2, findings.Count, Describe(findings));
            Assert.AreEqual(
                nameof(SerializableDictionaryAssetProblem.ValuesDropped),
                findings[0].Discriminator
            );
            Assert.AreEqual(
                nameof(SerializableDictionaryAssetProblem.ValuesDropped) + "#2",
                findings[1].Discriminator
            );
            Assert.AreNotEqual(findings[0].Id, findings[1].Id);
        }

        [Test]
        public void AHealthyDictionaryIsNotReported()
        {
            string assetPath = WriteAsset(
                "Healthy",
                new[] { "  _map:", "    _keys: []", "    _values: []", "    _boxedValues: []" }
            );

            CollectionAssert.IsEmpty(
                Judge(
                    new SerializableDictionaryPairingRule(),
                    Target(assetPath, typeof(ScriptableObject)),
                    null
                )
            );
        }

        [Test]
        public void AnEmptyKeyframeIsReportedAsAWarningNamingTheCurve()
        {
            AnimationClip clip = ClipWithKeyframes(FilledSprite(), null);
            ValidationTarget target = Target("Assets/Clip.anim", typeof(AnimationClip));

            List<ValidationFinding> findings = Judge(new AnimationKeyframeRule(), target, clip);

            Assert.AreEqual(1, findings.Count, Describe(findings));
            Assert.AreEqual(ValidationRuleIds.AnimationKeyframeEmpty, findings[0].RuleId);
            Assert.AreEqual(ValidationSeverity.Warning, findings[0].Severity);
            StringAssert.Contains(SpritePropertyName, findings[0].Discriminator);
        }

        [Test]
        public void AClipWhoseKeyframesAllResolveIsNotReported()
        {
            AnimationClip clip = ClipWithKeyframes(FilledSprite(), FilledSprite());
            ValidationTarget target = Target("Assets/Clip.anim", typeof(AnimationClip));

            CollectionAssert.IsEmpty(Judge(new AnimationKeyframeRule(), target, clip));
        }

        [Test]
        public void AScriptFileNotNamedAfterWhatItBindsIsReportedAsAWarning()
        {
            // Use a synthetic path because committing a misnamed script would break repository naming rules.
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(_scriptPath);
            Assert.IsTrue(script != null, _scriptPath);
            ValidationTarget target = Target("Assets/NotTheTypeName.cs", typeof(MonoScript));

            List<ValidationFinding> findings = Judge(new ScriptFileNameRule(), target, script);

            Assert.AreEqual(1, findings.Count, Describe(findings));
            Assert.AreEqual(ValidationRuleIds.ScriptFileNameMismatch, findings[0].RuleId);
            Assert.AreEqual(ValidationSeverity.Warning, findings[0].Severity);
            StringAssert.Contains(nameof(AuthoredRequirementTestAsset), findings[0].Message);
        }

        [Test]
        public void AScriptFileNamedAfterWhatItBindsIsNotReported()
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(_scriptPath);
            Assert.IsTrue(script != null, _scriptPath);

            CollectionAssert.IsEmpty(
                Judge(new ScriptFileNameRule(), AssetTarget(_scriptPath), script)
            );
        }

        /// <summary>
        /// Reporting nothing here would be a rule claiming an asset it never opened is fine, which
        /// is the failure the whole subsystem exists to prevent, turned on itself.
        /// </summary>
        [Test]
        public void ATextRuleThatCouldNotReadAnAssetSaysSoRatherThanReportingClean()
        {
            ValidationTarget absent = Target(
                Path.Combine(_root, "DeletedBeforeTheRead.asset"),
                typeof(ScriptableObject)
            );

            AssertCoverageHole(new AuthoredRequirementRule(), absent, null);
            AssertCoverageHole(new SerializableDictionaryPairingRule(), absent, null);
        }

        [Test]
        public void ARuleHandedNoAssetReportsTheHoleRatherThanThrowing()
        {
            AssertCoverageHole(
                new AnimationKeyframeRule(),
                Target("Assets/Clip.anim", typeof(AnimationClip)),
                null
            );
            AssertCoverageHole(
                new ScriptFileNameRule(),
                Target("Assets/Script.cs", typeof(MonoScript)),
                null
            );
        }

        /// <summary>
        /// The text rules read the committed file rather than the loaded object, so a null asset
        /// costs them the ping target and nothing else.
        /// </summary>
        [Test]
        public void ATextRuleJudgesTheFileEvenWhenTheAssetItselfDidNotLoad()
        {
            List<ValidationFinding> findings = Judge(
                new AuthoredRequirementRule(),
                AssetTarget(_emptyRequirements),
                null
            );

            Assert.IsTrue(0 < findings.Count, _emptyRequirements);
            Assert.IsFalse(findings[0].TryGetTarget(out Object target));
            Assert.IsTrue(target == null);
        }

        [Test]
        public void EveryRuleToleratesANullFindingList()
        {
            ValidationTarget target = AssetTarget(_emptyRequirements);

            foreach (IValidationRule rule in ShippedRules())
            {
                Assert.DoesNotThrow(() => rule.Validate(in target, null, null), rule.RuleId);
            }
        }

        [Test]
        public void AFindingIsSuppressedByItsOwnRuleAndByNoOther()
        {
            List<ValidationFinding> findings = Judge(
                new AuthoredRequirementRule(),
                AssetTarget(_emptyRequirements),
                null
            );
            Assert.IsTrue(0 < findings.Count, _emptyRequirements);

            ValidationSuppressions suppressions = ValidationSuppressions.Parse(
                ValidationSuppressions.Render(findings)
            );

            Assert.IsTrue(suppressions.IsSuppressed(findings[0]));
            Assert.IsFalse(
                suppressions.IsSuppressed(
                    UnderRule(findings[0], ValidationRuleIds.DictionaryPairing)
                ),
                "a suppression names one rule, so silencing one check must not silence another"
            );
        }

        /// <summary>
        /// Exercises the rules through the engine that will actually call them, rather than only
        /// through direct calls that cannot see an AppliesTo that claims nothing.
        /// </summary>
        [Test]
        public void AShippedRuleReportsThroughACompleteRun()
        {
            ValidationRun run = new ValidationRun(
                ShippedRules(),
                new List<ValidationTarget> { AssetTarget(_emptyRequirements) }
            );

            while (!run.Step(double.MaxValue)) { }

            CollectionAssert.IsEmpty(
                run.Failures,
                "no shipped rule may throw on a committed asset"
            );
            bool reported = false;
            foreach (ValidationFinding finding in run.Findings)
            {
                reported |= string.Equals(
                    finding.RuleId,
                    ValidationRuleIds.RequiredFieldEmpty,
                    StringComparison.Ordinal
                );
            }

            Assert.IsTrue(reported, Describe(new List<ValidationFinding>(run.Findings)));
        }

        private void AssertCoverageHole(IValidationRule rule, ValidationTarget target, Object asset)
        {
            List<ValidationFinding> findings = Judge(rule, target, asset);

            Assert.AreEqual(1, findings.Count, rule.RuleId + ": " + Describe(findings));
            Assert.AreEqual(rule.RuleId, findings[0].RuleId);
            Assert.AreEqual(ValidationSeverity.Info, findings[0].Severity);
            Assert.AreEqual(
                ValidationCoverage.UnreadableDiscriminator,
                findings[0].Discriminator,
                "a coverage hole has to be told apart from a defect by something other than wording"
            );
        }

        private static List<ValidationFinding> Judge(
            IValidationRule rule,
            ValidationTarget target,
            Object asset
        )
        {
            List<ValidationFinding> findings = new List<ValidationFinding>();
            rule.Validate(in target, asset, findings);
            return findings;
        }

        private static List<IValidationRule> ShippedRules()
        {
            return new List<IValidationRule>
            {
                new AuthoredRequirementRule(),
                new SerializableDictionaryPairingRule(),
                new AnimationKeyframeRule(),
                new ScriptFileNameRule(),
            };
        }

        private static ValidationFinding UnderRule(ValidationFinding finding, string ruleId)
        {
            return new ValidationFinding(
                ruleId,
                finding.Severity,
                null,
                finding.AssetGuid,
                finding.AssetPath,
                finding.Discriminator,
                finding.Message
            );
        }

        private static ValidationTarget Target(string assetPath, Type mainAssetType)
        {
            return new ValidationTarget(GuidOf(assetPath), assetPath, mainAssetType);
        }

        private static ValidationTarget AssetTarget(string assetPath)
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            Assert.IsFalse(
                string.IsNullOrEmpty(guid),
                assetPath
                    + " is not in the asset database, so the fixture is looking in the wrong "
                    + "place rather than at a clean project"
            );

            return new ValidationTarget(
                guid,
                assetPath,
                AssetDatabase.GetMainAssetTypeAtPath(assetPath)
            );
        }

        private static Object LoadMainAsset(string assetPath)
        {
            return AssetDatabase.LoadMainAssetAtPath(assetPath);
        }

        private static string GuidOf(string assetPath)
        {
            return ((uint)StringComparer.Ordinal.GetHashCode(assetPath)).ToString("x32");
        }

        private static string Describe(List<ValidationFinding> findings)
        {
            List<string> rendered = new List<string>(findings.Count);
            foreach (ValidationFinding finding in findings)
            {
                rendered.Add(finding.ToString());
            }

            return string.Join(Environment.NewLine, rendered);
        }

        private Sprite FilledSprite()
        {
            Texture2D texture = Track(new Texture2D(4, 4));
            return Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f)));
        }

        private AnimationClip ClipWithKeyframes(params Sprite[] values)
        {
            AnimationClip clip = Track(new AnimationClip());
            clip.name = nameof(ClipWithKeyframes);
            EditorCurveBinding binding = EditorCurveBinding.PPtrCurve(
                string.Empty,
                typeof(SpriteRenderer),
                SpritePropertyName
            );

            ObjectReferenceKeyframe[] keyframes = new ObjectReferenceKeyframe[values.Length];
            for (int index = 0; index < values.Length; ++index)
            {
                keyframes[index].time = index * 0.5f;
                keyframes[index].value = values[index];
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
            return clip;
        }

        private string WriteAsset(string name, IReadOnlyList<string> body)
        {
            List<string> lines = new List<string>
            {
                "%YAML 1.1",
                "%TAG !u! tag:unity3d.com,2011:",
                "--- !u!114 &11400000",
                "MonoBehaviour:",
                "  m_Script: {fileID: 11500000, guid: aaa, type: 3}",
                $"  m_Name: {name}",
            };
            lines.AddRange(body);

            string assetPath = Path.Combine(_root, $"{name}.asset");
            File.WriteAllLines(assetPath, lines);
            return assetPath;
        }

        private string _root;
        private string _scriptPath;
        private string _emptyRequirements;
        private string _filledRequirements;
    }
}
