// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Extensions
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEditor;

    /// <summary>
    /// Editor-only extension methods for working with SerializedProperty objects.
    /// </summary>
    public static class SerializedPropertyExtensions
    {
        private static readonly Dictionary<string, string[]> PathSplitCache = new(
            StringComparer.Ordinal
        );
        private static readonly Dictionary<string, string[]> TrimmedPathSplitCache = new(
            StringComparer.Ordinal
        );
        private static readonly char[] PathSeparator = { '.' };

        internal static void ClearCache()
        {
            PathSplitCache.Clear();
            TrimmedPathSplitCache.Clear();
        }

        private static string[] GetCachedPathParts(string propertyPath)
        {
            if (PathSplitCache.TryGetValue(propertyPath, out string[] cached))
            {
                return cached;
            }

            string[] parts = propertyPath.Split(PathSeparator);
            PathSplitCache[propertyPath] = parts;
            return parts;
        }

        private static string[] GetTrimmedPathParts(string propertyPath, string propertyName)
        {
            if (TrimmedPathSplitCache.TryGetValue(propertyPath, out string[] cached))
            {
                return cached;
            }

            string[] pathParts = GetCachedPathParts(propertyPath);

            if (
                string.Equals(propertyName, "data", StringComparison.Ordinal)
                && pathParts.Length > 1
                && pathParts[^1].Contains('[')
                && pathParts[^1].Contains(']')
                && string.Equals(pathParts[^2], "Array", StringComparison.Ordinal)
            )
            {
                string[] trimmed = new string[pathParts.Length - 2];
                Array.Copy(pathParts, trimmed, trimmed.Length);
                TrimmedPathSplitCache[propertyPath] = trimmed;
                return trimmed;
            }

            TrimmedPathSplitCache[propertyPath] = pathParts;
            return pathParts;
        }

        /// <summary>
        /// Appends a new default element to the end of an array/list property and returns it.
        /// Unlike InsertArrayElementAtIndex, this works even when the array is empty and avoids duplicating the last entry.
        /// </summary>
        /// <param name="arrayProperty">Serialized array/list property.</param>
        /// <returns>The SerializedProperty representing the newly added element.</returns>
        /// <exception cref="ArgumentNullException">Thrown if arrayProperty is null.</exception>
        public static SerializedProperty AppendArrayElement(this SerializedProperty arrayProperty)
        {
            if (arrayProperty == null)
            {
                throw new ArgumentNullException(nameof(arrayProperty));
            }

            if (!arrayProperty.isArray)
            {
                throw new InvalidOperationException(
                    $"SerializedProperty '{arrayProperty.propertyPath}' is not an array."
                );
            }

            int newIndex = arrayProperty.arraySize;
            arrayProperty.arraySize = newIndex + 1;
            return arrayProperty.GetArrayElementAtIndex(newIndex);
        }

        /// <summary>
        /// Gets the instance object that contains (encloses) the given SerializedProperty, along with the field's metadata.
        /// </summary>
        /// <param name="property">The SerializedProperty to reflect upon.</param>
        /// <param name="fieldInfo">Outputs the FieldInfo of the field represented by this property.</param>
        /// <returns>The instance object that owns the field, or null if the property or its target is null.</returns>
        /// <remarks>
        /// This method walks the property path to find the parent object that contains the field.
        /// It handles nested objects, arrays, and collections properly.
        /// Useful for implementing custom property drawers that need access to the containing object.
        /// Null handling: Returns null if the property or its target object is null.
        /// Thread-safe: No. Must be called from the main Unity thread.
        /// Performance: Uses reflection to traverse the property path. Cache results if called frequently.
        /// Array handling: Properly handles "Array.data[index]" patterns in property paths.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Never thrown explicitly, but may occur if property is null.</exception>
        public static object GetEnclosingObject(
            this SerializedProperty property,
            out FieldInfo fieldInfo
        )
        {
            FieldInfo resolvedField = null;
            object obj = property.serializedObject.targetObject;
            if (obj == null)
            {
                fieldInfo = null;
                return null;
            }
            Type type = obj.GetType();
            string[] pathParts = GetTrimmedPathParts(property.propertyPath, property.name);

            // Traverse the path but stop at the second-to-last field
            for (int i = 0; i < pathParts.Length - 1; ++i)
            {
                string fieldName = pathParts[i];

                if (string.Equals(fieldName, "Array", StringComparison.Ordinal))
                {
                    // Move to "data[i]", no need to length-check, we're guarded above
                    ++i;
                    fieldName = pathParts[i];
                    if (!TryParseArrayIndex(fieldName, out int index))
                    {
                        // Unexpected, die
                        fieldInfo = null;
                        return null;
                    }

                    obj = GetElementAtIndex(obj, index);
                    type = obj?.GetType();
                    UpdateField(fieldName, ref resolvedField);

                    if (i == pathParts.Length - 2)
                    {
                        fieldName = pathParts[i + 1];
                        UpdateField(fieldName, ref resolvedField);
                    }
                    continue;
                }

                UpdateField(fieldName, ref resolvedField);
                if (resolvedField == null)
                {
                    fieldInfo = null;
                    return null;
                }

                // Move deeper but stop before the last property in the path
                if (i < pathParts.Length - 2)
                {
                    obj = resolvedField.GetValue(obj);
                    type = resolvedField.FieldType;
                }
            }

            if (resolvedField == null)
            {
                // Use the last segment of the possibly-trimmed path (actual field name), not property.name (which can be "data")
                if (pathParts.Length > 0)
                {
                    UpdateField(pathParts[^1], ref resolvedField);
                }
            }

            fieldInfo = resolvedField;
            return obj;

            void UpdateField(string fieldName, ref FieldInfo field)
            {
                FieldInfo newField = type?.GetField(
                    fieldName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
                );
                if (newField != null)
                {
                    field = newField;
                }
            }
        }

        /// <summary>
        /// Gets the final target object and its FieldInfo for a given SerializedProperty.
        /// </summary>
        /// <param name="property">The SerializedProperty to reflect upon.</param>
        /// <param name="fieldInfo">Outputs the FieldInfo of the field represented by this property.</param>
        /// <returns>The instance value of the field itself, or null if the property or its target is null.</returns>
        /// <remarks>
        /// Unlike GetEnclosingObject, this method returns the value of the field itself, not its parent.
        /// This walks the full property path including the final field, retrieving the actual value.
        /// Handles arrays and nested objects properly.
        /// Null handling: Returns null if the property, its target object, or any intermediate field is null.
        /// Thread-safe: No. Must be called from the main Unity thread.
        /// Performance: Uses reflection to traverse the property path. Cache results if called frequently.
        /// Array handling: Properly handles "Array.data[index]" patterns in property paths.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Never thrown explicitly, but may occur if property is null.</exception>
        public static object GetTargetObjectWithField(
            this SerializedProperty property,
            out FieldInfo fieldInfo
        )
        {
            FieldInfo resolvedField = null;
            object obj = property.serializedObject.targetObject;
            if (obj == null)
            {
                fieldInfo = null;
                return null;
            }

            Type type = obj.GetType();
            string[] pathParts = GetCachedPathParts(property.propertyPath);

            for (int i = 0; i < pathParts.Length; ++i)
            {
                string fieldName = pathParts[i];

                if (string.Equals(fieldName, "Array", StringComparison.Ordinal))
                {
                    // Move to "data[i]"
                    ++i;
                    if (pathParts.Length <= i)
                    {
                        break;
                    }

                    if (!TryParseArrayIndex(pathParts[i], out int index))
                    {
                        // Unexpected, die
                        fieldInfo = null;
                        return null;
                    }
                    obj = GetElementAtIndex(obj, index);
                    type = obj?.GetType();
                    continue;
                }

                resolvedField = type?.GetField(
                    fieldName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
                );
                if (resolvedField == null)
                {
                    fieldInfo = null;
                    return null;
                }

                // Move deeper into the object tree
                obj = resolvedField.GetValue(obj);
                type = resolvedField.FieldType;
            }

            fieldInfo = resolvedField;
            return obj;
        }

        private static readonly Dictionary<string, int> ArrayIndexCache = new(
            StringComparer.Ordinal
        );

        private static bool TryParseArrayIndex(string dataField, out int index)
        {
            if (ArrayIndexCache.TryGetValue(dataField, out int cachedIndex))
            {
                index = cachedIndex;
                return cachedIndex >= 0;
            }

            if (
                dataField.StartsWith("data[", StringComparison.Ordinal)
                && dataField.EndsWith("]", StringComparison.Ordinal)
            )
            {
                ReadOnlySpan<char> span = dataField.AsSpan(5, dataField.Length - 6);
                if (int.TryParse(span, out int parsedIndex))
                {
                    ArrayIndexCache[dataField] = parsedIndex;
                    index = parsedIndex;
                    return true;
                }
            }

            ArrayIndexCache[dataField] = -1;
            index = -1;
            return false;
        }

        private static object GetElementAtIndex(object obj, int index)
        {
            if (obj is System.Collections.IList list && index >= 0 && index < list.Count)
            {
                return list[index];
            }
            return null;
        }
    }
#endif
}
