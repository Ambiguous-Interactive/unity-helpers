// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.IO;

    internal static class File
    {
        internal static Exception ReplacementFailure { get; set; }
        internal static int ReplacementFailuresRemaining { get; set; } = -1;
        internal static bool RejectMove { get; set; }
        internal static Action<string, string> BeforeFailedReplacement { get; set; }
        internal static Action<string, string> BeforeMove { get; set; }
        internal static Action<string, string> AfterMove { get; set; }
        internal static string ObservedDestination { get; set; }
        internal static int ReplacementCalls { get; private set; }
        internal static int MoveCalls { get; private set; }
        internal static int DestinationDeleteCalls { get; private set; }

        internal static void Reset()
        {
            ReplacementFailure = null;
            ReplacementFailuresRemaining = -1;
            RejectMove = false;
            BeforeFailedReplacement = null;
            BeforeMove = null;
            AfterMove = null;
            ObservedDestination = null;
            ReplacementCalls = 0;
            MoveCalls = 0;
            DestinationDeleteCalls = 0;
        }

        internal static bool Exists(string path)
        {
            return System.IO.File.Exists(path);
        }

        internal static FileAttributes GetAttributes(string path)
        {
            return System.IO.File.GetAttributes(path);
        }

        internal static byte[] ReadAllBytes(string path)
        {
            return System.IO.File.ReadAllBytes(path);
        }

        internal static string ReadAllText(string path)
        {
            return System.IO.File.ReadAllText(path);
        }

        internal static void Delete(string path)
        {
            if (string.Equals(path, ObservedDestination, StringComparison.Ordinal))
            {
                ++DestinationDeleteCalls;
            }

            System.IO.File.Delete(path);
        }

        internal static void Move(string source, string destination)
        {
            ++MoveCalls;
            if (RejectMove)
            {
                throw new IOException("Simulated failure publishing staged contents.");
            }

            BeforeMove?.Invoke(source, destination);
            System.IO.File.Move(source, destination);
            AfterMove?.Invoke(source, destination);
        }

        internal static void Replace(
            string source,
            string destination,
            string destinationBackupFileName
        )
        {
            ++ReplacementCalls;
            if (ReplacementFailure != null && ReplacementFailuresRemaining != 0)
            {
                if (0 < ReplacementFailuresRemaining)
                {
                    --ReplacementFailuresRemaining;
                }
                BeforeFailedReplacement?.Invoke(destination, destinationBackupFileName);
                throw ReplacementFailure;
            }

            System.IO.File.Replace(source, destination, destinationBackupFileName);
        }
    }
}
