// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Persists validation profiles and authored rules with the project.</summary>
    [FilePath(
        "ProjectSettings/UnityHelpersValidation.asset",
        FilePathAttribute.Location.ProjectFolder
    )]
    public sealed class ValidationWorkspaceSettings
        : ScriptableSingleton<ValidationWorkspaceSettings>
    {
        internal static event Action Changed;

        private void OnEnable()
        {
            Normalize();
            Undo.undoRedoPerformed += SaveAfterUndo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= SaveAfterUndo;
        }

        internal static readonly string[] Categories =
        {
            "Prefabs",
            "Scenes",
            "ScriptableObjects",
            "Materials",
            "Scripts",
            "Addressables",
            "Settings",
            "Build Profiles",
        };

        internal static readonly string[] Properties =
        {
            "AudioSource.spatialBlend",
            "AudioSource.clip.channels",
            "Rigidbody.mass",
            "Renderer.sharedMaterial",
            "Transform.localScale.y",
            "[Required] fields",
            "Texture.maxSize",
            "Collider.isTrigger",
        };

        internal static readonly string[] Conditions =
        {
            "==",
            "!=",
            ">",
            "<",
            "contains",
            "is null",
            "is missing",
        };
        internal static readonly string[] Fixes =
        {
            "None (report only)",
            "Force mono on import",
            "Remove component",
            "Rename to pattern",
            "Set import max size",
        };

        [SerializeField]
        internal string selectedProfile = "Default";

        [SerializeField]
        internal int frameBudget = 8;

        [SerializeField]
        internal int workerThreads = 4;

        [SerializeField]
        internal List<Profile> profiles = new List<Profile>
        {
            new Profile { name = "Default", triggers = new[] { 0, 0, 0, 0, 0, 0, 2, 2 } },
            new Profile
            {
                name = "Release",
                triggers = new[] { 1, 1, 1, 1, 1, 1, 1, 1 },
                gateBuild = true,
            },
            new Profile
            {
                name = "CI Gate",
                triggers = new[] { 2, 2, 2, 2, 2, 2, 2, 2 },
                gateBuild = true,
                failOn = ValidationSeverity.Warning,
            },
        };

        [SerializeField]
        internal List<RulePreference> rulePreferences = new List<RulePreference>();

        [SerializeField]
        internal List<RuleDefinition> projectRules = new List<RuleDefinition>();

        internal Profile ActiveProfile
        {
            get
            {
                foreach (Profile profile in profiles)
                {
                    if (string.Equals(profile.name, selectedProfile, StringComparison.Ordinal))
                    {
                        return profile;
                    }
                }
                return profiles.Count == 0 ? new Profile() : profiles[0];
            }
        }

        internal void Normalize()
        {
            profiles ??= new List<Profile>();
            profiles.RemoveAll(profile => profile == null);
            if (profiles.Count == 0)
                profiles.Add(new Profile());
            foreach (Profile profile in profiles)
            {
                int[] normalized = new int[Categories.Length];
                for (int index = 0; index < normalized.Length; index++)
                    normalized[index] =
                        profile.triggers != null && index < profile.triggers.Length
                            ? Math.Max(0, Math.Min(2, profile.triggers[index]))
                            : 2;
                profile.triggers = normalized;
                if (
                    profile.failOn != ValidationSeverity.Warning
                    && profile.failOn != ValidationSeverity.Error
                )
                    profile.failOn = ValidationSeverity.Error;
            }
            rulePreferences ??= new List<RulePreference>();
            rulePreferences.RemoveAll(preference => preference == null);
            projectRules ??= new List<RuleDefinition>();
            projectRules.RemoveAll(rule => rule == null);
            frameBudget = Math.Max(1, Math.Min(100, frameBudget));
            workerThreads = Math.Max(1, Math.Min(32, workerThreads));
        }

        internal int TriggerFor(string path)
        {
            int index = Array.IndexOf(Categories, CategoryFor(path));
            int[] triggers = ActiveProfile.triggers;
            return index < 0 || triggers == null || triggers.Length <= index
                ? 2
                : Math.Max(0, Math.Min(2, triggers[index]));
        }

        internal RulePreference PreferenceFor(string ruleId)
        {
            foreach (RulePreference preference in rulePreferences)
            {
                if (string.Equals(preference.ruleId, ruleId, StringComparison.Ordinal))
                {
                    return preference;
                }
            }
            return null;
        }

        internal bool IsEnabled(string ruleId)
        {
            RulePreference preference = PreferenceFor(ruleId);
            return preference == null || preference.enabled;
        }

        internal ValidationSeverity SeverityFor(string ruleId, ValidationSeverity original)
        {
            RulePreference preference = PreferenceFor(ruleId);
            return preference != null && preference.overrideSeverity
                ? preference.severity
                : original;
        }

        internal void SetRulePreference(
            string ruleId,
            bool enabled,
            bool overrideSeverity,
            ValidationSeverity severity
        )
        {
            Change(
                "Configure validation rule",
                () =>
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
            );
        }

        internal void Change(string operation, Action mutation)
        {
            if (mutation == null)
            {
                return;
            }
            Undo.RecordObject(this, operation);
            mutation();
            Normalize();
            Save(true);
            Changed?.Invoke();
        }

        internal void SaveAfterUndo()
        {
            Normalize();
            Save(true);
            Changed?.Invoke();
        }

        internal static string CategoryFor(string path)
        {
            string normalized = (path ?? string.Empty).Replace('\\', '/');
            if (normalized.IndexOf("/BuildProfiles/", StringComparison.OrdinalIgnoreCase) != -1)
            {
                return "Build Profiles";
            }
            if (normalized.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase))
            {
                return "Settings";
            }
            if (
                normalized.IndexOf("/AddressableAssetsData/", StringComparison.OrdinalIgnoreCase)
                != -1
            )
            {
                return "Addressables";
            }
            string extension = System.IO.Path.GetExtension(normalized);
            if (string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase))
                return "Prefabs";
            if (string.Equals(extension, ".unity", StringComparison.OrdinalIgnoreCase))
                return "Scenes";
            if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
                return "Scripts";
            if (string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase))
                return "ScriptableObjects";
            return "Materials";
        }

        [Serializable]
        internal sealed class Profile
        {
            public string name = "Default";
            public int[] triggers = new int[8];
            public bool gateBuild;
            public ValidationSeverity failOn = ValidationSeverity.Error;
        }

        [Serializable]
        internal sealed class RulePreference
        {
            public string ruleId;
            public bool enabled = true;
            public bool overrideSeverity;
            public ValidationSeverity severity = ValidationSeverity.Warning;
        }

        [Serializable]
        internal sealed class RuleDefinition
        {
            public string id;
            public string name = "Mono audio for 3D sources";
            public string target = "Prefabs";
            public string pathFilter = "Assets/Prefabs";
            public List<RuleCondition> checks = new List<RuleCondition>
            {
                new RuleCondition
                {
                    property = "AudioSource.spatialBlend",
                    comparison = ">",
                    value = "0.5",
                },
                new RuleCondition
                {
                    property = "AudioSource.clip.channels",
                    comparison = ">",
                    value = "1",
                },
            };
            public ValidationSeverity severity = ValidationSeverity.Warning;
            public string message = "3D AudioSource plays a stereo clip";
            public string fix = "Force mono on import";
            public string fixValue = "1024";
        }

        [Serializable]
        internal sealed class RuleCondition
        {
            public Vector2 graphPosition;
            public string property = "Rigidbody.mass";
            public string comparison = ">";
            public string value = "100";
        }
    }
#endif
}
