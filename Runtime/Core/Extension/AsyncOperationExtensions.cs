// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using UnityEngine;

    /// <summary>
    /// Extension methods for Unity AsyncOperation and Task/ValueTask/IEnumerator interoperability.
    /// </summary>
    public static class AsyncOperationExtensions
    {
        private static readonly ConcurrentDictionary<
            AsyncOperation,
            Action<AsyncOperation>
        > Handlers = new();
        private static readonly ConcurrentDictionary<AsyncOperation, Action> Continuations = new();

        private static readonly Action<AsyncOperation> CachedHandler = OnOperationCompleted;

        private static void OnOperationCompleted(AsyncOperation operation)
        {
            Handlers.Remove(operation, out Action<AsyncOperation> _);
            if (!Continuations.Remove(operation, out Action completionCondition))
            {
                return;
            }

            completionCondition?.Invoke();
        }

        /// <summary>
        /// Converts a Unity AsyncOperation to a Task.
        /// </summary>
        /// <param name="asyncOp">The AsyncOperation to convert.</param>
        /// <returns>A Task that completes when the AsyncOperation completes.</returns>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if asyncOp is null when awaiting.</para>
        /// <para>Thread safety: Must complete on Unity main thread. No Unity main thread requirement for initial call.</para>
        /// <para>Performance: O(1). Returns immediately if already complete.</para>
        /// <para>Allocations: Allocates Task state machine if operation not complete.</para>
        /// <para>Edge cases: Returns immediately if operation is already done.</para>
        /// </remarks>
        public static async Task AsTask(this AsyncOperation asyncOp)
        {
            if (asyncOp.isDone)
            {
                return;
            }

            await asyncOp;
        }

        /// <summary>
        /// Converts a Unity AsyncOperation to a ValueTask.
        /// </summary>
        /// <param name="asyncOp">The AsyncOperation to convert.</param>
        /// <returns>A ValueTask that completes when the AsyncOperation completes.</returns>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if asyncOp is null when awaiting.</para>
        /// <para>Thread safety: Must complete on Unity main thread. No Unity main thread requirement for initial call.</para>
        /// <para>Performance: O(1). Returns immediately if already complete.</para>
        /// <para>Allocations: No allocations if operation is already done, otherwise allocates ValueTask state machine.</para>
        /// <para>Edge cases: Returns immediately if operation is already done. Prefer over AsTask for completed operations.</para>
        /// </remarks>
        public static async ValueTask AsValueTask(this AsyncOperation asyncOp)
        {
            if (asyncOp.isDone)
            {
                return;
            }

            await asyncOp;
        }

#if !UNITY_2023_1_OR_NEWER
        /// <summary>
        /// Gets an awaiter for the AsyncOperation, enabling async/await syntax.
        /// Only available in Unity versions before 2023.1 (Unity 2023.1+ provides this natively).
        /// </summary>
        /// <param name="op">The AsyncOperation to get an awaiter for.</param>
        /// <returns>An AsyncOperationAwaiter for the operation.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if op is null (thrown by AsyncOperationAwaiter constructor).</para>
        /// <para>Thread safety: Thread-safe. Must complete on Unity main thread.</para>
        /// <para>Performance: O(1).</para>
        /// <para>Allocations: Allocates AsyncOperationAwaiter struct (stack allocation).</para>
        /// <para>Edge cases: Not available in Unity 2023.1+, where AsyncOperation implements INotifyCompletion natively.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when op is null.</exception>
        public static AsyncOperationAwaiter GetAwaiter(this AsyncOperation op)
        {
            return new AsyncOperationAwaiter(op);
        }
#endif

        /// <summary>
        /// Executes a continuation action after a ValueTask completes.
        /// </summary>
        /// <param name="task">The task to await.</param>
        /// <param name="continuation">The action to execute after the task completes. Can be null.</param>
        /// <returns>A ValueTask that completes after the continuation executes.</returns>
        /// <remarks>
        /// <para>Null handling: If continuation is null, no action is taken after the task completes.</para>
        /// <para>Thread safety: Continuation executes on the same context as the task completion. No Unity main thread requirement unless task requires it.</para>
        /// <para>Performance: O(1) overhead for continuation invocation.</para>
        /// <para>Allocations: Allocates async state machine.</para>
        /// <para>Edge cases: Null continuation is allowed and does nothing.</para>
        /// </remarks>
        public static async ValueTask WithContinuation(this ValueTask task, Action continuation)
        {
            await task;
            continuation?.Invoke();
        }

        /// <summary>
        /// Executes a continuation function that transforms the result after a ValueTask completes.
        /// </summary>
        /// <typeparam name="TResult">The type of the task result.</typeparam>
        /// <param name="task">The task to await.</param>
        /// <param name="continuation">The function to execute on the result. Can be null, in which case the original result is returned.</param>
        /// <returns>A ValueTask containing the transformed result, or the original result if continuation is null.</returns>
        /// <remarks>
        /// <para>Null handling: If continuation is null, returns the original task result unchanged.</para>
        /// <para>Thread safety: Continuation executes on the same context as the task completion. No Unity main thread requirement unless task requires it.</para>
        /// <para>Performance: O(1) overhead for continuation invocation.</para>
        /// <para>Allocations: Allocates async state machine.</para>
        /// <para>Edge cases: Null continuation is allowed and returns original result.</para>
        /// </remarks>
        public static async ValueTask<TResult> WithContinuation<TResult>(
            this ValueTask<TResult> task,
            Func<TResult, TResult> continuation
        )
        {
            TResult result = await task;
            return continuation != null ? continuation(result) : result;
        }

        /// <summary>
        /// Executes a continuation action with the result after a ValueTask completes.
        /// </summary>
        /// <typeparam name="TResult">The type of the task result.</typeparam>
        /// <param name="task">The task to await.</param>
        /// <param name="continuation">The action to execute with the result. Can be null.</param>
        /// <returns>A ValueTask that completes after the continuation executes.</returns>
        /// <remarks>
        /// <para>Null handling: If continuation is null, no action is taken after the task completes.</para>
        /// <para>Thread safety: Continuation executes on the same context as the task completion. No Unity main thread requirement unless task requires it.</para>
        /// <para>Performance: O(1) overhead for continuation invocation.</para>
        /// <para>Allocations: Allocates async state machine.</para>
        /// <para>Edge cases: Null continuation is allowed and does nothing.</para>
        /// </remarks>
        public static async ValueTask WithContinuation<TResult>(
            this ValueTask<TResult> task,
            Action<TResult> continuation
        )
        {
            TResult result = await task;
            continuation?.Invoke(result);
        }

        // Task/ValueTask to IEnumerator conversions
        /// <summary>
        /// Converts a Task to a Unity coroutine (IEnumerator).
        /// </summary>
        /// <param name="task">The task to convert.</param>
        /// <returns>An IEnumerator that can be used with StartCoroutine.</returns>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if task is null when checking IsCompleted.</para>
        /// <para>Thread safety: Must be iterated on Unity main thread. No Unity main thread requirement for task execution.</para>
        /// <para>Performance: Yields every frame until task completes. O(1) per iteration.</para>
        /// <para>Allocations: Allocates iterator state machine.</para>
        /// <para>Edge cases: Throws task.Exception if task is faulted. Blocks coroutine execution until task completes.</para>
        /// </remarks>
        /// <exception cref="Exception">Throws the task's exception if the task is faulted.</exception>
        public static IEnumerator AsCoroutine(this Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception;
            }
        }

        /// <summary>
        /// Converts a Task with a result to a Unity coroutine (IEnumerator), optionally invoking a callback with the result.
        /// </summary>
        /// <typeparam name="T">The type of the task result.</typeparam>
        /// <param name="task">The task to convert.</param>
        /// <param name="onResult">Optional callback to receive the task result. Can be null.</param>
        /// <returns>An IEnumerator that can be used with StartCoroutine.</returns>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if task is null. onResult can be null.</para>
        /// <para>Thread safety: Must be iterated on Unity main thread. No Unity main thread requirement for task execution.</para>
        /// <para>Performance: Yields every frame until task completes. O(1) per iteration.</para>
        /// <para>Allocations: Allocates iterator state machine.</para>
        /// <para>Edge cases: Throws task.Exception if task is faulted. onResult is invoked with result after successful completion.</para>
        /// </remarks>
        /// <exception cref="Exception">Throws the task's exception if the task is faulted.</exception>
        public static IEnumerator AsCoroutine<T>(this Task<T> task, Action<T> onResult = null)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception;
            }

            onResult?.Invoke(task.Result);
        }

        /// <summary>
        /// Converts a Task returning a tuple with two elements to a Unity coroutine (IEnumerator), optionally invoking a callback with the tuple elements.
        /// </summary>
        /// <typeparam name="T1">The type of the first tuple element.</typeparam>
        /// <typeparam name="T2">The type of the second tuple element.</typeparam>
        /// <param name="task">The task to convert.</param>
        /// <param name="onResult">Optional callback invoked with the tuple elements. Can be null.</param>
        /// <returns>An IEnumerator that can be used with StartCoroutine.</returns>
        /// <remarks>
        /// <para>Null handling: Task cannot be null. onResult can be null.</para>
        /// <para>Thread safety: Must be iterated on Unity main thread. No Unity main thread requirement for task execution.</para>
        /// <para>Performance: Delegates work to the generic Task overload. O(1) per iteration.</para>
        /// <para>Allocations: Iterator allocation only.</para>
        /// <para>Edge cases: Returns immediately if task is complete. Propagates task exceptions.</para>
        /// </remarks>
        /// <exception cref="Exception">Throws the task's exception if the task is faulted.</exception>
        public static IEnumerator AsCoroutine<T1, T2>(
            this Task<(T1 First, T2 Second)> task,
            Action<T1, T2> onResult = null
        )
        {
            return task.AsCoroutine(tuple =>
            {
                if (onResult != null)
                {
                    onResult(tuple.First, tuple.Second);
                }
            });
        }

        /// <summary>
        /// Converts a Task returning a tuple with three elements to a Unity coroutine (IEnumerator), optionally invoking a callback with the tuple elements.
        /// </summary>
        /// <typeparam name="T1">The type of the first tuple element.</typeparam>
        /// <typeparam name="T2">The type of the second tuple element.</typeparam>
        /// <typeparam name="T3">The type of the third tuple element.</typeparam>
        /// <param name="task">The task to convert.</param>
        /// <param name="onResult">Optional callback invoked with the tuple elements. Can be null.</param>
        /// <returns>An IEnumerator that can be used with StartCoroutine.</returns>
        /// <remarks>
        /// <para>Null handling: Task cannot be null. onResult can be null.</para>
        /// <para>Thread safety: Must be iterated on Unity main thread. No Unity main thread requirement for task execution.</para>
        /// <para>Performance: Delegates work to the generic Task overload. O(1) per iteration.</para>
        /// <para>Allocations: Iterator allocation only.</para>
        /// <para>Edge cases: Returns immediately if task is complete. Propagates task exceptions.</para>
        /// </remarks>
        /// <exception cref="Exception">Throws the task's exception if the task is faulted.</exception>
        public static IEnumerator AsCoroutine<T1, T2, T3>(
            this Task<(T1 First, T2 Second, T3 Third)> task,
            Action<T1, T2, T3> onResult = null
        )
        {
            return task.AsCoroutine(tuple =>
            {
                if (onResult != null)
                {
                    onResult(tuple.First, tuple.Second, tuple.Third);
                }
            });
        }

        /// <summary>
        /// Converts a ValueTask to a Unity coroutine (IEnumerator).
        /// </summary>
        /// <param name="task">The ValueTask to convert.</param>
        /// <returns>An IEnumerator that can be used with StartCoroutine.</returns>
        /// <remarks>
        /// <para>Null handling: ValueTask is a value type and cannot be null.</para>
        /// <para>Thread safety: Must be iterated on Unity main thread. No Unity main thread requirement for task execution.</para>
        /// <para>Performance: No yielding if already complete. Otherwise yields every frame. O(1) per iteration.</para>
        /// <para>Allocations: No allocations if already complete. Otherwise allocates iterator and converts to Task internally.</para>
        /// <para>Edge cases: Returns immediately if task is already completed. Throws task exception if faulted.</para>
        /// </remarks>
        /// <exception cref="Exception">Throws the task's exception if the task is faulted.</exception>
        public static IEnumerator AsCoroutine(this ValueTask task)
        {
            if (task.IsCompleted)
            {
                if (task.IsFaulted)
                {
                    throw task.AsTask().Exception;
                }
                yield break;
            }

            Task innerTask = task.AsTask();
            while (!innerTask.IsCompleted)
            {
                yield return null;
            }

            if (innerTask.IsFaulted)
            {
                throw innerTask.Exception;
            }
        }

        /// <summary>
        /// Converts a ValueTask with a result to a Unity coroutine (IEnumerator), optionally invoking a callback with the result.
        /// </summary>
        /// <typeparam name="T">The type of the task result.</typeparam>
        /// <param name="task">The ValueTask to convert.</param>
        /// <param name="onResult">Optional callback to receive the task result. Can be null.</param>
        /// <returns>An IEnumerator that can be used with StartCoroutine.</returns>
        /// <remarks>
        /// <para>Null handling: ValueTask is a value type and cannot be null. onResult can be null.</para>
        /// <para>Thread safety: Must be iterated on Unity main thread. No Unity main thread requirement for task execution.</para>
        /// <para>Performance: No yielding if already complete. Otherwise yields every frame. O(1) per iteration.</para>
        /// <para>Allocations: No allocations if already complete. Otherwise allocates iterator and converts to Task internally.</para>
        /// <para>Edge cases: Returns immediately if task is already completed. onResult invoked with result after successful completion.</para>
        /// </remarks>
        /// <exception cref="Exception">Throws the task's exception if the task is faulted.</exception>
        public static IEnumerator AsCoroutine<T>(this ValueTask<T> task, Action<T> onResult = null)
        {
            if (task.IsCompleted)
            {
                if (task.IsFaulted)
                {
                    throw task.AsTask().Exception;
                }
                onResult?.Invoke(task.Result);
                yield break;
            }

            Task<T> innerTask = task.AsTask();
            while (!innerTask.IsCompleted)
            {
                yield return null;
            }

            if (innerTask.IsFaulted)
            {
                throw innerTask.Exception;
            }

            onResult?.Invoke(innerTask.Result);
        }

        /// <summary>
        /// Converts a ValueTask returning a tuple with two elements to a Unity coroutine (IEnumerator), optionally invoking a callback with the tuple elements.
        /// </summary>
        /// <typeparam name="T1">The type of the first tuple element.</typeparam>
        /// <typeparam name="T2">The type of the second tuple element.</typeparam>
        /// <param name="task">The ValueTask to convert.</param>
        /// <param name="onResult">Optional callback invoked with the tuple elements. Can be null.</param>
        /// <returns>An IEnumerator that can be used with StartCoroutine.</returns>
        /// <remarks>
        /// <para>Null handling: ValueTask is a value type and cannot be null. onResult can be null.</para>
        /// <para>Thread safety: Must be iterated on Unity main thread. No Unity main thread requirement for task execution.</para>
        /// <para>Performance: Delegates work to the generic ValueTask overload. O(1) per iteration.</para>
        /// <para>Allocations: Iterator allocation only.</para>
        /// <para>Edge cases: Returns immediately if task is complete. Propagates task exceptions.</para>
        /// </remarks>
        /// <exception cref="Exception">Throws the task's exception if the task is faulted.</exception>
        public static IEnumerator AsCoroutine<T1, T2>(
            this ValueTask<(T1 First, T2 Second)> task,
            Action<T1, T2> onResult = null
        )
        {
            return task.AsCoroutine(tuple =>
            {
                if (onResult != null)
                {
                    onResult(tuple.First, tuple.Second);
                }
            });
        }

        /// <summary>
        /// Converts a ValueTask returning a tuple with three elements to a Unity coroutine (IEnumerator), optionally invoking a callback with the tuple elements.
        /// </summary>
        /// <typeparam name="T1">The type of the first tuple element.</typeparam>
        /// <typeparam name="T2">The type of the second tuple element.</typeparam>
        /// <typeparam name="T3">The type of the third tuple element.</typeparam>
        /// <param name="task">The ValueTask to convert.</param>
        /// <param name="onResult">Optional callback invoked with the tuple elements. Can be null.</param>
        /// <returns>An IEnumerator that can be used with StartCoroutine.</returns>
        /// <remarks>
        /// <para>Null handling: ValueTask is a value type and cannot be null. onResult can be null.</para>
        /// <para>Thread safety: Must be iterated on Unity main thread. No Unity main thread requirement for task execution.</para>
        /// <para>Performance: Delegates work to the generic ValueTask overload. O(1) per iteration.</para>
        /// <para>Allocations: Iterator allocation only.</para>
        /// <para>Edge cases: Returns immediately if task is complete. Propagates task exceptions.</para>
        /// </remarks>
        /// <exception cref="Exception">Throws the task's exception if the task is faulted.</exception>
        public static IEnumerator AsCoroutine<T1, T2, T3>(
            this ValueTask<(T1 First, T2 Second, T3 Third)> task,
            Action<T1, T2, T3> onResult = null
        )
        {
            return task.AsCoroutine(tuple =>
            {
                if (onResult != null)
                {
                    onResult(tuple.First, tuple.Second, tuple.Third);
                }
            });
        }

        // IEnumerator to Task/ValueTask conversions
        /// <summary>
        /// Converts a Unity coroutine (IEnumerator) to a Task.
        /// </summary>
        /// <param name="coroutine">The coroutine to convert.</param>
        /// <returns>A Task that completes when the coroutine finishes.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if coroutine is null.</para>
        /// <para>Thread safety: Coroutine must be iterated on the thread where it's executed. Typically requires Unity main thread.</para>
        /// <para>Performance: O(n) where n is the number of iterations. Yields control between iterations.</para>
        /// <para>Allocations: Allocates async state machine.</para>
        /// <para>Edge cases: Does not use Unity's StartCoroutine - manually iterates the enumerator. Task.Yield() returns control to caller between iterations.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when coroutine is null.</exception>
        public static async Task AsTask(this IEnumerator coroutine)
        {
            if (coroutine == null)
            {
                throw new ArgumentNullException(nameof(coroutine));
            }

            while (coroutine.MoveNext())
            {
                await Task.Yield();
            }
        }

        /// <summary>
        /// Converts a Unity coroutine (IEnumerator) to a ValueTask.
        /// </summary>
        /// <param name="coroutine">The coroutine to convert.</param>
        /// <returns>A ValueTask that completes when the coroutine finishes.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if coroutine is null.</para>
        /// <para>Thread safety: Coroutine must be iterated on the thread where it's executed. Typically requires Unity main thread.</para>
        /// <para>Performance: O(n) where n is the number of iterations. Yields control between iterations.</para>
        /// <para>Allocations: Allocates async state machine.</para>
        /// <para>Edge cases: Does not use Unity's StartCoroutine - manually iterates the enumerator. Task.Yield() returns control to caller between iterations.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when coroutine is null.</exception>
        public static async ValueTask AsValueTask(this IEnumerator coroutine)
        {
            if (coroutine == null)
            {
                throw new ArgumentNullException(nameof(coroutine));
            }

            while (coroutine.MoveNext())
            {
                await Task.Yield();
            }
        }

        /// <summary>
        /// Provides an awaiter for Unity AsyncOperation objects, enabling async/await syntax.
        /// </summary>
        /// <remarks>
        /// <para>This struct is used internally to enable async/await on AsyncOperation.</para>
        /// <para>Thread safety: Thread-safe using concurrent dictionaries for handler storage. Must complete on Unity main thread.</para>
        /// <para>Performance: O(1) for completion checks. Allocations occur for continuation storage in dictionaries.</para>
        /// <para>Allocations: Allocates dictionary entries for tracking completions. Cleaned up on completion.</para>
        /// </remarks>
        public readonly struct AsyncOperationAwaiter : INotifyCompletion
        {
            private readonly AsyncOperation _operation;

            /// <summary>
            /// Initializes a new instance of the AsyncOperationAwaiter struct.
            /// </summary>
            /// <param name="operation">The AsyncOperation to await.</param>
            /// <exception cref="ArgumentNullException">Thrown when operation is null.</exception>
            public AsyncOperationAwaiter(AsyncOperation operation)
            {
                _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            }

            /// <summary>
            /// Gets a value indicating whether the async operation has completed.
            /// </summary>
            public bool IsCompleted => _operation.isDone;

            /// <summary>
            /// Schedules the continuation action to be invoked when the operation completes.
            /// </summary>
            /// <param name="continuation">The action to invoke when the operation completes.</param>
            /// <remarks>
            /// An operation that finishes between <see cref="IsCompleted"/> reading false and the
            /// subscription resumes the awaiting state machine synchronously, on the calling stack,
            /// rather than on a later frame.
            /// </remarks>
            public void OnCompleted(Action continuation)
            {
                if (continuation == null)
                {
                    return;
                }

                /*
                    Every await of the same operation registers here. Storing through the indexer made
                    the second registration overwrite the first, so the first awaiter never resumed;
                    combining leaves every awaiter's continuation to run.

                    This registration must stay ahead of the subscription below. Unity's completed
                    adder is hand-written: when isDone already reads true it invokes the handler
                    inside the += and never stores it. Subscribing first would therefore drain an
                    empty Continuations and only then add a continuation nothing would ever run --
                    the hang #700 was filed about, which the current order already prevents.
                */
                Continuations.AddOrUpdate(
                    _operation,
                    static (_, added) => added,
                    static (_, existing, added) => existing + added,
                    continuation
                );

                Action<AsyncOperation> handler = CachedHandler;
                if (!Handlers.TryAdd(_operation, handler))
                {
                    return;
                }

                _operation.completed += handler;
            }

            /// <summary>
            /// Gets the result of the async operation. Since AsyncOperation has no return value, this is a no-op.
            /// </summary>
            public void GetResult() { }
        }
    }
}
