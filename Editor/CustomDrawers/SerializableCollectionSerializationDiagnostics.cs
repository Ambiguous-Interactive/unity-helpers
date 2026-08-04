// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
    using UnityEditor;

    /// <summary>
    /// Detects serialized collection backing arrays that Unity silently refuses to write, so the
    /// drawers can report them instead of drawing rows that persist nothing.
    /// </summary>
    /// <remarks>
    /// Unity does not serialize a nested collection. A <c>SerializableDictionary&lt;TKey, List&lt;T&gt;&gt;</c>
    /// declares its backing values field as <c>List&lt;T&gt;[]</c>, which Unity drops entirely, while the
    /// parallel <c>TKey[]</c> keys field survives. The asset therefore records keys and no values, the
    /// Inspector keeps accepting entries, and every runtime lookup comes back empty with no error.
    /// A missing <see cref="SerializedProperty"/> is the only observable signal: Unity returns null from
    /// <see cref="SerializedProperty.FindPropertyRelative"/> for a field it never serialized.
    /// </remarks>
    internal static class SerializableCollectionSerializationDiagnostics
    {
        /// <summary>
        /// Builds the Inspector error shown for a dictionary whose values array Unity dropped.
        /// </summary>
        /// <param name="displayName">The dictionary field's Inspector display name.</param>
        /// <returns>An actionable error message naming the supported alternative.</returns>
        internal static string BuildDroppedDictionaryValuesMessage(string displayName)
        {
            return $"'{displayName}' cannot round-trip through Unity serialization: Unity refuses "
                + "this value type -- most often a nested collection such as List<T> -- so the "
                + "values array is never written and every lookup returns nothing. Wrap the value "
                + "type in a [Serializable] class, or declare the dictionary as "
                + "SerializableDictionary<TKey, TValue, TValueCache> with a "
                + "SerializableDictionary.Cache<TValue> subclass as TValueCache.";
        }

        /// <summary>
        /// Builds the Inspector error shown for a set whose items array Unity dropped.
        /// </summary>
        /// <param name="displayName">The set field's Inspector display name.</param>
        /// <returns>An actionable error message naming the supported alternative.</returns>
        internal static string BuildDroppedSetItemsMessage(string displayName)
        {
            return $"'{displayName}' cannot round-trip through Unity serialization: Unity refuses "
                + "this element type -- most often a nested collection such as List<T> -- so the "
                + "items array is never written and the set is always empty at runtime. Wrap the "
                + "element type in a [Serializable] class.";
        }
    }
}
