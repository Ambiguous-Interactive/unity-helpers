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
    ///   detaches and calls <see cref="Object.DontDestroyOnLoad(Object)"/> to persist across scene loads.
    /// - In <see cref="Start"/>, detects duplicate instances and destroys the newer one.
    /// - Instance cache is cleared on domain reload before scene load via <see cref="RuntimeSingletonRegistry"/>.
    /// - Call <see cref="ClearInstance"/> to manually drop a stale reference in editor tooling or at runtime.
    ///
    /// Odin compatibility: this runtime type derives from Odin's serialized base when the
    /// <c>odininspector</c> package is installed; otherwise it falls back to <see cref="MonoBehaviour"/>.
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

        private static bool _creationRefused;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static bool _creationRefusedWarningLogged;
#endif

        static RuntimeSingleton()
        {
            RuntimeSingletonRegistry.Register(
                typeof(T),
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

            // An unrecognized value is treated as the default rather than as a refusal: a policy this
            // build does not know about must not silently stop a singleton from existing.
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
        /// <see cref="CreationPolicy"/> is <see cref="SingletonCreationPolicy.NeverCreate"/>.
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

                // A refusal is remembered, because a scene-wide search that already came back empty
                // cannot start returning something without Awake running -- and Awake assigns
                // _instance, which is answered above. Without this a NeverCreate singleton with
                // nothing in the scene pays a full FindAnyObjectByType on EVERY access, forever,
                // which is exactly what the documented `if (X.Instance != null)` guard does per
                // frame. CreateOnDemand never had the problem: it creates one and caches it.
                //
                // Play mode only, and that is not caution -- the invariant is Awake, and Unity does
                // not run Awake outside play mode. Measured: AddComponent in the editor produces a
                // live, active instance with _instance still null, so remembering a refusal there
                // would hide a singleton that is genuinely in the scene from editor tooling.
                bool canRememberRefusal = Application.isPlaying;
                if (canRememberRefusal && _creationRefused)
                {
                    return null;
                }

                _instance = FindAnyObjectByType<T>(FindObjectsInactive.Exclude);
                if (_instance != null)
                {
                    return _instance;
                }

                Type type = typeof(T);
                if (_creationPolicy == SingletonCreationPolicy.NeverCreate)
                {
                    _creationRefused = canRememberRefusal;
                    WarnCreationRefused(type);
                    return null;
                }

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
        ///
        /// <b>There is no fresh instance for a <see cref="SingletonCreationPolicy.NeverCreate"/>
        /// singleton.</b> This destroys every live instance, including one authored in a scene, and
        /// such a type will not build a replacement -- <see cref="Instance"/> returns <c>null</c>
        /// until something else creates one. Clearing those is for tests and for editor tooling that
        /// is about to reload the scene, not for ordinary runtime resets.
        /// </remarks>
        public static void ClearInstance()
        {
            // Sweep EVERY live instance of T, not just the cached _instance. The singleton spawns
            // on the fly the moment anything touches Instance (e.g. a logger/dispatcher access from
            // a leaked coroutine), and Start()'s duplicate-destroy is deferred, so a PlayMode test
            // can leave behind extra or inactive instances that _instance no longer points at. Those
            // survivors keep HasInstance/activeInHierarchy lying and pollute later tests. Use the
            // version-safe shim (FindObjectsByType is 2022.2+) and include inactive so a deactivated
            // survivor is caught. StopAllCoroutines before the (deferred) destroy so a coroutine
            // hosted on the singleton cannot tick once more and log/throw past the test boundary.
            T[] liveInstances = UnityObjectExtensions.FindObjectsOfTypeShim<T>(true);
            for (int i = 0; i < liveInstances.Length; i++)
            {
                T inst = liveInstances[i];
                if (inst == null)
                {
                    continue;
                }

                inst.StopAllCoroutines();
                // Destroy the whole GameObject, not just the component: the Instance getter creates a
                // dedicated "<Type>-Singleton" GameObject and Start()'s duplicate-detection path also
                // destroys the GameObject. Destroying only the component leaked an empty GameObject
                // (into DontDestroyOnLoad for Preserve singletons), which both failed
                // ClearInstanceDestroysGameObjectAndClearsReference and polluted later PlayMode tests.
                inst.gameObject.Destroy();
            }

            Interlocked.Exchange(ref _initializeCount, 0);
            _instance = null;
            // An explicit clear is exactly the moment the remembered refusal should end: the next
            // access is a fresh question, and a test that expects the warning twice would otherwise
            // never see it a second time.
            _creationRefused = false;
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
