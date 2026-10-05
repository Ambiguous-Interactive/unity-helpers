// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;

    /// <summary>Refuses legacy exception binary serialization outside the Unity runtime harness.</summary>
    internal static class ReflectionHelpers
    {
        internal static Type TryResolveType(string typeName)
        {
            throw new NotSupportedException(
                "Legacy exception binary restoration requires the Unity runtime harness."
            );
        }

        internal static string GetAssemblyQualifiedName(Type type)
        {
            throw new NotSupportedException(
                "Legacy exception binary serialization requires the Unity runtime harness."
            );
        }
    }
}
