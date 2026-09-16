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

    internal sealed class AnalyzerPolicyWindow : EditorWindow
    {
        internal const string RulesetAssetPath = "Assets/Default.ruleset";

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
        private static readonly AnalyzerPolicy[] Policies =
        {
            new(
                "WUH001",
                "Lookup factory allocation",
                "Reports method groups that allocate a delegate on every cache lookup."
            ),
            new(
                "WUH002",
                "Nested serialized collection",
                "Reports nested collections that Unity silently fails to serialize."
            ),
            new(
                "WUH003",
                "Unity object null propagation",
                "Reports CLR null propagation that misses destroyed Unity objects."
            ),
            new(
                "WUH004",
                "Unity object null assertion",
                "Reports NUnit null assertions that miss destroyed Unity objects."
            ),
            new(
                "WUH005",
                "UnityEngine.Random",
                "Reports global random state that isolated tests cannot replay."
            ),
            new(
                "WUH006",
                "Discarded effect handle",
                "Reports an EffectHandle that cannot later remove an infinite effect."
            ),
            new(
                "WUH007",
                "Discarded coroutine handle",
                "Reports a coroutine handle that cannot later stop its routine."
            ),
            new(
                "WUH008",
                "Untested Try out value",
                "Reports a Try-pattern out value read without testing success."
            ),
            new(
                "WUH009",
                "Teardown base-call order",
                "Reports teardown work that runs after its base teardown."
            ),
            new(
                "WUH010",
                "Dictionary indexer read",
                "Reports reads that throw when a key is absent. This policy is opt-in by default."
            ),
            new(
                "WUH011",
                "Comparer mutation after use",
                "Reports serialized comparer modes changed after collection construction."
            ),
            new(
                "WUH012",
                "Unchecked serialized row",
                "Reports a serialized row dereferenced without a null test."
            ),
            new(
                "WUH013",
                "Index-only counting loop",
                "Reports counting loops that can be allocation-free foreach loops. This policy is opt-in by default."
            ),
            new(
                "WUH014",
                "Assigning disposable struct",
                "Reports disposable structs whose Dispose mutates a copy."
            ),
            new(
                "WUH015",
                "Invalid Unity callback",
                "Reports Unity lifecycle methods whose signature prevents invocation."
            ),
            new(
                "WUH016",
                "Hidden Unity callback",
                "Reports Unity callbacks that hide an inherited callback."
            ),
            new(
                "WUH017",
                "GetComponent null comparison",
                "Reports GetComponent probes that allocate instead of using TryGetComponent."
            ),
            new(
                "WUH018",
                "Implicit string equality",
                "Reports string equality whose comparison policy is unstated. This policy is opt-in by default."
            ),
        };

        private Vector2 _scrollPosition;
        private AnalyzerPolicyState _state;
        private string _stateMessage;

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Analyzer Policies", priority = -1)]
        internal static void ShowWindow()
        {
            GetWindow<AnalyzerPolicyWindow>("Analyzer Policies");
        }

        internal static IReadOnlyList<AnalyzerPolicy> GetPolicies()
        {
            return Policies;
        }

        private static string GetRulesetPath()
        {
            return Path.GetFullPath(RulesetAssetPath);
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
            EditorGUILayout.HelpBox(_stateMessage, GetMessageType(_state));

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
                    EditorGUILayout.LabelField(policy.HeadingContent, EditorStyles.boldLabel);
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

        private static MessageType GetMessageType(AnalyzerPolicyState state)
        {
            switch (state)
            {
                case AnalyzerPolicyState.Enabled:
                case AnalyzerPolicyState.Disabled:
                    return MessageType.Info;
                case AnalyzerPolicyState.Missing:
                    return MessageType.Warning;
                default:
                    return MessageType.Error;
            }
        }

        private void ApplyState(AnalyzerPolicyState state)
        {
            if (
                !AnalyzerPolicyRuleset.TryWrite(
                    GetRulesetPath(),
                    state,
                    Policies,
                    out string message
                )
            )
            {
                _state = AnalyzerPolicyState.Drifted;
                _stateMessage = message;
                return;
            }

            AssetDatabase.ImportAsset(RulesetAssetPath, ImportAssetOptions.ForceUpdate);
            RefreshState();
        }

        private void RefreshState()
        {
            AnalyzerPolicyRuleset.TryRead(
                GetRulesetPath(),
                Policies,
                out _state,
                out _stateMessage
            );
            Repaint();
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
            foreach (XElement managedGroup in managedGroups)
            {
                managedGroup.Remove();
            }

            root.Add(CreateManagedGroup(root.Name.Namespace, requestedState, policies));
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

            string temporaryPath = path + ".wallstop.tmp";
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
                using (XmlWriter writer = XmlWriter.Create(temporaryPath, settings))
                {
                    document.Save(writer);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
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
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (Exception exception)
                        when (exception is IOException || exception is UnauthorizedAccessException)
                    { }
                }
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
