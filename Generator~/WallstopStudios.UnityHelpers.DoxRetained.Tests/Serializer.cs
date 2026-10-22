// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;

    internal static class Serializer
    {
        public static T JsonDeserialize<T>(string value)
        {
            throw new NotSupportedException(
                "Consumer JSON methods are outside host wire admission."
            );
        }
    }
}
