// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Collections.Generic;
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Authoring data for a periodic modifier bundle that executes on a cadence while an effect is active.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The owning <see cref="EffectHandler"/> evaluates each periodic definition after
    /// <see cref="PeriodicEffectDefinition.initialDelay"/>, applies the configured
    /// <see cref="PeriodicEffectDefinition.modifications"/>, and repeats every
    /// <see cref="PeriodicEffectDefinition.interval"/> seconds until
    /// <see cref="PeriodicEffectDefinition.maxTicks"/> is reached or the effect ends.
    /// </para>
    /// <para>
    /// Definitions are processed in list order and maintain independent runtime state per
    /// <see cref="EffectHandle"/>, enabling designers to mix damage-over-time, heal-over-time,
    /// or custom triggers alongside bespoke <see cref="EffectBehavior"/> implementations.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// using System.Collections.Generic;
    /// using UnityEngine;
    /// using WallstopStudios.UnityHelpers.Tags;
    ///
    /// public sealed class BurnEffectAuthoring : MonoBehaviour
    /// {
    ///     [SerializeField]
    ///     private AttributeEffect burnEffect;
    ///
    ///     [SerializeField]
    ///     private EffectHandler effectHandler;
    ///
    ///     public void ApplyBurn(GameObject target)
    ///     {
    ///         PeriodicEffectDefinition burnTick = new PeriodicEffectDefinition
    ///         {
    ///             name = "Burn Damage",
    ///             initialDelay = 0.5f,
    ///             interval = 1.0f,
    ///             maxTicks = 5,
    ///             modifications = new List&lt;AttributeModification&gt;
    ///             {
    ///                 new AttributeModification("Health", ModificationAction.Addition, -5f),
    ///             },
    ///         };
    ///
    ///         burnEffect.periodicEffects.Add(burnTick);
    ///
    ///         if (effectHandler == null)
    ///         {
    ///             effectHandler = target.GetComponent&lt;EffectHandler&gt;();
    ///         }
    ///
    ///         EffectHandle? handle = effectHandler.ApplyEffect(burnEffect);
    ///     }
    /// }
    /// </code>
    /// <para>
    /// In the example above the handler waits for <c>initialDelay</c>, applies the
    /// <see cref="modifications"/> every <c>interval</c> seconds, and stops after
    /// <see cref="maxTicks"/> executions or as soon as the effect is removed.
    /// </para>
    /// </example>
    [Serializable]
    [ProtoContract]
    [WProtoContract]
    public sealed partial class PeriodicEffectDefinition
    {
        /// <summary>
        /// Optional label shown in tooling to help distinguish multiple periodic definitions.
        /// </summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public string name;

        /// <summary>
        /// Time (seconds) before the first tick fires after the effect is applied.
        /// </summary>
        [Min(0f)]
        [ProtoMember(2)]
        [WProtoMember(2)]
        public float initialDelay;

        /// <summary>
        /// Interval (seconds) between ticks once the first tick has executed.
        /// </summary>
        [Min(0.01f)]
        [ProtoMember(3)]
        [WProtoMember(3)]
        public float interval = 1f;

        /// <summary>
        /// Maximum number of ticks to execute. Zero or negative means unlimited ticks.
        /// </summary>
        [Min(0)]
        [ProtoMember(4)]
        [WProtoMember(4)]
        public int maxTicks;

        /// <summary>
        /// Attribute modifications applied each time the tick fires.
        /// </summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        public List<AttributeModification> modifications = new();
    }
}
