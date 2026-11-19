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

        internal static (LatestWriteFilePathOrder Order, long Submission) Reserve(
            string path,
            bool replacesPending
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
                return (order, ++order._lastSubmission);
            }
        }

        private static StringComparer CreatePathComparer()
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            return isWindows || isMac ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        }

        private static Exception Write(string path, WritePayload contents)
        {
            try
            {
                bool success;
                Exception error;
                if (contents.TryGetT0(out string text))
                {
                    success = DurableFile.TryWriteAllText(path, text, out error);
                }
                else
                {
                    byte[] bytes = contents.AsT1;
                    success = DurableFile.TryWriteAllBytes(path, bytes, out error);
                }

                return success ? null : error ?? new IOException("The durable write failed.");
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        internal Exception Publish(string path, long submission, WritePayload contents)
        {
            try
            {
                lock (_publicationGate)
                {
                    if (submission < _lastAttempt)
                    {
                        return _lastFailure;
                    }
                    _lastAttempt = submission;
                    _lastFailure = Write(path, contents);
                    return _lastFailure;
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
    }
}
