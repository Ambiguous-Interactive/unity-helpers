// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;

    [TestFixture]
    public sealed class ValidationWorkspaceAPITests
    {
        private const string SettingsPath = "ProjectSettings/UnityHelpersValidation.asset";
        private ValidationWorkspaceSettings _settings;
        private string _originalJson;
        private byte[] _originalFile;
        private bool _enabled;
        private int _undoGroup;

        [SetUp]
        public void PreserveWorkspace()
        {
            _settings = ValidationWorkspaceSettings.instance;
            _originalJson = EditorJsonUtility.ToJson(_settings);
            _originalFile = File.Exists(SettingsPath) ? File.ReadAllBytes(SettingsPath) : null;
            _enabled = ValidationPreferences.Enabled;
            ValidationPreferences.Enabled = true;
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            _settings.selectedProfile = "API A";
            _settings.frameBudget = 8;
            _settings.profiles = new List<ValidationWorkspaceSettings.Profile>
            {
                new ValidationWorkspaceSettings.Profile { name = "API A" },
                new ValidationWorkspaceSettings.Profile { name = "API B" },
            };
            _settings.projectRules = new List<ValidationWorkspaceSettings.RuleDefinition>();
            _settings.rulePreferences = new List<ValidationWorkspaceSettings.RulePreference>();
            _settings.SaveAfterUndo();
        }

        [TearDown]
        public void RestoreWorkspace()
        {
            try
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(_undoGroup);
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(_originalJson, _settings);
                ValidationPreferences.Enabled = _enabled;
                if (_originalFile == null)
                {
                    File.Delete(SettingsPath);
                }
                else
                {
                    File.WriteAllBytes(SettingsPath, _originalFile);
                }
            }
        }

        [Test]
        public void RuleInputsAndSnapshotsAreIsolatedAndIdsRemainStable()
        {
            ValidationWorkspaceSettings.RuleDefinition input =
                new ValidationWorkspaceSettings.RuleDefinition
                {
                    id = "caller.id",
                    name = "API isolated rule",
                };
            Assert.IsTrue(
                _settings.TryCreateAuthoredRule(input, out string id, out string error),
                error
            );
            StringAssert.StartsWith("project.", id);
            Assert.AreEqual("caller.id", input.id);
            string persisted = File.ReadAllText(SettingsPath);
            StringAssert.Contains(id, persisted);
            StringAssert.Contains("API isolated rule", persisted);
            input.name = "Changed caller";
            input.checks[0].value = "900";
            ValidationWorkspaceSettings.RuleDefinition[] snapshot = _settings.GetAuthoredRules();
            Assert.AreEqual("API isolated rule", snapshot[0].name);
            Assert.AreEqual("0.5", snapshot[0].checks[0].value);
            snapshot[0].id = "Changed snapshot";
            snapshot[0].checks.Clear();
            Assert.AreEqual(id, _settings.GetAuthoredRules()[0].id);
            Assert.AreEqual(2, _settings.GetAuthoredRules()[0].checks.Count);
            Assert.AreEqual(persisted, File.ReadAllText(SettingsPath));
            Assert.IsTrue(
                _settings.TryCreateAuthoredRule(input, out string secondId, out error),
                error
            );
            Assert.AreNotEqual(id, secondId);
        }

        [Test]
        public void ProfileInputsAndReadSnapshotsAreIsolated()
        {
            ValidationWorkspaceSettings.Profile input = _settings.GetProfiles()[1];
            input.triggers[0] = 2;
            input.gateBuild = true;
            input.failOn = ValidationSeverity.Warning;
            Assert.IsTrue(_settings.TryConfigureProfile(input, out string error), error);
            string persisted = File.ReadAllText(SettingsPath);
            input.triggers[0] = 1;
            input.gateBuild = false;
            ValidationWorkspaceSettings.Profile[] snapshot = _settings.GetProfiles();
            Assert.AreEqual(2, snapshot[1].triggers[0]);
            Assert.IsTrue(snapshot[1].gateBuild);
            Assert.AreEqual(ValidationSeverity.Warning, snapshot[1].failOn);
            snapshot[1].triggers[0] = 0;
            Assert.AreEqual(2, _settings.GetProfiles()[1].triggers[0]);
            string[] categories = ValidationWorkspaceSettings.GetCategories();
            categories[0] = "Changed";
            Assert.AreEqual("Prefabs", ValidationWorkspaceSettings.GetCategories()[0]);
            Assert.AreEqual(persisted, File.ReadAllText(SettingsPath));
        }

        [TestCase(false, TestName = "Workspace.NullConditionList.SnapshotIsolated")]
        [TestCase(true, TestName = "Workspace.NullConditionElement.SnapshotIsolated")]
        public void MalformedStoredRulesCanBeReadWithoutChangingWorkspace(bool nullElement)
        {
            ValidationWorkspaceSettings.RuleDefinition rule =
                new ValidationWorkspaceSettings.RuleDefinition();
            if (nullElement)
            {
                rule.checks[0] = null;
            }
            else
            {
                rule.checks = null;
            }
            _settings.projectRules.Add(rule);
            byte[] beforeFile = File.ReadAllBytes(SettingsPath);
            ValidationWorkspaceSettings.RuleDefinition[] snapshot = _settings.GetAuthoredRules();
            Assert.IsFalse(_settings.TryCreateAuthoredRule(rule, out string id, out string error));
            Assert.IsTrue(id == null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            string before = EditorJsonUtility.ToJson(_settings);
            Assert.AreEqual(1, snapshot.Length);
            if (nullElement)
            {
                Assert.IsTrue(snapshot[0].checks[0] == null);
                snapshot[0].checks.Clear();
            }
            else
            {
                Assert.IsTrue(snapshot[0].checks == null);
                snapshot[0].checks = new List<ValidationWorkspaceSettings.RuleCondition>();
            }
            Assert.AreEqual(before, EditorJsonUtility.ToJson(_settings));
            CollectionAssert.AreEqual(beforeFile, File.ReadAllBytes(SettingsPath));
        }

        [TestCase(0, TestName = "Workspace.CreateRule.UndoRedo")]
        [TestCase(1, TestName = "Workspace.DeleteRule.UndoRedo")]
        [TestCase(2, TestName = "Workspace.RulePreference.UndoRedo")]
        [TestCase(3, TestName = "Workspace.SelectProfile.UndoRedo")]
        [TestCase(4, TestName = "Workspace.ConfigureProfile.UndoRedo")]
        [TestCase(5, TestName = "Workspace.FrameBudget.UndoRedo")]
        [TestCase(6, TestName = "Workspace.ProfileTrigger.UndoRedo")]
        [TestCase(7, TestName = "Workspace.BuildGate.UndoRedo")]
        public void MutationsPersistAndUndoRedoRestoresSettings(int operation)
        {
            Assert.IsTrue(
                _settings.TryCreateAuthoredRule(
                    new ValidationWorkspaceSettings.RuleDefinition(),
                    out string id,
                    out string error
                ),
                error
            );
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            string before = EditorJsonUtility.ToJson(_settings);
            string beforeFile = File.ReadAllText(SettingsPath);
            ValidationWorkspaceSettings.Profile profile = _settings.GetProfiles()[1];
            profile.triggers[0] = 2;
            profile.gateBuild = true;
            profile.failOn = ValidationSeverity.Warning;
            bool success;
            switch (operation)
            {
                case 0:
                    success = _settings.TryCreateAuthoredRule(
                        new ValidationWorkspaceSettings.RuleDefinition(),
                        out _,
                        out error
                    );
                    break;
                case 1:
                    success = _settings.TryDeleteAuthoredRule(id, out error);
                    break;
                case 2:
                    success = _settings.TrySetRulePreference(
                        id,
                        false,
                        true,
                        ValidationSeverity.Error,
                        out error
                    );
                    break;
                case 3:
                    success = _settings.TrySelectProfile("API B", out error);
                    break;
                case 4:
                    success = _settings.TryConfigureProfile(profile, out error);
                    break;
                case 5:
                    success = _settings.TrySetFrameBudget(17, out error);
                    break;
                case 6:
                    success = _settings.TrySetProfileTrigger("API B", "Prefabs", 1, out error);
                    break;
                default:
                    success = _settings.TrySetProfileBuildGate(
                        "API B",
                        true,
                        ValidationSeverity.Warning,
                        out error
                    );
                    break;
            }
            Assert.IsTrue(success, error);
            string after = EditorJsonUtility.ToJson(_settings);
            string afterFile = File.ReadAllText(SettingsPath);
            Assert.AreNotEqual(before, after);
            Assert.AreNotEqual(beforeFile, afterFile);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.AreEqual(before, EditorJsonUtility.ToJson(_settings));
            Assert.AreEqual(beforeFile, File.ReadAllText(SettingsPath));
            Undo.PerformRedo();
            Assert.AreEqual(after, EditorJsonUtility.ToJson(_settings));
            Assert.AreEqual(afterFile, File.ReadAllText(SettingsPath));
            if (operation == 1)
            {
                Assert.AreEqual(0, _settings.GetAuthoredRules().Length);
                Assert.IsFalse(_settings.GetRulePreference(id).enabled);
            }
        }

        [Test]
        public void DisabledSentinelStillPersistsApiUndoAndRedo()
        {
            ValidationPreferences.Enabled = false;
            string beforeFile = File.ReadAllText(SettingsPath);
            Assert.IsTrue(_settings.TrySetFrameBudget(100, out string error), error);
            string afterFile = File.ReadAllText(SettingsPath);
            Assert.AreEqual(100, _settings.FrameBudgetMilliseconds);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.AreEqual(8, _settings.FrameBudgetMilliseconds);
            Assert.AreEqual(beforeFile, File.ReadAllText(SettingsPath));
            Undo.PerformRedo();
            Assert.AreEqual(100, _settings.FrameBudgetMilliseconds);
            Assert.AreEqual(afterFile, File.ReadAllText(SettingsPath));
        }

        [Test]
        public void PersistenceFailureRestoresMemoryAndDoesNotAddAnUndoStep()
        {
            Assert.IsTrue(_settings.TrySetFrameBudget(12, out string error), error);
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            string before = EditorJsonUtility.ToJson(_settings);
            byte[] persisted = File.ReadAllBytes(SettingsPath);
            File.Delete(SettingsPath);
            Directory.CreateDirectory(SettingsPath);
            try
            {
                Assert.IsFalse(_settings.TrySetFrameBudget(20, out error));
                Assert.IsFalse(string.IsNullOrWhiteSpace(error));
                Assert.AreEqual(before, EditorJsonUtility.ToJson(_settings));
                Assert.IsTrue(Directory.Exists(SettingsPath));
                Assert.AreEqual(0, Directory.GetFileSystemEntries(SettingsPath).Length);
            }
            finally
            {
                Directory.Delete(SettingsPath);
                File.WriteAllBytes(SettingsPath, persisted);
            }
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.AreEqual(8, _settings.FrameBudgetMilliseconds);
            StringAssert.Contains("frameBudget: 8", File.ReadAllText(SettingsPath));
        }

        [TestCase(0, TestName = "Workspace.NullRule.Rejected")]
        [TestCase(1, TestName = "Workspace.InvalidRuleSeverity.Rejected")]
        [TestCase(2, TestName = "Workspace.InvalidRulePath.Rejected")]
        [TestCase(3, TestName = "Workspace.MissingDeleteId.Rejected")]
        [TestCase(4, TestName = "Workspace.EmptyPreferenceId.Rejected")]
        [TestCase(5, TestName = "Workspace.InvalidPreferenceSeverity.Rejected")]
        [TestCase(6, TestName = "Workspace.MissingProfile.Rejected")]
        [TestCase(7, TestName = "Workspace.NullProfile.Rejected")]
        [TestCase(8, TestName = "Workspace.ShortTriggers.Rejected")]
        [TestCase(9, TestName = "Workspace.InvalidTrigger.Rejected")]
        [TestCase(10, TestName = "Workspace.InvalidBuildSeverity.Rejected")]
        [TestCase(11, TestName = "Workspace.LowBudget.Rejected")]
        [TestCase(12, TestName = "Workspace.HighBudget.Rejected")]
        [TestCase(13, TestName = "Workspace.UnsupportedCondition.Rejected")]
        public void InvalidInputsReturnDiagnosticsWithoutChangingMemoryOrFile(int operation)
        {
            string before = EditorJsonUtility.ToJson(_settings);
            byte[] beforeFile = File.ReadAllBytes(SettingsPath);
            ValidationWorkspaceSettings.RuleDefinition rule =
                new ValidationWorkspaceSettings.RuleDefinition();
            ValidationWorkspaceSettings.Profile profile = _settings.GetProfiles()[0];
            bool success;
            string error;
            switch (operation)
            {
                case 0:
                    success = _settings.TryCreateAuthoredRule(null, out _, out error);
                    break;
                case 1:
                    rule.severity = (ValidationSeverity)99;
                    success = _settings.TryCreateAuthoredRule(rule, out _, out error);
                    break;
                case 2:
                    rule.pathFilter = "Assets/../Outside";
                    success = _settings.TryCreateAuthoredRule(rule, out _, out error);
                    break;
                case 3:
                    success = _settings.TryDeleteAuthoredRule("missing", out error);
                    break;
                case 4:
                    success = _settings.TrySetRulePreference(
                        " ",
                        true,
                        false,
                        ValidationSeverity.Info,
                        out error
                    );
                    break;
                case 5:
                    success = _settings.TrySetRulePreference(
                        "rule",
                        true,
                        true,
                        (ValidationSeverity)99,
                        out error
                    );
                    break;
                case 6:
                    success = _settings.TrySelectProfile("missing", out error);
                    break;
                case 7:
                    success = _settings.TryConfigureProfile(null, out error);
                    break;
                case 8:
                    profile.triggers = new int[1];
                    success = _settings.TryConfigureProfile(profile, out error);
                    break;
                case 9:
                    success = _settings.TrySetProfileTrigger("API A", "Prefabs", 3, out error);
                    break;
                case 10:
                    success = _settings.TrySetProfileBuildGate(
                        "API A",
                        true,
                        ValidationSeverity.Info,
                        out error
                    );
                    break;
                case 11:
                    success = _settings.TrySetFrameBudget(0, out error);
                    break;
                case 12:
                    success = _settings.TrySetFrameBudget(101, out error);
                    break;
                default:
                    rule.checks[0].property = "Unsupported";
                    success = _settings.TryCreateAuthoredRule(rule, out _, out error);
                    break;
            }
            Assert.IsFalse(success);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            Assert.AreEqual(before, EditorJsonUtility.ToJson(_settings));
            CollectionAssert.AreEqual(beforeFile, File.ReadAllBytes(SettingsPath));
        }
    }
}
