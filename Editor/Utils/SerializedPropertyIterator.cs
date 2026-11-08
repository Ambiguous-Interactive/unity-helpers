// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;

    /// <summary>
    /// Provides allocation-optimized iteration over SerializedObject properties.
    /// Uses GetIterator() to avoid repeated FindProperty() allocations.
    /// </summary>
    internal static class SerializedPropertyIterator
    {
        private const string ScriptPropertyPath = "m_Script";

        /// <summary>
        /// Iterates through all visible properties of a SerializedObject once,
        /// invoking callbacks for each property based on whether it matches a set of paths to draw.
        /// This avoids O(n) FindProperty calls by doing a single O(n) iteration.
        /// </summary>
        /// <param name="serializedObject">The serialized object to iterate.</param>
        /// <param name="pathsToDraw">Set of property paths that should be drawn with the custom drawer.</param>
        /// <param name="scriptPropertyCallback">Called when m_Script property is found (may be null if not found).</param>
        /// <param name="matchedPropertyCallback">Called for each property whose path is in pathsToDraw.</param>
        /// <param name="unmatchedPropertyCallback">Called for each property whose path is NOT in pathsToDraw.</param>
        internal static void IterateVisibleProperties(
            SerializedObject serializedObject,
            HashSet<string> pathsToDraw,
            Action<SerializedProperty> scriptPropertyCallback,
            Action<SerializedProperty> matchedPropertyCallback,
            Action<SerializedProperty> unmatchedPropertyCallback
        )
        {
            if (serializedObject == null)
            {
                return;
            }

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                string path = iterator.propertyPath;

                if (string.Equals(path, ScriptPropertyPath, StringComparison.Ordinal))
                {
                    scriptPropertyCallback?.Invoke(iterator);
                    continue;
                }

                if (pathsToDraw != null && pathsToDraw.Contains(path))
                {
                    matchedPropertyCallback?.Invoke(iterator);
                }
                else
                {
                    unmatchedPropertyCallback?.Invoke(iterator);
                }
            }
        }

        /// <summary>
        /// Iterates through all visible properties and collects copies into a pooled dictionary.
        /// Use this when you need random access to properties by path.
        /// Note: The returned properties are copies that remain valid after iteration.
        /// </summary>
        /// <param name="serializedObject">The serialized object to iterate.</param>
        /// <param name="propertyLookup">Dictionary to populate with property copies keyed by path.</param>
        /// <param name="excludeScript">If true, excludes the m_Script property.</param>
        internal static void BuildPropertyLookup(
            SerializedObject serializedObject,
            Dictionary<string, SerializedProperty> propertyLookup,
            bool excludeScript = true
        )
        {
            if (serializedObject == null || propertyLookup == null)
            {
                return;
            }

            propertyLookup.Clear();
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                string path = iterator.propertyPath;

                if (
                    excludeScript
                    && string.Equals(path, ScriptPropertyPath, StringComparison.Ordinal)
                )
                {
                    continue;
                }

                propertyLookup[path] = iterator.Copy();
            }
        }

        /// <summary>
        /// Finds a specific property by iterating through visible properties.
        /// More efficient than FindProperty when you only need one property and are already iterating.
        /// </summary>
        /// <param name="serializedObject">The serialized object to search.</param>
        /// <param name="propertyPath">The path of the property to find.</param>
        /// <returns>A copy of the property if found, null otherwise.</returns>
        internal static SerializedProperty FindPropertyByIteration(
            SerializedObject serializedObject,
            string propertyPath
        )
        {
            if (serializedObject == null || string.IsNullOrWhiteSpace(propertyPath))
            {
                return null;
            }

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (string.Equals(iterator.propertyPath, propertyPath, StringComparison.Ordinal))
                {
                    return iterator.Copy();
                }
            }

            return null;
        }

        /// <summary>
        /// Draws properties using a single iteration pass.
        /// Groups are collected first, then drawn during iteration when their anchor property is encountered.
        /// </summary>
        /// <param name="serializedObject">The serialized object.</param>
        /// <param name="groupedPaths">Set of property paths that belong to groups (will be skipped in normal iteration).</param>
        /// <param name="anchorToGroups">Maps anchor property paths to their group definitions.</param>
        /// <param name="drawProperty">Callback to draw a non-grouped property.</param>
        /// <param name="drawGroups">Callback to draw groups anchored at a specific property.</param>
        /// <param name="drawScriptProperty">Callback to draw the script property (or null to skip).</param>
        internal static void DrawPropertiesWithGroups<TGroup>(
            SerializedObject serializedObject,
            HashSet<string> groupedPaths,
            Dictionary<string, List<TGroup>> anchorToGroups,
            Action<SerializedProperty> drawProperty,
            Action<List<TGroup>> drawGroups,
            Action<SerializedProperty> drawScriptProperty
        )
        {
            if (serializedObject == null)
            {
                return;
            }

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                string path = iterator.propertyPath;

                if (string.Equals(path, ScriptPropertyPath, StringComparison.Ordinal))
                {
                    drawScriptProperty?.Invoke(iterator);
                    continue;
                }

                if (
                    anchorToGroups != null
                    && anchorToGroups.TryGetValue(path, out List<TGroup> groups)
                )
                {
                    drawGroups?.Invoke(groups);
                    continue;
                }

                if (groupedPaths != null && groupedPaths.Contains(path))
                {
                    continue;
                }

                drawProperty?.Invoke(iterator);
            }
        }
    }
#endif
}
