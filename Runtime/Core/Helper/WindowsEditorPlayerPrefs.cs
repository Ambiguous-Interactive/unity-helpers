// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
#if UNITY_EDITOR_WIN
    using System;
    using System.Runtime.InteropServices;
    using UnityEngine;

    internal static class WindowsEditorPlayerPrefs
    {
        private const int CurrentUserRegistryHive = unchecked((int)0x80000001);
        private const int RegistryQueryValue = 1;
        private const int RegistrySuccess = 0;
        private const int RegistryFileNotFound = 2;
        private const int RegistryPathNotFound = 3;

        internal static bool CanQuery()
        {
            string path = string.Concat(
                @"Software\Unity\UnityEditor\",
                Application.companyName,
                @"\",
                Application.productName
            );
            IntPtr currentUser = new(CurrentUserRegistryHive);
            int status = RegOpenKeyExW(currentUser, path, 0, RegistryQueryValue, out IntPtr key);
            if (status == RegistrySuccess)
            {
                return RegCloseKey(key) == RegistrySuccess;
            }

            return status == RegistryFileNotFound || status == RegistryPathNotFound;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int RegOpenKeyExW(
            IntPtr key,
            string subKey,
            int options,
            int desiredAccess,
            out IntPtr openedKey
        );

        [DllImport("advapi32.dll", ExactSpelling = true)]
        private static extern int RegCloseKey(IntPtr key);
    }
#endif
}
