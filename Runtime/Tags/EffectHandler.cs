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
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
        private TagHandler _tagHandler;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value

        [SiblingComponent(Optional = true)]
        private HashSet<AttributesComponent> _attributes;

        // Stores instanced cosmetic effect data for associated effects.
        private readonly Dictionary<
            EffectHandle,
            PooledResource<List<CosmeticEffectData>>
        > _instancedCosmeticEffects = new();

        /*
            Application can be abandoned before the cosmetic phase is reached, because a behaviour
            or a tag subscriber removed the handle from OnApply. Teardown must then deliver no
            OnRemoveEffect at all: a user override that stops a particle system would otherwise stop
            one that never started.
        */
        private readonly HashSet<long> _handlesWithAppliedCosmetics = new();

        private readonly Dictionary<
            EffectStackKey,
            PooledResource<List<EffectHandle>>
        > _handlesByStackKey = new();
        private readonly Dictionary<long, EffectStackKey> _stackKeyByHandleId = new();

        // Stores expiration time of duration effects (We store by Id because it's much cheaper to iterate Guids than it is EffectHandles
        private readonly Dictionary<long, float> _effectExpirations = new();
        private readonly Dictionary<long, EffectHandle> _effectHandlesById = new();

        // Used only to save allocations in Update()
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

        /*
            A lease owned by a dictionary slot can be under enumeration by a suspended tick loop
            when a callback removes its handle. Returning it to the shared pool then does two things
            at once: Clear() invalidates the enumerator, and the LIFO pool hands the very same List
            back to the next caller. Releases taken during a traversal are queued here and flushed
            when the outermost one exits. Members rather than rented buffers, so the steady tick
            path stays allocation-free.
        */
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
                /*
                    A static property of the asset, so it is reported once per effect rather than
                    on every application, and by name rather than by serializing the whole effect
                    to JSON inside the interpolated string (#567). AttributeEffect.OnValidate
                    reports the same mistake in the Inspector, where it is fixable.
                */
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
                /*
                    The caller never receives this handle, so nobody could ever remove it. Unwind
                    the partial application before the exception leaves, and keep the original
                    failure -- a teardown that also throws must not mask the cause.
                */
                Exception rollbackFailure = RemoveEffectCore(newHandle);
                /*
                    Idempotent, and the only index registered before the first statement that could
                    have thrown.
                */
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
            /*
                Re-resolved every iteration, never held across RemoveEffect. Removing the last
                handle for a stack key returns that list to the shared buffer pool, and RemoveEffect
                also invokes OnEffectRemoved -- so a subscriber that applies an effect, or rents a
                list of its own, can be handed the very list this loop was reading. Replace, three
                cases up, copies for the same reason.
            */
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
            /*
                Destruction can reach here from inside a live traversal -- a behaviour that calls
                DestroyImmediate on this GameObject from OnTick -- so it takes the same traversal
                discipline every other phase does. Stamping the counter instead would drive it
                negative as the suspended frames unwind, and disable deferral for good.
            */
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

        /*
            Returns the first exception a teardown callback raised, or null. Never throws: every
            caller decides for itself whether the failure propagates or is logged, and the
            transition itself completes either way.
        */
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

        /*
            _attributes is a HashSet, and every loop over it crosses user code: an attribute
            notification can AddComponent another AttributesComponent -- which registers itself
            here from Awake -- or destroy one, either of which invalidates the enumerator. The throw
            that follows is raised by the foreach itself, OUTSIDE the per-component catch, so it
            escapes the removal sequence entirely: the remaining phases never run, tags stay raised,
            instanced cosmetics are orphaned and behaviour clones leak. Copy first; the pooled list
            keeps the steady path allocation-free.

            A copy can hold a component the same callback destroyed, and only the removal loop
            catches per element -- so every caller tests each entry with Unity's `==` before using
            it, or the phase aborts on a MissingReferenceException instead of the throw it replaced.
        */
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

        /*
            Tags are removed after attributes so cosmetic components can look up whether any tags
            are still applied.
        */
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

                /*
                    Destroyed whatever OnRemove did: the clone is this handler's, and a throwing
                    callback must not leak a ScriptableObject.
                */
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
                /*
                    A behaviour is free to remove its own handle from OnApply. Everything below
                    applies state that the teardown has already run past, so it would never be
                    removed.
                */
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

        /*
            No Instant warning here. The one caller is ApplyEffect's Instant branch, which has
            already tested the condition -- so this one was always true and the message was always
            the second copy of one the console had just shown.
        */
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
                    /*
                        Published before the first clone exists, so a behaviour that removes its own
                        handle from OnApply finds its siblings to tear them down, and a throw from
                        Instantiate leaves the lease and every clone already in it reachable by the
                        rollback in ApplyEffect rather than stranded.
                    */
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
                    /*
                        Removing the handle from OnPeriodicTick is the documented way to cancel an
                        effect early. Every clone in this list has already had OnRemove delivered by
                        the time control returns here.
                    */
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
            /*
                Refresh is the DEFAULT stacking mode, and it re-enters this method for an effect
                that is already applied. Tags, attributes and instanced cosmetics each have an
                idempotence guard; a SHARED cosmetic had none, because the guard above only covers
                the instanced ones. So every refresh delivered a second OnApplyEffect against one
                OnRemoveEffect: a template that starts a particle system on apply and stops it on
                remove never stopped, and the shared template's applied-target list grew by an
                entry per refresh, holding the entity's GameObject after it was destroyed. The
                return value of this Add is exactly the "already applied" bit, and it was discarded.
            */
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
                        /*
                            Same static-authoring shape as the Instant diagnostic above: once per
                            effect, by name rather than by serializing it, and no stack trace -- the
                            mistake is in the asset, not on this call stack (#567).
                        */
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
                            /*
                                Published before the first instance exists, so a callback that
                                removes the handle -- or a throw from Instantiate -- destroys what
                                was made instead of orphaning it under this transform. A re-entrant
                                application that already claimed the slot owns those instances, and
                                this one stops rather than duplicating them.
                            */
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
                        /*
                            The buffer is a snapshot, and one entry's callback can destroy another
                            -- a one-shot sibling cleaning itself up takes its neighbors with it,
                            and Destroy runs OnDestroy immediately outside play mode. Invoking a
                            destroyed component throws out of a phase that has already raised the
                            tags, so the same guard DestroyCosmeticInstance carries belongs on
                            every one of these loops.
                        */
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

        /*
            The effect declared no instanced cosmetics, so the components live on the shared
            template and only need the removal callback. Returns the first callback failure, or
            null.
        */
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

                /*
                    An instancing entry with nothing instanced means the cosmetic phase stopped
                    before it was reached, because an earlier entry's callback removed the handle.
                    There is nothing to tear down and nothing wrong.
                */
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

        /*
            The caller has already detached the lease, so nothing can reach these instances again.
            Releasing the lease itself is the caller's job, in its finally. Returns the first
            callback failure, or null -- every instance is notified and destroyed whatever one of
            them does.
        */
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
                /*
                    A component that cleans itself up can take its neighbors with it, and Destroy
                    runs OnDestroy immediately outside play mode, so an entry read after either is a
                    destroyed object. RequiresInstancing and GetCurrentCosmeticTypes already check;
                    these two reads were the ones that did not.

                    A destroyed entry does not change the decision below. It is tempting to read
                    one as having cleaned itself up, but a sibling's teardown destroys components
                    that never opted out, and vetoing on their behalf leaks the GameObject this
                    call exists to destroy. The guard is here to stop the read from throwing,
                    nothing more.
                */
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

        /*
            Returning a lease clears its list, which is exactly what a suspended enumerator cannot
            survive -- so nothing here runs until the outermost traversal has exited. Each queue is
            popped from the end rather than enumerated, and the queues are revisited until all four
            are empty, so a lease queued into an earlier one while a later one drains is still
            released by this call.
        */
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
                /*
                    Indexed rather than enumerated: a teardown callback is free to destroy this
                    handler, and OnDestroy clears this very list. One subscriber that throws must
                    not hold back the rest of this frame's expirations either, so the failure is
                    reported once they have all run.
                */
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
                        /*
                            OnTick may have removed this handle, in which case every clone in this
                            list has already been sent OnRemove and destroyed.
                        */
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

                        /*
                            Removing the handle from a periodic callback is the documented way to
                            cancel an effect early: the remaining definitions must not tick, and
                            this list is no longer ours to read once the outermost traversal exits.
                        */
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
