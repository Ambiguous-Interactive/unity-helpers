// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Threading
{
#if !SINGLE_THREADED
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Runs enqueued work one item at a time on a single background worker, in enqueue order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Disposal discards queued work.</b> <see cref="Dispose"/> and <see cref="DisposeAsync"/>
    /// cancel the worker rather than draining it, so anything enqueued but not yet started is
    /// dropped. The <c>await</c> inside <see cref="DisposeAsync"/> waits only for the item already
    /// in flight. That is the right behavior for work that can simply be redone, such as
    /// generation passes, and the wrong behavior for durable work such as persistence writes:
    /// call <see cref="DrainAsync"/> first when queued items must run.
    /// </para>
    /// <para>
    /// The window is narrow enough to hide in testing. Work enqueued a millisecond or more before
    /// disposal almost always completes; work enqueued immediately before it almost never does.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code><![CDATA[
    /// // Work that can be redone: drop whatever is still queued.
    /// pool.Dispose();
    ///
    /// // Durable work: let the queue run down first.
    /// await pool.DrainAsync();
    /// await pool.DisposeAsync();
    /// ]]></code>
    /// </example>
    public sealed class SingleThreadedThreadPool : IDisposable
    {
        /*
            Teardown-only poll interval. Draining waits on the worker finishing items rather than on
            a signal, which keeps the enqueue path free of extra synchronization for a path that
            runs once per pool.
        */
        private static readonly TimeSpan DrainPollInterval = TimeSpan.FromMilliseconds(1);

        /// <summary>
        /// Exceptions thrown by work items, in the order they were caught.
        /// </summary>
        public ConcurrentQueue<Exception> Exceptions => _exceptions;

        /// <summary>
        /// Number of work items queued, plus the one currently executing.
        /// </summary>
        public int Count => _work.Count + (_isWorking ? 1 : 0);

        /// <summary>
        /// Whether the pool still accepts work. Turns off permanently on the first
        /// <see cref="DrainAsync"/> or disposal.
        /// </summary>
        public bool IsAcceptingWork => _acceptingWork && !_disposed;

        private volatile bool _active = true;
        private volatile bool _acceptingWork = true;
        private volatile bool _isWorking;
        private volatile bool _disposed;

        private readonly Task _workerTask;
        private readonly ConcurrentQueue<WorkItem> _work;
        private readonly SemaphoreSlim _workAvailable;
        private readonly ConcurrentQueue<Exception> _exceptions;
        private readonly TimeSpan _noWorkWaitTime;
        private readonly CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// Starts the worker immediately.
        /// </summary>
        /// <param name="runInBackground">
        /// When <see langword="true"/> the worker runs on a thread pool thread; otherwise it gets
        /// its own long-running thread.
        /// </param>
        /// <param name="noWorkWaitTime">
        /// How long the idle worker waits for a signal before re-checking the queue. Defaults to
        /// one second.
        /// </param>
        public SingleThreadedThreadPool(
            bool runInBackground = true,
            TimeSpan? noWorkWaitTime = null
        )
        {
            _work = new ConcurrentQueue<WorkItem>();
            _exceptions = new ConcurrentQueue<Exception>();
            _workAvailable = new SemaphoreSlim(0);
            _noWorkWaitTime = noWorkWaitTime ?? TimeSpan.FromSeconds(1);
            _cancellationTokenSource = new CancellationTokenSource();

            _workerTask = runInBackground
                ? Task.Run(DoWorkAsync)
                : Task.Factory.StartNew(DoWorkAsync, TaskCreationOptions.LongRunning).Unwrap();
        }

        /// <summary>
        /// Queues an action to run on the worker. Ignored once the pool stops accepting work.
        /// </summary>
        /// <param name="work">The action to run.</param>
        public void Enqueue(Action work)
        {
            if (_disposed || !_acceptingWork)
            {
                return;
            }

            _work.Enqueue(WorkItem.FromAction(work));
            Signal();
        }

        /// <summary>
        /// Queues an asynchronous action to run on the worker, which awaits it before starting the
        /// next item. Ignored once the pool stops accepting work.
        /// </summary>
        /// <param name="work">The asynchronous action to run.</param>
        public void Enqueue(Func<Task> work)
        {
            if (_disposed || !_acceptingWork)
            {
                return;
            }

            _work.Enqueue(WorkItem.FromTask(work));
            Signal();
        }

        /// <summary>
        /// Queues an asynchronous action to run on the worker, which awaits it before starting the
        /// next item. Ignored once the pool stops accepting work.
        /// </summary>
        /// <param name="work">The asynchronous action to run.</param>
        public void Enqueue(Func<ValueTask> work)
        {
            if (_disposed || !_acceptingWork)
            {
                return;
            }

            _work.Enqueue(WorkItem.FromValueTask(work));
            Signal();
        }

        private void Signal()
        {
            try
            {
                _workAvailable.Release();
            }
            catch
            {
                // Swallow
            }
        }

        /// <summary>
        /// Stops accepting new work and waits for everything already queued to finish running.
        /// </summary>
        /// <param name="cancellationToken">Abandons the wait; queued work is left in the queue.</param>
        /// <returns>
        /// <see langword="true"/> when the queue ran down and the worker went idle;
        /// <see langword="false"/> when the wait was abandoned, the pool was already disposed, or
        /// the worker had already stopped with items still queued.
        /// </returns>
        /// <remarks>
        /// Closes the pool to new work permanently, then returns once the queue is empty and no
        /// item is executing. The guarantee is that every item the calling thread enqueued before
        /// this call has run; a concurrent producer racing this call is not covered, so stop those
        /// producers first. Disposal is still required afterwards -- draining does not stop the
        /// worker or release its handles.
        /// </remarks>
        /// <example>
        /// <code><![CDATA[
        /// if (!await pool.DrainAsync())
        /// {
        ///     Debug.LogWarning("Pool did not drain; writing final state on this thread.");
        /// }
        ///
        /// await pool.DisposeAsync();
        /// ]]></code>
        /// </example>
        public async ValueTask<bool> DrainAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                return false;
            }

            _acceptingWork = false;
            Signal();

            while (!_work.IsEmpty || _isWorking)
            {
                if (cancellationToken.IsCancellationRequested || _disposed)
                {
                    return false;
                }

                // A stopped worker will never drain what is left, so waiting for it would hang.
                if (_workerTask.IsCompleted && !_isWorking)
                {
                    return _work.IsEmpty;
                }

                try
                {
                    await Task.Delay(DrainPollInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Cancels the worker and releases its handles, discarding any work still queued.
        /// </summary>
        /// <remarks>
        /// Waits only for the item already in flight. Call <see cref="DrainAsync"/> first when
        /// queued items must run.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _acceptingWork = false;
            _active = false;
            _disposed = true;

            _cancellationTokenSource.Cancel();
            Signal();
            try
            {
                await _workerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch
            {
                // Swallow other exceptions during disposal
            }

            _cancellationTokenSource?.Dispose();
            _workAvailable?.Dispose();

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Cancels the worker and releases its handles, discarding any work still queued.
        /// </summary>
        /// <remarks>
        /// Blocks the calling thread until the in-flight item finishes. This is safe for work
        /// items that do not resume on a captured synchronization context; the pool itself uses
        /// <c>ConfigureAwait(false)</c> throughout, so it posts nothing back to Unity's main
        /// thread. A work item whose own continuations capture the main thread's context would
        /// deadlock here, because <c>OnDestroy</c> runs on that thread. Prefer
        /// <see cref="DisposeAsync"/> when the work items await anything context-bound.
        /// </remarks>
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        private async Task DoWorkAsync()
        {
            CancellationToken cancellationToken = _cancellationTokenSource.Token;

            while (_active && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    /*
                        Claim busy off a non-empty queue rather than off a successful dequeue.
                        Marking after TryDequeue leaves a window where the item has left the queue
                        and is not yet in flight, and DrainAsync -- which waits for an empty queue
                        and an idle worker -- would report a drain that had not happened. Ordering
                        it this way means one of the two conditions always holds for a live item.
                    */
                    if (!_work.IsEmpty)
                    {
                        _isWorking = true;
                        try
                        {
                            if (_work.TryDequeue(out WorkItem workItem))
                            {
                                await workItem.ExecuteAsync().ConfigureAwait(false);
                            }
                        }
                        catch (Exception e)
                        {
                            _exceptions.Enqueue(e);
                        }
                        finally
                        {
                            _isWorking = false;
                        }
                    }
                    else
                    {
                        try
                        {
                            await _workAvailable
                                .WaitAsync(_noWorkWaitTime, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private enum WorkItemType
        {
            Unknown = 0,
            Action = 1,
            Task = 2,
            ValueTask = 3,
        }

        private readonly struct WorkItem
        {
            private readonly WorkItemType _type;
            private readonly Action _action;
            private readonly Func<Task> _taskFunc;
            private readonly Func<ValueTask> _valueTaskFunc;

            private WorkItem(
                WorkItemType type,
                Action action = null,
                Func<Task> taskFunc = null,
                Func<ValueTask> valueTaskFunc = null
            )
            {
                _type = type;
                _action = action;
                _taskFunc = taskFunc;
                _valueTaskFunc = valueTaskFunc;
            }

            public static WorkItem FromAction(Action action) =>
                new(WorkItemType.Action, action: action);

            public static WorkItem FromTask(Func<Task> taskFunc) =>
                new(WorkItemType.Task, taskFunc: taskFunc);

            public static WorkItem FromValueTask(Func<ValueTask> valueTaskFunc) =>
                new(WorkItemType.ValueTask, valueTaskFunc: valueTaskFunc);

            public ValueTask ExecuteAsync()
            {
                return _type switch
                {
                    WorkItemType.Action => ExecuteAction(),
                    WorkItemType.Task => ExecuteTask(),
                    WorkItemType.ValueTask => _valueTaskFunc(),
                    _ => default,
                };
            }

            private ValueTask ExecuteAction()
            {
                _action();
                return default;
            }

            private async ValueTask ExecuteTask()
            {
                await _taskFunc().ConfigureAwait(false);
            }
        }
    }
#endif
}
