// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || !UNITY_5_3_OR_NEWER
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using Microsoft.Win32.SafeHandles;
    using HostRuntimeInformation = System.Runtime.InteropServices.RuntimeInformation;

    internal sealed class DurableFileWindowsBackup
    {
        private const int MaximumPendingBackups = 32;
        private const int FileNotFound = 2;
        private const int PathNotFound = 3;
        private const int FileDispositionInfo = 4;
        private const int FileIdInfo = 18;
        private const uint ReadAttributes = 0x80;
        private const uint DeleteAccess = 0x10000;
        private const uint OpenReparsePoint = 0x00200000;
        private const uint SupportsOpenByFileId = 0x01000000;

        private static readonly List<DurableFileWindowsBackup> Pending = new();
        private static readonly object PendingLock = new();

        private readonly string _destination;
        private readonly string _backup;
        private readonly SafeFileHandle _previous;

        private DurableFileWindowsBackup(string destination, string backup, SafeFileHandle previous)
        {
            _destination = destination;
            _backup = backup;
            _previous = previous;
        }

        internal static void ReplaceOnce(string staged, string destination, string backup)
        {
            SafeFileHandle previous = OpenPrevious(destination);
            try
            {
                File.Replace(staged, destination, backup);
                if (CanTrackBackup(previous) && !TryReclaim(backup, previous))
                {
                    Remember(new DurableFileWindowsBackup(destination, backup, previous));
                    previous = null;
                }
            }
            finally
            {
                previous?.Dispose();
            }
        }

        internal static void RetryPending(string path)
        {
            List<DurableFileWindowsBackup> matches = null;
            lock (PendingLock)
            {
                if (Pending.Count == 0)
                {
                    return;
                }

                string destination = Path.GetFullPath(path);
                for (int index = Pending.Count - 1; 0 <= index; --index)
                {
                    DurableFileWindowsBackup item = Pending[index];
                    if (
                        string.Equals(
                            item._destination,
                            destination,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        matches ??= new List<DurableFileWindowsBackup>();
                        matches.Add(item);
                        Pending.RemoveAt(index);
                    }
                }
            }

            if (matches == null)
            {
                return;
            }

            for (int index = matches.Count - 1; 0 <= index; --index)
            {
                DurableFileWindowsBackup item = matches[index];
                if (TryReclaim(item._backup, item._previous))
                {
                    item._previous.Dispose();
                }
                else
                {
                    Remember(item);
                }
            }
        }

        private static SafeFileHandle OpenPrevious(string path)
        {
            if (!HostRuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return null;
            }

            try
            {
                SafeFileHandle previous = Open(
                    path,
                    ReadAttributes,
                    FileShare.ReadWrite | FileShare.Delete
                );
                if (!previous.IsInvalid)
                {
                    return previous;
                }

                /*
                    CreateFile reports ordinary denials through an invalid handle. Publication
                    still runs through ReplaceFile when we cannot pin the previous identity.
                */
                previous.Dispose();
                return null;
            }
            catch (Exception failure) when (NativeApiUnavailable(failure))
            {
                return null;
            }
        }

        private static bool CanTrackBackup(SafeFileHandle previous)
        {
            if (previous == null || previous.IsInvalid)
            {
                return false;
            }

            try
            {
                return GetVolumeInformationByHandle(
                        previous,
                        IntPtr.Zero,
                        0,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        out uint flags,
                        IntPtr.Zero,
                        0
                    )
                    && (flags & SupportsOpenByFileId) != 0;
            }
            catch (Exception failure) when (NativeApiUnavailable(failure))
            {
                return false;
            }
        }

        private static bool NativeApiUnavailable(Exception failure)
        {
            return failure
                is DllNotFoundException
                    or EntryPointNotFoundException
                    or PlatformNotSupportedException;
        }

        private static void Remember(DurableFileWindowsBackup pending)
        {
            DurableFileWindowsBackup evicted = null;
            lock (PendingLock)
            {
                if (Pending.Count == MaximumPendingBackups)
                {
                    evicted = Pending[0];
                    Pending.RemoveAt(0);
                }
                Pending.Add(pending);
            }
            evicted?._previous.Dispose();
        }

        private static bool TryReclaim(string backupPath, SafeFileHandle previous)
        {
            try
            {
                // The old handle prevents ID reuse. The delete handle prevents a rename during the identity check.
                using SafeFileHandle backup = Open(
                    backupPath,
                    ReadAttributes | DeleteAccess,
                    FileShare.ReadWrite
                );
                if (backup.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    return error is FileNotFound or PathNotFound;
                }

                uint identitySize = (uint)Marshal.SizeOf<FileIdentity>();
                if (
                    !GetFileInformationByHandleEx(
                        previous,
                        FileIdInfo,
                        out FileIdentity expected,
                        identitySize
                    )
                    || !GetFileInformationByHandleEx(
                        backup,
                        FileIdInfo,
                        out FileIdentity actual,
                        identitySize
                    )
                )
                {
                    return false;
                }

                if (
                    (expected.IdLow == 0 && expected.IdHigh == 0)
                    || expected.Volume != actual.Volume
                    || expected.IdLow != actual.IdLow
                    || expected.IdHigh != actual.IdHigh
                )
                {
                    return true;
                }

                byte delete = 1;
                return SetFileInformationByHandle(
                    backup,
                    FileDispositionInfo,
                    ref delete,
                    sizeof(byte)
                );
            }
            catch (Exception failure) when (NativeApiUnavailable(failure))
            {
                return false;
            }
        }

        private static SafeFileHandle Open(string fullPath, uint access, FileShare share)
        {
            return CreateFile(
                DurableFile.GetWindowsNativePath(fullPath),
                access,
                share,
                IntPtr.Zero,
                FileMode.Open,
                OpenReparsePoint,
                IntPtr.Zero
            );
        }

        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateFileW",
            CharSet = CharSet.Unicode,
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern SafeFileHandle CreateFile(
            string path,
            uint access,
            FileShare share,
            IntPtr security,
            FileMode disposition,
            uint flags,
            IntPtr template
        );

        [DllImport(
            "kernel32.dll",
            EntryPoint = "GetVolumeInformationByHandleW",
            ExactSpelling = true
        )]
        private static extern bool GetVolumeInformationByHandle(
            SafeFileHandle file,
            IntPtr volumeName,
            uint volumeNameLength,
            IntPtr volumeSerialNumber,
            IntPtr maximumComponentLength,
            out uint flags,
            IntPtr fileSystemName,
            uint fileSystemNameLength
        );

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle file,
            int informationClass,
            out FileIdentity identity,
            uint size
        );

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern bool SetFileInformationByHandle(
            SafeFileHandle file,
            int informationClass,
            ref byte information,
            uint size
        );

        [StructLayout(LayoutKind.Sequential)]
        private struct FileIdentity
        {
            public ulong Volume;
            public ulong IdLow;
            public ulong IdHigh;
        }
    }
#endif
}
