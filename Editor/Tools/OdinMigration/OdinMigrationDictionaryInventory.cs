// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System.Collections.Generic;

    /// <summary>Reports saved Unity documents that dictionary source changes may affect.</summary>
    public sealed class OdinMigrationDictionaryInventory
    {
        /// <summary>Gets whether every scanned path was inspected without cancellation or failure.</summary>
        public bool Complete { get; }

        /// <summary>Gets whether every discovered retained document is included in the paired preview.</summary>
        public bool AllRetainedDocumentsSelected => Complete && MissingRetainedDocuments == 0;

        /// <summary>Gets whether the scan was cancelled.</summary>
        public bool Cancelled { get; }

        /// <summary>Gets the number of asset files inspected.</summary>
        public int ScannedFiles { get; }

        /// <summary>Gets the number of potentially affected retained documents missing from the preview.</summary>
        public int MissingRetainedDocuments { get; }

        /// <summary>Gets immutable potentially affected document findings.</summary>
        public IReadOnlyList<OdinMigrationAffectedDocument> Documents { get; }

        /// <summary>Gets immutable failures that prevent a complete inventory.</summary>
        public IReadOnlyList<string> Failures { get; }

        internal OdinMigrationDictionaryInventory(
            bool complete,
            bool cancelled,
            int scannedFiles,
            int missingRetainedDocuments,
            List<OdinMigrationAffectedDocument> documents,
            List<string> failures
        )
        {
            Complete = complete;
            Cancelled = cancelled;
            ScannedFiles = scannedFiles;
            MissingRetainedDocuments = missingRetainedDocuments;
            Documents = documents.AsReadOnly();
            Failures = failures.AsReadOnly();
        }
    }
}
