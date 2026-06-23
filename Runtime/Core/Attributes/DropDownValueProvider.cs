// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal static class DropDownValueProvider<T>
    {
        private static readonly Func<T[]> EmptyFactory = () => Array.Empty<T>();

        public static Func<T[]> FromList(T[] values)
        {
            if (values == null)
            {
                return EmptyFactory;
            }

            return () => values;
        }

        public static Func<T[]> FromMethod(
            Type providerType,
            string methodName,
            string attributeName
        )
        {
            if (providerType == null)
            {
                Debug.LogWarning($"{attributeName}: Provider type cannot be null.");
                return EmptyFactory;
            }

            if (string.IsNullOrEmpty(methodName))
            {
                Debug.LogWarning($"{attributeName}: Method name cannot be null or empty.");
                return EmptyFactory;
            }

            MethodInfo resolved = ResolveProviderMethod(providerType, methodName);
            if (resolved == null)
            {
                Debug.LogWarning(
                    $"{attributeName}: Could not locate a parameterless static method named '{methodName}' on {providerType.FullName} that returns {typeof(T).Name} values."
                );
                return EmptyFactory;
            }

            object cachedSourceResult = null;
            T[] cachedTypedResult = null;

            return () =>
            {
                object result;
                try
                {
                    result = ReflectionHelpers.InvokeStaticMethod(resolved);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"{attributeName}: Invocation of '{providerType.FullName}.{methodName}' threw {e.GetType().Name}."
                    );
                    return Array.Empty<T>();
                }

                if (result == null)
                {
                    return Array.Empty<T>();
                }

                if (ReferenceEquals(result, cachedSourceResult) && cachedTypedResult != null)
                {
                    return cachedTypedResult;
                }

                T[] typedResult = ConvertResult(result, providerType, methodName, attributeName);
                cachedSourceResult = result;
                cachedTypedResult = typedResult;
                return typedResult;
            };
        }

        private static T[] ConvertResult(
            object result,
            Type providerType,
            string methodName,
            string attributeName
        )
        {
            if (result is T[] typedArray)
            {
                return typedArray;
            }

            if (result is IEnumerable<T> enumerable)
            {
                return ConvertEnumerable(enumerable);
            }

            Debug.LogWarning(
                $"{attributeName}: Method '{providerType.FullName}.{methodName}' returned incompatible type '{result.GetType().FullName}'. Expected {typeof(T).Name}[] or IEnumerable<{typeof(T).Name}>."
            );
            return Array.Empty<T>();
        }

        private static T[] ConvertEnumerable(IEnumerable<T> enumerable)
        {
            List<T> values = new();
            foreach (T entry in enumerable)
            {
                values.Add(entry);
            }

            if (values.Count == 0)
            {
                return Array.Empty<T>();
            }

            return values.ToArray();
        }

        private static MethodInfo ResolveProviderMethod(
            Type providerType,
            string methodName,
            bool requireEnumerable = true
        )
        {
            MethodInfo[] methods = providerType.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            );

            for (int index = 0; index < methods.Length; index += 1)
            {
                MethodInfo candidate = methods[index];
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (candidate.GetParameters().Length != 0)
                {
                    continue;
                }

                ParameterInfo returnParameter = candidate.ReturnParameter;
                if (returnParameter == null)
                {
                    continue;
                }

                Type returnType = returnParameter.ParameterType;
                if (returnType == typeof(T[]))
                {
                    return candidate;
                }

                if (typeof(IEnumerable<T>).IsAssignableFrom(returnType))
                {
                    return candidate;
                }
            }

            return null;
        }
    }

    internal static class DropDownValueProvider
    {
        private static readonly Func<object[]> EmptyFactory = () => Array.Empty<object>();

        public static Func<object[]> FromList(Type valueType, object[] values, string attributeName)
        {
            if (valueType == null)
            {
                Debug.LogWarning($"{attributeName}: Value type cannot be null.");
                return EmptyFactory;
            }

            if (values == null || values.Length == 0)
            {
                return EmptyFactory;
            }

            object[] normalized = NormalizeValues(values, valueType, attributeName);
            return () => normalized;
        }

        public static Func<object[]> FromMethod(
            Type providerType,
            string methodName,
            Type valueType,
            string attributeName,
            bool logErrorIfNotFound = true
        )
        {
            if (valueType == null)
            {
                Debug.LogWarning($"{attributeName}: Value type cannot be null.");
                return logErrorIfNotFound ? EmptyFactory : null;
            }

            if (providerType == null)
            {
                Debug.LogWarning($"{attributeName}: Provider type cannot be null.");
                return logErrorIfNotFound ? EmptyFactory : null;
            }

            if (string.IsNullOrEmpty(methodName))
            {
                Debug.LogWarning($"{attributeName}: Method name cannot be null or empty.");
                return logErrorIfNotFound ? EmptyFactory : null;
            }

            MethodInfo resolved = ResolveProviderMethod(providerType, methodName);
            if (resolved == null)
            {
                if (logErrorIfNotFound)
                {
                    Debug.LogWarning(
                        $"{attributeName}: Could not locate a parameterless static method named '{methodName}' on {providerType.FullName} that returns enumerable values."
                    );
                    return EmptyFactory;
                }
                return null;
            }

            object cachedSourceResult = null;
            object[] cachedNormalizedResult = null;

            return () =>
            {
                object result;
                try
                {
                    result = ReflectionHelpers.InvokeStaticMethod(resolved);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"{attributeName}: Invocation of '{providerType.FullName}.{methodName}' threw {e.GetType().Name}."
                    );
                    return Array.Empty<object>();
                }

                if (result == null)
                {
                    return Array.Empty<object>();
                }

                if (ReferenceEquals(result, cachedSourceResult) && cachedNormalizedResult != null)
                {
                    return cachedNormalizedResult;
                }

                object[] normalized = NormalizeResult(result, valueType, attributeName);
                cachedSourceResult = result;
                cachedNormalizedResult = normalized;
                return normalized;
            };
        }

        public static Func<object[]> FromMethod(
            Type providerType,
            string methodName,
            string attributeName,
            out Type resolvedValueType,
            bool logErrorIfNotFound = true
        )
        {
            resolvedValueType = typeof(object);

            if (providerType == null)
            {
                Debug.LogWarning($"{attributeName}: Provider type cannot be null.");
                return logErrorIfNotFound ? EmptyFactory : null;
            }

            if (string.IsNullOrEmpty(methodName))
            {
                Debug.LogWarning($"{attributeName}: Method name cannot be null or empty.");
                return logErrorIfNotFound ? EmptyFactory : null;
            }

            MethodInfo resolved = ResolveProviderMethod(
                providerType,
                methodName,
                requireEnumerable: false
            );
            if (resolved == null)
            {
                if (logErrorIfNotFound)
                {
                    Debug.LogWarning(
                        $"{attributeName}: Could not locate a parameterless static method named '{methodName}' on {providerType.FullName} that returns enumerable values."
                    );
                    return EmptyFactory;
                }
                return null;
            }

            if (!TryGetElementType(resolved.ReturnType, out Type elementType))
            {
                if (logErrorIfNotFound)
                {
                    Debug.LogWarning(
                        $"{attributeName}: Method '{providerType.FullName}.{methodName}' must return an array or IEnumerable<T>."
                    );
                    return EmptyFactory;
                }
                resolvedValueType = typeof(object);
                return null;
            }

            resolvedValueType = elementType ?? typeof(object);

            Type conversionType = resolvedValueType ?? typeof(object);

            object cachedSourceResult = null;
            object[] cachedNormalizedResult = null;

            return () =>
            {
                object result;
                try
                {
                    result = ReflectionHelpers.InvokeStaticMethod(resolved);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"{attributeName}: Invocation of '{providerType.FullName}.{methodName}' threw {e.GetType().Name}."
                    );
                    return Array.Empty<object>();
                }

                if (result == null)
                {
                    return Array.Empty<object>();
                }

                if (ReferenceEquals(result, cachedSourceResult) && cachedNormalizedResult != null)
                {
                    return cachedNormalizedResult;
                }

                object[] normalized = NormalizeResult(result, conversionType, attributeName);
                cachedSourceResult = result;
                cachedNormalizedResult = normalized;
                return normalized;
            };
        }

        private static object[] NormalizeResult(object result, Type valueType, string attributeName)
        {
            if (result is Array array)
            {
                return NormalizeArray(array, valueType, attributeName);
            }

            if (result is IEnumerable enumerable)
            {
                return NormalizeEnumerable(enumerable, valueType, attributeName);
            }

            Debug.LogWarning(
                $"{attributeName}: Provider returned incompatible type '{result.GetType().FullName}'. Expected an array or IEnumerable."
            );
            return Array.Empty<object>();
        }

        private static object[] NormalizeValues(
            object[] values,
            Type valueType,
            string attributeName
        )
        {
            List<object> normalized = new();
            for (int index = 0; index < values.Length; index += 1)
            {
                object current = values[index];
                if (TryConvertValue(current, valueType, out object converted))
                {
                    normalized.Add(converted);
                }
                else
                {
                    Debug.LogWarning(
                        $"{attributeName}: Unable to convert value at index {index} to {valueType.FullName}."
                    );
                }
            }

            if (normalized.Count == 0)
            {
                return Array.Empty<object>();
            }

            return normalized.ToArray();
        }

        private static object[] NormalizeArray(Array array, Type valueType, string attributeName)
        {
            List<object> normalized = new();
            IEnumerator enumerator = array.GetEnumerator();
            int index = 0;
            while (enumerator.MoveNext())
            {
                object current = enumerator.Current;
                if (TryConvertValue(current, valueType, out object converted))
                {
                    normalized.Add(converted);
                }
                else
                {
                    Debug.LogWarning(
                        $"{attributeName}: Unable to convert value at index {index} to {valueType.FullName}."
                    );
                }

                index += 1;
            }

            if (normalized.Count == 0)
            {
                return Array.Empty<object>();
            }

            return normalized.ToArray();
        }

        private static object[] NormalizeEnumerable(
            IEnumerable enumerable,
            Type valueType,
            string attributeName
        )
        {
            List<object> normalized = new();
            IEnumerator enumerator = enumerable.GetEnumerator();
            int index = 0;
            while (enumerator.MoveNext())
            {
                object current = enumerator.Current;
                if (TryConvertValue(current, valueType, out object converted))
                {
                    normalized.Add(converted);
                }
                else
                {
                    Debug.LogWarning(
                        $"{attributeName}: Unable to convert value at index {index} to {valueType.FullName}."
                    );
                }

                index += 1;
            }

            if (normalized.Count == 0)
            {
                return Array.Empty<object>();
            }

            return normalized.ToArray();
        }

        private static MethodInfo ResolveProviderMethod(
            Type providerType,
            string methodName,
            bool requireEnumerable = true
        )
        {
            MethodInfo[] methods = providerType.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            );

            for (int index = 0; index < methods.Length; index += 1)
            {
                MethodInfo candidate = methods[index];
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (candidate.GetParameters().Length != 0)
                {
                    continue;
                }

                ParameterInfo returnParameter = candidate.ReturnParameter;
                if (returnParameter == null)
                {
                    continue;
                }

                Type returnType = returnParameter.ParameterType;
                if (returnType == typeof(void))
                {
                    continue;
                }

                bool isEnumerable =
                    returnType.IsArray
                    || (
                        returnType != typeof(string)
                        && typeof(IEnumerable).IsAssignableFrom(returnType)
                    );

                if (requireEnumerable)
                {
                    if (isEnumerable)
                    {
                        return candidate;
                    }

                    continue;
                }

                if (returnType == typeof(string))
                {
                    continue;
                }

                if (isEnumerable)
                {
                    return candidate;
                }

                return candidate;
            }

            return null;
        }

        private static bool TryGetElementType(Type returnType, out Type elementType)
        {
            elementType = null;
            if (returnType == null || returnType == typeof(void))
            {
                return false;
            }

            if (returnType.IsArray)
            {
                elementType = returnType.GetElementType();
                return elementType != null;
            }

            if (typeof(IEnumerable).IsAssignableFrom(returnType))
            {
                if (returnType.IsGenericType)
                {
                    Type[] genericArguments = returnType.GetGenericArguments();
                    if (genericArguments.Length == 1)
                    {
                        elementType = genericArguments[0];
                        return true;
                    }
                }

                Type enumerableInterface = FindEnumerableInterface(returnType);
                if (enumerableInterface != null)
                {
                    elementType = enumerableInterface.GetGenericArguments()[0];
                    return true;
                }

                elementType = typeof(object);
                return true;
            }

            return false;
        }

        private static Type FindEnumerableInterface(Type type)
        {
            if (
                type.IsInterface
                && type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            )
            {
                return type;
            }

            Type[] interfaces = type.GetInterfaces();
            for (int index = 0; index < interfaces.Length; index += 1)
            {
                Type candidate = interfaces[index];
                if (
                    candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                )
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool TryConvertValue(object value, Type targetType, out object converted)
        {
            if (value == null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                {
                    converted = null;
                    return false;
                }

                converted = null;
                return true;
            }

            if (targetType.IsInstanceOfType(value))
            {
                converted = value;
                return true;
            }

            try
            {
                if (targetType.IsEnum)
                {
                    return TryConvertEnum(value, targetType, out converted);
                }

                Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
                converted = Convert.ChangeType(
                    value,
                    underlying,
                    System.Globalization.CultureInfo.InvariantCulture
                );
                return true;
            }
            catch (Exception)
            {
                converted = null;
                return false;
            }
        }

        private static bool TryConvertEnum(object value, Type targetType, out object converted)
        {
            try
            {
                if (value is string stringValue)
                {
                    converted = Enum.Parse(targetType, stringValue, true);
                    return true;
                }

                Type underlying = Enum.GetUnderlyingType(targetType);
                object numeric = Convert.ChangeType(
                    value,
                    underlying,
                    System.Globalization.CultureInfo.InvariantCulture
                );
                converted = Enum.ToObject(targetType, numeric);
                return true;
            }
            catch (Exception)
            {
                converted = null;
                return false;
            }
        }
    }
}
