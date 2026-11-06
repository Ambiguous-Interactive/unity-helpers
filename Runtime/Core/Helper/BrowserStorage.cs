// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Globalization;
    using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
    using System.Runtime.InteropServices;
#endif

    /// <summary>Stores scoped or literal strings in browser local storage or PlayerPrefs.</summary>
    /// <remarks>Call from Unity's main thread. Storage is not encrypted.</remarks>
    public sealed class BrowserStorage
    {
        private const string StoragePrefix = "WallstopBrowserStorage:";

        /// <summary>Gets a store that uses keys unchanged to read existing raw-key saves.</summary>
        /// <remarks>Shares the backend's key namespace with other callers; it does not migrate or delete existing data.</remarks>
        public static BrowserStorage Unscoped { get; } = new();

        /// <summary>Whether the store has a valid scope or uses literal keys.</summary>
        public bool IsConfigured => _keyPrefix != null;

        private readonly string _keyPrefix;

        /// <summary>Creates a store whose keys are isolated by the supplied scope.</summary>
        public BrowserStorage(string scope)
        {
            if (!string.IsNullOrWhiteSpace(scope) && IsSupportedText(scope))
            {
                string scopeLength = scope.Length.ToString(CultureInfo.InvariantCulture);
                _keyPrefix = string.Concat(StoragePrefix, scopeLength, ":", scope);
            }
        }

        private BrowserStorage()
        {
            _keyPrefix = string.Empty;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal", ExactSpelling = true)]
        private static extern int WUHBrowserStorageHasKey(string key);

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

        /// <summary>Checks key existence and reports backend failure separately from absence.</summary>
        /// <remarks>Returns false with exists=false for invalid keys, an invalid scope, or unavailable storage.</remarks>
        public bool TryHasKey(string key, out bool exists)
        {
            if (!TryGetStorageKey(key, out string storageKey))
            {
                exists = false;
                return false;
            }
            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                int status = WUHBrowserStorageHasKey(storageKey);
                if (status < 0 || 1 < status)
                {
                    exists = false;
                    return false;
                }
                bool present = status == 1;
#else
                bool present = PlayerPrefs.HasKey(storageKey);
#endif
                exists = present;
                return true;
            }
            catch (Exception)
            {
                exists = false;
                return false;
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
#if UNITY_WEBGL && !UNITY_EDITOR
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
#if UNITY_WEBGL && !UNITY_EDITOR
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

        /// <summary>Deletes this store's key; an absent key counts as success.</summary>
        public bool TryDeleteKey(string key)
        {
            if (!TryGetStorageKey(key, out string storageKey))
            {
                return false;
            }

            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
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

        private bool TryGetStorageKey(string key, out string storageKey)
        {
            if (_keyPrefix == null || string.IsNullOrEmpty(key) || !IsSupportedText(key))
            {
                storageKey = null;
                return false;
            }

            storageKey = _keyPrefix.Length == 0 ? key : string.Concat(_keyPrefix, ":", key);
            return true;
        }
    }
}
