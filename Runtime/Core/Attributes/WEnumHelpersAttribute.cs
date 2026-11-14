// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
#if UNITY_5_3_OR_NEWER
    using UnityEngine.Scripting;
#endif

    /// <summary>
    /// Requests per-enum fast-path overloads for flag checks, numeric conversion, cached names,
    /// display names, and span formatting.
    /// </summary>
    /// <remarks>
    /// Generation is an optimization only. Call sites that do not receive a generated overload,
    /// including generic call sites and enums from compiled assemblies, continue to use the
    /// generalized helpers in <c>EnumExtensions</c>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
#if UNITY_5_3_OR_NEWER
    [Preserve]
#endif
    public sealed class WEnumHelpersAttribute : Attribute { }
}
