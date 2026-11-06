// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace NestedOnlyConsumer
{
    using System;
    using System.Runtime.InteropServices;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>Exercises actual browser storage without seeding through the package API.</summary>
    public static class BrowserStorageCases
    {
        /// <summary>Verifies raw saves, existence results and blocked-backend preservation.</summary>
        public static void Run()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string key = nameof(BrowserStorageCases) + Guid.NewGuid().ToString("N");
            const string retained = "retained 雪 😀";
            BrowserStorage raw = BrowserStorage.Unscoped;
            BrowserStorage scoped = new(key);
            ConsumerCases.Require(
                BrowserConsumerStorageSeedRaw(key, retained) == 1,
                "Could not seed raw browser key"
            );
            try
            {
                ConsumerCases.Require(
                    raw.TryHasKey(key, out bool present) && present,
                    "Raw browser key is missing"
                );
                ConsumerCases.Require(
                    raw.TryGetString(key, out string value) && value == retained,
                    "Raw browser save changed"
                );
                ConsumerCases.Require(
                    scoped.TryHasKey(key, out bool scopedPresent) && !scopedPresent,
                    "Scoped key aliases raw key"
                );
                ConsumerCases.Require(
                    BrowserConsumerStorageSetBlocked(1) == 1,
                    "Could not block browser storage"
                );
                try
                {
                    ConsumerCases.Require(
                        !raw.TryHasKey(key, out bool unavailable) && !unavailable,
                        "Blocked storage reported key presence"
                    );
                    ConsumerCases.Require(
                        !raw.TryGetString(key, out string missing) && missing == null,
                        "Blocked storage reported a value"
                    );
                    ConsumerCases.Require(
                        !raw.TrySetString(key, "lost"),
                        "Blocked storage accepted a write"
                    );
                    ConsumerCases.Require(
                        !raw.TryDeleteKey(key),
                        "Blocked storage accepted a delete"
                    );
                }
                finally
                {
                    ConsumerCases.Require(
                        BrowserConsumerStorageSetBlocked(0) == 1,
                        "Could not restore browser storage"
                    );
                }
                ConsumerCases.Require(
                    raw.TryGetString(key, out value) && value == retained,
                    "Failed mutation lost the raw save"
                );
                ConsumerCases.Require(
                    raw.TrySetString(key, string.Empty),
                    "Could not write an empty raw value"
                );
                ConsumerCases.Require(
                    raw.TryHasKey(key, out present) && present,
                    "Empty raw value was treated as absent"
                );
                ConsumerCases.Require(
                    raw.TryGetString(key, out value) && value == string.Empty,
                    "Empty raw value changed"
                );
                ConsumerCases.Require(raw.TryDeleteKey(key), "Could not delete owned raw key");
                ConsumerCases.Require(
                    raw.TryHasKey(key, out present) && !present,
                    "Absent raw key was treated as present"
                );
            }
            finally
            {
                BrowserConsumerStorageSetBlocked(0);
                raw.TryDeleteKey(key);
                scoped.TryDeleteKey(key);
            }
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal", ExactSpelling = true)]
        private static extern int BrowserConsumerStorageSeedRaw(string key, string value);

        [DllImport("__Internal", ExactSpelling = true)]
        private static extern int BrowserConsumerStorageSetBlocked(int blocked);
#endif
    }
}
