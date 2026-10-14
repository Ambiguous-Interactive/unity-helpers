// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    /// <summary>Reads a coordinated dictionary migration result after compilation and domain reload.</summary>
    public sealed class OdinMigrationDictionaryApply
    {
        /// <summary>Gets the retained recovery directory.</summary>
        public string BackupRoot { get; }

        /// <summary>Gets whether verification or recovery reached a terminal state.</summary>
        public bool Completed
        {
            get
            {
                return TryReadStatus(out OdinMigrationApplyState state, out string _)
                    && (
                        state == OdinMigrationApplyState.Verified
                        || state == OdinMigrationApplyState.Recovered
                        || state == OdinMigrationApplyState.RecoveryFailed
                    );
            }
        }

        /// <summary>Gets whether imported source and dictionary values were verified.</summary>
        public bool Succeeded
        {
            get
            {
                return TryReadStatus(out OdinMigrationApplyState state, out string _)
                    && state == OdinMigrationApplyState.Verified;
            }
        }

        internal OdinMigrationDictionaryApply(string backupRoot)
        {
            BackupRoot = backupRoot;
        }

        /// <summary>Reads the current persisted phase and failure without loading consumer assets.</summary>
        public bool TryReadStatus(out OdinMigrationApplyState state, out string failure)
        {
            return OdinMigrationDictionaryApplyCoordinator.TryReadStatus(
                BackupRoot,
                out state,
                out failure
            );
        }

        /// <summary>Requests restoration of original files when this migration is still running.</summary>
        public bool TryCancel(out string failure)
        {
            return OdinMigrationDictionaryApplyCoordinator.TryRecover(
                BackupRoot,
                "Migration was cancelled.",
                out failure
            );
        }
    }
}
