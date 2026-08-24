// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Reflection;

    /// <summary>
    /// Internal helpers for detecting Unity test assemblies and types by name markers or attributes.
    /// </summary>
    internal static class TestAssemblyHelper
    {
        private static readonly StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

        /// <summary>
        /// Heuristically determines if an assembly is a test assembly by name markers or Unity test attributes.
        /// </summary>
        internal static bool IsTestAssembly(Assembly assembly)
        {
            if (assembly == null || assembly.IsDynamic)
            {
                return false;
            }

            string name = assembly.GetName().Name;
            if (!string.IsNullOrEmpty(name) && ContainsTestMarker(name))
            {
                return true;
            }

            if (HasUnityTestAttribute(assembly))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Heuristically determines if a type belongs to a test assembly or a namespace with test markers.
        /// </summary>
        internal static bool IsTestType(Type type)
        {
            if (type == null)
            {
                return false;
            }

            string ns = type.Namespace;
            if (string.IsNullOrEmpty(ns))
            {
                return ContainsTestMarker(type.Name) || IsTestAssembly(type.Assembly);
            }

            if (ContainsTestMarker(ns))
            {
                return true;
            }

            return IsTestAssembly(type.Assembly);
        }

        /// <summary>
        /// Returns true if the string contains common test markers (e.g., Test, Tests, prefixes/suffixes/segments).
        /// </summary>
        internal static bool ContainsTestMarker(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (
                0 <= value.IndexOf(".Tests", Comparison)
                || 0 <= value.IndexOf(".Test", Comparison)
                || 0 <= value.IndexOf("Tests.", Comparison)
                || 0 <= value.IndexOf("Tests_", Comparison)
                || 0 <= value.IndexOf("Test.", Comparison)
                || 0 <= value.IndexOf("Test_", Comparison)
            )
            {
                return true;
            }

            if (value.EndsWith("Tests", Comparison) || value.EndsWith("Test", Comparison))
            {
                return true;
            }

            if (value.StartsWith("Tests", Comparison) || value.StartsWith("Test", Comparison))
            {
                return true;
            }

            if (0 <= value.IndexOf("Test", Comparison))
            {
                return true;
            }

            return false;
        }

        private static bool HasUnityTestAttribute(Assembly assembly)
        {
            try
            {
                foreach (Attribute attribute in assembly.GetAllAttributesSafe(inherit: false))
                {
                    Type attributeType = attribute?.GetType();
                    if (attributeType == null)
                    {
                        continue;
                    }

                    string fullName = attributeType.FullName;
                    if (
                        string.Equals(
                            fullName,
                            "UnityEngine.TestTools.UnityTestAssemblyAttribute",
                            Comparison
                        )
                        || string.Equals(
                            fullName,
                            "UnityEditor.TestTools.TestRunner.UnityTestAssemblyAttribute",
                            Comparison
                        )
                    )
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
