// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Serialization;
    using Object = UnityEngine.Object;

    /// <summary>
    /// One serialized key an asset still records that no field of its type claims.
    /// </summary>
    public readonly struct StaleSerializedKeyFinding
    {
        /// <summary>Initializes a new instance of the <see cref="StaleSerializedKeyFinding"/> struct.</summary>
        /// <param name="assetPath">The asset carrying the key.</param>
        /// <param name="lineNumber">The one-based line the key is written on.</param>
        /// <param name="ownerType">The type the document's script resolves to.</param>
        /// <param name="key">The key nothing claims.</param>
        public StaleSerializedKeyFinding(
            string assetPath,
            int lineNumber,
            Type ownerType,
            string key
        )
        {
            AssetPath = assetPath;
            LineNumber = lineNumber;
            OwnerType = ownerType;
            Key = key;
        }

        /// <summary>The asset carrying the key.</summary>
        public string AssetPath { get; }

        /// <summary>The one-based line the key is written on.</summary>
        public int LineNumber { get; }

        /// <summary>The type the document's script resolves to.</summary>
        public Type OwnerType { get; }

        /// <summary>The key nothing claims.</summary>
        public string Key { get; }

        /// <summary>
        /// The cause, one entry per type and key, because a migration retires a field once and every
        /// asset of that type inherits it.
        /// </summary>
        public string Cause => $"{OwnerType?.FullName}::{Key}";

        /// <summary>Renders the finding as a location a reader can open.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            return $"{AssetPath}:{LineNumber}: {Cause}";
        }
    }

    /// <summary>
    /// Asks the mirror of <see cref="SerializedFieldValidator"/>'s question: which keys are already
    /// written that no field claims?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity keeps an unknown serialized key on load and writes it straight back out. So a field
    /// deleted years ago is still in the YAML today and reads exactly like a live one, and nothing
    /// reports it -- not the inspector, not a build, not a domain reload. The costs are diff noise
    /// in every asset of an affected type, and worse: a retired object-reference key keeps another
    /// asset looking referenced, so a guid search answers "this sprite is used by the level prefab"
    /// about a slot nothing reads.
    /// </para>
    /// <para>
    /// The declared set is <see cref="SerializedObject"/> over a throwaway instance, never
    /// reflection over the type's fields. Only Unity knows what its serializer accepted, and the
    /// base chain routinely leaves the project into package assemblies; modelling it instead was
    /// measured reporting roughly 1771 findings of which about four were real. The probe is
    /// deactivated <b>before</b> the component is added, exactly as
    /// <see cref="SerializedFieldValidator"/> does, so a project-wide scan does not run the startup
    /// half of every behaviour in the project.
    /// </para>
    /// <para>
    /// Every <see cref="FormerlySerializedAsAttribute"/> alias is a live key and is added, walking
    /// the base chain. Judging by <see cref="SerializedObject"/> alone was measured reporting 565
    /// aliases doing their job as orphans in one project -- including the only <c>clip</c> key on a
    /// live effect, which a reader following the report would have deleted.
    /// </para>
    /// <para>
    /// A document whose script does not resolve is counted, not reported. That is a missing script,
    /// a different defect with its own signal, and guessing at its keys reports all of them.
    /// </para>
    /// </remarks>
    public static class StaleSerializedKeyValidator
    {
        /// <summary>
        /// Reports every key in <paramref name="assetPaths"/> that no field of its type claims.
        /// </summary>
        /// <param name="assetPaths">The committed assets to read.</param>
        /// <param name="findings">Receives one entry per site.</param>
        /// <param name="unresolvedScripts">Receives how many documents named a script that resolves to nothing.</param>
        /// <returns><c>false</c> when the scan could not run at all.</returns>
        public static bool TryScan(
            IReadOnlyList<string> assetPaths,
            List<StaleSerializedKeyFinding> findings,
            out int unresolvedScripts
        )
        {
            unresolvedScripts = 0;
            if (assetPaths == null || findings == null)
            {
                return false;
            }

            findings.Clear();
            Dictionary<Type, HashSet<string>> declared = new();

            for (int index = 0; index < assetPaths.Count; ++index)
            {
                string assetPath = assetPaths[index];
                if (
                    !AuthoredAssetYaml.TryReadDocuments(
                        assetPath,
                        out IReadOnlyList<string> _,
                        out IReadOnlyList<AuthoredAssetDocument> documents
                    )
                )
                {
                    continue;
                }

                for (int document = 0; document < documents.Count; ++document)
                {
                    AuthoredAssetDocument candidate = documents[document];
                    if (
                        candidate.UnityTypeId != AuthoredAssetYaml.MonoBehaviourTypeId
                        || candidate.IsStripped
                        || string.IsNullOrEmpty(candidate.ScriptGuid)
                    )
                    {
                        continue;
                    }

                    if (!MonoScriptIndex.TryGetScriptType(candidate.ScriptGuid, out Type owner))
                    {
                        ++unresolvedScripts;
                        continue;
                    }

                    if (!TryGetDeclaredKeys(owner, declared, out HashSet<string> keys))
                    {
                        ++unresolvedScripts;
                        continue;
                    }

                    Judge(assetPath, candidate, owner, keys, findings);
                }
            }

            return true;
        }

        /// <summary>
        /// Groups <paramref name="findings"/> by the cause that produced them.
        /// </summary>
        /// <param name="findings">The per-site findings to group.</param>
        /// <returns>How many sites each <c>Type::Key</c> cause accounts for.</returns>
        /// <remarks>
        /// The per-site list is what a reader opens; the causes are what a reader fixes. One retired
        /// field can be hundreds of sites, and reading them as hundreds of problems is why this
        /// question usually ends in a shrug.
        /// </remarks>
        public static IReadOnlyDictionary<string, int> CausesOf(
            IReadOnlyList<StaleSerializedKeyFinding> findings
        )
        {
            Dictionary<string, int> causes = new(StringComparer.Ordinal);
            if (findings == null)
            {
                return causes;
            }

            for (int index = 0; index < findings.Count; ++index)
            {
                string cause = findings[index].Cause;
                causes[cause] = causes.TryGetValue(cause, out int count) ? count + 1 : 1;
            }

            return causes;
        }

        private static void Judge(
            string assetPath,
            AuthoredAssetDocument document,
            Type owner,
            HashSet<string> declared,
            List<StaleSerializedKeyFinding> findings
        )
        {
            IReadOnlyList<AuthoredAssetEntry> entries = document.Entries;
            if (entries.Count <= 0)
            {
                return;
            }

            int fieldIndent = entries[0].Indent;
            for (int index = 1; index < entries.Count; ++index)
            {
                if (entries[index].Indent < fieldIndent)
                {
                    fieldIndent = entries[index].Indent;
                }
            }

            for (int index = 0; index < entries.Count; ++index)
            {
                AuthoredAssetEntry entry = entries[index];
                if (entry.Indent != fieldIndent || declared.Contains(entry.Key))
                {
                    continue;
                }

                findings.Add(
                    new StaleSerializedKeyFinding(assetPath, entry.LineNumber, owner, entry.Key)
                );
            }
        }

        private static bool TryGetDeclaredKeys(
            Type owner,
            Dictionary<Type, HashSet<string>> cache,
            out HashSet<string> keys
        )
        {
            if (cache.TryGetValue(owner, out keys))
            {
                return keys != null;
            }

            keys = BuildDeclaredKeys(owner);
            cache[owner] = keys;
            return keys != null;
        }

        private static HashSet<string> BuildDeclaredKeys(Type owner)
        {
            Object instance = null;
            GameObject host = null;
            try
            {
                if (typeof(ScriptableObject).IsAssignableFrom(owner))
                {
                    instance = ScriptableObject.CreateInstance(owner);
                }
                else if (typeof(MonoBehaviour).IsAssignableFrom(owner))
                {
                    host = new GameObject("WallstopStudios.StaleSerializedKeyProbe")
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                    };

                    host.SetActive(false);
                    instance = host.AddComponent(owner);
                }

                if (instance == null)
                {
                    return null;
                }

                HashSet<string> keys = new(StringComparer.Ordinal);
                using SerializedObject serialized = new(instance);
                SerializedProperty iterator = serialized.GetIterator();
                bool remaining = iterator.Next(true);
                while (remaining)
                {
                    keys.Add(iterator.name);
                    remaining = iterator.Next(false);
                }

                AddAliases(owner, keys);
                return keys;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (host != null)
                {
                    Object.DestroyImmediate(host);
                }
                else if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        private static void AddAliases(Type owner, HashSet<string> keys)
        {
            const BindingFlags Declared =
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;

            for (Type type = owner; type != null; type = type.BaseType)
            {
                FieldInfo[] fields = type.GetFields(Declared);
                for (int index = 0; index < fields.Length; ++index)
                {
                    object[] aliases = fields[index]
                        .GetCustomAttributes(typeof(FormerlySerializedAsAttribute), inherit: true);

                    for (int alias = 0; alias < aliases.Length; ++alias)
                    {
                        if (aliases[alias] is FormerlySerializedAsAttribute former)
                        {
                            keys.Add(former.oldName);
                        }
                    }
                }
            }
        }
    }
#endif
}
