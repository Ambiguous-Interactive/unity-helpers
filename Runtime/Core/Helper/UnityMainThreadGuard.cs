// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using UnityEngine;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// Captures Unity’s main-thread context and exposes guard helpers for APIs that must run on that thread.
    /// This prevents accidental background-thread access to Unity APIs.
    /// <para>
    /// Typical usage inside getters or event handlers:
    /// <code>
    /// public T Instance
    /// {
    ///     get
    ///     {
    ///         UnityMainThreadGuard.EnsureMainThread();
    ///         return _instance;
    ///     }
    /// }
    ///
    /// public void RefreshUI()
    /// {
    ///     UnityMainThreadGuard.EnsureMainThread("Refreshing UI");
    ///     // safe to interact with Unity objects here
    /// }
    /// </code>
    /// </para>
    /// </summary>
    internal static class UnityMainThreadGuard
    {
        private static int _mainThreadId;
        private static SynchronizationContext _mainThreadContext;
        private static int _initialized;

        internal static bool IsInitialized => _initialized == 1;

        internal static SynchronizationContext MainThreadContext => _mainThreadContext;

        internal static bool IsMainThread
        {
            get
            {
                if (!IsInitialized)
                {
                    return true;
                }

                if (_mainThreadId == 0)
                {
                    return true;
                }

                int currentId = Thread.CurrentThread.ManagedThreadId;
                return currentId == _mainThreadId;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureRuntimeThread()
        {
            Capture(Thread.CurrentThread);
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        internal static void CaptureEditorThread()
        {
            if (Application.isPlaying)
            {
                return;
            }

            Capture(Thread.CurrentThread);
        }
#endif

        /// <summary>
        /// Captures the provided thread as the main thread and stores its <see cref="SynchronizationContext"/>.
        /// Normally invoked automatically via <see cref="RuntimeInitializeOnLoadMethodAttribute"/> /
        /// <c>[InitializeOnLoadMethod]</c>.
        /// </summary>
        /// <param name="thread">Thread to treat as the Unity main thread.</param>
        internal static void Capture(Thread thread)
        {
            if (thread == null)
            {
                return;
            }

            _mainThreadId = thread.ManagedThreadId;
            _mainThreadContext = SynchronizationContext.Current ?? new SynchronizationContext();
            Interlocked.Exchange(ref _initialized, 1);
        }

        /// <summary>
        /// Throws an <see cref="InvalidOperationException"/> when invoked on a non-main thread.
        /// Caller metadata is captured automatically via compiler attributes so the resulting message
        /// pinpoints the offending member and source location.
        /// <para>
        /// Example:
        /// <code>
        /// void Update()
        /// {
        ///     UnityMainThreadGuard.EnsureMainThread();
        ///     // Update logic...
        /// }
        /// </code>
        /// </para>
        /// </summary>
        /// <param name="context">
        /// Optional label describing why the guard is required, appended to the error message.
        /// </param>
        /// <param name="memberName">Populated automatically with <see cref="CallerMemberNameAttribute"/>.</param>
        /// <param name="callerFilePath">Populated automatically with <see cref="CallerFilePathAttribute"/>.</param>
        /// <param name="callerLineNumber">Populated automatically with <see cref="CallerLineNumberAttribute"/>.</param>
        internal static void EnsureMainThread(
            string context = null,
            [CallerMemberName] string memberName = null,
            [CallerFilePath] string callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = 0
        )
        {
            if (IsMainThread)
            {
                return;
            }

            string fileBaseName = string.IsNullOrWhiteSpace(callerFilePath)
                ? "UnknownFile"
                : Path.GetFileNameWithoutExtension(callerFilePath);
            string fileLabel = string.IsNullOrWhiteSpace(callerFilePath)
                ? "UnknownFile"
                : Path.GetFileName(callerFilePath);

            string location = string.IsNullOrEmpty(memberName)
                ? fileBaseName
                : $"{fileBaseName}.{memberName}";

            if (!string.IsNullOrEmpty(context))
            {
                location = $"{location} ({context})";
            }

            string message =
                $"{location} must be accessed on Unity's main thread (called from {fileLabel}:{callerLineNumber}). Use UnityMainThreadDispatcher.Instance.RunOnMainThread to marshal work safely.";

            throw new InvalidOperationException(message);
        }

        internal static bool TryPostToMainThread(Action action)
        {
            if (action == null)
            {
                return false;
            }

            SynchronizationContext context = _mainThreadContext;
            if (context == null)
            {
                return false;
            }

            try
            {
                context.Post(static state => ((Action)state)?.Invoke(), action);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
