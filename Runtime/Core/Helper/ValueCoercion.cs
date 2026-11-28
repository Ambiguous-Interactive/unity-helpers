// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Globalization;

    /// <summary>Coerces loosely typed configuration values to a runtime target type.</summary>
    /// <remarks>
    /// Same-type values pass through. Strings parse with the invariant culture, enums accept
    /// declared names or integers, and other convertible inputs use <see cref="IConvertible"/>.
    /// Unsupported conversions return false and never throw.
    /// </remarks>
    public static class ValueCoercion
    {
        /// <summary>Tries to convert an input to the target type.</summary>
        /// <param name="target">The type to produce.</param>
        /// <param name="input">The value to convert. Null is accepted for reference and nullable targets.</param>
        /// <param name="value">The converted value, or null when the conversion fails.</param>
        /// <returns>Whether the input was converted.</returns>
        public static bool TryCoerce(Type target, object input, out object value)
        {
            if (target == null)
            {
                value = null;
                return false;
            }

            Type nullable = Nullable.GetUnderlyingType(target);
            Type effective = nullable ?? target;
            if (input == null)
            {
                value = null;
                return !target.IsValueType || nullable != null;
            }

            if (effective.IsInstanceOfType(input))
            {
                value = input;
                return true;
            }

            if (effective.IsEnum)
            {
                if (input is string name)
                {
                    if (EnumValues.TryParse(effective, name.AsSpan(), false, out value))
                    {
                        return true;
                    }

                    if (
                        long.TryParse(
                            name,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out long parsed
                        )
                    )
                    {
                        return EnumValues.TryFromInteger(effective, parsed, out value);
                    }

                    return false;
                }

                if (input is IConvertible)
                {
                    try
                    {
                        long numeric = Convert.ToInt64(input, CultureInfo.InvariantCulture);
                        return EnumValues.TryFromInteger(effective, numeric, out value);
                    }
                    catch (Exception exception)
                        when (exception is FormatException
                            || exception is InvalidCastException
                            || exception is OverflowException
                        )
                    {
                        value = null;
                        return false;
                    }
                }

                value = null;
                return false;
            }

            if (effective == typeof(string))
            {
                value = Convert.ToString(input, CultureInfo.InvariantCulture);
                return value != null;
            }

            if (effective == typeof(bool) && input is string text)
            {
                if (bool.TryParse(text, out bool parsed))
                {
                    value = parsed;
                    return true;
                }

                value = null;
                return false;
            }

            if (input is IConvertible)
            {
                try
                {
                    value = Convert.ChangeType(input, effective, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception exception)
                    when (exception is FormatException
                        || exception is InvalidCastException
                        || exception is OverflowException
                        || exception is ArgumentException
                    )
                {
                    value = null;
                    return false;
                }
            }

            value = null;
            return false;
        }
    }
}
