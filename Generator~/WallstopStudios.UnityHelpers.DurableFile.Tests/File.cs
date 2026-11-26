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
        internal static Action<string> BeforeExists { get; set; }
        internal static Action<string> AfterMissingTextRead { get; set; }
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
            BeforeExists = null;
            AfterMissingTextRead = null;
            ObservedDestination = null;
            ReplacementCalls = 0;
            MoveCalls = 0;
            DestinationDeleteCalls = 0;
        }

        internal static bool Exists(string path)
        {
            BeforeExists?.Invoke(path);
            return System.IO.File.Exists(path);
        }

        internal static FileAttributes GetAttributes(string path)
        {
            try
            {
                return System.IO.File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                AfterMissingTextRead?.Invoke(path);
                throw;
            }
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

            System.IO.File.Move(source, destination);
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
