// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using System.Text;
    using UnityEditor;
    using UnityEditor.AnimatedValues;
    using UnityEditorInternal;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using static WallstopStudios.UnityHelpers.Editor.CustomDrawers.SerializableDictionaryPropertyDrawer;
    using Debug = UnityEngine.Debug;
    using Object = UnityEngine.Object;

    /// <summary>Provides test-side setup and inspection for SerializableDictionaryPropertyDrawer.</summary>
    internal static class SerializableDictionaryPropertyDrawerTestAccess
    {
        /// <summary>
        /// Gets the frame number when the child height changed signal was last set.
        /// For testing purposes only.
        /// </summary>
        internal static int GetChildHeightChangedFrame()
        {
            return _childHeightChangedFrame;
        }

        /// <summary>
        /// Resets the child height changed frame to -1 for testing purposes.
        /// </summary>
        internal static void ResetChildHeightChangedFrame()
        {
            _childHeightChangedFrame = -1;
        }

        internal static void ResetLayoutTracking() { }

        /// <summary>
        /// Returns the resolved pending section padding value for test diagnostics.
        /// </summary>
        internal static float GetPendingSectionPadding()
        {
            return PendingSectionPadding;
        }

        internal static bool HasDroppedValuesArray(SerializedProperty dictionaryProperty)
        {
            return SerializableDictionaryPropertyDrawer.HasDroppedValuesArray(
                new CachedPropertyPair
                {
                    keysProperty = dictionaryProperty.FindPropertyRelative(
                        SerializableDictionarySerializedPropertyNames.Keys
                    ),
                    valuesProperty = ResolveValuesArray(dictionaryProperty),
                },
                dictionaryProperty
            );
        }

        /// <summary>
        /// Returns the expected static foldout progress without animation.
        /// This static method cannot access instance animation state.
        /// Use <see cref="GetPendingFoldoutProgressFromInstance"/> for actual animation testing.
        /// </summary>
        internal static float GetPendingFoldoutProgress(
            SerializedProperty property,
            bool expanded,
            bool isSorted
        )
        {
            return SerializableDictionaryPropertyDrawer.GetPendingFoldoutProgress(
                new PendingEntry { isExpanded = expanded, isSorted = isSorted },
                property?.propertyPath
            );
        }

        /// <summary>
        /// Clears the main foldout animation cache. Used for testing purposes.
        /// </summary>
        internal static void ClearMainFoldoutAnimCache()
        {
            MainFoldoutAnimations.Clear();
        }

        /// <summary>
        /// Returns true if a main foldout AnimBool exists for the given property path and target object.
        /// </summary>
        internal static bool HasMainFoldoutAnimBool(
            SerializedObject serializedObject,
            string propertyPath
        )
        {
            MainFoldoutCacheKey cacheKey =
                SerializableDictionaryPropertyDrawer.GetMainFoldoutCacheKey(
                    serializedObject,
                    propertyPath
                );
            return MainFoldoutAnimations.ContainsKey(cacheKey);
        }

        /// <summary>
        /// Gets the main foldout cache key for testing purposes.
        /// Returns the string representation of the struct key.
        /// </summary>
        internal static string GetMainFoldoutCacheKey(
            SerializedObject serializedObject,
            string propertyPath
        )
        {
            return SerializableDictionaryPropertyDrawer
                .GetMainFoldoutCacheKey(serializedObject, propertyPath)
                .ToString();
        }

        internal static void SetRowFoldoutState(
            this SerializableDictionaryPropertyDrawer owner,
            string cacheKey,
            int globalIndex,
            bool isExpanded
        )
        {
            SerializableDictionaryPropertyDrawer.RowFoldoutKey key = BuildRowFoldoutStateKey(
                cacheKey,
                globalIndex
            );
            if (!key.IsValid)
            {
                return;
            }

            owner._rowValueFoldoutStates[key] = isExpanded;
        }

        /// <summary>
        /// Gets the pending entry's expanded state and animation information for testing.
        /// </summary>
        /// <param name="property">The serialized property for the dictionary.</param>
        /// <param name="isExpanded">Output: whether the pending section is logically expanded.</param>
        /// <param name="animProgress">Output: the current animation progress (0 to 1).</param>
        /// <param name="hasAnimBool">Output: whether an AnimBool is active for this entry.</param>
        /// <returns>True if a pending entry was found, false otherwise.</returns>
        internal static bool TryGetPendingAnimationState(
            this SerializableDictionaryPropertyDrawer owner,
            SerializedProperty property,
            out bool isExpanded,
            out float animProgress,
            out bool hasAnimBool
        )
        {
            if (property == null)
            {
                isExpanded = false;
                animProgress = 0f;
                hasAnimBool = false;
                return false;
            }

            string cacheKey = owner.GetListKey(property);
            if (
                !owner._pendingEntries.TryGetValue(
                    cacheKey,
                    out SerializableDictionaryPropertyDrawer.PendingEntry pending
                )
                || pending == null
            )
            {
                isExpanded = false;
                animProgress = 0f;
                hasAnimBool = false;
                return false;
            }

            isExpanded = pending.isExpanded;
            hasAnimBool = pending.foldoutAnim != null;
            animProgress = SerializableDictionaryPropertyDrawer.GetPendingFoldoutProgress(pending);
            return true;
        }

        /// <summary>
        /// Sets the pending entry's expanded state for testing purposes.
        /// This properly triggers animation state updates.
        /// </summary>
        internal static void SetPendingExpandedState(
            this SerializableDictionaryPropertyDrawer owner,
            SerializedProperty property,
            bool expanded
        )
        {
            if (property == null)
            {
                return;
            }

            string cacheKey = owner.GetListKey(property);
            if (
                !owner._pendingEntries.TryGetValue(
                    cacheKey,
                    out SerializableDictionaryPropertyDrawer.PendingEntry pending
                )
                || pending == null
            )
            {
                return;
            }

            pending.isExpanded = expanded;
            AnimBool anim = EnsurePendingFoldoutAnim(pending, property.propertyPath);
            if (anim != null)
            {
                anim.target = expanded;
            }
        }

        /// <summary>
        /// Gets the actual animated foldout progress from the drawer instance's pending entry.
        /// Use this for testing that animations are actually progressing over time.
        /// </summary>
        /// <param name="property">The serialized property for the dictionary.</param>
        /// <returns>
        /// The current animation progress (0 to 1), or -1 if no pending entry exists for this property.
        /// When tweening is disabled, returns 0 or 1 immediately based on expanded state.
        /// </returns>
        internal static float GetPendingFoldoutProgressFromInstance(
            this SerializableDictionaryPropertyDrawer owner,
            SerializedProperty property
        )
        {
            if (property == null)
            {
                return -1f;
            }

            string cacheKey = owner.GetListKey(property);
            if (
                !owner._pendingEntries.TryGetValue(cacheKey, out PendingEntry pending)
                || pending == null
            )
            {
                return -1f;
            }

            return SerializableDictionaryPropertyDrawer.GetPendingFoldoutProgress(pending);
        }
    }
#endif
}
