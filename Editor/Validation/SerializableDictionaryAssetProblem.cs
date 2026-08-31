// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;

    /// <summary>
    /// The state an authored <c>SerializableDictionary</c> is in when it will not load as authored.
    /// </summary>
    public enum SerializableDictionaryAssetProblem
    {
        /// <summary>Unused.</summary>
        [Obsolete("A finding always names a problem.")]
        Unknown = 0,

        /// <summary>
        /// Keys were written and no array carries the values, so the asset records what the mapping
        /// is about and nothing about what it maps to. Every lookup misses.
        /// </summary>
        ValuesDropped = 1,

        /// <summary>
        /// The keys and the array carrying the values are different lengths, so the pairing is not
        /// recoverable and the dictionary loads empty.
        /// </summary>
        ValueCountMismatch = 2,

        /// <summary>
        /// A real key sits beside a value that names no object. The asset is well-formed, so
        /// <c>TryGetValue</c> returns <c>true</c> with a null and every caller has to remember a
        /// second check.
        /// </summary>
        NullValueBesideKey = 3,
    }
#endif
}
