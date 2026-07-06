// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;

    /// <summary>
    /// Non-generic registry to manage RuntimeSingleton instance clearing.
    /// This class exists to work around Unity 6.3's restriction on
    /// [RuntimeInitializeOnLoadMethod] in generic classes.
    /// </summary>
    internal static class RuntimeSingletonRegistry
    {
        private sealed class RuntimeSingletonRegistration
        {
            internal readonly Type type;
            internal readonly Action clearAction;
            internal readonly Func<UnityEngine.Object> getCachedInstance;
            internal readonly Func<UnityEngine.Object[]> findLiveInstances;

            internal RuntimeSingletonRegistration(
                Type type,
                Action clearAction,
                Func<UnityEngine.Object> getCachedInstance,
                Func<UnityEngine.Object[]> findLiveInstances
            )
            {
                this.type = type;
                this.clearAction = clearAction;
                this.getCachedInstance = getCachedInstance;
                this.findLiveInstances = findLiveInstances;
            }
        }

        private static readonly Dictionary<Type, RuntimeSingletonRegistration> _registrations =
            new();

        /// <summary>
        /// Registers a clear action for a singleton type.
        /// </summary>
        internal static void Register(
            Type type,
            Action clearAction,
            Func<UnityEngine.Object> getCachedInstance,
            Func<UnityEngine.Object[]> findLiveInstances
        )
        {
            if (
                type == null
                || clearAction == null
                || getCachedInstance == null
                || findLiveInstances == null
            )
            {
                return;
            }

            lock (_registrations)
            {
                _registrations[type] = new RuntimeSingletonRegistration(
                    type,
                    clearAction,
                    getCachedInstance,
                    findLiveInstances
                );
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
            RuntimeSingletonRegistration[] registrations;
            lock (_registrations)
            {
                registrations = new RuntimeSingletonRegistration[_registrations.Count];
                _registrations.Values.CopyTo(registrations, 0);
            }

            foreach (RuntimeSingletonRegistration registration in registrations)
            {
                try
                {
                    registration.clearAction.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        internal static string DescribeLiveInstancesForTesting()
        {
            RuntimeSingletonRegistration[] registrations;
            lock (_registrations)
            {
                registrations = new RuntimeSingletonRegistration[_registrations.Count];
                _registrations.Values.CopyTo(registrations, 0);
            }

            StringBuilder builder = null;
            foreach (RuntimeSingletonRegistration registration in registrations)
            {
                UnityEngine.Object cachedInstance = null;
                try
                {
                    cachedInstance = registration.getCachedInstance();
                }
                catch (Exception ex)
                {
                    builder ??= new StringBuilder();
                    builder.Append(registration.type.FullName);
                    builder.Append(" cache inspection failed: ");
                    builder.Append(ex.Message);
                    builder.AppendLine();
                }

                UnityEngine.Object[] liveInstances = Array.Empty<UnityEngine.Object>();
                try
                {
                    liveInstances =
                        registration.findLiveInstances() ?? Array.Empty<UnityEngine.Object>();
                }
                catch (Exception ex)
                {
                    builder ??= new StringBuilder();
                    builder.Append(registration.type.FullName);
                    builder.Append(" live-instance inspection failed: ");
                    builder.Append(ex.Message);
                    builder.AppendLine();
                }

                foreach (UnityEngine.Object liveInstance in liveInstances)
                {
                    if (liveInstance == null)
                    {
                        continue;
                    }

                    builder ??= new StringBuilder();
                    builder.Append(registration.type.FullName);
                    builder.Append(" '");
                    builder.Append(liveInstance.name);
                    builder.Append("'#");
                    builder.Append(liveInstance.GetUnityObjectId());
                    if (liveInstance is Component component && component.gameObject != null)
                    {
                        builder.Append(" scene='");
                        builder.Append(component.gameObject.scene.name);
                        builder.Append("'");
                    }
                    builder.Append(" cached=");
                    builder.Append(cachedInstance == liveInstance);
                    builder.AppendLine();
                }
            }

            return builder?.ToString().Trim();
        }
    }
}
