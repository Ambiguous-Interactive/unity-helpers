// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEditor.Compilation;
    using UnityEngine;
    using Assembly = UnityEditor.Compilation.Assembly;

    /// <summary>
    /// Holds every concrete component and asset type to the two rules that keep it authorable.
    /// </summary>
    /// <remarks>
    /// Rule one is the symptom -- every concrete type resolves to a <c>MonoScript</c>. Rule two is
    /// the cause and what keeps rule one true -- every script asset is named after the type it
    /// binds. Nested and open-generic types are deliberately not excluded. See
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/docs/features/editor-tools/authored-asset-validation.md">Authored Asset Validation</see>.
    /// </remarks>
    public static class MonoScriptBindingValidator
    {
        /// <summary>
        /// Reports every type and script asset under <paramref name="assetPathPrefixes"/> that
        /// breaks either rule.
        /// </summary>
        /// <param name="assetPathPrefixes">Asset path prefixes to scope the scan to, such as <c>Assets/</c>.</param>
        /// <param name="findings">Receives one entry per violation.</param>
        /// <param name="typesConsidered">Receives how many concrete types rule one judged.</param>
        /// <param name="scriptsConsidered">Receives how many script assets rule two judged.</param>
        /// <returns><c>false</c> when the scan could not run at all.</returns>
        /// <remarks>
        /// The two counts are outputs rather than diagnostics: a scan whose scope stops matching
        /// reports zero findings, and zero findings is exactly what a passing scan reports. A caller
        /// that asserts the counts are non-zero cannot be made green by a broken subject list.
        /// </remarks>
        public static bool TryScan(
            IReadOnlyList<string> assetPathPrefixes,
            List<MonoScriptBindingFinding> findings,
            out int typesConsidered,
            out int scriptsConsidered
        )
        {
            return TryScan(
                assetPathPrefixes,
                findings,
                new List<string>(),
                out typesConsidered,
                out scriptsConsidered
            );
        }

        /// <summary>
        /// Reports every authorable type in scope that cannot be authored onto anything.
        /// </summary>
        /// <param name="assetPathPrefixes">Asset path prefixes to scope the scan to, such as <c>Assets/</c>.</param>
        /// <param name="findings">Receives one entry per violation.</param>
        /// <param name="unreadable">Receives the script paths the database named but would not load.</param>
        /// <param name="typesConsidered">Receives how many concrete types rule one judged.</param>
        /// <param name="scriptsConsidered">Receives how many script assets rule two judged.</param>
        /// <returns><c>false</c> when the scan could not run at all.</returns>
        public static bool TryScan(
            IReadOnlyList<string> assetPathPrefixes,
            List<MonoScriptBindingFinding> findings,
            List<string> unreadable,
            out int typesConsidered,
            out int scriptsConsidered
        )
        {
            if (
                findings == null
                || unreadable == null
                || assetPathPrefixes == null
                || assetPathPrefixes.Count <= 0
            )
            {
                typesConsidered = 0;
                scriptsConsidered = 0;
                return false;
            }

            int types = 0;
            int scripts = 0;
            findings.Clear();
            unreadable.Clear();
            HashSet<string> scopedAssemblies = ScopedAssemblyNames(assetPathPrefixes);

            foreach (Type type in ConcreteAuthorableTypes())
            {
                if (!scopedAssemblies.Contains(type.Assembly.GetName().Name))
                {
                    continue;
                }

                ++types;
                if (MonoScriptIndex.TryGetScriptGuid(type, out _))
                {
                    continue;
                }

                findings.Add(
                    new MonoScriptBindingFinding(MonoScriptBindingProblem.NoBoundScript, type, null)
                );
            }

            foreach (string scriptPath in ScopedScriptPaths(assetPathPrefixes))
            {
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
                if (script == null)
                {
                    unreadable.Add(scriptPath);
                    continue;
                }

                Type bound = script.GetClass();
                if (!IsAuthorable(bound))
                {
                    continue;
                }

                ++scripts;
                string fileName = Path.GetFileNameWithoutExtension(scriptPath);
                if (string.Equals(fileName, SimpleNameOf(bound), StringComparison.Ordinal))
                {
                    continue;
                }

                findings.Add(
                    new MonoScriptBindingFinding(
                        MonoScriptBindingProblem.FileNameMismatch,
                        bound,
                        scriptPath
                    )
                );
            }

            UnreadableAssetPaths.SortAndDeduplicate(unreadable);
            typesConsidered = types;
            scriptsConsidered = scripts;
            return true;
        }

        /// <summary>
        /// Every concrete type an author can put onto a GameObject or create as an asset.
        /// </summary>
        /// <returns>The candidate types, from Unity's own type index.</returns>
        /// <remarks>
        /// Discovery is <c>TypeCache</c> rather than a source scan, because a source scan has been
        /// measured missing real instances in a real tree and cannot see a partial class at all.
        /// </remarks>
        public static IEnumerable<Type> ConcreteAuthorableTypes()
        {
            foreach (Type type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                if (!type.IsAbstract)
                {
                    yield return type;
                }
            }

            foreach (Type type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                if (!type.IsAbstract && !typeof(UnityEditor.Editor).IsAssignableFrom(type))
                {
                    yield return type;
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="bound"/> is a type an author can put onto a GameObject or create
        /// as an asset.
        /// </summary>
        internal static bool IsAuthorable(Type bound)
        {
            return bound != null
                && !typeof(UnityEditor.Editor).IsAssignableFrom(bound)
                && (
                    typeof(MonoBehaviour).IsAssignableFrom(bound)
                    || typeof(ScriptableObject).IsAssignableFrom(bound)
                );
        }

        internal static string SimpleNameOf(Type type)
        {
            string name = type.Name;
            int arity = name.IndexOf('`');
            return arity <= 0 ? name : name.Substring(0, arity);
        }

        private static HashSet<string> ScopedAssemblyNames(IReadOnlyList<string> assetPathPrefixes)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            CollectScopedAssemblyNames(AssembliesType.Editor, assetPathPrefixes, names);
            CollectScopedAssemblyNames(AssembliesType.Player, assetPathPrefixes, names);
            return names;
        }

        /// <summary>
        /// Adds every assembly of <paramref name="assembliesType"/> compiled from a source under
        /// <paramref name="assetPathPrefixes"/>.
        /// </summary>
        /// <param name="assembliesType">Which compilation set to ask for.</param>
        /// <param name="assetPathPrefixes">Asset path prefixes to scope to.</param>
        /// <param name="names">Receives the matching assembly names.</param>
        /// <remarks>
        /// Both sets are asked because an assembly excluded from one is absent from it entirely, and
        /// a scope that quietly loses a tree reports the same zero findings a clean one does.
        /// </remarks>
        private static void CollectScopedAssemblyNames(
            AssembliesType assembliesType,
            IReadOnlyList<string> assetPathPrefixes,
            HashSet<string> names
        )
        {
            Assembly[] assemblies = CompilationPipeline.GetAssemblies(assembliesType);
            if (assemblies == null)
            {
                return;
            }

            foreach (Assembly assembly in assemblies)
            {
                string[] sources = assembly.sourceFiles;
                if (sources == null)
                {
                    continue;
                }

                foreach (string source in sources)
                {
                    if (!AuthoredAssetYaml.IsUnderAnyPrefix(source, assetPathPrefixes))
                    {
                        continue;
                    }

                    names.Add(assembly.name);
                    break;
                }
            }
        }

        private static IEnumerable<string> ScopedScriptPaths(
            IReadOnlyList<string> assetPathPrefixes
        )
        {
            string[] guids = AssetDatabase.FindAssets("t:MonoScript");
            if (guids == null)
            {
                yield break;
            }

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (
                    string.IsNullOrEmpty(path)
                    || !AuthoredAssetYaml.IsUnderAnyPrefix(path, assetPathPrefixes)
                )
                {
                    continue;
                }

                yield return path;
            }
        }
    }
#endif
}
