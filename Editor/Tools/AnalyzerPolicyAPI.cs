// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    /// <summary>Writes Unity Helpers analyzer policies without opening a window.</summary>
    public static class AnalyzerPolicyAPI
    {
        public const string DefaultRulesetAssetPath = "Assets/Default.ruleset";

        internal static readonly AnalyzerPolicy[] Policies =
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
            new(
                "WUH019",
                "Repeated stable loop bound",
                "Reports counting loops that repeatedly read a stable size. This policy is opt-in by default."
            ),
        };

        /// <summary>Enables or disables every Unity Helpers analyzer in the default ruleset.</summary>
        public static bool TrySetEnabled(bool enabled, out string message)
        {
            return TrySetEnabled(DefaultRulesetAssetPath, enabled, out message);
        }

        /// <summary>Enables or disables every Unity Helpers analyzer in an asset ruleset.</summary>
        public static bool TrySetEnabled(string rulesetAssetPath, bool enabled, out string message)
        {
            string normalizedPath = rulesetAssetPath?.Replace('\\', '/');
            if (
                string.IsNullOrWhiteSpace(normalizedPath)
                || !normalizedPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !string.Equals(
                    Path.GetExtension(normalizedPath),
                    ".ruleset",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                message = "A .ruleset asset path under Assets is required.";
                return false;
            }

            string[] segments = normalizedPath.Split('/');
            foreach (string segment in segments)
            {
                if (
                    segment.Length == 0
                    || string.Equals(segment, ".", StringComparison.Ordinal)
                    || string.Equals(segment, "..", StringComparison.Ordinal)
                )
                {
                    message = "The ruleset asset path cannot contain empty or parent segments.";
                    return false;
                }
            }

            string folder;
            try
            {
                folder = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(folder) || !AssetDatabase.IsValidFolder(folder))
                {
                    message = "The ruleset asset folder does not exist.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                message = $"The ruleset asset folder is invalid: {exception.Message}";
                return false;
            }

            string assetsRoot;
            string fullPath;
            try
            {
                assetsRoot = Path.GetFullPath(Application.dataPath);
                fullPath = Path.GetFullPath(
                    Path.Combine(assetsRoot, normalizedPath.Substring("Assets/".Length))
                );
            }
            catch (Exception exception)
            {
                message = $"The ruleset asset path is invalid: {exception.Message}";
                return false;
            }

            if (
                !fullPath.StartsWith(
                    assetsRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                message = "The ruleset asset path must remain under Assets.";
                return false;
            }

            AnalyzerPolicyState state = enabled
                ? AnalyzerPolicyState.Enabled
                : AnalyzerPolicyState.Disabled;
            try
            {
                if (!AnalyzerPolicyRuleset.TryWrite(fullPath, state, Policies, out message))
                {
                    return false;
                }
            }
            catch (Exception exception)
            {
                message = $"The ruleset could not be written: {exception.Message}";
                return false;
            }

            try
            {
                AssetDatabase.ImportAsset(normalizedPath, ImportAssetOptions.ForceUpdate);
                return true;
            }
            catch (Exception exception)
            {
                message = $"The ruleset was written but could not be imported: {exception.Message}";
                return false;
            }
        }
    }
#endif
}
