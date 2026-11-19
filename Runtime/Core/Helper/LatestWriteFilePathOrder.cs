// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using WritePayload = WallstopStudios.UnityHelpers.Core.OneOf.FastOneOf<string, byte[]>;

    internal sealed class LatestWriteFilePathOrder
    {
        internal static readonly StringComparer PathComparer = CreatePathComparer();

        private static readonly object RegistryGate = new();
        private static readonly Dictionary<string, LatestWriteFilePathOrder> Registry = new(
            PathComparer
        );

        private readonly object _publicationGate = new();
        private long _lastSubmission;
        private long _lastAttempt;
        private Exception _lastFailure;
        private int _outstanding;

        private LatestWriteFilePathOrder() { }

        internal static PendingWrite Reserve(
            string path,
            bool replacesPending,
            WritePayload contents
        )
        {
            lock (RegistryGate)
            {
                if (!Registry.TryGetValue(path, out LatestWriteFilePathOrder order))
                {
                    order = new LatestWriteFilePathOrder();
                    Registry.Add(path, order);
                }

                // A coalesced payload keeps the existing reservation until publication.
                if (!replacesPending)
                {
                    ++order._outstanding;
                }
                return new PendingWrite(order, ++order._lastSubmission, contents);
            }
        }

        private static StringComparer CreatePathComparer()
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            return isWindows || isMac ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        }

        private static bool TryWrite(string path, WritePayload contents, out Exception error)
        {
            try
            {
                bool success;
                Exception failure;
                if (contents.TryGetT0(out string text))
                {
                    success = DurableFile.TryWriteAllText(path, text, out failure);
                }
                else
                {
                    byte[] bytes = contents.AsT1;
                    success = DurableFile.TryWriteAllBytes(path, bytes, out failure);
                }

                if (success)
                {
                    error = null;
                    return true;
                }
                error = failure ?? new IOException("The durable write failed.");
                return false;
            }
            catch (Exception exception)
            {
                error = exception;
                return false;
            }
        }

        private bool TryPublish(
            string path,
            long submission,
            WritePayload contents,
            out Exception error
        )
        {
            try
            {
                lock (_publicationGate)
                {
                    if (submission < _lastAttempt)
                    {
                        error = _lastFailure;
                        return error == null;
                    }
                    _lastAttempt = submission;
                    bool success = TryWrite(path, contents, out _lastFailure);
                    error = _lastFailure;
                    return success;
                }
            }
            finally
            {
                lock (RegistryGate)
                {
                    if (--_outstanding == 0)
                    {
                        Registry.Remove(path);
                    }
                }
            }
        }

        internal readonly struct PendingWrite
        {
            private readonly LatestWriteFilePathOrder _order;
            private readonly long _submission;
            private readonly WritePayload _contents;

            internal PendingWrite(
                LatestWriteFilePathOrder order,
                long submission,
                WritePayload contents
            )
            {
                _order = order;
                _submission = submission;
                _contents = contents;
            }

            internal bool TryPublish(string path, out Exception error)
            {
                return _order.TryPublish(path, _submission, _contents, out error);
            }
        }
    }
}
