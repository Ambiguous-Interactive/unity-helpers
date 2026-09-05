// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WButton
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEditorInternal;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;

    internal static class WButtonInvocationController
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            IncludeFields = true,
        };

        internal static void ProcessTriggeredMethods(List<WButtonMethodContext> triggeredContexts)
        {
            if (triggeredContexts == null || triggeredContexts.Count == 0)
            {
                return;
            }

            foreach (WButtonMethodContext context in triggeredContexts)
            {
                ExecuteContext(context);
                context.ResetTrigger();
            }
        }

        internal static void CancelActiveInvocations(WButtonMethodContext context)
        {
            if (context == null)
            {
                return;
            }

            WButtonMethodState[] states = context.States;
            foreach (WButtonMethodState state in states)
            {
                CancelInvocation(state);
            }
        }

        private static void ExecuteContext(WButtonMethodContext context)
        {
            WButtonMethodMetadata metadata = context.Metadata;
            int historyCapacity =
                0 <= metadata.HistoryCapacity
                    ? metadata.HistoryCapacity
                    : UnityHelpersSettings.GetWButtonHistorySize();

            WButtonMethodState[] states = context.States;
            UnityEngine.Object[] targets = context.Targets;

            for (int index = 0; index < states.Length; index++)
            {
                WButtonMethodState state = states[index];
                UnityEngine.Object target = targets[index];
                if (target == null)
                {
                    continue;
                }

                CancelInvocation(state);

                if (
                    !TryBuildArguments(
                        state,
                        metadata,
                        out object[] arguments,
                        out CancellationTokenSource cancellationSource,
                        out string argumentError
                    )
                )
                {
                    state.AddResult(
                        new WButtonResultEntry(
                            WButtonResultKind.Error,
                            DateTime.UtcNow,
                            null,
                            argumentError,
                            null
                        ),
                        historyCapacity
                    );
                    continue;
                }

                switch (metadata.ExecutionKind)
                {
                    case WButtonExecutionKind.Synchronous:
                        ExecuteSynchronous(metadata, target, state, arguments, historyCapacity);
                        cancellationSource?.Dispose();
                        break;

                    case WButtonExecutionKind.Task:
                        ExecuteTaskBasedInvocation(
                            metadata,
                            target,
                            state,
                            arguments,
                            historyCapacity,
                            cancellationSource
                        );
                        break;

                    case WButtonExecutionKind.ValueTask:
                        ExecuteValueTaskInvocation(
                            metadata,
                            target,
                            state,
                            arguments,
                            historyCapacity,
                            cancellationSource
                        );
                        break;

                    case WButtonExecutionKind.Enumerator:
                        // IEnumerator buttons need cancellation sources even without a token parameter so Cancel remains functional.
                        cancellationSource ??= new CancellationTokenSource();
                        ExecuteEnumeratorInvocation(
                            metadata,
                            target,
                            state,
                            arguments,
                            historyCapacity,
                            cancellationSource
                        );
                        break;
                }
            }

            RequestRepaint();
        }

        private static void ExecuteSynchronous(
            WButtonMethodMetadata metadata,
            UnityEngine.Object target,
            WButtonMethodState state,
            object[] arguments,
            int historyCapacity
        )
        {
            try
            {
                object result = metadata.Method.IsStatic
                    ? ReflectionHelpers.InvokeStaticMethod(metadata.Method, arguments)
                    : ReflectionHelpers.InvokeMethod(metadata.Method, target, arguments);

                if (!metadata.ReturnsVoid)
                {
                    WButtonResultEntry entry = CreateResultEntry(
                        WButtonResultKind.Success,
                        metadata.ReturnType,
                        result,
                        null
                    );
                    state.AddResult(entry, historyCapacity);
                }
                else
                {
                    state.AddResult(
                        new WButtonResultEntry(
                            WButtonResultKind.Success,
                            DateTime.UtcNow,
                            null,
                            "Completed successfully.",
                            null
                        ),
                        historyCapacity
                    );
                }
            }
            catch (TargetInvocationException tie)
            {
                HandleSynchronousException(state, historyCapacity, tie.InnerException ?? tie);
            }
            catch (Exception ex)
            {
                HandleSynchronousException(state, historyCapacity, ex);
            }
        }

        private static void ExecuteTaskBasedInvocation(
            WButtonMethodMetadata metadata,
            UnityEngine.Object target,
            WButtonMethodState state,
            object[] arguments,
            int historyCapacity,
            CancellationTokenSource cancellationSource
        )
        {
            Task task;
            try
            {
                object taskObject = metadata.Method.IsStatic
                    ? ReflectionHelpers.InvokeStaticMethod(metadata.Method, arguments)
                    : ReflectionHelpers.InvokeMethod(metadata.Method, target, arguments);
                task = taskObject as Task;
                if (task == null)
                {
                    state.AddResult(
                        new WButtonResultEntry(
                            WButtonResultKind.Error,
                            DateTime.UtcNow,
                            null,
                            "Method did not return a Task instance.",
                            null
                        ),
                        historyCapacity
                    );
                    cancellationSource?.Dispose();
                    return;
                }
            }
            catch (TargetInvocationException tie)
            {
                HandleSynchronousException(state, historyCapacity, tie.InnerException ?? tie);
                cancellationSource?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                HandleSynchronousException(state, historyCapacity, ex);
                cancellationSource?.Dispose();
                return;
            }

            WButtonInvocationHandle handle = new(
                state,
                target,
                metadata.ExecutionKind,
                cancellationSource
            );
            state.ActiveInvocation = handle;

            task.ContinueWith(
                t =>
                {
                    EnqueueOnMainThread(() =>
                    {
                        FinalizeTaskInvocation(metadata, state, handle, t, historyCapacity);
                    });
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default
            );
        }

        private static void ExecuteValueTaskInvocation(
            WButtonMethodMetadata metadata,
            UnityEngine.Object target,
            WButtonMethodState state,
            object[] arguments,
            int historyCapacity,
            CancellationTokenSource cancellationSource
        )
        {
            object returnedValueTask;
            try
            {
                returnedValueTask = metadata.Method.IsStatic
                    ? ReflectionHelpers.InvokeStaticMethod(metadata.Method, arguments)
                    : ReflectionHelpers.InvokeMethod(metadata.Method, target, arguments);
            }
            catch (TargetInvocationException tie)
            {
                HandleSynchronousException(state, historyCapacity, tie.InnerException ?? tie);
                cancellationSource?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                HandleSynchronousException(state, historyCapacity, ex);
                cancellationSource?.Dispose();
                return;
            }

            if (returnedValueTask == null)
            {
                state.AddResult(
                    new WButtonResultEntry(
                        WButtonResultKind.Error,
                        DateTime.UtcNow,
                        null,
                        "Method returned null instead of a ValueTask.",
                        null
                    ),
                    historyCapacity
                );
                cancellationSource?.Dispose();
                return;
            }

            MethodInfo asTaskMethod = metadata.ReturnType.GetMethod("AsTask", Type.EmptyTypes);
            if (asTaskMethod == null)
            {
                state.AddResult(
                    new WButtonResultEntry(
                        WButtonResultKind.Error,
                        DateTime.UtcNow,
                        null,
                        "Unable to access ValueTask.AsTask().",
                        null
                    ),
                    historyCapacity
                );
                cancellationSource?.Dispose();
                return;
            }

            Task task = (Task)asTaskMethod.Invoke(returnedValueTask, Array.Empty<object>());
            WButtonInvocationHandle handle = new(
                state,
                target,
                metadata.ExecutionKind,
                cancellationSource
            );
            state.ActiveInvocation = handle;

            task.ContinueWith(
                t =>
                {
                    EnqueueOnMainThread(() =>
                    {
                        FinalizeTaskInvocation(metadata, state, handle, t, historyCapacity);
                    });
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default
            );
        }

        private static void ExecuteEnumeratorInvocation(
            WButtonMethodMetadata metadata,
            UnityEngine.Object target,
            WButtonMethodState state,
            object[] arguments,
            int historyCapacity,
            CancellationTokenSource cancellationSource
        )
        {
            System.Collections.IEnumerator enumerator;
            try
            {
                object result = metadata.Method.IsStatic
                    ? ReflectionHelpers.InvokeStaticMethod(metadata.Method, arguments)
                    : ReflectionHelpers.InvokeMethod(metadata.Method, target, arguments);
                enumerator = result as System.Collections.IEnumerator;
                if (enumerator == null)
                {
                    state.AddResult(
                        new WButtonResultEntry(
                            WButtonResultKind.Error,
                            DateTime.UtcNow,
                            null,
                            "Method did not return an IEnumerator.",
                            null
                        ),
                        historyCapacity
                    );
                    cancellationSource?.Dispose();
                    return;
                }
            }
            catch (TargetInvocationException tie)
            {
                HandleSynchronousException(state, historyCapacity, tie.InnerException ?? tie);
                cancellationSource?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                HandleSynchronousException(state, historyCapacity, ex);
                cancellationSource?.Dispose();
                return;
            }

            WButtonInvocationHandle handle = new(
                state,
                target,
                metadata.ExecutionKind,
                cancellationSource
            );
            state.ActiveInvocation = handle;

            handle.CoroutineTicket = WButtonCoroutineScheduler.Schedule(
                enumerator,
                cancellationSource,
                () =>
                {
                    state.ActiveInvocation = null;
                    handle.MarkCompleted(null);
                    state.AddResult(
                        new WButtonResultEntry(
                            WButtonResultKind.Success,
                            DateTime.UtcNow,
                            null,
                            "Enumerator completed.",
                            null
                        ),
                        historyCapacity
                    );
                    RequestRepaint();
                },
                exception =>
                {
                    state.ActiveInvocation = null;
                    handle.MarkFaulted(exception);
                    state.AddResult(
                        new WButtonResultEntry(
                            WButtonResultKind.Error,
                            DateTime.UtcNow,
                            null,
                            exception.Message,
                            null,
                            exception
                        ),
                        historyCapacity
                    );
                    Debug.LogException(exception, target);
                    RequestRepaint();
                },
                () =>
                {
                    state.ActiveInvocation = null;
                    handle.MarkCancelled();
                    state.AddResult(
                        new WButtonResultEntry(
                            WButtonResultKind.Cancelled,
                            DateTime.UtcNow,
                            null,
                            "Enumerator cancelled.",
                            null
                        ),
                        historyCapacity
                    );
                    RequestRepaint();
                }
            );
        }

        private static void FinalizeTaskInvocation(
            WButtonMethodMetadata metadata,
            WButtonMethodState state,
            WButtonInvocationHandle handle,
            Task task,
            int historyCapacity
        )
        {
            state.ActiveInvocation = null;
            handle.CancellationTokenSource?.Dispose();

            if (task.IsCanceled || handle.Status == WButtonInvocationStatus.CancelRequested)
            {
                handle.MarkCancelled();
                state.AddResult(
                    new WButtonResultEntry(
                        WButtonResultKind.Cancelled,
                        DateTime.UtcNow,
                        null,
                        "Operation cancelled.",
                        null
                    ),
                    historyCapacity
                );
                RequestRepaint();
                return;
            }

            if (task.IsFaulted)
            {
                Exception exception = task.Exception?.GetBaseException() ?? task.Exception;
                handle.MarkFaulted(exception);
                state.AddResult(
                    new WButtonResultEntry(
                        WButtonResultKind.Error,
                        DateTime.UtcNow,
                        null,
                        exception?.Message ?? "Task faulted.",
                        null,
                        exception
                    ),
                    historyCapacity
                );
                if (exception != null)
                {
                    Debug.LogException(exception);
                }
                RequestRepaint();
                return;
            }

            object asyncResult = null;
            if (metadata.AsyncResultType != null)
            {
                asyncResult = ExtractTaskResult(task);
            }

            handle.MarkCompleted(asyncResult);
            WButtonResultEntry entry =
                metadata.AsyncResultType != null
                    ? CreateResultEntry(
                        WButtonResultKind.Success,
                        metadata.AsyncResultType,
                        asyncResult,
                        null
                    )
                    : new WButtonResultEntry(
                        WButtonResultKind.Success,
                        DateTime.UtcNow,
                        null,
                        "Completed successfully.",
                        null
                    );
            state.AddResult(entry, historyCapacity);
            RequestRepaint();
        }

        private static void HandleSynchronousException(
            WButtonMethodState state,
            int historyCapacity,
            Exception exception
        )
        {
            state.AddResult(
                new WButtonResultEntry(
                    WButtonResultKind.Error,
                    DateTime.UtcNow,
                    null,
                    exception.Message,
                    null,
                    exception
                ),
                historyCapacity
            );
            Debug.LogException(exception);
        }

        private static void CancelInvocation(WButtonMethodState state)
        {
            if (state == null)
            {
                return;
            }

            WButtonInvocationHandle handle = state.ActiveInvocation;
            if (handle == null)
            {
                return;
            }

            if (handle.ExecutionKind == WButtonExecutionKind.Enumerator)
            {
                WButtonCoroutineScheduler.Cancel(handle.CoroutineTicket);
            }

            handle.Cancel();
        }

        private static bool TryBuildArguments(
            WButtonMethodState state,
            WButtonMethodMetadata metadata,
            out object[] arguments,
            out CancellationTokenSource cancellationSource,
            out string error
        )
        {
            WButtonParameterMetadata[] parameters = metadata.Parameters;
            if (parameters == null || parameters.Length == 0)
            {
                arguments = Array.Empty<object>();
                cancellationSource = null;
                error = null;
                return true;
            }

            object[] builtArguments = new object[parameters.Length];
            CancellationTokenSource builtCancellationSource = null;

            for (int index = 0; index < parameters.Length; index++)
            {
                WButtonParameterMetadata parameter = parameters[index];
                WButtonParameterState parameterState = state.Parameters[index];

                if (parameter.IsCancellationToken)
                {
                    builtCancellationSource = new CancellationTokenSource();
                    builtArguments[index] = builtCancellationSource.Token;
                    continue;
                }

                object value = parameterState.CurrentValue;
                if (value == null && !string.IsNullOrWhiteSpace(parameterState.JsonFallback))
                {
                    if (
                        !TryDeserializeFromJson(
                            parameter.ParameterType,
                            parameterState.JsonFallback,
                            out value,
                            out string jsonError
                        )
                    )
                    {
                        builtCancellationSource?.Dispose();
                        arguments = null;
                        cancellationSource = null;
                        error = $"Failed to deserialize '{parameter.Name}': {jsonError}";
                        return false;
                    }
                }

                if (value == null)
                {
                    if (parameter.ParameterType.IsValueType && !parameter.IsOptional)
                    {
                        if (
                            !WButtonValueUtility.TryCreateInstance(
                                parameter.ParameterType,
                                out object createdValue
                            )
                        )
                        {
                            value = null;
                        }
                        else
                        {
                            value = createdValue;
                        }
                    }
                    else if (parameter.IsParamsArray)
                    {
                        Type elementType =
                            parameter.ParameterType.GetElementType() ?? typeof(object);
                        value = Array.CreateInstance(elementType, 0);
                    }
                }

                builtArguments[index] = WButtonValueUtility.CloneValue(value);
            }

            arguments = builtArguments;
            cancellationSource = builtCancellationSource;
            error = null;
            return true;
        }

        private static bool TryDeserializeFromJson(
            Type targetType,
            string json,
            out object value,
            out string error
        )
        {
            try
            {
                value = JsonSerializer.Deserialize(json, targetType, JsonOptions);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                value = null;
                error = ex.Message;
                return false;
            }
        }

        private static object ExtractTaskResult(Task task)
        {
            try
            {
                Type taskType = task.GetType();
                PropertyInfo resultProperty = taskType.GetProperty("Result");
                return resultProperty?.GetValue(task);
            }
            catch
            {
                return null;
            }
        }

        private static WButtonResultEntry CreateResultEntry(
            WButtonResultKind kind,
            Type valueType,
            object value,
            Exception exception
        )
        {
            UnityEngine.Object objectReference = value as UnityEngine.Object;
            string summary = BuildSummary(kind, valueType, value, exception, out bool logged);

            if (logged && exception != null)
            {
                Debug.LogException(exception);
            }

            return new WButtonResultEntry(
                kind,
                DateTime.UtcNow,
                value,
                summary,
                objectReference,
                exception
            );
        }

        private static string BuildSummary(
            WButtonResultKind kind,
            Type valueType,
            object value,
            Exception exception,
            out bool logged
        )
        {
            bool didLog = false;
            string summary;
            switch (kind)
            {
                case WButtonResultKind.Cancelled:
                {
                    summary = "Cancelled.";
                    break;
                }
                case WButtonResultKind.Error:
                {
                    didLog = true;
                    summary = exception?.Message ?? "Error.";
                    break;
                }
                default:
                {
                    if (value == null)
                    {
                        summary = "Completed.";
                    }
                    else if (value is UnityEngine.Object unityObject)
                    {
                        summary = unityObject != null ? unityObject.name : "None";
                    }
                    else if (value is string str)
                    {
                        summary = str;
                    }
                    else if (value is float or double or decimal)
                    {
                        summary = Convert.ToString(
                            value,
                            System.Globalization.CultureInfo.InvariantCulture
                        );
                    }
                    else if (value is IFormattable formattable)
                    {
                        summary = formattable.ToString(
                            null,
                            System.Globalization.CultureInfo.InvariantCulture
                        );
                    }
                    else
                    {
                        try
                        {
                            summary = JsonSerializer.Serialize(
                                value,
                                valueType ?? value.GetType(),
                                JsonOptions
                            );
                        }
                        catch (Exception ex)
                        {
                            didLog = true;
                            Debug.LogWarning(
                                $"[WButton] Failed to serialize result of type {(valueType ?? value.GetType()).Name}: {ex.Message}"
                            );
                            summary = "(see console)";
                        }
                    }

                    break;
                }
            }

            logged = didLog;
            return summary;
        }

        private static void RequestRepaint()
        {
            InternalEditorUtility.RepaintAllViews();
        }

        private static readonly object MainThreadQueueLock = new();
        private static readonly Queue<Action> MainThreadQueue = new();
        private static bool _isUpdateSubscribed;

        private static void EnqueueOnMainThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            lock (MainThreadQueueLock)
            {
                MainThreadQueue.Enqueue(action);
                if (!_isUpdateSubscribed)
                {
                    EditorApplication.update += ProcessMainThreadQueue;
                    _isUpdateSubscribed = true;
                }
            }
        }

        private static void ProcessMainThreadQueue()
        {
            Action[] actionsToProcess;
            lock (MainThreadQueueLock)
            {
                if (MainThreadQueue.Count == 0)
                {
                    EditorApplication.update -= ProcessMainThreadQueue;
                    _isUpdateSubscribed = false;
                    return;
                }

                actionsToProcess = MainThreadQueue.ToArray();
                MainThreadQueue.Clear();
            }

            foreach (Action action in actionsToProcess)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
#endif
}
