// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WButton
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Concurrent;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal static class WButtonValueUtility
    {
        private static readonly ConcurrentDictionary<Type, Func<object>> ParameterlessFactoryCache =
            new();
        private static readonly ConcurrentDictionary<Type, byte> UnsupportedFactoryTypes = new();

        internal static object CloneValue(object value)
        {
            switch (value)
            {
                case Array array:
                    return array.Clone();
                case AnimationCurve curve:
                {
                    AnimationCurve clone = new(curve.keys)
                    {
                        preWrapMode = curve.preWrapMode,
                        postWrapMode = curve.postWrapMode,
                    };
                    return clone;
                }
                default:
                    return value;
            }
        }

        internal static bool ValuesEqual(object left, object right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left is not Array leftArray || right is not Array rightArray)
            {
                return left.Equals(right);
            }

            if (leftArray.Length != rightArray.Length)
            {
                return false;
            }

            for (int index = 0; index < leftArray.Length; index++)
            {
                object leftElement = leftArray.GetValue(index);
                object rightElement = rightArray.GetValue(index);
                if (!ValuesEqual(leftElement, rightElement))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool TryCreateInstance(Type type, out object value)
        {
            if (type == null)
            {
                value = null;
                return false;
            }

            if (ParameterlessFactoryCache.TryGetValue(type, out Func<object> cached))
            {
                value = cached();
                return value != null;
            }

            if (UnsupportedFactoryTypes.ContainsKey(type))
            {
                value = null;
                return false;
            }

            try
            {
                Func<object> factory = ParameterlessFactoryCache.GetOrAdd(
                    type,
                    ReflectionHelpers.GetParameterlessConstructor(type)
                );
                value = factory();
                return value != null;
            }
            catch (ArgumentException)
            {
                UnsupportedFactoryTypes.TryAdd(type, 0);
                value = null;
                return false;
            }
        }
    }
#endif
}
