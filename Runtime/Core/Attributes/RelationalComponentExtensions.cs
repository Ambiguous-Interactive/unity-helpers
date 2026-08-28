// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Extension methods for automatically assigning components based on relational attributes.
    /// </summary>
    public static class RelationalComponentExtensions
    {
        /// <summary>
        /// Assigns all relational components (parent, sibling, and child components) to fields marked with the corresponding attributes.
        /// </summary>
        /// <param name="component">The component on which to perform the assignment.</param>
        /// <remarks>
        /// This is a convenience method that calls <see cref="ParentComponentExtensions.AssignParentComponents(UnityEngine.Component)"/>,
        /// <see cref="SiblingComponentExtensions.AssignSiblingComponents(UnityEngine.Component)"/>, and <see cref="ChildComponentExtensions.AssignChildComponents(UnityEngine.Component)"/>
        /// in sequence.
        ///
        /// Fields must be marked with <see cref="ParentComponentAttribute"/>, <see cref="SiblingComponentAttribute"/>, or
        /// <see cref="ChildComponentAttribute"/> for automatic assignment.
        ///
        /// Call from <c>Awake()</c> or <c>OnEnable()</c> so dependent code has references ready.
        ///
        /// To avoid any first-use overhead from generating reflection helpers lazily, you can explicitly
        /// pre-initialize all relational component reflection caches using
        /// <see cref="RelationalComponentInitializer.Initialize(System.Collections.Generic.IEnumerable{System.Type}, bool)"/>.
        /// Consider calling it during a loading/bootstrap phase.
        /// Null handling: If the component is null, this will cause a <see cref="System.NullReferenceException"/>.
        /// Thread-safety: Not thread-safe; Unity component access must occur on the main thread.
        /// Performance: O(n*m) where n is the number of attributed fields and m is the search space for each component.
        /// </remarks>
        /// <seealso cref="ParentComponentAttribute"/>
        /// <seealso cref="SiblingComponentAttribute"/>
        /// <seealso cref="ChildComponentAttribute"/>
        /// <example>
        /// <code><![CDATA[
        /// using UnityEngine;
        /// using WallstopStudios.UnityHelpers.Core.Attributes;
        ///
        /// public class Player : MonoBehaviour
        /// {
        ///     [SiblingComponent] private SpriteRenderer sprite;
        ///     [ParentComponent(OnlyAncestors = true)] private Transform parentTransform;
        ///     [ChildComponent(OnlyDescendants = true, MaxDepth = 1)] private Collider2D[] immediateChildColliders;
        ///
        ///     private void Awake()
        ///     {
        ///         // Wires up all fields above in one call
        ///         this.AssignRelationalComponents();
        ///     }
        /// }
        /// ]]></code>
        /// </example>
        public static void AssignRelationalComponents(this Component component)
        {
            if (component == null)
            {
                return;
            }

            Type componentType = component.GetType();
            ParentComponentExtensions.AssignParentComponents(
                component,
                ParentComponentExtensions.GetOrCreateFields(componentType)
            );
            SiblingComponentExtensions.AssignSiblingComponents(
                component,
                SiblingComponentExtensions.GetOrCreateFields(componentType)
            );
            ChildComponentExtensions.AssignChildComponents(
                component,
                ChildComponentExtensions.GetOrCreateFields(componentType)
            );
        }
    }
}
