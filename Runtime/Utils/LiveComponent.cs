// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using UnityEngine;

    /// <summary>Registers a component while enabled with its closed LiveComponents registry.</summary>
    /// <typeparam name="T">The concrete component type deriving from this base.</typeparam>
    /// <remarks>
    /// Overrides of these lifecycle methods must call their base implementation.
    /// This base does not discover inactive objects or register before Unity invokes OnEnable.
    /// </remarks>
    public abstract class LiveComponent<T> : MonoBehaviour
        where T : LiveComponent<T>
    {
        /// <summary>Adds this component to its registry when Unity enables it.</summary>
        protected virtual void OnEnable()
        {
            if (this is T component)
            {
                LiveComponents<T>.Register(component);
            }
        }

        /// <summary>Removes this component from its registry when Unity disables it.</summary>
        protected virtual void OnDisable()
        {
            if (this is T component)
            {
                LiveComponents<T>.Unregister(component);
            }
        }

        /// <summary>Removes this component from its registry when Unity destroys it.</summary>
        protected virtual void OnDestroy()
        {
            if (this is T component)
            {
                LiveComponents<T>.Unregister(component);
            }
        }
    }
}
