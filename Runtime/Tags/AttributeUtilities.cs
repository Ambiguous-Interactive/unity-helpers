// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Core.Extension;
    using Core.Helper;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Provides utility methods and extension methods for working with the attribute/effect system.
    /// Includes methods for applying/removing effects, checking tags, and discovering attribute fields via reflection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Key features:
    /// - Extension methods for applying effects to any Unity Object
    /// - Tag checking utilities
    /// - Reflection-based attribute field discovery with caching
    /// - Integration with AttributeMetadataCache for performance
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Extension method usage
    /// GameObject player = ...;
    /// AttributeEffect speedBoost = ...;
    ///
    /// // Apply an effect
    /// EffectHandle? handle = player.ApplyEffect(speedBoost);
    ///
    /// // Check tags
    /// if (player.HasTag("Stunned"))
    /// {
    ///     // Can't move
    /// }
    ///
    /// // Remove effect
    /// if (handle.HasValue)
    /// {
    ///     player.RemoveEffect(handle.Value);
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public static class AttributeUtilities
    {
        internal static string[] AllAttributeNames;
        internal static readonly Dictionary<Type, Dictionary<string, FieldInfo>> AttributeFields =
            new();

        private static readonly Dictionary<
            Type,
            Dictionary<string, Func<object, Attribute>>
        > OptimizedAttributeFields = new();

        /// <summary>
        /// Gets an array of all unique attribute field names across all AttributesComponent subclasses.
        /// Results are cached for performance. Uses AttributeMetadataCache if available, otherwise uses reflection.
        /// </summary>
        /// <returns>An array of all attribute names discovered in the project.</returns>
        public static string[] GetAllAttributeNames()
        {
            if (AllAttributeNames != null)
            {
                return AllAttributeNames;
            }

            // Try to load from cache first
            AttributeMetadataCache cache = AttributeMetadataCache.Instance;
            if (cache != null && 0 < cache.AllAttributeNames.Length)
            {
                AllAttributeNames = cache.AllAttributeNames;
                return AllAttributeNames;
            }

            using PooledResource<HashSet<string>> uniqueNamesLease = Buffers<string>.HashSet.Get(
                out HashSet<string> uniqueNames
            );
            uniqueNames.Clear();

            IEnumerable<Type> loadedTypes = ReflectionHelpers.GetAllLoadedTypes();
            foreach (Type type in loadedTypes)
            {
                if (
                    type == null
                    || type.IsAbstract
                    || !type.IsSubclassOf(typeof(AttributesComponent))
                )
                {
                    continue;
                }

                FieldInfo[] fields = type.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
                foreach (FieldInfo fieldInfo in fields)
                {
                    if (fieldInfo.FieldType == typeof(Attribute))
                    {
                        uniqueNames.Add(fieldInfo.Name);
                    }
                }
            }

            if (uniqueNames.Count == 0)
            {
                AllAttributeNames = Array.Empty<string>();
                return AllAttributeNames;
            }

            using PooledResource<List<string>> orderedNamesLease = Buffers<string>.GetList(
                uniqueNames.Count,
                out List<string> orderedNames
            );
            orderedNames.Clear();
            orderedNames.AddRange(uniqueNames);
            orderedNames.Sort(StringComparer.Ordinal);
            AllAttributeNames = orderedNames.ToArray();
            return AllAttributeNames;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ClearCache()
        {
            AllAttributeNames = null;
            AttributeFields.Clear();
            OptimizedAttributeFields.Clear();
        }

        /// <summary>
        /// Extension method to check if a Unity Object has a specific tag.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to check.</param>
        /// <param name="effectTag">The tag to check for.</param>
        /// <returns><c>true</c> if the target has a TagHandler with the specified tag; otherwise, <c>false</c>.</returns>
        /// <example>
        /// <code>
        /// if (player.HasTag("Stunned"))
        /// {
        ///     DisableMovement();
        /// }
        /// </code>
        /// </example>
        public static bool HasTag(this Object target, string effectTag)
        {
            if (target == null)
            {
                return false;
            }

            return target.TryGetComponent(out TagHandler tagHandler)
                && tagHandler.HasTag(effectTag);
        }

        /// <summary>
        /// Extension method to check if a Unity Object has any of the specified tags.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to check.</param>
        /// <param name="effectTags">The collection of tags to check for.</param>
        /// <returns><c>true</c> if the target has any of the specified tags; otherwise, <c>false</c>.</returns>
        /// <example>
        /// <code>
        /// string[] crowdControlTags = { "Stunned", "Frozen", "KnockedDown" };
        /// if (player.HasAnyTag(crowdControlTags))
        /// {
        ///     ShowCrowdControlUI();
        /// }
        /// </code>
        /// </example>
        public static bool HasAnyTag(this Object target, IEnumerable<string> effectTags)
        {
            if (target == null)
            {
                return false;
            }

            return target.TryGetComponent(out TagHandler tagHandler)
                && tagHandler.HasAnyTag(effectTags);
        }

        /// <summary>
        /// Extension method to check if a Unity Object has any of the specified tags (IReadOnlyList overload for performance).
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to check.</param>
        /// <param name="effectTags">The list of tags to check for.</param>
        /// <returns><c>true</c> if the target has any of the specified tags; otherwise, <c>false</c>.</returns>
        /// <remarks>Equivalent to <see cref="HasAnyTag(UnityEngine.Object,System.Collections.Generic.IEnumerable{string})"/> but optimized for indexable lists.</remarks>
        public static bool HasAnyTag(this Object target, IReadOnlyList<string> effectTags)
        {
            if (target == null)
            {
                return false;
            }

            return target.TryGetComponent(out TagHandler tagHandler)
                && tagHandler.HasAnyTag(effectTags);
        }

        /// <summary>
        /// Extension method to check if a Unity Object has all of the specified tags.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to check.</param>
        /// <param name="effectTags">The collection of tags that must all be active.</param>
        /// <returns><c>true</c> if all tags are active; otherwise, <c>false</c>.</returns>
        /// <example>
        /// <code>
        /// string[] stealthRequirements = { "Invisible", "Silenced" };
        /// if (player.HasAllTags(stealthRequirements))
        /// {
        ///     EnableBackstabBonus();
        /// }
        /// </code>
        /// </example>
        public static bool HasAllTags(this Object target, IEnumerable<string> effectTags)
        {
            if (target == null)
            {
                return false;
            }

            return target.TryGetComponent(out TagHandler tagHandler)
                && tagHandler.HasAllTags(effectTags);
        }

        /// <summary>
        /// Extension method to check if a Unity Object has all of the specified tags.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to check.</param>
        /// <param name="effectTags">The list of tags that must all be active.</param>
        /// <returns><c>true</c> if all tags are active; otherwise, <c>false</c>.</returns>
        /// <remarks>Equivalent to <see cref="HasAllTags(UnityEngine.Object,System.Collections.Generic.IEnumerable{string})"/> but optimized for indexable lists.</remarks>
        public static bool HasAllTags(this Object target, IReadOnlyList<string> effectTags)
        {
            if (target == null)
            {
                return false;
            }

            return target.TryGetComponent(out TagHandler tagHandler)
                && tagHandler.HasAllTags(effectTags);
        }

        /// <summary>
        /// Extension method to determine whether none of the specified tags are active on the target.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to check.</param>
        /// <param name="effectTags">The collection of tags to inspect.</param>
        /// <returns><c>true</c> if no tags in the collection are active; otherwise, <c>false</c>.</returns>
        public static bool HasNoneOfTags(this Object target, IEnumerable<string> effectTags)
        {
            if (target == null)
            {
                return true;
            }

            return !target.TryGetComponent(out TagHandler tagHandler)
                || tagHandler.HasNoneOfTags(effectTags);
        }

        /// <summary>
        /// Extension method to determine whether none of the specified tags are active on the target.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to check.</param>
        /// <param name="effectTags">The list of tags to inspect.</param>
        /// <returns><c>true</c> if no tags in the collection are active; otherwise, <c>false</c>.</returns>
        public static bool HasNoneOfTags(this Object target, IReadOnlyList<string> effectTags)
        {
            if (target == null)
            {
                return true;
            }

            return !target.TryGetComponent(out TagHandler tagHandler)
                || tagHandler.HasNoneOfTags(effectTags);
        }

        /// <summary>
        /// Attempts to retrieve the active count for a specific tag on the target.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to inspect.</param>
        /// <param name="effectTag">The tag whose count should be retrieved.</param>
        /// <param name="count">
        /// When this method returns, contains the tag count (cast to <see cref="int"/>) if available; otherwise, zero.
        /// </param>
        /// <returns><c>true</c> if the target has a <see cref="TagHandler"/> and the tag is tracked; otherwise, <c>false</c>.</returns>
        /// <example>
        /// <code>
        /// if (target.TryGetTagCount("Bleeding", out int stacks) && stacks >= 5)
        /// {
        ///     ApplyMajorWoundPenalty();
        /// }
        /// </code>
        /// </example>
        public static bool TryGetTagCount(this Object target, string effectTag, out int count)
        {
            if (target == null)
            {
                count = 0;
                return false;
            }

            if (!target.TryGetComponent(out TagHandler tagHandler))
            {
                count = 0;
                return false;
            }

            return tagHandler.TryGetTagCount(effectTag, out count);
        }

        /// <summary>
        /// Retrieves the active tags on the target into an optional buffer.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to inspect.</param>
        /// <param name="buffer">
        /// Optional buffer to populate. When <c>null</c>, a new list is created. The buffer is cleared before population.
        /// </param>
        /// <returns>The populated buffer of active tags. The buffer is empty when no tags are present or no handler exists.</returns>
        /// <example>
        /// <code>
        /// List&lt;string&gt; activeTags = target.GetActiveTags(_tagBuffer);
        /// if (activeTags.Contains("Invisible"))
        /// {
        ///     EnableDetectionShader();
        /// }
        /// </code>
        /// </example>
        public static List<string> GetActiveTags(this Object target, List<string> buffer = null)
        {
            if (target == null)
            {
                return buffer ?? new List<string>(0);
            }

            if (!target.TryGetComponent(out TagHandler tagHandler))
            {
                return buffer ?? new List<string>(0);
            }

            List<string> targetBuffer = buffer ?? new List<string>();
            targetBuffer.Clear();
            return tagHandler.GetActiveTags(targetBuffer);
        }

        /// <summary>
        /// Returns an allocation-free enumerable view of the active tags on the target.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to inspect.</param>
        /// <returns>A struct enumerable that yields each active tag exactly once.</returns>
        public static TagHandler.ActiveTagEnumerable EnumerateActiveTags(this Object target)
        {
            if (target == null)
            {
                return TagHandler.ActiveTagEnumerable.Empty;
            }

            return target.TryGetComponent(out TagHandler tagHandler)
                ? tagHandler.EnumerateActiveTags()
                : TagHandler.ActiveTagEnumerable.Empty;
        }

        /// <summary>
        /// Returns an allocation-free enumerable of handles that contribute the specified tag.
        /// </summary>
        public static TagHandler.HandleEnumerable EnumerateHandlesWithTag(
            this Object target,
            string effectTag
        )
        {
            if (target == null || string.IsNullOrEmpty(effectTag))
            {
                return TagHandler.HandleEnumerable.Empty;
            }

            return target.TryGetComponent(out TagHandler tagHandler)
                ? tagHandler.EnumerateHandlesWithTag(effectTag)
                : TagHandler.HandleEnumerable.Empty;
        }

        /// <summary>
        /// Retrieves all effect handles that contributed a specific tag on the target.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to inspect.</param>
        /// <param name="effectTag">The tag to query.</param>
        /// <param name="buffer">
        /// Optional buffer to populate. When <c>null</c>, a new list is created. The buffer is cleared before population.
        /// </param>
        /// <returns>The populated buffer of effect handles whose effects contain <paramref name="effectTag"/>.</returns>
        /// <example>
        /// <code>
        /// List&lt;EffectHandle&gt; taggedHandles = target.GetHandlesWithTag("Burning", _handleBuffer);
        /// foreach (EffectHandle handle in taggedHandles)
        /// {
        ///     target.RefreshEffect(handle);
        /// }
        /// </code>
        /// </example>
        public static List<EffectHandle> GetHandlesWithTag(
            this Object target,
            string effectTag,
            List<EffectHandle> buffer = null
        )
        {
            if (string.IsNullOrEmpty(effectTag))
            {
                return buffer ?? new List<EffectHandle>(0);
            }

            if (target == null)
            {
                return buffer ?? new List<EffectHandle>(0);
            }

            if (!target.TryGetComponent(out TagHandler tagHandler))
            {
                return buffer ?? new List<EffectHandle>(0);
            }

            List<EffectHandle> targetBuffer = buffer ?? new List<EffectHandle>();
            targetBuffer.Clear();
            return tagHandler.GetHandlesWithTag(effectTag, targetBuffer);
        }

        /// <summary>
        /// Extension method to apply an effect to a Unity Object.
        /// Automatically adds an EffectHandler component if one doesn't exist.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to apply the effect to.</param>
        /// <param name="attributeEffect">The effect to apply.</param>
        /// <returns>An EffectHandle for non-instant effects, or null for instant effects.</returns>
        /// <example>
        /// <code>
        /// EffectHandle? handle = enemy.ApplyEffect(poisonEffect);
        /// if (!handle.HasValue)
        /// {
        ///     // Instant effects do not return a handle
        ///     return;
        /// }
        /// activeHandles.Add(handle.Value);
        /// </code>
        /// </example>
        public static EffectHandle? ApplyEffect(this Object target, AttributeEffect attributeEffect)
        {
            if (target == null)
            {
                return null;
            }

            GameObject go = target.GetGameObject();
            if (go == null)
            {
                return null;
            }

            EffectHandler effectHandler = go.GetOrAddComponent<EffectHandler>();
            return effectHandler.ApplyEffect(attributeEffect);
        }

        /// <summary>
        /// Applies a list of effects to the target without allocating a result collection.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to modify.</param>
        /// <param name="attributeEffects">The list of effects to apply.</param>
        /// <remarks>Effects are applied sequentially; instant effects still return <c>null</c> handles internally.</remarks>
        /// <example>
        /// <code>
        /// AttributeUtilities.ApplyEffectsNoAlloc(player, _precomputedEffects);
        /// </code>
        /// </example>
        public static void ApplyEffectsNoAlloc(
            this Object target,
            List<AttributeEffect> attributeEffects
        )
        {
            if (attributeEffects is not { Count: > 0 })
            {
                return;
            }

            if (target == null)
            {
                return;
            }

            GameObject go = target.GetGameObject();
            if (go == null)
            {
                return;
            }

            EffectHandler effectHandler = go.GetOrAddComponent<EffectHandler>();
            foreach (AttributeEffect attributeEffect in attributeEffects)
            {
                _ = effectHandler.ApplyEffect(attributeEffect);
            }
        }

        /// <summary>
        /// Applies a sequence of effects to the target without allocations, iterating any enumerable.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to modify.</param>
        /// <param name="attributeEffects">The enumerable of effects to apply.</param>
        /// <remarks>Use when you are streaming effects from a generator or LINQ query.</remarks>
        public static void ApplyEffectsNoAlloc(
            this Object target,
            IEnumerable<AttributeEffect> attributeEffects
        )
        {
            if (target == null)
            {
                return;
            }

            GameObject go = target.GetGameObject();
            if (go == null)
            {
                return;
            }

            EffectHandler effectHandler = go.GetOrAddComponent<EffectHandler>();
            foreach (AttributeEffect attributeEffect in attributeEffects)
            {
                _ = effectHandler.ApplyEffect(attributeEffect);
            }
        }

        /// <summary>
        /// Applies a list of effects to the target and collects any returned handles into the supplied buffer.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to modify.</param>
        /// <param name="attributeEffects">The list of effects to apply.</param>
        /// <param name="effectHandles">Buffer that receives non-<c>null</c> handles. The buffer is not cleared automatically.</param>
        /// <example>
        /// <code>
        /// _handles.Clear();
        /// target.ApplyEffectsNoAlloc(burstEffects, _handles);
        /// // _handles now contains handles for duration and infinite effects.
        /// </code>
        /// </example>
        public static void ApplyEffectsNoAlloc(
            this Object target,
            List<AttributeEffect> attributeEffects,
            List<EffectHandle> effectHandles
        )
        {
            if (target == null)
            {
                return;
            }

            GameObject go = target.GetGameObject();
            if (go == null)
            {
                return;
            }

            EffectHandler effectHandler = go.GetOrAddComponent<EffectHandler>();
            foreach (AttributeEffect attributeEffect in attributeEffects)
            {
                EffectHandle? handle = effectHandler.ApplyEffect(attributeEffect);
                if (handle.HasValue)
                {
                    effectHandles.Add(handle.Value);
                }
            }
        }

        /// <summary>
        /// Applies a list of effects to the target and returns the collected handles.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to modify.</param>
        /// <param name="attributeEffects">The list of effects to apply.</param>
        /// <returns>A list containing handles for every duration or infinite effect that was applied.</returns>
        /// <example>
        /// <code>
        /// List&lt;EffectHandle&gt; handles = player.ApplyEffects(bossOpeners);
        /// _activeBossEffects.AddRange(handles);
        /// </code>
        /// </example>
        public static List<EffectHandle> ApplyEffects(
            this Object target,
            List<AttributeEffect> attributeEffects
        )
        {
            List<EffectHandle> handles = new(attributeEffects.Count);
            target.ApplyEffectsNoAlloc(attributeEffects, handles);
            return handles;
        }

        /// <summary>
        /// Removes a previously applied effect by handle.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to modify.</param>
        /// <param name="effectHandle">The handle returned by <see cref="ApplyEffect(UnityEngine.Object, AttributeEffect)"/>.</param>
        /// <example>
        /// <code>
        /// if (_slowHandle.HasValue)
        /// {
        ///     enemy.RemoveEffect(_slowHandle.Value);
        ///     _slowHandle = null;
        /// }
        /// </code>
        /// </example>
        public static void RemoveEffect(this Object target, EffectHandle effectHandle)
        {
            if (target == null)
            {
                return;
            }

            if (target.TryGetComponent(out EffectHandler effectHandler))
            {
                effectHandler.RemoveEffect(effectHandle);
            }
        }

        /// <summary>
        /// Removes a collection of effect handles from the target.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to modify.</param>
        /// <param name="effectHandles">Handles to remove. The list is iterated as-is.</param>
        /// <example>
        /// <code>
        /// target.RemoveEffects(_queuedDispels);
        /// _queuedDispels.Clear();
        /// </code>
        /// </example>
        public static void RemoveEffects(this Object target, List<EffectHandle> effectHandles)
        {
            if (target == null || effectHandles.Count <= 0)
            {
                return;
            }

            if (target.TryGetComponent(out EffectHandler effectHandler))
            {
                foreach (EffectHandle effectHandle in effectHandles)
                {
                    effectHandler.RemoveEffect(effectHandle);
                }
            }
        }

        /// <summary>
        /// Removes every active effect from the target.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to modify.</param>
        /// <example>
        /// <code>
        /// // Cleanse all effects when respawning the character
        /// character.RemoveAllEffects();
        /// </code>
        /// </example>
        public static void RemoveAllEffects(this Object target)
        {
            if (target == null)
            {
                return;
            }

            if (target.TryGetComponent(out EffectHandler effectHandler))
            {
                effectHandler.RemoveAllEffects();
            }
        }

        /// <summary>
        /// Determines whether the specified effect is currently active on the target.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to inspect.</param>
        /// <param name="attributeEffect">The effect to query.</param>
        /// <returns><c>true</c> if the effect is active; otherwise, <c>false</c>.</returns>
        /// <example>
        /// <code>
        /// if (!enemy.IsEffectActive(slowEffect))
        /// {
        ///     enemy.ApplyEffect(slowEffect);
        /// }
        /// </code>
        /// </example>
        public static bool IsEffectActive(this Object target, AttributeEffect attributeEffect)
        {
            if (target == null)
            {
                return false;
            }

            return target.TryGetComponent(out EffectHandler effectHandler)
                && effectHandler.IsEffectActive(attributeEffect);
        }

        /// <summary>
        /// Retrieves the number of active handles for the specified effect on the target.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to inspect.</param>
        /// <param name="attributeEffect">The effect to count.</param>
        /// <returns>The number of active handles; zero when inactive.</returns>
        /// <example>
        /// <code>
        /// int stacks = target.GetEffectStackCount(bleedEffect);
        /// bleedStacksLabel.text = stacks.ToString();
        /// </code>
        /// </example>
        public static int GetEffectStackCount(this Object target, AttributeEffect attributeEffect)
        {
            if (target == null)
            {
                return 0;
            }

            return target.TryGetComponent(out EffectHandler effectHandler)
                ? effectHandler.GetEffectStackCount(attributeEffect)
                : 0;
        }

        /// <summary>
        /// Copies all active effect handles on the target into the provided buffer.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to inspect.</param>
        /// <param name="buffer">
        /// Optional buffer to populate. When <c>null</c>, a new list is created. The buffer is cleared before population.
        /// </param>
        /// <returns>The populated buffer containing every active effect handle.</returns>
        /// <example>
        /// <code>
        /// List&lt;EffectHandle&gt; handles = target.GetActiveEffects(_handleBuffer);
        /// foreach (EffectHandle handle in handles)
        /// {
        ///     Debug.Log(handle);
        /// }
        /// </code>
        /// </example>
        public static List<EffectHandle> GetActiveEffects(
            this Object target,
            List<EffectHandle> buffer = null
        )
        {
            if (target == null)
            {
                return buffer ?? new List<EffectHandle>(0);
            }

            if (!target.TryGetComponent(out EffectHandler effectHandler))
            {
                return buffer ?? new List<EffectHandle>(0);
            }

            List<EffectHandle> targetBuffer = buffer ?? new List<EffectHandle>();
            targetBuffer.Clear();
            return effectHandler.GetActiveEffects(targetBuffer);
        }

        /// <summary>
        /// Attempts to retrieve the remaining duration for the specified effect handle on the target.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to inspect.</param>
        /// <param name="effectHandle">The handle to query.</param>
        /// <param name="remainingDuration">When this method returns, contains the remaining time if available; otherwise, zero.</param>
        /// <returns><c>true</c> if a duration was found; otherwise, <c>false</c>.</returns>
        public static bool TryGetRemainingDuration(
            this Object target,
            EffectHandle effectHandle,
            out float remainingDuration
        )
        {
            if (target == null)
            {
                remainingDuration = 0f;
                return false;
            }

            if (!target.TryGetComponent(out EffectHandler effectHandler))
            {
                remainingDuration = 0f;
                return false;
            }

            return effectHandler.TryGetRemainingDuration(effectHandle, out remainingDuration);
        }

        /// <summary>
        /// Ensures an effect handle exists on the target, adding an EffectHandler when necessary.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to modify.</param>
        /// <param name="attributeEffect">The effect to apply or refresh.</param>
        /// <returns>An active handle for the effect, or <c>null</c> for instant effects.</returns>
        public static EffectHandle? EnsureHandle(
            this Object target,
            AttributeEffect attributeEffect
        )
        {
            return target.EnsureHandle(attributeEffect, refreshDuration: true);
        }

        /// <summary>
        /// Ensures an effect handle exists on the target, adding an EffectHandler when necessary.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to modify.</param>
        /// <param name="attributeEffect">The effect to apply or refresh.</param>
        /// <param name="refreshDuration">
        /// When <c>true</c>, attempts to refresh the duration of an existing handle if the effect supports it.
        /// </param>
        /// <returns>An active handle for the effect, or <c>null</c> for instant effects.</returns>
        public static EffectHandle? EnsureHandle(
            this Object target,
            AttributeEffect attributeEffect,
            bool refreshDuration
        )
        {
            if (target == null)
            {
                return null;
            }

            GameObject go = target.GetGameObject();
            if (go == null)
            {
                return null;
            }

            EffectHandler effectHandler = go.GetOrAddComponent<EffectHandler>();
            return effectHandler.EnsureHandle(attributeEffect, refreshDuration);
        }

        /// <summary>
        /// Attempts to refresh the duration of an effect handle on the target.
        /// </summary>
        /// <param name="target">The Unity Object (GameObject or Component) to inspect.</param>
        /// <param name="effectHandle">The handle to refresh.</param>
        /// <param name="ignoreReapplicationPolicy">
        /// When <c>true</c>, refreshes the duration even if the effect disallows reapplication resets.
        /// </param>
        /// <returns><c>true</c> if the duration was refreshed; otherwise, <c>false</c>.</returns>
        /// <example>
        /// <code>
        /// if (!target.RefreshEffect(handle))
        /// {
        ///     target.RefreshEffect(handle, ignoreReapplicationPolicy: true);
        /// }
        /// </code>
        /// </example>
        public static bool RefreshEffect(
            this Object target,
            EffectHandle effectHandle,
            bool ignoreReapplicationPolicy = false
        )
        {
            if (target == null)
            {
                return false;
            }

            return target.TryGetComponent(out EffectHandler effectHandler)
                && effectHandler.RefreshEffect(effectHandle, ignoreReapplicationPolicy);
        }

        /// <summary>
        /// Retrieves a dictionary of attribute fields for the specified component type, keyed by field name.
        /// Uses cached metadata when available and falls back to reflection otherwise.
        /// </summary>
        /// <param name="type">Component type that declares <see cref="Attribute"/> fields.</param>
        /// <returns>A dictionary mapping attribute field names to their <see cref="FieldInfo"/>.</returns>
        /// <example>
        /// <code>
        /// Dictionary&lt;string, FieldInfo&gt; fields = AttributeUtilities.GetAttributeFields(typeof(CharacterStats));
        /// if (fields.TryGetValue("Health", out FieldInfo healthField))
        /// {
        ///     Debug.Log($"Health base value: {healthField.GetValue(stats)}");
        /// }
        /// </code>
        /// </example>
        public static Dictionary<string, FieldInfo> GetAttributeFields(Type type)
        {
            return AttributeFields.GetOrAdd(
                type,
                static inputType =>
                {
                    // Try to use cached field names first
                    AttributeMetadataCache cache = AttributeMetadataCache.Instance;
                    if (cache != null && cache.TryGetFieldNames(inputType, out string[] fieldNames))
                    {
                        // Build dictionary from cached field names
                        Dictionary<string, FieldInfo> result = new(
                            fieldNames.Length,
                            StringComparer.Ordinal
                        );
                        foreach (string fieldName in fieldNames)
                        {
                            FieldInfo field = inputType.GetField(
                                fieldName,
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                            );
                            if (field != null && field.FieldType == typeof(Attribute))
                            {
                                result[fieldName] = field;
                            }
                        }
                        return result;
                    }
                    else
                    {
                        // Fallback to runtime reflection
                        FieldInfo[] fields = inputType.GetFields(
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        );
                        Dictionary<string, FieldInfo> result = new(
                            fields.Length,
                            StringComparer.Ordinal
                        );
                        for (int i = 0; i < fields.Length; i++)
                        {
                            FieldInfo field = fields[i];
                            if (field.FieldType == typeof(Attribute))
                            {
                                result[field.Name] = field;
                            }
                        }

                        return result;
                    }
                }
            );
        }

        /// <summary>
        /// Retrieves attribute fields for the specified component type with compiled getters for fast access.
        /// Prefers cached metadata and generates delegates on demand when the cache is unavailable.
        /// </summary>
        /// <param name="type">Component type that declares <see cref="Attribute"/> fields.</param>
        /// <returns>A dictionary mapping attribute field names to compiled getter delegates.</returns>
        /// <example>
        /// <code>
        /// Dictionary&lt;string, Func&lt;object, Attribute&gt;&gt; getters = AttributeUtilities.GetOptimizedAttributeFields(typeof(CharacterStats));
        /// Attribute health = getters["Health"](stats);
        /// Debug.Log($"Current health: {health.CurrentValue}");
        /// </code>
        /// </example>
        public static Dictionary<string, Func<object, Attribute>> GetOptimizedAttributeFields(
            Type type
        )
        {
            return OptimizedAttributeFields.GetOrAdd(
                type,
                static inputType =>
                {
                    // Try to use cached field names first
                    AttributeMetadataCache cache = AttributeMetadataCache.Instance;
                    if (cache != null && cache.TryGetFieldNames(inputType, out string[] fieldNames))
                    {
                        // Build dictionary from cached field names
                        Dictionary<string, Func<object, Attribute>> result = new(
                            fieldNames.Length,
                            StringComparer.Ordinal
                        );
                        foreach (string fieldName in fieldNames)
                        {
                            FieldInfo field = inputType.GetField(
                                fieldName,
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                            );
                            if (field != null && field.FieldType == typeof(Attribute))
                            {
                                result[fieldName] = ReflectionHelpers.GetFieldGetter<
                                    object,
                                    Attribute
                                >(field);
                            }
                        }

                        return result;
                    }
                    else
                    {
                        // Fallback to runtime reflection
                        FieldInfo[] fields = inputType.GetFields(
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        );
                        Dictionary<string, Func<object, Attribute>> result = new(
                            fields.Length,
                            StringComparer.Ordinal
                        );
                        for (int i = 0; i < fields.Length; i++)
                        {
                            FieldInfo field = fields[i];
                            if (field.FieldType == typeof(Attribute))
                            {
                                result[field.Name] = ReflectionHelpers.GetFieldGetter<
                                    object,
                                    Attribute
                                >(field);
                            }
                        }

                        return result;
                    }
                }
            );
        }
    }
}
