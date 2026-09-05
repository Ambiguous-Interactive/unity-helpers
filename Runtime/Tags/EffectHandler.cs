// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.ExceptionServices;
    using Core.Attributes;
    using Core.Extension;
    using Core.Helper;
    using UnityEngine;
    using Utils;

    // ReSharper disable once GrammarMistakeInComment
    /// <summary>
    /// Manages the application and removal of AttributeEffects on a GameObject.
    /// Handles effect duration tracking, tag application, cosmetic effects, and attribute modifications.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The EffectHandler is the central component for the effect system. It:
    /// - Applies effects and creates handles for tracking
    /// - Manages effect durations and automatic expiration
    /// - Coordinates with TagHandler for tag-based effects
    /// - Manages cosmetic effect instantiation and cleanup
    /// - Distributes attribute modifications to AttributesComponents
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// var effectHandler = gameObject.GetComponent&lt;EffectHandler&gt;();
    ///
    /// // Apply an effect
    /// AttributeEffect poisonEffect = ...;
    /// EffectHandle? handle = effectHandler.ApplyEffect(poisonEffect);
    ///
    /// // Remove a specific effect
    /// if (handle.HasValue)
    /// {
    ///     effectHandler.RemoveEffect(handle.Value);
    /// }
    ///
    /// // Remove all effects
    /// effectHandler.RemoveAllEffects();
    /// </code>
    /// </para>
    /// <para>
    /// Removal isolates a throwing callback per unit of work -- per attribute component, per tag,
    /// per cosmetic component, per behaviour -- and reports the first failure once the whole
    /// transition has run. Application and ticking do not: the first exception from
    /// <see cref="EffectBehavior.OnApply"/>, <see cref="CosmeticEffectComponent.OnApplyEffect"/>,
    /// <see cref="EffectBehavior.OnTick"/> or <see cref="EffectBehavior.OnPeriodicTick"/> abandons
    /// the rest of that pass. The asymmetry is deliberate: a removal that stops half way strands a
    /// modification nothing can undo, because the handle has already left every index, while an
    /// application unwinds to nothing and a tick is retried on the next frame.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TagHandler))]
    public sealed class EffectHandler : MonoBehaviour
    {
        private const int MaxPeriodicCatchUpTicksPerUpdate = 32;

        /// <summary>
        /// Invoked when an effect is successfully applied.
        /// </summary>
        public event Action<EffectHandle> OnEffectApplied;

        /// <summary>
        /// Invoked when an effect is removed (either manually or through expiration).
        /// </summary>
        public event Action<EffectHandle> OnEffectRemoved;

        [SiblingComponent]
#pragma warning disable CS0649
        private TagHandler _tagHandler;
#pragma warning restore CS0649

        [SiblingComponent(Optional = true)]
        private HashSet<AttributesComponent> _attributes;

        private readonly Dictionary<
            EffectHandle,
            PooledResource<List<CosmeticEffectData>>
        > _instancedCosmeticEffects = new();

        // An effect removed before cosmetics start must not receive a cosmetic teardown callback.
        private readonly HashSet<long> _handlesWithAppliedCosmetics = new();

        private readonly Dictionary<
            EffectStackKey,
            PooledResource<List<EffectHandle>>
        > _handlesByStackKey = new();
        private readonly Dictionary<long, EffectStackKey> _stackKeyByHandleId = new();

        // Iterating effect IDs is cheaper than iterating full handles.
        private readonly Dictionary<long, float> _effectExpirations = new();
        private readonly Dictionary<long, EffectHandle> _effectHandlesById = new();

        private readonly List<long> _expiredEffectIds = new();
        private readonly List<EffectHandle> _appliedEffects = new();
        private readonly Dictionary<
            long,
            PooledResource<List<PeriodicEffectRuntimeState>>
        > _periodicEffectStates = new();
        private readonly Dictionary<
            long,
            PooledResource<List<EffectBehavior>>
        > _behaviorsByHandleId = new();

        // Defer pool returns until traversals finish so callbacks cannot clear or reuse an enumerated list.
        private readonly List<PooledResource<List<EffectBehavior>>> _deferredBehaviorLeases = new();
        private readonly List<
            PooledResource<List<PeriodicEffectRuntimeState>>
        > _deferredPeriodicLeases = new();
        private readonly List<PooledResource<List<EffectHandle>>> _deferredHandleLeases = new();
        private readonly List<PooledResource<List<CosmeticEffectData>>> _deferredCosmeticLeases =
            new();

        private int _traversalDepth;

        private bool _initialized;

        internal int TraversalDepthForTesting => _traversalDepth;

        internal int DeferredLeaseCountForTesting =>
            _deferredBehaviorLeases.Count
            + _deferredPeriodicLeases.Count
            + _deferredHandleLeases.Count
            + _deferredCosmeticLeases.Count;

        private void Awake()
        {
            this.AssignRelationalComponents();
            _initialized = true;
        }

        /// <summary>
        /// Registers an AttributesComponent to receive effect modifications.
        /// Called automatically by AttributesComponent during Awake.
        /// </summary>
        /// <param name="attributesComponent">The component to register.</param>
        internal void Register(AttributesComponent attributesComponent)
        {
            _attributes ??= new HashSet<AttributesComponent>();
            _ = _attributes.Add(attributesComponent);
        }

        /// <summary>
        /// Unregisters an AttributesComponent from receiving effect modifications.
        /// Called automatically by AttributesComponent during OnDestroy.
        /// </summary>
        /// <param name="attributesComponent">The component to unregister.</param>
        internal void Remove(AttributesComponent attributesComponent)
        {
            _attributes?.Remove(attributesComponent);
        }

        /// <summary>
        /// Applies an AttributeEffect to this GameObject, handling tags, cosmetic effects, and attribute modifications.
        /// </summary>
        /// <param name="effect">The effect to apply.</param>
        /// <returns>
        /// An EffectHandle if the effect is non-instant (Duration or Infinite), allowing later removal.
        /// Null for instant effects that permanently modify base values, and null when a
        /// <see cref="EffectStackingMode.Stack"/> application is refused because
        /// <see cref="AttributeEffect.maximumStacks"/> could not be honoured.
        /// </returns>
        /// <remarks>
        /// <para>
        /// For Duration effects with the same name, reapplying can either reset the timer (if resetDurationOnReapplication is true)
        /// or be ignored if already active.
        /// </para>
        /// <para>
        /// A <see cref="EffectStackingMode.Stack"/> application first evicts the oldest handles down
        /// to <see cref="AttributeEffect.maximumStacks"/>. When an <see cref="OnEffectRemoved"/>
        /// subscriber re-applies the same effect, an eviction can leave the stack no smaller than it
        /// started; the application is then refused with a warning rather than registering a handle
        /// past the cap.
        /// </para>
        /// <para>
        /// Application invokes user code -- <see cref="EffectBehavior.OnApply"/>,
        /// <see cref="CosmeticEffectComponent.OnApplyEffect"/>, attribute notifications and
        /// <see cref="OnEffectApplied"/> -- and a callback is free to remove the handle it was given.
        /// When one does, the remaining application steps are skipped so nothing is applied that
        /// teardown has already run past, and the returned handle is already inactive. If a callback
        /// throws instead, the partial application is unwound through the same removal transition
        /// before the exception is rethrown.
        /// </para>
        /// </remarks>
        public EffectHandle? ApplyEffect(AttributeEffect effect)
        {
            return ApplyEffect(effect, Time.time);
        }

        internal EffectHandle? ApplyEffectForTesting(AttributeEffect effect, float currentTime)
        {
            return ApplyEffect(effect, currentTime);
        }

        private EffectHandle? ApplyEffect(AttributeEffect effect, float currentTime)
        {
            if (effect == null)
            {
                return null;
            }

            if (effect.durationType == ModifierDurationType.Instant)
            {
                // Warn once per asset; the Inspector reports the same fixable configuration error.
                if (effect.ShouldReportInstantWithHandleData())
                {
                    this.LogWarn(
                        $"Effect {effect.name} defines periodic or behaviour data but is Instant. These features require a Duration or Infinite effect.",
                        stackTrace: false
                    );
                }

                InternalApplyEffect(effect);
                return null;
            }

            EffectStackKey stackKey = effect.GetStackKey();
            List<EffectHandle> existingHandles = TryGetStackHandles(stackKey);

            switch (effect.stackingMode)
            {
                case EffectStackingMode.Ignore:
                {
                    if (existingHandles is { Count: > 0 })
                    {
                        return existingHandles[0];
                    }

                    break;
                }
                case EffectStackingMode.Refresh:
                {
                    if (existingHandles is { Count: > 0 })
                    {
                        EffectHandle handle = existingHandles[0];
                        InternalApplyEffect(handle, currentTime);
                        return handle;
                    }

                    break;
                }
                case EffectStackingMode.Replace:
                {
                    if (existingHandles is { Count: > 0 })
                    {
                        using PooledResource<List<EffectHandle>> handleBufferResource =
                            Buffers<EffectHandle>.List.Get(out List<EffectHandle> handleBuffer);
                        handleBuffer.AddRange(existingHandles);
                        foreach (EffectHandle handle in handleBuffer)
                        {
                            RemoveEffect(handle);
                        }
                    }

                    break;
                }
                case EffectStackingMode.Stack:
                {
                    if (
                        0 < effect.maximumStacks
                        && !TryEvictToStackCap(effect, stackKey, existingHandles)
                    )
                    {
                        return null;
                    }

                    break;
                }
            }

            EffectHandle newHandle = EffectHandle.CreateInstance(effect);
            RegisterStackHandle(stackKey, newHandle);
            try
            {
                InternalApplyEffect(newHandle, currentTime);
            }
            catch
            {
                // Unwind failed applications because no caller receives the handle; preserve the original exception.
                Exception rollbackFailure = RemoveEffectCore(newHandle);
                // Only this index can have been registered before the failure; removal is idempotent.
                DetachStackHandle(newHandle);
                if (rollbackFailure != null)
                {
                    this.LogError(
                        $"Rolling back a failed application of {effect.name} raised a second exception.",
                        rollbackFailure
                    );
                }

                throw;
            }

            return newHandle;
        }

        private List<EffectHandle> TryGetStackHandles(EffectStackKey stackKey)
        {
            return _handlesByStackKey.TryGetValue(
                stackKey,
                out PooledResource<List<EffectHandle>> lease
            )
                ? lease.resource
                : null;
        }

        private void RegisterStackHandle(EffectStackKey stackKey, EffectHandle handle)
        {
            long handleId = handle.id;
            _stackKeyByHandleId[handleId] = stackKey;

            List<EffectHandle> handles;
            if (
                !_handlesByStackKey.TryGetValue(
                    stackKey,
                    out PooledResource<List<EffectHandle>> handlesLease
                )
            )
            {
                handlesLease = RentHandleList(out handles);
                _handlesByStackKey.Add(stackKey, handlesLease);
            }
            else
            {
                handles = handlesLease.resource;
            }

            handles.Add(handle);
        }

        private bool TryEvictToStackCap(
            AttributeEffect effect,
            EffectStackKey stackKey,
            List<EffectHandle> existingHandles
        )
        {
            // Resolve again after callbacks because removal can return this list to the pool for immediate reuse.
            List<EffectHandle> stackHandles = existingHandles;
            while (stackHandles is { Count: > 0 } && effect.maximumStacks <= stackHandles.Count)
            {
                int handlesBeforeEviction = stackHandles.Count;
                RemoveEffect(stackHandles[0]);
                stackHandles = TryGetStackHandles(stackKey);
                if (stackHandles != null && handlesBeforeEviction <= stackHandles.Count)
                {
                    this.LogWarn(
                        $"Evicting a stack of {effect.name} made no progress because a removal callback re-applied it, so this application is refused rather than exceeding maximumStacks of {effect.maximumStacks}."
                    );
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Removes a specific effect by its handle, cleaning up tags, cosmetic effects, and attribute modifications.
        /// </summary>
        /// <param name="handle">The handle of the effect to remove.</param>
        /// <remarks>
        /// <para>
        /// Removal is a two-phase transition. The handle is first detached: it leaves every index
        /// this handler keeps before a single line of user code runs. Only then are the teardown
        /// callbacks delivered. A callback therefore observes the handle as already inactive --
        /// <see cref="IsEffectActive"/> is <c>false</c>, <see cref="GetEffectStackCount"/> no longer
        /// counts it and <see cref="GetActiveEffects"/> no longer lists it -- and calling this
        /// method again for the same handle, or for one that was never applied, is a no-op rather
        /// than a recursion.
        /// </para>
        /// <para>
        /// Callbacks are delivered in a fixed order: attribute modifications are removed, then
        /// tags, then cosmetic effects, then <see cref="OnEffectRemoved"/>, then
        /// <see cref="EffectBehavior.OnRemove"/> on each cloned behaviour. Re-applying the effect
        /// from any of them produces an independent new handle.
        /// </para>
        /// <para>
        /// If a callback throws, the remaining phases still run and every pooled buffer, behaviour
        /// clone and cosmetic instance is still released; the first exception is rethrown once the
        /// handler's state is consistent, and any later one is logged.
        /// </para>
        /// </remarks>
        public void RemoveEffect(EffectHandle handle)
        {
            Exception teardownFailure = RemoveEffectCore(handle);
            if (teardownFailure != null)
            {
                ExceptionDispatchInfo.Capture(teardownFailure).Throw();
            }
        }

        /// <summary>
        /// Removes every effect currently active on this handler.
        /// </summary>
        /// <remarks>
        /// Each handle goes through the same transition as <see cref="RemoveEffect"/>, over a
        /// snapshot taken before teardown begins. An effect applied by a teardown callback is
        /// therefore left active rather than silently forgotten.
        /// </remarks>
        public void RemoveAllEffects()
        {
            Exception teardownFailure = RemoveAllEffectsCore();
            if (teardownFailure != null)
            {
                ExceptionDispatchInfo.Capture(teardownFailure).Throw();
            }
        }

        private Exception RemoveAllEffectsCore()
        {
            using PooledResource<List<EffectHandle>> handleBufferResource =
                Buffers<EffectHandle>.List.Get(out List<EffectHandle> handleBuffer);
            handleBuffer.AddRange(_appliedEffects);
            Exception teardownFailure = null;
            foreach (EffectHandle handle in handleBuffer)
            {
                teardownFailure = RecordFailure(teardownFailure, RemoveEffectCore(handle));
            }

            return teardownFailure;
        }

        private void OnDestroy()
        {
            // Destruction can interrupt OnTick; normal traversal unwinding preserves the deferred-release counter.
            ++_traversalDepth;
            try
            {
                Exception teardownFailure = RemoveAllEffectsCore();
                if (teardownFailure != null)
                {
                    this.LogError(
                        $"Effect teardown during destruction raised an exception.",
                        teardownFailure
                    );
                }

                if (0 < _handlesByStackKey.Count)
                {
                    using PooledResource<List<EffectStackKey>> stackKeysResource =
                        Buffers<EffectStackKey>.List.Get(out List<EffectStackKey> stackKeys);
                    stackKeys.AddRange(_handlesByStackKey.Keys);
                    foreach (EffectStackKey stackKey in stackKeys)
                    {
                        if (
                            _handlesByStackKey.TryGetValue(
                                stackKey,
                                out PooledResource<List<EffectHandle>> lease
                            )
                        )
                        {
                            ReleaseHandleList(lease);
                        }
                    }
                    _handlesByStackKey.Clear();
                    _stackKeyByHandleId.Clear();
                }

                foreach (
                    KeyValuePair<
                        EffectHandle,
                        PooledResource<List<CosmeticEffectData>>
                    > cosmetic in _instancedCosmeticEffects
                )
                {
                    ReleaseCosmeticDataList(cosmetic.Value);
                }
                _instancedCosmeticEffects.Clear();

                foreach (
                    KeyValuePair<
                        long,
                        PooledResource<List<PeriodicEffectRuntimeState>>
                    > periodic in _periodicEffectStates
                )
                {
                    ReleasePeriodicStateList(periodic.Value);
                }
                _periodicEffectStates.Clear();

                foreach (
                    KeyValuePair<
                        long,
                        PooledResource<List<EffectBehavior>>
                    > behavior in _behaviorsByHandleId
                )
                {
                    ReleaseBehaviorList(behavior.Value);
                }
                _behaviorsByHandleId.Clear();

                _effectExpirations.Clear();
                _effectHandlesById.Clear();
                _handlesWithAppliedCosmetics.Clear();
                _expiredEffectIds.Clear();
                _appliedEffects.Clear();
            }
            finally
            {
                EndTraversal();
            }
        }

        private void DetachStackHandle(EffectHandle handle)
        {
            long handleId = handle.id;
            if (!_stackKeyByHandleId.Remove(handleId, out EffectStackKey stackKey))
            {
                return;
            }

            if (
                !_handlesByStackKey.TryGetValue(
                    stackKey,
                    out PooledResource<List<EffectHandle>> handlesLease
                )
            )
            {
                return;
            }

            List<EffectHandle> handles = handlesLease.resource;
            _ = handles.Remove(handle);
            if (handles.Count == 0)
            {
                _ = _handlesByStackKey.Remove(stackKey);
                ReleaseHandleList(handlesLease);
            }
        }

        /// <summary>
        /// Determines whether the specified effect is currently active on this handler.
        /// </summary>
        /// <param name="effect">The effect to check.</param>
        /// <returns><c>true</c> if at least one handle for the effect is active; otherwise, <c>false</c>.</returns>
        public bool IsEffectActive(AttributeEffect effect)
        {
            if (effect == null)
            {
                return false;
            }

            foreach (EffectHandle handle in _appliedEffects)
            {
                if (handle.effect == effect)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the number of active handles for the specified effect.
        /// </summary>
        /// <param name="effect">The effect to count.</param>
        /// <returns>The number of active handles associated with <paramref name="effect"/>.</returns>
        public int GetEffectStackCount(AttributeEffect effect)
        {
            if (effect == null)
            {
                return 0;
            }

            int count = 0;
            foreach (EffectHandle handle in _appliedEffects)
            {
                if (handle.effect == effect)
                {
                    ++count;
                }
            }

            return count;
        }

        /// <summary>
        /// Copies all active effect handles into the provided buffer.
        /// </summary>
        /// <param name="buffer">
        /// Optional list to populate. When <c>null</c>, a new list is created. The buffer is cleared before population.
        /// </param>
        /// <returns>The populated buffer containing all currently active effect handles.</returns>
        public List<EffectHandle> GetActiveEffects(List<EffectHandle> buffer = null)
        {
            buffer ??= new List<EffectHandle>();
            buffer.Clear();
            buffer.AddRange(_appliedEffects);
            return buffer;
        }

        /// <summary>
        /// Attempts to retrieve the remaining duration for the specified effect handle.
        /// </summary>
        /// <param name="handle">The handle to inspect.</param>
        /// <param name="remainingDuration">When this method returns, contains the remaining time in seconds, or zero if unavailable.</param>
        /// <returns><c>true</c> if the handle has a tracked duration; otherwise, <c>false</c>.</returns>
        public bool TryGetRemainingDuration(EffectHandle handle, out float remainingDuration)
        {
            return TryGetRemainingDuration(handle, Time.time, out remainingDuration);
        }

        internal bool TryGetRemainingDuration(
            EffectHandle handle,
            float currentTime,
            out float remainingDuration
        )
        {
            long handleId = handle.id;
            if (!_effectExpirations.TryGetValue(handleId, out float expiration))
            {
                remainingDuration = 0f;
                return false;
            }

            float timeRemaining = expiration - currentTime;
            if (timeRemaining < 0f)
            {
                timeRemaining = 0f;
            }

            remainingDuration = timeRemaining;
            return true;
        }

        /// <summary>
        /// Ensures an effect handle exists for the specified effect, optionally refreshing its duration if already active.
        /// </summary>
        /// <param name="effect">The effect to apply or refresh.</param>
        /// <returns>
        /// An active handle for the effect, or <c>null</c> when <see cref="ApplyEffect(AttributeEffect)"/> produces
        /// none.
        /// </returns>
        public EffectHandle? EnsureHandle(AttributeEffect effect)
        {
            return EnsureHandle(effect, refreshDuration: true);
        }

        /// <summary>
        /// Ensures an effect handle exists for the specified effect, optionally refreshing its duration if already active.
        /// </summary>
        /// <param name="effect">The effect to apply or refresh.</param>
        /// <param name="refreshDuration">
        /// When <c>true</c>, attempts to refresh the effect's duration when it is already active and supports reapplication.
        /// </param>
        /// <returns>
        /// An active handle for the effect, or <c>null</c> when <see cref="ApplyEffect(AttributeEffect)"/> produces
        /// none.
        /// </returns>
        public EffectHandle? EnsureHandle(AttributeEffect effect, bool refreshDuration)
        {
            return EnsureHandle(effect, refreshDuration, Time.time);
        }

        internal EffectHandle? EnsureHandle(
            AttributeEffect effect,
            bool refreshDuration,
            float currentTime
        )
        {
            if (effect == null)
            {
                return null;
            }

            foreach (EffectHandle handle in _appliedEffects)
            {
                if (handle.effect == effect)
                {
                    if (refreshDuration)
                    {
                        _ = RefreshEffect(
                            handle,
                            ignoreReapplicationPolicy: false,
                            currentTime: currentTime
                        );
                    }

                    return handle;
                }
            }

            return ApplyEffect(effect, currentTime);
        }

        /// <summary>
        /// Attempts to refresh the duration of the specified effect handle.
        /// </summary>
        /// <param name="handle">The handle to refresh.</param>
        /// <returns><c>true</c> if the duration was refreshed; otherwise, <c>false</c>.</returns>
        public bool RefreshEffect(EffectHandle handle)
        {
            return RefreshEffect(handle, ignoreReapplicationPolicy: false);
        }

        /// <summary>
        /// Attempts to refresh the duration of the specified effect handle.
        /// </summary>
        /// <param name="handle">The handle to refresh.</param>
        /// <param name="ignoreReapplicationPolicy">
        /// When <c>true</c>, refreshes the duration even if <see cref="AttributeEffect.resetDurationOnReapplication"/> is <c>false</c>.
        /// </param>
        /// <returns><c>true</c> if the duration was refreshed; otherwise, <c>false</c>.</returns>
        public bool RefreshEffect(EffectHandle handle, bool ignoreReapplicationPolicy)
        {
            return RefreshEffect(handle, ignoreReapplicationPolicy, Time.time);
        }

        internal bool RefreshEffect(
            EffectHandle handle,
            bool ignoreReapplicationPolicy,
            float currentTime
        )
        {
            AttributeEffect effect = handle.effect;
            if (effect == null)
            {
                return false;
            }

            if (effect.durationType != ModifierDurationType.Duration)
            {
                return false;
            }

            if (!ignoreReapplicationPolicy && !effect.resetDurationOnReapplication)
            {
                return false;
            }

            long handleId = handle.id;
            if (!_effectExpirations.ContainsKey(handleId))
            {
                return false;
            }

            float newExpiration = currentTime + effect.duration;
            _effectExpirations[handleId] = newExpiration;
            _effectHandlesById[handleId] = handle;
            return true;
        }

        // Complete teardown before reporting the first callback failure; callers decide whether to propagate it.
        private Exception RemoveEffectCore(EffectHandle handle)
        {
            long handleId = handle.id;
            if (!_effectHandlesById.ContainsKey(handleId))
            {
                // Unknown, or already detached by an outer frame of this same transition.
                return null;
            }

            _ = _effectHandlesById.Remove(handleId);
            _ = _effectExpirations.Remove(handleId);
            _ = _appliedEffects.Remove(handle);
            DetachStackHandle(handle);
            if (
                _periodicEffectStates.Remove(
                    handleId,
                    out PooledResource<List<PeriodicEffectRuntimeState>> periodicLease
                )
            )
            {
                ReleasePeriodicStateList(periodicLease);
            }

            bool hasBehaviors = _behaviorsByHandleId.Remove(
                handleId,
                out PooledResource<List<EffectBehavior>> behaviorLease
            );
            bool hasCosmetics = _instancedCosmeticEffects.Remove(
                handle,
                out PooledResource<List<CosmeticEffectData>> cosmeticLease
            );
            bool hasAppliedCosmetics = _handlesWithAppliedCosmetics.Remove(handleId);

            Exception firstFailure = null;
            ++_traversalDepth;
            try
            {
                firstFailure = RecordFailure(firstFailure, RunAttributeRemoval(handle));
                firstFailure = RecordFailure(firstFailure, RunTagRemoval(handle));
                firstFailure = RecordFailure(
                    firstFailure,
                    RunCosmeticRemoval(handle, hasCosmetics, hasAppliedCosmetics, cosmeticLease)
                );
                firstFailure = RecordFailure(firstFailure, RunEffectRemovedEvent(handle));
                if (hasBehaviors)
                {
                    firstFailure = RecordFailure(
                        firstFailure,
                        RunBehaviorRemoval(handle, behaviorLease.resource)
                    );
                }
            }
            finally
            {
                if (hasBehaviors)
                {
                    ReleaseBehaviorList(behaviorLease);
                }

                if (hasCosmetics)
                {
                    ReleaseCosmeticDataList(cosmeticLease);
                }

                EndTraversal();
            }

            return firstFailure;
        }

        private Exception RunAttributeRemoval(EffectHandle handle)
        {
            if (_attributes == null)
            {
                return null;
            }

            Exception firstFailure = null;
            using PooledResource<List<AttributesComponent>> lease = SnapshotAttributes(
                out List<AttributesComponent> attributes
            );
            foreach (AttributesComponent attributesComponent in attributes)
            {
                if (attributesComponent == null)
                {
                    continue;
                }

                try
                {
                    attributesComponent.ForceRemoveAttributeModifications(handle);
                }
                catch (Exception attributeFailure)
                {
                    firstFailure = RecordFailure(firstFailure, attributeFailure);
                }
            }

            return firstFailure;
        }

        // Snapshot before callbacks can mutate registration; callers must also skip destroyed snapshot entries.
        private PooledResource<List<AttributesComponent>> SnapshotAttributes(
            out List<AttributesComponent> attributes
        )
        {
            PooledResource<List<AttributesComponent>> lease = Buffers<AttributesComponent>.List.Get(
                out attributes
            );
            if (_attributes != null)
            {
                attributes.AddRange(_attributes);
            }

            return lease;
        }

        // Remove attributes before tags so cosmetic callbacks can inspect the remaining tags.
        private Exception RunTagRemoval(EffectHandle handle)
        {
            if (!_initialized && _tagHandler == null)
            {
                this.AssignRelationalComponents();
            }

            if (_tagHandler == null)
            {
                return null;
            }

            try
            {
                _ = _tagHandler.ForceRemoveTags(handle);
            }
            catch (Exception tagFailure)
            {
                return tagFailure;
            }

            return null;
        }

        private Exception RunCosmeticRemoval(
            EffectHandle handle,
            bool hasInstances,
            bool hasAppliedCosmetics,
            PooledResource<List<CosmeticEffectData>> cosmeticLease
        )
        {
            if (hasInstances)
            {
                return RemoveInstancedCosmeticEffects(cosmeticLease.resource);
            }

            return hasAppliedCosmetics ? RemoveTemplateCosmeticEffects(handle) : null;
        }

        private Exception RunEffectRemovedEvent(EffectHandle handle)
        {
            try
            {
                OnEffectRemoved?.Invoke(handle);
            }
            catch (Exception eventFailure)
            {
                return eventFailure;
            }

            return null;
        }

        private Exception RunBehaviorRemoval(EffectHandle handle, List<EffectBehavior> behaviors)
        {
            Exception firstFailure = null;
            EffectBehaviorContext context = new(this, handle, 0f);
            foreach (EffectBehavior behavior in behaviors)
            {
                if (behavior == null)
                {
                    continue;
                }

                try
                {
                    behavior.OnRemove(context);
                }
                catch (Exception behaviorFailure)
                {
                    firstFailure = RecordFailure(firstFailure, behaviorFailure);
                }

                // Destroy owned clones even when their teardown callback throws.
                Destroy(behavior);
            }

            return firstFailure;
        }

        private Exception RecordFailure(Exception firstFailure, Exception failure)
        {
            return TeardownFailures.KeepFirst(this, firstFailure, failure);
        }

        private void InternalApplyEffect(EffectHandle handle, float currentTime)
        {
            bool exists = _appliedEffects.Contains(handle);
            if (!exists)
            {
                _appliedEffects.Add(handle);
            }

            long handleId = handle.id;
            _effectHandlesById[handleId] = handle;

            AttributeEffect effect = handle.effect;
            if (effect.durationType == ModifierDurationType.Duration)
            {
                if (!exists || effect.resetDurationOnReapplication)
                {
                    _effectExpirations[handleId] = currentTime + effect.duration;
                }
            }

            if (!exists)
            {
                RegisterPeriodicRuntime(handle, currentTime);
                RegisterBehaviors(handle);
                // OnApply can remove this handle; applying further state would leave it without a removal path.
                if (!_effectHandlesById.ContainsKey(handleId))
                {
                    return;
                }
            }

            if (!_initialized && _tagHandler == null)
            {
                this.AssignRelationalComponents();
            }

            if (_tagHandler != null && effect.effectTags is { Count: > 0 })
            {
                _tagHandler.ForceApplyTags(handle);
                if (!_effectHandlesById.ContainsKey(handleId))
                {
                    return;
                }
            }

            if (effect.cosmeticEffects is { Count: > 0 })
            {
                InternalApplyCosmeticEffects(handle);
                if (!_effectHandlesById.ContainsKey(handleId))
                {
                    return;
                }
            }

            if (effect.modifications is { Count: > 0 })
            {
                using PooledResource<List<AttributesComponent>> lease = SnapshotAttributes(
                    out List<AttributesComponent> attributes
                );
                foreach (AttributesComponent attributesComponent in attributes)
                {
                    if (attributesComponent == null)
                    {
                        continue;
                    }

                    attributesComponent.ForceApplyAttributeModifications(handle);
                    if (!_effectHandlesById.ContainsKey(handleId))
                    {
                        return;
                    }
                }
            }

            OnEffectApplied?.Invoke(handle);
        }

        private void InternalApplyEffect(AttributeEffect effect)
        {
            if (!_initialized && _tagHandler == null)
            {
                this.AssignRelationalComponents();
            }

            if (_tagHandler != null && effect.effectTags is { Count: > 0 })
            {
                _tagHandler.ForceApplyEffect(effect);
            }

            if (effect.cosmeticEffects is { Count: > 0 })
            {
                InternalApplyCosmeticEffects(effect);
            }

            if (effect.modifications is { Count: > 0 })
            {
                using PooledResource<List<AttributesComponent>> lease = SnapshotAttributes(
                    out List<AttributesComponent> attributes
                );
                foreach (AttributesComponent attributesComponent in attributes)
                {
                    if (attributesComponent != null)
                    {
                        attributesComponent.ForceApplyAttributeModifications(effect);
                    }
                }
            }
        }

        private void RegisterPeriodicRuntime(EffectHandle handle, float startTime)
        {
            AttributeEffect effect = handle.effect;
            if (effect.periodicEffects is not { Count: > 0 })
            {
                return;
            }

            List<PeriodicEffectRuntimeState> runtimeStates = null;
            PooledResource<List<PeriodicEffectRuntimeState>> runtimeStatesLease = default;

            foreach (PeriodicEffectDefinition definition in effect.periodicEffects)
            {
                if (definition == null)
                {
                    continue;
                }

                if (runtimeStates == null)
                {
                    runtimeStatesLease = RentPeriodicStateList(out runtimeStates);
                }

                runtimeStates.Add(new PeriodicEffectRuntimeState(definition, startTime));
            }

            if (runtimeStates is { Count: > 0 })
            {
                _periodicEffectStates[handle.id] = runtimeStatesLease;
            }
            else if (runtimeStates != null)
            {
                RecyclePeriodicStateList(runtimeStatesLease);
            }
        }

        private void RegisterBehaviors(EffectHandle handle)
        {
            AttributeEffect effect = handle.effect;
            if (effect.behaviors is not { Count: > 0 })
            {
                return;
            }

            long handleId = handle.id;
            List<EffectBehavior> instances = null;
            foreach (EffectBehavior behavior in effect.behaviors)
            {
                if (behavior == null)
                {
                    continue;
                }

                if (instances == null)
                {
                    // Publish the lease before cloning so reentrant removal and failure rollback can reach every instance.
                    _behaviorsByHandleId[handleId] = RentBehaviorList(out instances);
                }

                instances.Add(Instantiate(behavior));
            }

            if (instances == null)
            {
                return;
            }

            EffectBehaviorContext context = new(this, handle, 0f);
            ++_traversalDepth;
            try
            {
                foreach (EffectBehavior instance in instances)
                {
                    if (instance == null)
                    {
                        continue;
                    }

                    instance.OnApply(context);
                    if (!_effectHandlesById.ContainsKey(handleId))
                    {
                        break;
                    }
                }
            }
            finally
            {
                EndTraversal();
            }
        }

        private void ApplyPeriodicTick(
            EffectHandle handle,
            PeriodicEffectRuntimeState runtimeState,
            float currentTime,
            float deltaTime
        )
        {
            PeriodicEffectDefinition definition = runtimeState.definition;
            if (_attributes is { Count: > 0 } && definition.modifications is { Count: > 0 })
            {
                using PooledResource<List<AttributesComponent>> lease = SnapshotAttributes(
                    out List<AttributesComponent> attributes
                );
                foreach (AttributesComponent attributesComponent in attributes)
                {
                    if (attributesComponent != null)
                    {
                        attributesComponent.ApplyAttributeModifications(
                            definition.modifications,
                            null
                        );
                    }
                }
            }

            if (
                _behaviorsByHandleId.TryGetValue(
                    handle.id,
                    out PooledResource<List<EffectBehavior>> behaviorLease
                )
            )
            {
                List<EffectBehavior> behaviors = behaviorLease.resource;
                if (behaviors.Count == 0)
                {
                    return;
                }

                EffectBehaviorContext context = new(this, handle, deltaTime);
                PeriodicEffectTickContext tickContext = new(
                    definition,
                    runtimeState.ExecutedTicks,
                    currentTime
                );

                long handleId = handle.id;
                foreach (EffectBehavior behavior in behaviors)
                {
                    if (behavior == null)
                    {
                        continue;
                    }

                    behavior.OnPeriodicTick(context, tickContext);
                    // A periodic callback can remove the handle and tear down every remaining clone.
                    if (!_effectHandlesById.ContainsKey(handleId))
                    {
                        break;
                    }
                }
            }
        }

        private void InternalApplyCosmeticEffects(EffectHandle handle)
        {
            if (_instancedCosmeticEffects.ContainsKey(handle))
            {
                return;
            }

            long handleId = handle.id;
            AttributeEffect effect = handle.effect;
            List<CosmeticEffectData> instancedCosmeticData = null;
            // Refresh must not apply shared cosmetics twice for one eventual removal.
            if (!_handlesWithAppliedCosmetics.Add(handleId))
            {
                return;
            }

            ++_traversalDepth;
            try
            {
                foreach (CosmeticEffectData cosmeticEffectData in effect.cosmeticEffects)
                {
                    CosmeticEffectData cosmeticEffect = cosmeticEffectData;
                    if (cosmeticEffect == null)
                    {
                        // Warn once per asset without a stack trace; this is an authoring error.
                        if (effect.ShouldReportUnassignedCosmeticEffect())
                        {
                            this.LogError(
                                $"Effect {effect.name} has an unassigned CosmeticEffectData entry, which cannot be instanced and is skipped.",
                                stackTrace: false
                            );
                        }

                        continue;
                    }

                    if (cosmeticEffectData.RequiresInstancing)
                    {
                        if (instancedCosmeticData == null)
                        {
                            // Publish before instantiation so reentrant removal or failure can clean up partial instances.
                            PooledResource<List<CosmeticEffectData>> instancedCosmeticLease =
                                RentCosmeticDataList(out instancedCosmeticData);
                            if (!_instancedCosmeticEffects.TryAdd(handle, instancedCosmeticLease))
                            {
                                ReleaseCosmeticDataList(instancedCosmeticLease);
                                break;
                            }
                        }

                        cosmeticEffect = Instantiate(
                            cosmeticEffectData,
                            transform.position,
                            Quaternion.identity
                        );
                        instancedCosmeticData.Add(cosmeticEffect);
                        cosmeticEffect.transform.SetParent(transform, true);
                    }

                    using PooledResource<List<CosmeticEffectComponent>> cosmeticEffectsResource =
                        Buffers<CosmeticEffectComponent>.List.Get(
                            out List<CosmeticEffectComponent> cosmeticEffectsBuffer
                        );
                    cosmeticEffect.GetComponents(cosmeticEffectsBuffer);
                    foreach (CosmeticEffectComponent cosmeticComponent in cosmeticEffectsBuffer)
                    {
                        // Callbacks can destroy later snapshot entries, including during EditMode teardown.
                        if (cosmeticComponent == null)
                        {
                            continue;
                        }

                        cosmeticComponent.OnApplyEffect(gameObject);
                        if (!_effectHandlesById.ContainsKey(handleId))
                        {
                            break;
                        }
                    }

                    if (!_effectHandlesById.ContainsKey(handleId))
                    {
                        break;
                    }
                }
            }
            finally
            {
                EndTraversal();
            }
        }

        private void InternalApplyCosmeticEffects(AttributeEffect attributeEffect)
        {
            foreach (CosmeticEffectData cosmeticEffectData in attributeEffect.cosmeticEffects)
            {
                if (cosmeticEffectData == null)
                {
                    if (attributeEffect.ShouldReportUnassignedCosmeticEffect())
                    {
                        this.LogError(
                            $"Effect {attributeEffect.name} has an unassigned CosmeticEffectData entry, which cannot be instanced and is skipped.",
                            stackTrace: false
                        );
                    }

                    continue;
                }

                if (cosmeticEffectData.RequiresInstancing)
                {
                    this.LogError(
                        $"CosmeticEffectData requires instancing, but can't instance (no handle)."
                    );
                    continue;
                }

                using PooledResource<List<CosmeticEffectComponent>> cosmeticEffectsResource =
                    Buffers<CosmeticEffectComponent>.List.Get(
                        out List<CosmeticEffectComponent> cosmeticEffectsBuffer
                    );
                cosmeticEffectData.GetComponents(cosmeticEffectsBuffer);
                foreach (CosmeticEffectComponent cosmeticComponent in cosmeticEffectsBuffer)
                {
                    if (cosmeticComponent == null)
                    {
                        continue;
                    }

                    cosmeticComponent.OnApplyEffect(gameObject);
                }
            }
        }

        // Shared templates need callbacks only; destroying their components would destroy the asset state.
        private Exception RemoveTemplateCosmeticEffects(EffectHandle handle)
        {
            AttributeEffect effect = handle.effect;
            if (effect == null || effect.cosmeticEffects == null)
            {
                return null;
            }

            Exception firstFailure = null;
            foreach (CosmeticEffectData cosmeticEffectData in effect.cosmeticEffects)
            {
                if (cosmeticEffectData == null)
                {
                    continue;
                }

                // An earlier callback can stop application before this entry instantiates anything.
                if (cosmeticEffectData.RequiresInstancing)
                {
                    continue;
                }

                using PooledResource<List<CosmeticEffectComponent>> cosmeticEffectsResource =
                    Buffers<CosmeticEffectComponent>.List.Get(
                        out List<CosmeticEffectComponent> cosmeticEffectsBuffer
                    );
                cosmeticEffectData.GetComponents(cosmeticEffectsBuffer);
                foreach (CosmeticEffectComponent cosmeticComponent in cosmeticEffectsBuffer)
                {
                    if (cosmeticComponent == null)
                    {
                        continue;
                    }

                    try
                    {
                        cosmeticComponent.OnRemoveEffect(gameObject);
                    }
                    catch (Exception cosmeticFailure)
                    {
                        firstFailure = RecordFailure(firstFailure, cosmeticFailure);
                    }
                }
            }

            return firstFailure;
        }

        // No removal path retains this detached lease; destroy every instance despite callback failures.
        private Exception RemoveInstancedCosmeticEffects(List<CosmeticEffectData> cosmeticDatas)
        {
            if (cosmeticDatas == null)
            {
                return null;
            }

            Exception firstFailure = null;
            foreach (CosmeticEffectData cosmeticData in cosmeticDatas)
            {
                if (cosmeticData == null)
                {
                    continue;
                }

                using PooledResource<List<CosmeticEffectComponent>> cosmeticEffectsResource =
                    Buffers<CosmeticEffectComponent>.List.Get(
                        out List<CosmeticEffectComponent> cosmeticEffectsBuffer
                    );
                cosmeticData.GetComponents(cosmeticEffectsBuffer);
                foreach (CosmeticEffectComponent cosmeticComponent in cosmeticEffectsBuffer)
                {
                    if (cosmeticComponent == null)
                    {
                        continue;
                    }

                    try
                    {
                        cosmeticComponent.OnRemoveEffect(gameObject);
                    }
                    catch (Exception cosmeticFailure)
                    {
                        firstFailure = RecordFailure(firstFailure, cosmeticFailure);
                    }
                }
            }

            foreach (CosmeticEffectData data in cosmeticDatas)
            {
                if (data == null)
                {
                    continue;
                }

                try
                {
                    DestroyCosmeticInstance(data);
                }
                catch (Exception destroyFailure)
                {
                    firstFailure = RecordFailure(firstFailure, destroyFailure);
                }
            }

            return firstFailure;
        }

        private static void DestroyCosmeticInstance(CosmeticEffectData data)
        {
            bool shouldDestroyGameObject = true;
            using PooledResource<List<CosmeticEffectComponent>> cosmeticEffectsResource =
                Buffers<CosmeticEffectComponent>.List.Get(
                    out List<CosmeticEffectComponent> cosmeticEffectsBuffer
                );
            data.GetComponents(cosmeticEffectsBuffer);
            foreach (CosmeticEffectComponent cosmeticEffect in cosmeticEffectsBuffer)
            {
                // Skip destroyed entries without treating sibling destruction as an opt-out from GameObject cleanup.
                if (cosmeticEffect == null)
                {
                    continue;
                }

                if (cosmeticEffect.CleansUpSelf)
                {
                    shouldDestroyGameObject = false;
                    continue;
                }

                cosmeticEffect.Destroy();
            }

            if (shouldDestroyGameObject && data != null)
            {
                data.gameObject.Destroy();
            }
        }

        private static PooledResource<List<EffectHandle>> RentHandleList(
            out List<EffectHandle> handles
        )
        {
            return Buffers<EffectHandle>.List.Get(out handles);
        }

        private static PooledResource<List<EffectBehavior>> RentBehaviorList(
            out List<EffectBehavior> behaviors
        )
        {
            return Buffers<EffectBehavior>.List.Get(out behaviors);
        }

        private static PooledResource<List<PeriodicEffectRuntimeState>> RentPeriodicStateList(
            out List<PeriodicEffectRuntimeState> states
        )
        {
            return Buffers<PeriodicEffectRuntimeState>.List.Get(out states);
        }

        private static PooledResource<List<CosmeticEffectData>> RentCosmeticDataList(
            out List<CosmeticEffectData> cosmeticData
        )
        {
            return Buffers<CosmeticEffectData>.List.Get(out cosmeticData);
        }

        private static void ClearAndDispose<T>(PooledResource<List<T>> lease)
        {
            List<T> list = lease.resource;
            list?.Clear();
            lease.Dispose();
        }

        private static void RecycleBehaviorList(PooledResource<List<EffectBehavior>> lease)
        {
            ClearAndDispose(lease);
        }

        private static void RecyclePeriodicStateList(
            PooledResource<List<PeriodicEffectRuntimeState>> lease
        )
        {
            ClearAndDispose(lease);
        }

        private static void RecycleCosmeticDataList(
            PooledResource<List<CosmeticEffectData>> cosmeticLease
        )
        {
            List<CosmeticEffectData> cosmeticData = cosmeticLease.resource;
            if (cosmeticData != null)
            {
                for (int i = cosmeticData.Count - 1; 0 <= i; --i)
                {
                    cosmeticData[i] = null;
                }

                cosmeticData.Clear();
            }

            cosmeticLease.Dispose();
        }

        private void ReleaseBehaviorList(PooledResource<List<EffectBehavior>> lease)
        {
            if (0 < _traversalDepth)
            {
                _deferredBehaviorLeases.Add(lease);
                return;
            }

            RecycleBehaviorList(lease);
        }

        private void ReleasePeriodicStateList(
            PooledResource<List<PeriodicEffectRuntimeState>> lease
        )
        {
            if (0 < _traversalDepth)
            {
                _deferredPeriodicLeases.Add(lease);
                return;
            }

            RecyclePeriodicStateList(lease);
        }

        private void ReleaseHandleList(PooledResource<List<EffectHandle>> lease)
        {
            if (0 < _traversalDepth)
            {
                _deferredHandleLeases.Add(lease);
                return;
            }

            ClearAndDispose(lease);
        }

        private void ReleaseCosmeticDataList(PooledResource<List<CosmeticEffectData>> lease)
        {
            if (0 < _traversalDepth)
            {
                _deferredCosmeticLeases.Add(lease);
                return;
            }

            RecycleCosmeticDataList(lease);
        }

        private void EndTraversal()
        {
            --_traversalDepth;
            if (0 < _traversalDepth)
            {
                return;
            }

            FlushDeferredReleases();
        }

        // Drain after outermost traversal; revisit queues because releasing one lease may enqueue another.
        private void FlushDeferredReleases()
        {
            while (
                0 < _deferredBehaviorLeases.Count
                || 0 < _deferredPeriodicLeases.Count
                || 0 < _deferredHandleLeases.Count
                || 0 < _deferredCosmeticLeases.Count
            )
            {
                while (0 < _deferredBehaviorLeases.Count)
                {
                    int lastIndex = _deferredBehaviorLeases.Count - 1;
                    PooledResource<List<EffectBehavior>> lease = _deferredBehaviorLeases[lastIndex];
                    _deferredBehaviorLeases.RemoveAt(lastIndex);
                    RecycleBehaviorList(lease);
                }

                while (0 < _deferredPeriodicLeases.Count)
                {
                    int lastIndex = _deferredPeriodicLeases.Count - 1;
                    PooledResource<List<PeriodicEffectRuntimeState>> lease =
                        _deferredPeriodicLeases[lastIndex];
                    _deferredPeriodicLeases.RemoveAt(lastIndex);
                    RecyclePeriodicStateList(lease);
                }

                while (0 < _deferredHandleLeases.Count)
                {
                    int lastIndex = _deferredHandleLeases.Count - 1;
                    PooledResource<List<EffectHandle>> lease = _deferredHandleLeases[lastIndex];
                    _deferredHandleLeases.RemoveAt(lastIndex);
                    ClearAndDispose(lease);
                }

                while (0 < _deferredCosmeticLeases.Count)
                {
                    int lastIndex = _deferredCosmeticLeases.Count - 1;
                    PooledResource<List<CosmeticEffectData>> lease = _deferredCosmeticLeases[
                        lastIndex
                    ];
                    _deferredCosmeticLeases.RemoveAt(lastIndex);
                    RecycleCosmeticDataList(lease);
                }
            }
        }

        private void Update()
        {
            ProcessEffectExpirations();
            ProcessBehaviorTicks();
            ProcessPeriodicEffects();
        }

        private void ProcessEffectExpirations()
        {
            if (_effectExpirations.Count <= 0)
            {
                return;
            }

            _expiredEffectIds.Clear();
            float currentTime = Time.time;
            foreach (KeyValuePair<long, float> entry in _effectExpirations)
            {
                if (entry.Value <= currentTime)
                {
                    _expiredEffectIds.Add(entry.Key);
                }
            }

            Exception teardownFailure = null;
            try
            {
                // Teardown callbacks can clear this list through OnDestroy; finish expirations before reporting failures.
#pragma warning disable WUH013 // Teardown callbacks can clear this list through OnDestroy.
                for (int i = 0; i < _expiredEffectIds.Count; ++i)
                {
                    if (
                        _effectHandlesById.TryGetValue(
                            _expiredEffectIds[i],
                            out EffectHandle expiredHandle
                        )
                    )
                    {
                        teardownFailure = RecordFailure(
                            teardownFailure,
                            RemoveEffectCore(expiredHandle)
                        );
                    }
                }
#pragma warning restore WUH013
            }
            finally
            {
                _expiredEffectIds.Clear();
            }

            if (teardownFailure != null)
            {
                ExceptionDispatchInfo.Capture(teardownFailure).Throw();
            }
        }

        private void ProcessBehaviorTicks()
        {
            _ = ProcessBehaviorTicks(Time.deltaTime);
        }

        internal int ProcessBehaviorTicksForTesting(float deltaTime)
        {
            return ProcessBehaviorTicks(deltaTime);
        }

        private int ProcessBehaviorTicks(float deltaTime)
        {
            if (_behaviorsByHandleId.Count <= 0)
            {
                return 0;
            }

            int processedTicks = 0;
            ++_traversalDepth;
            try
            {
                using PooledResource<List<long>> behaviorHandleIdsResource = Buffers<long>.List.Get(
                    out List<long> behaviorHandleIdsBuffer
                );
                behaviorHandleIdsBuffer.AddRange(_behaviorsByHandleId.Keys);

                foreach (long handleId in behaviorHandleIdsBuffer)
                {
                    if (!_effectHandlesById.TryGetValue(handleId, out EffectHandle handle))
                    {
                        continue;
                    }

                    if (
                        !_behaviorsByHandleId.TryGetValue(
                            handleId,
                            out PooledResource<List<EffectBehavior>> behaviorLease
                        )
                    )
                    {
                        continue;
                    }

                    List<EffectBehavior> behaviors = behaviorLease.resource;
                    EffectBehaviorContext context = new(this, handle, deltaTime);
                    foreach (EffectBehavior behavior in behaviors)
                    {
                        if (behavior == null)
                        {
                            continue;
                        }

                        behavior.OnTick(context);
                        ++processedTicks;
                        // OnTick can remove the handle and destroy every remaining clone.
                        if (!_effectHandlesById.ContainsKey(handleId))
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                EndTraversal();
            }

            return processedTicks;
        }

        private void ProcessPeriodicEffects()
        {
            _ = ProcessPeriodicEffects(Time.time, Time.deltaTime);
        }

        internal int ProcessPeriodicEffectsForTesting(float currentTime, float deltaTime)
        {
            return ProcessPeriodicEffects(currentTime, deltaTime);
        }

        private int ProcessPeriodicEffects(float currentTime, float deltaTime)
        {
            if (_periodicEffectStates.Count <= 0)
            {
                return 0;
            }

            int consumedTicks = 0;
            ++_traversalDepth;
            try
            {
                using PooledResource<List<long>> periodicRemovalResource = Buffers<long>.List.Get(
                    out List<long> periodicRemovalBuffer
                );
                using PooledResource<List<long>> periodHandleIdsResource = Buffers<long>.List.Get(
                    out List<long> periodicHandleIdsBuffer
                );
                periodicHandleIdsBuffer.AddRange(_periodicEffectStates.Keys);

                foreach (long handleId in periodicHandleIdsBuffer)
                {
                    if (!_effectHandlesById.TryGetValue(handleId, out EffectHandle handle))
                    {
                        periodicRemovalBuffer.Add(handleId);
                        continue;
                    }

                    if (
                        !_periodicEffectStates.TryGetValue(
                            handleId,
                            out PooledResource<List<PeriodicEffectRuntimeState>> runtimesLease
                        )
                    )
                    {
                        continue;
                    }

                    List<PeriodicEffectRuntimeState> runtimes = runtimesLease.resource;
                    bool hasActive = false;
                    bool stillApplied = true;

                    foreach (PeriodicEffectRuntimeState runtimeState in runtimes)
                    {
                        if (runtimeState == null)
                        {
                            continue;
                        }

                        int consumedTicksThisUpdate = 0;
                        while (
                            consumedTicksThisUpdate < MaxPeriodicCatchUpTicksPerUpdate
                            && _effectHandlesById.ContainsKey(handleId)
                            && runtimeState.TryConsumeTick(currentTime)
                        )
                        {
                            consumedTicksThisUpdate++;
                            consumedTicks++;
                            ApplyPeriodicTick(handle, runtimeState, currentTime, deltaTime);
                        }

                        // A periodic callback can cancel the effect; remaining definitions must not tick afterward.
                        if (!_effectHandlesById.ContainsKey(handleId))
                        {
                            stillApplied = false;
                            break;
                        }

                        if (!runtimeState.IsComplete)
                        {
                            hasActive = true;
                        }
                    }

                    if (stillApplied && !hasActive)
                    {
                        periodicRemovalBuffer.Add(handleId);
                    }
                }

                foreach (long periodicHandleId in periodicRemovalBuffer)
                {
                    if (
                        _periodicEffectStates.Remove(
                            periodicHandleId,
                            out PooledResource<List<PeriodicEffectRuntimeState>> lease
                        )
                    )
                    {
                        ReleasePeriodicStateList(lease);
                    }
                }
            }
            finally
            {
                EndTraversal();
            }

            return consumedTicks;
        }
    }
}
