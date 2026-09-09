// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System.Collections.Generic;
    using Core.Extension;
    using UnityEngine;

    /// <summary>
    /// Abstract base class for cosmetic effect behaviors that provide visual or audio feedback for effects.
    /// CosmeticEffectComponents are attached to CosmeticEffectData and are invoked when effects are applied or removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cosmetic effects can be:
    /// - Shared across all effect applications (RequiresInstance = false)
    /// - Instanced per application (RequiresInstance = true) for independent control
    /// </para>
    /// <para>
    /// Example implementations:
    /// - Particle effects that play when an effect is active
    /// - Audio clips that trigger on effect application
    /// - Visual indicators like status icons or color tints
    /// - Animation triggers
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// public class PoisonVisuals : CosmeticEffectComponent
    /// {
    ///     public override bool RequiresInstance => true;
    ///     public ParticleSystem poisonParticles;
    ///
    ///     public override void OnApplyEffect(GameObject target)
    ///     {
    ///         base.OnApplyEffect(target);
    ///         poisonParticles.Play();
    ///     }
    ///
    ///     public override void OnRemoveEffect(GameObject target)
    ///     {
    ///         base.OnRemoveEffect(target);
    ///         poisonParticles.Stop();
    ///     }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CosmeticEffectData))]
    public abstract class CosmeticEffectComponent : MonoBehaviour
    {
        /// <summary>
        /// If true, this cosmetic effect requires a new instance to be created for each effect application.
        /// If false, the same instance is shared across all applications.
        /// </summary>
        public virtual bool RequiresInstance => false;

        /// <summary>
        /// If true, this component handles its own cleanup (e.g., via delayed destruction or animations).
        /// If false, the GameObject will be destroyed immediately when the effect is removed.
        /// </summary>
        public virtual bool CleansUpSelf => false;

        /// <summary>
        /// Tracks all GameObjects this cosmetic effect has been applied to.
        /// </summary>
        protected readonly List<GameObject> _appliedTargets = new();

        /// <summary>
        /// Called when the associated effect is applied to a target GameObject.
        /// Override this to implement custom behavior (e.g., play particles, show UI).
        /// </summary>
        /// <param name="target">The GameObject the effect was applied to.</param>
        public virtual void OnApplyEffect(GameObject target)
        {
            _appliedTargets.Add(target);
        }

        /// <summary>
        /// Called when the associated effect is removed from a target GameObject.
        /// Only invoked for non-instant effects. Override this to implement cleanup behavior.
        /// </summary>
        /// <param name="target">The GameObject the effect was removed from.</param>
        public virtual void OnRemoveEffect(GameObject target)
        {
            int appliedIndex = _appliedTargets.IndexOf(target);
            if (0 <= appliedIndex)
            {
                _appliedTargets.RemoveAtSwapBack(appliedIndex);
            }
        }

        /// <summary>
        /// Cleanup method that removes the effect from all targets when this component is destroyed.
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (_appliedTargets.Count <= 0)
            {
                return;
            }

            foreach (GameObject appliedTarget in _appliedTargets.ToArray())
            {
                if (appliedTarget == null)
                {
                    continue;
                }

                OnRemoveEffect(appliedTarget);
            }
        }
    }
}
