// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;

    /// <summary>Resolves dictionary declarations registered by editor compilation.</summary>
    /// <remarks>Register and resolve bindings on the Unity editor thread.</remarks>
    public static class OdinMigrationCompiledBindings
    {
        private static readonly Dictionary<Type, OdinMigrationCompiledBinding> Bindings =
            new Dictionary<Type, OdinMigrationCompiledBinding>();

        /// <summary>Registers compiler metadata before editor migration begins.</summary>
        public static bool Register(OdinMigrationCompiledBinding binding)
        {
            if (binding == null || binding.OriginalType == null)
            {
                return false;
            }
            Bindings[binding.OriginalType] = binding;
            return true;
        }

        /// <summary>Finds the compiled dictionary declarations for a Unity type.</summary>
        public static bool TryGet(Type originalType, out OdinMigrationCompiledBinding binding)
        {
            if (originalType == null)
            {
                binding = null;
                return false;
            }
            return Bindings.TryGetValue(originalType, out binding);
        }
    }
#endif
}
