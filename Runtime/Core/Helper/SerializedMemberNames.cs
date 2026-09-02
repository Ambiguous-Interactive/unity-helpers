// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Extension;
#if !SINGLE_THREADED
    using System.Collections.Concurrent;
#endif

    /// <summary>
    /// Translates between the name a member has in source and the name Unity serializes it under.
    /// </summary>
    /// <remarks>
    /// The two differ for exactly one construct, and it is a common one. An auto-property marked
    /// <c>[field: SerializeField]</c> is serialized through the compiler-generated backing field,
    /// whose name is <c>&lt;Speed&gt;k__BackingField</c> -- so
    /// <c>serializedObject.FindProperty("Speed")</c> finds nothing, and an attribute that names
    /// <c>"Speed"</c> as a condition or a value source resolves against nothing while looking
    /// entirely correct.
    /// <para>
    /// Nobody writes the mangled spelling by hand, so every lookup should try the source name first
    /// and this translation second.
    /// </para>
    /// </remarks>
    public static class SerializedMemberNames
    {
        private const string BackingFieldPrefix = "<";
        private const string BackingFieldSuffix = ">k__BackingField";

        /*
            Held rather than written at the call site: a method group in argument position builds a
            new delegate on every call, cache hits included, which is what WUH001 reports.
        */
        private static readonly Func<string, string> BackingFieldNameFactory =
            BuildBackingFieldName;

        /*
            One call site for both, because DictionaryExtensions.GetOrAdd already covers
            IDictionary -- and dispatches to ConcurrentDictionary's own overload when that is what it
            is handed. Only the field's type needs the define.
        */
#if !SINGLE_THREADED
        private static readonly ConcurrentDictionary<string, string> BackingFieldNames = new(
            StringComparer.Ordinal
        );
#else
        private static readonly Dictionary<string, string> BackingFieldNames = new(
            StringComparer.Ordinal
        );
#endif

        /// <summary>
        /// The field name Unity serializes an auto-property under.
        /// </summary>
        /// <param name="propertyName">Property name as written in source.</param>
        /// <returns>
        /// <c>&lt;propertyName&gt;k__BackingField</c>, or <paramref name="propertyName"/> unchanged
        /// when it is null, empty, or already a backing-field name.
        /// </returns>
        /// <remarks>
        /// Cached, because the result is used on inspector paint paths and the set of names a
        /// project uses is small and fixed.
        /// </remarks>
        public static string BackingFieldFor(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName) || IsBackingField(propertyName))
            {
                return propertyName;
            }

            return BackingFieldNames.GetOrAdd(propertyName, BackingFieldNameFactory);
        }

        /// <summary>
        /// Whether <paramref name="fieldName"/> is a compiler-generated auto-property backing field.
        /// </summary>
        /// <param name="fieldName">Field name to test.</param>
        /// <returns><c>true</c> when the name has the backing-field shape.</returns>
        public static bool IsBackingField(string fieldName)
        {
            return !string.IsNullOrEmpty(fieldName)
                && fieldName.StartsWith(BackingFieldPrefix, StringComparison.Ordinal)
                && fieldName.EndsWith(BackingFieldSuffix, StringComparison.Ordinal)
                && BackingFieldPrefix.Length + BackingFieldSuffix.Length < fieldName.Length;
        }

        /// <summary>
        /// Recovers the property name from a compiler-generated backing field name.
        /// </summary>
        /// <param name="fieldName">Field name, possibly a backing field.</param>
        /// <param name="propertyName">The property name when one was recovered.</param>
        /// <returns><c>true</c> when <paramref name="fieldName"/> was a backing field.</returns>
        /// <remarks>
        /// The <c>Substring</c> below cannot throw, and the guard above is what makes that true
        /// rather than the shape of the input: <see cref="IsBackingField"/> has already established
        /// that the name starts with the prefix, ends with the suffix, and is STRICTLY longer than
        /// the two combined -- so the offset is in range and the length is at least one. The length
        /// test is the load-bearing one; without it <c>"&lt;&gt;k__BackingField"</c> would ask for a
        /// zero-length name and anything shorter for a negative one.
        /// </remarks>
        public static bool TryGetPropertyName(string fieldName, out string propertyName)
        {
            if (!IsBackingField(fieldName))
            {
                propertyName = fieldName;
                return false;
            }

            propertyName = fieldName.Substring(
                BackingFieldPrefix.Length,
                fieldName.Length - BackingFieldPrefix.Length - BackingFieldSuffix.Length
            );
            return true;
        }

        private static string BuildBackingFieldName(string propertyName)
        {
            return BackingFieldPrefix + propertyName + BackingFieldSuffix;
        }
    }
}
