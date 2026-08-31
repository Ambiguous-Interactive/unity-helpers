// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using Helper;
    using Helper.Logging;
    using UnityEngine;
    using Utils;
    using Object = UnityEngine.Object;
#if !SINGLE_THREADED
    using System.Collections.Concurrent;
#endif

    /// <summary>
    /// Provides advanced logging extensions for Unity Objects with metadata extraction, thread-aware logging,
    /// and per-object logging control. Enabled in development builds, debug builds, and Unity Editor.
    /// </summary>
    /// <remarks>
    /// Thread Safety: Thread-safe. Automatically routes logs to Unity main thread when necessary.
    /// Performance: Uses reflection-based metadata caching with periodic cleanup. Metadata is cached per type.
    /// Allocations: When disabled, none: the entry points are
    /// <see cref="System.Diagnostics.ConditionalAttribute"/>, so the compiler removes the entire
    /// call site -- receiver and arguments included. Nothing is evaluated and no
    /// <see cref="System.FormattableString"/> is built. When enabled, uses the metadata cache and
    /// pooled dictionary resources to minimize allocations.
    /// Configuration: enabled automatically wherever Unity defines UNITY_EDITOR,
    /// DEVELOPMENT_BUILD or DEBUG. To enable logging in a release build, define
    /// ENABLE_UBERLOGGING (or the per-level DEBUG_LOGGING / WARN_LOGGING / ERROR_LOGGING)
    /// <b>project-wide</b> -- for example in Player Settings' scripting define symbols. A
    /// [Conditional] symbol is evaluated in the assembly that <i>calls</i> the method, so
    /// defining it only inside this package has no effect on consumer code.
    /// </remarks>
    public static class WallstopStudiosLogger
    {
        public static readonly UnityLogTagFormatter LogInstance = new(
            createDefaultDecorators: true
        );

        private static bool ShouldLogOnMainThread =>
            UnityMainThreadGuard.IsInitialized
                ? UnityMainThreadGuard.IsMainThread
                : Equals(Thread.CurrentThread, UnityMainThread)
                    || (UnityMainThread == null && !Application.isPlaying);

        private static Thread UnityMainThread;
        private const int LogsPerCacheClean = 5;

        /*
            An int rather than a bool so the read and the write are ordered: a worker thread that
            flips this has no other barrier, and a plain static field lets a reader keep its own
            cached copy indefinitely.
        */
        private static int LoggingEnabled = 1;
        private static long _cacheAccessCount;

        /*
            Every touch of Disabled is under this lock. The old set was enumerated into a buffer
            every fifth log while another thread could be adding to it, which is a collection-
            modified exception in the middle of a diagnostic.
        */
        private static readonly object DisabledLock = new();
        private static readonly HashSet<Object> Disabled = new(ObjectIdentityComparer.Instance);

#if !SINGLE_THREADED
        private static readonly ConcurrentDictionary<
            Type,
            (string, Func<object, object>)[]
        > MetadataCache = new();
#else
        private static readonly Dictionary<Type, (string, Func<object, object>)[]> MetadataCache =
            new();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeMainThread()
        {
            UnityMainThread = Thread.CurrentThread;
            UnityMainThreadGuard.Capture(UnityMainThread);
            lock (DisabledLock)
            {
                Disabled.Clear();
            }
        }

        /// <summary>
        /// Globally enables logging for all Unity Objects.
        /// </summary>
        /// <param name="component">The Unity Object requesting the enable (not used, can be any Object).</param>
        /// <remarks>
        /// Thread-safe: Yes.
        /// Performance: O(1).
        /// Allocations: None.
        /// Edge cases: Overrides any per-object disable settings when global logging is re-enabled.
        /// </remarks>
        public static void GlobalEnableLogging(this Object component)
        {
            Volatile.Write(ref LoggingEnabled, 1);
        }

        public static void GlobalDisableLogging(this Object component)
        {
            Volatile.Write(ref LoggingEnabled, 0);
        }

        /// <summary>
        /// Gets whether global logging is enabled.
        /// </summary>
        public static bool IsGlobalLoggingEnabled()
        {
            return Volatile.Read(ref LoggingEnabled) != 0;
        }

        /// <summary>
        /// Sets global logging enabled/disabled without requiring an Object instance.
        /// </summary>
        public static void SetGlobalLoggingEnabled(bool enabled)
        {
            Volatile.Write(ref LoggingEnabled, enabled ? 1 : 0);
        }

        public static void EnableLogging(this Object component)
        {
            lock (DisabledLock)
            {
                _ = Disabled.Remove(component);
            }
        }

        public static void DisableLogging(this Object component)
        {
            lock (DisabledLock)
            {
                _ = Disabled.Add(component);
            }
        }

        [HideInCallstack]
        public static string GenericToString(this Object component)
        {
            if (component == null)
            {
                return "null";
            }

            (string, Func<object, object>)[] metadataAccess = MetadataCache.GetOrAdd(
                component.GetType(),
                static inType =>
                {
                    FieldInfo[] fields = inType.GetFields(
                        BindingFlags.Public | BindingFlags.Instance
                    );
                    PropertyInfo[] properties = inType.GetProperties(
                        BindingFlags.Public | BindingFlags.Instance
                    );

                    using PooledResource<List<(string, Func<object, object>)>> bufferResource =
                        Buffers<(string, Func<object, object>)>.List.Get(
                            out List<(string, Func<object, object>)> buffer
                        );

                    for (int i = 0; i < fields.Length; i++)
                    {
                        FieldInfo field = fields[i];
                        buffer.Add((field.Name, ReflectionHelpers.GetFieldGetter(field)));
                    }

                    for (int i = 0; i < properties.Length; i++)
                    {
                        PropertyInfo property = properties[i];
                        buffer.Add((property.Name, ReflectionHelpers.GetPropertyGetter(property)));
                    }

                    return buffer.ToArray();
                }
            );

            /*
                Per call, not a shared static: two threads describing two objects through one
                dictionary produced one object's fields under the other's name.
            */
            using PooledResource<Dictionary<string, object>> valuesResource = DictionaryBuffer<
                string,
                object
            >.Dictionary.Get(out Dictionary<string, object> values);
            foreach ((string name, Func<object, object> access) in metadataAccess)
            {
                try
                {
                    string valueFormat = ValueFormat(access(component));
                    if (valueFormat != null)
                    {
                        values[name] = valueFormat;
                    }
                }
                catch
                {
                    // Skip
                }
            }

            return values.ToJson();
        }

        [HideInCallstack]
        private static string ValueFormat(object value)
        {
            if (value is Object obj)
            {
                return obj != null ? obj.name : "null";
            }
            return value?.ToString();
        }

        /// <summary>
        /// Logs an informational message through the package formatter.
        /// </summary>
        /// <param name="component">The context object the log is attributed to.</param>
        /// <param name="message">The interpolated message. Formatter tags such as <c>:json</c> apply.</param>
        /// <param name="e">An optional exception to append.</param>
        /// <param name="pretty">When <see langword="true"/>, prefixes the timestamp and context.</param>
        /// <param name="stackTrace">
        /// Pass <see langword="false"/> for a diagnostic that repeats -- once per object at load, or
        /// once per frame. Unity captures a managed stack trace for every log by default, measured
        /// on 6000.4.6f1 at 178.4 us against 13.3 us without it, and for a message that already
        /// names its component, field and type that stack is the same internal path every time.
        /// </param>
        [HideInCallstack]
        [System.Diagnostics.Conditional(CompilationSymbols.EnableUberLogging)]
        [System.Diagnostics.Conditional(CompilationSymbols.DevelopmentBuild)]
        [System.Diagnostics.Conditional(CompilationSymbols.Debug)]
        [System.Diagnostics.Conditional(CompilationSymbols.UnityEditor)]
        [System.Diagnostics.Conditional(CompilationSymbols.DebugLogging)]
        public static void Log(
            this Object component,
            FormattableString message,
            Exception e = null,
            bool pretty = true,
            bool stackTrace = true
        )
        {
            LogDebugCore(component, message, e, pretty, stackTrace);
        }

        /// <summary>
        /// Logs a debug message through the package formatter.
        /// </summary>
        /// <param name="component">The context object the log is attributed to.</param>
        /// <param name="message">The interpolated message. Formatter tags such as <c>:json</c> apply.</param>
        /// <param name="e">An optional exception to append.</param>
        /// <param name="pretty">When <see langword="true"/>, prefixes the timestamp and context.</param>
        /// <param name="stackTrace">
        /// Pass <see langword="false"/> for a diagnostic that repeats -- once per object at load, or
        /// once per frame. Unity captures a managed stack trace for every log by default, measured
        /// on 6000.4.6f1 at 178.4 us against 13.3 us without it, and for a message that already
        /// names its component, field and type that stack is the same internal path every time.
        /// </param>
        [HideInCallstack]
        [System.Diagnostics.Conditional(CompilationSymbols.EnableUberLogging)]
        [System.Diagnostics.Conditional(CompilationSymbols.DevelopmentBuild)]
        [System.Diagnostics.Conditional(CompilationSymbols.Debug)]
        [System.Diagnostics.Conditional(CompilationSymbols.UnityEditor)]
        [System.Diagnostics.Conditional(CompilationSymbols.DebugLogging)]
        public static void LogDebug(
            this Object component,
            FormattableString message,
            Exception e = null,
            bool pretty = true,
            bool stackTrace = true
        )
        {
            LogDebugCore(component, message, e, pretty, stackTrace);
        }

        /// <summary>
        /// Logs a warning through the package formatter.
        /// </summary>
        /// <param name="component">The context object the log is attributed to.</param>
        /// <param name="message">The interpolated message. Formatter tags such as <c>:json</c> apply.</param>
        /// <param name="e">An optional exception to append.</param>
        /// <param name="pretty">When <see langword="true"/>, prefixes the timestamp and context.</param>
        /// <param name="stackTrace">
        /// Pass <see langword="false"/> for a diagnostic that repeats -- once per object at load, or
        /// once per frame. Unity captures a managed stack trace for every log by default, measured
        /// on 6000.4.6f1 at 178.4 us against 13.3 us without it, and for a message that already
        /// names its component, field and type that stack is the same internal path every time.
        /// </param>
        [HideInCallstack]
        [System.Diagnostics.Conditional(CompilationSymbols.EnableUberLogging)]
        [System.Diagnostics.Conditional(CompilationSymbols.DevelopmentBuild)]
        [System.Diagnostics.Conditional(CompilationSymbols.Debug)]
        [System.Diagnostics.Conditional(CompilationSymbols.UnityEditor)]
        [System.Diagnostics.Conditional(CompilationSymbols.WarnLogging)]
        public static void LogWarn(
            this Object component,
            FormattableString message,
            Exception e = null,
            bool pretty = true,
            bool stackTrace = true
        )
        {
            LogWarnCore(component, message, e, pretty, stackTrace);
        }

        /// <summary>
        /// Logs an error through the package formatter.
        /// </summary>
        /// <param name="component">The context object the log is attributed to.</param>
        /// <param name="message">The interpolated message. Formatter tags such as <c>:json</c> apply.</param>
        /// <param name="e">An optional exception to append.</param>
        /// <param name="pretty">When <see langword="true"/>, prefixes the timestamp and context.</param>
        /// <param name="stackTrace">
        /// Pass <see langword="false"/> for a diagnostic that repeats -- once per object at load, or
        /// once per frame. Unity captures a managed stack trace for every log by default, measured
        /// on 6000.4.6f1 at 178.4 us against 13.3 us without it, and for a message that already
        /// names its component, field and type that stack is the same internal path every time.
        /// </param>
        [HideInCallstack]
        [System.Diagnostics.Conditional(CompilationSymbols.EnableUberLogging)]
        [System.Diagnostics.Conditional(CompilationSymbols.DevelopmentBuild)]
        [System.Diagnostics.Conditional(CompilationSymbols.Debug)]
        [System.Diagnostics.Conditional(CompilationSymbols.UnityEditor)]
        [System.Diagnostics.Conditional(CompilationSymbols.ErrorLogging)]
        public static void LogError(
            this Object component,
            FormattableString message,
            Exception e = null,
            bool pretty = true,
            bool stackTrace = true
        )
        {
            LogErrorCore(component, message, e, pretty, stackTrace);
        }

        // The public entry points above are [Conditional], which strips the whole call site --
        // receiver and arguments included -- in any assembly that defines none of the symbols.
        // They must delegate to these unconditional cores rather than to one another: a
        // [Conditional] call is resolved against the *calling* assembly's symbols, so a
        // package-internal call to a [Conditional] method would be stripped whenever this
        // package itself is compiled without them, emptying the public methods even for a
        // consumer that did define ENABLE_UBERLOGGING.
        //
        // They are internal for the same reason: any other package API that is itself
        // [Conditional] must reach the log through here, never through a [Conditional] entry
        // point, or it inherits exactly the emptying this indirection exists to prevent.
        [HideInCallstack]
        internal static void LogDebugCore(
            Object component,
            FormattableString message,
            Exception e,
            bool pretty,
            bool stackTrace = true
        )
        {
            if (!LoggingAllowed(component))
            {
                return;
            }

            if (ShouldLogOnMainThread)
            {
                LogInstance.Log(message, component, e, pretty, stackTrace);
            }
            else
            {
                FormattableString localMessage = message;
                Object localComponent = component;
                Exception localE = e;
                bool localPretty = pretty;
                bool localStackTrace = stackTrace;
                if (
                    !TryInvokeOnMainThread(() =>
                        LogInstance.Log(
                            localMessage,
                            localComponent,
                            localE,
                            localPretty,
                            localStackTrace
                        )
                    )
                )
                {
                    LogOffline(LogType.Log, localComponent, localMessage, localE);
                }
            }
        }

        [HideInCallstack]
        internal static void LogWarnCore(
            Object component,
            FormattableString message,
            Exception e,
            bool pretty,
            bool stackTrace = true
        )
        {
            if (!LoggingAllowed(component))
            {
                return;
            }

            if (ShouldLogOnMainThread)
            {
                LogInstance.LogWarn(message, component, e, pretty, stackTrace);
            }
            else
            {
                FormattableString localMessage = message;
                Object localComponent = component;
                Exception localE = e;
                bool localPretty = pretty;
                bool localStackTrace = stackTrace;
                if (
                    !TryInvokeOnMainThread(() =>
                        LogInstance.LogWarn(
                            localMessage,
                            localComponent,
                            localE,
                            localPretty,
                            localStackTrace
                        )
                    )
                )
                {
                    LogOffline(LogType.Warning, localComponent, localMessage, localE);
                }
            }
        }

        [HideInCallstack]
        internal static void LogErrorCore(
            Object component,
            FormattableString message,
            Exception e,
            bool pretty,
            bool stackTrace = true
        )
        {
            if (!LoggingAllowed(component))
            {
                return;
            }

            if (ShouldLogOnMainThread)
            {
                LogInstance.LogError(message, component, e, pretty, stackTrace);
            }
            else
            {
                FormattableString localMessage = message;
                Object localComponent = component;
                Exception localE = e;
                bool localPretty = pretty;
                bool localStackTrace = stackTrace;
                if (
                    !TryInvokeOnMainThread(() =>
                        LogInstance.LogError(
                            localMessage,
                            localComponent,
                            localE,
                            localPretty,
                            localStackTrace
                        )
                    )
                )
                {
                    LogOffline(LogType.Error, localComponent, localMessage, localE);
                }
            }
        }

        [HideInCallstack]
        private static bool LoggingAllowed(Object component)
        {
            if (Volatile.Read(ref LoggingEnabled) == 0)
            {
                return false;
            }

            /*
                Only the main thread sweeps, and only on a call that could still log. The thread
                matters because Object's == is a native aliveness check; the order matters because
                ShouldLogOnMainThread can reach Application.isPlaying, which a caller that has
                already decided not to log should never provoke.
            */
            if (
                Interlocked.Increment(ref _cacheAccessCount) % LogsPerCacheClean == 0
                && ShouldLogOnMainThread
            )
            {
                SweepDestroyedDisabledObjects();
            }

            lock (DisabledLock)
            {
                return !Disabled.Contains(component);
            }
        }

        /// <summary>
        /// Drops entries whose Unity object has been destroyed. Only the main thread runs it:
        /// <c>Object</c>'s <c>==</c> is a native aliveness check, so a worker asking it is asking
        /// the engine a question from the wrong thread.
        /// </summary>
        private static void SweepDestroyedDisabledObjects()
        {
            using PooledResource<List<Object>> bufferResource = Buffers<Object>.List.Get(
                out List<Object> buffer
            );
            lock (DisabledLock)
            {
                buffer.AddRange(Disabled);
            }

            using PooledResource<List<Object>> destroyedResource = Buffers<Object>.List.Get(
                out List<Object> destroyed
            );
            foreach (Object disabled in buffer)
            {
                if (disabled == null)
                {
                    destroyed.Add(disabled);
                }
            }

            if (destroyed.Count == 0)
            {
                return;
            }

            lock (DisabledLock)
            {
                foreach (Object disabled in destroyed)
                {
                    _ = Disabled.Remove(disabled);
                }
            }
        }

        private static bool TryInvokeOnMainThread(Action action)
        {
            return UnityMainThreadDispatcher.TryDispatchToMainThread(action)
                || UnityMainThreadGuard.TryPostToMainThread(action);
        }

        private static void LogOffline(
            LogType type,
            Object component,
            FormattableString message,
            Exception exception
        )
        {
            try
            {
                string contextLabel = ReferenceEquals(component, null)
                    ? "null"
                    : component.GetType().Name;
                string formattedMessage = message?.ToString() ?? string.Empty;
                if (exception != null)
                {
                    formattedMessage = $"{formattedMessage} :: {exception}";
                }

                Debug.unityLogger.Log(
                    type,
                    $"[WallstopMainThreadLogger:{contextLabel}] {formattedMessage}"
                );
            }
            catch
            {
                // Swallow
            }
        }

        /// <summary>
        /// Reference identity, so membership never asks a Unity object whether its native half is
        /// still alive -- which is what <c>Object.Equals</c> does, on whatever thread asked.
        /// </summary>
        private sealed class ObjectIdentityComparer : IEqualityComparer<Object>
        {
            internal static readonly ObjectIdentityComparer Instance = new();

            private ObjectIdentityComparer() { }

            public bool Equals(Object left, Object right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(Object value)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
            }
        }
    }
}
