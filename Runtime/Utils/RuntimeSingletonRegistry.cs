// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Non-generic registry to manage RuntimeSingleton instance clearing.
    /// This class exists to work around Unity 6.3's restriction on
    /// [RuntimeInitializeOnLoadMethod] in generic classes.
    /// </summary>
    internal static class RuntimeSingletonRegistry
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoad()
        {
            ClearAllRegisteredInstances();
        }

        /// <summary>
        /// Clears every registered <see cref="RuntimeSingleton{T}"/> instance.
        /// Invoked automatically before scene load and available for manual editor/runtime resets.
        /// </summary>
        internal static void ClearAllRegisteredInstances()
        {
            lock (_clearActions)
            {
                foreach (Action clearAction in _clearActions)
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
}
