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

        private static readonly Dictionary<Type, OdinMigrationCompiledBinding> ResolvedBindings =
            new Dictionary<Type, OdinMigrationCompiledBinding>();

        /// <summary>Registers compiler metadata before editor migration begins.</summary>
        public static bool Register(OdinMigrationCompiledBinding binding)
        {
            if (binding == null || binding.OriginalType == null)
            {
                return false;
            }
            Bindings[binding.OriginalType] = binding;
            ResolvedBindings.Clear();
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
            if (
                ResolvedBindings.TryGetValue(
                    originalType,
                    out OdinMigrationCompiledBinding resolved
                )
            )
            {
                binding = resolved;
                return true;
            }
            if (!TryResolve(originalType, Bindings, out OdinMigrationCompiledBinding combined))
            {
                binding = null;
                return false;
            }
            ResolvedBindings[originalType] = combined;
            binding = combined;
            return true;
        }

        internal static bool TryResolve(
            Type originalType,
            IReadOnlyDictionary<Type, OdinMigrationCompiledBinding> registrations,
            out OdinMigrationCompiledBinding binding
        )
        {
            if (
                originalType == null
                || registrations == null
                || !registrations.TryGetValue(
                    originalType,
                    out OdinMigrationCompiledBinding original
                )
                || original == null
                || original.OriginalType != originalType
            )
            {
                binding = null;
                return false;
            }
            if (original.InheritedBindingType == null)
            {
                binding = original;
                return true;
            }
            List<OdinMigrationCompiledSource> sources = new List<OdinMigrationCompiledSource>();
            Dictionary<string, string> checksums = new Dictionary<string, string>(
                StringComparer.Ordinal
            );
            List<OdinMigrationCompiledField> fields = new List<OdinMigrationCompiledField>();
            List<OdinMigrationCompiledSourceEdit> edits =
                new List<OdinMigrationCompiledSourceEdit>();
            List<Type> affectedTypes = new List<Type>();
            HashSet<Type> visited = new HashSet<Type>();
            OdinMigrationCompiledBinding current = original;
            while (current != null)
            {
                if (!visited.Add(current.OriginalType))
                {
                    binding = null;
                    return false;
                }
                int sourceCount = current.Sources.Count;
                for (int index = 0; index < sourceCount; ++index)
                {
                    OdinMigrationCompiledSource source = current.Sources[index];
                    if (
                        source == null
                        || string.IsNullOrWhiteSpace(source.Path)
                        || string.IsNullOrWhiteSpace(source.Checksum)
                    )
                    {
                        binding = null;
                        return false;
                    }
                    if (checksums.TryGetValue(source.Path, out string checksum))
                    {
                        if (!string.Equals(checksum, source.Checksum, StringComparison.Ordinal))
                        {
                            binding = null;
                            return false;
                        }
                    }
                    else
                    {
                        checksums.Add(source.Path, source.Checksum);
                        sources.Add(source);
                    }
                }
                int fieldCount = current.Fields.Count;
                for (int index = 0; index < fieldCount; ++index)
                {
                    fields.Add(current.Fields[index]);
                }
                int editCount = current.BaseEdits.Count;
                for (int index = 0; index < editCount; ++index)
                {
                    edits.Add(current.BaseEdits[index]);
                }
                int affectedCount = current.AffectedTypes.Count;
                for (int index = 0; index < affectedCount; ++index)
                {
                    Type affected = current.AffectedTypes[index];
                    if (!affectedTypes.Contains(affected))
                    {
                        affectedTypes.Add(affected);
                    }
                }
                Type inherited = current.InheritedBindingType;
                if (inherited == null)
                {
                    break;
                }
                if (
                    !inherited.IsAssignableFrom(current.OriginalType)
                    || !registrations.TryGetValue(inherited, out current)
                    || current == null
                    || current.OriginalType != inherited
                )
                {
                    binding = null;
                    return false;
                }
            }
            OdinMigrationCompiledBinding combined = new OdinMigrationCompiledBinding(
                originalType,
                sources.ToArray(),
                fields.ToArray(),
                edits.ToArray(),
                affectedTypes.ToArray()
            );
            binding = combined;
            return true;
        }
    }
#endif
}
