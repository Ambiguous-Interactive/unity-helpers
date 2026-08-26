// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Runtime.CompilerServices;
    using System.Text.Json.Serialization;
    using Core.Extension;
    using UnityEngine;

    /// <summary>
    /// Represents a dynamic numeric attribute that supports temporary modifications through effects.
    /// Attributes maintain a base value and automatically calculate a current value by applying all active modifications.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides a flexible system for game attributes (like health, speed, damage, etc.) that can be
    /// temporarily or permanently modified. Modifications are applied in a specific order based on their action type:
    /// Addition, then Multiplication, then Override.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Create an attribute with base value of 100
    /// Attribute health = new Attribute(100f);
    ///
    /// // Apply a modification (e.g., +20 health from a buff)
    /// health.ApplyAttributeModification(new AttributeModification
    /// {
    ///     action = ModificationAction.Addition,
    ///     value = 20f
    /// }, effectHandle);
    ///
    /// // Current value is now 120
    /// float currentHealth = health.CurrentValue;
    /// </code>
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class Attribute
        : IEquatable<Attribute>,
            IEquatable<float>,
            IComparable<Attribute>,
            IComparable<float>
    {
        /// <summary>
        /// Gets the current calculated value of the attribute, including all active modifications.
        /// This value is cached and recalculated only when modifications change.
        /// </summary>
        /// <value>The current value after applying all modifications to the base value.</value>
        public float CurrentValue
        {
            get
            {
                // Unity writes _baseValue straight into the field on every deserialization -- an
                // Inspector edit, a prefab apply, an undo -- without running any code that could
                // invalidate the cache. Equals rather than == so a NaN base value still matches the
                // NaN it was calculated from instead of recalculating on every read.
                if (_currentValueCalculated && _calculatedFromBaseValue.Equals(_baseValue))
                {
                    return _currentValue;
                }

                CalculateCurrentValue();
                return _currentValue;
            }
        }

        /// <summary>
        /// Gets the base value of the attribute before any modifications are applied.
        /// </summary>
        /// <value>The unmodified base value.</value>
        public float BaseValue => _baseValue;

        [SerializeField]
        internal float _baseValue;

        [SerializeField]
        private float _currentValue;

        private bool _currentValueCalculated;

        private float _calculatedFromBaseValue;

        private readonly Dictionary<EffectHandle, List<AttributeModification>> _modifications =
            new();

        /// <summary>
        /// Initializes a new instance of the <see cref="Attribute"/> class with a base value of 0.
        /// </summary>
        public Attribute()
            : this(0) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="Attribute"/> class with the specified base value.
        /// </summary>
        /// <param name="value">The base value for this attribute.</param>
        public Attribute(float value)
        {
            _baseValue = value;
            _currentValueCalculated = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Attribute"/> class for JSON deserialization.
        /// </summary>
        /// <param name="baseValue">The base value for this attribute.</param>
        /// <param name="currentValue">The cached current value.</param>
        /// <remarks>
        /// <paramref name="currentValue"/> is taken as already calculated. Active modifications are
        /// not serialized, so recalculating on load would report the base value for an attribute
        /// that was written while buffed. Call <see cref="ClearCache"/> after deserializing if the
        /// caller intends to rebuild modifications and wants the value to follow them.
        /// </remarks>
        [JsonConstructor]
        public Attribute(float baseValue, float currentValue)
        {
            _baseValue = baseValue;
            _currentValue = currentValue;
            _currentValueCalculated = true;
            _calculatedFromBaseValue = baseValue;
        }

        /// <summary>
        /// Recalculates the current value by applying all active modifications to the base value.
        /// Modifications are sorted and applied in order: Addition, Multiplication, then Override.
        /// </summary>
        internal void CalculateCurrentValue()
        {
            float calculatedValue = _baseValue;
            if (0 < _modifications.Count)
            {
                // The Addition pass has to visit every modification anyway, so it reports which
                // other actions are present and the other two passes are skipped when they would
                // find nothing. Most effects use one action, so this is usually one traversal
                // rather than three: measured 0.464 us -> 0.171 us for three handles of two
                // additions, on 6000.4.6f1 (#529).
                //
                // The passes stay separate and in this order. Addition, Multiplication and
                // Override are not interchangeable, and a single pass accumulating them would
                // also change the ORDER of the float additions, which changes their result.
                ApplyModificationsInOrder(
                    ModificationAction.Addition,
                    ref calculatedValue,
                    out bool hasMultiplication,
                    out bool hasOverride
                );
                if (hasMultiplication)
                {
                    ApplyModificationsInOrder(
                        ModificationAction.Multiplication,
                        ref calculatedValue,
                        out _,
                        out _
                    );
                }

                if (hasOverride)
                {
                    ApplyModificationsInOrder(
                        ModificationAction.Override,
                        ref calculatedValue,
                        out _,
                        out _
                    );
                }
            }

            _currentValue = calculatedValue;
            _currentValueCalculated = true;
            _calculatedFromBaseValue = _baseValue;
        }

        /// <summary>
        /// Implicitly converts an Attribute to its current float value.
        /// </summary>
        /// <param name="attribute">The attribute to convert.</param>
        /// <returns>The current value of the attribute.</returns>
        public static implicit operator float(Attribute attribute) => attribute.CurrentValue;

        /// <summary>
        /// Implicitly converts a float value to an Attribute with that base value.
        /// </summary>
        /// <param name="value">The base value for the attribute.</param>
        /// <returns>A new Attribute with the specified base value.</returns>
        public static implicit operator Attribute(float value) => new(value);

        /// <summary>
        /// Applies a temporary additive modification to the attribute.
        /// </summary>
        /// <param name="value">The amount to add to the attribute's calculated value.</param>
        /// <returns>
        /// An effect handle that can later be supplied to <see cref="RemoveAttributeModification(EffectHandle)"/>
        /// to revoke this addition.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="value"/> is not a finite number.
        /// </exception>
        public EffectHandle Add(float value)
        {
            ValidateInput(value);

            EffectHandle handle = EffectHandle.CreateInstanceInternal();
            AttributeModification modification = new()
            {
                action = ModificationAction.Addition,
                value = value,
            };
            ApplyAttributeModification(modification, handle);
            return handle;
        }

        /// <summary>
        /// Applies a temporary subtractive modification to the attribute.
        /// </summary>
        /// <param name="value">The amount to subtract from the attribute's calculated value.</param>
        /// <returns>
        /// An effect handle that can later be supplied to <see cref="RemoveAttributeModification(EffectHandle)"/>
        /// to revoke this subtraction.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="value"/> is not a finite number.
        /// </exception>
        public EffectHandle Subtract(float value)
        {
            ValidateInput(value);

            EffectHandle handle = EffectHandle.CreateInstanceInternal();
            AttributeModification modification = new()
            {
                action = ModificationAction.Addition,
                // Subtraction is represented as a negative additive modifier to preserve modifier ordering.
                value = -value,
            };
            ApplyAttributeModification(modification, handle);
            return handle;
        }

        /// <summary>
        /// Applies a temporary division-based modification to the attribute.
        /// </summary>
        /// <param name="value">
        /// The divisor that will be applied to the attribute's calculated value.
        /// </param>
        /// <returns>
        /// An effect handle that can later be supplied to <see cref="RemoveAttributeModification(EffectHandle)"/>
        /// to revoke this division.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="value"/> is zero or not a finite number.
        /// </exception>
        public EffectHandle Divide(float value)
        {
            ValidateInput(value);

            if (value == 0f)
            {
                throw new ArgumentException("Cannot divide by zero.", nameof(value));
            }

            EffectHandle handle = EffectHandle.CreateInstanceInternal();
            AttributeModification modification = new()
            {
                action = ModificationAction.Multiplication,
                // Apply division by multiplying by the reciprocal to maintain multiplication ordering guarantees.
                value = 1f / value,
            };
            ApplyAttributeModification(modification, handle);
            return handle;
        }

        /// <summary>
        /// Applies a temporary multiplicative modification to the attribute.
        /// </summary>
        /// <param name="value">The multiplier to apply to the attribute's calculated value.</param>
        /// <returns>
        /// An effect handle that can later be supplied to <see cref="RemoveAttributeModification(EffectHandle)"/>
        /// to revoke this multiplication.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="value"/> is not a finite number.
        /// </exception>
        public EffectHandle Multiply(float value)
        {
            ValidateInput(value);

            EffectHandle handle = EffectHandle.CreateInstanceInternal();
            AttributeModification modification = new()
            {
                action = ModificationAction.Multiplication,
                value = value,
            };
            ApplyAttributeModification(modification, handle);
            return handle;
        }

        /// <summary>
        /// Clears the cached current value, forcing it to be recalculated on next access.
        /// </summary>
        /// <remarks>
        /// Deserialization does not call this, and the two paths differ deliberately. Unity restores
        /// only the two <c>[SerializeField]</c> values and leaves the "already calculated" flag
        /// false, so the first read recalculates from the base value. JSON goes through the
        /// <see cref="Attribute(float, float)"/> constructor, which keeps the value it was given --
        /// modifications are not serialized, so recalculating there would silently drop every
        /// active effect's contribution rather than preserve the value that was written.
        /// </remarks>
        public void ClearCache()
        {
            _currentValueCalculated = false;
        }

        private void ApplyModificationsInOrder(
            ModificationAction action,
            ref float value,
            out bool hasMultiplication,
            out bool hasOverride
        )
        {
            hasMultiplication = false;
            hasOverride = false;
            foreach (
                KeyValuePair<EffectHandle, List<AttributeModification>> entry in _modifications
            )
            {
                List<AttributeModification> modifications = entry.Value;
                for (int index = 0; index < modifications.Count; index++)
                {
                    AttributeModification modification = modifications[index];
                    ModificationAction modificationAction = modification.action;
                    if (modificationAction == action)
                    {
                        ApplyAttributeModification(modification, ref value);
                        continue;
                    }

                    switch (modificationAction)
                    {
                        case ModificationAction.Multiplication:
                        {
                            hasMultiplication = true;
                            break;
                        }
                        case ModificationAction.Override:
                        {
                            hasOverride = true;
                            break;
                        }
                    }
                }
            }
        }

        private static void ValidateInput(float value, [CallerMemberName] string caller = null)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException(
                    $"Cannot {caller?.ToLowerInvariant()} by infinity or NaN.",
                    nameof(value)
                );
            }
        }

        /// <summary>
        /// Applies an attribute modification to this attribute.
        /// If a handle is provided, the modification is temporary and can be removed.
        /// If no handle is provided, the modification is permanent and applied directly to the base value.
        /// </summary>
        /// <param name="attributeModification">The modification to apply.</param>
        /// <param name="handle">Optional effect handle for temporary modifications. If null, the modification is permanent.</param>
        public void ApplyAttributeModification(
            AttributeModification attributeModification,
            EffectHandle? handle = null
        )
        {
            // If we don't have a handle, then this is an instant effect, apply it to the base value.
            if (!handle.HasValue)
            {
                ApplyAttributeModification(attributeModification, ref _baseValue);
            }
            else
            {
                _modifications.GetOrAdd(handle.Value).Add(attributeModification);
            }

            CalculateCurrentValue();
        }

        /// <summary>
        /// Removes all modifications associated with the specified effect handle.
        /// </summary>
        /// <param name="handle">The effect handle whose modifications should be removed.</param>
        /// <returns><c>true</c> if modifications were found and removed; otherwise, <c>false</c>.</returns>
        public bool RemoveAttributeModification(EffectHandle handle)
        {
            bool removed = _modifications.Remove(handle);
            if (removed)
            {
                CalculateCurrentValue();
            }

            return removed;
        }

        private static void ApplyAttributeModification(
            AttributeModification attributeModification,
            ref float value
        )
        {
            switch (attributeModification.action)
            {
                case ModificationAction.Addition:
                {
                    value += attributeModification.value;
                    break;
                }
                case ModificationAction.Multiplication:
                {
                    value *= attributeModification.value;
                    break;
                }
                case ModificationAction.Override:
                {
                    value = attributeModification.value;
                    break;
                }
                default:
                {
                    throw new InvalidEnumArgumentException(
                        nameof(attributeModification.action),
                        (int)attributeModification.action,
                        typeof(ModificationAction)
                    );
                }
            }
        }

        /// <summary>
        /// Determines whether this attribute is equal to another attribute by comparing their current values.
        /// </summary>
        /// <param name="other">The attribute to compare with.</param>
        /// <returns><c>true</c> if the current values are equal; otherwise, <c>false</c>.</returns>
        public bool Equals(Attribute other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return other != null && CurrentValue.Equals(other.CurrentValue);
        }

        /// <summary>
        /// Compares this attribute to another attribute based on their current values.
        /// </summary>
        /// <param name="other">The attribute to compare with.</param>
        /// <returns>
        /// A value less than 0 if this attribute is less than <paramref name="other"/>;
        /// 0 if they are equal;
        /// a value greater than 0 if this attribute is greater than <paramref name="other"/>.
        /// </returns>
        public int CompareTo(Attribute other)
        {
            if (ReferenceEquals(this, other))
            {
                return 0;
            }
            return other == null ? 1 : CurrentValue.CompareTo(other.CurrentValue);
        }

        /// <summary>
        /// Compares this attribute's current value to a float value.
        /// </summary>
        /// <param name="other">The float value to compare with.</param>
        /// <returns>
        /// A value less than 0 if this attribute is less than <paramref name="other"/>;
        /// 0 if they are equal;
        /// a value greater than 0 if this attribute is greater than <paramref name="other"/>.
        /// </returns>
        public int CompareTo(float other)
        {
            return CurrentValue.CompareTo(other);
        }

        /// <summary>
        /// Determines whether this attribute is equal to the specified object.
        /// Supports comparison with Attribute and numeric types.
        /// </summary>
        /// <param name="other">The object to compare with.</param>
        /// <returns><c>true</c> if the values are equal; otherwise, <c>false</c>.</returns>
        public override bool Equals(object other)
        {
            switch (other)
            {
                case Attribute attribute:
                {
                    return Equals(attribute);
                }
                case float attribute:
                {
                    return Equals(attribute);
                }
                case double attribute:
                {
                    return Equals((float)attribute);
                }
                case int attribute:
                {
                    return Equals((float)attribute);
                }
                case long attribute:
                {
                    return Equals((float)attribute);
                }
                case short attribute:
                {
                    return Equals((float)attribute);
                }
                case uint attribute:
                {
                    return Equals((float)attribute);
                }
                case ulong attribute:
                {
                    return Equals((float)attribute);
                }
                case ushort attribute:
                {
                    return Equals((float)attribute);
                }
                case byte attribute:
                {
                    return Equals((float)attribute);
                }
                case sbyte attribute:
                {
                    return Equals((float)attribute);
                }
                default:
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Determines whether this attribute's current value equals the specified float value.
        /// </summary>
        /// <param name="other">The float value to compare with.</param>
        /// <returns><c>true</c> if the values are equal; otherwise, <c>false</c>.</returns>
        public bool Equals(float other)
        {
            return CurrentValue.Equals(other);
        }

        /// <summary>
        /// Returns the hash code for this attribute.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode()
        {
            // ReSharper disable once BaseObjectGetHashCodeCallInGetHashCode
            return base.GetHashCode();
        }

        /// <summary>
        /// Converts this attribute to its string representation using the current value.
        /// </summary>
        /// <returns>A string representation of the current value in invariant culture format.</returns>
        public override string ToString()
        {
            return ((float)this).ToString(CultureInfo.InvariantCulture);
        }
    }
}
