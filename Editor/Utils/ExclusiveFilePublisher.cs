// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Publishes a staged editor file without replacing a concurrently created destination.
    /// </summary>
    internal static class ExclusiveFilePublisher
    {
        private const int UnixNameAlreadyExists = 17;

        internal static Action SimulatePostPublishCleanupFailureForTests;

        /// <summary>
        /// Returns false when another entry occupies the destination. Publish failures throw;
        /// cleanup failures return a warning after the output is already published.
        /// </summary>
        internal static bool TryPublishNewFile(
            string stagedPath,
            string destinationPath,
            out Exception cleanupWarning
        )
        {
            Exception warning = null;
            bool removeStagedFile = true;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    File.Move(stagedPath, destinationPath);
                    removeStagedFile = false;
                }
                catch (IOException) when (File.Exists(destinationPath))
                {
                    cleanupWarning = null;
                    return false;
                }
            }
            else
            {
                bool linked;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    linked = CreateHardLinkMac(stagedPath, destinationPath) == 0;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    linked = CreateHardLinkLinux(stagedPath, destinationPath) == 0;
                }
                else
                {
                    throw new PlatformNotSupportedException(
                        "Exclusive file publication needs hard links."
                    );
                }

                if (!linked)
                {
                    int nativeError = Marshal.GetLastWin32Error();
                    if (nativeError == UnixNameAlreadyExists)
                    {
                        cleanupWarning = null;
                        return false;
                    }

                    throw new IOException(
                        $"Could not publish '{destinationPath}' with a hard link (OS error {nativeError}). Check that the output filesystem supports hard links and allows writing."
                    );
                }
            }

            if (removeStagedFile)
            {
                try
                {
                    SimulatePostPublishCleanupFailureForTests?.Invoke();
                    File.Delete(stagedPath);
                }
                catch (Exception cleanupError)
                {
                    warning = new IOException(
                        $"Published '{destinationPath}' but could not remove staged file '{stagedPath}'.",
                        cleanupError
                    );
                }
            }

            cleanupWarning = warning;
            return true;
        }

        [DllImport("libc", EntryPoint = "link", ExactSpelling = true, SetLastError = true)]
        private static extern int CreateHardLinkLinux(string stagedPath, string destinationPath);

        [DllImport(
            "libSystem.B.dylib",
            EntryPoint = "link",
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern int CreateHardLinkMac(string stagedPath, string destinationPath);
    }
#endif
}
