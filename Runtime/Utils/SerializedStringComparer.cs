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

        /// <summary>The comparison rule this instance applies.</summary>
        public StringCompareMode compareMode = StringCompareMode.Ordinal;

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

        /*
            A serialized field holds whatever the asset holds: an enum member that was renumbered,
            a hand-edited YAML value, a value from a newer version of this package. Throwing here
            would surface as a crash at dictionary-lookup time, so an unrecognized mode falls back
            to the field's own default.
        */
        private StringComparer Resolve()
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
    }
}
