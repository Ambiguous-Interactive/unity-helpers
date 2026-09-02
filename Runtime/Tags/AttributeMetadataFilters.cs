// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Core.Helper;

    internal static class AttributeMetadataFilters
    {
        internal static bool ShouldSerialize(Type type)
        {
            if (type == null || type.IsAbstract || type.IsGenericTypeDefinition)
            {
                return false;
            }

            if (IsExplicitInclude(type))
            {
                return true;
            }

            return !TestAssemblyHelper.IsTestType(type);
        }

        internal static bool HasTestAssembliesLoaded()
        {
            foreach (Assembly assembly in ReflectionHelpers.GetAllLoadedAssemblies())
            {
                if (TestAssemblyHelper.IsTestAssembly(assembly))
                {
                    return true;
                }
            }
            return false;
        }

        internal static string[] MergeWithExcludedAttributeNames(string[] cachedNames)
        {
            string[] baseNames = cachedNames ?? Array.Empty<string>();

            if (!HasTestAssembliesLoaded())
            {
                return baseNames;
            }

            HashSet<string> names = new(baseNames, StringComparer.Ordinal);
            bool added = false;

            foreach (Type type in EnumerateLoadedTypes())
            {
                if (type == null || type.IsAbstract)
                {
                    continue;
                }

                if (!typeof(AttributesComponent).IsAssignableFrom(type))
                {
                    continue;
                }

                if (ShouldSerialize(type))
                {
                    continue;
                }

                foreach (
                    FieldInfo field in ReflectionHelpers.GetInstanceFieldsIncludingBaseTypes(type)
                )
                {
                    if (field.FieldType != typeof(Attribute))
                    {
                        continue;
                    }

                    if (names.Add(field.Name))
                    {
                        added = true;
                    }
                }
            }

            if (!added)
            {
                return baseNames;
            }

            string[] result = new string[names.Count];
            names.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static bool IsExplicitInclude(Type type)
        {
            return ReflectionHelpers.HasAttributeSafe<AlwaysIncludeInAttributeMetadataCacheAttribute>(
                type,
                inherit: true
            );
        }

        private static IEnumerable<Type> EnumerateLoadedTypes()
        {
            return ReflectionHelpers.GetAllLoadedTypes();
        }
    }
}
