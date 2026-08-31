// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Runtime.CompilerServices;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Key used to group effect handles for stacking decisions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Effect stacking is driven by <see cref="EffectStackGroup"/>. For reference-based grouping, the
    /// key stores the effect asset; for custom grouping it stores a string key. Equality and hashing are
    /// tuned to avoid allocations and rely on reference equality for effect assets and ordinal comparison
    /// for custom keys.
    /// </para>
    /// <para>
    /// Keys are created via the factory methods and are used by <see cref="EffectHandler"/> to bucket
    /// active <see cref="EffectHandle"/> instances when applying stacking policies.
    /// </para>
    /// </remarks>
    internal readonly struct EffectStackKey : IEquatable<EffectStackKey>
    {
        private readonly EffectStackGroup _group;
        private readonly AttributeEffect _effect;
        private readonly string _customKey;

        private EffectStackKey(EffectStackGroup group, AttributeEffect effect, string customKey)
        {
            _group = group;
            _effect = effect;
            _customKey = customKey;
        }

        /// <summary>
        /// Creates a stack key that groups by effect asset reference.
        /// </summary>
        /// <param name="effect">The effect asset to use for grouping.</param>
        /// <returns>A key representing the reference-based stack group.</returns>
        /// <example>
        /// <code>
        /// EffectStackKey key = EffectStackKey.CreateReference(poisonEffect);
        /// // All handles from the same poisonEffect asset share a stack.
        /// </code>
        /// </example>
        public static EffectStackKey CreateReference(AttributeEffect effect)
        {
            return new EffectStackKey(EffectStackGroup.Reference, effect, null);
        }

        /// <summary>
        /// Creates a stack key that groups by a custom string identifier.
        /// </summary>
        /// <param name="customKey">Identifier used to group otherwise distinct effects.</param>
        /// <returns>A key representing the custom stack group.</returns>
        /// <example>
        /// <code>
        /// EffectStackKey key = EffectStackKey.CreateCustom("DamageOverTime");
        /// // Different assets can share the same custom key to stack together.
        /// </code>
        /// </example>
        public static EffectStackKey CreateCustom(string customKey)
        {
            return new EffectStackKey(EffectStackGroup.CustomKey, null, customKey);
        }

        /// <summary>
        /// Compares this key to another for value equality.
        /// </summary>
        /// <param name="other">The other key to compare.</param>
        /// <returns><c>true</c> when both keys represent the same stack group; otherwise, <c>false</c>.</returns>
        public bool Equals(EffectStackKey other)
        {
            if (_group != other._group)
            {
                return false;
            }

            return _group switch
            {
                EffectStackGroup.Reference => ReferenceEquals(_effect, other._effect),
                EffectStackGroup.CustomKey => string.Equals(
                    _customKey,
                    other._customKey,
                    StringComparison.Ordinal
                ),
                _ => false,
            };
        }

        /// <summary>
        /// Determines whether this key is equal to another object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns><c>true</c> if the object is an <see cref="EffectStackKey"/> representing the same group; otherwise, <c>false</c>.</returns>
        public override bool Equals(object obj)
        {
            return obj is EffectStackKey other && Equals(other);
        }

        /// <summary>
        /// Generates a hash code consistent with <see cref="Equals(EffectStackKey)"/>.
        /// </summary>
        /// <remarks>
        /// A reference key hashes the managed identity of the effect, because that is what its
        /// equality compares. Hashing the effect through Unity's own <c>GetHashCode</c> would move
        /// the key the moment the asset was destroyed, stranding every handle already bucketed
        /// under it.
        /// </remarks>
        /// <returns>A hash code for use in dictionaries and sets.</returns>
        public override int GetHashCode()
        {
            return _group switch
            {
                EffectStackGroup.Reference => Objects.HashCode(
                    _group,
                    RuntimeHelpers.GetHashCode(_effect)
                ),
                EffectStackGroup.CustomKey => Objects.HashCode(
                    _group,
                    _customKey != null ? StringComparer.Ordinal.GetHashCode(_customKey) : 0
                ),
                _ => Objects.HashCode(_group),
            };
        }

        /// <summary>
        /// Determines whether two keys are equal.
        /// </summary>
        /// <param name="left">First key to compare.</param>
        /// <param name="right">Second key to compare.</param>
        /// <returns><c>true</c> if both keys represent the same stack group; otherwise, <c>false</c>.</returns>
        public static bool operator ==(EffectStackKey left, EffectStackKey right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two keys are not equal.
        /// </summary>
        /// <param name="left">First key to compare.</param>
        /// <param name="right">Second key to compare.</param>
        /// <returns><c>true</c> if the keys differ; otherwise, <c>false</c>.</returns>
        public static bool operator !=(EffectStackKey left, EffectStackKey right)
        {
            return !(left == right);
        }
    }
}
