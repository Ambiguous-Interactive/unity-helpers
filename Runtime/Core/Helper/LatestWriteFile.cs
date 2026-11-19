// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using WritePayload = WallstopStudios.UnityHelpers.Core.OneOf.FastOneOf<string, byte[]>;
#if !SINGLE_THREADED
    using System.Diagnostics;
    using System.Threading.Tasks;
#endif

    /// <summary>Coalesces queued file writes and flushes the latest pending payload for each path.</summary>
    /// <remarks>
    /// Flush before a durable boundary. Disposal stops submissions and flushes accepted work.
    /// Writers share publication order for each normalized path in the process. An older submission
    /// flushed after a newer attempt takes that attempt's result without writing. A newer submission
    /// waiting in a manual batch does not delay an older writer's flush.
    /// DurableFile's replacement and crash-durability limits apply.
    /// </remarks>
    public sealed class LatestWriteFile : IDisposable
    {
#if !SINGLE_THREADED
        private static readonly TimeSpan MaximumWait = TimeSpan.FromMilliseconds(int.MaxValue);
#endif

        /// <summary>Gets the number of distinct queued paths, excluding writes already in progress.</summary>
        public int PendingPathCount
        {
            get
            {
                lock (_stateGate)
                {
                    return _pending.Count;
                }
            }
        }

        /// <summary>Gets one unresolved write failure, or null after every failed path has recovered.</summary>
        public Exception Failure
        {
            get
            {
                lock (_stateGate)
                {
                    return FirstFailureCore();
                }
            }
        }

        private readonly object _stateGate = new object();
        private readonly object _writeGate = new object();
        private readonly bool _writeInBackground;
        private readonly Action<string, Exception> _onFailure;
        private readonly Dictionary<string, Exception> _failures = new Dictionary<
            string,
            Exception
        >(LatestWriteFilePathOrder.PathComparer);
#if !SINGLE_THREADED
        private readonly WaitCallback _drainCallback;
        private readonly Func<Exception> _flushCallback;
        private Task<Exception> _queuedFlush;
#endif
        private Dictionary<string, PendingWrite> _pending = new(
            LatestWriteFilePathOrder.PathComparer
        );
        private bool _accepting = true;
        private bool _workerScheduled;

        /// <summary>Creates a writer with background processing or explicit flush-only batching.</summary>
        /// <param name="writeInBackground">Process queued writes automatically; SINGLE_THREADED builds flush these submissions synchronously.</param>
        /// <param name="onFailure">Receives the normalized path and failure outside the writer's locks, on the thread attempting the write.</param>
        public LatestWriteFile(
            bool writeInBackground = true,
            Action<string, Exception> onFailure = null
        )
        {
            _writeInBackground = writeInBackground;
            _onFailure = onFailure;
#if !SINGLE_THREADED
            _drainCallback = DrainPendingWrites;
            _flushCallback = FlushInBackground;
#endif
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A destination path is required.", nameof(path));
            }
            return Path.GetFullPath(path);
        }

        /// <summary>Queues immutable text for UTF-8 writing, replacing the pending payload for the same normalized path.</summary>
        /// <remarks>A true result means accepted, not persisted. Null text is an empty file. Encoding is deferred until writing.</remarks>
        public bool TryEnqueueText(string path, string contents, out Exception error)
        {
            try
            {
                string normalized = NormalizePath(path);
                WritePayload payload = contents ?? string.Empty;
                return TryEnqueueOwned(normalized, payload, out error);
            }
            catch (Exception exception)
            {
                error = exception;
                return false;
            }
        }

        /// <summary>Queues a copy of the bytes, replacing the pending payload for the same normalized path.</summary>
        /// <remarks>A true result means accepted, not persisted. Null bytes are an empty file.</remarks>
        public bool TryEnqueueBytes(string path, byte[] contents, out Exception error)
        {
            try
            {
                string normalized = NormalizePath(path);
                byte[] bytes =
                    contents == null || contents.Length == 0
                        ? Array.Empty<byte>()
                        : (byte[])contents.Clone();
                WritePayload payload = bytes;
                return TryEnqueueOwned(normalized, payload, out error);
            }
            catch (Exception exception)
            {
                error = exception;
                return false;
            }
        }

        /// <summary>Queues the supplied byte array without copying it.</summary>
        /// <remarks>
        /// On acceptance, the caller transfers ownership and must stop modifying or recycling the array.
        /// Null bytes are an empty file. Flush reports persistence or a later submission's result.
        /// </remarks>
        public bool TryEnqueueOwnedBytes(string path, byte[] contents, out Exception error)
        {
            try
            {
                string normalized = NormalizePath(path);
                WritePayload payload = contents ?? Array.Empty<byte>();
                return TryEnqueueOwned(normalized, payload, out error);
            }
            catch (Exception exception)
            {
                error = exception;
                return false;
            }
        }

        /// <summary>Synchronously flushes the current batch and reports unresolved write failures.</summary>
        public bool Flush()
        {
            return Flush(out _);
        }

        /// <summary>Synchronously finishes the pending batch and reports whether every failed path has recovered.</summary>
        /// <remarks>Concurrent submissions after the batch is captured may remain pending. Failure callbacks may reenter the writer.</remarks>
        public bool Flush(out Exception error)
        {
            return FlushBatch(queued: false, out error);
        }

        /// <summary>Starts a flush and waits up to the specified limit for its result.</summary>
        /// <remarks>
        /// A timeout returns false with a <see cref="TimeoutException"/>. The flush continues in the background.
        /// Failure callbacks may run on a worker thread even for a manual writer.
        /// <see cref="Timeout.InfiniteTimeSpan"/> performs a synchronous flush.
        /// Finite limits require a threaded build.
        /// </remarks>
        public bool Flush(TimeSpan limit, out Exception error)
        {
            if (limit == Timeout.InfiniteTimeSpan)
            {
                return Flush(out error);
            }
            if (limit < TimeSpan.Zero)
            {
                error = new ArgumentOutOfRangeException(nameof(limit));
                return false;
            }
#if SINGLE_THREADED
            error = new PlatformNotSupportedException(
                "A timed flush requires a background thread."
            );
            return false;
#else
            try
            {
                long started = Stopwatch.GetTimestamp();
                Task<Exception> completion;
                lock (_stateGate)
                {
                    _queuedFlush ??= Task.Run(_flushCallback);
                    completion = _queuedFlush;
                }
                TimeSpan remaining = limit - StopwatchTimestamp.Since(started);
                while (MaximumWait < remaining && !completion.Wait(MaximumWait))
                {
                    remaining = limit - StopwatchTimestamp.Since(started);
                }
                TimeSpan wait = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
                if (!completion.IsCompleted && !completion.Wait(wait))
                {
                    error = new TimeoutException("The file flush is still running.");
                    return false;
                }
                error = completion.GetAwaiter().GetResult();
                return error == null;
            }
            catch (Exception exception)
            {
                error = exception;
                return false;
            }
#endif
        }

        /// <summary>Stops accepting writes and synchronously flushes accepted work.</summary>
        /// <remarks>Inspect Failure or call Flush to observe a failed final write; disposal does not throw.</remarks>
        public void Dispose()
        {
            lock (_stateGate)
            {
                _accepting = false;
            }
            Flush(out _);
        }

        private bool FlushBatch(bool queued, out Exception error)
        {
            List<KeyValuePair<string, Exception>> notifications = null;
            try
            {
                lock (_writeGate)
                {
                    Dictionary<string, PendingWrite> batch;
                    lock (_stateGate)
                    {
#if !SINGLE_THREADED
                        if (queued)
                        {
                            _queuedFlush = null;
                        }
#endif
                        if (_pending.Count == 0)
                        {
                            error = FirstFailureCore();
                            return error == null;
                        }
                        Dictionary<string, PendingWrite> next = new(
                            LatestWriteFilePathOrder.PathComparer
                        );
                        batch = _pending;
                        _pending = next;
                    }
                    foreach (KeyValuePair<string, PendingWrite> entry in batch)
                    {
                        Exception failure = WriteOwned(entry.Key, entry.Value);
                        if (failure != null)
                        {
                            notifications ??= new List<KeyValuePair<string, Exception>>();
                            KeyValuePair<string, Exception> notification = new KeyValuePair<
                                string,
                                Exception
                            >(entry.Key, failure);
                            notifications.Add(notification);
                        }
                    }
                }
                if (notifications != null)
                {
                    foreach (KeyValuePair<string, Exception> notification in notifications)
                    {
                        NotifyFailure(notification.Key, notification.Value);
                    }
                }
                lock (_stateGate)
                {
                    error = FirstFailureCore();
                    return error == null;
                }
            }
            catch (Exception exception)
            {
                error = exception;
                return false;
            }
        }

        private bool TryEnqueueOwned(string path, WritePayload contents, out Exception error)
        {
            bool schedule = false;
            lock (_stateGate)
            {
                if (!_accepting)
                {
                    error = new ObjectDisposedException(nameof(LatestWriteFile));
                    return false;
                }
                bool replacesPending = _pending.ContainsKey(path);
                (LatestWriteFilePathOrder order, long submission) =
                    LatestWriteFilePathOrder.Reserve(path, replacesPending);
                PendingWrite write = new(order, submission, contents);
                _pending[path] = write;
                if (_writeInBackground && !_workerScheduled)
                {
                    _workerScheduled = true;
                    schedule = true;
                }
            }
            if (schedule)
            {
#if SINGLE_THREADED
                DrainPendingWrites(null);
#else
                try
                {
                    if (!ThreadPool.QueueUserWorkItem(_drainCallback))
                    {
                        Exception schedulingError = new InvalidOperationException(
                            "The background write could not be scheduled."
                        );
                        SchedulingFailed(path, schedulingError);
                    }
                }
                catch (Exception exception)
                {
                    SchedulingFailed(path, exception);
                }
#endif
            }
            error = null;
            return true;
        }

        private void DrainPendingWrites(object state)
        {
            while (true)
            {
                string path = null;
                PendingWrite write = default;
                Exception failure;
                lock (_writeGate)
                {
                    lock (_stateGate)
                    {
                        foreach (KeyValuePair<string, PendingWrite> entry in _pending)
                        {
                            path = entry.Key;
                            write = entry.Value;
                            break;
                        }
                        if (path == null)
                        {
                            _workerScheduled = false;
                            return;
                        }
                        _pending.Remove(path);
                    }
                    failure = WriteOwned(path, write);
                }
                if (failure != null)
                {
                    NotifyFailure(path, failure);
                }
            }
        }

#if !SINGLE_THREADED
        private Exception FlushInBackground()
        {
            FlushBatch(queued: true, out Exception error);
            return error;
        }

        private void SchedulingFailed(string path, Exception error)
        {
            lock (_stateGate)
            {
                _workerScheduled = false;
                _failures[path] = error;
            }
            NotifyFailure(path, error);
        }
#endif

        private Exception WriteOwned(string path, PendingWrite write)
        {
            Exception error = write.Order.Publish(path, write.Submission, write.Contents);
            lock (_stateGate)
            {
                if (error == null)
                {
                    _failures.Remove(path);
                }
                else
                {
                    _failures[path] = error;
                }
            }
            return error;
        }

        private void NotifyFailure(string path, Exception error)
        {
            try
            {
                _onFailure?.Invoke(path, error);
            }
            catch (Exception callbackError)
            {
                lock (_stateGate)
                {
                    if (
                        _failures.TryGetValue(path, out Exception current)
                        && ReferenceEquals(current, error)
                    )
                    {
                        _failures[path] = new AggregateException(error, callbackError);
                    }
                }
            }
        }

        private Exception FirstFailureCore()
        {
            foreach (KeyValuePair<string, Exception> failure in _failures)
            {
                return failure.Value;
            }
            return null;
        }

        private readonly struct PendingWrite
        {
            internal readonly LatestWriteFilePathOrder Order;
            internal readonly long Submission;
            internal readonly WritePayload Contents;

            internal PendingWrite(
                LatestWriteFilePathOrder order,
                long submission,
                WritePayload contents
            )
            {
                Order = order;
                Submission = submission;
                Contents = contents;
            }
        }
    }
}
