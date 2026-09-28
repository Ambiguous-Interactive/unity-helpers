// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Tools;

    [TestFixture]
    public sealed class AnalyzerPolicyWindowTests
    {
        private string _temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "unity-helpers-analyzer-policy-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, true);
            }
        }

        [Test]
        public void AnalyzerPolicyLifecyclePreservesConfigurationAndRepairsDrift()
        {
            IReadOnlyList<AnalyzerPolicy> policies = AnalyzerPolicyWindow.GetPolicies();
            string path = GetRulesetPath();

            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(Application.dataPath, "Default.ruleset")),
                AnalyzerPolicyWindow.GetRulesetPath()
            );
            Assert.AreEqual(18, policies.Count);
            for (int index = 0; index < policies.Count; ++index)
            {
                Assert.AreEqual($"WUH{index + 1:000}", policies[index].Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(policies[index].Title));
                Assert.IsFalse(string.IsNullOrWhiteSpace(policies[index].Description));
            }

            Assert.IsTrue(
                AnalyzerPolicyRuleset.TryRead(
                    path,
                    policies,
                    out AnalyzerPolicyState missingState,
                    out string missingMessage
                ),
                missingMessage
            );
            Assert.AreEqual(AnalyzerPolicyState.Missing, missingState);
            Assert.IsFalse(File.Exists(path));

            File.WriteAllText(
                path,
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                    + "<RuleSet Name=\"Existing\" ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\n"
                    + "  <Rules AnalyzerId=\"Microsoft.CodeAnalysis.CSharp\" RuleNamespace=\"Microsoft.CodeAnalysis.CSharp\">\n"
                    + "    <Rule Id=\"CS0618\" Action=\"Error\" />\n"
                    + "  </Rules>\n"
                    + "</RuleSet>\n"
            );
            Assert.IsTrue(
                AnalyzerPolicyRuleset.TryWrite(
                    path,
                    AnalyzerPolicyState.Enabled,
                    policies,
                    out string enableMessage
                ),
                enableMessage
            );
            Assert.IsTrue(
                AnalyzerPolicyRuleset.TryRead(
                    path,
                    policies,
                    out AnalyzerPolicyState enabledState,
                    out string enabledMessage
                ),
                enabledMessage
            );
            Assert.AreEqual(AnalyzerPolicyState.Enabled, enabledState);
            string contents = File.ReadAllText(path);
            StringAssert.Contains("AnalyzerId=\"Microsoft.CodeAnalysis.CSharp\"", contents);
            StringAssert.Contains("Id=\"CS0618\" Action=\"Error\"", contents);
            StringAssert.Contains("Id=\"WUH001\" Action=\"Warning\"", contents);
            StringAssert.Contains("Id=\"WUH018\" Action=\"Warning\"", contents);

            File.WriteAllText(
                path,
                contents.Replace(
                    "Id=\"WUH018\" Action=\"Warning\"",
                    "Id=\"WUH018\" Action=\"None\""
                )
            );
            Assert.IsTrue(
                AnalyzerPolicyRuleset.TryRead(
                    path,
                    policies,
                    out AnalyzerPolicyState driftedState,
                    out string driftedMessage
                ),
                driftedMessage
            );
            Assert.AreEqual(AnalyzerPolicyState.Drifted, driftedState);
            Assert.IsTrue(
                AnalyzerPolicyRuleset.TryWrite(
                    path,
                    AnalyzerPolicyState.Disabled,
                    policies,
                    out string disableMessage
                ),
                disableMessage
            );
            Assert.IsTrue(
                AnalyzerPolicyRuleset.TryRead(
                    path,
                    policies,
                    out AnalyzerPolicyState disabledState,
                    out string disabledMessage
                ),
                disabledMessage
            );
            Assert.AreEqual(AnalyzerPolicyState.Disabled, disabledState);
            StringAssert.DoesNotContain("Action=\"Warning\"", File.ReadAllText(path));

            const string malformed = "<RuleSet><Rules>";
            File.WriteAllText(path, malformed);
            Assert.IsFalse(
                AnalyzerPolicyRuleset.TryWrite(
                    path,
                    AnalyzerPolicyState.Enabled,
                    policies,
                    out string malformedMessage
                )
            );
            StringAssert.Contains("left unchanged", malformedMessage);
            Assert.AreEqual(malformed, File.ReadAllText(path));
        }

        [Test]
        public void ExplicitAssetRulesetWritesAndImportsWithoutOpeningWindow()
        {
            Assert.That(
                AnalyzerPolicyAPI.DefaultRulesetAssetPath,
                Is.EqualTo("Assets/Default.ruleset")
            );
            string folderName = nameof(AnalyzerPolicyWindowTests) + Guid.NewGuid().ToString("N");
            string folderPath = "Assets/" + folderName;
            Assert.That(AssetDatabase.CreateFolder("Assets", folderName), Is.Not.Empty);
            try
            {
                string assetPath = folderPath + "/Default.ruleset";
                string fullPath = Path.Combine(Application.dataPath, folderName, "Default.ruleset");
                Assert.That(
                    AnalyzerPolicyAPI.TrySetEnabled(assetPath, true, out string createMessage),
                    Is.True,
                    createMessage
                );
                Assert.That(File.Exists(fullPath), Is.True);
                Assert.That(AssetDatabase.AssetPathToGUID(assetPath), Is.Not.Empty);
                File.WriteAllText(
                    fullPath,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                        + "<RuleSet Name=\"Existing\" ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\n"
                        + "  <Rules AnalyzerId=\"Microsoft.CodeAnalysis.CSharp\" RuleNamespace=\"Microsoft.CodeAnalysis.CSharp\">\n"
                        + "    <Rule Id=\"CS0618\" Action=\"Error\" />\n"
                        + "  </Rules>\n"
                        + "</RuleSet>\n"
                );

                Assert.That(
                    AnalyzerPolicyAPI.TrySetEnabled(
                        assetPath.Replace('/', '\\'),
                        true,
                        out string enableMessage
                    ),
                    Is.True,
                    enableMessage
                );
                Assert.That(AssetDatabase.AssetPathToGUID(assetPath), Is.Not.Empty);
                string enabledContent = File.ReadAllText(fullPath);
                Assert.That(enabledContent, Does.Contain("Id=\"CS0618\" Action=\"Error\""));
                Assert.That(enabledContent, Does.Contain("Id=\"WUH001\" Action=\"Warning\""));
                Assert.That(enabledContent, Does.Contain("Id=\"WUH018\" Action=\"Warning\""));

                Assert.That(
                    AnalyzerPolicyAPI.TrySetEnabled(assetPath, false, out string disableMessage),
                    Is.True,
                    disableMessage
                );
                string disabledContent = File.ReadAllText(fullPath);
                Assert.That(disabledContent, Does.Contain("Id=\"CS0618\" Action=\"Error\""));
                Assert.That(disabledContent, Does.Contain("Id=\"WUH001\" Action=\"None\""));
                Assert.That(disabledContent, Does.Contain("Id=\"WUH018\" Action=\"None\""));

                const string malformed = "<RuleSet><Rules>";
                File.WriteAllText(fullPath, malformed);
                Assert.That(
                    AnalyzerPolicyAPI.TrySetEnabled(assetPath, true, out string malformedMessage),
                    Is.False
                );
                Assert.That(malformedMessage, Does.Contain("left unchanged"));
                Assert.That(File.ReadAllText(fullPath), Is.EqualTo(malformed));
                Assert.That(
                    AnalyzerPolicyAPI.TrySetEnabled(
                        folderPath + "/../outside.ruleset",
                        true,
                        out string escapedPathMessage
                    ),
                    Is.False
                );
                Assert.That(escapedPathMessage, Is.Not.Empty);
                Assert.That(
                    AnalyzerPolicyAPI.TrySetEnabled(null, true, out string nullPathMessage),
                    Is.False
                );
                Assert.That(nullPathMessage, Is.Not.Empty);
                Assert.That(
                    AnalyzerPolicyAPI.TrySetEnabled(
                        folderPath + "/missing/Other.ruleset",
                        true,
                        out string missingFolderMessage
                    ),
                    Is.False
                );
                Assert.That(missingFolderMessage, Is.Not.Empty);
                Assert.That(
                    AnalyzerPolicyAPI.TrySetEnabled(
                        folderPath + "/not-ruleset.txt",
                        true,
                        out string extensionMessage
                    ),
                    Is.False
                );
                Assert.That(extensionMessage, Is.Not.Empty);
                Assert.That(File.ReadAllText(fullPath), Is.EqualTo(malformed));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folderPath);
            }
        }

        private string GetRulesetPath()
        {
            return Path.Combine(_temporaryDirectory, "Default.ruleset");
        }
    }
}
