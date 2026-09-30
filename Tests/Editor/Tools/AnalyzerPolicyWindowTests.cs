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
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    public sealed class AnalyzerPolicyWindowTests : CommonTestBase
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
        public override void TearDown()
        {
            try
            {
                if (Directory.Exists(_temporaryDirectory))
                {
                    Directory.Delete(_temporaryDirectory, true);
                }
            }
            finally
            {
                base.TearDown();
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
            Assert.AreEqual(19, policies.Count);
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
            StringAssert.Contains("Id=\"WUH019\" Action=\"Warning\"", contents);

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

                Assert.That(
                    AnalyzerPolicyAPI.TrySetSeverity(
                        assetPath,
                        "WUH001",
                        "Error",
                        out string severityMessage
                    ),
                    Is.True,
                    severityMessage
                );
                Assert.That(
                    File.ReadAllText(fullPath),
                    Does.Contain("Id=\"WUH001\" Action=\"Error\"")
                );
                Assert.That(
                    File.ReadAllText(fullPath),
                    Does.Contain("Id=\"WUH018\" Action=\"None\"")
                );
                Assert.That(
                    AnalyzerPolicyAPI.TrySetSeverity(assetPath, null, "Error", out _),
                    Is.False
                );

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

        [TestCase("Default")]
        [TestCase("None")]
        [TestCase("Info")]
        [TestCase("Warning")]
        [TestCase("Error")]
        [TestCase("Hidden")]
        public void PerRuleSeveritySynchronizesExternalChangesAndPreservesConfiguration(
            string action
        )
        {
            string path = GetRulesetPath();
            AnalyzerPolicyWindow window = Track(
                ScriptableObject.CreateInstance<AnalyzerPolicyWindow>()
            );
            window.RulesetPathOverride = path;
            window.RefreshState();
            Assert.That(window.GetAction("WUH010"), Is.EqualTo("Default"));
            Assert.That(File.Exists(path), Is.False);
            File.WriteAllText(
                path,
                "<RuleSet Name=\"Existing\" ToolsVersion=\"15.0\"><Include Path=\"Base.ruleset\" Action=\"Default\"/><Rules AnalyzerId=\"WallstopStudios.UnityHelpers.Analyzers\" RuleNamespace=\"WallstopStudios.UnityHelpers.Analyzers\"><Rule Id=\"WUH001\" Action=\"Warning\"/><Rule Id=\"WUH999\" Action=\"Error\"/></Rules><Rules AnalyzerId=\"Other\"><Rule Id=\"OTHER001\" Action=\"Error\"/></Rules></RuleSet>"
            );
            window.RefreshState();
            Assert.That(window.GetAction("WUH001"), Is.EqualTo("Warning"));
            Assert.That(window.TryApplySeverity("WUH001", action), Is.True);
            Assert.That(window.GetAction("WUH001"), Is.EqualTo(action));
            string contents = File.ReadAllText(path);
            Assert.That(contents, Does.Contain("Id=\"WUH001\" Action=\"" + action + "\""));
            Assert.That(contents, Does.Contain("Id=\"WUH999\" Action=\"Error\""));
            Assert.That(contents, Does.Contain("Id=\"OTHER001\" Action=\"Error\""));
            Assert.That(contents, Does.Contain("Path=\"Base.ruleset\""));
            File.WriteAllText(
                path,
                contents.Replace(
                    "Id=\"WUH001\" Action=\"" + action + "\"",
                    "Id=\"WUH001\" Action=\"None\""
                )
            );
            window.OnInspectorUpdate();
            Assert.That(window.GetAction("WUH001"), Is.EqualTo("None"));
            File.WriteAllText(
                path,
                File.ReadAllText(path)
                    .Replace("Id=\"WUH001\" Action=\"None\"", "Id=\"WUH001\" Action=\"Error\"")
            );
            Assert.That(window.TryApplySeverity("WUH002", "Info"), Is.True);
            Assert.That(window.GetAction("WUH001"), Is.EqualTo("Error"));
            Assert.That(window.GetAction("WUH002"), Is.EqualTo("Info"));
            File.Delete(path);
            window.RefreshState();
            Assert.That(window.GetAction("WUH001"), Is.EqualTo("Default"));
        }

        [TestCase("WUH001", "Fatal", "<RuleSet />")]
        [TestCase("WUH999", "Warning", "<RuleSet />")]
        [TestCase("WUH001", null, "<RuleSet />")]
        [TestCase("WUH001", "Warning", "<NotRules />")]
        [TestCase("WUH001", "Warning", "<RuleSet><Rules>")]
        [TestCase(
            "WUH001",
            "Info",
            "<RuleSet><Rules AnalyzerId=\"WallstopStudios.UnityHelpers.Analyzers\"><Rule Id=\"WUH001\" Action=\"Warning\"/><Rule Id=\"WUH001\" Action=\"None\"/></Rules></RuleSet>"
        )]
        public void PerRuleSeverityRejectsInvalidInputWithoutChangingFile(
            string id,
            string action,
            string contents
        )
        {
            string path = GetRulesetPath();
            File.WriteAllText(path, contents);
            Assert.That(
                AnalyzerPolicyRuleset.TryWriteSeverity(
                    path,
                    id,
                    action,
                    AnalyzerPolicyWindow.GetPolicies(),
                    out string message
                ),
                Is.False
            );
            Assert.That(message, Is.Not.Empty);
            Assert.That(File.ReadAllText(path), Is.EqualTo(contents));
        }

        [TestCase("default", "Default")]
        [TestCase("none", "None")]
        [TestCase("info", "Info")]
        [TestCase("warning", "Warning")]
        [TestCase("error", "Error")]
        [TestCase("hidden", "Hidden")]
        public void SeverityActionsAreWrittenWithCanonicalCasing(string input, string expected)
        {
            string path = GetRulesetPath();
            Assert.That(
                AnalyzerPolicyRuleset.TryWriteSeverity(
                    path,
                    "WUH001",
                    input,
                    AnalyzerPolicyWindow.GetPolicies(),
                    out string message
                ),
                Is.True,
                message
            );
            Assert.That(File.ReadAllText(path), Does.Contain("Action=\"" + expected + "\""));
        }

        [TestCase("Fatal")]
        [TestCase(null)]
        public void InvalidExternalSeverityIsReportedWithoutChangingFile(string action)
        {
            string path = GetRulesetPath();
            string contents =
                "<RuleSet><Rules AnalyzerId=\"WallstopStudios.UnityHelpers.Analyzers\"><Rule Id=\"WUH001\" Action=\""
                + action
                + "\"/></Rules></RuleSet>";
            File.WriteAllText(path, contents);
            Dictionary<string, string> actions = new();
            Assert.That(
                AnalyzerPolicyRuleset.TryReadActions(
                    path,
                    AnalyzerPolicyWindow.GetPolicies(),
                    actions,
                    out string message
                ),
                Is.False
            );
            Assert.That(message, Does.Contain("invalid or duplicate"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(contents));
        }

        [Test]
        public void NonRuleNodesAreIgnoredAndPreservedWhenUpdatingSeverity()
        {
            string path = GetRulesetPath();
            File.WriteAllText(
                path,
                "<RuleSet><Rules AnalyzerId=\"WallstopStudios.UnityHelpers.Analyzers\"><Extension Id=\"WUH001\" Action=\"Custom\"/><Rule Id=\"WUH001\" Action=\"Warning\"/></Rules></RuleSet>"
            );
            Dictionary<string, string> actions = new();
            Assert.That(
                AnalyzerPolicyRuleset.TryReadActions(
                    path,
                    AnalyzerPolicyWindow.GetPolicies(),
                    actions,
                    out string readMessage
                ),
                Is.True,
                readMessage
            );
            Assert.That(actions.TryGetValue("WUH001", out string action), Is.True);
            Assert.That(action, Is.EqualTo("Warning"));
            Assert.That(
                AnalyzerPolicyRuleset.TryWriteSeverity(
                    path,
                    "WUH001",
                    "Info",
                    AnalyzerPolicyWindow.GetPolicies(),
                    out string writeMessage
                ),
                Is.True,
                writeMessage
            );
            Assert.That(
                File.ReadAllText(path),
                Does.Contain("Extension Id=\"WUH001\" Action=\"Custom\"")
            );
        }

        [TestCase(true, "Warning")]
        [TestCase(false, "None")]
        public void BulkSeverityPreservesUnknownXmlAndRepairsDuplicateKnownOverrides(
            bool enabled,
            string action
        )
        {
            AnalyzerPolicyState state = enabled
                ? AnalyzerPolicyState.Enabled
                : AnalyzerPolicyState.Disabled;
            string path = GetRulesetPath();
            File.WriteAllText(
                path,
                "<RuleSet><Include Path=\"Base.ruleset\" Action=\"Default\"/><Rules AnalyzerId=\"WallstopStudios.UnityHelpers.Analyzers\"><!--Keep--><Extension Id=\"WUH001\" Action=\"Custom\"/><Rule Id=\"WUH999\" Action=\"Error\"/><Rule Id=\"WUH001\" Action=\"Warning\"/></Rules><Rules AnalyzerId=\"WallstopStudios.UnityHelpers.Analyzers\"><Rule Id=\"WUH001\" Action=\"None\"/><Rule Id=\"WUH998\" Action=\"Info\"/></Rules></RuleSet>"
            );
            Assert.That(
                AnalyzerPolicyRuleset.TryWrite(
                    path,
                    state,
                    AnalyzerPolicyWindow.GetPolicies(),
                    out string writeMessage
                ),
                Is.True,
                writeMessage
            );
            string contents = File.ReadAllText(path);
            Assert.That(contents, Does.Contain("<!--Keep-->"));
            Assert.That(contents, Does.Contain("Path=\"Base.ruleset\""));
            Assert.That(contents, Does.Contain("Extension Id=\"WUH001\" Action=\"Custom\""));
            Assert.That(contents, Does.Contain("Id=\"WUH999\" Action=\"Error\""));
            Assert.That(contents, Does.Contain("Id=\"WUH998\" Action=\"Info\""));
            Dictionary<string, string> actions = new();
            Assert.That(
                AnalyzerPolicyRuleset.TryReadActions(
                    path,
                    AnalyzerPolicyWindow.GetPolicies(),
                    actions,
                    out string readMessage
                ),
                Is.True,
                readMessage
            );
            Assert.That(actions.Count, Is.EqualTo(AnalyzerPolicyWindow.GetPolicies().Count));
            foreach (KeyValuePair<string, string> entry in actions)
            {
                Assert.That(entry.Value, Is.EqualTo(action));
            }
        }

        private string GetRulesetPath()
        {
            return Path.Combine(_temporaryDirectory, "Default.ruleset");
        }
    }
}
