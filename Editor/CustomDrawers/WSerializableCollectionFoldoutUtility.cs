// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;

    /// <summary>
    /// Provides shared helpers for initializing SerializableDictionary/SerializableSet foldout states.
    /// </summary>
    internal static class WSerializableCollectionFoldoutUtility
    {
        private static readonly HashSet<FoldoutInitializationKey> InitializedKeys = new();

        internal static void EnsureFoldoutInitialized(
            SerializedProperty property,
            FieldInfo fieldInfo,
            SerializableCollectionType collectionType
        )
        {
            if (property == null)
            {
                return;
            }

            if (!MarkInitialized(property))
            {
                return;
            }

            bool startExpanded = ResolveStartExpanded(fieldInfo, collectionType);
            if (!startExpanded || property.isExpanded)
            {
                return;
            }

            property.isExpanded = true;
        }

        private static bool MarkInitialized(SerializedProperty property)
        {
            SerializedObject serializedObject = property.serializedObject;
            bool added = false;
            if (serializedObject == null)
            {
                added |= InitializedKeys.Add(
                    new FoldoutInitializationKey(null, property.propertyPath)
                );
                return added;
            }

            UnityEngine.Object[] targets = serializedObject.targetObjects;
            if (targets == null || targets.Length == 0)
            {
                added |= InitializedKeys.Add(
                    new FoldoutInitializationKey(
                        serializedObject.targetObject,
                        property.propertyPath
                    )
                );
                return added;
            }

            for (int index = 0; index < targets.Length; index++)
            {
                added |= InitializedKeys.Add(
                    new FoldoutInitializationKey(targets[index], property.propertyPath)
                );
            }

            return added;
        }

        private static bool ResolveStartExpanded(
            FieldInfo fieldInfo,
            SerializableCollectionType collectionType
        )
        {
            WSerializableCollectionFoldoutAttribute attribute =
                fieldInfo?.GetCustomAttribute<WSerializableCollectionFoldoutAttribute>();
            if (attribute != null)
            {
                return attribute.StartExpanded;
            }

            return collectionType switch
            {
                SerializableCollectionType.Dictionary =>
                    !UnityHelpersSettings.ShouldStartSerializableDictionaryCollapsed(),
                SerializableCollectionType.Set =>
                    !UnityHelpersSettings.ShouldStartSerializableSetCollapsed(),
                _ => false,
            };
        }

        private readonly struct FoldoutInitializationKey : IEquatable<FoldoutInitializationKey>
        {
            public FoldoutInitializationKey(UnityEngine.Object target, string propertyPath)
            {
                TargetId = target != null ? target.GetUnityObjectId() : 0;
                PropertyPath = propertyPath ?? string.Empty;
            }

            private int TargetId { get; }

            private string PropertyPath { get; }

            public bool Equals(FoldoutInitializationKey other)
            {
                return TargetId == other.TargetId
                    && string.Equals(PropertyPath, other.PropertyPath, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is FoldoutInitializationKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return Objects.HashCode(TargetId, PropertyPath);
            }
        }

        internal enum SerializableCollectionType
        {
            Dictionary = 0,
            Set = 1,
        }
    }
}
