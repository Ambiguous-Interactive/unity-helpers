// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using WallstopStudios.UnityHelpers.Utils;
    using UnityEditor;

    /// <summary>
    /// Handles the IMGUI needed to pick types and methods for animation events.
    /// </summary>
    internal static class AnimationEventMethodSelector
    {
        private const int DefaultTypeLimit = 50;
        private const int SearchTypeLimit = 200;

        public static IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> FilterLookup(
            AnimationEventItem item,
            IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> lookup
        )
        {
            if (lookup == null)
            {
                return null;
            }

            string cacheKey = item.search + "|" + item.typeSearch;
            if (item.cachedLookup != null && item.lastSearchForCache == cacheKey)
            {
                return item.cachedLookup;
            }

            Dictionary<Type, IReadOnlyList<MethodInfo>> filtered = new();
            using PooledResource<List<string>> methodSearchLease = Buffers<string>.List.Get(
                out List<string> methodSearchTerms
            );
            using PooledResource<List<string>> typeSearchLease = Buffers<string>.List.Get(
                out List<string> typeSearchTerms
            );
            {
                BuildSearchTerms(item.search, methodSearchTerms);
                BuildSearchTerms(item.typeSearch, typeSearchTerms);

                foreach (KeyValuePair<Type, IReadOnlyList<MethodInfo>> entry in lookup)
                {
                    Type type = entry.Key;

                    if (0 < typeSearchTerms.Count)
                    {
                        string typeLower =
                            type.FullName != null ? type.FullName.ToLowerInvariant() : string.Empty;
                        bool matches = ContainsAllTokens(typeLower, typeSearchTerms);
                        if (!matches)
                        {
                            continue;
                        }
                    }

                    if (methodSearchTerms.Count == 0)
                    {
                        filtered[type] = entry.Value;
                        continue;
                    }

                    List<MethodInfo> methodBuffer = new();
                    foreach (MethodInfo method in entry.Value)
                    {
                        string methodLower = method.Name.ToLowerInvariant();
                        if (ContainsAllTokens(methodLower, methodSearchTerms))
                        {
                            methodBuffer.Add(method);
                        }
                    }

                    if (0 < methodBuffer.Count)
                    {
                        filtered[type] = methodBuffer;
                    }
                }
            }

            item.cachedLookup = filtered;
            item.lastSearchForCache = cacheKey;
            return filtered;
        }

        public static void EnsureSelection(
            AnimationEventItem item,
            IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> lookup
        )
        {
            if (item.selectedType != null || lookup == null)
            {
                return;
            }

            using PooledResource<List<Type>> sortedTypesResource = Buffers<Type>.List.Get(
                out List<Type> sortedTypes
            );
            {
                foreach (Type type in lookup.Keys)
                {
                    sortedTypes.Add(type);
                }

                sortedTypes.Sort(
                    static (lhs, rhs) =>
                        string.Compare(lhs.FullName, rhs.FullName, StringComparison.Ordinal)
                );

                for (int ti = 0; ti < sortedTypes.Count; ti++)
                {
                    Type type = sortedTypes[ti];
                    if (!lookup.TryGetValue(type, out IReadOnlyList<MethodInfo> methods))
                    {
                        continue;
                    }

                    for (int mi = 0; mi < methods.Count; mi++)
                    {
                        MethodInfo method = methods[mi];
                        if (
                            string.Equals(
                                method.Name,
                                item.animationEvent.functionName,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            item.selectedType = type;
                            item.selectedMethod = method;
                            return;
                        }
                    }
                }
            }
        }

        public static void ValidateSelection(
            AnimationEventItem item,
            IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> lookup
        )
        {
            item.isValid = true;
            item.validationMessage = string.Empty;

            if (string.IsNullOrEmpty(item.animationEvent.functionName))
            {
                item.isValid = false;
                item.validationMessage = "Function name is empty";
                return;
            }

            if (item.selectedType != null && item.selectedMethod != null)
            {
                return;
            }

            if (lookup == null)
            {
                item.isValid = false;
                item.validationMessage = "No types available for validation.";
                return;
            }

            foreach (KeyValuePair<Type, IReadOnlyList<MethodInfo>> entry in lookup)
            {
                foreach (MethodInfo method in entry.Value)
                {
                    if (
                        string.Equals(
                            method.Name,
                            item.animationEvent.functionName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return;
                    }
                }
            }

            item.isValid = false;
            item.validationMessage =
                $"No method named '{item.animationEvent.functionName}' found in available types";
        }

        public static bool DrawTypeSelector(
            AnimationEventItem item,
            IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> lookup,
            Action<string> recordUndo
        )
        {
            if (lookup == null || lookup.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No types with animation event methods found",
                    MessageType.Info
                );
                return false;
            }

            using PooledResource<List<Type>> allTypesLease = Buffers<Type>.List.Get(
                out List<Type> allTypes
            );
            {
                foreach (Type type in lookup.Keys)
                {
                    allTypes.Add(type);
                }

                allTypes.Sort(
                    static (lhs, rhs) =>
                        string.Compare(lhs.FullName, rhs.FullName, StringComparison.Ordinal)
                );

                using PooledResource<List<Type>> filteredLease = Buffers<Type>.List.Get(
                    out List<Type> filtered
                );
                {
                    ApplyTypeSearch(allTypes, item, filtered, out bool truncated);

                    string[] displayNames = new string[filtered.Count];
                    for (int i = 0; i < filtered.Count; i++)
                    {
                        displayNames[i] = filtered[i]?.FullName ?? string.Empty;
                    }

                    int currentIndex = Math.Max(filtered.IndexOf(item.selectedType), 0);
                    EditorGUI.BeginChangeCheck();
                    int selectedIndex = EditorGUILayout.Popup(
                        "TypeName",
                        currentIndex,
                        displayNames
                    );
                    if (EditorGUI.EndChangeCheck())
                    {
                        recordUndo?.Invoke("Change Animation Event Type");
                        item.selectedType = filtered[selectedIndex];
                        item.selectedMethod = null;
                    }

                    if (truncated)
                    {
                        EditorGUILayout.HelpBox(
                            "Result list trimmed. Use Type Search to narrow results.",
                            MessageType.Info
                        );
                    }

                    return item.selectedType != null;
                }
            }
        }

        public static bool DrawMethodSelector(
            AnimationEventItem item,
            IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> lookup,
            Action<string> recordUndo
        )
        {
            if (lookup == null || item.selectedType == null)
            {
                return false;
            }

            if (!lookup.TryGetValue(item.selectedType, out IReadOnlyList<MethodInfo> methods))
            {
                methods = Array.Empty<MethodInfo>();
            }

            using PooledResource<List<MethodInfo>> bufferLease = Buffers<MethodInfo>.List.Get(
                out List<MethodInfo> buffer
            );
            {
                for (int i = 0; i < methods.Count; i++)
                {
                    buffer.Add(methods[i]);
                }

                if (item.selectedMethod == null || !buffer.Contains(item.selectedMethod))
                {
                    foreach (MethodInfo methodInfo in buffer)
                    {
                        if (
                            string.Equals(
                                methodInfo.Name,
                                item.animationEvent.functionName,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            item.selectedMethod = methodInfo;
                            break;
                        }
                    }

                    if (item.selectedMethod != null && !buffer.Contains(item.selectedMethod))
                    {
                        buffer.Add(item.selectedMethod);
                    }
                }

                string[] methodNames = new string[buffer.Count];
                int currentIndex = -1;
                for (int i = 0; i < buffer.Count; i++)
                {
                    methodNames[i] = buffer[i]?.Name ?? string.Empty;
                    if (buffer[i] == item.selectedMethod)
                    {
                        currentIndex = i;
                    }
                }

                EditorGUI.BeginChangeCheck();
                int selectedIndex = EditorGUILayout.Popup("MethodName", currentIndex, methodNames);
                if (EditorGUI.EndChangeCheck() && 0 <= selectedIndex)
                {
                    recordUndo?.Invoke("Change Animation Event Method");
                    item.selectedMethod = buffer[selectedIndex];
                    item.animationEvent.functionName = item.selectedMethod.Name;
                }

                return item.selectedMethod != null;
            }
        }

        private static void BuildSearchTerms(string raw, List<string> destination)
        {
            destination.Clear();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            string[] parts = raw.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i];
                if (string.IsNullOrWhiteSpace(token) || token == "*")
                {
                    continue;
                }

                destination.Add(token.Trim().ToLowerInvariant());
            }
        }

        private static bool ContainsAllTokens(string haystack, IReadOnlyList<string> tokens)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (haystack.IndexOf(tokens[i], StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ApplyTypeSearch(
            List<Type> allTypes,
            AnimationEventItem item,
            List<Type> filtered,
            out bool truncated
        )
        {
            int limit = string.IsNullOrEmpty(item.typeSearch) ? DefaultTypeLimit : SearchTypeLimit;
            filtered.Clear();
            for (int i = 0; i < allTypes.Count; i++)
            {
                filtered.Add(allTypes[i]);
            }

            if (!string.IsNullOrEmpty(item.typeSearch))
            {
                string searchLower = item.typeSearch.ToLowerInvariant();
                filtered.Clear();
                for (int i = 0; i < allTypes.Count; i++)
                {
                    Type type = allTypes[i];
                    string fullName = type.FullName ?? string.Empty;
                    if (
                        0
                        <= fullName
                            .ToLowerInvariant()
                            .IndexOf(searchLower, StringComparison.Ordinal)
                    )
                    {
                        filtered.Add(type);
                    }
                }
            }

            if (item.selectedType != null && !filtered.Contains(item.selectedType))
            {
                filtered.Insert(0, item.selectedType);
            }

            if (limit < filtered.Count)
            {
                filtered.RemoveRange(limit, filtered.Count - limit);
                truncated = true;
                return;
            }

            truncated = false;
        }
    }
#endif
}
