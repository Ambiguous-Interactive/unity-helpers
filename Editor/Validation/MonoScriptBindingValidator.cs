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
    /// One type or script asset whose <c>MonoScript</c> binding is missing or decided by accident.
    /// </summary>
    public readonly struct MonoScriptBindingFinding
    {
        /// <summary>Initializes a new instance of the <see cref="MonoScriptBindingFinding"/> struct.</summary>
        /// <param name="problem">Which of the two rules the subject breaks.</param>
        /// <param name="subject">The type that cannot be authored, or the type a file misnames.</param>
        /// <param name="scriptPath">The script asset involved, or <c>null</c> when there is none.</param>
        public MonoScriptBindingFinding(
            MonoScriptBindingProblem problem,
            Type subject,
            string scriptPath
        )
        {
            Problem = problem;
            Subject = subject;
            ScriptPath = scriptPath;
        }

        /// <summary>Which of the two rules the subject breaks.</summary>
        public MonoScriptBindingProblem Problem { get; }

        /// <summary>The type that cannot be authored, or the type a file misnames.</summary>
        public Type Subject { get; }

        /// <summary>The script asset involved, or <c>null</c> when no script binds the type.</summary>
        public string ScriptPath { get; }

        /// <summary>Renders the finding as one line, naming the consequence rather than the rule.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            if (Problem == MonoScriptBindingProblem.FileNameMismatch)
            {
                return $"{ScriptPath} binds {Subject?.FullName}, which the file is not named after. "
                    + "Unity picks the class for a file by name and falls back to declaration order, "
                    + "so one type added above this one moves the binding and every reference to it "
                    + "becomes a missing script.";
            }

            return $"{Subject?.FullName} has no MonoScript, so it cannot be dragged onto a "
                + "GameObject or created as an asset. It still compiles and AddComponent still "
                + "constructs it, so no behavioural test sees this.";
        }
    }

    /// <summary>
    /// Holds every concrete component and asset type to the two rules that keep it authorable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rule one is the symptom: every concrete <c>MonoBehaviour</c> and <c>ScriptableObject</c>
    /// resolves to a <c>MonoScript</c>. Rule two is the cause, and what keeps rule one true: every
    /// script asset is named after the type it binds. Unity picks the class for a file by name and
    /// falls back <em>silently</em> to whatever it finds, so a file named after its type cannot lose
    /// the binding to a type added above it and a file that is not is one edit from fatal.
    /// </para>
    /// <para>
    /// Neither nested nor open-generic component types are excluded. Neither can carry a
    /// <c>MonoScript</c> either, so excluding them is a license to introduce the same defect in a
    /// shape the gate has stopped looking at. Abstract types are excluded, because nothing can be an
    /// instance of one.
    /// </para>
    /// <para>
    /// Together the two settle "one file per <c>MonoBehaviour</c> or <c>ScriptableObject</c>"
    /// without a source scan: one file yields one <c>MonoScript</c> bound to one class, so a second
    /// component type sharing a file can satisfy at most one of the two rules.
    /// </para>
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
            typesConsidered = 0;
            scriptsConsidered = 0;
            if (findings == null || assetPathPrefixes == null || assetPathPrefixes.Count <= 0)
            {
                return false;
            }

            findings.Clear();
            HashSet<string> scopedAssemblies = ScopedAssemblyNames(assetPathPrefixes);

            foreach (Type type in ConcreteAuthorableTypes())
            {
                if (!scopedAssemblies.Contains(type.Assembly.GetName().Name))
                {
                    continue;
                }

                ++typesConsidered;
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
                    continue;
                }

                Type bound = script.GetClass();
                if (bound == null)
                {
                    continue;
                }

                ++scriptsConsidered;
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

        private static string SimpleNameOf(Type type)
        {
            string name = type.Name;
            int arity = name.IndexOf('`');
            return arity <= 0 ? name : name.Substring(0, arity);
        }

        private static HashSet<string> ScopedAssemblyNames(IReadOnlyList<string> assetPathPrefixes)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            Assembly[] assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            if (assemblies == null)
            {
                return names;
            }

            for (int index = 0; index < assemblies.Length; ++index)
            {
                Assembly assembly = assemblies[index];
                string[] sources = assembly.sourceFiles;
                if (sources == null)
                {
                    continue;
                }

                for (int source = 0; source < sources.Length; ++source)
                {
                    if (!IsUnderAnyPrefix(sources[source], assetPathPrefixes))
                    {
                        continue;
                    }

                    names.Add(assembly.name);
                    break;
                }
            }

            return names;
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

            for (int index = 0; index < guids.Length; ++index)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                if (string.IsNullOrEmpty(path) || !IsUnderAnyPrefix(path, assetPathPrefixes))
                {
                    continue;
                }

                yield return path;
            }
        }

        private static bool IsUnderAnyPrefix(string path, IReadOnlyList<string> assetPathPrefixes)
        {
            string normalized = path.Replace('\\', '/');
            for (int index = 0; index < assetPathPrefixes.Count; ++index)
            {
                string prefix = assetPathPrefixes[index];
                if (
                    !string.IsNullOrEmpty(prefix)
                    && normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }

            return false;
        }
    }
#endif
}
