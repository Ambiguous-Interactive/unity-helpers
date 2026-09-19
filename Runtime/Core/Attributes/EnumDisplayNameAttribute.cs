// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
#if UNITY_5_3_OR_NEWER
    using UnityEngine.Scripting;
#endif

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
#if UNITY_5_3_OR_NEWER
    [Preserve]
#endif
    public sealed class EnumDisplayNameAttribute : Attribute
    {
        public string DisplayName { get; }

        public EnumDisplayNameAttribute(string displayName)
        {
            DisplayName = displayName;
        }
    }
}
