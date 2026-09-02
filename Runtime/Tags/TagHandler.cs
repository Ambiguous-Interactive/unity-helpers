// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.ExceptionServices;
    using Core.Extension;
    using UnityEngine;

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

        /*
            How many of each handle's effect tags were actually raised, which is not always all of
            them. Removal used to decrement every tag the effect names, so a handle that applied
            only the first of two -- because a subscriber removed the effect, or threw, between
            them -- decremented a count belonging to a DIFFERENT effect. That effect then reports
            active while the tag it owns is gone, and nothing repairs it, because InternalRemoveTag
            on an absent key is silent.

            A count rather than the tags themselves: application walks effect.effectTags in order
            and stops, so what was raised is always a prefix. Reading the list again at removal is
            what this type has always done, so the count adds no new assumption about that list
            holding still, and it keeps the path allocation-free.
        */
        private readonly Dictionary<long, int> _appliedTagCountByHandle = new();

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

            if (_tagCount.Count == 0)
            {
                return target ?? new List<string>(0);
            }

            if (target == null)
            {
                target = new List<string>(_tagCount.Count);
            }
            else if (target.Capacity < _tagCount.Count)
            {
                target.Capacity = _tagCount.Count;
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
        /// <returns>The populated buffer containing matching effect handles.</returns>
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
            if (string.IsNullOrEmpty(effectTag))
            {
                return buffer ?? new List<EffectHandle>(0);
            }

            List<EffectHandle> target = buffer;
            if (target != null)
            {
                target.Clear();
            }

            if (_effectHandles.Count == 0)
            {
                return target ?? new List<EffectHandle>(0);
            }

            int estimatedCapacity = Math.Min(_effectHandles.Count, 8);
            foreach (EffectHandle handle in _effectHandles.Values)
            {
                if (
                    handle.effect != null
                    && handle.effect.effectTags != null
                    && handle.effect.effectTags.Contains(effectTag)
                )
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
        /// Removes all instances of the specified tag and returns the contributing effect handles.
        /// </summary>
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
            if (0 < _effectHandles.Count)
            {
                int estimatedCapacity = Math.Min(_effectHandles.Count, 8);
                foreach (EffectHandle handle in _effectHandles.Values)
                {
                    if (
                        handle.effect != null
                        && handle.effect.effectTags != null
                        && handle.effect.effectTags.Contains(effectTag)
                    )
                    {
                        target ??= new List<EffectHandle>(estimatedCapacity);
                        target.Add(handle);
                    }
                }

                if (target != null)
                {
                    foreach (EffectHandle handle in target)
                    {
                        try
                        {
                            _ = ForceRemoveTags(handle);
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
                }
            }

            try
            {
                InternalRemoveTag(effectTag, allInstances: true);
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

            return new HandleEnumerable(_effectHandles.GetEnumerator(), effectTag);
        }

        /// <summary>
        /// Applies all tags from an effect handle's effect.
        /// Tracks the handle to support later removal via <see cref="ForceRemoveTags"/>.
        /// </summary>
        /// <param name="handle">The effect handle containing tags to apply.</param>
        public void ForceApplyTags(EffectHandle handle)
        {
            long id = handle.id;
            if (!_effectHandles.TryAdd(id, handle))
            {
                return;
            }

            _appliedTagCountByHandle[id] = 0;
            try
            {
                ApplyTrackedEffectTags(id, handle.effect);
            }
            catch (Exception applyFailure)
            {
                /*
                    Take the handle back out and undo exactly what this call raised, so the caller's
                    rollback -- which calls ForceRemoveTags -- finds nothing and cannot decrement a
                    tag this handle never raised. Guarded on the removal succeeding: a subscriber
                    that already tore the effect down did that unwinding itself, and repeating it
                    would decrement counts belonging to other effects.
                */
                if (_effectHandles.Remove(id))
                {
                    _ = _appliedTagCountByHandle.Remove(id, out int applied);
                    try
                    {
                        RemoveTrackedTags(handle.effect, applied);
                    }
                    catch (Exception unwindFailure)
                    {
                        /*
                            RemoveTrackedTags rethrows the first subscriber that threw while tags
                            were coming off. Letting that escape replaces the exception being
                            unwound -- the CAUSE -- with a consequence of unwinding it, and the
                            rethrow below never runs at all. Keep the cause and log the rest, which
                            is what every other teardown path in this type does.
                        */
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
        /// Removes all tags that were applied by the specified effect handle.
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
            if (!_effectHandles.Remove(id, out EffectHandle appliedHandle))
            {
                return false;
            }

            /*
                The two dictionaries are written together in ForceApplyTags with nothing between
                them, so a registered handle always has a ledger entry. A miss yields 0, which
                removes nothing -- the conservative answer, rather than falling back to decrementing
                every tag the effect names, which is the defect this ledger exists to fix.
            */
            _ = _appliedTagCountByHandle.Remove(id, out int applied);
            RemoveTrackedTags(appliedHandle.effect, applied);
            return true;
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
            if (effect == null)
            {
                return;
            }

            if (effect.effectTags == null)
            {
                return;
            }

            foreach (string effectTag in effect.effectTags)
            {
                InternalApplyTag(effectTag);
            }
        }

        /*
            InternalApplyTag raises OnTagAdded / OnTagCountChanged, which is user code, and a
            subscriber may remove the effect between one tag and the next. Without the liveness
            check the loop went on raising tags for a handle that no longer exists anywhere, and a
            tag raised that way can never come off: no effect owns it and no removal path can find
            it. The entity stays stunned, rooted or silenced for good.
        */
        private void ApplyTrackedEffectTags(long id, AttributeEffect effect)
        {
            if (effect == null)
            {
                return;
            }

            List<string> effectTags = effect.effectTags;
            if (effectTags == null)
            {
                return;
            }

            int applied = 0;
            foreach (string effectTag in effectTags)
            {
                if (!_effectHandles.ContainsKey(id))
                {
                    return;
                }

                /*
                    The ledger is written between raising the count and notifying, because the
                    notification is where a subscriber can tear this handle down: a ForceRemoveTags
                    reached from inside NotifyTagApplied must already see this tag as one of ours,
                    or it removes fewer tags than were raised and the surplus never comes off.
                */
                uint currentCount = RaiseTagCount(effectTag);
                ++applied;
                _appliedTagCountByHandle[id] = applied;
                NotifyTagApplied(effectTag, currentCount);
            }
        }

        /*
            Every tag comes off even when a subscriber throws: the handle is already detached, so a
            tag skipped here keeps its reference count raised for good and the entity never leaves
            the state. The first failure is rethrown once the last tag is removed.
        */
        private void RemoveTrackedTags(AttributeEffect effect, int applied)
        {
            /*
                An effect asset can be unloaded while a handle from it is still live, and reading a
                field off a destroyed ScriptableObject throws. Unity's `==` is the only check that
                sees that; a managed null test steps straight past it.
            */
            if (effect == null)
            {
                return;
            }

            List<string> effectTags = effect.effectTags;
            if (effectTags == null)
            {
                return;
            }

            Exception firstFailure = null;
            int removable = applied < effectTags.Count ? applied : effectTags.Count;
            for (int index = 0; index < removable; ++index)
            {
                try
                {
                    InternalRemoveTag(effectTags[index], allInstances: false);
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

        /// <summary>
        /// Provides an allocation-free enumerable view of the currently active tags.
        /// </summary>
        /// <returns>A struct enumerable that yields each active tag exactly once.</returns>
        public ActiveTagEnumerable EnumerateActiveTags()
        {
            if (_tagCount.Count == 0)
            {
                return ActiveTagEnumerable.Empty;
            }

            return new ActiveTagEnumerable(_tagCount);
        }

        /// <summary>
        /// Struct-backed enumerable over the active tags without additional allocations.
        /// </summary>
        public readonly struct ActiveTagEnumerable
        {
            private readonly Dictionary<string, uint> _source;

            internal ActiveTagEnumerable(Dictionary<string, uint> source)
            {
                _source = source;
            }

            public static ActiveTagEnumerable Empty => new ActiveTagEnumerable(null);

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
            private readonly Dictionary<long, EffectHandle>.Enumerator _enumerator;
            private readonly string _effectTag;
            private readonly bool _hasData;

            internal HandleEnumerable(
                Dictionary<long, EffectHandle>.Enumerator enumerator,
                string effectTag
            )
            {
                _enumerator = enumerator;
                _effectTag = effectTag;
                _hasData = true;
            }

            public static HandleEnumerable Empty => new HandleEnumerable(default, string.Empty);

            public HandleEnumerator GetEnumerator()
            {
                if (!_hasData || string.IsNullOrEmpty(_effectTag))
                {
                    return default;
                }

                return new HandleEnumerator(_enumerator, _effectTag);
            }
        }

        /// <summary>
        /// Enumerator that filters effect handles by tag without temporary lists.
        /// </summary>
        public struct HandleEnumerator
        {
            private Dictionary<long, EffectHandle>.Enumerator _enumerator;
            private readonly string _effectTag;
            private bool _hasEnumerator;
            private EffectHandle _current;

            internal HandleEnumerator(
                Dictionary<long, EffectHandle>.Enumerator enumerator,
                string effectTag
            )
            {
                _enumerator = enumerator;
                _effectTag = effectTag;
                _hasEnumerator = true;
                _current = default;
            }

            public readonly EffectHandle Current => _current;

            public bool MoveNext()
            {
                if (!_hasEnumerator)
                {
                    return false;
                }

                while (_enumerator.MoveNext())
                {
                    EffectHandle handle = _enumerator.Current.Value;
                    AttributeEffect effect = handle.effect;
                    if (
                        effect != null
                        && effect.effectTags != null
                        && effect.effectTags.Contains(_effectTag)
                    )
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
            private Dictionary<string, uint>.Enumerator _enumerator;
            private bool _hasEnumerator;
            private string _current;

            internal ActiveTagEnumerator(Dictionary<string, uint>.Enumerator enumerator)
            {
                _enumerator = enumerator;
                _hasEnumerator = true;
                _current = string.Empty;
            }

            public readonly string Current => _current ?? string.Empty;

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
