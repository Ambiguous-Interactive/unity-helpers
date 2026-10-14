// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;

    /// <summary>Identifies the persisted phase of a coordinated dictionary migration.</summary>
    public enum OdinMigrationApplyState
    {
        /// <summary>Represents an uninitialized recovery record.</summary>
        [Obsolete("An uninitialized migration state cannot be used.")]
        Unknown = 0,

        /// <summary>Indicates that recovery records exist before target writes.</summary>
        Prepared = 1,

        /// <summary>Indicates that replacement sources and assets await compilation and import.</summary>
        AwaitingImport = 2,

        /// <summary>Indicates that imported sources and dictionary values were verified.</summary>
        Verified = 3,

        /// <summary>Indicates that original file restoration is required.</summary>
        RecoveryRequested = 4,

        /// <summary>Indicates that restored sources and assets await compilation and import.</summary>
        AwaitingRecoveryImport = 5,

        /// <summary>Indicates that original files were restored and compilation completed.</summary>
        Recovered = 6,

        /// <summary>Indicates that recovery requires manual intervention.</summary>
        RecoveryFailed = 7,
    }
}
