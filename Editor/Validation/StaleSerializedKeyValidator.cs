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
    /// Asks the mirror of <see cref="SerializedFieldValidator"/>'s question: which keys are already
    /// written that no field claims?
    /// </summary>
    public static class StaleSerializedKeyValidator
    {
        /// <summary>
        /// The keys Unity writes into every <c>!u!114</c> document from the engine's own layout.
        /// </summary>
        public static readonly IReadOnlyCollection<string> UnityOwnedKeys = new HashSet<string>(
            StringComparer.Ordinal
        )
        {
            "m_ObjectHideFlags",
            "m_CorrespondingSourceObject",
            "m_PrefabInstance",
            "m_PrefabAsset",
            "m_PrefabInternal",
            "m_PrefabParentObject",
            "m_GameObject",
            "m_Enabled",
            "m_EditorHideFlags",
            "m_Script",
            "m_Name",
            "m_EditorClassIdentifier",
            "serializedVersion",
            "references",
        };

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
            return TryScan(assetPaths, findings, new List<string>(), out unresolvedScripts);
        }

        /// <summary>
        /// Reports every key in <paramref name="assetPaths"/> that no field of its type claims, and
        /// every asset the scan could not read.
        /// </summary>
        /// <param name="assetPaths">The committed assets to read.</param>
        /// <param name="findings">Receives one entry per site.</param>
        /// <param name="unreadable">Receives the asset paths the scan could not open, sorted.</param>
        /// <param name="unresolvedScripts">Receives how many documents named a script that resolves to nothing.</param>
        /// <returns><c>false</c> when the scan could not run at all.</returns>
        /// <remarks>See <see cref="UnreadableAssetPaths"/> for why an unreadable asset is never a finding.</remarks>
        public static bool TryScan(
            IReadOnlyList<string> assetPaths,
            List<StaleSerializedKeyFinding> findings,
            List<string> unreadable,
            out int unresolvedScripts
        )
        {
            if (assetPaths == null || findings == null || unreadable == null)
            {
                unresolvedScripts = 0;
                return false;
            }

            int unresolved = 0;
            findings.Clear();
            unreadable.Clear();
            Dictionary<Type, HashSet<string>> declared = new();

            for (int index = 0; index < assetPaths.Count; ++index)
            {
                string assetPath = assetPaths[index];
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                if (
                    !AuthoredAssetYaml.TryReadDocuments(
                        AuthoredAssetPaths.ToFileSystemPath(assetPath),
                        out IReadOnlyList<string> _,
                        out IReadOnlyList<AuthoredAssetDocument> documents
                    )
                )
                {
                    unreadable.Add(assetPath);
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
                        ++unresolved;
                        continue;
                    }

                    if (!TryGetDeclaredKeys(owner, declared, out HashSet<string> keys))
                    {
                        ++unresolved;
                        continue;
                    }

                    Judge(assetPath, candidate, owner, keys, findings);
                }
            }

            UnreadableAssetPaths.SortAndDeduplicate(unreadable);
            unresolvedScripts = unresolved;
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
            if (cache.TryGetValue(owner, out HashSet<string> cached))
            {
                keys = cached;
                return cached != null;
            }

            HashSet<string> built = BuildDeclaredKeys(owner);
            cache[owner] = built;
            keys = built;
            return built != null;
        }

        internal static HashSet<string> BuildDeclaredKeys(Type owner)
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

                HashSet<string> keys = new(UnityOwnedKeys, StringComparer.Ordinal);
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
                foreach (FieldInfo field in type.GetFields(Declared))
                {
                    object[] aliases = field.GetCustomAttributes(
                        typeof(FormerlySerializedAsAttribute),
                        inherit: true
                    );

                    foreach (object alias in aliases)
                    {
                        if (alias is FormerlySerializedAsAttribute former)
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
