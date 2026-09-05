// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestUtils
{
#if UNITY_EDITOR
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Utility class for cleaning up Unity's automatically created "Temp N" duplicate folders.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When tests create and delete temporary folders rapidly, Unity's AssetDatabase can
    /// occasionally create duplicate folders named "Temp 1", "Temp 2", etc. This utility
    /// provides methods to clean up these duplicates safely.
    /// </para>
    /// <para>
    /// The cleanup methods detect folders matching the pattern "Temp N" where N is a positive
    /// integer. The base "Temp" folder is never deleted by these methods since it may contain
    /// active test fixtures.
    /// </para>
    /// </remarks>
    public static class TempFolderCleanupUtility
    {
        /// <summary>
        /// Default retry count for individual fixture cleanup operations.
        /// </summary>
        /// <remarks>
        /// Individual fixture cleanup uses 3 retries, which is sufficient for most cases
        /// where duplicates are created during a single AssetDatabase.Refresh() call.
        /// </remarks>
        public const int DefaultRetryCount = 3;

        /// <summary>
        /// Retry count for assembly-level final cleanup operations.
        /// </summary>
        /// <remarks>
        /// Assembly-level cleanup uses 5 retries as a final safety net to ensure
        /// all duplicate folders are removed after multiple fixture cleanups have
        /// occurred throughout the test run. This higher count accounts for the
        /// cumulative effect of multiple sequential cleanup operations.
        /// </remarks>
        public const int AssemblyLevelRetryCount = 5;

        /// <summary>
        /// Determines whether a folder name matches the "Temp N" pattern for duplicate folders.
        /// </summary>
        /// <param name="folderName">The folder name to check (not the full path).</param>
        /// <returns>True if the folder name matches the "Temp N" pattern where N is a positive integer.</returns>
        /// <remarks>
        /// <para>
        /// This method checks for the exact pattern used by Unity when creating duplicate folders:
        /// <list type="bullet">
        /// <item>Must start with "Temp " (case-insensitive, single space)</item>
        /// <item>Must be followed by a positive integer</item>
        /// <item>Double spaces ("Temp  1") are rejected</item>
        /// <item>Non-integer suffixes ("Temp abc") are rejected</item>
        /// <item>Zero or negative numbers ("Temp 0", "Temp -1") are rejected</item>
        /// </list>
        /// </para>
        /// </remarks>
        private static bool IsTempDuplicateFolder(string folderName)
        {
            if (string.IsNullOrEmpty(folderName))
            {
                return false;
            }

            if (!folderName.StartsWith("Temp ", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string suffix = folderName.Substring(5);

            /*
                Reject if suffix starts with whitespace (handles double-space like "Temp  1") int.TryParse would
                otherwise accept " 1" as valid since it trims whitespace
            */
            if (suffix.Length == 0 || char.IsWhiteSpace(suffix[0]))
            {
                return false;
            }

            return int.TryParse(suffix, out int number) && 0 < number;
        }

        /// <summary>
        /// Cleans up numbered duplicate Temp folders that may have been created by Unity.
        /// </summary>
        /// <returns>The number of folders that were deleted.</returns>
        /// <remarks>
        /// <para>
        /// This method scans the Assets folder for subfolders matching the pattern "Temp N"
        /// where N is a positive integer (e.g., "Temp 1", "Temp 2", "Temp 42").
        /// </para>
        /// <para>
        /// The method handles edge cases such as:
        /// <list type="bullet">
        /// <item>Double spaces ("Temp  1") - not matched</item>
        /// <item>Non-integer suffixes ("Temp abc") - not matched</item>
        /// <item>Zero or negative numbers ("Temp 0", "Temp -1") - not matched</item>
        /// <item>Base "Temp" folder - never deleted</item>
        /// </list>
        /// </para>
        /// </remarks>
        public static int CleanupTempDuplicates()
        {
            if (!AssetDatabase.IsValidFolder("Assets"))
            {
                return 0;
            }

            string[] subFolders = AssetDatabase.GetSubFolders("Assets");
            if (subFolders == null)
            {
                return 0;
            }

            int deletedCount = 0;
            foreach (string folder in subFolders)
            {
                string name = Path.GetFileName(folder);
                if (IsTempDuplicateFolder(name) && AssetDatabase.DeleteAsset(folder))
                {
                    deletedCount++;
                }
            }

            return deletedCount;
        }

        /// <summary>
        /// Cleans up "Temp N" duplicate folders with retry logic.
        /// </summary>
        /// <param name="maxRetries">
        /// The maximum number of cleanup attempts. Defaults to <see cref="DefaultRetryCount"/>.
        /// </param>
        /// <returns>The total number of folders deleted across all retry attempts.</returns>
        /// <remarks>
        /// <para>
        /// The AssetDatabase.Refresh() call at the end of batch operations may create new
        /// duplicate folders. This method retries cleanup after each refresh to catch any
        /// newly created duplicates.
        /// </para>
        /// <para>
        /// The method stops early if no folders were deleted in an attempt, as this indicates
        /// the AssetDatabase state has stabilized.
        /// </para>
        /// </remarks>
        public static int CleanupTempDuplicatesWithRetry(int maxRetries = DefaultRetryCount)
        {
            int totalDeleted = 0;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                int deletedCount = CleanupTempDuplicates();
                totalDeleted += deletedCount;

                if (deletedCount == 0)
                {
                    break;
                }

                // Refreshing deleted folders may reveal additional numbered duplicates.
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            return totalDeleted;
        }

        /// <summary>
        /// Verifies that no "Temp N" duplicate folders remain after cleanup.
        /// Logs warnings for any remaining duplicate folders.
        /// </summary>
        /// <returns>
        /// True if no duplicate folders remain; false if any were found.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This method should be called after cleanup operations to verify success.
        /// It logs warnings with detailed information about any remaining folders,
        /// including the folder path and possible causes.
        /// </para>
        /// <para>
        /// Possible causes for remaining folders include:
        /// <list type="bullet">
        /// <item>File locks from external processes</item>
        /// <item>Unity Editor keeping references to deleted assets</item>
        /// <item>Race conditions during rapid folder creation/deletion</item>
        /// <item>Filesystem synchronization delays</item>
        /// </list>
        /// </para>
        /// </remarks>
        public static bool VerifyNoTempDuplicatesRemain()
        {
            if (!AssetDatabase.IsValidFolder("Assets"))
            {
                return true;
            }

            string[] subFolders = AssetDatabase.GetSubFolders("Assets");
            if (subFolders == null)
            {
                return true;
            }

            bool allClean = true;
            foreach (string folder in subFolders)
            {
                string name = Path.GetFileName(folder);
                if (IsTempDuplicateFolder(name))
                {
                    Debug.LogWarning(
                        $"[TempFolderCleanupUtility] Failed to clean up temp folder: {folder}. "
                            + "This may be caused by file locks, Unity Editor references, or filesystem delays. "
                            + "Consider running cleanup again or manually deleting the folder."
                    );
                    allClean = false;
                }
            }

            return allClean;
        }

        /// <summary>
        /// Attempts to clean up the empty Assets/Temp parent folder.
        /// </summary>
        /// <returns>True if the folder was deleted or didn't exist; false otherwise.</returns>
        /// <remarks>
        /// <para>
        /// This method only deletes the Assets/Temp folder if it exists and is empty.
        /// It is safe to call even when test fixtures are actively using the Temp folder,
        /// as non-empty folders will not be deleted.
        /// </para>
        /// <para>
        /// Call this method at the end of assembly-level teardown after all fixtures
        /// have released their resources.
        /// </para>
        /// </remarks>
        public static bool TryCleanupEmptyTempFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Temp"))
            {
                return true;
            }

            string[] remainingAssets = AssetDatabase.FindAssets("", new[] { "Assets/Temp" });
            if (0 < remainingAssets.Length)
            {
                return false;
            }

            return AssetDatabase.DeleteAsset("Assets/Temp");
        }
    }
#endif
}
