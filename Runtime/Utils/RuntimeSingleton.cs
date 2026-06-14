// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using Core.Attributes;
    using Core.Extension;
    using Core.Helper;
    using UnityEngine;
#if ODIN_INSPECTOR
    using Sirenix.OdinInspector;
#endif

    /// <summary>
    /// Provides a simple, robust runtime singleton pattern for components.
    /// Ensures there is at most one active instance of <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// Access the global instance via <see cref="Instance"/>; if no active instance exists,
    /// a new <see cref="GameObject"/> named "&lt;Type&gt;-Singleton" is created and the component is added.
    ///
    /// Lifecycle:
    /// - On first access, searches for an active instance; otherwise creates one.
    /// - In <see cref="Awake"/>, sets the static instance and, when <see cref="Preserve"/> is true and in play mode,
    ///   detaches and calls <see cref="Object.DontDestroyOnLoad(Object)"/> to persist across scene loads.
    /// - In <see cref="Start"/>, detects duplicate instances and destroys the newer one.
    /// - Instance cache is cleared on domain reload before scene load via <see cref="RuntimeSingletonRegistry"/>.
    /// - Call <see cref="ClearInstance"/> to manually drop a stale reference in editor tooling or at runtime.
    ///
    /// ODIN compatibility: When the <c>ODIN_INSPECTOR</c> symbol is defined, this class derives from
    /// <c>Sirenix.OdinInspector.SerializedMonoBehaviour</c> for richer serialization; otherwise it derives from
    /// <see cref="MonoBehaviour"/>.
    /// </remarks>
    /// <typeparam name="T">Concrete singleton component type that derives from this base.</typeparam>
    [DisallowMultipleComponent]
    public abstract class RuntimeSingleton<T> :
#if ODIN_INSPECTOR
        SerializedMonoBehaviour
#else
        MonoBehaviour
#endif
        where T : RuntimeSingleton<T>
    {
        /// <summary>
        /// Gets a value indicating whether an instance is currently assigned.
        /// </summary>
        public static bool HasInstance => _instance != null;

        public static long InitializeCount => Interlocked.Read(ref _initializeCount);

        protected static long _initializeCount;

        protected internal static T _instance;

        static RuntimeSingleton()
        {
            RuntimeSingletonRegistry.Register(ClearInstance);
        }

        /// <summary>
        /// Gets a value that controls whether the instance persists across scene loads.
        /// Defaults to <c>true</c>. Override and return <c>false</c> to keep the instance
        /// scene‑local.
        /// </summary>
        protected virtual bool Preserve => true;

        protected virtual bool LogErrorOnDestruction => true;

        /// <summary>
        /// Gets the global instance, creating one if needed.
        /// </summary>
        /// <example>
        /// <code>
        /// public sealed class GameServices : RuntimeSingleton&lt;GameServices&gt;
        /// {
        ///     protected override bool Preserve =&gt; false; // stay scene‑local
        ///     public void Log(string msg) =&gt; Debug.Log(msg);
        /// }
        ///
        /// // Usage from anywhere
        /// GameServices.Instance.Log("Hello");
        /// </code>
        /// </example>
        public static T Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                UnityMainThreadGuard.EnsureMainThread();

                _instance = FindAnyObjectByType<T>(FindObjectsInactive.Exclude);
                if (_instance != null)
                {
                    return _instance;
                }

                Type type = typeof(T);
                GameObject instance = new($"{type.Name}-Singleton", type);
                if (_instance == null)
                {
                    _ = instance.TryGetComponent(out _instance);
                }

                return _instance;
            }
        }

        /// <summary>
        /// Clears the cached singleton instance, destroying its <see cref="GameObject"/> when present.
        /// Safe to call unconditionally; no-op when <see cref="HasInstance"/> is false.
        /// </summary>
        /// <remarks>
        /// Use when editor tooling or runtime code needs to drop a stale reference and force a fresh
        /// instance on the next <see cref="Instance"/> access. Automatic clearing on domain reload is
        /// handled by <see cref="RuntimeSingletonRegistry"/> because Unity disallows
        /// <c>[RuntimeInitializeOnLoadMethod]</c> on methods in generic classes.
        /// </remarks>
        public static void ClearInstance()
        {
            if (_instance == null)
            {
                return;
            }

            _instance.Destroy();
            Interlocked.Exchange(ref _initializeCount, 0);
            _instance = null;
        }

        protected virtual void Awake()
        {
            Interlocked.Increment(ref _initializeCount);
            this.AssignRelationalComponents();
            if (_instance == null)
            {
                _instance = Unsafe.As<T>(this);
            }

            if (Preserve && Application.isPlaying)
            {
                transform.SetParent(null, worldPositionStays: false);
                DontDestroyOnLoad(gameObject);
            }
        }

        protected virtual void Start()
        {
            if (_instance == null || _instance == this)
            {
                return;
            }

            string duplicateMessage =
                $"Double singleton detected, {_instance.name} conflicts with {name}. Total initialize count: {InitializeCount}.";

            if (LogErrorOnDestruction)
            {
                Debug.LogError(duplicateMessage);
            }
            else
            {
                Debug.Log(duplicateMessage);
            }

            gameObject.Destroy();
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        protected virtual void OnApplicationQuit() { }
    }
}
