// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Threading;
    using Core.Attributes;
    using Core.Extension;
    using Core.Helper;
    using UnityEngine;
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using RuntimeSingletonBase = Sirenix.OdinInspector.SerializedMonoBehaviour;
#else
    using RuntimeSingletonBase = UnityEngine.MonoBehaviour;
#endif

    /// <summary>
    /// Provides a simple, robust runtime singleton pattern for components.
    /// Ensures there is at most one active instance of <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// Access the global instance via <see cref="Instance"/>; if no active instance exists,
    /// a new <see cref="GameObject"/> named "&lt;Type&gt;-Singleton" is created and the component is added.
    /// Annotate the type with
    /// <see cref="WallstopStudios.UnityHelpers.Core.Attributes.SingletonCreationAttribute"/> and
    /// <see cref="SingletonCreationPolicy.NeverCreate"/> to keep that from happening, which is what a
    /// singleton holding authored <c>[SerializeField]</c> state wants.
    ///
    /// Lifecycle:
    /// - On first access, searches for an active instance; otherwise creates one.
    /// - In <see cref="Awake"/>, sets the static instance and, when <see cref="Preserve"/> is true and in play mode,
    ///   detaches and calls <see cref="UnityEngine.Object.DontDestroyOnLoad(UnityEngine.Object)"/> to persist across scene loads.
    /// - In <see cref="Start"/>, detects duplicate instances and destroys the newer one.
    /// - Instance cache is cleared before scene load via <see cref="RuntimeSingletonRegistry"/>
    ///   without destroying live scene-authored instances.
    /// - Call <see cref="ClearInstance"/> to manually drop a stale reference in editor tooling or at runtime.
    ///
    /// Odin compatibility: this runtime type derives from Odin's serialized base when the
    /// <c>odininspector</c> package is installed; otherwise it falls back to <see cref="MonoBehaviour"/>.
    /// A component whose runtime type does not implement the requested closed generic singleton type
    /// is rejected during <see cref="Awake"/> and is never stored in that singleton's static cache.
    /// </remarks>
    /// <typeparam name="T">Concrete singleton component type that derives from this base.</typeparam>
    [DisallowMultipleComponent]
    public abstract class RuntimeSingleton<T> : RuntimeSingletonBase
        where T : RuntimeSingleton<T>
    {
        /// <summary>
        /// Gets a value indicating whether an instance is currently assigned.
        /// </summary>
        public static bool HasInstance => _instance != null;

        public static long InitializeCount => Interlocked.Read(ref _initializeCount);

        /// <summary>
        /// Gets what <see cref="Instance"/> does when no instance exists, taken from the
        /// <see cref="SingletonCreationAttribute"/> on <typeparamref name="T"/> and
        /// <see cref="SingletonCreationPolicy.CreateOnDemand"/> when there is none.
        /// </summary>
        public static SingletonCreationPolicy CreationPolicy => _creationPolicy;

        protected static long _initializeCount;

        protected internal static T _instance;

        private static readonly SingletonCreationPolicy _creationPolicy = ResolveCreationPolicy();

        // -1 rather than 0, because frame 0 is a real frame.
        private static int _creationRefusedFrame = -1;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static bool _creationRefusedWarningLogged;
#endif

        static RuntimeSingleton()
        {
            RuntimeSingletonRegistry.Register(
                typeof(T),
                ResetCachedInstance,
                ClearInstance,
                () => _instance,
                () => Resources.FindObjectsOfTypeAll<T>()
            );
        }

        private static SingletonCreationPolicy ResolveCreationPolicy()
        {
            if (
                !ReflectionHelpers.TryGetAttributeSafe(
                    typeof(T),
                    out SingletonCreationAttribute attribute,
                    inherit: true
                )
            )
            {
                return SingletonCreationPolicy.CreateOnDemand;
            }

            // Unknown policies use the default so newer serialized values cannot suppress singleton creation.
            return attribute.Policy == SingletonCreationPolicy.NeverCreate
                ? SingletonCreationPolicy.NeverCreate
                : SingletonCreationPolicy.CreateOnDemand;
        }

        /// <summary>
        /// Gets a value that controls whether the instance persists across scene loads.
        /// Defaults to <c>true</c>. Override and return <c>false</c> to keep the instance
        /// scene‑local.
        /// </summary>
        protected virtual bool Preserve => true;

        protected virtual bool LogErrorOnDestruction => true;

        /// <summary>
        /// Gets the global instance, creating one if needed. Returns <c>null</c> when none exists and
        /// <see cref="CreationPolicy"/> is <see cref="SingletonCreationPolicy.NeverCreate"/>, or
        /// when Unity has begun application shutdown.
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

                // Cache misses for one frame only; editor additions and overrides may bypass Awake registration.
                int frame = Time.frameCount;
                if (_creationRefusedFrame == frame)
                {
                    return null;
                }

                _instance = FindAnyObjectByType<T>(FindObjectsInactive.Exclude);
                if (_instance != null)
                {
                    return _instance;
                }

                if (RuntimeSingletonRegistry.IsApplicationQuitting)
                {
                    return null;
                }

                Type type = typeof(T);
                if (_creationPolicy == SingletonCreationPolicy.NeverCreate)
                {
                    _creationRefusedFrame = frame;
                    WarnCreationRefused(type);
                    return null;
                }

                GameObject instance = new($"{type.Name}-Singleton", type);
                if (_instance == null && !instance.TryGetComponent(out _instance))
                {
                    // Discard a GameObject whose behaviour failed to attach rather than caching an inert singleton.
                    instance.Destroy();
                    return null;
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
        /// instance on the next <see cref="Instance"/> access. Automatic startup cache resets are
        /// handled by <see cref="RuntimeSingletonRegistry"/> because Unity disallows
        /// <c>[RuntimeInitializeOnLoadMethod]</c> on methods in generic classes.
        ///
        /// <b>There is no fresh instance for a <see cref="SingletonCreationPolicy.NeverCreate"/>
        /// singleton.</b> This destroys every live instance, including one authored in a scene, and
        /// such a type will not build a replacement -- <see cref="Instance"/> returns <c>null</c>
        /// until something else creates one. Clearing those is for tests and for editor tooling that
        /// is about to reload the scene, not for ordinary runtime resets.
        /// </remarks>
        public static void ClearInstance()
        {
            // Include inactive and uncached duplicates; stop coroutines before deferred destruction can leak another tick.
            T[] liveInstances = UnityObjectExtensions.FindObjectsOfTypeShim<T>(true);
            foreach (T inst in liveInstances)
            {
                if (inst == null)
                {
                    continue;
                }

                inst.StopAllCoroutines();
                // Singleton creation owns the whole GameObject, so destroying only the component would leak it.
                inst.gameObject.Destroy();
            }

            ResetCachedInstance();
        }

        private static void ResetCachedInstance()
        {
            Interlocked.Exchange(ref _initializeCount, 0);
            _instance = null;
            // A cache reset must also clear remembered misses.
            _creationRefusedFrame = -1;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _creationRefusedWarningLogged = false;
#endif
        }

        private static void WarnCreationRefused(Type type)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_creationRefusedWarningLogged)
            {
                return;
            }

            _creationRefusedWarningLogged = true;
            Debug.LogWarning(
                $"RuntimeSingleton could not locate an active instance of {type.FullName}, and "
                    + $"{nameof(SingletonCreationPolicy)}.{nameof(SingletonCreationPolicy.NeverCreate)} forbids creating one. Returning null."
            );
#else
            _ = type;
#endif
        }

        protected virtual void Awake()
        {
            if (!(this is T instance))
            {
                Debug.LogError(
                    $"{GetType().FullName} derives from {typeof(RuntimeSingleton<T>).FullName} but is not assignable to {typeof(T).FullName}. Use RuntimeSingleton<{GetType().Name}> so the singleton cache cannot hold the wrong runtime type."
                );
                return;
            }

            Interlocked.Increment(ref _initializeCount);
            this.AssignRelationalComponents();
            if (_instance == null)
            {
                _instance = instance;
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
