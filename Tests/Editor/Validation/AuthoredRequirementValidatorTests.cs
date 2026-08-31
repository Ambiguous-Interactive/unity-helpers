// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
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

        private const string TestTypesFolder = "/TestTypes/";

        private string _filled;
        private string _empty;
        private string _inherited;
    }
}
