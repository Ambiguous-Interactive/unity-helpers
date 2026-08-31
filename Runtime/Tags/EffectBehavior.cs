// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using UnityEngine;

    /// <summary>
    /// Base class for authoring custom effect behaviours that respond to the lifecycle of an active <see cref="AttributeEffect"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each behaviour asset is cloned per <see cref="EffectHandle"/>, which means derived classes can safely store mutable state
    /// between calls to <see cref="OnApply(EffectBehaviorContext)"/>, <see cref="OnTick(EffectBehaviorContext)"/>,
    /// <see cref="OnPeriodicTick(EffectBehaviorContext, PeriodicEffectTickContext)"/>, and <see cref="OnRemove(EffectBehaviorContext)"/>.
    /// </para>
    /// <para>
    /// Attach behaviour assets to <see cref="AttributeEffect.behaviors"/> to augment the data-driven attribute pipeline with bespoke
    /// gameplay logic, visual or audio feedback, or integration hooks into other game systems.
    /// </para>
    /// <para>
    /// All callbacks are synchronously invoked by <see cref="EffectHandler"/> on the main thread, ensuring safe interaction with Unity APIs.
    /// </para>
    /// <para>
    /// Every callback may re-enter the handler, including removing the handle it was given. Doing so
    /// detaches the handle first, so <see cref="EffectHandler.IsEffectActive"/> reports it inactive,
    /// removing it again is a no-op, and re-applying the effect produces an independent new handle.
    /// <see cref="OnRemove(EffectBehaviorContext)"/> is delivered to this instance and to every
    /// behaviour cloned alongside it before control returns to the callback, and no further
    /// <see cref="OnTick(EffectBehaviorContext)"/> or
    /// <see cref="OnPeriodicTick(EffectBehaviorContext, PeriodicEffectTickContext)"/> is delivered
    /// for that handle.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// using UnityEngine;
    /// using WallstopStudios.UnityHelpers.Tags;
    ///
    /// [CreateAssetMenu(menuName = "Game/Effects/Burning Behaviour")]
    /// public sealed class BurningBehavior : EffectBehavior
    /// {
    ///     [SerializeField]
    ///     private GameObject flamePrefab;
    ///
    ///     private GameObject spawnedInstance;
    ///
    ///     public override void OnApply(EffectBehaviorContext context)
    ///     {
    ///         if (flamePrefab == null)
    ///         {
    ///             return;
    ///         }
    ///
    ///         Transform parent = context.Target.transform;
    ///         spawnedInstance = Object.Instantiate(flamePrefab, parent.position, parent.rotation, parent);
    ///     }
    ///
    ///     public override void OnPeriodicTick(EffectBehaviorContext context, PeriodicEffectTickContext tickContext)
    ///     {
    ///         // Cancel the effect early once the periodic bundle has executed three times. Safe from
    ///         // inside the callback: OnRemove reaches this instance and its siblings before control
    ///         // returns here, and no further ticks are delivered for this handle.
    ///         if (3 &lt;= tickContext.executedTicks)
    ///         {
    ///             context.handler.RemoveEffect(context.handle);
    ///         }
    ///     }
    ///
    ///     public override void OnRemove(EffectBehaviorContext context)
    ///     {
    ///         if (spawnedInstance != null)
    ///         {
    ///             Object.Destroy(spawnedInstance);
    ///             spawnedInstance = null;
    ///         }
    ///     }
    /// }
    ///
    /// // Assign the behaviour asset to an AttributeEffect so it is cloned per application.
    /// AttributeEffect burnEffect = ScriptableObject.CreateInstance&lt;AttributeEffect&gt;();
    /// burnEffect.behaviors.Add(burningBehaviorAsset);
    /// </code>
    /// </example>
    public abstract class EffectBehavior : ScriptableObject
    {
        /// <summary>
        /// Invoked once when the effect handle becomes active.
        /// </summary>
        /// <param name="context">Runtime context for the effect instance.</param>
        public virtual void OnApply(EffectBehaviorContext context) { }

        /// <summary>
        /// Invoked every frame while the effect remains active.
        /// </summary>
        /// <param name="context">Runtime context for the effect instance.</param>
        public virtual void OnTick(EffectBehaviorContext context) { }

        /// <summary>
        /// Invoked after a periodic tick has been processed for the owning effect.
        /// </summary>
        /// <param name="context">Runtime context for the effect instance.</param>
        /// <param name="tickContext">Information about the specific periodic tick.</param>
        public virtual void OnPeriodicTick(
            EffectBehaviorContext context,
            PeriodicEffectTickContext tickContext
        ) { }

        /// <summary>
        /// Invoked when the effect handle is removed or expires.
        /// </summary>
        /// <param name="context">Runtime context for the effect instance.</param>
        public virtual void OnRemove(EffectBehaviorContext context) { }
    }

    /// <summary>
    /// Immutable runtime data passed to behaviour callbacks.
    /// </summary>
    public readonly struct EffectBehaviorContext
    {
        /// <summary>
        /// Gets the effect asset backing the handle.
        /// </summary>
        public AttributeEffect Effect => handle.effect;

        /// <summary>
        /// Gets the GameObject targeted by the effect handler.
        /// </summary>
        public GameObject Target => handler.gameObject;

        /// <summary>
        /// Gets the deltaTime used for the current invocation. For <see cref="EffectBehavior.OnApply"/>,
        /// this value is zero.
        /// </summary>
        public readonly float deltaTime;

        /// <summary>
        /// Gets the handler managing the effect.
        /// </summary>
        public readonly EffectHandler handler;

        /// <summary>
        /// Gets the handle associated with this behaviour invocation.
        /// </summary>
        public readonly EffectHandle handle;

        public EffectBehaviorContext(EffectHandler handler, EffectHandle handle, float deltaTime)
        {
            this.handler = handler;
            this.handle = handle;
            this.deltaTime = deltaTime;
        }
    }

    /// <summary>
    /// Details about a specific periodic tick that just executed.
    /// </summary>
    public readonly struct PeriodicEffectTickContext
    {
        /// <summary>
        /// Gets the periodic definition that produced this tick.
        /// </summary>
        public readonly PeriodicEffectDefinition definition;

        /// <summary>
        /// Gets the number of ticks executed so far, including this one.
        /// </summary>
        public readonly int executedTicks;

        /// <summary>
        /// Gets the timestamp when the tick occurred.
        /// </summary>
        public readonly float currentTime;

        public PeriodicEffectTickContext(
            PeriodicEffectDefinition definition,
            int executedTicks,
            float currentTime
        )
        {
            this.definition = definition;
            this.executedTicks = executedTicks;
            this.currentTime = currentTime;
        }
    }
}
