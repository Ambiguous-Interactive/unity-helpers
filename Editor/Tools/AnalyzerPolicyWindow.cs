// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Xml;
    using System.Xml.Linq;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal sealed class AnalyzerPolicyWindow : EditorWindow
    {
        private const string RulesetFileName = "Default.ruleset";

        private static readonly GUIContent EnableContent = new(
            "Enable All",
            "Set every Unity Helpers analyzer policy to Warning for user code."
        );
        private static readonly GUIContent DisableContent = new(
            "Disable All",
            "Set every Unity Helpers analyzer policy to None for user code."
        );
        private static readonly GUIContent RefreshContent = new(
            "Refresh",
            "Read Assets/Default.ruleset again."
        );
        private static readonly string[] SeverityLabels =
        {
            "Default",
            "Off",
            "Info",
            "Warning",
            "Error",
            "Hidden",
        };
        private static readonly string[] SeverityActions =
        {
            "Default",
            "None",
            "Info",
            "Warning",
            "Error",
            "Hidden",
        };
        private static readonly AnalyzerPolicy[] Policies = AnalyzerPolicyAPI.Policies;
        internal string RulesetPathOverride;

        private Vector2 _scrollPosition;
        private string _stateMessage;
        private readonly Dictionary<string, string> _actions = new(StringComparer.Ordinal);
        private bool _canEdit;
        private double _nextRefreshTime;

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Analyzer Policies", priority = -1)]
        internal static void ShowWindow()
        {
            GetWindow<AnalyzerPolicyWindow>("Analyzer Policies");
        }

        internal static IReadOnlyList<AnalyzerPolicy> GetPolicies()
        {
            return Policies;
        }

        internal static string GetRulesetPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, RulesetFileName));
        }

        internal string GetAction(string id)
        {
            return _actions.TryGetValue(id, out string action) ? action : "Default";
        }

        internal bool TryApplySeverity(string id, string action)
        {
            string path = GetCurrentRulesetPath();
            bool written =
                RulesetPathOverride == null
                    ? AnalyzerPolicyAPI.TrySetSeverity(id, action, out _stateMessage)
                    : AnalyzerPolicyRuleset.TryWriteSeverity(
                        path,
                        id,
                        action,
                        Policies,
                        out _stateMessage
                    );
            if (written)
            {
                RefreshState();
            }
            else
            {
                _canEdit = false;
                Repaint();
            }
            return written;
        }

        internal void OnInspectorUpdate()
        {
            if (_nextRefreshTime <= EditorApplication.timeSinceStartup)
            {
                _nextRefreshTime = EditorApplication.timeSinceStartup + 0.5;
                RefreshState();
            }
        }

        internal void RefreshState()
        {
            _canEdit = AnalyzerPolicyRuleset.TryReadActions(
                GetCurrentRulesetPath(),
                Policies,
                _actions,
                out string actionMessage
            );
            _stateMessage = actionMessage;
            Repaint();
        }

        private void OnEnable()
        {
            RefreshState();
        }

        private void OnFocus()
        {
            RefreshState();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Unity Helpers Analyzer Policies", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_stateMessage, _canEdit ? MessageType.Info : MessageType.Error);
            EditorGUILayout.LabelField("Assets/Default.ruleset — changes are saved immediately.");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(EnableContent))
                {
                    ApplyState(AnalyzerPolicyState.Enabled);
                }

                if (GUILayout.Button(DisableContent))
                {
                    ApplyState(AnalyzerPolicyState.Disabled);
                }

                if (GUILayout.Button(RefreshContent))
                {
                    RefreshState();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Policies", EditorStyles.boldLabel);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            try
            {
                foreach (AnalyzerPolicy policy in Policies)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(policy.HeadingContent, EditorStyles.boldLabel);
                        int selected = 0;
                        string currentAction = GetAction(policy.Id);
                        for (int index = 0; index < SeverityActions.Length; ++index)
                        {
                            if (
                                string.Equals(
                                    SeverityActions[index],
                                    currentAction,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            {
                                selected = index;
                                break;
                            }
                        }
                        using (new EditorGUI.DisabledScope(!_canEdit))
                        {
                            EditorGUI.BeginChangeCheck();
                            int updated = EditorGUILayout.Popup(
                                selected,
                                SeverityLabels,
                                GUILayout.Width(110)
                            );
                            if (
                                EditorGUI.EndChangeCheck()
                                && 0 <= updated
                                && updated < SeverityActions.Length
                            )
                            {
                                TryApplySeverity(policy.Id, SeverityActions[updated]);
                            }
                        }
                    }
                    EditorGUILayout.LabelField(
                        policy.DescriptionContent,
                        EditorStyles.wordWrappedLabel
                    );
                    EditorGUILayout.Space();
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void ApplyState(AnalyzerPolicyState state)
        {
            if (
                !AnalyzerPolicyAPI.TrySetEnabled(
                    state == AnalyzerPolicyState.Enabled,
                    out string message
                )
            )
            {
                _canEdit = false;
                _stateMessage = message;
                return;
            }

            RefreshState();
        }

        private string GetCurrentRulesetPath()
        {
            return RulesetPathOverride ?? GetRulesetPath();
        }
    }

    internal static class AnalyzerPolicyRuleset
    {
        private const string AnalyzerId = "WallstopStudios.UnityHelpers.Analyzers";
        private const string RuleNamespace = "WallstopStudios.UnityHelpers.Analyzers";
        private const string RulesetNamespace =
            "http://schemas.microsoft.com/developer/msbuild/2003";

        internal static bool TryRead(
            string path,
            IReadOnlyList<AnalyzerPolicy> policies,
            out AnalyzerPolicyState state,
            out string message
        )
        {
            if (string.IsNullOrWhiteSpace(path) || policies == null || policies.Count == 0)
            {
                state = AnalyzerPolicyState.Drifted;
                message = "The analyzer policy catalog or ruleset path is invalid.";
                return false;
            }

            if (!File.Exists(path))
            {
                message =
                    "Assets/Default.ruleset is missing. Enable or disable the policies to create it.";
                state = AnalyzerPolicyState.Missing;
                return true;
            }

            if (!TryLoad(path, out XDocument document, out message))
            {
                state = AnalyzerPolicyState.Drifted;
                return false;
            }

            return TryClassify(document, policies, out state, out message);
        }

        internal static bool TryWrite(
            string path,
            AnalyzerPolicyState requestedState,
            IReadOnlyList<AnalyzerPolicy> policies,
            out string message
        )
        {
            if (
                string.IsNullOrWhiteSpace(path)
                || policies == null
                || policies.Count == 0
                || (
                    requestedState != AnalyzerPolicyState.Enabled
                    && requestedState != AnalyzerPolicyState.Disabled
                )
            )
            {
                message = "Only a valid enabled or disabled analyzer policy can be written.";
                return false;
            }

            XDocument document;
            if (File.Exists(path))
            {
                if (!TryLoad(path, out document, out message))
                {
                    return false;
                }
            }
            else
            {
                XNamespace rulesetNamespace = RulesetNamespace;
                document = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    new XElement(
                        rulesetNamespace + "RuleSet",
                        new XAttribute("Name", "Default Rules"),
                        new XAttribute("ToolsVersion", "15.0")
                    )
                );
            }

            XElement root = document.Root;
            if (
                root == null
                || !string.Equals(root.Name.LocalName, "RuleSet", StringComparison.Ordinal)
            )
            {
                message = "The existing ruleset has no valid RuleSet root and was left unchanged.";
                return false;
            }

            List<XElement> managedGroups = FindManagedGroups(root);
            List<XElement> replacedRules = new();
            foreach (XElement managedGroup in managedGroups)
            {
                foreach (XElement rule in managedGroup.Elements())
                {
                    if (
                        string.Equals(rule.Name.LocalName, "Rule", StringComparison.Ordinal)
                        && IsKnownPolicy((string)rule.Attribute("Id"), policies)
                    )
                    {
                        replacedRules.Add(rule);
                    }
                }
            }
            foreach (XElement rule in replacedRules)
            {
                rule.Remove();
            }
            XElement replacement = CreateManagedGroup(
                root.Name.Namespace,
                requestedState,
                policies
            );
            if (managedGroups.Count == 0)
            {
                root.Add(replacement);
            }
            else
            {
                managedGroups[0].Add(replacement.Elements());
            }
            if (!TrySaveAtomically(path, document, out message))
            {
                return false;
            }

            message =
                requestedState == AnalyzerPolicyState.Enabled
                    ? "All Unity Helpers analyzer policies are enabled for user code."
                    : "All Unity Helpers analyzer policies are disabled for user code.";
            return true;
        }

        internal static bool TryReadActions(
            string path,
            IReadOnlyList<AnalyzerPolicy> policies,
            Dictionary<string, string> actions,
            out string message
        )
        {
            actions.Clear();
            if (!File.Exists(path))
            {
                message = "No project overrides. Each analyzer uses its shipped default.";
                return true;
            }
            if (
                !TryLoad(path, out XDocument document, out message)
                || !TryGetRoot(document, out XElement root, out message)
            )
            {
                return false;
            }
            foreach (XElement group in FindManagedGroups(root))
            {
                foreach (XElement rule in group.Elements())
                {
                    if (!string.Equals(rule.Name.LocalName, "Rule", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string id = (string)rule.Attribute("Id");
                    if (!IsKnownPolicy(id, policies))
                    {
                        continue;
                    }
                    string action = (string)rule.Attribute("Action");
                    if (!IsValidAction(action) || actions.ContainsKey(id))
                    {
                        message =
                            id
                            + " has an invalid or duplicate override. Correct the file before editing.";
                        return false;
                    }
                    actions.Add(id, action);
                }
            }
            message =
                "Project severity overrides are synchronized with the ruleset. Default uses the analyzer's shipped setting.";
            return true;
        }

        internal static bool TryWriteSeverity(
            string path,
            string id,
            string action,
            IReadOnlyList<AnalyzerPolicy> policies,
            out string message
        )
        {
            if (
                string.IsNullOrWhiteSpace(path)
                || !IsKnownPolicy(id, policies)
                || !IsValidAction(action)
            )
            {
                message =
                    "A known analyzer, valid ruleset path, and Default, None, Info, Warning, Error, or Hidden action are required.";
                return false;
            }
            string canonicalAction;
            if (string.Equals(action, "None", StringComparison.OrdinalIgnoreCase))
            {
                canonicalAction = "None";
            }
            else if (string.Equals(action, "Info", StringComparison.OrdinalIgnoreCase))
            {
                canonicalAction = "Info";
            }
            else if (string.Equals(action, "Warning", StringComparison.OrdinalIgnoreCase))
            {
                canonicalAction = "Warning";
            }
            else if (string.Equals(action, "Error", StringComparison.OrdinalIgnoreCase))
            {
                canonicalAction = "Error";
            }
            else if (string.Equals(action, "Hidden", StringComparison.OrdinalIgnoreCase))
            {
                canonicalAction = "Hidden";
            }
            else
            {
                canonicalAction = "Default";
            }
            XDocument document;
            if (File.Exists(path))
            {
                if (!TryLoad(path, out document, out message))
                {
                    return false;
                }
            }
            else
            {
                document = new XDocument(
                    new XElement(
                        "RuleSet",
                        new XAttribute("Name", "Default Rules"),
                        new XAttribute("ToolsVersion", "15.0")
                    )
                );
            }
            if (!TryGetRoot(document, out XElement root, out message))
            {
                return false;
            }
            List<XElement> groups = FindManagedGroups(root);
            XElement destination = groups.Count == 0 ? null : groups[0];
            XElement existing = null;
            foreach (XElement group in groups)
            {
                foreach (XElement rule in group.Elements())
                {
                    if (
                        string.Equals(rule.Name.LocalName, "Rule", StringComparison.Ordinal)
                        && string.Equals((string)rule.Attribute("Id"), id, StringComparison.Ordinal)
                    )
                    {
                        if (existing != null)
                        {
                            message = id + " has duplicate overrides and was left unchanged.";
                            return false;
                        }
                        existing = rule;
                    }
                }
            }
            if (existing != null)
            {
                existing.SetAttributeValue("Action", canonicalAction);
            }
            else
            {
                if (destination == null)
                {
                    destination = new XElement(
                        root.Name.Namespace + "Rules",
                        new XAttribute("AnalyzerId", AnalyzerId),
                        new XAttribute("RuleNamespace", RuleNamespace)
                    );
                    root.Add(destination);
                }
                destination.Add(
                    new XElement(
                        root.Name.Namespace + "Rule",
                        new XAttribute("Id", id),
                        new XAttribute("Action", canonicalAction)
                    )
                );
            }
            return TrySaveAtomically(path, document, out message);
        }

        private static bool TryGetRoot(XDocument document, out XElement root, out string message)
        {
            root = document.Root;
            bool valid =
                root != null
                && string.Equals(root.Name.LocalName, "RuleSet", StringComparison.Ordinal);
            message = valid
                ? null
                : "The existing ruleset has no valid RuleSet root and was left unchanged.";
            return valid;
        }

        private static bool IsKnownPolicy(string id, IReadOnlyList<AnalyzerPolicy> policies)
        {
            if (policies != null)
            {
                foreach (AnalyzerPolicy policy in policies)
                {
                    if (string.Equals(policy.Id, id, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsValidAction(string action)
        {
            return string.Equals(action, "Default", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "None", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Info", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Warning", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Error", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Hidden", StringComparison.OrdinalIgnoreCase);
        }

        private static XElement CreateManagedGroup(
            XNamespace rulesetNamespace,
            AnalyzerPolicyState state,
            IReadOnlyList<AnalyzerPolicy> policies
        )
        {
            XElement group = new(
                rulesetNamespace + "Rules",
                new XAttribute("AnalyzerId", AnalyzerId),
                new XAttribute("RuleNamespace", RuleNamespace)
            );
            string action = state == AnalyzerPolicyState.Enabled ? "Warning" : "None";
            for (int index = 0; index < policies.Count; ++index)
            {
                group.Add(
                    new XElement(
                        rulesetNamespace + "Rule",
                        new XAttribute("Id", policies[index].Id),
                        new XAttribute("Action", action)
                    )
                );
            }

            return group;
        }

        private static List<XElement> FindManagedGroups(XElement root)
        {
            List<XElement> groups = new();
            foreach (XElement element in root.Elements())
            {
                if (
                    string.Equals(element.Name.LocalName, "Rules", StringComparison.Ordinal)
                    && string.Equals(
                        (string)element.Attribute("AnalyzerId"),
                        AnalyzerId,
                        StringComparison.Ordinal
                    )
                )
                {
                    groups.Add(element);
                }
            }

            return groups;
        }

        private static bool TryClassify(
            XDocument document,
            IReadOnlyList<AnalyzerPolicy> policies,
            out AnalyzerPolicyState state,
            out string message
        )
        {
            XElement root = document.Root;
            if (
                root == null
                || !string.Equals(root.Name.LocalName, "RuleSet", StringComparison.Ordinal)
            )
            {
                state = AnalyzerPolicyState.Drifted;
                message = "Assets/Default.ruleset has no valid RuleSet root.";
                return false;
            }

            List<XElement> groups = FindManagedGroups(root);
            if (groups.Count != 1)
            {
                message =
                    groups.Count == 0
                        ? "The Unity Helpers analyzer policy block is missing."
                        : "The Unity Helpers analyzer policy block is duplicated.";
                state = AnalyzerPolicyState.Drifted;
                return true;
            }

            Dictionary<string, string> actions = new(StringComparer.Ordinal);
            foreach (XElement rule in groups[0].Elements())
            {
                if (!string.Equals(rule.Name.LocalName, "Rule", StringComparison.Ordinal))
                {
                    continue;
                }

                string id = (string)rule.Attribute("Id");
                string action = (string)rule.Attribute("Action");
                if (
                    string.IsNullOrWhiteSpace(id)
                    || string.IsNullOrWhiteSpace(action)
                    || actions.ContainsKey(id)
                )
                {
                    message =
                        "The Unity Helpers analyzer policy block contains an invalid or duplicate rule.";
                    state = AnalyzerPolicyState.Drifted;
                    return true;
                }

                actions.Add(id, action);
            }

            AnalyzerPolicyState detected = AnalyzerPolicyState.Missing;
            for (int index = 0; index < policies.Count; ++index)
            {
                if (!actions.TryGetValue(policies[index].Id, out string action))
                {
                    message =
                        policies[index].Id + " is missing from the Unity Helpers policy block.";
                    state = AnalyzerPolicyState.Drifted;
                    return true;
                }

                AnalyzerPolicyState ruleState;
                if (string.Equals(action, "Warning", StringComparison.OrdinalIgnoreCase))
                {
                    ruleState = AnalyzerPolicyState.Enabled;
                }
                else if (string.Equals(action, "None", StringComparison.OrdinalIgnoreCase))
                {
                    ruleState = AnalyzerPolicyState.Disabled;
                }
                else
                {
                    state = AnalyzerPolicyState.Drifted;
                    message = policies[index].Id + " has unsupported action '" + action + "'.";
                    return true;
                }

                if (detected == AnalyzerPolicyState.Missing)
                {
                    detected = ruleState;
                }
                else if (detected != ruleState)
                {
                    message =
                        "Unity Helpers analyzer policies are mixed instead of uniformly enabled or disabled.";
                    state = AnalyzerPolicyState.Drifted;
                    return true;
                }
            }

            if (actions.Count != policies.Count)
            {
                state = AnalyzerPolicyState.Drifted;
                message = "The Unity Helpers analyzer policy block contains an unknown rule.";
                return true;
            }

            message =
                detected == AnalyzerPolicyState.Enabled
                    ? "All Unity Helpers analyzer policies are enabled for user code."
                    : "All Unity Helpers analyzer policies are disabled for user code.";
            state = detected;
            return true;
        }

        private static bool TryLoad(string path, out XDocument document, out string message)
        {
            try
            {
                document = XDocument.Load(path, LoadOptions.None);
                message = null;
                return true;
            }
            catch (Exception exception)
                when (exception is IOException
                    || exception is UnauthorizedAccessException
                    || exception is XmlException
                )
            {
                message =
                    "Assets/Default.ruleset could not be read and was left unchanged: "
                    + exception.Message;
                document = null;
                return false;
            }
        }

        private static bool TrySaveAtomically(string path, XDocument document, out string message)
        {
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                message = "The ruleset path has no writable parent directory.";
                return false;
            }

            try
            {
                Directory.CreateDirectory(directory);
                XmlWriterSettings settings = new()
                {
                    Encoding = new UTF8Encoding(false),
                    Indent = true,
                    NewLineChars = "\n",
                    NewLineHandling = NewLineHandling.Replace,
                };
                using MemoryStream output = new();
                using (XmlWriter writer = XmlWriter.Create(output, settings))
                {
                    document.Save(writer);
                }

                if (!DurableFile.TryWriteAllBytes(path, output.ToArray(), out Exception writeError))
                {
                    message =
                        "Assets/Default.ruleset could not be written: "
                        + (writeError != null ? writeError.Message : "Unknown write failure.");
                    return false;
                }

                message = null;
                return true;
            }
            catch (Exception exception)
                when (exception is IOException
                    || exception is UnauthorizedAccessException
                    || exception is XmlException
                )
            {
                message = "Assets/Default.ruleset could not be written: " + exception.Message;
                return false;
            }
        }
    }

    internal enum AnalyzerPolicyState
    {
        Missing,
        Enabled,
        Disabled,
        Drifted,
    }

    internal readonly struct AnalyzerPolicy
    {
        internal readonly string Id;
        internal readonly string Title;
        internal readonly string Description;
        internal readonly GUIContent HeadingContent;
        internal readonly GUIContent DescriptionContent;

        internal AnalyzerPolicy(string id, string title, string description)
        {
            Id = id;
            Title = title;
            Description = description;
            HeadingContent = new GUIContent(id + " — " + title);
            DescriptionContent = new GUIContent(description);
        }
    }
#endif
}
