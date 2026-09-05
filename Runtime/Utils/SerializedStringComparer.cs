// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An <see cref="IEqualityComparer{T}"/> for strings whose comparison mode is a serialized
    /// field, so it can be authored in the Inspector and stored in an asset.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// [SerializeField]
    /// private SerializedStringComparer _keyComparer = new(SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase);
    ///
    /// private Dictionary<string, Item> _byName;
    /// private void Awake() => _byName = new Dictionary<string, Item>(_keyComparer);
    /// ]]></code>
    /// </example>
    [Serializable]
    public sealed class SerializedStringComparer : IEqualityComparer<string>
    {
        /// <summary>The comparison rule this instance applies.</summary>
        /// <remarks>
        /// <para><b>Changing this after a dictionary has been built with this comparer breaks that
        /// dictionary.</b> Its keys are already in buckets chosen by the old rule, so a lookup under
        /// the new one hashes to a bucket the key is not in and the entry reads as absent. Nothing
        /// throws; the data is simply unreachable.</para>
        /// <para>Call <see cref="Freeze"/> once the comparer has been handed to a collection and
        /// later writes to this field stop taking effect. A future major version makes the backing
        /// state immutable outright.</para>
        /// </remarks>
        public StringCompareMode compareMode = StringCompareMode.Ordinal;

        [NonSerialized]
        private StringComparer _resolved;

        [NonSerialized]
        private StringCompareMode _resolvedMode;

        [NonSerialized]
        private bool _frozen;

        /// <summary>Creates a comparer that compares by code unit.</summary>
        public SerializedStringComparer() { }

        /// <summary>Creates a comparer that applies the given rule.</summary>
        /// <param name="compareMode">The comparison rule to apply.</param>
        public SerializedStringComparer(StringCompareMode compareMode)
        {
            this.compareMode = compareMode;
        }

        /// <summary>Determines whether two strings are equal under <see cref="compareMode"/>.</summary>
        /// <param name="x">The left string.</param>
        /// <param name="y">The right string.</param>
        /// <returns><c>true</c> when the strings are equal.</returns>
        public bool Equals(string x, string y)
        {
            return Resolve().Equals(x, y);
        }

        /// <summary>Returns a hash consistent with <see cref="Equals(string, string)"/>.</summary>
        /// <param name="obj">The string to hash.</param>
        /// <returns>The hash, or zero when <paramref name="obj"/> is null.</returns>
        public int GetHashCode(string obj)
        {
            if (obj == null)
            {
                return 0;
            }

            return Resolve().GetHashCode(obj);
        }

        /// <summary>
        /// Pins the rule this comparer currently applies, so a later write to
        /// <see cref="compareMode"/> cannot re-bucket a collection that was already built with it.
        /// The first call pins the rule and every later call is a no-op, so re-freezing after a write
        /// to <see cref="compareMode"/> cannot quietly adopt the new value.
        /// </summary>
        /// <returns>This comparer, so a collection can be constructed in one expression.</returns>
        /// <example>
        /// <code><![CDATA[
        /// _byName = new Dictionary<string, Item>(_keyComparer.Freeze());
        /// ]]></code>
        /// </example>
        public SerializedStringComparer Freeze()
        {
            if (_frozen)
            {
                return this;
            }

            _resolved = ResolveMode(compareMode);
            _resolvedMode = compareMode;
            _frozen = true;
            return this;
        }

        /// <summary>Whether <see cref="Freeze"/> has pinned this comparer's rule.</summary>
        public bool IsFrozen => _frozen;

        private StringComparer Resolve()
        {
            if (_frozen)
            {
                return _resolved;
            }

            if (_resolved != null && _resolvedMode == compareMode)
            {
                return _resolved;
            }

            _resolved = ResolveMode(compareMode);
            _resolvedMode = compareMode;
            return _resolved;
        }

        // Unknown serialized modes use the field default instead of throwing during dictionary lookup.
        private static StringComparer ResolveMode(StringCompareMode compareMode)
        {
            return compareMode switch
            {
                StringCompareMode.OrdinalIgnoreCase => StringComparer.OrdinalIgnoreCase,
                StringCompareMode.CurrentCulture => StringComparer.CurrentCulture,
                StringCompareMode.CurrentCultureIgnoreCase =>
                    StringComparer.CurrentCultureIgnoreCase,
                StringCompareMode.InvariantCulture => StringComparer.InvariantCulture,
                StringCompareMode.InvariantCultureIgnoreCase =>
                    StringComparer.InvariantCultureIgnoreCase,
                _ => StringComparer.Ordinal,
            };
        }

        /// <summary>The comparison rule a <see cref="SerializedStringComparer"/> applies.</summary>
        public enum StringCompareMode
        {
            /// <summary>Compares by code unit. The default, and the right choice for identifiers.</summary>
            Ordinal = 0,

            /// <summary>Compares by code unit, ignoring case.</summary>
            OrdinalIgnoreCase = 1,

            /// <summary>Compares using the current culture's linguistic rules.</summary>
            CurrentCulture = 2,

            /// <summary>Compares using the current culture's linguistic rules, ignoring case.</summary>
            CurrentCultureIgnoreCase = 3,

            /// <summary>Compares using culture-independent linguistic rules.</summary>
            InvariantCulture = 4,

            /// <summary>Compares using culture-independent linguistic rules, ignoring case.</summary>
            InvariantCultureIgnoreCase = 5,
        }
    }
}
