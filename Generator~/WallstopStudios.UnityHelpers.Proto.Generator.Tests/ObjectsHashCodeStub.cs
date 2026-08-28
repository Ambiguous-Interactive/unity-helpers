// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System.Collections.Generic;

    /// <summary>Unity-free hash helper used only by the linked tuple contracts in this harness.</summary>
    internal static class Objects
    {
        internal static int HashCode<T1, T2>(T1 first, T2 second)
        {
            int hash = EqualityComparer<T1>.Default.GetHashCode(first);
            return unchecked((hash * 397) ^ EqualityComparer<T2>.Default.GetHashCode(second));
        }

        internal static int HashCode<T1, T2, T3>(T1 first, T2 second, T3 third)
        {
            int hash = HashCode(first, second);
            return unchecked((hash * 397) ^ EqualityComparer<T3>.Default.GetHashCode(third));
        }
    }
}
