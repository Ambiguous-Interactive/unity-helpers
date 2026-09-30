// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Editor.Tools;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    public sealed class AnalyzerPolicyWindowTests : CommonTestBase
    {
        private const string NoGraphicsInitializeMessage =
            "No graphic device is available to initialize the view.";
        private const string NoGraphicsShowMessage =
            "No graphic device is available to show the window.";
        private string _temporaryDirectory;

        private static void ShowWindowWithExpectedGraphicsErrors(Action showWindow, bool headless)
        {
            if (headless)
            {
                Application.logMessageReceived += ExpectKnownGraphicsError;
            }
            try
            {
                showWindow();
            }
            finally
            {
                if (headless)
                {
                    Application.logMessageReceived -= ExpectKnownGraphicsError;
                }
            }
        }

        private static void ExpectKnownGraphicsError(
            string message,
            string stackTrace,
            LogType type
        )
        {
            if (
                type == LogType.Error
                && (
                    string.Equals(message, NoGraphicsInitializeMessage, StringComparison.Ordinal)
                    || string.Equals(message, NoGraphicsShowMessage, StringComparison.Ordinal)
                )
            )
            {
                LogAssert.Expect(type, message);
            }
        }

        private static Rect ToolbarContentBounds(ToolbarButton button)
        {
            Rect bounds = button.worldBound;
            IResolvedStyle style = button.resolvedStyle;
            float left = style.paddingLeft + style.borderLeftWidth;
            float right = style.paddingRight + style.borderRightWidth;
            return new Rect(
                bounds.xMin + left,
                bounds.yMin,
                bounds.width - left - right,
                bounds.height
            );
        }

        private static void PumpPolicyLayout(AnalyzerPolicyWindow window)
        {
            IPanel panel = window.rootVisualElement.panel;
            Assert.That(panel != null, Is.True);
            panel.Pick(Vector2.zero);
        }

        private static void ResizePolicyWindow(
            AnalyzerPolicyWindow window,
            float width,
            float height
        )
        {
            window.position = new Rect(100f, 100f, width, height);
            window.rootVisualElement.style.width = width;
            window.rootVisualElement.style.height = height;
            PumpPolicyLayout(window);
        }

        private static void AssertViewportDimensions(
            AnalyzerPolicyWindow window,
            float width,
            float height
        )
        {
            PumpPolicyLayout(window);
            IResolvedStyle viewport = window.rootVisualElement.resolvedStyle;
            string dimensions =
                "Requested viewport "
                + width
                + "x"
                + height
                + "; resolved viewport "
                + viewport.width
                + "x"
                + viewport.height
                + "; graphics device "
                + SystemInfo.graphicsDeviceType
                + ".";
            Assert.That(window.rootVisualElement.panel != null, Is.True, dimensions);
            Assert.That(viewport.width, Is.EqualTo(width).Within(0.5f), dimensions);
            Assert.That(viewport.height, Is.EqualTo(height).Within(0.5f), dimensions);
        }

        private static void AssertCardInsideWindow(AnalyzerPolicyWindow window)
        {
            Rect rootBounds = window.rootVisualElement.worldBound;
            Rect cardBounds = window.DetailsCard.worldBound;
            Assert.LessOrEqual(rootBounds.xMin - 0.5f, cardBounds.xMin);
            Assert.LessOrEqual(rootBounds.yMin - 0.5f, cardBounds.yMin);
            Assert.LessOrEqual(cardBounds.xMax, rootBounds.xMax + 0.5f);
            Assert.LessOrEqual(cardBounds.yMax, rootBounds.yMax + 0.5f);
        }

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

        [UnityTest]
        public IEnumerator SeverityDropdownSavesAndRefreshChangesControlWithoutWriting()
        {
            AnalyzerPolicyWindow window = CreatePolicyWindow();
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(window.rootVisualElement.panel != null, Is.True);
            string path = GetRulesetPath();
            DropdownField dropdown = window.GetSeverityField("WUH001");
            Assert.That(dropdown.value, Is.EqualTo("Default"));
            Assert.That(File.Exists(path), Is.False);
            window.RefreshState();
            Assert.That(File.Exists(path), Is.False);
            dropdown.value = "Error";
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(File.ReadAllText(path), Does.Contain("Id=\"WUH001\" Action=\"Error\""));
            string external = File.ReadAllText(path).Replace("Action=\"Error\"", "Action=\"Info\"");
            File.WriteAllText(path, external);
            window.RefreshState();
            Assert.That(dropdown.value, Is.EqualTo("Info"));
            Assert.That(ReferenceEquals(dropdown, window.GetSeverityField("WUH001")), Is.True);
            Assert.That(File.ReadAllText(path), Is.EqualTo(external));
            Assert.That(window.DetailsCard.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator SearchControlFiltersIdsTitlesAndDescriptionsWithoutChangingRuleset()
        {
            AnalyzerPolicyWindow window = CreatePolicyWindow();
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(window.rootVisualElement.panel != null, Is.True);
            ToolbarSearchField search = window.rootVisualElement.Q<ToolbarSearchField>();
            (string Query, string ExpectedId)[] cases =
            {
                ("wuh018", "WUH018"),
                ("lookup factory", "WUH001"),
                ("infinite effect", "WUH006"),
                ("   wuh019   ", "WUH019"),
            };
            foreach ((string query, string expectedId) in cases)
            {
                search.value = query;
                yield return null;
                PumpPolicyLayout(window);
                foreach (AnalyzerPolicy policy in AnalyzerPolicyWindow.GetPolicies())
                {
                    DisplayStyle expected = string.Equals(
                        policy.Id,
                        expectedId,
                        StringComparison.Ordinal
                    )
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                    Assert.That(
                        window.GetPolicyRow(policy.Id).style.display.value,
                        Is.EqualTo(expected),
                        query + " matched " + policy.Id + " incorrectly."
                    );
                }
                Assert.That(File.Exists(GetRulesetPath()), Is.False);
            }
            search.value = string.Empty;
            yield return null;
            PumpPolicyLayout(window);
            foreach (AnalyzerPolicy policy in AnalyzerPolicyWindow.GetPolicies())
            {
                Assert.That(
                    window.GetPolicyRow(policy.Id).style.display.value,
                    Is.EqualTo(DisplayStyle.Flex)
                );
            }
        }

        [UnityTest]
        public IEnumerator KeyboardFocusOpensExamplesEnterPinsAndEscapeCloses()
        {
            string previousClipboard = GUIUtility.systemCopyBuffer;
            try
            {
                AnalyzerPolicyWindow window = CreatePolicyWindow();
                yield return null;
                PumpPolicyLayout(window);
                Assert.That(window.rootVisualElement.panel != null, Is.True);
                Button title = window.GetPolicyRow("WUH001").Q<Button>();
                PumpPolicyLayout(window);
                title.Focus();
                yield return null;
                PumpPolicyLayout(window);
                Assert.That(window.DetailsCard.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(window.DetailsCard.Q<Label>().text, Does.Contain("WUH001"));
                using (
                    KeyDownEvent enter = KeyDownEvent.GetPooled(
                        new Event { type = EventType.KeyDown, keyCode = KeyCode.Return }
                    )
                )
                {
                    title.SendEvent(enter);
                }
                yield return null;
                PumpPolicyLayout(window);
                window.CopyFixButton.Focus();
                yield return null;
                PumpPolicyLayout(window);
                Assert.That(
                    window.rootVisualElement.panel.focusController.focusedElement,
                    Is.SameAs(window.CopyFixButton)
                );
                using (NavigationSubmitEvent copy = NavigationSubmitEvent.GetPooled())
                {
                    copy.target = window.CopyFixButton;
                    window.CopyFixButton.SendEvent(copy);
                }
                yield return null;
                PumpPolicyLayout(window);
                Assert.That(
                    AnalyzerPolicyExamples.TryGet("WUH001", out _, out string goodCode),
                    Is.True
                );
                Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo(goodCode));
                Button otherTitle = window.GetPolicyRow("WUH002").Q<Button>();
                PumpPolicyLayout(window);
                otherTitle.Focus();
                yield return null;
                PumpPolicyLayout(window);
                Assert.That(window.DetailsCard.Q<Label>().text, Does.Contain("WUH001"));
                using (
                    KeyDownEvent escape = KeyDownEvent.GetPooled(
                        new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }
                    )
                )
                {
                    window.rootVisualElement.SendEvent(escape);
                }
                yield return null;
                PumpPolicyLayout(window);
                Assert.That(window.DetailsCard.style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(File.Exists(GetRulesetPath()), Is.False);
            }
            finally
            {
                GUIUtility.systemCopyBuffer = previousClipboard;
            }
        }

        [Test]
        public void MalformedExternalRulesetDisablesDropdownAndRecoveryRestoresIt()
        {
            AnalyzerPolicyWindow window = CreatePolicyWindow();
            string path = GetRulesetPath();
            const string malformed = "<RuleSet><Rules>";
            File.WriteAllText(path, malformed);
            window.RefreshState();
            Assert.That(window.GetSeverityField("WUH001").enabledSelf, Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(malformed));
            File.Delete(path);
            window.RefreshState();
            Assert.That(window.GetSeverityField("WUH001").enabledSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator BulkToolbarUsesWindowRulesetAndUpdatesExistingDropdowns()
        {
            AnalyzerPolicyWindow window = CreatePolicyWindow();
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(window.rootVisualElement.panel != null, Is.True);
            DropdownField dropdown = window.GetSeverityField("WUH001");
            ToolbarButton enable = window.rootVisualElement.Q<ToolbarButton>();
            PumpPolicyLayout(window);
            enable.Focus();
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(
                window.rootVisualElement.panel.focusController.focusedElement,
                Is.SameAs(enable)
            );
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = enable;
                enable.SendEvent(submit);
            }
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(File.Exists(GetRulesetPath()), Is.True);
            Assert.That(dropdown.value, Is.EqualTo("Warning"));
            Assert.That(ReferenceEquals(dropdown, window.GetSeverityField("WUH001")), Is.True);
            foreach (AnalyzerPolicy policy in AnalyzerPolicyWindow.GetPolicies())
            {
                Assert.That(window.GetSeverityField(policy.Id).value, Is.EqualTo("Warning"));
            }
        }

        [UnityTest]
        public IEnumerator NarrowWindowStacksExamplesAndKeepsPinnedCardAndToolbarInsideBounds()
        {
            AnalyzerPolicyWindow window = CreatePolicyWindow();
            ResizePolicyWindow(window, 420f, 400f);
            yield return null;
            PumpPolicyLayout(window);
            yield return null;
            PumpPolicyLayout(window);
            AssertViewportDimensions(window, 420f, 400f);
            Button title = window.GetPolicyRow("WUH016").Q<Button>();
            PumpPolicyLayout(window);
            title.Focus();
            yield return null;
            PumpPolicyLayout(window);
            using (
                KeyDownEvent enter = KeyDownEvent.GetPooled(
                    new Event { type = EventType.KeyDown, keyCode = KeyCode.Return }
                )
            )
            {
                title.SendEvent(enter);
            }
            yield return null;
            PumpPolicyLayout(window);
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(
                window.ExamplesContainer.style.flexDirection.value,
                Is.EqualTo(FlexDirection.Column)
            );
            AssertCardInsideWindow(window);
            Vector2 narrowGlyphs = window.BadCodeLabel.MeasureTextSize(
                "iiii",
                0f,
                VisualElement.MeasureMode.Undefined,
                0f,
                VisualElement.MeasureMode.Undefined
            );
            Vector2 wideGlyphs = window.BadCodeLabel.MeasureTextSize(
                "WWWW",
                0f,
                VisualElement.MeasureMode.Undefined,
                0f,
                VisualElement.MeasureMode.Undefined
            );
            Assert.Less(0f, narrowGlyphs.x, "The attached code label must measure visible glyphs.");
            Assert.That(
                narrowGlyphs.x,
                Is.EqualTo(wideGlyphs.x).Within(0.01f),
                "Code rendering must use a monospace font."
            );
            Toolbar toolbar = window.rootVisualElement.Q<Toolbar>();
            ToolbarButton previous = null;
            Rect previousContent = default;
            foreach (VisualElement control in toolbar.Children())
            {
                if (control is ToolbarButton button)
                {
                    Rect content = ToolbarContentBounds(button);
                    Vector2 textSize = button.MeasureTextSize(
                        button.text,
                        0f,
                        VisualElement.MeasureMode.Undefined,
                        0f,
                        VisualElement.MeasureMode.Undefined
                    );
                    Assert.LessOrEqual(
                        textSize.x,
                        content.width,
                        button.text + " does not fit its padded content area."
                    );
                    if (previous != null)
                    {
                        Assert.LessOrEqual(
                            previousContent.xMax,
                            content.xMin,
                            "Toolbar label areas overlap."
                        );
                    }
                    previous = button;
                    previousContent = content;
                }
            }
            ToolbarSearchField search = toolbar.Q<ToolbarSearchField>();
            Assert.IsTrue(previous != null);
            Assert.LessOrEqual(previousContent.xMax, search.worldBound.xMin);
            Assert.LessOrEqual(
                search.worldBound.xMax,
                window.rootVisualElement.worldBound.xMax + 0.5f
            );
            ResizePolicyWindow(window, 750f, 640f);
            yield return null;
            PumpPolicyLayout(window);
            yield return null;
            PumpPolicyLayout(window);
            AssertViewportDimensions(window, 750f, 640f);
            Assert.That(
                window.ExamplesContainer.style.flexDirection.value,
                Is.EqualTo(FlexDirection.Row)
            );
            Assert.That(window.DetailsCard.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(window.DetailsCard.Q<Label>().text, Does.Contain("WUH016"));
            AssertCardInsideWindow(window);
        }

        [UnityTest]
        public IEnumerator PinnedExamplesSurviveOtherControlsSearchAndResizeUntilExplicitlyDismissed()
        {
            AnalyzerPolicyWindow window = CreatePolicyWindow();
            ResizePolicyWindow(window, 750f, 640f);
            yield return null;
            PumpPolicyLayout(window);
            yield return null;
            PumpPolicyLayout(window);
            AssertViewportDimensions(window, 750f, 640f);
            Button title = window.GetPolicyRow("WUH001").Q<Button>();
            PumpPolicyLayout(window);
            title.Focus();
            yield return null;
            PumpPolicyLayout(window);
            using (
                KeyDownEvent enter = KeyDownEvent.GetPooled(
                    new Event { type = EventType.KeyDown, keyCode = KeyCode.Return }
                )
            )
            {
                title.SendEvent(enter);
            }
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(window.DetailsCard.Q<Button>().text, Is.EqualTo("Unpin"));
            Toolbar toolbar = window.rootVisualElement.Q<Toolbar>();
            ToolbarSearchField search = toolbar.Q<ToolbarSearchField>();
            DropdownField severity = window.GetSeverityField("WUH002");
            ToolbarButton refresh = toolbar[2] as ToolbarButton;
            Assert.IsTrue(refresh != null);
            (VisualElement Target, string Label)[] controls =
            {
                (search, "search"),
                (severity, "severity"),
                (refresh, "refresh"),
            };
            foreach ((VisualElement target, string label) in controls)
            {
                using (
                    PointerDownEvent pointer = PointerDownEvent.GetPooled(
                        new Event
                        {
                            type = EventType.MouseDown,
                            button = 0,
                            mousePosition = target.worldBound.center,
                        }
                    )
                )
                {
                    target.SendEvent(pointer);
                }
                yield return null;
                PumpPolicyLayout(window);
                Assert.That(
                    window.DetailsCard.style.display.value,
                    Is.EqualTo(DisplayStyle.Flex),
                    "Clicking " + label + " dismissed the pinned card."
                );
                Assert.That(window.DetailsCard.Q<Label>().text, Does.Contain("WUH001"));
                using (
                    PointerUpEvent pointer = PointerUpEvent.GetPooled(
                        new Event
                        {
                            type = EventType.MouseUp,
                            button = 0,
                            mousePosition = target.worldBound.center,
                        }
                    )
                )
                {
                    target.SendEvent(pointer);
                }
                yield return null;
                PumpPolicyLayout(window);
            }
            severity.value = "Error";
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(window.GetAction("WUH002"), Is.EqualTo("Error"));
            PumpPolicyLayout(window);
            refresh.Focus();
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(
                window.rootVisualElement.panel.focusController.focusedElement,
                Is.SameAs(refresh)
            );
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = refresh;
                refresh.SendEvent(submit);
            }
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(window.DetailsCard.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Button otherTitle = window.GetPolicyRow("WUH002").Q<Button>();
            PumpPolicyLayout(window);
            otherTitle.Focus();
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(window.DetailsCard.Q<Label>().text, Does.Contain("WUH001"));
            float pinnedTop =
                window.DetailsCard.worldBound.y - window.rootVisualElement.worldBound.y;
            search.value = "WUH002";
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(
                window.GetPolicyRow("WUH001").style.display.value,
                Is.EqualTo(DisplayStyle.None)
            );
            Assert.That(
                window.DetailsCard.style.display.value,
                Is.EqualTo(DisplayStyle.Flex),
                "Filtering the anchor should keep the pinned reference visible."
            );
            ResizePolicyWindow(window, 760f, 640f);
            yield return null;
            PumpPolicyLayout(window);
            yield return null;
            PumpPolicyLayout(window);
            AssertViewportDimensions(window, 760f, 640f);
            Assert.That(
                window.DetailsCard.worldBound.y - window.rootVisualElement.worldBound.y,
                Is.EqualTo(pinnedTop).Within(0.5f),
                "Resizing after filtering should retain the pinned card position."
            );
            AssertCardInsideWindow(window);
            PumpPolicyLayout(window);
            otherTitle.Focus();
            yield return null;
            PumpPolicyLayout(window);
            using (
                KeyDownEvent replace = KeyDownEvent.GetPooled(
                    new Event { type = EventType.KeyDown, keyCode = KeyCode.Return }
                )
            )
            {
                otherTitle.SendEvent(replace);
            }
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(window.DetailsCard.Q<Label>().text, Does.Contain("WUH002"));
            using (
                KeyDownEvent escape = KeyDownEvent.GetPooled(
                    new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape }
                )
            )
            {
                window.rootVisualElement.SendEvent(escape);
            }
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(window.DetailsCard.style.display.value, Is.EqualTo(DisplayStyle.None));
            PumpPolicyLayout(window);
            otherTitle.Focus();
            yield return null;
            PumpPolicyLayout(window);
            using (
                KeyDownEvent enter = KeyDownEvent.GetPooled(
                    new Event { type = EventType.KeyDown, keyCode = KeyCode.Return }
                )
            )
            {
                otherTitle.SendEvent(enter);
            }
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(window.DetailsCard.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Button close = window.DetailsCard.Query<Button>().ToList()[2];
            Assert.That(close.text, Is.EqualTo("×"));
            PumpPolicyLayout(window);
            close.Focus();
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(
                window.rootVisualElement.panel.focusController.focusedElement,
                Is.SameAs(close)
            );
            using (NavigationSubmitEvent dismiss = NavigationSubmitEvent.GetPooled())
            {
                dismiss.target = close;
                close.SendEvent(dismiss);
            }
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(window.DetailsCard.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator UnpinnedExamplesRetainPositionWhenTheirAnchorIsFilteredOut()
        {
            AnalyzerPolicyWindow window = CreatePolicyWindow();
            ResizePolicyWindow(window, 750f, 640f);
            yield return null;
            PumpPolicyLayout(window);
            yield return null;
            PumpPolicyLayout(window);
            AssertViewportDimensions(window, 750f, 640f);
            Button title = window.GetPolicyRow("WUH001").Q<Button>();
            PumpPolicyLayout(window);
            title.Focus();
            yield return null;
            PumpPolicyLayout(window);
            using (
                KeyDownEvent enter = KeyDownEvent.GetPooled(
                    new Event { type = EventType.KeyDown, keyCode = KeyCode.Return }
                )
            )
            {
                title.SendEvent(enter);
            }
            yield return null;
            PumpPolicyLayout(window);
            Button pin = window.DetailsCard.Q<Button>();
            Assert.That(pin.text, Is.EqualTo("Unpin"));
            ToolbarSearchField search = window.rootVisualElement.Q<ToolbarSearchField>();
            search.value = "WUH002";
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(
                window.GetPolicyRow("WUH001").style.display.value,
                Is.EqualTo(DisplayStyle.None)
            );
            float cardTop = window.DetailsCard.worldBound.y - window.rootVisualElement.worldBound.y;
            PumpPolicyLayout(window);
            pin.Focus();
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(
                window.rootVisualElement.panel.focusController.focusedElement,
                Is.SameAs(pin)
            );
            using (NavigationSubmitEvent unpin = NavigationSubmitEvent.GetPooled())
            {
                unpin.target = pin;
                pin.SendEvent(unpin);
            }
            yield return null;
            PumpPolicyLayout(window);
            Assert.That(pin.text, Is.EqualTo("Pin"));
            Assert.That(window.DetailsCard.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(
                window.DetailsCard.worldBound.y - window.rootVisualElement.worldBound.y,
                Is.EqualTo(cardTop).Within(0.5f)
            );
            ResizePolicyWindow(window, 760f, 640f);
            yield return null;
            PumpPolicyLayout(window);
            yield return null;
            PumpPolicyLayout(window);
            AssertViewportDimensions(window, 760f, 640f);
            Assert.That(window.DetailsCard.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(
                window.DetailsCard.worldBound.y - window.rootVisualElement.worldBound.y,
                Is.EqualTo(cardTop).Within(0.5f)
            );
            AssertCardInsideWindow(window);
        }

        [Test]
        public void HeadlessWindowLogHandlingExpectsOnlyKnownErrorsDuringShow()
        {
            const string unrelatedMessage = "Analyzer policy unexpected graphics control error.";
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            ShowWindowWithExpectedGraphicsErrors(
                () =>
                {
                    Debug.LogError(NoGraphicsInitializeMessage);
                    Debug.LogError(NoGraphicsShowMessage);
                    Debug.LogError(NoGraphicsInitializeMessage);
                    LogAssert.NoUnexpectedReceived();
                    Debug.LogError(unrelatedMessage);
                    Assert.Catch<Exception>(() => LogAssert.NoUnexpectedReceived());
                    LogAssert.Expect(LogType.Error, unrelatedMessage);
                    LogAssert.NoUnexpectedReceived();
                },
                headless: true
            );
            Assert.That(LogAssert.ignoreFailingMessages, Is.EqualTo(previousIgnoreFailingMessages));
            Debug.LogError(NoGraphicsInitializeMessage);
            Assert.Catch<Exception>(() => LogAssert.NoUnexpectedReceived());
            LogAssert.Expect(LogType.Error, NoGraphicsInitializeMessage);
            LogAssert.NoUnexpectedReceived();
        }

        private AnalyzerPolicyWindow CreatePolicyWindow()
        {
            AnalyzerPolicyWindow window = Track(
                ScriptableObject.CreateInstance<AnalyzerPolicyWindow>()
            );
            window.RulesetPathOverride = GetRulesetPath();
            window.RefreshState();
            window.BuildUserInterface();
            ShowWindowWithExpectedGraphicsErrors(
                window.Show,
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null
            );
            ResizePolicyWindow(window, 750f, 640f);
            return window;
        }

        private string GetRulesetPath()
        {
            return Path.Combine(_temporaryDirectory, "Default.ruleset");
        }
    }
}
