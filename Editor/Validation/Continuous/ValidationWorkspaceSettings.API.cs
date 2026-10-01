// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEditorInternal;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using UnityEngine;

    public sealed partial class ValidationWorkspaceSettings
    {
        /// <summary>Gets the selected profile name.</summary>
        public string SelectedProfileName
        {
            get { return selectedProfile; }
        }

        /// <summary>Gets the validation frame budget in milliseconds.</summary>
        public int FrameBudgetMilliseconds
        {
            get { return frameBudget; }
        }

        private bool _persistUndoWhileDisabled;

        /// <summary>Gets a detached copy of supported category names in trigger order.</summary>
        public static string[] GetCategories()
        {
            return (string[])Categories.Clone();
        }

        private static bool IsSeverity(ValidationSeverity severity)
        {
            return severity == ValidationSeverity.Info
                || severity == ValidationSeverity.Warning
                || severity == ValidationSeverity.Error;
        }

        private static RuleDefinition Clone(RuleDefinition source)
        {
            if (source == null)
            {
                return null;
            }
            RuleDefinition copy = new RuleDefinition
            {
                id = source.id,
                name = source.name,
                target = source.target,
                pathFilter = source.pathFilter,
                severity = source.severity,
                message = source.message,
                fix = source.fix,
                fixValue = source.fixValue,
            };
            if (source.checks == null)
            {
                copy.checks = null;
                return copy;
            }
            copy.checks.Clear();
            foreach (RuleCondition condition in source.checks)
            {
                if (condition == null)
                {
                    copy.checks.Add(null);
                    continue;
                }
                copy.checks.Add(
                    new RuleCondition
                    {
                        graphPosition = condition.graphPosition,
                        property = condition.property,
                        comparison = condition.comparison,
                        value = condition.value,
                    }
                );
            }
            return copy;
        }

        private static Profile Clone(Profile source)
        {
            if (source == null)
            {
                return null;
            }
            return new Profile
            {
                name = source.name,
                triggers = source.triggers == null ? null : (int[])source.triggers.Clone(),
                gateBuild = source.gateBuild,
                failOn = source.failOn,
            };
        }

        private static RulePreference Clone(RulePreference source)
        {
            return source == null
                ? null
                : new RulePreference
                {
                    ruleId = source.ruleId,
                    enabled = source.enabled,
                    overrideSeverity = source.overrideSeverity,
                    severity = source.severity,
                };
        }

        private static void NotifyChanged()
        {
            if (Changed == null)
            {
                return;
            }
            foreach (Delegate subscriber in Changed.GetInvocationList())
            {
                try
                {
                    ((Action)subscriber)();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        /// <summary>Gets detached copies of authored rules.</summary>
        public RuleDefinition[] GetAuthoredRules()
        {
            RuleDefinition[] copies = new RuleDefinition[projectRules.Count];
            int count = copies.Length;
            for (int index = 0; index < count; index++)
            {
                copies[index] = Clone(projectRules[index]);
            }
            return copies;
        }

        /// <summary>Gets detached copies of profile configurations.</summary>
        public Profile[] GetProfiles()
        {
            Profile[] copies = new Profile[profiles.Count];
            int count = copies.Length;
            for (int index = 0; index < count; index++)
            {
                copies[index] = Clone(profiles[index]);
            }
            return copies;
        }

        /// <summary>Gets a detached preference, or null when the rule uses defaults.</summary>
        public RulePreference GetRulePreference(string ruleId)
        {
            return Clone(PreferenceFor(ruleId));
        }

        /// <summary>Creates and persists an isolated authored rule with a new stable identifier.</summary>
        public bool TryCreateAuthoredRule(
            RuleDefinition definition,
            out string ruleId,
            out string error
        )
        {
            if (!ValidationProjectRule.ValidateDefinition(definition, out string validationError))
            {
                ruleId = null;
                error = validationError;
                return false;
            }
            if (!IsSeverity(definition.severity))
            {
                ruleId = null;
                error = "Choose Info, Warning, or Error severity.";
                return false;
            }
            string path = definition.pathFilter;
            if (
                !string.IsNullOrEmpty(path)
                && (
                    path[0] == '/'
                    || path[0] == '\\'
                    || 0 <= path.IndexOf(':')
                    || 0 <= path.IndexOf('\0')
                    || 0 <= Array.IndexOf(path.Replace('\\', '/').Split('/'), "..")
                )
            )
            {
                error =
                    "The path filter must be a project-relative folder without parent traversal.";
                ruleId = null;
                return false;
            }
            RuleDefinition copy = Clone(definition);
            copy.id = "project." + Guid.NewGuid().ToString("N");
            if (!TryChange("Create validation rule", () => projectRules.Add(copy), out error))
            {
                ruleId = null;
                return false;
            }
            ruleId = copy.id;
            return true;
        }

        /// <summary>Deletes an authored rule and disables its stored findings in one undoable operation.</summary>
        public bool TryDeleteAuthoredRule(string ruleId, out string error)
        {
            RuleDefinition found = null;
            foreach (RuleDefinition definition in projectRules)
            {
                if (string.Equals(definition.id, ruleId, StringComparison.Ordinal))
                {
                    found = definition;
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(ruleId) || found == null)
            {
                error = "Choose an existing authored rule identifier.";
                return false;
            }
            return TryChange(
                "Delete validation rule",
                () =>
                {
                    projectRules.Remove(found);
                    SetPreferenceValues(ruleId, false, false, ValidationSeverity.Warning);
                },
                out error
            );
        }

        /// <summary>Persists a rule preference without requiring a window or registered rule.</summary>
        public bool TrySetRulePreference(
            string ruleId,
            bool enabled,
            bool overrideSeverity,
            ValidationSeverity severity,
            out string error
        )
        {
            if (string.IsNullOrWhiteSpace(ruleId))
            {
                error = "A rule identifier is required.";
                return false;
            }
            if (!IsSeverity(severity))
            {
                error = "Choose Info, Warning, or Error severity.";
                return false;
            }
            return TryChange(
                "Configure validation rule",
                () => SetPreferenceValues(ruleId, enabled, overrideSeverity, severity),
                out error
            );
        }

        /// <summary>Selects an existing profile by its exact name.</summary>
        public bool TrySelectProfile(string profileName, out string error)
        {
            if (FindProfile(profileName) == null)
            {
                error = "Choose an existing profile name.";
                return false;
            }
            return TryChange(
                "Select validation profile",
                () => selectedProfile = profileName,
                out error
            );
        }

        /// <summary>Replaces an existing profile configuration with an isolated copy.</summary>
        public bool TryConfigureProfile(Profile configuration, out string error)
        {
            Profile profile = configuration == null ? null : FindProfile(configuration.name);
            if (profile == null)
            {
                error = "Choose an existing profile name.";
                return false;
            }
            if (
                configuration.triggers == null
                || configuration.triggers.Length != Categories.Length
            )
            {
                error = "Supply one trigger for every supported category.";
                return false;
            }
            foreach (int trigger in configuration.triggers)
            {
                if (trigger < 0 || 2 < trigger)
                {
                    error = "Triggers must be change (0), save (1), or manual (2).";
                    return false;
                }
            }
            if (
                configuration.failOn != ValidationSeverity.Warning
                && configuration.failOn != ValidationSeverity.Error
            )
            {
                error = "Build failure severity must be Warning or Error.";
                return false;
            }
            Profile copy = Clone(configuration);
            return TryChange(
                "Configure validation profile",
                () =>
                {
                    profile.triggers = copy.triggers;
                    profile.gateBuild = copy.gateBuild;
                    profile.failOn = copy.failOn;
                },
                out error
            );
        }

        /// <summary>Sets one category trigger on an existing profile.</summary>
        public bool TrySetProfileTrigger(
            string profileName,
            string category,
            int trigger,
            out string error
        )
        {
            Profile profile = FindProfile(profileName);
            int index = Array.IndexOf(Categories, category);
            if (profile == null || index < 0 || trigger < 0 || 2 < trigger)
            {
                error = "Choose an existing profile, supported category, and trigger from 0 to 2.";
                return false;
            }
            if (profile.triggers == null || profile.triggers.Length != Categories.Length)
            {
                error = "The stored profile has an invalid trigger matrix.";
                return false;
            }
            Profile copy = Clone(profile);
            copy.triggers[index] = trigger;
            return TryConfigureProfile(copy, out error);
        }

        /// <summary>Sets the build gate and failure threshold of an existing profile.</summary>
        public bool TrySetProfileBuildGate(
            string profileName,
            bool gateBuild,
            ValidationSeverity failOn,
            out string error
        )
        {
            Profile profile = FindProfile(profileName);
            if (profile == null)
            {
                error = "Choose an existing profile name.";
                return false;
            }
            Profile copy = Clone(profile);
            copy.gateBuild = gateBuild;
            copy.failOn = failOn;
            return TryConfigureProfile(copy, out error);
        }

        /// <summary>Persists a frame budget from 1 through 100 milliseconds.</summary>
        public bool TrySetFrameBudget(int milliseconds, out string error)
        {
            if (milliseconds < 1 || 100 < milliseconds)
            {
                error = "The frame budget must be from 1 through 100 milliseconds.";
                return false;
            }
            return TryChange(
                "Set validation frame budget",
                () => frameBudget = milliseconds,
                out error
            );
        }

        private Profile FindProfile(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return null;
            }
            foreach (Profile profile in profiles)
            {
                if (string.Equals(profile.name, profileName, StringComparison.Ordinal))
                {
                    return profile;
                }
            }
            return null;
        }

        private void SetPreferenceValues(
            string ruleId,
            bool enabled,
            bool overrideSeverity,
            ValidationSeverity severity
        )
        {
            RulePreference preference = PreferenceFor(ruleId);
            if (preference == null)
            {
                preference = new RulePreference { ruleId = ruleId };
                rulePreferences.Add(preference);
            }
            preference.enabled = enabled;
            preference.overrideSeverity = overrideSeverity;
            preference.severity = severity;
        }

        private bool TryPersist(out string error)
        {
            string stagedPath =
                "ProjectSettings/.UnityHelpersValidation." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                InternalEditorUtility.SaveToSerializedFileAndForget(
                    new UnityEngine.Object[] { this },
                    stagedPath,
                    true
                );
                byte[] serialized = File.ReadAllBytes(stagedPath);
                if (
                    !DurableFile.TryWriteAllBytes(
                        "ProjectSettings/UnityHelpersValidation.asset",
                        serialized,
                        out Exception failure
                    )
                )
                {
                    error = failure?.Message ?? "Could not persist validation workspace settings.";
                    return false;
                }
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                try
                {
                    File.Delete(stagedPath);
                }
                catch (Exception) { }
            }
        }

        private bool TryChange(string operation, Action mutation, out string error)
        {
            string before = EditorJsonUtility.ToJson(this);
            try
            {
                Undo.RecordObject(this, operation);
                mutation();
                if (!TryPersist(out string persistError))
                {
                    EditorJsonUtility.FromJsonOverwrite(before, this);
                    error = persistError;
                    return false;
                }
                _persistUndoWhileDisabled = true;
                UpdateUndoSubscription();
            }
            catch (Exception exception)
            {
                EditorJsonUtility.FromJsonOverwrite(before, this);
                error = exception.Message;
                return false;
            }
            NotifyChanged();
            error = null;
            return true;
        }
    }
#endif
}
