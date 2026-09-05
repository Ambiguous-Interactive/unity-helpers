// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Collections.Generic;
    using Core.Attributes;
    using UnityEngine;

    /// <summary>
    /// Monitors the TagHandler for a specific tag that disables/enables colliders on this GameObject.
    /// When the "CollisionDisabledTag" is applied, all enabled colliders are disabled and tracked.
    /// When the tag is removed, the colliders are re-enabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This component provides a tag-based way to temporarily disable collision detection,
    /// useful for effects like invulnerability, phasing, or ghost mode.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Apply an effect with the CollisionDisabledTag
    /// AttributeEffect ghostMode = ...;
    /// ghostMode.effectTags.Add(CollisionSenses.CollisionDisabledTag);
    /// gameObject.ApplyEffect(ghostMode);
    ///
    /// // Colliders are now disabled
    /// // When the effect expires, colliders are automatically re-enabled
    /// </code>
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TagHandler))]
    public sealed class CollisionSenses : MonoBehaviour
    {
        /// <summary>
        /// The tag name that triggers collision disable/enable behavior.
        /// </summary>
        public const string CollisionDisabledTag = nameof(CollisionDisabledTag);

        [SiblingComponent]
#pragma warning disable CS0649
        private TagHandler _tagHandler;
#pragma warning restore CS0649

        private readonly List<Collider2D> _managedColliders = new();

        private void Awake()
        {
            this.AssignRelationalComponents();
        }

        private void OnEnable()
        {
            if (_tagHandler.HasTag(CollisionDisabledTag))
            {
                StartManagingColliders();
            }

            _tagHandler.OnTagAdded += CheckForTagAddition;
            _tagHandler.OnTagRemoved += CheckForTagRemoval;
        }

        private void OnDisable()
        {
            _tagHandler.OnTagAdded -= CheckForTagAddition;
            _tagHandler.OnTagRemoved -= CheckForTagRemoval;
            StopManagingColliders();
        }

        private void CheckForTagAddition(string addedTag)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (string.Equals(addedTag, CollisionDisabledTag, StringComparison.Ordinal))
            {
                StartManagingColliders();
            }
        }

        private void CheckForTagRemoval(string removedTag)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (string.Equals(removedTag, CollisionDisabledTag, StringComparison.Ordinal))
            {
                StopManagingColliders();
            }
        }

        private void StopManagingColliders()
        {
            foreach (Collider2D managedCollider in _managedColliders)
            {
                if (managedCollider != null)
                {
                    managedCollider.enabled = true;
                }
            }

            _managedColliders.Clear();
        }

        private void StartManagingColliders()
        {
            GetComponentsInChildren(_managedColliders);
            _managedColliders.RemoveAll(managed => !managed.enabled);
            foreach (Collider2D managedCollider in _managedColliders)
            {
                managedCollider.enabled = false;
            }
        }
    }
}
