// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    /// <summary>
    /// Caches classifications of the declared type once per closed generic type.
    /// </summary>
    /// <typeparam name="T">The declared type to classify.</typeparam>
    public static class ObjectsTypeTraits<T>
    {
        /// <summary>
        /// Whether <typeparamref name="T"/> is a value type, including nullable value types.
        /// </summary>
        public static readonly bool IsValueType = typeof(T).IsValueType;

        /// <summary>
        /// Whether <typeparamref name="T"/> is exactly <see cref="object"/>.
        /// </summary>
        public static readonly bool IsObjectType = typeof(T) == typeof(object);

        /// <summary>
        /// Whether <typeparamref name="T"/> derives from or is <see cref="UnityEngine.Object"/>.
        /// </summary>
        public static readonly bool IsUnityObject =
            !IsValueType && typeof(UnityEngine.Object).IsAssignableFrom(typeof(T));
    }
}
