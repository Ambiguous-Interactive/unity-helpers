// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.ExceptionServices;
    using Core.Extension;
    using UnityEngine;
    using Utils;

    /// <summary>
    /// Tag system for gameplay state: applies, counts, and queries string-based tags on a GameObject.
    /// Used to represent transient states (stunned, poisoned) and effect categories without coupling to specific effects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why tags? Tags decouple “what is active” from “what applied it.” Systems can ask
    /// “is Stunned?” or “has any of X,Y?” without caring which effect created the state.
    /// This enables clean gating (e.g., block movement while Stunned) and cross‑system coordination.
    /// </para>
    /// <para>
    /// Counting semantics: TagHandler maintains a reference count per tag. Multiple effects can apply the
    /// same tag concurrently; the tag remains active until its count returns to 0. This solves common issues
    /// where removing one source would accidentally clear the state still required by another effect.
    /// </para>
    /// <para>
    /// Integration: The <see cref="EffectHandler"/> coordinates tag application/removal via
    /// <see cref="ForceApplyTags(EffectHandle)"/> and <see cref="ForceRemoveTags(EffectHandle)"/>.
    /// Instant effects can call <see cref="ForceApplyEffect(AttributeEffect)"/> since no handle exists.
    /// </para>
    /// <para>
    /// Benefits:
    /// - Decoupled state queries across systems (AI, input, animation)
    /// - Safe stacking via counts (no premature clears)
    /// - Lightweight string keys with event notifications for UI/FX
    /// - Optimized overloads for common collection types
    /// </para>
    /// <para>
    /// Usage examples:
    /// <code>
    /// TagHandler tags = gameObject.GetComponent&lt;TagHandler&gt;();
    ///
    /// // Querying
    /// if (tags.HasTag("Stunned")) { /* disable input */ }
    /// if (tags.HasAnyTag(new [] { "Frozen", "Stunned" })) { /* play break-free anim */ }
    ///
    /// // Manual application (advanced; normally applied via EffectHandler)
    /// tags.ApplyTag("Poisoned");
    /// tags.RemoveTag("Poisoned", allInstances: false);
    ///
    /// // Events for UI/telemetry
    /// tags.OnTagAdded += tag => Debug.Log($"+{tag}");
    /// tags.OnTagRemoved += tag => Debug.Log($"-{tag}");
    /// tags.OnTagCountChanged += (tag, count) => Debug.Log($"{tag}: {count}");
    /// </code>
    /// </para>
    /// <para>
    /// Tips:
    /// - Keep tag strings consistent (consider central constants to avoid typos).
    /// - Prefer using AttributeEffects to drive tags rather than calling ApplyTag/RemoveTag directly.
    /// - Use <see cref="HasAnyTag(System.Collections.Generic.IReadOnlyList{string})"/> for perf‑critical code.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TagHandler : MonoBehaviour
    {
        /// <summary>
        /// Invoked when a tag is first applied (count goes from 0 to 1).
        /// </summary>
        public event Action<string> OnTagAdded;

        /// <summary>
        /// Invoked when a tag is completely removed (count goes from 1 to 0).
        /// </summary>
        public event Action<string> OnTagRemoved;

        /// <summary>
        /// Invoked when a tag's count changes but remains above 0.
        /// Provides the tag name and the new count.
        /// </summary>
        public event Action<string, uint> OnTagCountChanged;

        /// <summary>
        /// Gets a read-only collection of all currently active tags (tags with count > 0).
        /// </summary>
        public IReadOnlyCollection<string> Tags => _tagCount.Keys;

        [SerializeField]
        private List<string> _initialEffectTags = new();

        private readonly Dictionary<string, uint> _tagCount = new(StringComparer.Ordinal);
        private readonly Dictionary<long, EffectHandle> _effectHandles = new();
        private bool _retired;

        private readonly Dictionary<long, PooledResource<List<string>>> _appliedTagsByHandle =
            new();

        /// <summary>
        /// Checks whether the specified tag is currently active (has a count > 0).
        /// </summary>
        /// <param name="effectTag">The tag to check for.</param>
        /// <returns><c>true</c> if the tag is active; otherwise, <c>false</c>. Returns <c>false</c> for null or empty strings.</returns>
        public bool HasTag(string effectTag)
        {
            if (string.IsNullOrEmpty(effectTag))
            {
                return false;
            }

            return _tagCount.ContainsKey(effectTag);
        }

        /// <summary>
        /// Checks whether any of the specified tags are currently active.
        /// Optimized for different collection types with specialized implementations.
        /// </summary>
        /// <param name="effectTags">The collection of tags to check.</param>
        /// <returns><c>true</c> if any of the tags are active; otherwise, <c>false</c>.</returns>
        public bool HasAnyTag(IEnumerable<string> effectTags)
        {
            switch (effectTags)
            {
                case IReadOnlyList<string> list:
                {
                    return HasAnyTag(list);
                }
                case HashSet<string> hashSet:
                {
                    foreach (string effectTag in hashSet)
                    {
                        if (string.IsNullOrEmpty(effectTag))
                        {
                            continue;
                        }
                        if (_tagCount.ContainsKey(effectTag))
                        {
                            return true;
                        }
                    }

                    return false;
                }
                case SortedSet<string> sortedSet:
                {
                    foreach (string effectTag in sortedSet)
                    {
                        if (string.IsNullOrEmpty(effectTag))
                        {
                            continue;
                        }
                        if (_tagCount.ContainsKey(effectTag))
                        {
                            return true;
                        }
                    }

                    return false;
                }
                case Queue<string> queue:
                {
                    foreach (string effectTag in queue)
                    {
                        if (string.IsNullOrEmpty(effectTag))
                        {
                            continue;
                        }
                        if (_tagCount.ContainsKey(effectTag))
                        {
                            return true;
                        }
                    }

                    return false;
                }
                case Stack<string> stack:
                {
                    foreach (string effectTag in stack)
                    {
                        if (string.IsNullOrEmpty(effectTag))
                        {
                            continue;
                        }
                        if (_tagCount.ContainsKey(effectTag))
                        {
                            return true;
                        }
                    }

                    return false;
                }
                case LinkedList<string> linkedList:
                {
                    foreach (string effectTag in linkedList)
                    {
                        if (string.IsNullOrEmpty(effectTag))
                        {
                            continue;
                        }
                        if (_tagCount.ContainsKey(effectTag))
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }

            foreach (string effectTag in effectTags)
            {
                if (string.IsNullOrEmpty(effectTag))
                {
                    continue;
                }
                if (_tagCount.ContainsKey(effectTag))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether any of the specified tags are currently active.
        /// Optimized for IReadOnlyList with index-based iteration.
        /// </summary>
        /// <param name="effectTags">The list of tags to check.</param>
        /// <returns><c>true</c> if any of the tags are active; otherwise, <c>false</c>.</returns>
        public bool HasAnyTag(IReadOnlyList<string> effectTags)
        {
            for (int i = 0; i < effectTags.Count; ++i)
            {
                string effectTag = effectTags[i];
                if (string.IsNullOrEmpty(effectTag))
                {
                    continue;
                }

                if (_tagCount.ContainsKey(effectTag))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether all of the specified tags are currently active.
        /// Optimized for different collection types with specialized implementations.
        /// </summary>
        /// <param name="effectTags">The collection of tags to check.</param>
        /// <returns><c>true</c> if all tags are active; otherwise, <c>false</c>. Returns <c>false</c> when <paramref name="effectTags"/> is <c>null</c>.</returns>
        public bool HasAllTags(IEnumerable<string> effectTags)
        {
            if (effectTags == null)
            {
                return false;
            }

            switch (effectTags)
            {
                case IReadOnlyList<string> list:
                {
                    return HasAllTags(list);
                }
                case HashSet<string> hashSet:
                {
                    foreach (string effectTag in hashSet)
                    {
                        if (string.IsNullOrEmpty(effectTag))
                        {
                            continue;
                        }
                        if (!_tagCount.ContainsKey(effectTag))
                        {
                            return false;
                        }
                    }

                    return true;
                }
                case SortedSet<string> sortedSet:
                {
                    foreach (string effectTag in sortedSet)
                    {
                        if (string.IsNullOrEmpty(effectTag))
                        {
                            continue;
                        }
                        if (!_tagCount.ContainsKey(effectTag))
                        {
                            return false;
                        }
                    }

                    return true;
                }
                case Queue<string> queue:
                {
                    foreach (string effectTag in queue)
                    {
                        if (string.IsNullOrEmpty(effectTag))
                        {
                            continue;
                        }
                        if (!_tagCount.ContainsKey(effectTag))
                        {
                            return false;
                        }
                    }

                    return true;
                }
                case Stack<string> stack:
                {
                    foreach (string effectTag in stack)
                    {
                        if (string.IsNullOrEmpty(effectTag))
                        {
                            continue;
                        }
                        if (!_tagCount.ContainsKey(effectTag))
                        {
                            return false;
                        }
                    }

                    return true;
                }
                case LinkedList<string> linkedList:
                {
                    foreach (string effectTag in linkedList)
                    {
                        if (string.IsNullOrEmpty(effectTag))
                        {
                            continue;
                        }
                        if (!_tagCount.ContainsKey(effectTag))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }

            foreach (string effectTag in effectTags)
            {
                if (string.IsNullOrEmpty(effectTag))
                {
                    continue;
                }
                if (!_tagCount.ContainsKey(effectTag))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks whether all of the specified tags are active.
        /// Optimized for IReadOnlyList with index-based iteration.
        /// </summary>
        /// <param name="effectTags">The list of tags to check.</param>
        /// <returns><c>true</c> if all of the tags are active, or if the list is empty; otherwise, <c>false</c>.</returns>
        public bool HasAllTags(IReadOnlyList<string> effectTags)
        {
            if (effectTags == null)
            {
                return false;
            }

            for (int i = 0; i < effectTags.Count; ++i)
            {
                string effectTag = effectTags[i];
                if (string.IsNullOrEmpty(effectTag))
                {
                    continue;
                }

                if (!_tagCount.ContainsKey(effectTag))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether none of the specified tags are active.
        /// </summary>
        /// <param name="effectTags">The collection of tags to inspect.</param>
        /// <returns>
        /// <c>true</c> when the collection is <c>null</c>, empty, or every tag is currently inactive; otherwise, <c>false</c>.
        /// </returns>
        /// <example>
        /// <code>
        /// if (tagHandler.HasNoneOfTags(new[] { "Stunned", "Frozen" }))
        /// {
        ///     EnablePlayerInput();
        /// }
        /// </code>
        /// </example>
        public bool HasNoneOfTags(IEnumerable<string> effectTags)
        {
            if (effectTags == null)
            {
                return true;
            }

            return !HasAnyTag(effectTags);
        }

        /// <summary>
        /// Determines whether none of the specified tags are active.
        /// </summary>
        /// <param name="effectTags">The list of tags to inspect.</param>
        /// <returns>
        /// <c>true</c> when the list is <c>null</c>, empty, or every tag is currently inactive; otherwise, <c>false</c>.
        /// </returns>
        public bool HasNoneOfTags(IReadOnlyList<string> effectTags)
        {
            if (effectTags == null)
            {
                return true;
            }

            return !HasAnyTag(effectTags);
        }

        /// <summary>
        /// Attempts to retrieve the active instance count for the specified tag.
        /// </summary>
        /// <param name="effectTag">The tag whose count should be retrieved.</param>
        /// <param name="count">
        /// When this method returns, contains the active count for the tag (cast to <see cref="int"/>) if found; otherwise, zero.
        /// </param>
        /// <returns><c>true</c> if the tag is currently tracked; otherwise, <c>false</c>.</returns>
        /// <example>
        /// <code>
        /// if (tagHandler.TryGetTagCount("Poisoned", out int stacks) && stacks >= 3)
        /// {
        ///     TriggerCriticalWarning();
        /// }
        /// </code>
        /// </example>
        public bool TryGetTagCount(string effectTag, out int count)
        {
            if (string.IsNullOrEmpty(effectTag))
            {
                count = default;
                return false;
            }

            if (_tagCount.TryGetValue(effectTag, out uint uintCount))
            {
                count = unchecked((int)uintCount);
                return true;
            }

            count = default;
            return false;
        }

        /// <summary>
        /// Retrieves the set of currently active tags into an optional buffer.
        /// </summary>
        /// <param name="buffer">
        /// Optional list to populate. When <c>null</c>, a new list is created. The buffer is cleared before population.
        /// </param>
        /// <returns>The populated buffer containing all active tags.</returns>
        /// <example>
        /// <code>
        /// List&lt;string&gt; activeTags = tagHandler.GetActiveTags(_reusableTagBuffer);
        /// if (activeTags.Contains("Rooted"))
        /// {
        ///     DisableMovement();
        /// }
        /// </code>
        /// </example>
        public List<string> GetActiveTags(List<string> buffer = null)
        {
            List<string> target = buffer;
            if (target != null)
            {
                target.Clear();
            }

            int tagCount = _tagCount.Count;
            if (tagCount == 0)
            {
                return target ?? new List<string>(0);
            }

            if (target == null)
            {
                target = new List<string>(tagCount);
            }
            else if (target.Capacity < tagCount)
            {
                target.Capacity = tagCount;
            }

            foreach (KeyValuePair<string, uint> entry in _tagCount)
            {
                if (entry.Value == 0)
                {
                    continue;
                }

                target.Add(entry.Key);
            }

            return target;
        }

        /// <summary>
        /// Collects all active effect handles that currently contribute the specified tag.
        /// </summary>
        /// <param name="effectTag">The tag to query.</param>
        /// <param name="buffer">
        /// Optional list to populate. When <c>null</c>, a new list is created. The buffer is cleared before population.
        /// </param>
        /// <returns>The populated buffer containing matching effect handles, or an empty buffer for an invalid tag.</returns>
        /// <example>
        /// <code>
        /// List&lt;EffectHandle&gt; handles = tagHandler.GetHandlesWithTag("Burning", _handleBuffer);
        /// foreach (EffectHandle handle in handles)
        /// {
        ///     effectHandler.RemoveEffect(handle);
        /// }
        /// </code>
        /// </example>
        public List<EffectHandle> GetHandlesWithTag(
            string effectTag,
            List<EffectHandle> buffer = null
        )
        {
            List<EffectHandle> target = buffer;
            if (target != null)
            {
                target.Clear();
            }

            if (string.IsNullOrEmpty(effectTag) || _effectHandles.Count == 0)
            {
                return target ?? new List<EffectHandle>(0);
            }

            int estimatedCapacity = Math.Min(_effectHandles.Count, 8);
            foreach (EffectHandle handle in _effectHandles.Values)
            {
                if (HasAppliedTag(handle.id, effectTag))
                {
                    target ??= new List<EffectHandle>(estimatedCapacity);
                    target.Add(handle);
                }
            }

            return target ?? new List<EffectHandle>(0);
        }

        /// <summary>
        /// Applies a tag, incrementing its count. If the tag is new, raises <see cref="OnTagAdded"/>.
        /// Otherwise, raises <see cref="OnTagCountChanged"/>.
        /// </summary>
        /// <param name="effectTag">The tag to apply.</param>
        public void ApplyTag(string effectTag)
        {
            InternalApplyTag(effectTag);
        }

        /// <summary>
        /// Removes current contributing effect applications and untracked instances of the specified tag.
        /// </summary>
        /// <remarks>Tracked applications created during removal callbacks remain active, including reapplications of the same handle.</remarks>
        /// <param name="effectTag">The tag to remove.</param>
        /// <param name="buffer">
        /// Optional list that receives the handles whose effects applied <paramref name="effectTag"/>.
        /// When <c>null</c>, a new list is created. The buffer is cleared before population.
        /// </param>
        /// <returns>
        /// The populated buffer of handles whose tags were removed. The buffer is empty when the tag was not active.
        /// </returns>
        /// <example>
        /// <code>
        /// List&lt;EffectHandle&gt; dispelled = tagHandler.RemoveTag("Stunned", _handles);
        /// foreach (EffectHandle handle in dispelled)
        /// {
        ///     NotifyDispel(handle);
        /// }
        /// </code>
        /// </example>
        public List<EffectHandle> RemoveTag(string effectTag, List<EffectHandle> buffer = null)
        {
            if (string.IsNullOrEmpty(effectTag))
            {
                if (buffer != null)
                {
                    buffer.Clear();
                    return buffer;
                }

                return new List<EffectHandle>(0);
            }

            List<EffectHandle> target = buffer;
            if (target != null)
            {
                target.Clear();
            }

            Exception firstFailure = null;
            int handleCount = _effectHandles.Count;
            if (0 < handleCount)
            {
                int estimatedCapacity = Math.Min(handleCount, 8);
                foreach (EffectHandle handle in _effectHandles.Values)
                {
                    if (HasAppliedTag(handle.id, effectTag))
                    {
                        target ??= new List<EffectHandle>(estimatedCapacity);
                        target.Add(handle);
                    }
                }

                if (target != null && 0 < target.Count)
                {
                    // Snapshot the buffer before callbacks can reenter and replace its contents.
                    using PooledResource<
                        List<(EffectHandle handle, PooledResource<List<string>> owner)>
                    > scratchLease = Buffers<(
                        EffectHandle handle,
                        PooledResource<List<string>> owner
                    )>.List.Get(
                        out List<(EffectHandle handle, PooledResource<List<string>> owner)> scratch
                    );
                    foreach (EffectHandle handle in target)
                    {
                        if (
                            _appliedTagsByHandle.TryGetValue(
                                handle.id,
                                out PooledResource<List<string>> owner
                            )
                        )
                        {
                            scratch.Add((handle, owner));
                        }
                    }
                    foreach (
                        (EffectHandle handle, PooledResource<List<string>> owner) entry in scratch
                    )
                    {
                        if (!entry.owner.IsHeld)
                        {
                            continue;
                        }
                        try
                        {
                            _ = ForceRemoveTags(entry.handle);
                        }
                        catch (Exception handleFailure)
                        {
                            firstFailure = TeardownFailures.KeepFirst(
                                this,
                                firstFailure,
                                handleFailure
                            );
                        }
                    }

                    // Restore this call's results after reentrant calls reuse the caller buffer.
                    target.Clear();
                    foreach (
                        (EffectHandle handle, PooledResource<List<string>> owner) entry in scratch
                    )
                    {
                        target.Add(entry.handle);
                    }
                }
            }

            try
            {
                RemoveUntrackedTagInstances(effectTag);
            }
            catch (Exception tagFailure)
            {
                firstFailure = TeardownFailures.KeepFirst(this, firstFailure, tagFailure);
            }

            if (firstFailure != null)
            {
                ExceptionDispatchInfo.Capture(firstFailure).Throw();
            }

            return target ?? new List<EffectHandle>(0);
        }

        /// <summary>
        /// Provides an allocation-free view of handles contributing the specified tag.
        /// </summary>
        /// <param name="effectTag">The tag to query.</param>
        /// <remarks>
        /// <b>Read-only for the duration of the loop.</b> This walks the live handle table, so
        /// removing an effect from inside the loop -- which the
        /// <see cref="GetHandlesWithTag(string, List{EffectHandle})"/> example does, and which is
        /// the obvious thing to want -- mutates the collection being enumerated and leaves the rest
        /// of the handles unvisited. Take the buffered overload for that: it copies first, and the
        /// buffer is the caller's, so it costs nothing per call either.
        /// </remarks>
        public HandleEnumerable EnumerateHandlesWithTag(string effectTag)
        {
            if (string.IsNullOrEmpty(effectTag) || _effectHandles.Count == 0)
            {
                return HandleEnumerable.Empty;
            }

            return new HandleEnumerable(_effectHandles.GetEnumerator(), effectTag, this);
        }

        /// <summary>
        /// Applies all tags from an effect handle's effect.
        /// Tracks the handle to support later removal via <see cref="ForceRemoveTags"/>.
        /// </summary>
        /// <param name="handle">The effect handle containing tags to apply.</param>
        public void ForceApplyTags(EffectHandle handle)
        {
            long id = handle.id;
            if (_retired || !_effectHandles.TryAdd(id, handle))
            {
                return;
            }

            PooledResource<List<string>> appliedLease = Buffers<string>.List.Get(out _);
            _appliedTagsByHandle[id] = appliedLease;
            try
            {
                ApplyTrackedEffectTags(handle.effect, appliedLease);
            }
            catch (Exception applyFailure)
            {
                // Undo only tags this call raised; reentrant teardown may already have unwound them.
                if (appliedLease.IsHeld && _effectHandles.Remove(id))
                {
                    try
                    {
                        RemoveTrackedTags(id);
                    }
                    catch (Exception unwindFailure)
                    {
                        // Preserve the application failure while logging secondary teardown failures.
                        _ = TeardownFailures.KeepFirst(this, applyFailure, unwindFailure);
                    }
                }

                throw;
            }
        }

        /// <summary>
        /// Applies all tags from an effect without tracking a handle.
        /// Used for instant effects that don't need removal tracking.
        /// </summary>
        /// <param name="effect">The effect containing tags to apply.</param>
        public void ForceApplyEffect(AttributeEffect effect)
        {
            ApplyEffectTags(effect);
        }

        /// <summary>
        /// Removes the tags recorded when the handle was applied, even if its effect asset changes or is destroyed.
        /// </summary>
        /// <param name="handle">The effect handle whose tags should be removed.</param>
        /// <returns><c>true</c> if the handle was found and tags were removed; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// Every tag comes off even when an <see cref="OnTagRemoved"/> or
        /// <see cref="OnTagCountChanged"/> subscriber throws. The first exception is rethrown once
        /// the last tag is removed, and any later one is logged.
        /// </remarks>
        public bool ForceRemoveTags(EffectHandle handle)
        {
            long id = handle.id;
            if (!_effectHandles.Remove(id))
            {
                return false;
            }

            RemoveTrackedTags(id);
            return true;
        }

        /// <summary>
        /// Provides an allocation-free enumerable view of the currently active tags.
        /// </summary>
        /// <returns>A struct enumerable that yields each active tag exactly once.</returns>
        /// <remarks>
        /// <b>Read-only for the duration of the loop.</b> This walks the live tag table, so
        /// applying or removing a tag from inside the loop -- reacting to an observed tag, which
        /// is the obvious thing to want -- mutates the collection being enumerated and leaves the
        /// rest of the tags unvisited. Take the buffered
        /// <see cref="GetActiveTags(List{string})"/> overload for that: it copies first, and the
        /// buffer is the caller's, so it costs nothing per call either.
        /// </remarks>
        public ActiveTagEnumerable EnumerateActiveTags()
        {
            if (_tagCount.Count == 0)
            {
                return ActiveTagEnumerable.Empty;
            }

            return new ActiveTagEnumerable(_tagCount);
        }

        private void Awake()
        {
            if (_initialEffectTags is { Count: > 0 })
            {
                foreach (string effectTag in _initialEffectTags)
                {
                    InternalApplyTag(effectTag);
                }
            }
        }

        private void InternalApplyTag(string effectTag)
        {
            NotifyTagApplied(effectTag, RaiseTagCount(effectTag));
        }

        private uint RaiseTagCount(string effectTag)
        {
            return _tagCount.AddOrUpdate(
                effectTag,
                static _ => 1U,
                static (_, existing) => existing + 1
            );
        }

        private void NotifyTagApplied(string effectTag, uint currentCount)
        {
            if (currentCount == 1)
            {
                OnTagAdded?.Invoke(effectTag);
            }
            else
            {
                OnTagCountChanged?.Invoke(effectTag, currentCount);
            }
        }

        private void InternalRemoveTag(string effectTag, bool allInstances)
        {
            if (!_tagCount.TryGetValue(effectTag, out uint count))
            {
                return;
            }

            if (count != 0)
            {
                if (!allInstances)
                {
                    --count;
                }
                else
                {
                    count = 0;
                }
            }

            if (count == 0)
            {
                _ = _tagCount.Remove(effectTag);
                OnTagRemoved?.Invoke(effectTag);
            }
            else
            {
                _tagCount[effectTag] = count;
                OnTagCountChanged?.Invoke(effectTag, count);
            }
        }

        private void ApplyEffectTags(AttributeEffect effect)
        {
            if (_retired || effect == null)
            {
                return;
            }

            if (effect.effectTags == null)
            {
                return;
            }

            using PooledResource<List<string>> sourceLease = Buffers<string>.List.Get(
                out List<string> source
            );
            source.AddRange(effect.effectTags);
            foreach (string effectTag in source)
            {
                if (_retired)
                {
                    return;
                }

                InternalApplyTag(effectTag);
            }
        }

        private bool HasAppliedTag(long id, string effectTag)
        {
            return _appliedTagsByHandle.TryGetValue(id, out PooledResource<List<string>> lease)
                && lease.resource.Contains(effectTag);
        }

        private void ApplyTrackedEffectTags(
            AttributeEffect effect,
            PooledResource<List<string>> appliedLease
        )
        {
            if (effect == null || effect.effectTags == null)
            {
                return;
            }

            using PooledResource<List<string>> sourceLease = Buffers<string>.List.Get(
                out List<string> source
            );
            source.AddRange(effect.effectTags);
            List<string> applied = appliedLease.resource;
            foreach (string effectTag in source)
            {
                if (!appliedLease.IsHeld)
                {
                    return;
                }

                uint currentCount = RaiseTagCount(effectTag);
                applied.Add(effectTag);
                NotifyTagApplied(effectTag, currentCount);
            }
        }

        private void RemoveTrackedTags(long id)
        {
            if (!_appliedTagsByHandle.Remove(id, out PooledResource<List<string>> lease))
            {
                return;
            }

            using (lease)
            {
                Exception firstFailure = null;
                foreach (string effectTag in lease.resource)
                {
                    try
                    {
                        InternalRemoveTag(effectTag, allInstances: false);
                    }
                    catch (Exception tagFailure)
                    {
                        firstFailure = TeardownFailures.KeepFirst(this, firstFailure, tagFailure);
                    }
                }

                if (firstFailure != null)
                {
                    ExceptionDispatchInfo.Capture(firstFailure).Throw();
                }
            }
        }

        private void RemoveUntrackedTagInstances(string effectTag)
        {
            if (!_tagCount.TryGetValue(effectTag, out uint currentCount))
            {
                return;
            }
            uint trackedCount = 0;
            foreach (PooledResource<List<string>> owner in _appliedTagsByHandle.Values)
            {
                foreach (string appliedTag in owner.resource)
                {
                    if (string.Equals(appliedTag, effectTag, StringComparison.Ordinal))
                    {
                        ++trackedCount;
                    }
                }
            }
            if (trackedCount == 0)
            {
                InternalRemoveTag(effectTag, allInstances: true);
            }
            else if (trackedCount < currentCount)
            {
                _tagCount[effectTag] = trackedCount;
                OnTagCountChanged?.Invoke(effectTag, trackedCount);
            }
        }

        private void OnDestroy()
        {
            _retired = true;
            if (_appliedTagsByHandle.Count == 0)
            {
                _effectHandles.Clear();
                _tagCount.Clear();
                return;
            }
            using PooledResource<List<PooledResource<List<string>>>> lease = Buffers<
                PooledResource<List<string>>
            >.List.Get(out List<PooledResource<List<string>>> snapshots);
            snapshots.AddRange(_appliedTagsByHandle.Values);
            _appliedTagsByHandle.Clear();
            _effectHandles.Clear();
            _tagCount.Clear();
            foreach (PooledResource<List<string>> snapshot in snapshots)
            {
                snapshot.Dispose();
            }
        }

        /// <summary>
        /// Struct-backed enumerable over the active tags without additional allocations.
        /// </summary>
        public readonly struct ActiveTagEnumerable
        {
            public static ActiveTagEnumerable Empty => new ActiveTagEnumerable(null);

            private readonly Dictionary<string, uint> _source;

            internal ActiveTagEnumerable(Dictionary<string, uint> source)
            {
                _source = source;
            }

            public ActiveTagEnumerator GetEnumerator()
            {
                if (_source == null || _source.Count == 0)
                {
                    return default;
                }

                return new ActiveTagEnumerator(_source.GetEnumerator());
            }
        }

        /// <summary>
        /// Struct-backed enumerable over effect handles that contribute a specific tag.
        /// </summary>
        public readonly struct HandleEnumerable
        {
            public static HandleEnumerable Empty =>
                new HandleEnumerable(default, string.Empty, null);

            private readonly Dictionary<long, EffectHandle>.Enumerator _enumerator;
            private readonly string _effectTag;
            private readonly TagHandler _owner;
            private readonly bool _hasData;

            internal HandleEnumerable(
                Dictionary<long, EffectHandle>.Enumerator enumerator,
                string effectTag,
                TagHandler owner
            )
            {
                _enumerator = enumerator;
                _effectTag = effectTag;
                _owner = owner;
                _hasData = true;
            }

            public HandleEnumerator GetEnumerator()
            {
                if (!_hasData || string.IsNullOrEmpty(_effectTag))
                {
                    return default;
                }

                return new HandleEnumerator(_enumerator, _effectTag, _owner);
            }
        }

        /// <summary>
        /// Enumerator that filters effect handles by tag without temporary lists.
        /// </summary>
        public struct HandleEnumerator
        {
            public readonly EffectHandle Current => _current;

            private Dictionary<long, EffectHandle>.Enumerator _enumerator;
            private readonly string _effectTag;
            private readonly TagHandler _owner;
            private bool _hasEnumerator;
            private EffectHandle _current;

            internal HandleEnumerator(
                Dictionary<long, EffectHandle>.Enumerator enumerator,
                string effectTag,
                TagHandler owner
            )
            {
                _enumerator = enumerator;
                _effectTag = effectTag;
                _owner = owner;
                _hasEnumerator = true;
                _current = default;
            }

            public bool MoveNext()
            {
                if (!_hasEnumerator)
                {
                    return false;
                }

                while (_enumerator.MoveNext())
                {
                    EffectHandle handle = _enumerator.Current.Value;
                    if (_owner.HasAppliedTag(handle.id, _effectTag))
                    {
                        _current = handle;
                        return true;
                    }
                }

                _hasEnumerator = false;
                _current = default;
                return false;
            }
        }

        /// <summary>
        /// Enumerator that skips tags whose counts have dropped to zero.
        /// </summary>
        public struct ActiveTagEnumerator
        {
            public readonly string Current => _current ?? string.Empty;

            private Dictionary<string, uint>.Enumerator _enumerator;
            private bool _hasEnumerator;
            private string _current;

            internal ActiveTagEnumerator(Dictionary<string, uint>.Enumerator enumerator)
            {
                _enumerator = enumerator;
                _hasEnumerator = true;
                _current = string.Empty;
            }

            public bool MoveNext()
            {
                if (!_hasEnumerator)
                {
                    return false;
                }

                while (_enumerator.MoveNext())
                {
                    KeyValuePair<string, uint> entry = _enumerator.Current;
                    if (entry.Value == 0)
                    {
                        continue;
                    }

                    _current = entry.Key;
                    return true;
                }

                _hasEnumerator = false;
                _current = string.Empty;
                return false;
            }
        }
    }
}
