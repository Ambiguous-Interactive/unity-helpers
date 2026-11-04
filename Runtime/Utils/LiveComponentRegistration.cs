// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Registers components on this GameObject without changing their bases or callbacks.</summary>
    /// <remarks>
    /// Add this component directly, or require it with Unity's RequireComponent attribute.
    /// Components are indexed by their exact runtime type. Queries finish pending enable-time registration.
    /// Call Refresh after adding or removing components at runtime, or after a reset without another OnEnable.
    /// Disabling this adapter removes only its registrations; manual registrations remain independent.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Wallstop Studios/Unity Helpers/Live Component Registration")]
    public sealed class LiveComponentRegistration : MonoBehaviour
    {
        private readonly List<Component> _registered = new();
        private readonly List<Component> _current = new();
        private bool _scheduled;

        private static bool ContainsReference(List<Component> components, Component target)
        {
            foreach (Component component in components)
            {
                if (ReferenceEquals(component, target))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Refreshes registrations from the components currently attached to this GameObject.</summary>
        /// <remarks>Requires Unity's initialized main thread and an enabled adapter; worker calls are ignored.</remarks>
        [ContextMenu(nameof(Refresh))]
        public void Refresh()
        {
            if (!LiveComponentRegistry.CanAccess)
            {
                return;
            }
            LiveComponentRegistry.Cancel(this);
            _scheduled = false;
            ApplyRegistrations();
        }

        internal void CancelPendingRefresh()
        {
            _scheduled = false;
        }

        internal void ApplyPendingRegistration()
        {
            _scheduled = false;
            ApplyRegistrations();
        }

        private void OnEnable()
        {
            if (!_scheduled)
            {
                _scheduled = true;
                LiveComponentRegistry.Schedule(this);
            }
        }

        private void Start()
        {
            Refresh();
        }

        private void OnDisable()
        {
            _scheduled = false;
            LiveComponentRegistry.Cancel(this);
            foreach (Component component in _registered)
            {
                LiveComponentRegistry.UnregisterAdapter(component, this);
            }
            _registered.Clear();
            _current.Clear();
        }

        private void OnDestroy()
        {
            OnDisable();
        }

        private void ApplyRegistrations()
        {
            if (!LiveComponentRegistry.CanAccess || !isActiveAndEnabled)
            {
                return;
            }
            _current.Clear();
            GetComponents(_current);
            foreach (Component component in _registered)
            {
                if (!ContainsReference(_current, component))
                {
                    LiveComponentRegistry.UnregisterAdapter(component, this);
                }
            }
            foreach (Component component in _current)
            {
                LiveComponentRegistry.RegisterAdapter(component, this);
            }
            _registered.Clear();
            _registered.AddRange(_current);
        }
    }
}
