// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;

    /// <summary>
    /// Provides shared condition evaluation logic for WShowIf attribute drawers.
    /// </summary>
    /// <remarks>
    /// This utility class consolidates common code used by both the standard PropertyDrawer
    /// and the Odin Inspector drawer implementations of WShowIf. By centralizing these
    /// elements, we ensure consistent behavior and eliminate code duplication.
    /// </remarks>
    public static class ShowIfConditionEvaluator
    {
        /// <summary>
        /// Binding flags for resolving members on types.
        /// </summary>
        public const BindingFlags MemberBindingFlags =
            BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.FlattenHierarchy;

        private static readonly Dictionary<Type, MethodInfo> CompareToMethodCache = new();

        /// <summary>
        /// Tries to evaluate the condition and determine whether the property should be shown.
        /// </summary>
        /// <param name="conditionValue">The current value of the condition field.</param>
        /// <param name="showIf">The WShowIf attribute containing comparison settings.</param>
        /// <param name="shouldShow">
        /// When this method returns true, contains whether the property should be shown.
        /// </param>
        /// <returns>
        /// True if the condition was successfully evaluated; false if evaluation failed.
        /// </returns>
        public static bool TryEvaluateCondition(
            object conditionValue,
            WShowIfAttribute showIf,
            out bool shouldShow
        )
        {
            bool? evaluation = EvaluateCondition(conditionValue, showIf);
            if (!evaluation.HasValue)
            {
                shouldShow = true;
                return false;
            }

            bool matched = evaluation.Value;
            shouldShow = showIf.inverse ? !matched : matched;
            return true;
        }

        /// <summary>
        /// Evaluates the condition based on the condition value and attribute settings.
        /// </summary>
        /// <param name="conditionValue">The current value of the condition field.</param>
        /// <param name="attribute">The WShowIf attribute containing comparison settings.</param>
        /// <returns>
        /// True if condition matches, false if it doesn't match, null if evaluation failed.
        /// </returns>
        public static bool? EvaluateCondition(object conditionValue, WShowIfAttribute attribute)
        {
            WShowIfComparison comparison = attribute.comparison;
#pragma warning disable CS0618 // Type or member is obsolete
            if (comparison == WShowIfComparison.Unknown)
#pragma warning restore CS0618 // Type or member is obsolete
            {
                comparison = WShowIfComparison.Equal;
            }

            switch (comparison)
            {
                case WShowIfComparison.IsNull:
                    return IsNull(conditionValue);
                case WShowIfComparison.IsNotNull:
                    return !IsNull(conditionValue);
                case WShowIfComparison.IsNullOrEmpty:
                    return IsNullOrEmpty(conditionValue);
                case WShowIfComparison.IsNotNullOrEmpty:
                    return !IsNullOrEmpty(conditionValue);
                default:
                    break;
            }

            object[] expectedValues = attribute.expectedValues;
            if (conditionValue is bool boolean)
            {
                return EvaluateBooleanCondition(boolean, comparison, expectedValues);
            }

            if (expectedValues == null || expectedValues.Length == 0)
            {
                return null;
            }

            switch (comparison)
            {
                case WShowIfComparison.Equal:
                    return MatchesAny(conditionValue, expectedValues);
                case WShowIfComparison.NotEqual:
                    return !MatchesAny(conditionValue, expectedValues);
                case WShowIfComparison.GreaterThan:
                case WShowIfComparison.GreaterThanOrEqual:
                case WShowIfComparison.LessThan:
                case WShowIfComparison.LessThanOrEqual:
                    object referenceValue = expectedValues[0];
                    return EvaluateRelationalComparison(conditionValue, referenceValue, comparison);
                default:
                    return MatchesAny(conditionValue, expectedValues);
            }
        }

        /// <summary>
        /// Evaluates a boolean condition against expected values.
        /// </summary>
        /// <param name="value">The boolean value to evaluate.</param>
        /// <param name="comparison">The comparison type.</param>
        /// <param name="expectedValues">The expected values to compare against.</param>
        /// <returns>True if condition matches, false otherwise.</returns>
        public static bool? EvaluateBooleanCondition(
            bool value,
            WShowIfComparison comparison,
            object[] expectedValues
        )
        {
            if (expectedValues is { Length: > 0 })
            {
                bool matches = MatchesAny(value, expectedValues);
                if (comparison == WShowIfComparison.NotEqual)
                {
                    return !matches;
                }

                return matches;
            }

            if (comparison == WShowIfComparison.NotEqual)
            {
                return !value;
            }

            return value;
        }

        /// <summary>
        /// Checks if the condition value matches any of the expected values.
        /// </summary>
        /// <param name="conditionValue">The current value to check.</param>
        /// <param name="expectedValues">The expected values to compare against.</param>
        /// <returns>True if condition value matches any expected value.</returns>
        public static bool MatchesAny(object conditionValue, object[] expectedValues)
        {
            for (int index = 0; index < expectedValues.Length; index++)
            {
                if (ValuesEqual(conditionValue, expectedValues[index]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Compares two values for equality, handling enums, flags, and numeric conversions.
        /// </summary>
        /// <param name="actual">The actual value.</param>
        /// <param name="expected">The expected value.</param>
        /// <returns>True if values are considered equal.</returns>
        public static bool ValuesEqual(object actual, object expected)
        {
            if (ReferenceEquals(actual, expected))
            {
                return true;
            }

            if (actual == null || expected == null)
            {
                return false;
            }

            if (actual.Equals(expected))
            {
                return true;
            }

            Type actualType = actual.GetType();
            Type expectedType = expected.GetType();

            try
            {
                if (actualType.IsEnum || expectedType.IsEnum)
                {
                    // Convert.ToInt64 overflows on a ulong-backed member above long.MaxValue, and
                    // the catch below turns that into a silent "not equal". TryConvertToUInt64
                    // dispatches on the underlying type and is total over all nine enum shapes.
                    if (
                        !TryConvertOperandToUInt64(actual, out ulong actualValue)
                        || !TryConvertOperandToUInt64(expected, out ulong expectedValue)
                    )
                    {
                        return false;
                    }

                    Type enumType = actualType.IsEnum ? actualType : expectedType;
                    if (enumType.IsDefined(typeof(FlagsAttribute), false))
                    {
                        return (actualValue & expectedValue) == expectedValue;
                    }

                    return actualValue == expectedValue;
                }
            }
            catch
            {
                return false;
            }

            if (actual is not IConvertible || expected is not IConvertible)
            {
                return false;
            }

            try
            {
                double actualValue = Convert.ToDouble(actual);
                double expectedValue = Convert.ToDouble(expected);
                return Math.Abs(actualValue - expectedValue) < double.Epsilon;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reduces one side of an enum comparison to the 64 bits both sides are compared on.
        /// </summary>
        /// <param name="value">The operand, an enum or any convertible the caller passed.</param>
        /// <param name="result">The bit pattern, or 0 when the operand cannot be reduced.</param>
        /// <returns><c>true</c> when a bit pattern was produced.</returns>
        private static bool TryConvertOperandToUInt64(object value, out ulong result)
        {
            if (value is Enum boxedEnum)
            {
                return boxedEnum.TryConvertToUInt64(out result);
            }

            switch (Type.GetTypeCode(value.GetType()))
            {
                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                {
                    result = ((IConvertible)value).ToUInt64(CultureInfo.InvariantCulture);
                    return true;
                }
                default:
                {
                    // Everything else -- the signed integers, and the float, string and bool forms
                    // the old Convert.ToInt64 accepted -- keeps its previous conversion,
                    // reinterpreted so both operands are compared as one domain. A throw here is
                    // still caught by the caller and still means "not equal".
                    long signed = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                    result = unchecked((ulong)signed);
                    return true;
                }
            }
        }

        /// <summary>
        /// Evaluates a relational comparison between two values.
        /// </summary>
        /// <param name="actual">The actual value.</param>
        /// <param name="expected">The expected value to compare against.</param>
        /// <param name="comparison">The comparison type.</param>
        /// <returns>
        /// True if comparison succeeds, false if it fails, null if comparison is not possible.
        /// </returns>
        public static bool? EvaluateRelationalComparison(
            object actual,
            object expected,
            WShowIfComparison comparison
        )
        {
            if (!TryCompare(actual, expected, out int compareResult))
            {
                return null;
            }

            switch (comparison)
            {
                case WShowIfComparison.GreaterThan:
                    return compareResult > 0;
                case WShowIfComparison.GreaterThanOrEqual:
                    return compareResult >= 0;
                case WShowIfComparison.LessThan:
                    return compareResult < 0;
                case WShowIfComparison.LessThanOrEqual:
                    return compareResult <= 0;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Tries to compare two values and returns the comparison result.
        /// </summary>
        /// <param name="actual">The actual value.</param>
        /// <param name="expected">The expected value.</param>
        /// <param name="comparisonResult">
        /// When this method returns true, contains the comparison result
        /// (negative if actual less than expected, zero if equal, positive if greater than).
        /// </param>
        /// <returns>True if comparison was successful; false otherwise.</returns>
        public static bool TryCompare(object actual, object expected, out int comparisonResult)
        {
            if (actual == null || expected == null)
            {
                comparisonResult = 0;
                return false;
            }

            IComparable comparable = actual as IComparable;
            if (comparable != null)
            {
                object converted = ConvertValue(
                    actual.GetType(),
                    expected,
                    out bool conversionSucceeded
                );
                if (conversionSucceeded)
                {
                    try
                    {
                        comparisonResult = comparable.CompareTo(converted);
                        return true;
                    }
                    catch
                    {
                        // Fall through to other comparison methods
                    }
                }
            }

            if (TryGenericComparableCompare(actual, expected, out int genericResult, false))
            {
                comparisonResult = genericResult;
                return true;
            }

            IComparable expectedComparable = expected as IComparable;
            if (expectedComparable != null)
            {
                object converted = ConvertValue(
                    expected.GetType(),
                    actual,
                    out bool conversionSucceeded
                );
                if (conversionSucceeded)
                {
                    try
                    {
                        comparisonResult = -expectedComparable.CompareTo(converted);
                        return true;
                    }
                    catch
                    {
                        // Fall through to other comparison methods
                    }
                }
            }

            if (TryGenericComparableCompare(expected, actual, out int invertedResult, true))
            {
                comparisonResult = invertedResult;
                return true;
            }

            if (
                TryConvertToDouble(actual, out double actualDouble)
                && TryConvertToDouble(expected, out double expectedDouble)
            )
            {
                comparisonResult = actualDouble.CompareTo(expectedDouble);
                return true;
            }

            comparisonResult = 0;
            return false;
        }

        /// <summary>
        /// Converts a value to the target type.
        /// </summary>
        /// <param name="targetType">The type to convert to.</param>
        /// <param name="value">The value to convert.</param>
        /// <param name="success">True if conversion succeeded; false otherwise.</param>
        /// <returns>The converted value, or null if conversion failed.</returns>
        public static object ConvertValue(Type targetType, object value, out bool success)
        {
            if (value == null)
            {
                success = !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;
                return null;
            }

            if (targetType.IsInstanceOfType(value))
            {
                success = true;
                return value;
            }

            try
            {
                if (targetType.IsEnum)
                {
                    Type underlyingType = Enum.GetUnderlyingType(targetType);
                    object numericValue = Convert.ChangeType(value, underlyingType);
                    object enumValue = Enum.ToObject(targetType, numericValue);
                    success = true;
                    return enumValue;
                }

                object converted = Convert.ChangeType(value, targetType);
                success = true;
                return converted;
            }
            catch
            {
                success = false;
                return null;
            }
        }

        /// <summary>
        /// Tries to convert a value to a double.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <param name="result">When this method returns true, contains the converted double.</param>
        /// <returns>True if conversion succeeded; false otherwise.</returns>
        public static bool TryConvertToDouble(object value, out double result)
        {
            if (value == null)
            {
                result = 0d;
                return false;
            }

            try
            {
                result = Convert.ToDouble(value);
                return true;
            }
            catch
            {
                result = 0d;
                return false;
            }
        }

        /// <summary>
        /// Tries to compare values using IComparable&lt;T&gt; interface.
        /// </summary>
        /// <param name="lhs">The left-hand side value.</param>
        /// <param name="rhs">The right-hand side value.</param>
        /// <param name="comparisonResult">
        /// When this method returns true, contains the comparison result.
        /// </param>
        /// <param name="invert">
        /// If true, the comparison result is inverted (for when comparing rhs to lhs).
        /// </param>
        /// <returns>True if comparison was successful; false otherwise.</returns>
        public static bool TryGenericComparableCompare(
            object lhs,
            object rhs,
            out int comparisonResult,
            bool invert
        )
        {
            if (lhs == null)
            {
                comparisonResult = 0;
                return false;
            }

            Type lhsType = lhs.GetType();

            if (!CompareToMethodCache.TryGetValue(lhsType, out MethodInfo compareTo))
            {
                compareTo = FindCompareToMethod(lhsType);
                CompareToMethodCache[lhsType] = compareTo;
            }

            if (compareTo == null)
            {
                comparisonResult = 0;
                return false;
            }

            Type genericArgument = compareTo.GetParameters()[0].ParameterType;
            object converted = ConvertValue(genericArgument, rhs, out bool success);
            if (!success)
            {
                comparisonResult = 0;
                return false;
            }

            try
            {
                object[] args = new object[1];
                args[0] = converted;
                object compareResult = compareTo.Invoke(lhs, args);
                int result = Convert.ToInt32(compareResult);
                if (invert)
                {
                    result = -result;
                }

                comparisonResult = result;
                return true;
            }
            catch
            {
                comparisonResult = 0;
                return false;
            }
        }

        /// <summary>
        /// Finds the CompareTo method for IComparable&lt;T&gt; on a type.
        /// </summary>
        /// <param name="type">The type to search for CompareTo method.</param>
        /// <returns>The CompareTo method, or null if not found.</returns>
        public static MethodInfo FindCompareToMethod(Type type)
        {
            Type[] interfaces = type.GetInterfaces();
            for (int index = 0; index < interfaces.Length; index++)
            {
                Type iface = interfaces[index];
                if (
                    !iface.IsGenericType
                    || iface.GetGenericTypeDefinition() != typeof(IComparable<>)
                )
                {
                    continue;
                }

                Type genericArgument = iface.GetGenericArguments()[0];
                Type[] paramTypes = new Type[1];
                paramTypes[0] = genericArgument;
                MethodInfo method = iface.GetMethod("CompareTo", paramTypes);
                if (method != null)
                {
                    return method;
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if a value is null, with special handling for Unity objects.
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns>True if value is null or a destroyed Unity object.</returns>
        public static bool IsNull(object value)
        {
            if (value == null)
            {
                return true;
            }

            // Use ReferenceEquals to check if the cast succeeded, avoiding Unity's
            // overloaded == operator which returns true for destroyed objects.
            // We want to detect destroyed objects here, not skip them.
            UnityEngine.Object unityObject = value as UnityEngine.Object;
            if (!ReferenceEquals(unityObject, null))
            {
                // Unity's == operator returns true for destroyed objects
                return unityObject == null;
            }

            return false;
        }

        /// <summary>
        /// Checks if a value is null or empty (for strings, collections, and enumerables).
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns>True if value is null, empty string, or empty collection.</returns>
        public static bool IsNullOrEmpty(object value)
        {
            if (IsNull(value))
            {
                return true;
            }

            string stringValue = value as string;
            if (stringValue != null)
            {
                return stringValue.Length == 0;
            }

            ICollection collection = value as ICollection;
            if (collection != null)
            {
                return collection.Count == 0;
            }

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                IEnumerator enumerator = enumerable.GetEnumerator();
                try
                {
                    return !enumerator.MoveNext();
                }
                finally
                {
                    IDisposable disposable = enumerator as IDisposable;
                    if (disposable != null)
                    {
                        disposable.Dispose();
                    }
                }
            }

            return false;
        }
    }
#endif
}
