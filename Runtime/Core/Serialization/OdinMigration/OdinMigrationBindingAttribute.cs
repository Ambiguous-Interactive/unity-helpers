// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration
{
#if UNITY_EDITOR
    using System;

    /// <summary>Identifies a compiler-bound migration owner in a referenced editor assembly.</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class OdinMigrationBindingAttribute : Attribute
    {
        /// <summary>Gets the original owner type whose binding the assembly registers.</summary>
        public Type OriginalType { get; }

        /// <summary>Records the compiler-visible owner without exposing its private fields.</summary>
        public OdinMigrationBindingAttribute(Type originalType)
        {
            OriginalType = originalType;
        }
    }
#endif
}
