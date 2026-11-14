// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Reflection;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal static class RuntimeShim
    {
        public static bool IsAttributeDefined<T>(
            this FieldInfo provider,
            out T attribute,
            bool inherit = true
        )
            where T : Attribute
        {
            Type type = typeof(T);
            if (provider.IsDefined(type, inherit))
            {
                object[] attributes = provider.GetCustomAttributes(type, inherit);
                if (attributes.Length != 0)
                {
                    attribute = (T)attributes[0];
                    return true;
                }
            }
            attribute = default;
            return false;
        }
    }
}
