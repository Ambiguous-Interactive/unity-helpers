// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System.Runtime.CompilerServices;

    internal static class LongHash
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ulong Mix(ulong key)
        {
            unchecked
            {
                key = (key ^ (key >> 30)) * 0xBF58476D1CE4E5B9UL;
                key = (key ^ (key >> 27)) * 0x94D049BB133111EBUL;
                return key ^ (key >> 31);
            }
        }
    }
}
