// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestUtils
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core.TestUtils;

    /// <summary>
    /// Defines the types of folder templates available for test fixtures.
    /// Each template type represents a common folder structure used by multiple test classes.
    /// </summary>
    public enum TemplateType
    {
        /// <summary>
        /// Unknown or unspecified template type. Do not use directly.
        /// </summary>
        [Obsolete("Use a specific template type")]
        Unknown = 0,

        /// <summary>
        /// Folder structure for singleton-related tests.
        /// Creates folders like Assets/Resources/Tests/SingletonTests.
        /// </summary>
        SingletonTests = 1,

        /// <summary>
        /// Folder structure for migration tests.
        /// Creates folders like Assets/Resources/Tests/MigrationTests.
        /// </summary>
        MigrationTests = 2,

        /// <summary>
        /// Folder structure for asset change detection tests.
        /// Creates folders like Assets/__DetectAssetChangedTests__.
        /// </summary>
        AssetChangeTests = 3,

        /// <summary>
        /// Folder structure for property drawer tests.
        /// Creates folders like Assets/Temp/DrawerTests.
        /// </summary>
        DrawerTests = 4,
    }

    /// <summary>
    /// Manages shared folder templates for test fixtures with reference counting.
    /// Folders are created on first acquisition and deleted when the last reference is released.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides a centralized way to manage test folder structures that are shared
    /// across multiple test classes. Instead of each test class creating and cleaning up its
    /// own folders, tests can acquire a template and share the folder structure.
    /// </para>
    /// <para>
    /// Thread safety: All public methods use locks for correctness. This is necessary because
    /// test fixtures may run in different orders and potentially on different threads in some
    /// Unity test configurations.
    /// </para>
    /// </remarks>
    public static class FolderTemplateManager
    {
        private static readonly object Lock = new();

        private static readonly Dictionary<TemplateType, int> ReferenceCounts = new();
        private static readonly Dictionary<TemplateType, bool> CreatedTemplates = new();

        private static readonly Dictionary<TemplateType, string[]> TemplateFolders = new()
        {
            {
                TemplateType.SingletonTests,
                new[]
                {
                    "Assets/Resources",
                    "Assets/Resources/Tests",
                    "Assets/Resources/Tests/SingletonTests",
                }
            },
            {
                TemplateType.MigrationTests,
                new[]
                {
                    "Assets/Resources",
                    "Assets/Resources/Tests",
                    "Assets/Resources/Tests/MigrationTests",
                }
            },
            { TemplateType.AssetChangeTests, new[] { "Assets/__DetectAssetChangedTests__" } },
            { TemplateType.DrawerTests, new[] { "Assets/Temp", "Assets/Temp/DrawerTests" } },
        };

        /// <summary>
        /// Acquires a reference to the specified folder template.
        /// Creates the folders if this is the first acquisition.
        /// </summary>
        /// <param name="templateType">The type of folder template to acquire.</param>
        /// <returns>The array of folder paths that were created or already exist.</returns>
        /// <remarks>
        /// <para>
        /// Call this method in your test fixture's <c>[OneTimeSetUp]</c> method.
        /// The method is thread-safe and uses reference counting to track how many
        /// consumers are using the template.
        /// </para>
        /// <para>
        /// The returned paths are in creation order (parent folders first).
        /// </para>
        /// </remarks>
        public static string[] AcquireTemplate(TemplateType templateType)
        {
            lock (Lock)
            {
                if (!ReferenceCounts.TryGetValue(templateType, out int count))
                {
                    count = 0;
                }

                ReferenceCounts[templateType] = count + 1;

                if (!CreatedTemplates.TryGetValue(templateType, out bool created) || !created)
                {
                    CreateTemplateFolders(templateType);
                    CreatedTemplates[templateType] = true;
                }

                return TemplateFolders.TryGetValue(templateType, out string[] folders)
                    ? folders
                    : Array.Empty<string>();
            }
        }

        /// <summary>
        /// Releases a reference to the specified folder template.
        /// Deletes the folders when the last reference is released.
        /// </summary>
        /// <param name="templateType">The type of folder template to release.</param>
        /// <remarks>
        /// <para>
        /// Call this method in your test fixture's <c>[OneTimeTearDown]</c> method.
        /// The method is thread-safe and uses reference counting to determine when
        /// to clean up the folders.
        /// </para>
        /// <para>
        /// Folders are deleted in reverse order (deepest paths first) to ensure
        /// proper cleanup of nested structures.
        /// </para>
        /// </remarks>
        public static void ReleaseTemplate(TemplateType templateType)
        {
            lock (Lock)
            {
                if (!ReferenceCounts.TryGetValue(templateType, out int count))
                {
                    return;
                }

                count--;
                if (count <= 0)
                {
                    ReferenceCounts.Remove(templateType);
                    DeleteTemplateFolders(templateType);
                    CreatedTemplates[templateType] = false;
                }
                else
                {
                    ReferenceCounts[templateType] = count;
                }
            }
        }

        /// <summary>
        /// Gets the current reference count for a template type.
        /// Useful for diagnostic purposes.
        /// </summary>
        /// <param name="templateType">The template type to query.</param>
        /// <returns>The current reference count, or 0 if not acquired.</returns>
        public static int GetReferenceCount(TemplateType templateType)
        {
            lock (Lock)
            {
                return ReferenceCounts.TryGetValue(templateType, out int count) ? count : 0;
            }
        }

        /// <summary>
        /// Gets whether a template's folders have been created.
        /// </summary>
        /// <param name="templateType">The template type to query.</param>
        /// <returns>True if the template folders exist, false otherwise.</returns>
        public static bool IsTemplateCreated(TemplateType templateType)
        {
            lock (Lock)
            {
                return CreatedTemplates.TryGetValue(templateType, out bool created) && created;
            }
        }

        /// <summary>
        /// Forces cleanup of all templates regardless of reference count.
        /// Use this only for emergency cleanup or test infrastructure resets.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Warning:</strong> This method bypasses reference counting and immediately
        /// deletes all template folders. Only use this when you need to ensure a clean state,
        /// such as during global test teardown.
        /// </para>
        /// </remarks>
        public static void ForceCleanupAll()
        {
            lock (Lock)
            {
                using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: true))
                {
                    foreach (TemplateType templateType in TemplateFolders.Keys)
                    {
                        DeleteTemplateFolders(templateType, cleanupTempDuplicates: false);
                    }
                }

                TempFolderCleanupUtility.CleanupTempDuplicatesWithRetry();
                ReferenceCounts.Clear();
                CreatedTemplates.Clear();
            }
        }

        /// <summary>
        /// Gets the folder paths for a specific template type without acquiring a reference.
        /// </summary>
        /// <param name="templateType">The template type to query.</param>
        /// <returns>The array of folder paths for the template, or empty if not found.</returns>
        public static string[] GetTemplatePaths(TemplateType templateType)
        {
            return TemplateFolders.TryGetValue(templateType, out string[] folders)
                ? folders
                : Array.Empty<string>();
        }

        private static void CreateTemplateFolders(TemplateType templateType)
        {
            if (!TemplateFolders.TryGetValue(templateType, out string[] folders))
            {
                return;
            }

            if (TemplateUsesTempFolder(folders))
            {
                TempFolderCleanupUtility.CleanupTempDuplicates();
            }

            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                foreach (string folderPath in folders)
                {
                    AssetDatabaseBatchHelper.EnsureAssetFolder(folderPath);
                }
            }

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
        }

        private static void DeleteTemplateFolders(
            TemplateType templateType,
            bool cleanupTempDuplicates = true
        )
        {
            if (!TemplateFolders.TryGetValue(templateType, out string[] folders))
            {
                return;
            }

            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: true))
            {
                for (int i = folders.Length - 1; i >= 0; i--)
                {
                    string folderPath = folders[i];
                    if (AssetDatabase.IsValidFolder(folderPath))
                    {
                        AssetDatabase.DeleteAsset(folderPath);
                    }
                }
            }

            if (cleanupTempDuplicates && TemplateUsesTempFolder(folders))
            {
                TempFolderCleanupUtility.CleanupTempDuplicatesWithRetry();
            }
        }

        private static bool TemplateUsesTempFolder(string[] folders)
        {
            foreach (string folderPath in folders)
            {
                if (
                    string.Equals(folderPath, "Assets/Temp", StringComparison.OrdinalIgnoreCase)
                    || folderPath.StartsWith("Assets/Temp/", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }

            return false;
        }
    }
#endif
}
