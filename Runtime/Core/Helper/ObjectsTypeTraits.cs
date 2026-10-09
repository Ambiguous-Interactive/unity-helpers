// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    internal static class ObjectsTypeTraits<T>
    {
        internal static readonly bool IsValueType = typeof(T).IsValueType;
    }
}
