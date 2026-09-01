// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;

    /// <summary>
    /// Resolves a type to the <c>MonoScript</c> that gives it a door into an asset, and back again.
    /// </summary>
    /// <remarks>
    /// The forward lookup is name-narrowed first, then falls through to a full index rather than to
    /// <c>null</c> -- a resolver that silently answers "no such type" makes every "except its own
    /// file" exclusion match nothing. See
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/docs/features/editor-tools/authored-asset-validation.md">Authored Asset Validation</see>.
    /// </remarks>
    public static class MonoScriptIndex
    {
        private static readonly Dictionary<Type, string> TypeToGuid = new();
        private static readonly Dictionary<string, Type> GuidToType = new();
        private static bool _fullIndexBuilt;

        /// <summary>
        /// Finds the guid of the <c>MonoScript</c> that binds <paramref name="type"/>.
        /// </summary>
        /// <param name="type">The type to resolve.</param>
        /// <param name="guid">Receives the script's guid.</param>
        /// <returns><c>false</c> when no script asset binds the type.</returns>
        public static bool TryGetScriptGuid(Type type, out string guid)
        {
            if (type == null)
            {
                guid = null;
                return false;
            }

            if (TypeToGuid.TryGetValue(type, out string cached))
            {
                guid = cached;
                return cached != null;
            }

            string resolved = FindScriptGuid(type);
            TypeToGuid[type] = resolved;
            guid = resolved;
            return resolved != null;
        }

        /// <summary>
        /// Finds the asset path of the <c>MonoScript</c> that binds <paramref name="type"/>.
        /// </summary>
        /// <param name="type">The type to resolve.</param>
        /// <param name="assetPath">Receives the script's asset path.</param>
        /// <returns><c>false</c> when no script asset binds the type.</returns>
        public static bool TryGetScriptPath(Type type, out string assetPath)
        {
            if (!TryGetScriptGuid(type, out string guid))
            {
                assetPath = null;
                return false;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            assetPath = path;
            return !string.IsNullOrEmpty(path);
        }

        /// <summary>
        /// Finds the type a document's <c>m_Script</c> guid names.
        /// </summary>
        /// <param name="guid">The guid to resolve.</param>
        /// <param name="type">Receives the bound type.</param>
        /// <returns><c>false</c> when the guid resolves to no script, or to a script binding nothing.</returns>
        public static bool TryGetScriptType(string guid, out Type type)
        {
            if (string.IsNullOrEmpty(guid))
            {
                type = null;
                return false;
            }

            if (GuidToType.TryGetValue(guid, out Type cached))
            {
                type = cached;
                return cached != null;
            }

            Type resolved = LoadScriptType(guid);
            GuidToType[guid] = resolved;
            type = resolved;
            return resolved != null;
        }

        /// <summary>
        /// Forgets everything resolved so far, so a scan after a script change sees the new state.
        /// </summary>
        public static void ClearCaches()
        {
            TypeToGuid.Clear();
            GuidToType.Clear();
            _fullIndexBuilt = false;
        }

        private static string FindScriptGuid(Type type)
        {
            string narrowed = FindScriptGuidByName(type);
            if (narrowed != null)
            {
                return narrowed;
            }

            BuildFullIndex();
            return TypeToGuid.TryGetValue(type, out string indexed) ? indexed : null;
        }

        private static string FindScriptGuidByName(Type type)
        {
            string searchName = SearchNameOf(type);
            if (string.IsNullOrEmpty(searchName))
            {
                return null;
            }

            string[] candidates = AssetDatabase.FindAssets($"{searchName} t:MonoScript");
            if (candidates == null)
            {
                return null;
            }

            foreach (string guid in candidates)
            {
                if (BoundTypeOf(guid) == type)
                {
                    return guid;
                }
            }

            return null;
        }

        private static void BuildFullIndex()
        {
            if (_fullIndexBuilt)
            {
                return;
            }

            _fullIndexBuilt = true;
            string[] guids = AssetDatabase.FindAssets("t:MonoScript");
            if (guids == null)
            {
                return;
            }

            foreach (string guid in guids)
            {
                Type bound = BoundTypeOf(guid);
                if (bound == null)
                {
                    continue;
                }

                GuidToType[guid] = bound;
                if (!TypeToGuid.TryGetValue(bound, out string existing) || existing == null)
                {
                    TypeToGuid[bound] = guid;
                }
            }
        }

        private static Type LoadScriptType(string guid)
        {
            Type bound = BoundTypeOf(guid);
            if (bound != null)
            {
                return bound;
            }

            BuildFullIndex();
            return GuidToType.TryGetValue(guid, out Type indexed) ? indexed : null;
        }

        private static Type BoundTypeOf(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script == null)
            {
                return null;
            }

            return script.GetClass();
        }

        private static string SearchNameOf(Type type)
        {
            string name = type.Name;
            int arity = name.IndexOf('`');
            return arity <= 0 ? name : name.Substring(0, arity);
        }
    }
#endif
}
