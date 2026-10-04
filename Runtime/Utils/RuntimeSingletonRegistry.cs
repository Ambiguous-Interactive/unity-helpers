// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// Non-generic registry to manage RuntimeSingleton cache resets and explicit instance clearing.
    /// This class exists to work around Unity 6.3's restriction on
    /// [RuntimeInitializeOnLoadMethod] in generic classes.
    /// </summary>
    internal static class RuntimeSingletonRegistry
    {
        internal static bool IsClearingInstances => _isClearingInstances;

        internal static bool _isApplicationQuitting;

        private static readonly Dictionary<Type, RuntimeSingletonRegistration> _registrations =
            new();

        private static readonly Queue<Action> _pendingClears = new();
        private static readonly HashSet<Action> _clearingActions = new();
        private static bool _isClearingInstances;
#if UNITY_EDITOR
        internal static bool _isEditorQuitting;
#endif

        internal static bool IsApplicationQuitting
        {
            get
            {
#if UNITY_EDITOR
                return _isApplicationQuitting && (Application.isPlaying || _isEditorQuitting);
#else
                return _isApplicationQuitting;
#endif
            }
        }

        static RuntimeSingletonRegistry()
        {
            SubscribeToLifecycleEvents();
        }

        /// <summary>
        /// Registers cache-reset and destructive-clear actions for a singleton type.
        /// </summary>
        internal static void Register(
            Type type,
            Action resetCacheAction,
            Action clearAction,
            Func<UnityEngine.Object> getCachedInstance,
            Func<UnityEngine.Object[]> findLiveInstances
        )
        {
            if (
                type == null
                || resetCacheAction == null
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
                    resetCacheAction,
                    clearAction,
                    getCachedInstance,
                    findLiveInstances
                );
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void OnSubsystemRegistration()
        {
            _isApplicationQuitting = false;
#if UNITY_EDITOR
            _isEditorQuitting = false;
#endif
            SubscribeToLifecycleEvents();
        }

        internal static void OnApplicationQuitting()
        {
            _isApplicationQuitting = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoad()
        {
            _isApplicationQuitting = false;
            ResetAllRegisteredCaches();
        }

        private static void SubscribeToLifecycleEvents()
        {
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
#endif
        }

#if UNITY_EDITOR
        private static void OnEditorQuitting()
        {
            _isEditorQuitting = true;
            _isApplicationQuitting = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (
                state == PlayModeStateChange.EnteredEditMode
                || state == PlayModeStateChange.ExitingEditMode
            )
            {
                _isApplicationQuitting = false;
                _isEditorQuitting = false;
            }
        }
#endif

        /// <summary>
        /// Resets every registered singleton cache without destroying live instances.
        /// </summary>
        internal static void ResetAllRegisteredCaches()
        {
            foreach (RuntimeSingletonRegistration registration in GetRegistrationsSnapshot())
            {
                try
                {
                    registration.resetCacheAction.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        /// <summary>
        /// Clears every registered <see cref="RuntimeSingleton{T}"/> instance.
        /// Available for explicit test, editor, and runtime cleanup.
        /// </summary>
        internal static void ClearAllRegisteredInstances()
        {
            foreach (RuntimeSingletonRegistration registration in GetRegistrationsSnapshot())
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

        internal static void ClearInstances(Action clearAction)
        {
            if (!_clearingActions.Add(clearAction))
            {
                return;
            }

            _pendingClears.Enqueue(clearAction);
            if (_isClearingInstances)
            {
                return;
            }

            _isClearingInstances = true;
            try
            {
                while (_pendingClears.TryDequeue(out Action pendingClear))
                {
                    try
                    {
                        // Nested clears can otherwise destroy an object or its parent while Unity is deactivating it.
                        pendingClear();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                    finally
                    {
                        _clearingActions.Remove(pendingClear);
                    }
                }
            }
            finally
            {
                _isClearingInstances = false;
            }
        }

        internal static RuntimeSingletonRegistration[] GetRegistrationsSnapshot()
        {
            lock (_registrations)
            {
                RuntimeSingletonRegistration[] registrations = new RuntimeSingletonRegistration[
                    _registrations.Count
                ];
                _registrations.Values.CopyTo(registrations, 0);
                return registrations;
            }
        }

        internal sealed class RuntimeSingletonRegistration
        {
            internal readonly Type type;
            internal readonly Action resetCacheAction;
            internal readonly Action clearAction;
            internal readonly Func<UnityEngine.Object> getCachedInstance;
            internal readonly Func<UnityEngine.Object[]> findLiveInstances;

            internal RuntimeSingletonRegistration(
                Type type,
                Action resetCacheAction,
                Action clearAction,
                Func<UnityEngine.Object> getCachedInstance,
                Func<UnityEngine.Object[]> findLiveInstances
            )
            {
                this.type = type;
                this.resetCacheAction = resetCacheAction;
                this.clearAction = clearAction;
                this.getCachedInstance = getCachedInstance;
                this.findLiveInstances = findLiveInstances;
            }
        }
    }
}
