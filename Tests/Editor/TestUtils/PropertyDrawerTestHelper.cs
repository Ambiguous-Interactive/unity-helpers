// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.TestUtils
{
    using System;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using PropertyAttribute = UnityEngine.PropertyAttribute;

    /// <summary>
    ///     Provides helper methods for configuring PropertyDrawer instances in tests.
    ///     Unity's PropertyDrawer base class uses internal fields (m_FieldInfo, m_Attribute)
    ///     that must be set via reflection for proper testing of custom drawers.
    /// </summary>
    internal static class PropertyDrawerTestHelper
    {
        private static readonly FieldInfo FieldInfoField = typeof(PropertyDrawer).GetField(
            "m_FieldInfo",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        private static readonly FieldInfo AttributeField = typeof(PropertyDrawer).GetField(
            "m_Attribute",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        /// <summary>
        ///     Assigns the FieldInfo for a PropertyDrawer instance, simulating how Unity
        ///     internally configures drawers when rendering inspector fields.
        /// </summary>
        /// <param name="drawer">The PropertyDrawer instance to configure.</param>
        /// <param name="hostType">The type containing the field to draw.</param>
        /// <param name="fieldName">The name of the field on the host type.</param>
        public static void AssignFieldInfo(PropertyDrawer drawer, Type hostType, string fieldName)
        {
            if (drawer == null || hostType == null || string.IsNullOrEmpty(fieldName))
            {
                return;
            }

            FieldInfo hostField = hostType.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (hostField == null)
            {
                return;
            }

            FieldInfoField?.SetValue(drawer, hostField);
        }

        /// <summary>
        ///     Assigns both the FieldInfo and PropertyAttribute for a PropertyDrawer instance.
        /// </summary>
        /// <param name="drawer">The PropertyDrawer instance to configure.</param>
        /// <param name="fieldInfo">The FieldInfo for the field being drawn.</param>
        /// <param name="attribute">The PropertyAttribute decorating the field.</param>
        public static void ConfigureDrawer(
            PropertyDrawer drawer,
            FieldInfo fieldInfo,
            PropertyAttribute attribute
        )
        {
            FieldInfoField?.SetValue(drawer, fieldInfo);
            AttributeField?.SetValue(drawer, attribute);
        }

        /// <summary>
        ///     Assigns the PropertyAttribute for a PropertyDrawer instance.
        /// </summary>
        /// <param name="drawer">The PropertyDrawer instance to configure.</param>
        /// <param name="attribute">The PropertyAttribute decorating the field.</param>
        public static void AssignAttribute(PropertyDrawer drawer, PropertyAttribute attribute)
        {
            AttributeField?.SetValue(drawer, attribute);
        }

        /// <summary>
        ///     Gets the FieldInfo for a field on a given type.
        ///     Searches public and non-public instance fields.
        /// </summary>
        /// <param name="hostType">The type containing the field.</param>
        /// <param name="fieldName">The name of the field.</param>
        /// <returns>The FieldInfo, or null if not found.</returns>
        public static FieldInfo GetFieldInfo(Type hostType, string fieldName)
        {
            if (hostType == null || string.IsNullOrEmpty(fieldName))
            {
                return null;
            }

            return hostType.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        }

        /// <summary>
        ///     Gets the FieldInfo for a field on a given type, with assertion on failure.
        /// </summary>
        /// <param name="hostType">The type containing the field.</param>
        /// <param name="fieldName">The name of the field.</param>
        /// <returns>The FieldInfo.</returns>
        /// <exception cref="AssertionException">Thrown if the field cannot be found.</exception>
        public static FieldInfo GetFieldInfoOrFail(Type hostType, string fieldName)
        {
            FieldInfo field = GetFieldInfo(hostType, fieldName);
            Assert.IsTrue(
                field != null,
                $"Unable to resolve field '{fieldName}' on type {hostType?.FullName ?? "null"}"
            );
            return field;
        }

        /// <summary>
        ///     Gets a custom attribute from a SerializedProperty's backing field.
        /// </summary>
        /// <typeparam name="T">The attribute type to retrieve.</typeparam>
        /// <param name="property">The SerializedProperty to inspect.</param>
        /// <returns>The attribute instance, or null if not found.</returns>
        public static T GetAttributeFromProperty<T>(SerializedProperty property)
            where T : Attribute
        {
            if (property == null)
            {
                return null;
            }

            Type targetType = property.serializedObject.targetObject.GetType();
            FieldInfo field = targetType.GetField(
                property.name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );

            return field?.GetCustomAttribute<T>();
        }

        /// <summary>
        ///     Finds the first public instance field on a type that has the specified attribute.
        /// </summary>
        /// <typeparam name="TAttribute">The attribute type to search for.</typeparam>
        /// <param name="hostType">The type to search.</param>
        /// <returns>
        ///     A tuple containing the FieldInfo and the attribute instance, or (null, null) if not found.
        /// </returns>
        public static (
            FieldInfo field,
            TAttribute attribute
        ) FindFirstFieldWithAttribute<TAttribute>(Type hostType)
            where TAttribute : Attribute
        {
            if (hostType == null)
            {
                return (null, null);
            }

            FieldInfo[] fields = hostType.GetFields(BindingFlags.Instance | BindingFlags.Public);

            foreach (FieldInfo field in fields)
            {
                TAttribute attr = (TAttribute)
                    Attribute.GetCustomAttribute(field, typeof(TAttribute));
                if (attr != null)
                {
                    return (field, attr);
                }
            }

            return (null, null);
        }

        /// <summary>
        ///     Finds the first public instance field on a type that has the specified attribute,
        ///     with assertion on failure.
        /// </summary>
        /// <typeparam name="TAttribute">The attribute type to search for.</typeparam>
        /// <param name="hostType">The type to search.</param>
        /// <returns>A tuple containing the FieldInfo and the attribute instance.</returns>
        /// <exception cref="AssertionException">Thrown if no field with the attribute is found.</exception>
        public static (
            FieldInfo field,
            TAttribute attribute
        ) FindFirstFieldWithAttributeOrFail<TAttribute>(Type hostType)
            where TAttribute : Attribute
        {
            (FieldInfo field, TAttribute attribute) result =
                FindFirstFieldWithAttribute<TAttribute>(hostType);
            Assert.IsTrue(
                result.field != null,
                $"No field with {typeof(TAttribute).Name} found on {hostType?.FullName ?? "null"}"
            );
            return result;
        }
    }
}
