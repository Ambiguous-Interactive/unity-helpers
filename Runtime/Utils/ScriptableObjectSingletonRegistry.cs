// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Non-generic registry to manage ScriptableObjectSingleton instance clearing.
    /// This class exists to work around Unity 6.3's restriction on
    /// [RuntimeInitializeOnLoadMethod] in generic classes.
    /// </summary>
    internal static class ScriptableObjectSingletonRegistry
    {
        private static readonly HashSet<Action> _clearActions = new();

        /// <summary>
        /// Registers a clear action for a singleton type.
        /// </summary>
        internal static void Register(Action clearAction)
        {
            if (clearAction == null)
            {
                return;
            }

            lock (_clearActions)
            {
                _clearActions.Add(clearAction);
            }
        }

        /// <summary>
        /// Removes a previously registered clear action.
        /// </summary>
        /// <param name="clearAction">The action to remove.</param>
        /// <returns><c>true</c> when the action was registered; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// The registry is process-wide and every entry is invoked on every scene load, so anything
        /// that registers for a bounded lifetime -- a test, a tool that installs a singleton and
        /// tears it down again -- must be able to leave. Without this, such a caller silently adds
        /// work to every later load for the life of the domain.
        /// </remarks>
        internal static bool Unregister(Action clearAction)
        {
            if (clearAction == null)
            {
                return false;
            }

            lock (_clearActions)
            {
                return _clearActions.Remove(clearAction);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void ClearAllInstances()
        {
            /*
                Snapshot, and invoke outside the lock. Each action is a
                ScriptableObjectSingleton<T>.ClearInstance, which calls the consumer's
                OnInstanceCleared; touching another singleton type for the first time there runs its
                static constructor, which re-enters Register on this same re-entrant monitor and
                adds to the set being enumerated. The MoveNext that then throws is raised by the
                foreach, OUTSIDE the per-action catch below, so it escapes a
                [RuntimeInitializeOnLoadMethod] and every later singleton goes uncleaned.
                RuntimeSingletonRegistry has always copied first, for the same reason.
            */
            Action[] snapshot;
            lock (_clearActions)
            {
                snapshot = new Action[_clearActions.Count];
                _clearActions.CopyTo(snapshot, 0);
            }

            foreach (Action clearAction in snapshot)
            {
                try
                {
                    clearAction.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
}
