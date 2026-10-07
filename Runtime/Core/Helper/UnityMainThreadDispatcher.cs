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
    /// Queued task operations are canceled on destruction; started operations complete normally.
    /// Submissions before initialization or after destruction are rejected.
    /// </remarks>
    [ExecuteAlways]
    public sealed class UnityMainThreadDispatcher : RuntimeSingleton<UnityMainThreadDispatcher>
    {
        private const int DefaultQueueLimit = 4096;

        private readonly ConcurrentQueue<QueuedAction> _actions = new();
        private readonly object _queueGate = new();
        private bool _retired;
        private bool _initialized;
        private int _pendingActionCount;
        private int _lastOverflowFrame = -1;
#if UNITY_EDITOR
        private const HideFlags EditorDispatcherHideFlags = HideFlags.None;
#endif

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

        protected override bool Preserve => false;

        protected override bool LogErrorOnDestruction => false;

        [SerializeField]
        [Tooltip(
            "Maximum number of queued actions before new submissions are dropped. Set to 0 for unlimited."
        )]
        private int maxPendingActions = DefaultQueueLimit;

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

            return dispatcher.TryRunOnMainThread(action);
        }

        internal static int GetLiveDispatcherCount()
        {
            return Resources.FindObjectsOfTypeAll<UnityMainThreadDispatcher>().Length;
        }

#if UNITY_EDITOR
        private readonly EditorApplication.CallbackFunction _update;
        private bool _attachedEditorUpdate;
#endif

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

        public UnityMainThreadDispatcher()
        {
#if UNITY_EDITOR
            _update = Update;
#endif
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
                if (DestroyDispatcherObject(dispatcher, immediate))
                {
                    destroyed = true;
                }
            }

            UnityMainThreadDispatcher[] allDispatchers =
                Resources.FindObjectsOfTypeAll<UnityMainThreadDispatcher>();
            if (allDispatchers is { Length: > 0 })
            {
                foreach (UnityMainThreadDispatcher localDispatcher in allDispatchers)
                {
                    if (DestroyDispatcherObject(localDispatcher, immediate))
                    {
                        destroyed = true;
                    }
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

#if UNITY_EDITOR
            if (EditorUtility.IsPersistent(dispatcher))
            {
                return false;
            }
#endif

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
                DestroyImmediate(dispatcherObject);
                return true;
            }

            Destroy(dispatcherObject);

            return true;
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
                // Report the token that actually canceled the operation, which may differ from the caller token.
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                CancellationToken cancelingToken = ObserveCancellationToken(task);
                if (cancelingToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancelingToken);
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
        /// Reads the token a canceled task was canceled with, observing its exception so it cannot
        /// resurface as an unobserved-task fault. A task canceled without one answers
        /// <see cref="CancellationToken.None"/>.
        /// </summary>
        private static CancellationToken ObserveCancellationToken(Task task)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException canceled)
            {
                return canceled.CancellationToken;
            }
            catch (Exception)
            {
                return CancellationToken.None;
            }

            return CancellationToken.None;
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
                logOverflow: true,
                onDiscard: () => taskCompletionSource.TrySetCanceled()
            );

            if (!queued && !taskCompletionSource.Task.IsCompleted)
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
                logOverflow: true,
                onDiscard: () =>
                {
                    registration.Dispose();
                    if (cancellationToken.IsCancellationRequested)
                    {
                        taskCompletionSource.TrySetCanceled(cancellationToken);
                    }
                    else
                    {
                        taskCompletionSource.TrySetCanceled();
                    }
                }
            );

            if (!queued)
            {
                registration.Dispose();
                if (!taskCompletionSource.Task.IsCompleted)
                {
                    taskCompletionSource.TrySetException(
                        new InvalidOperationException(BuildOverflowMessage())
                    );
                }
            }

            return taskCompletionSource.Task;
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
                logOverflow: true,
                onDiscard: () => taskCompletionSource.TrySetCanceled()
            );

            if (!queued && !taskCompletionSource.Task.IsCompleted)
            {
                taskCompletionSource.TrySetException(
                    new InvalidOperationException(BuildOverflowMessage())
                );
            }

            return taskCompletionSource.Task;
        }

        protected override void Awake()
        {
            base.Awake();
            InitializeQueue();
        }

        protected override void OnDestroy()
        {
            lock (_queueGate)
            {
                _retired = true;
            }
            while (_actions.TryDequeue(out QueuedAction queued))
            {
                try
                {
                    queued.onDiscard?.Invoke();
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingActionCount);
                }
            }
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

        internal bool TryDequeueQueuedAction(out QueuedAction queued)
        {
            lock (_queueGate)
            {
                if (_retired || !_initialized)
                {
                    queued = default;
                    return false;
                }
                return _actions.TryDequeue(out queued);
            }
        }

        internal void ExecuteQueuedAction(QueuedAction queued)
        {
            try
            {
                queued.action();
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

        private bool Enqueue(Action action, bool logOverflow, Action onDiscard = null)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (!TryEnqueueInternal(new QueuedAction(action, onDiscard), out bool retired))
            {
                if (retired)
                {
                    onDiscard?.Invoke();
                    return false;
                }
                if (logOverflow)
                {
                    LogOverflow();
                }
                return false;
            }

            return true;
        }

        private bool TryEnqueueInternal(QueuedAction queued, out bool retired)
        {
            lock (_queueGate)
            {
                if (_retired || !_initialized)
                {
                    retired = true;
                    return false;
                }
                int newCount = Interlocked.Increment(ref _pendingActionCount);
                if (0 < maxPendingActions && maxPendingActions < newCount)
                {
                    Interlocked.Decrement(ref _pendingActionCount);
                    retired = false;
                    return false;
                }
                _actions.Enqueue(queued);
                retired = false;
                return true;
            }
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

        private void InitializeQueue()
        {
            lock (_queueGate)
            {
                _initialized = !_retired;
            }
        }

        private void OnEnable()
        {
            InitializeQueue();
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

        private void Update()
        {
            while (TryDequeueQueuedAction(out QueuedAction queued))
            {
                ExecuteQueuedAction(queued);
            }
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

        /// <summary>
        /// Disposable helper that temporarily overrides <see cref="AutoCreationEnabled"/> and (optionally) destroys dispatcher instances on enter/exit.
        /// Use this to keep tests and integration setups deterministic without hand-written <c>try/finally</c> blocks.
        /// </summary>
        /// <remarks>
        /// The scope records the previous <see cref="AutoCreationEnabled"/> value, switches to the desired state, and restores the original value on dispose.
        /// It also exposes knobs for destroying existing dispatcher GameObjects immediately (ideal for EditMode tests) or on dispose.
        /// Cleanup preserves loaded prefab assets and their dispatcher components.
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

        internal readonly struct QueuedAction
        {
            internal readonly Action action;
            internal readonly Action onDiscard;

            internal QueuedAction(Action action, Action onDiscard)
            {
                this.action = action;
                this.onDiscard = onDiscard;
            }
        }
    }
}
