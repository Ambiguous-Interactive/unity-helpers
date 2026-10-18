// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;

    internal static class NonWireExtensions
    {
        public static string ToJson<T>(this T value)
        {
            throw new NotSupportedException(
                "Consumer JSON methods are outside host wire admission."
            );
        }

        public static IEnumerable<T> Ordered<T>(this IEnumerable<T> values)
        {
            throw new NotSupportedException(
                "Consumer hash methods are outside host wire admission."
            );
        }

        public static bool ContentEquals<TKey, TValue>(
            this Dictionary<TKey, TValue> first,
            Dictionary<TKey, TValue> second
        )
        {
            throw new NotSupportedException("Admission compares map contents independently.");
        }
    }
}
