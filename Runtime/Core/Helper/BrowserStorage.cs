// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Globalization;
    using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR && !WALLSTOP_BROWSER_STORAGE_PLAYER_PREFS
    using System.Runtime.InteropServices;
#endif

    /// <summary>Stores scoped strings in browser local storage or PlayerPrefs.</summary>
    /// <remarks>Call from Unity's main thread. Storage is not encrypted.</remarks>
    public sealed class BrowserStorage
    {
        /// <summary>Whether the scope is nonblank and can cross the UTF-8 bridge unchanged.</summary>
        public bool IsConfigured => _keyPrefix != null;

        private readonly string _keyPrefix;

        /// <summary>Creates a store whose keys are isolated by the supplied scope.</summary>
        public BrowserStorage(string scope)
        {
            if (!string.IsNullOrWhiteSpace(scope) && IsSupportedText(scope))
            {
                _keyPrefix =
                    "WallstopBrowserStorage:"
                    + scope.Length.ToString(CultureInfo.InvariantCulture)
                    + ":"
                    + scope
                    + ":";
            }
        }

        /// <summary>Reads a present string, including an empty value; otherwise returns false.</summary>
        public bool TryGetString(string key, out string value)
        {
            if (!TryGetStorageKey(key, out string storageKey))
            {
                value = null;
                return false;
            }

            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR && !WALLSTOP_BROWSER_STORAGE_PLAYER_PREFS
                string storedValue = WUHBrowserStorageGetString(storageKey);
                if (storedValue == null)
                {
                    value = null;
                    return false;
                }
#else
                if (!PlayerPrefs.HasKey(storageKey))
                {
                    value = null;
                    return false;
                }
                string storedValue = PlayerPrefs.GetString(storageKey);
#endif
                if (!IsSupportedText(storedValue))
                {
                    value = null;
                    return false;
                }
                value = storedValue;
                return true;
            }
            catch (Exception)
            {
                value = null;
                return false;
            }
        }

        /// <summary>Writes a string and returns whether the backend accepted it.</summary>
        public bool TrySetString(string key, string value)
        {
            if (!IsSupportedText(value) || !TryGetStorageKey(key, out string storageKey))
            {
                return false;
            }

            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR && !WALLSTOP_BROWSER_STORAGE_PLAYER_PREFS
                return WUHBrowserStorageSetString(storageKey, value) == 1;
#else
                PlayerPrefs.SetString(storageKey, value);
                PlayerPrefs.Save();
                return true;
#endif
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Deletes only this scoped key; an absent key counts as success.</summary>
        public bool TryDeleteKey(string key)
        {
            if (!TryGetStorageKey(key, out string storageKey))
            {
                return false;
            }

            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR && !WALLSTOP_BROWSER_STORAGE_PLAYER_PREFS
                return WUHBrowserStorageDeleteKey(storageKey) == 1;
#else
                PlayerPrefs.DeleteKey(storageKey);
                PlayerPrefs.Save();
                return true;
#endif
            }
            catch (Exception)
            {
                return false;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR && !WALLSTOP_BROWSER_STORAGE_PLAYER_PREFS
        [DllImport("__Internal", ExactSpelling = true)]
        private static extern string WUHBrowserStorageGetString(string key);

        [DllImport("__Internal", ExactSpelling = true)]
        private static extern int WUHBrowserStorageSetString(string key, string value);

        [DllImport("__Internal", ExactSpelling = true)]
        private static extern int WUHBrowserStorageDeleteKey(string key);
#endif

        private static bool IsSupportedText(string value)
        {
            if (value == null)
            {
                return false;
            }

            int length = value.Length;
            for (int index = 0; index < length; ++index)
            {
                char character = value[index];
                if (character == '\0' || char.IsLowSurrogate(character))
                {
                    return false;
                }
                if (char.IsHighSurrogate(character))
                {
                    if (length <= ++index || !char.IsLowSurrogate(value[index]))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private bool TryGetStorageKey(string key, out string storageKey)
        {
            if (_keyPrefix == null || string.IsNullOrEmpty(key) || !IsSupportedText(key))
            {
                storageKey = null;
                return false;
            }

            storageKey = _keyPrefix + key;
            return true;
        }
    }
}
