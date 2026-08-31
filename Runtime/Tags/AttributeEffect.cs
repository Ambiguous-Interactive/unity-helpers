// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Text;
    using System.Text.Json.Serialization;
    using Core.Extension;
    using Core.Helper;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using AttributeEffectBase = Sirenix.OdinInspector.SerializedScriptableObject;
#else
    using AttributeEffectBase = UnityEngine.ScriptableObject;
#endif

    /// <summary>
    /// Determines which handles are considered the "same stack" when evaluating stacking policies.
    /// </summary>
    public enum EffectStackGroup
    {
        [Obsolete("Please use a valid EffectStackGroup instead.")]
        None = 0,

        /// <summary>
        /// Uses the effect asset reference. Each ScriptableObject instance is its own group.
        /// </summary>
        Reference = 1,

        /// <summary>
        /// Uses a custom string key supplied via <see cref="AttributeEffect.stackGroupKey"/>.
        /// Effects with matching keys share a stack regardless of asset reference.
        /// </summary>
        CustomKey = 2,
    }

    /// <summary>
    /// Describes how additional applications of an effect interact with existing stacks.
    /// </summary>
    public enum EffectStackingMode
    {
        [Obsolete("Please use a valid EffectStackingMode instead.")]
        None = 0,

        /// <summary>
        /// Always create a new stack (subject to optional stack limit).
        /// </summary>
        Stack = 1,

        /// <summary>
        /// Reuse the first existing stack and refresh duration if possible.
        /// </summary>
        Refresh = 2,

        /// <summary>
        /// Remove existing stacks sharing the same group before creating a new one.
        /// </summary>
        Replace = 3,

        /// <summary>
        /// Ignore new applications when a stack is already active.
        /// </summary>
        Ignore = 4,
    }

    /// <summary>
    /// Reusable, data‑driven bundle of stat modifications, tags, and cosmetic feedback.
    /// Serves as the authoring unit for buffs, debuffs, and status effects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Composition:
    /// - Attribute modifications: a list of <see cref="AttributeModification"/> applied to <see cref="Attribute"/> fields
    /// - Tags: string markers for cross‑system state gating and queries
    /// - Cosmetics: <see cref="CosmeticEffectData"/> references for visuals/audio on apply/remove
    /// - Duration: <see cref="ModifierDurationType"/> with seconds and reapplication policy
    /// </para>
    /// <para>
    /// Problems solved and benefits:
    /// - Centralizes effect logic and presentation in one asset
    /// - Safely stacks via <see cref="EffectHandle"/> per application
    /// - Works with <see cref="EffectHandler"/> and <see cref="TagHandler"/> for lifecycle and state tracking
    /// - Author once, reuse everywhere (designers can tweak without code changes)
    /// </para>
    /// <para>
    /// Usage examples:
    /// <code>
    /// // Create a speed boost effect in the editor
    /// // Then apply it to a GameObject:
    /// GameObject player = ...;
    /// AttributeEffect speedBoost = ...; // ScriptableObject reference
    /// EffectHandle? handle = player.ApplyEffect(speedBoost);
    ///
    /// // Instant vs Duration vs Infinite
    /// //  - Instant: modifies base values permanently, returns null handle
    /// //  - Duration: temporary, expires automatically, returns handle
    /// //  - Infinite: persists until RemoveEffect(handle) is called, returns handle
    ///
    /// // Removing later
    /// if (handle.HasValue) player.RemoveEffect(handle.Value);
    /// </code>
    /// </para>
    /// </remarks>
    [Serializable]
    [CreateAssetMenu(menuName = "Wallstop Studios/Unity Helpers/Attribute Effect")]
    public sealed class AttributeEffect : AttributeEffectBase, IEquatable<AttributeEffect>
    {
        /// <summary>
        /// Gets a human-readable description of this effect based on its modifications.
        /// The description is automatically generated from the modifications list.
        /// </summary>
        /// <value>A formatted string describing all modifications in this effect.</value>
        /// <example>"+20 Health, +1.5x Speed, -10% Defense"</example>
        public string HumanReadableDescription => BuildDescription();

        /// <summary>
        /// The list of attribute modifications to apply when this effect is activated.
        /// Each modification specifies an attribute name, action type, and value.
        /// </summary>
        public List<AttributeModification> modifications = new();

        /// <summary>
        /// Periodic modifier sets executed on a cadence while the effect remains active.
        /// </summary>
        public List<PeriodicEffectDefinition> periodicEffects = new();

        /// <summary>
        /// Specifies how long this effect should persist (Instant, Duration, or Infinite).
        /// </summary>
        public ModifierDurationType durationType = ModifierDurationType.Duration;

        /// <summary>
        /// The duration in seconds for this effect. Only used when <see cref="durationType"/> is <see cref="ModifierDurationType.Duration"/>.
        /// </summary>
        [WShowIf(
            nameof(durationType),
            expectedValues: new object[] { ModifierDurationType.Duration }
        )]
        public float duration;

        /// <summary>
        /// If true, reapplying this effect while it's already active will reset the duration timer.
        /// Only used when <see cref="durationType"/> is <see cref="ModifierDurationType.Duration"/>.
        /// </summary>
        /// <example>
        /// A poison effect with resetDurationOnReapplication=true will restart its 5-second timer
        /// each time the poison is reapplied, preventing stacking but extending the effect.
        /// </example>
        [WShowIf(
            nameof(durationType),
            expectedValues: new object[] { ModifierDurationType.Duration }
        )]
        public bool resetDurationOnReapplication;

        /// <summary>
        /// A list of string tags that are applied when this effect is active.
        /// Tags can be used to track effect categories, prevent certain actions, or enable special behaviors.
        /// </summary>
        /// <example>
        /// Tags like "Stunned", "Poisoned", "Invulnerable" can be checked by game systems
        /// to determine if certain actions should be allowed or prevented.
        /// </example>
        public List<string> effectTags = new();

        /// <summary>
        /// A list of cosmetic effect data that defines visual and audio feedback for this effect.
        /// These are applied when the effect becomes active and removed when it expires.
        /// </summary>
        [JsonIgnore]
        public List<CosmeticEffectData> cosmeticEffects = new();

        /// <summary>
        /// Custom behaviours instantiated per active handle.
        /// </summary>
        [JsonIgnore]
        public List<EffectBehavior> behaviors = new();

        [NonSerialized]
        private bool _instantWithHandleDataReported;

        [NonSerialized]
        private bool _unassignedCosmeticReported;

        /// <summary>
        /// Gets whether this effect is <see cref="ModifierDurationType.Instant"/> yet carries
        /// periodic or behaviour data. Instant effects return no handle, so neither can ever run.
        /// </summary>
        internal bool IsInstantWithHandleData =>
            durationType == ModifierDurationType.Instant
            && ((periodicEffects is { Count: > 0 }) || (behaviors is { Count: > 0 }));

        /// <summary>
        /// Returns <c>true</c> the first time it is called on a misconfigured effect, and
        /// <c>false</c> forever after.
        /// </summary>
        /// <remarks>
        /// The condition is a static property of the asset, so reporting it on the per-application
        /// path made a single authoring mistake cost a diagnostic on every hit, every tick, for the
        /// whole session. Editing the effect re-arms the report through
        /// <see cref="OnValidate"/>.
        /// </remarks>
        internal bool ShouldReportInstantWithHandleData()
        {
            if (_instantWithHandleDataReported || !IsInstantWithHandleData)
            {
                return false;
            }

            _instantWithHandleDataReported = true;
            return true;
        }

        /// <summary>
        /// Gets whether <see cref="cosmeticEffects"/> holds an unassigned entry. Those entries
        /// cannot be instanced, so they are skipped every time the effect is applied.
        /// </summary>
        internal bool HasUnassignedCosmeticEffect
        {
            get
            {
                foreach (CosmeticEffectData cosmeticEffect in cosmeticEffects)
                {
                    if (cosmeticEffect == null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Returns <c>true</c> the first time it is called on an effect holding an unassigned
        /// cosmetic entry, and <c>false</c> forever after.
        /// </summary>
        /// <remarks>
        /// Same shape as <see cref="ShouldReportInstantWithHandleData"/>: a static property of the
        /// asset, previously reported once per unassigned entry per application.
        /// </remarks>
        internal bool ShouldReportUnassignedCosmeticEffect()
        {
            if (_unassignedCosmeticReported || !HasUnassignedCosmeticEffect)
            {
                return false;
            }

            _unassignedCosmeticReported = true;
            return true;
        }

        private void OnValidate()
        {
            _instantWithHandleDataReported = false;
            _unassignedCosmeticReported = false;
            if (IsInstantWithHandleData)
            {
                this.LogWarn(
                    $"Effect {name} defines periodic or behaviour data but is Instant. These features require a Duration or Infinite effect.",
                    stackTrace: false
                );
            }

            if (HasUnassignedCosmeticEffect)
            {
                this.LogWarn(
                    $"Effect {name} has an unassigned CosmeticEffectData entry, which cannot be instanced and is skipped.",
                    stackTrace: false
                );
            }
        }

        /// <summary>
        /// Determines how this effect groups stacks for stacking decisions.
        /// </summary>
        public EffectStackGroup stackGroup = EffectStackGroup.Reference;

        /// <summary>
        /// Optional stack key used when <see cref="stackGroup"/> is set to <see cref="EffectStackGroup.CustomKey"/>.
        /// </summary>
        public string stackGroupKey;

        /// <summary>
        /// Determines how successive applications interact with existing stacks for the same group.
        /// </summary>
        public EffectStackingMode stackingMode = EffectStackingMode.Refresh;

        /// <summary>
        /// Optional cap on simultaneous stacks when <see cref="stackingMode"/> is <see cref="EffectStackingMode.Stack"/>.
        /// A value of 0 means unlimited stacks.
        /// </summary>
        [Min(0)]
        public int maximumStacks;

        /// <summary>
        /// Determines whether this effect applies the specified tag.
        /// </summary>
        /// <param name="effectTag">The tag to check.</param>
        /// <returns><c>true</c> if the tag is present; otherwise, <c>false</c>.</returns>
        public bool HasTag(string effectTag)
        {
            if (effectTags == null || string.IsNullOrEmpty(effectTag))
            {
                return false;
            }

            for (int i = 0; i < effectTags.Count; ++i)
            {
                if (string.Equals(effectTags[i], effectTag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether this effect applies any of the specified tags.
        /// </summary>
        /// <param name="effectTagsToCheck">The tags to inspect.</param>
        /// <returns><c>true</c> if at least one tag is applied; otherwise, <c>false</c>.</returns>
        public bool HasAnyTag(IEnumerable<string> effectTagsToCheck)
        {
            if (effectTags == null || effectTagsToCheck == null)
            {
                return false;
            }

            switch (effectTagsToCheck)
            {
                case IReadOnlyList<string> list:
                {
                    return HasAnyTag(list);
                }
                case HashSet<string> hashSet:
                {
                    foreach (string candidate in hashSet)
                    {
                        if (string.IsNullOrEmpty(candidate))
                        {
                            continue;
                        }

                        if (HasTag(candidate))
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }

            foreach (string candidate in effectTagsToCheck)
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                if (HasTag(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether this effect applies any of the specified tags.
        /// Optimized for indexed collections.
        /// </summary>
        /// <param name="effectTagsToCheck">The tags to inspect.</param>
        /// <returns><c>true</c> if at least one tag is applied; otherwise, <c>false</c>.</returns>
        public bool HasAnyTag(IReadOnlyList<string> effectTagsToCheck)
        {
            if (effectTags == null || effectTagsToCheck == null)
            {
                return false;
            }

            for (int i = 0; i < effectTagsToCheck.Count; ++i)
            {
                string candidate = effectTagsToCheck[i];
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                if (HasTag(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether this effect contains modifications for the specified attribute.
        /// </summary>
        /// <param name="attributeName">The attribute name to inspect.</param>
        /// <returns><c>true</c> if the effect modifies <paramref name="attributeName"/>; otherwise, <c>false</c>.</returns>
        public bool ModifiesAttribute(string attributeName)
        {
            if (modifications == null || string.IsNullOrEmpty(attributeName))
            {
                return false;
            }

            for (int i = 0; i < modifications.Count; ++i)
            {
                AttributeModification modification = modifications[i];
                if (string.Equals(modification.attribute, attributeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Copies all modifications that affect the specified attribute into the provided buffer.
        /// </summary>
        /// <param name="attributeName">The attribute to filter by.</param>
        /// <param name="buffer">The destination buffer. Existing entries are preserved.</param>
        /// <returns>The number of modifications added to <paramref name="buffer"/>.</returns>
        public List<AttributeModification> GetModifications(
            string attributeName,
            List<AttributeModification> buffer = null
        )
        {
            buffer ??= new List<AttributeModification>();
            buffer.Clear();
            if (modifications == null || string.IsNullOrEmpty(attributeName))
            {
                return buffer;
            }

            for (int i = 0; i < modifications.Count; ++i)
            {
                AttributeModification modification = modifications[i];
                if (string.Equals(modification.attribute, attributeName, StringComparison.Ordinal))
                {
                    buffer.Add(modification);
                }
            }

            return buffer;
        }

        /// <summary>
        /// Converts this effect to a JSON string representation including all modifications, tags, and cosmetic effects.
        /// </summary>
        /// <returns>A JSON string representing this effect.</returns>
        public override string ToString()
        {
            string[] cosmeticEffectNames = BuildCosmeticEffectNames();

            return new
            {
                Description = HumanReadableDescription,
                CosmeticEffects = cosmeticEffectNames,
                modifications,
                durationType,
                duration,
                tags = effectTags,
            }.ToJson();
        }

        private string[] BuildCosmeticEffectNames()
        {
            if (cosmeticEffects == null || cosmeticEffects.Count == 0)
            {
                return Array.Empty<string>();
            }

            using PooledResource<List<string>> namesLease = Buffers<string>.List.Get(
                out List<string> names
            );
            {
                for (int i = 0; i < cosmeticEffects.Count; i++)
                {
                    CosmeticEffectData effect = cosmeticEffects[i];
                    if (effect == null)
                    {
                        continue;
                    }

                    string effectName = effect.name;
                    if (effectName.Length == 0)
                    {
                        continue;
                    }

                    names.Add(effectName);
                }

                if (names.Count == 0)
                {
                    return Array.Empty<string>();
                }

                return names.ToArray();
            }
        }

        private string BuildDescription()
        {
            if (modifications == null)
            {
                return nameof(AttributeEffect);
            }

            using PooledResource<StringBuilder> stringBuilderBuffer = Buffers.StringBuilder.Get(
                out StringBuilder descriptionBuilder
            );
            for (int i = 0; i < modifications.Count; ++i)
            {
                AttributeModification modification = modifications[i];
                switch (modification.action)
                {
                    case ModificationAction.Addition:
                    {
                        if (modification.value < 0)
                        {
                            _ = descriptionBuilder.Append(modification.value);
                            _ = descriptionBuilder.Append(' ');
                        }
                        else if (modification.value == 0)
                        {
                            continue;
                        }
                        else
                        {
                            _ = descriptionBuilder.AppendFormat("+{0} ", modification.value);
                        }

                        break;
                    }
                    case ModificationAction.Multiplication:
                    {
                        if (modification.value < 1)
                        {
                            _ = descriptionBuilder.AppendFormat(
                                "-{0}% ",
                                (1 - modification.value) * 100
                            );
                        }
                        // ReSharper disable once CompareOfFloatsByEqualityOperator
                        else if (modification.value == 1)
                        {
                            continue;
                        }
                        else
                        {
                            _ = descriptionBuilder.AppendFormat(
                                "+{0}% ",
                                (modification.value - 1) * 100
                            );
                        }

                        break;
                    }
                    case ModificationAction.Override:
                    {
                        _ = descriptionBuilder.AppendFormat("{0} ", modification.value);
                        break;
                    }
                    default:
                    {
                        throw new InvalidEnumArgumentException(
                            nameof(modification.value),
                            (int)modification.value,
                            typeof(ModificationAction)
                        );
                    }
                }

                _ = descriptionBuilder.Append(modification.attribute.ToPascalCase(" "));
                if (i < modifications.Count - 1)
                {
                    _ = descriptionBuilder.Append(", ");
                }
            }

            return descriptionBuilder.ToString();
        }

        internal EffectStackKey GetStackKey()
        {
            if (stackGroup == EffectStackGroup.CustomKey && !string.IsNullOrEmpty(stackGroupKey))
            {
                return EffectStackKey.CreateCustom(stackGroupKey);
            }

            return EffectStackKey.CreateReference(this);
        }

        /// <summary>
        /// Determines whether this effect is equal to another effect by comparing every authored
        /// field that changes how the effect behaves: the name, duration policy, ordered
        /// modifications, periodic definitions, tags, cosmetics, behaviours and the whole stacking
        /// configuration.
        /// This is needed because deserialization creates new instances, so reference equality is insufficient.
        /// </summary>
        /// <param name="other">The effect to compare with.</param>
        /// <returns><c>true</c> if every authored field matches; otherwise, <c>false</c>.</returns>
        public bool Equals(AttributeEffect other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null)
            {
                return false;
            }

            if (!string.Equals(name, other.name))
            {
                return false;
            }

            if (durationType != other.durationType)
            {
                return false;
            }

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (duration != other.duration)
            {
                return false;
            }

            if (resetDurationOnReapplication != other.resetDurationOnReapplication)
            {
                return false;
            }

            if (modifications == null)
            {
                if (other.modifications != null)
                {
                    return false;
                }
            }
            else if (other.modifications == null)
            {
                return false;
            }
            else
            {
                if (modifications.Count != other.modifications.Count)
                {
                    return false;
                }

                for (int i = 0; i < modifications.Count; ++i)
                {
                    if (modifications[i] != other.modifications[i])
                    {
                        return false;
                    }
                }
            }

            if (effectTags == null)
            {
                if (other.effectTags != null)
                {
                    return false;
                }
            }
            else if (other.effectTags == null)
            {
                return false;
            }
            else
            {
                if (effectTags.Count != other.effectTags.Count)
                {
                    return false;
                }

                for (int i = 0; i < effectTags.Count; ++i)
                {
                    if (
                        !string.Equals(effectTags[i], other.effectTags[i], StringComparison.Ordinal)
                    )
                    {
                        return false;
                    }
                }
            }

            if (cosmeticEffects == null)
            {
                if (other.cosmeticEffects != null)
                {
                    return false;
                }
            }
            else if (other.cosmeticEffects == null)
            {
                return false;
            }
            else
            {
                if (cosmeticEffects.Count != other.cosmeticEffects.Count)
                {
                    return false;
                }

                for (int i = 0; i < cosmeticEffects.Count; ++i)
                {
                    if (!Equals(cosmeticEffects[i], other.cosmeticEffects[i]))
                    {
                        return false;
                    }
                }
            }

            if (!PeriodicEffectsEqual(periodicEffects, other.periodicEffects))
            {
                return false;
            }

            if (!BehaviorsEqual(behaviors, other.behaviors))
            {
                return false;
            }

            if (stackGroup != other.stackGroup)
            {
                return false;
            }

            if (!string.Equals(stackGroupKey, other.stackGroupKey, StringComparison.Ordinal))
            {
                return false;
            }

            if (stackingMode != other.stackingMode)
            {
                return false;
            }

            return maximumStacks == other.maximumStacks;
        }

        /// <summary>
        /// Determines whether this effect equals the specified object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns><c>true</c> if the object is an AttributeEffect with equal values; otherwise, <c>false</c>.</returns>
        public override bool Equals(object obj)
        {
            return ReferenceEquals(this, obj) || obj is AttributeEffect other && Equals(other);
        }

        /// <summary>
        /// Returns the hash code for this effect, derived from a subset of the authored fields
        /// <see cref="Equals(AttributeEffect)"/> compares.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An effect asset is authored data, so editing one in the Inspector moves its hash. Re-add
        /// an edited effect to any set or dictionary that was keyed on it.
        /// </para>
        /// <para>
        /// The two lists of Unity objects contribute their lengths rather than their contents, and
        /// the name is read through <see cref="Helpers.NameHashCode"/>. A hash a collection computes
        /// on every probe may not depend on live native state: reading <c>name</c> on a destroyed
        /// asset raises <c>MissingReferenceException</c>, and hashing a
        /// <see cref="CosmeticEffectData"/> walks its components. Hashing less than equality
        /// compares is coarser, never wrong.
        /// </para>
        /// </remarks>
        /// <returns>A hash code combining the managed authored fields.</returns>
        public override int GetHashCode()
        {
            return Objects.HashCode(
                Helpers.NameHashCode(this),
                durationType,
                duration,
                resetDurationOnReapplication,
                Objects.EnumerableHashCode(modifications),
                PeriodicEffectsHashCode(periodicEffects),
                Objects.EnumerableHashCode(effectTags),
                cosmeticEffects != null ? cosmeticEffects.Count : 0,
                behaviors != null ? behaviors.Count : 0,
                stackGroup,
                stackGroupKey,
                stackingMode,
                maximumStacks
            );
        }

        private static bool PeriodicEffectsEqual(
            List<PeriodicEffectDefinition> left,
            List<PeriodicEffectDefinition> right
        )
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; ++i)
            {
                if (!PeriodicEffectEqual(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /*
            Compared by content rather than by reference: the whole point of AttributeEffect.Equals
            is that deserialization hands back fresh instances, and PeriodicEffectDefinition is a
            plain serializable class with no equality of its own.
        */
        private static bool PeriodicEffectEqual(
            PeriodicEffectDefinition left,
            PeriodicEffectDefinition right
        )
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (!string.Equals(left.name, right.name, StringComparison.Ordinal))
            {
                return false;
            }

            if (!left.initialDelay.Equals(right.initialDelay))
            {
                return false;
            }

            if (!left.interval.Equals(right.interval))
            {
                return false;
            }

            if (left.maxTicks != right.maxTicks)
            {
                return false;
            }

            return ModificationsEqual(left.modifications, right.modifications);
        }

        private static bool ModificationsEqual(
            List<AttributeModification> left,
            List<AttributeModification> right
        )
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; ++i)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        /*
            Behaviours are ScriptableObject assets whose subclasses define their own state, so the
            only equality this type can honestly claim over them is identity.
        */
        private static bool BehaviorsEqual(List<EffectBehavior> left, List<EffectBehavior> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; ++i)
            {
                if (!ReferenceEquals(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int PeriodicEffectsHashCode(List<PeriodicEffectDefinition> definitions)
        {
            if (definitions == null)
            {
                return 0;
            }

            int hash = 0;
            for (int i = 0; i < definitions.Count; ++i)
            {
                hash = Objects.HashCode(hash, PeriodicEffectHashCode(definitions[i]));
            }

            return hash;
        }

        private static int PeriodicEffectHashCode(PeriodicEffectDefinition definition)
        {
            if (definition == null)
            {
                return 0;
            }

            return Objects.HashCode(
                definition.name,
                definition.initialDelay,
                definition.interval,
                definition.maxTicks,
                Objects.EnumerableHashCode(definition.modifications)
            );
        }
    }
}
