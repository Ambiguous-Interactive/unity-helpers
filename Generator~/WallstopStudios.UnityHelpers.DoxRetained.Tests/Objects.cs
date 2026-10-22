// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;

    internal static class Objects
    {
        public static int EnumerableHashCode<T>(IEnumerable<T> values)
        {
            throw new NotSupportedException(
                "Consumer hash methods are outside host wire admission."
            );
        }
    }
}
