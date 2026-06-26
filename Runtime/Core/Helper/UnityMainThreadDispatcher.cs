// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Concurrent;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;
    using Utils;
    using WallstopStudios.UnityHelpers.Core.Extension;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// Thread-safe dispatcher that enqueues work to run on Unity's main thread.
    /// </summary>
    /// <remarks>
    /// Works in both edit mode and play mode. Use for marshalling callbacks from tasks/threads to main thread.
    /// </remarks>
    [ExecuteAlways]
    public sealed class UnityMainThreadDispatcher : RuntimeSingleton<UnityMainThreadDispatcher>
    {
        private readonly ConcurrentQueue<Action> _actions = new();
        private const int DefaultQueueLimit = 4096;
        private int _pendingActionCount;
        private int _lastOverflowFrame = -1;
#if UNITY_EDITOR
        private const HideFlags EditorDispatcherHideFlags = HideFlags.None;
#endif

        [SerializeField]
        [Tooltip(
            "Maximum number of queued actions before new submissions are dropped. Set to 0 for unlimited."
        )]
        private int maxPendingActions = DefaultQueueLimit;

        protected override bool Preserve => false;

        protected override bool LogErrorOnDestruction => false;

        internal static bool AutoCreationEnabled { get; private set; } = false;

        /// <summary>
        /// Gets the number of actions currently waiting to be executed on the main thread.
        /// </summary>
        public int PendingActionCount => Volatile.Read(ref _pendingActionCount);

        /// <summary>
        /// Gets or sets the maximum number of queued actions allowed before new submissions are dropped.
        /// A value of 0 disables the limit.
        /// </summary>
        public int PendingActionLimit
        {
            get => maxPendingActions;
            set => maxPendingActions = Mathf.Max(0, value);
        }

        internal static bool TryGetInstance(out UnityMainThreadDispatcher dispatcher)
        {
            dispatcher = _instance;
            return dispatcher != null;
        }

        internal static bool TryDispatchToMainThread(Action action)
        {
            if (action == null)
            {
                return false;
            }

            UnityMainThreadDispatcher dispatcher = _instance;
            if (dispatcher == null)
            {
                return false;
            }

            dispatcher.RunOnMainThread(action);
            return true;
        }

        internal static int GetLiveDispatcherCount()
        {
            return Resources.FindObjectsOfTypeAll<UnityMainThreadDispatcher>().Length;
        }

        internal static int GetPendingActionCountForTesting()
        {
            int pendingActionCount = 0;
            UnityMainThreadDispatcher[] dispatchers =
                Resources.FindObjectsOfTypeAll<UnityMainThreadDispatcher>();
            foreach (UnityMainThreadDispatcher dispatcher in dispatchers)
            {
                if (dispatcher == null)
                {
                    continue;
                }

                pendingActionCount += dispatcher.PendingActionCount;
            }

            return pendingActionCount;
        }

        internal static int DrainPendingActionsForTesting(int maxActions = 1024)
        {
            int remainingActionBudget = Math.Max(1, maxActions);
            UnityMainThreadDispatcher[] dispatchers =
                Resources.FindObjectsOfTypeAll<UnityMainThreadDispatcher>();
            foreach (UnityMainThreadDispatcher dispatcher in dispatchers)
            {
                if (dispatcher == null || dispatcher.PendingActionCount <= 0)
                {
                    continue;
                }

                while (
                    remainingActionBudget > 0 && dispatcher._actions.TryDequeue(out Action action)
                )
                {
                    remainingActionBudget--;
                    dispatcher.ExecuteQueuedAction(action);
                }
            }

            return GetPendingActionCountForTesting();
        }

        internal static string DescribeLiveDispatchersForTesting()
        {
            UnityMainThreadDispatcher[] dispatchers =
                Resources.FindObjectsOfTypeAll<UnityMainThreadDispatcher>();
            if (dispatchers.Length == 0)
            {
                return "No dispatchers found.";
            }

            string[] descriptions = new string[dispatchers.Length];
            for (int i = 0; i < dispatchers.Length; i++)
            {
                UnityMainThreadDispatcher dispatcher = dispatchers[i];
                if (dispatcher == null)
                {
                    descriptions[i] = "null";
                    continue;
                }

                GameObject dispatcherObject = dispatcher.gameObject;
                string sceneName = dispatcherObject == null ? "null" : dispatcherObject.scene.name;
                descriptions[i] =
                    $"{dispatcher.name}#{dispatcher.GetUnityObjectId()} scene='{sceneName}' "
                    + $"active={dispatcherObject != null && dispatcherObject.activeInHierarchy} "
                    + $"pending={dispatcher.PendingActionCount}";
            }

            return string.Join(", ", descriptions);
        }

#if UNITY_EDITOR
        private readonly EditorApplication.CallbackFunction _update;
        private bool _attachedEditorUpdate;
#endif

        public UnityMainThreadDispatcher()
        {
#if UNITY_EDITOR
            _update = Update;
#endif
        }

        /// <summary>
        /// Gets the singleton dispatcher, creating it automatically when auto-creation is enabled.
        /// When auto-creation is disabled (for example inside tests) this simply returns the existing instance, which may be <c>null</c>.
        /// </summary>
        /// <example>
        /// <code>
        /// UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
        /// if (dispatcher != null)
        /// {
        ///     dispatcher.RunOnMainThread(() => Debug.Log("Marshalled to main thread"));
        /// }
        /// </code>
        /// </example>
        public static new UnityMainThreadDispatcher Instance
        {
            get
            {
                if (!AutoCreationEnabled)
                {
                    return _instance;
                }

                return RuntimeSingleton<UnityMainThreadDispatcher>.Instance;
            }
        }

        internal static void SetAutoCreationEnabled(bool enabled)
        {
            AutoCreationEnabled = enabled;
        }

        internal static bool DestroyExistingDispatcher(bool immediate)
        {
            bool destroyed = false;

            if (TryGetInstance(out UnityMainThreadDispatcher dispatcher) && dispatcher != null)
            {
                destroyed |= DestroyDispatcherObject(dispatcher, immediate);
            }

            UnityMainThreadDispatcher[] allDispatchers =
                Resources.FindObjectsOfTypeAll<UnityMainThreadDispatcher>();
            if (allDispatchers is { Length: > 0 })
            {
                foreach (UnityMainThreadDispatcher localDispatcher in allDispatchers)
                {
                    destroyed |= DestroyDispatcherObject(localDispatcher, immediate);
                }
            }

            return destroyed;
        }

        private static bool DestroyDispatcherObject(
            UnityMainThreadDispatcher dispatcher,
            bool immediate
        )
        {
            if (dispatcher == null)
            {
                return false;
            }

            GameObject dispatcherObject = dispatcher.gameObject;
            if (dispatcherObject == null)
            {
                return false;
            }

            if (_instance == dispatcher)
            {
                _instance = null;
            }

            if (immediate || !Application.isPlaying)
            {
                // Tests rely on immediate destruction to stay deterministic. Production code
                // should prefer deferred destruction by passing immediate: false.
                DestroyImmediate(dispatcherObject);
                return true;
            }

            Destroy(dispatcherObject);

            return true;
        }

        /// <summary>
        /// Disposable helper that temporarily overrides <see cref="AutoCreationEnabled"/> and (optionally) destroys dispatcher instances on enter/exit.
        /// Use this to keep tests and integration setups deterministic without hand-written <c>try/finally</c> blocks.
        /// </summary>
        /// <remarks>
        /// The scope records the previous <see cref="AutoCreationEnabled"/> value, switches to the desired state, and restores the original value on dispose.
        /// It also exposes knobs for destroying existing dispatcher GameObjects immediately (ideal for EditMode tests) or on dispose.
        /// </remarks>
        /// <example>
        /// <code>
        /// using UnityMainThreadDispatcher.AutoCreationScope scope =
        ///     UnityMainThreadDispatcher.AutoCreationScope.Disabled(
        ///         destroyExistingInstanceOnEnter: true,
        ///         destroyInstancesOnDispose: true,
        ///         destroyImmediate: true);
        ///
        /// // Inside the scope auto-creation is off, so tests can create/destroy the dispatcher manually.
        /// UnityMainThreadDispatcher.SetAutoCreationEnabled(true);
        /// UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
        /// </code>
        /// </example>
        public sealed class AutoCreationScope : IDisposable
        {
            private readonly bool _previousState;
            private readonly bool _destroyOnDispose;
            private readonly bool _destroyImmediate;
            private bool _disposed;

            private AutoCreationScope(
                bool desiredAutoCreationState,
                bool destroyExistingInstance,
                bool destroyInstancesOnDispose,
                bool destroyImmediate
            )
            {
                _previousState = AutoCreationEnabled;
                _destroyOnDispose = destroyInstancesOnDispose;
                _destroyImmediate = destroyImmediate;

                SetAutoCreationEnabled(desiredAutoCreationState);

                if (destroyExistingInstance)
                {
                    DestroyExistingDispatcher(destroyImmediate);
                }
            }

            /// <summary>
            /// Creates a scope that disables auto-creation and (by default) destroys dispatcher instances both when entering and leaving the scope.
            /// </summary>
            /// <param name="destroyExistingInstanceOnEnter">Set to <c>false</c> if the caller wants to keep the current dispatcher alive while auto-creation is disabled.</param>
            /// <param name="destroyInstancesOnDispose">When <c>true</c>, any dispatcher created while the scope was active is destroyed as soon as the scope is disposed.</param>
            /// <param name="destroyImmediate">
            /// Uses <see cref="UnityEngine.Object.DestroyImmediate(UnityEngine.Object)"/> when <c>true</c> (ideal for EditMode tests) and <see cref="UnityEngine.Object.Destroy(UnityEngine.Object)"/> otherwise.
            /// </param>
            /// <returns>A disposable scope that restores the previous <see cref="AutoCreationEnabled"/> value on dispose.</returns>
            /// <example>
            /// <code>
            /// using UnityMainThreadDispatcher.AutoCreationScope scope =
            ///     UnityMainThreadDispatcher.AutoCreationScope.Disabled(destroyImmediate: Application.isEditor);
            /// // Perform work that must not auto-create the dispatcher.
            /// </code>
            /// </example>
            public static AutoCreationScope Disabled(
                bool destroyExistingInstanceOnEnter = true,
                bool destroyInstancesOnDispose = true,
                bool destroyImmediate = true
            )
            {
                return new AutoCreationScope(
                    desiredAutoCreationState: false,
                    destroyExistingInstance: destroyExistingInstanceOnEnter,
                    destroyInstancesOnDispose: destroyInstancesOnDispose,
                    destroyImmediate: destroyImmediate
                );
            }

            /// <summary>
            /// Creates a scope that forces auto-creation on even if callers disabled it previously.
            /// This is useful for integration tests that temporarily require the dispatcher before restoring the prior state.
            /// </summary>
            /// <param name="destroyExistingInstanceOnEnter">Destroy the dispatcher before enabling auto-creation (rare).</param>
            /// <param name="destroyInstancesOnDispose">Destroy any instances created during the scope once it ends.</param>
            /// <param name="destroyImmediate">Choose between <see cref="UnityEngine.Object.DestroyImmediate(UnityEngine.Object)"/> and <see cref="UnityEngine.Object.Destroy(UnityEngine.Object)"/> for cleanup.</param>
            /// <returns>A scope that restores <see cref="AutoCreationEnabled"/> to its previous value when disposed.</returns>
            /// <example>
            /// <code>
            /// using UnityMainThreadDispatcher.AutoCreationScope scope =
            ///     UnityMainThreadDispatcher.AutoCreationScope.Enabled();
            /// // Dispatcher is guaranteed to auto-create when accessed here.
            /// </code>
            /// </example>
            public static AutoCreationScope Enabled(
                bool destroyExistingInstanceOnEnter = false,
                bool destroyInstancesOnDispose = false,
                bool destroyImmediate = true
            )
            {
                return new AutoCreationScope(
                    desiredAutoCreationState: true,
                    destroyExistingInstance: destroyExistingInstanceOnEnter,
                    destroyInstancesOnDispose: destroyInstancesOnDispose,
                    destroyImmediate: destroyImmediate
                );
            }

            /// <summary>
            /// Restores the previously captured <see cref="AutoCreationEnabled"/> value and optionally destroys dispatcher instances created inside the scope.
            /// </summary>
            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                SetAutoCreationEnabled(_previousState);

                if (_destroyOnDispose)
                {
                    DestroyExistingDispatcher(_destroyImmediate);
                }
            }
        }

        /// <summary>
        /// Creates a dispatcher test scope that follows the recommended pattern: disable auto-creation, destroy lingering instances immediately, re-enable auto-creation for the test body, and clean everything up on dispose.
        /// </summary>
        /// <param name="destroyImmediate">When <c>true</c>, uses <see cref="Object.DestroyImmediate(Object)"/> for cleanup. Set to <c>false</c> in play mode so Unity can process destruction safely.</param>
        /// <returns>An <see cref="AutoCreationScope"/> that automatically restores the previous auto-creation state when disposed.</returns>
        /// <example>
        /// <code>
        /// private UnityMainThreadDispatcher.AutoCreationScope _scope;
        ///
        /// [SetUp]
        /// public void SetUp()
        /// {
        ///     _scope = UnityMainThreadDispatcher.CreateTestScope(destroyImmediate: true);
        /// }
        ///
        /// [TearDown]
        /// public void TearDown()
        /// {
        ///     _scope?.Dispose();
        ///     _scope = null;
        /// }
        /// </code>
        /// </example>
        public static AutoCreationScope CreateTestScope(bool destroyImmediate = true)
        {
            AutoCreationScope scope = AutoCreationScope.Disabled(
                destroyExistingInstanceOnEnter: true,
                destroyInstancesOnDispose: true,
                destroyImmediate: destroyImmediate
            );

            SetAutoCreationEnabled(true);
            return scope;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void EnsureDispatcherBootstrap()
        {
            if (!AutoCreationEnabled)
            {
                return;
            }

            EnsureDispatcherExists("runtime bootstrap");
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void EnsureDispatcherBootstrapInEditor()
        {
            if (!AutoCreationEnabled)
            {
                return;
            }

            if (Application.isPlaying)
            {
                return;
            }

            EnsureDispatcherExists("editor bootstrap");
        }
#endif

        private static void EnsureDispatcherExists(string reason)
        {
            if (!AutoCreationEnabled)
            {
                return;
            }

            if (HasInstance)
            {
                return;
            }

            UnityMainThreadGuard.EnsureMainThread(reason);
            UnityMainThreadDispatcher dispatcher = Instance;
            if (!Application.isPlaying && dispatcher != null)
            {
#if UNITY_EDITOR
                dispatcher.ApplyEditorHideFlags();
#endif
            }
        }

        /// <summary>
        /// Enqueues an action to be executed on the main thread during the next Update.
        /// </summary>
        /// <example>
        /// <code>
        /// Task.Run(async () =>
        /// {
        ///     string data = await FetchAsync();
        ///     UnityMainThreadDispatcher.Instance.RunOnMainThread(() => Apply(data));
        /// });
        /// </code>
        /// </example>
        public void RunOnMainThread(Action action)
        {
            _ = Enqueue(action, logOverflow: true);
        }

        /// <summary>
        /// Attempts to enqueue an action to execute on the main thread without logging overflow warnings.
        /// Returns <c>true</c> when successfully queued; otherwise <c>false</c>.
        /// </summary>
        /// <remarks>Use this when overflow is expected and callers want to silently drop work (for example telemetry callbacks).</remarks>
        /// <example>
        /// <code>
        /// UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
        /// string status = BuildStatus();
        /// bool queued = dispatcher.TryRunOnMainThread(() => UpdateUi(status));
        /// if (!queued)
        /// {
        ///     Debug.LogWarning("UI update dropped because dispatcher queue is full.");
        /// }
        /// </code>
        /// </example>
        public bool TryRunOnMainThread(Action action)
        {
            return Enqueue(action, logOverflow: false);
        }

        private bool Enqueue(Action action, bool logOverflow)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (!TryEnqueueInternal(action))
            {
                if (logOverflow)
                {
                    LogOverflow();
                }
                return false;
            }

            return true;
        }

        private bool TryEnqueueInternal(Action action)
        {
            int newCount = Interlocked.Increment(ref _pendingActionCount);
            if (maxPendingActions > 0 && newCount > maxPendingActions)
            {
                Interlocked.Decrement(ref _pendingActionCount);
                return false;
            }

            _actions.Enqueue(action);
            return true;
        }

        private void LogOverflow()
        {
            string message = BuildOverflowMessage();

            if (!Application.isPlaying)
            {
                FormattableString formatted = FormattableStringFactory.Create("{0}", message);
                Debug.LogWarning(formatted);
                return;
            }

            int currentFrame = Time.frameCount;
            if (currentFrame == _lastOverflowFrame)
            {
                return;
            }

            _lastOverflowFrame = currentFrame;
            FormattableString throttled = FormattableStringFactory.Create("{0}", message);
            Debug.LogWarning(throttled);
        }

        private string BuildOverflowMessage()
        {
            int limit = maxPendingActions;
            if (limit <= 0)
            {
                limit = 0;
            }

            int pending = PendingActionCount;
            return $"UnityMainThreadDispatcher queue overflow (limit {limit}). Dropping action. Pending count: {pending}.";
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (!_attachedEditorUpdate && !Application.isPlaying)
            {
                EditorApplication.update += _update;
                _attachedEditorUpdate = true;
                ApplyEditorHideFlags();
            }
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            if (_attachedEditorUpdate)
            {
                EditorApplication.update -= _update;
                _attachedEditorUpdate = false;
            }
#endif
        }

        protected override void OnDestroy()
        {
#if UNITY_EDITOR
            if (_attachedEditorUpdate)
            {
                EditorApplication.update -= _update;
                _attachedEditorUpdate = false;
            }

            ApplyEditorHideFlags();
#endif
            base.OnDestroy();
        }

        private void Update()
        {
            while (_actions.TryDequeue(out Action action))
            {
                ExecuteQueuedAction(action);
            }
        }

        private void ExecuteQueuedAction(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogError($"UnityMainThreadDispatcher action threw an exception: {e}");
            }
            finally
            {
                Interlocked.Decrement(ref _pendingActionCount);
            }
        }

        /// <summary>
        /// Posts an action to run on the main thread and returns a <see cref="Task"/> that completes after execution.
        /// </summary>
        /// <example>
        /// <code>
        /// UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
        /// PlayerController player = GetPlayer();
        /// Animator playerAnimator = player.Animator;
        /// await dispatcher.RunAsync(() =>
        /// {
        ///     player.Health = 0;
        ///     playerAnimator.Play("Die");
        /// });
        /// </code>
        /// </example>
        public Task RunAsync(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            TaskCompletionSource<bool> taskCompletionSource = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            bool queued = Enqueue(
                () =>
                {
                    try
                    {
                        action();
                        taskCompletionSource.TrySetResult(true);
                    }
                    catch (Exception e)
                    {
                        taskCompletionSource.TrySetException(e);
                    }
                },
                logOverflow: true
            );

            if (!queued)
            {
                taskCompletionSource.TrySetException(
                    new InvalidOperationException(BuildOverflowMessage())
                );
            }

            return taskCompletionSource.Task;
        }

        /// <summary>
        /// Posts an asynchronous delegate that receives a <see cref="CancellationToken"/> and completes when the returned Task does.
        /// </summary>
        /// <example>
        /// <code>
        /// UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
        /// CanvasGroup canvasGroup = GetLoadingOverlay();
        /// using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        /// await dispatcher.RunAsync(async token =>
        /// {
        ///     await FadeCanvasGroupAsync(canvasGroup, 0f, token);
        /// }, timeout.Token);
        /// </code>
        /// </example>
        public Task RunAsync(
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default
        )
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            TaskCompletionSource<bool> taskCompletionSource = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            CancellationTokenRegistration registration = default;

            if (cancellationToken.CanBeCanceled)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    taskCompletionSource.TrySetCanceled(cancellationToken);
                    return taskCompletionSource.Task;
                }

                registration = cancellationToken.Register(() =>
                {
                    taskCompletionSource.TrySetCanceled(cancellationToken);
                });
            }

            bool queued = Enqueue(
                () =>
                {
                    if (taskCompletionSource.Task.IsCompleted)
                    {
                        registration.Dispose();
                        return;
                    }

                    Task runTask;
                    try
                    {
                        runTask = action(cancellationToken);
                    }
                    catch (Exception e)
                    {
                        registration.Dispose();
                        taskCompletionSource.TrySetException(e);
                        return;
                    }

                    if (runTask == null)
                    {
                        registration.Dispose();
                        taskCompletionSource.TrySetException(
                            new InvalidOperationException(
                                "UnityMainThreadDispatcher.RunAsync expected the delegate to return a Task."
                            )
                        );
                        return;
                    }

                    if (runTask.IsCompleted)
                    {
                        registration.Dispose();
                        CompleteFromTask(runTask, taskCompletionSource, cancellationToken);
                        return;
                    }

                    runTask.ContinueWith(
                        completedTask =>
                        {
                            registration.Dispose();
                            CompleteFromTask(
                                completedTask,
                                taskCompletionSource,
                                cancellationToken
                            );
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default
                    );
                },
                logOverflow: true
            );

            if (!queued)
            {
                registration.Dispose();
                taskCompletionSource.TrySetException(
                    new InvalidOperationException(BuildOverflowMessage())
                );
            }

            return taskCompletionSource.Task;
        }

        private static void CompleteFromTask(
            Task task,
            TaskCompletionSource<bool> completion,
            CancellationToken cancellationToken
        )
        {
            if (task == null || completion == null)
            {
                return;
            }

            if (task.IsCanceled)
            {
                if (cancellationToken.CanBeCanceled)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                else
                {
                    completion.TrySetCanceled();
                }

                return;
            }

            if (task.IsFaulted)
            {
                AggregateException aggregateException = task.Exception;
                if (aggregateException != null)
                {
                    AggregateException flattened = aggregateException.Flatten();
                    completion.TrySetException(flattened.InnerExceptions);
                }
                else
                {
                    completion.TrySetException(
                        new InvalidOperationException("Dispatcher task faulted.")
                    );
                }

                return;
            }

            completion.TrySetResult(true);
        }

        /// <summary>
        /// Posts a function to run on the main thread and returns its result via Task.
        /// </summary>
        public Task<T> Post<T>(Func<T> func)
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            TaskCompletionSource<T> taskCompletionSource = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            bool queued = Enqueue(
                () =>
                {
                    try
                    {
                        T result = func();
                        taskCompletionSource.TrySetResult(result);
                    }
                    catch (Exception e)
                    {
                        taskCompletionSource.TrySetException(e);
                    }
                },
                logOverflow: true
            );

            if (!queued)
            {
                taskCompletionSource.TrySetException(
                    new InvalidOperationException(BuildOverflowMessage())
                );
            }

            return taskCompletionSource.Task;
        }

#if UNITY_EDITOR
        private void ApplyEditorHideFlags()
        {
            if (!Application.isPlaying)
            {
                hideFlags = EditorDispatcherHideFlags;
            }
        }
#endif
    }
}
