// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if !ENABLE_UBERLOGGING && (DEVELOPMENT_BUILD || DEBUG || UNITY_EDITOR)
#define ENABLE_UBERLOGGING
#endif

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

    /// <summary>
    /// Provides advanced logging extensions for Unity Objects with metadata extraction, thread-aware logging,
    /// and per-object logging control. Enabled in development builds, debug builds, and Unity Editor.
    /// </summary>
    /// <remarks>
    /// Thread Safety: Thread-safe. Automatically routes logs to Unity main thread when necessary.
    /// Performance: Uses reflection-based metadata caching with periodic cleanup. Metadata is cached per type.
    /// Allocations: Uses metadata cache and pooled dictionary resources to minimize allocations.
    /// Configuration: Define ENABLE_UBERLOGGING to enable logging in non-development builds.
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

        private static bool LoggingEnabled = true;
        private static long _cacheAccessCount;

        private static readonly HashSet<Object> Disabled = new();
        private static readonly Dictionary<Type, (string, Func<object, object>)[]> MetadataCache =
            new();

        private static readonly Dictionary<string, object> GenericObject = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeMainThread()
        {
            UnityMainThread = Thread.CurrentThread;
            UnityMainThreadGuard.Capture(UnityMainThread);
            Disabled.Clear();
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
            LoggingEnabled = true;
        }

        public static void GlobalDisableLogging(this Object component)
        {
            LoggingEnabled = false;
        }

        /// <summary>
        /// Gets whether global logging is enabled.
        /// </summary>
        public static bool IsGlobalLoggingEnabled()
        {
            return LoggingEnabled;
        }

        /// <summary>
        /// Sets global logging enabled/disabled without requiring an Object instance.
        /// </summary>
        public static void SetGlobalLoggingEnabled(bool enabled)
        {
            LoggingEnabled = enabled;
        }

        public static void EnableLogging(this Object component)
        {
            Disabled.Remove(component);
        }

        public static void DisableLogging(this Object component)
        {
            Disabled.Add(component);
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

            GenericObject.Clear();
            foreach ((string name, Func<object, object> access) in metadataAccess)
            {
                try
                {
                    string valueFormat = ValueFormat(access(component));
                    if (valueFormat != null)
                    {
                        GenericObject[name] = valueFormat;
                    }
                }
                catch
                {
                    // Skip
                }
            }

            return GenericObject.ToJson();
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

        [HideInCallstack]
        public static void Log(
            this Object component,
            FormattableString message,
            Exception e = null,
            bool pretty = true
        )
        {
#if ENABLE_UBERLOGGING || DEBUG_LOGGING
            component.LogDebug(message, e, pretty);
#endif
        }

        [HideInCallstack]
        public static void LogDebug(
            this Object component,
            FormattableString message,
            Exception e = null,
            bool pretty = true
        )
        {
#if ENABLE_UBERLOGGING || DEBUG_LOGGING
            if (!LoggingAllowed(component))
            {
                return;
            }

            if (ShouldLogOnMainThread)
            {
                LogInstance.Log(message, component, e, pretty);
            }
            else
            {
                FormattableString localMessage = message;
                Object localComponent = component;
                Exception localE = e;
                bool localPretty = pretty;
                if (
                    !TryInvokeOnMainThread(() =>
                        LogInstance.Log(localMessage, localComponent, localE, localPretty)
                    )
                )
                {
                    LogOffline(LogType.Log, localComponent, localMessage, localE);
                }
            }
#endif
        }

        [HideInCallstack]
        public static void LogWarn(
            this Object component,
            FormattableString message,
            Exception e = null,
            bool pretty = true
        )
        {
#if ENABLE_UBERLOGGING || WARN_LOGGING
            if (!LoggingAllowed(component))
            {
                return;
            }

            if (ShouldLogOnMainThread)
            {
                LogInstance.LogWarn(message, component, e, pretty);
            }
            else
            {
                FormattableString localMessage = message;
                Object localComponent = component;
                Exception localE = e;
                bool localPretty = pretty;
                if (
                    !TryInvokeOnMainThread(() =>
                        LogInstance.LogWarn(localMessage, localComponent, localE, localPretty)
                    )
                )
                {
                    LogOffline(LogType.Warning, localComponent, localMessage, localE);
                }
            }
#endif
        }

        [HideInCallstack]
        public static void LogError(
            this Object component,
            FormattableString message,
            Exception e = null,
            bool pretty = true
        )
        {
#if ENABLE_UBERLOGGING || ERROR_LOGGING
            if (!LoggingAllowed(component))
            {
                return;
            }

            if (ShouldLogOnMainThread)
            {
                LogInstance.LogError(message, component, e, pretty);
            }
            else
            {
                FormattableString localMessage = message;
                Object localComponent = component;
                Exception localE = e;
                bool localPretty = pretty;
                if (
                    !TryInvokeOnMainThread(() =>
                        LogInstance.LogError(localMessage, localComponent, localE, localPretty)
                    )
                )
                {
                    LogOffline(LogType.Error, localComponent, localMessage, localE);
                }
            }
#endif
        }

        [HideInCallstack]
        private static bool LoggingAllowed(Object component)
        {
            if (Interlocked.Increment(ref _cacheAccessCount) % LogsPerCacheClean != 0)
            {
                return LoggingEnabled && !Disabled.Contains(component);
            }

            using PooledResource<List<Object>> bufferResource = Buffers<Object>.List.Get(
                out List<Object> buffer
            );
            buffer.AddRange(Disabled);

            foreach (Object disabled in buffer)
            {
                if (disabled == null)
                {
                    _ = Disabled.Remove(disabled);
                }
            }

            return LoggingEnabled && !Disabled.Contains(component);
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
#if ENABLE_UBERLOGGING || DEBUG_LOGGING || WARN_LOGGING || ERROR_LOGGING
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
#endif
        }
    }
}
