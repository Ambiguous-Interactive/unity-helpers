// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using WritePayload = WallstopStudios.UnityHelpers.Core.OneOf.FastOneOf<string, byte[]>;
#if !SINGLE_THREADED
    using System.Threading;
#endif

    /// <summary>Coalesces queued file writes and flushes the latest pending payload for each path.</summary>
    /// <remarks>
    /// Accepted submissions retain immutable text or a byte copy but do not prove persistence. Flush before a durable
    /// boundary; disposal stops submissions and flushes accepted work. A write already in progress
    /// finishes before a newer payload for the same path. Failures remain observable until a later
    /// successful write to that path. DurableFile's replacement and crash-durability limits apply.
    /// </remarks>
    public sealed class LatestWriteFile : IDisposable
    {
        private static readonly StringComparer PathComparer = CreatePathComparer();

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
        >(PathComparer);
#if !SINGLE_THREADED
        private readonly WaitCallback _drainCallback;
#endif
        private Dictionary<string, WritePayload> _pending = new Dictionary<string, WritePayload>(
            PathComparer
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
#endif
        }

        private static StringComparer CreatePathComparer()
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            return isWindows || isMac ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
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

        /// <summary>Synchronously flushes the current batch and reports unresolved write failures.</summary>
        public bool Flush()
        {
            return Flush(out _);
        }

        /// <summary>Synchronously finishes the pending batch and reports whether every failed path has recovered.</summary>
        /// <remarks>Concurrent submissions after the batch is captured may remain pending. Failure callbacks may reenter the writer.</remarks>
        public bool Flush(out Exception error)
        {
            List<KeyValuePair<string, Exception>> notifications = null;
            try
            {
                lock (_writeGate)
                {
                    Dictionary<string, WritePayload> batch;
                    lock (_stateGate)
                    {
                        if (_pending.Count == 0)
                        {
                            error = FirstFailureCore();
                            return error == null;
                        }
                        Dictionary<string, WritePayload> next = new Dictionary<
                            string,
                            WritePayload
                        >(PathComparer);
                        batch = _pending;
                        _pending = next;
                    }
                    foreach (KeyValuePair<string, WritePayload> entry in batch)
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
                _pending[path] = contents;
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
                WritePayload contents = default;
                Exception failure;
                lock (_writeGate)
                {
                    lock (_stateGate)
                    {
                        foreach (KeyValuePair<string, WritePayload> entry in _pending)
                        {
                            path = entry.Key;
                            contents = entry.Value;
                            break;
                        }
                        if (path == null)
                        {
                            _workerScheduled = false;
                            return;
                        }
                        _pending.Remove(path);
                    }
                    failure = WriteOwned(path, contents);
                }
                if (failure != null)
                {
                    NotifyFailure(path, failure);
                }
            }
        }

#if !SINGLE_THREADED
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

        private Exception WriteOwned(string path, WritePayload contents)
        {
            Exception error;
            try
            {
                bool success;
                if (contents.TryGetT0(out string text))
                {
                    success = DurableFile.TryWriteAllText(path, text, out error);
                }
                else
                {
                    byte[] bytes = contents.AsT1;
                    success = DurableFile.TryWriteAllBytes(path, bytes, out error);
                }
                if (success)
                {
                    error = null;
                }
                else if (error == null)
                {
                    error = new IOException("The durable write failed.");
                }
            }
            catch (Exception exception)
            {
                error = exception;
            }
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
    }
}
