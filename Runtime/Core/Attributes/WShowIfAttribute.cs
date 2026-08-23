// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Scripting;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Comparison strategy applied when resolving <see cref="WShowIfAttribute"/> visibility.
    /// </summary>
    public enum WShowIfComparison
    {
        /// <summary>
        /// Reserved value. Prefer selecting a specific comparison mode.
        /// </summary>
        [Obsolete("WShowIfComparison.Unknown is reserved. Choose an explicit comparison mode.")]
        Unknown = 0,

        /// <summary>
        /// Shows the property when the condition evaluates to true or matches the provided expected values.
        /// </summary>
        Equal = 1,

        /// <summary>
        /// Shows the property when the condition evaluates to false or does not match the provided expected values.
        /// </summary>
        NotEqual = 2,

        /// <summary>
        /// Shows the property when the condition is greater than the expected value (numbers and comparable types).
        /// </summary>
        GreaterThan = 3,

        /// <summary>
        /// Shows the property when the condition is greater than or equal to the expected value (numbers and comparable types).
        /// </summary>
        GreaterThanOrEqual = 4,

        /// <summary>
        /// Shows the property when the condition is less than the expected value (numbers and comparable types).
        /// </summary>
        LessThan = 5,

        /// <summary>
        /// Shows the property when the condition is less than or equal to the expected value (numbers and comparable types).
        /// </summary>
        LessThanOrEqual = 6,

        /// <summary>
        /// Shows the property when the condition resolves to null (supports <see cref="UnityEngine.Object"/> semantics).
        /// </summary>
        IsNull = 7,

        /// <summary>
        /// Shows the property when the condition resolves to a non-null value (supports <see cref="UnityEngine.Object"/> semantics).
        /// </summary>
        IsNotNull = 8,

        /// <summary>
        /// Shows the property when the condition resolves to a null or empty string, or an empty collection.
        /// </summary>
        IsNullOrEmpty = 9,

        /// <summary>
        /// Shows the property when the condition resolves to a non-empty string or collection.
        /// </summary>
        IsNotNullOrEmpty = 10,
    }

    /// <summary>
    /// Conditionally hides or shows a serialized field based on the value of another property or field on the same object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="WShowIfAttribute"/> works with booleans, numbers, enums, strings, <see cref="UnityEngine.Object"/> references, and even custom comparable types.
    /// You can specify comparison strategies such as <see cref="WShowIfComparison.GreaterThan"/> or <see cref="WShowIfComparison.IsNullOrEmpty"/> and
    /// optionally pass explicit expected values for equality checks.
    /// </para>
    /// <para>
    /// When multiple conditions are required, stack the attribute instance per rule or combine it with other inspector helpers such as
    /// <see cref="WGroupAttribute"/> to keep complex editors manageable.
    /// </para>
    /// </remarks>
    /// <example>
    /// Basic boolean toggle:
    /// <code>
    /// public bool advancedMode;
    ///
    /// [WShowIf(nameof(advancedMode))]
    /// public float advancedSetting;
    /// </code>
    /// Numerical comparisons:
    /// <code>
    /// public int upgradeLevel;
    ///
    /// [WShowIf(nameof(upgradeLevel), WShowIfComparison.GreaterThanOrEqual, 3)]
    /// public Ability ultimateAbility;
    /// </code>
    /// Reference checks and inverse usage:
    /// <code>
    /// public GameObject overridePrefab;
    ///
    /// [WShowIf(nameof(overridePrefab), WShowIfComparison.IsNull)]
    /// public GameObject fallbackPrefab;
    ///
    /// [WShowIf(nameof(overridePrefab), inverse: true)]
    /// public AbilityOverrides overrideSettings;
    /// </code>
    /// </example>
    [Preserve]
    // Declared rather than inherited, so the accepted targets are this package's decision instead
    // of drifting with whatever UnityEngine.PropertyAttribute declares in a given editor version.
    // Property is included because a property's data can genuinely be serialized -- through
    // [field: SerializeField], where the attribute lands on the backing field, and through Odin,
    // which draws a property directly. It is NOT an invitation to decorate a computed property
    // nothing serializes: that reaches no drawer, and WUH003 reports it.
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = true
    )]
    public sealed class WShowIfAttribute : PropertyAttribute
    {
        /// <summary>
        /// Gets the name of the field or property that determines visibility.
        /// </summary>
        public readonly string conditionField;

        /// <summary>
        /// Gets a value indicating whether the visibility rule should be inverted.
        /// </summary>
        public readonly bool inverse;

        /// <summary>
        /// Gets the comparison strategy that should be applied to the condition value.
        /// </summary>
        public readonly WShowIfComparison comparison;

        /// <summary>
        /// Gets the explicit values that must match the condition field in order for the target to display.
        /// </summary>
        public object[] expectedValues;

        /// <summary>
        /// Configures a conditional visibility rule for an inspector field.
        /// </summary>
        /// <param name="conditionField">Name of the member used for evaluation.</param>
        /// <param name="expectedValues">Optional explicit values that should evaluate as visible.</param>
        public WShowIfAttribute(string conditionField, params object[] expectedValues)
            : this(conditionField, false, WShowIfComparison.Equal, expectedValues) { }

        /// <summary>
        /// Configures a conditional visibility rule with explicit inversion for an inspector field.
        /// </summary>
        /// <param name="conditionField">Name of the member used for evaluation.</param>
        /// <param name="inverse">Set to <c>true</c> to flip the visibility result.</param>
        /// <param name="expectedValues">Optional explicit values that should evaluate as visible.</param>
        public WShowIfAttribute(string conditionField, bool inverse, params object[] expectedValues)
            : this(conditionField, inverse, WShowIfComparison.Equal, expectedValues) { }

        /// <summary>
        /// Configures a conditional visibility rule with a specific comparison mode.
        /// </summary>
        /// <param name="conditionField">Name of the member used for evaluation.</param>
        /// <param name="comparison">Comparison strategy applied to the condition value.</param>
        /// <param name="expectedValues">Optional explicit values that should evaluate as visible.</param>
        public WShowIfAttribute(
            string conditionField,
            WShowIfComparison comparison,
            params object[] expectedValues
        )
            : this(conditionField, false, comparison, expectedValues) { }

        /// <summary>
        /// Configures a conditional visibility rule for an inspector field.
        /// </summary>
        /// <param name="conditionField">Name of the member used for evaluation.</param>
        /// <param name="inverse">Set to <c>true</c> to flip the visibility result.</param>
        /// <param name="comparison">Comparison strategy applied to the condition value.</param>
        /// <param name="expectedValues">Optional explicit values that should evaluate as visible.</param>
        public WShowIfAttribute(
            string conditionField,
            bool inverse,
            WShowIfComparison comparison,
            params object[] expectedValues
        )
        {
            if (string.IsNullOrEmpty(conditionField))
            {
                throw new ArgumentException(
                    "Condition member name cannot be null or empty.",
                    nameof(conditionField)
                );
            }

            this.conditionField = conditionField;
            this.inverse = inverse;
            this.comparison = comparison;
            if (expectedValues == null || expectedValues.Length == 0)
            {
                this.expectedValues = Array.Empty<object>();
            }
            else
            {
                using (Buffers<object>.List.Get(out List<object> buffer))
                {
                    buffer.AddRange(expectedValues);
                    this.expectedValues = buffer.ToArray();
                }
            }
        }
    }
}
