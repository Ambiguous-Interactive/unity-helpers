// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    ///     A disposable scope that batches AssetDatabase operations for improved performance.
    ///     Calls <see cref="AssetDatabase.StartAssetEditing"/> and <see cref="AssetDatabase.DisallowAutoRefresh"/>
    ///     on construction, and <see cref="AssetDatabase.AllowAutoRefresh"/>, <see cref="AssetDatabase.StopAssetEditing"/>,
    ///     and optionally <see cref="AssetDatabase.Refresh"/> on disposal.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Use this struct with a <c>using</c> statement to automatically batch asset operations:
    ///     </para>
    ///     <code>
    ///     using (AssetDatabaseBatchHelper.BeginBatch())
    ///     {
    ///         // Multiple asset operations batched together
    ///         AssetDatabase.CreateAsset(obj, path);
    ///         AssetDatabase.DeleteAsset(oldPath);
    ///     }
    ///     </code>
    ///     <para>
    ///     Nested scopes are supported. The actual AssetDatabase calls are only made at the outermost scope.
    ///     </para>
    /// </remarks>
    public readonly struct AssetDatabaseBatchScope : IDisposable
    {
        /// <summary>
        ///     Whether to call <see cref="AssetDatabase.Refresh"/> when this scope is disposed
        ///     and is the scope that triggers cleanup.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     <strong>Out-of-order disposal:</strong> When scopes are disposed out of order
        ///     (e.g., an inner scope is disposed after the outer scope), the cleanup (including
        ///     the optional refresh) is performed by whichever scope's disposal brings the
        ///     counter to zero. This means:
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             If the inner scope (with <c>refreshOnDispose=true</c>) is disposed last,
        ///             the refresh will still occur.
        ///         </item>
        ///         <item>
        ///             If the outer scope (with <c>refreshOnDispose=false</c>) is disposed last
        ///             and happens to bring the counter to zero, no refresh will occur even if
        ///             the inner scope requested one.
        ///         </item>
        ///     </list>
        ///     <para>
        ///     For correct behavior, always dispose scopes in the reverse order of creation
        ///     (LIFO - Last In, First Out), which is automatic when using <c>using</c> statements.
        ///     </para>
        /// </remarks>
        private readonly bool _shouldRefreshOnDispose;

        /// <summary>
        ///     Whether this scope was the outermost scope when it was created.
        ///     Used to detect out-of-order disposal.
        /// </summary>
        private readonly bool _isOutermostScope;

        /// <summary>
        ///     Creates a new batch scope. Use <see cref="AssetDatabaseBatchHelper.BeginBatch"/> instead of calling this directly.
        /// </summary>
        /// <param name="refreshOnDispose">Whether to call <see cref="AssetDatabase.Refresh"/> when disposing.</param>
        internal AssetDatabaseBatchScope(bool refreshOnDispose)
        {
            _shouldRefreshOnDispose = refreshOnDispose;
            _isOutermostScope = AssetDatabaseBatchHelper.IncrementBatchDepthWithUnityCall();

            if (_isOutermostScope)
            {
                AssetDatabase.StartAssetEditing();
                AssetDatabase.DisallowAutoRefresh();
            }
        }

        /// <summary>
        ///     Ends the batch scope. If this is the outermost scope, calls
        ///     <see cref="AssetDatabase.AllowAutoRefresh"/>, <see cref="AssetDatabase.StopAssetEditing"/>,
        ///     and optionally <see cref="AssetDatabase.Refresh"/>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     This method is exception-safe: <see cref="AssetDatabase.StopAssetEditing"/> is guaranteed
        ///     to be called even if <see cref="AssetDatabase.AllowAutoRefresh"/> throws an exception.
        ///     This prevents Unity from being left in a stuck editing state.
        ///     </para>
        ///     <para>
        ///     Any exceptions during cleanup are logged but not rethrown to ensure disposal completes.
        ///     </para>
        /// </remarks>
        public void Dispose()
        {
            bool wasOutermost = AssetDatabaseBatchHelper.DecrementBatchDepthWithUnityCleanup();

            if (wasOutermost != _isOutermostScope)
            {
                Debug.LogWarning(
                    $"[{nameof(AssetDatabaseBatchScope)}] Scope disposal state mismatch: wasOutermost={wasOutermost}, _isOutermostScope={_isOutermostScope}. "
                        + "This may indicate out-of-order disposal or manual counter manipulation."
                );
            }

            // Perform cleanup when counter reaches 0 (wasOutermost is true).
            // Previously this required both wasOutermost AND _isOutermostScope to be true,
            // which caused Unity to be left in StartAssetEditing mode when scopes were
            // disposed out of order. The fix ensures cleanup happens whenever the counter
            // reaches 0, regardless of which scope is doing the disposing.
            if (wasOutermost)
            {
                bool allowAutoRefreshFailed = false;
                bool stopAssetEditingFailed = false;

                try
                {
                    AssetDatabase.AllowAutoRefresh();
                }
                catch (Exception allowAutoRefreshException)
                {
                    allowAutoRefreshFailed = true;
                    Debug.LogError(
                        $"[{nameof(AssetDatabaseBatchScope)}] {nameof(AssetDatabase.AllowAutoRefresh)} threw during Dispose: {allowAutoRefreshException.Message}"
                    );
                }

                try
                {
                    AssetDatabase.StopAssetEditing();
                }
                catch (Exception stopAssetEditingException)
                {
                    stopAssetEditingFailed = true;
                    Debug.LogError(
                        $"[{nameof(AssetDatabaseBatchScope)}] {nameof(AssetDatabase.StopAssetEditing)} threw during Dispose: {stopAssetEditingException.Message}"
                    );
                }

                if (allowAutoRefreshFailed || stopAssetEditingFailed)
                {
                    Debug.LogWarning(
                        $"[{nameof(AssetDatabaseBatchScope)}] Cleanup completed with errors. AllowAutoRefresh failed: {allowAutoRefreshFailed}, StopAssetEditing failed: {stopAssetEditingFailed}"
                    );
                }

                if (_shouldRefreshOnDispose)
                {
                    try
                    {
                        AssetDatabase.Refresh();
                    }
                    catch (Exception refreshException)
                    {
                        Debug.LogError(
                            $"[{nameof(AssetDatabaseBatchScope)}] {nameof(AssetDatabase.Refresh)} threw during Dispose: {refreshException.Message}"
                        );
                    }
                }
            }
        }
    }

    /// <summary>
    ///     A disposable scope that temporarily pauses AssetDatabase batch operations.
    ///     Calls <see cref="AssetDatabase.AllowAutoRefresh"/> and <see cref="AssetDatabase.StopAssetEditing"/>
    ///     on construction (if batching was active), and resumes batching on disposal.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Use this struct with a <c>using</c> statement to temporarily exit a batch scope:
    ///     </para>
    ///     <code>
    ///     using (AssetDatabaseBatchHelper.BeginBatch())
    ///     {
    ///         // ... batch operations ...
    ///         using (AssetDatabaseBatchHelper.PauseBatch())
    ///         {
    ///             // Operations here run outside of batch mode
    ///             importer.SaveAndReimport();
    ///         }
    ///         // ... more batch operations (batch mode resumed) ...
    ///     }
    ///     </code>
    ///     <para>
    ///     If no batch was active when <see cref="AssetDatabaseBatchHelper.PauseBatch"/> was called,
    ///     this struct does nothing on disposal.
    ///     </para>
    /// </remarks>
    public struct AssetDatabasePauseScope : IDisposable
    {
        private readonly bool _wasBatching;
        private bool _disposed;

        /// <summary>
        ///     Creates a new pause scope. Use <see cref="AssetDatabaseBatchHelper.PauseBatch"/> instead of calling this directly.
        /// </summary>
        /// <param name="wasBatching">Whether a batch was active and has been paused.</param>
        internal AssetDatabasePauseScope(bool wasBatching)
        {
            _wasBatching = wasBatching;
            _disposed = false;
        }

        /// <summary>
        ///     Ends the pause scope. If a batch was paused, resumes batch mode by calling
        ///     <see cref="AssetDatabase.StartAssetEditing"/> and <see cref="AssetDatabase.DisallowAutoRefresh"/>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     This method is exception-safe: any exceptions during batch resumption are logged but not rethrown.
        ///     </para>
        ///     <para>
        ///     This method is idempotent: calling it multiple times has no effect after the first call.
        ///     </para>
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_wasBatching)
            {
                try
                {
                    AssetDatabaseBatchHelper.ResumeBatch();
                }
                catch (Exception resumeBatchException)
                {
                    Debug.LogError(
                        $"[{nameof(AssetDatabasePauseScope)}] {nameof(AssetDatabaseBatchHelper.ResumeBatch)} threw during Dispose: {resumeBatchException.Message}"
                    );
                }
            }
        }
    }

    /// <summary>
    ///     Provides static helper methods for managing AssetDatabase batch operations.
    ///     This is the single source of truth for all AssetDatabase batching in the codebase.
    /// </summary>
    public static class AssetDatabaseBatchHelper
    {
        private static readonly object Lock = new();
        private static int _batchDepth;

        /// <summary>
        ///     Tracks the number of actual Unity AssetDatabase API calls we've made.
        ///     This can differ from _batchDepth if code manually calls IncrementBatchDepth.
        /// </summary>
        private static int _actualUnityBatchDepth;

        /// <summary>
        ///     Gets a value indicating whether AssetDatabase operations are currently being batched.
        /// </summary>
        public static bool IsCurrentlyBatching
        {
            get
            {
                lock (Lock)
                {
                    return _batchDepth > 0;
                }
            }
        }

        /// <summary>
        ///     Gets the current nesting depth of batch scopes.
        /// </summary>
        public static int CurrentBatchDepth
        {
            get
            {
                lock (Lock)
                {
                    return _batchDepth;
                }
            }
        }

        /// <summary>
        ///     Gets the number of actual Unity AssetDatabase batch operations in progress.
        ///     This tracks how many times StartAssetEditing/DisallowAutoRefresh were actually called.
        /// </summary>
        internal static int ActualUnityBatchDepth
        {
            get
            {
                lock (Lock)
                {
                    return _actualUnityBatchDepth;
                }
            }
        }

        /// <summary>
        ///     Begins a new AssetDatabase batch scope. All asset operations within the scope
        ///     are batched together for improved performance.
        /// </summary>
        /// <param name="refreshOnDispose">
        ///     Whether to call <see cref="AssetDatabase.Refresh"/> when the scope is disposed.
        ///     Defaults to <c>true</c>.
        /// </param>
        /// <returns>A disposable scope that ends the batch when disposed.</returns>
        /// <example>
        ///     <code>
        ///     using (AssetDatabaseBatchHelper.BeginBatch())
        ///     {
        ///         AssetDatabase.CreateAsset(obj1, path1);
        ///         AssetDatabase.CreateAsset(obj2, path2);
        ///         AssetDatabase.DeleteAsset(oldPath);
        ///     }
        ///     // Assets are now committed and database is refreshed
        ///     </code>
        /// </example>
        public static AssetDatabaseBatchScope BeginBatch(bool refreshOnDispose = true)
        {
            return new AssetDatabaseBatchScope(refreshOnDispose);
        }

        /// <summary>
        ///     Ensures that every folder segment of <paramref name="assetFolderPath"/> exists and is
        ///     registered with the <see cref="AssetDatabase"/>, creating any missing segments through
        ///     <see cref="AssetDatabase.CreateFolder(string, string)"/> only. This is the single,
        ///     batch-safe way to guarantee a parent folder is present before
        ///     <see cref="AssetDatabase.CreateAsset(Object, string)"/>.
        /// </summary>
        /// <param name="assetFolderPath">
        ///     A Unity-relative folder path rooted at <c>Assets</c> (e.g. <c>Assets/Resources/Tests/Foo</c>).
        ///     A path that points at an asset file is not valid input; pass the asset's parent folder.
        /// </param>
        /// <returns>
        ///     <see langword="true"/> if the folder exists in the AssetDatabase after the call;
        ///     otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>
        ///     <para>
        ///     <strong>Why this exists:</strong> while a batch opened by <see cref="BeginBatch"/> (or by
        ///     a fixture-wide <see cref="AssetDatabase.StartAssetEditing"/>) is active, folder creation is
        ///     deferred and <see cref="AssetDatabase.Refresh()"/> is a no-op, so the parent folder is not
        ///     registered when <see cref="AssetDatabase.CreateAsset(Object, string)"/> runs. Unity then
        ///     fails with "Parent directory must exist before creating asset". This method
        ///     <see cref="PauseBatch"/>es first so each <see cref="AssetDatabase.CreateFolder(string, string)"/>
        ///     takes effect immediately, then resumes the batch on return.
        ///     </para>
        ///     <para>
        ///     It deliberately uses <see cref="AssetDatabase.CreateFolder(string, string)"/> rather than
        ///     <see cref="System.IO.Directory.CreateDirectory"/>: raw disk creation leaves the
        ///     AssetDatabase out of sync and makes a later <see cref="AssetDatabase.CreateFolder(string, string)"/>
        ///     spawn numbered duplicates such as "Foo 1". Following defensive-programming rules, invalid input is
        ///     handled gracefully (returns <see langword="false"/>) instead of throwing.
        ///     </para>
        /// </remarks>
        public static bool EnsureAssetFolder(string assetFolderPath)
        {
            if (string.IsNullOrWhiteSpace(assetFolderPath))
            {
                return false;
            }

            string normalized = assetFolderPath.SanitizePath().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (AssetDatabase.IsValidFolder(normalized))
            {
                return true;
            }

            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return false;
            }

            // Only the Assets tree can be created through the AssetDatabase. Other roots
            // (e.g. Packages) are read-only here, so refuse rather than fabricate folders.
            if (!string.Equals(segments[0], "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Pausing guarantees CreateFolder/ImportAsset are applied synchronously even when a
            // fixture-wide StartAssetEditing batch is open. PauseBatch is a no-op when nothing is batching.
            using (PauseBatch())
            {
                string current = segments[0];
                for (int i = 1; i < segments.Length; i++)
                {
                    string next = current + "/" + segments[i];
                    if (
                        !AssetDatabase.IsValidFolder(next)
                        && !EnsureSingleFolderSegment(current, segments[i], next)
                    )
                    {
                        Debug.LogWarning(
                            $"[{nameof(AssetDatabaseBatchHelper)}] {nameof(EnsureAssetFolder)} failed to create folder '{next}'."
                        );
                        return false;
                    }

                    current = next;
                }

                return AssetDatabase.IsValidFolder(normalized);
            }
        }

        /// <summary>
        ///     Ensures a single child folder segment exists in the AssetDatabase, robust to the
        ///     common desync where the folder already exists on disk but has not yet been imported.
        ///     Calling <see cref="AssetDatabase.CreateFolder(string, string)"/> blindly in that
        ///     state either fails (empty GUID) or fabricates a numbered duplicate ("Name 1") that
        ///     leaves the intended path invalid — the root cause of the "Failed to ensure folder"
        ///     errors and the leftover "Name 1" duplicate folders. Must be called inside
        ///     <see cref="PauseBatch"/> so the operations apply synchronously.
        /// </summary>
        /// <param name="parentFolder">The (already-valid) parent folder, e.g. <c>Assets/Resources</c>.</param>
        /// <param name="childName">The single child segment to create, e.g. <c>Wallstop Studios</c>.</param>
        /// <param name="childFolder">The full intended child path (<paramref name="parentFolder"/>/<paramref name="childName"/>).</param>
        /// <returns><see langword="true"/> if <paramref name="childFolder"/> is a valid AssetDatabase folder afterwards.</returns>
        private static bool EnsureSingleFolderSegment(
            string parentFolder,
            string childName,
            string childFolder
        )
        {
            // 1. The folder already exists on disk but the AssetDatabase has not imported it yet
            //    (VCS checkout, external tooling, a sibling test's Directory.CreateDirectory, a
            //    leaked batch scope, ...). Adopt it via import instead of creating a duplicate.
            if (DirectoryExistsOnDisk(childFolder))
            {
                AssetDatabase.ImportAsset(childFolder, ImportAssetOptions.ForceSynchronousImport);
                if (AssetDatabase.IsValidFolder(childFolder))
                {
                    return true;
                }
            }

            // 2. Genuinely missing in both the AssetDatabase and on disk — create it.
            string createdGuid = AssetDatabase.CreateFolder(parentFolder, childName);

            // 3. CreateFolder collided with an on-disk folder (or a concurrent op) and produced a
            //    numbered duplicate ("Name 1") rather than the intended "Name". Remove the empty
            //    duplicate and adopt the real on-disk folder so the intended path becomes valid.
            if (!string.IsNullOrEmpty(createdGuid))
            {
                string createdPath = AssetDatabase.GUIDToAssetPath(createdGuid)?.SanitizePath();
                if (
                    !string.IsNullOrEmpty(createdPath)
                    && !string.Equals(createdPath, childFolder, StringComparison.OrdinalIgnoreCase)
                )
                {
                    AssetDatabase.DeleteAsset(createdPath);
                    if (DirectoryExistsOnDisk(childFolder))
                    {
                        AssetDatabase.ImportAsset(
                            childFolder,
                            ImportAssetOptions.ForceSynchronousImport
                        );
                    }
                }
            }

            return AssetDatabase.IsValidFolder(childFolder);
        }

        /// <summary>
        ///     Returns <see langword="true"/> if the given Unity-relative asset folder path
        ///     (rooted at <c>Assets</c>) exists as a directory on the project's filesystem,
        ///     regardless of whether the AssetDatabase has imported it.
        /// </summary>
        private static bool DirectoryExistsOnDisk(string assetFolderPath)
        {
            if (string.IsNullOrEmpty(assetFolderPath))
            {
                return false;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return false;
            }

            string absolute = Path.Combine(
                projectRoot,
                assetFolderPath.Replace('/', Path.DirectorySeparatorChar)
            );
            return Directory.Exists(absolute);
        }

        /// <summary>
        ///     Ensures the parent folder of <paramref name="assetPath"/> exists in the AssetDatabase
        ///     (creating missing segments through <see cref="AssetDatabase.CreateFolder(string, string)"/>)
        ///     so a subsequent <see cref="AssetDatabase.CreateAsset(Object, string)"/> at that path cannot
        ///     fail with "Parent directory must exist".
        /// </summary>
        /// <param name="assetPath">A Unity-relative asset path rooted at <c>Assets</c> (e.g. <c>Assets/Foo/Bar.asset</c>).</param>
        /// <returns>
        ///     <see langword="true"/> if the parent folder exists in the AssetDatabase after the call,
        ///     or if <paramref name="assetPath"/> sits directly under <c>Assets</c> (no folder to create);
        ///     otherwise <see langword="false"/>.
        /// </returns>
        /// <remarks>See <see cref="EnsureAssetFolder"/> for the batch-pause and AssetDatabase-only rationale.</remarks>
        public static bool EnsureAssetParentFolder(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            string normalized = assetPath.SanitizePath();
            int lastSlash = normalized.LastIndexOf('/');
            if (lastSlash <= 0)
            {
                // No parent segment (e.g. "Bar.asset"); nothing to create.
                return true;
            }

            string parentFolder = normalized.Substring(0, lastSlash);

            // A bare "Assets" parent always exists and is not creatable via CreateFolder.
            if (string.Equals(parentFolder, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return EnsureAssetFolder(parentFolder);
        }

        /// <summary>
        ///     Calls <see cref="AssetDatabase.Refresh"/> only if no batch scope is currently active.
        ///     Use this when you need to ensure assets are refreshed but want to respect active batch scopes.
        /// </summary>
        /// <remarks>
        ///     This method is useful for code that may be called both inside and outside of batch scopes.
        ///     When inside a batch scope, the refresh will be skipped (and handled by the scope disposal).
        ///     When outside a batch scope, the refresh will be performed immediately.
        /// </remarks>
        public static void RefreshIfNotBatching()
        {
            if (!IsCurrentlyBatching)
            {
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        ///     Calls <see cref="AssetDatabase.Refresh"/> only if no batch scope is currently active.
        ///     Use this when you need to ensure assets are refreshed but want to respect active batch scopes.
        /// </summary>
        /// <param name="options">The import options to use if refresh is performed.</param>
        /// <remarks>
        ///     This method is useful for code that may be called both inside and outside of batch scopes.
        ///     When inside a batch scope, the refresh will be skipped (and handled by the scope disposal).
        ///     When outside a batch scope, the refresh will be performed immediately.
        /// </remarks>
        public static void RefreshIfNotBatching(ImportAssetOptions options)
        {
            if (!IsCurrentlyBatching)
            {
                AssetDatabase.Refresh(options);
            }
        }

        /// <summary>
        ///     Calls <see cref="AssetDatabase.SaveAssets"/> followed by <see cref="AssetDatabase.Refresh"/>
        ///     only if no batch scope is currently active.
        ///     Use this for the common pattern of saving and refreshing assets together.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     This method combines the common <c>SaveAssets() + Refresh()</c> pattern into a single call
        ///     that respects batch scopes. When inside a batch scope, both operations are skipped
        ///     (and handled by the scope disposal).
        ///     </para>
        ///     <para>
        ///     When outside a batch scope, this method calls <see cref="AssetDatabase.SaveAssets"/>
        ///     followed by <see cref="AssetDatabase.Refresh"/> with <see cref="ImportAssetOptions.ForceSynchronousImport"/>.
        ///     </para>
        /// </remarks>
        public static void SaveAndRefreshIfNotBatching()
        {
            if (!IsCurrentlyBatching)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        /// <summary>
        ///     Calls <see cref="AssetDatabase.SaveAssets"/> followed by <see cref="AssetDatabase.Refresh"/>
        ///     only if no batch scope is currently active.
        ///     Use this for the common pattern of saving and refreshing assets together.
        /// </summary>
        /// <param name="options">The import options to use if refresh is performed.</param>
        /// <remarks>
        ///     <para>
        ///     This method combines the common <c>SaveAssets() + Refresh()</c> pattern into a single call
        ///     that respects batch scopes. When inside a batch scope, both operations are skipped
        ///     (and handled by the scope disposal).
        ///     </para>
        ///     <para>
        ///     When outside a batch scope, this method calls <see cref="AssetDatabase.SaveAssets"/>
        ///     followed by <see cref="AssetDatabase.Refresh"/> with the specified options.
        ///     </para>
        /// </remarks>
        public static void SaveAndRefreshIfNotBatching(ImportAssetOptions options)
        {
            if (!IsCurrentlyBatching)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(options);
            }
        }

        /// <summary>
        ///     Increments the batch depth counter and returns whether this is the outermost scope.
        ///     This method ONLY increments the counter - it does NOT call Unity's AssetDatabase APIs.
        /// </summary>
        /// <remarks>
        ///     <strong>Warning:</strong> Using this method directly creates a mismatch between the
        ///     tracked counter and Unity's actual state. Prefer using <see cref="BeginBatch"/> instead,
        ///     which properly manages both the counter and Unity's state.
        ///     This method exists primarily for testing the counter logic in isolation.
        /// </remarks>
        /// <returns><c>true</c> if this is the outermost (first) scope; otherwise, <c>false</c>.</returns>
        internal static bool IncrementBatchDepth()
        {
            lock (Lock)
            {
                int previousDepth = _batchDepth;
                _batchDepth++;
                return previousDepth == 0;
            }
        }

        /// <summary>
        ///     Increments the batch depth counter and tracks that Unity APIs will be called.
        ///     This is used internally by <see cref="AssetDatabaseBatchScope"/> when it will
        ///     call <see cref="AssetDatabase.StartAssetEditing"/> and <see cref="AssetDatabase.DisallowAutoRefresh"/>.
        /// </summary>
        /// <returns><c>true</c> if this is the outermost (first) scope; otherwise, <c>false</c>.</returns>
        internal static bool IncrementBatchDepthWithUnityCall()
        {
            lock (Lock)
            {
                int previousDepth = _batchDepth;
                _batchDepth++;
                bool isOutermost = previousDepth == 0;
                if (isOutermost)
                {
                    _actualUnityBatchDepth++;
                }
                return isOutermost;
            }
        }

        /// <summary>
        ///     Decrements the batch depth counter and returns whether this was the outermost scope.
        ///     This method ONLY decrements the counter - use <see cref="DecrementBatchDepthWithUnityCleanup"/>
        ///     when Unity cleanup will be performed.
        /// </summary>
        /// <returns><c>true</c> if this was the outermost scope (depth is now 0); otherwise, <c>false</c>.</returns>
        internal static bool DecrementBatchDepth()
        {
            lock (Lock)
            {
                _batchDepth--;

                if (_batchDepth < 0)
                {
                    _batchDepth = 0;
                    return false;
                }

                return _batchDepth == 0;
            }
        }

        /// <summary>
        ///     Decrements both the batch depth counter and the Unity API call tracker.
        ///     Returns whether this was the outermost scope (and thus Unity cleanup should be performed).
        /// </summary>
        /// <returns><c>true</c> if this was the outermost scope (depth is now 0); otherwise, <c>false</c>.</returns>
        internal static bool DecrementBatchDepthWithUnityCleanup()
        {
            lock (Lock)
            {
                _batchDepth--;

                if (_batchDepth < 0)
                {
                    _batchDepth = 0;
                    return false;
                }

                if (_batchDepth == 0)
                {
                    // Only decrement the Unity depth if we were tracking it
                    if (_actualUnityBatchDepth > 0)
                    {
                        _actualUnityBatchDepth--;
                    }
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        ///     Resets the batch depth counter to zero and properly cleans up Unity's AssetDatabase state.
        ///     This is the standard test cleanup method - use in SetUp/TearDown methods.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     <strong>When to use:</strong> Call this in test SetUp and TearDown methods to ensure
        ///     clean state between tests. This is the preferred cleanup method for normal test scenarios.
        ///     </para>
        ///     <para>
        ///     This method performs the following cleanup:
        ///     </para>
        ///     <list type="bullet">
        ///         <item>Calls <see cref="AssetDatabase.StopAssetEditing"/> for each tracked batch level</item>
        ///         <item>Calls <see cref="AssetDatabase.AllowAutoRefresh"/> for each tracked batch level</item>
        ///         <item>Resets the internal counter to zero</item>
        ///     </list>
        ///     <para>
        ///     <strong>Important:</strong> This method only cleans up Unity state for the number of times
        ///     that <see cref="BeginBatch"/> was called. If code manually incremented the batch depth
        ///     counter without using <see cref="BeginBatch"/>, those "phantom" levels are NOT cleaned
        ///     up because Unity's actual state doesn't need cleaning.
        ///     </para>
        ///     <para>
        ///     This method is exception-safe: cleanup continues even if individual Unity API calls fail.
        ///     </para>
        /// </remarks>
        internal static void ResetBatchDepth()
        {
            int depthToCleanup;

            lock (Lock)
            {
                int currentDepth = _batchDepth;
                int actualDepth = _actualUnityBatchDepth;
                _batchDepth = 0;
                _actualUnityBatchDepth = 0;

                depthToCleanup = currentDepth > 0 ? Math.Min(currentDepth, actualDepth) : 0;
            }

            int allowAutoRefreshFailures = 0;
            int stopAssetEditingFailures = 0;

            for (int i = 0; i < depthToCleanup; i++)
            {
                try
                {
                    AssetDatabase.AllowAutoRefresh();
                }
                catch (Exception allowAutoRefreshException)
                {
                    allowAutoRefreshFailures++;
                    Debug.LogError(
                        $"[{nameof(AssetDatabaseBatchHelper)}] {nameof(AssetDatabase.AllowAutoRefresh)} threw during {nameof(ResetBatchDepth)} (iteration {i + 1}/{depthToCleanup}): {allowAutoRefreshException.Message}"
                    );
                }

                try
                {
                    AssetDatabase.StopAssetEditing();
                }
                catch (Exception stopAssetEditingException)
                {
                    stopAssetEditingFailures++;
                    Debug.LogError(
                        $"[{nameof(AssetDatabaseBatchHelper)}] {nameof(AssetDatabase.StopAssetEditing)} threw during {nameof(ResetBatchDepth)} (iteration {i + 1}/{depthToCleanup}): {stopAssetEditingException.Message}"
                    );
                }
            }

            if (allowAutoRefreshFailures > 0 || stopAssetEditingFailures > 0)
            {
                Debug.LogWarning(
                    $"[{nameof(AssetDatabaseBatchHelper)}] {nameof(ResetBatchDepth)} completed with {allowAutoRefreshFailures + stopAssetEditingFailures} errors out of {depthToCleanup * 2} calls. Unity AssetDatabase state may be inconsistent."
                );
            }
        }

        /// <summary>
        ///     Force-resets the AssetDatabase to a clean state by cleaning up all Unity API calls we've tracked.
        ///     This method is equivalent to <see cref="ResetBatchDepth"/> and exists for API compatibility.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     <strong>Important:</strong> This method only cleans up Unity's AssetDatabase state for
        ///     the number of actual <see cref="AssetDatabase.StartAssetEditing"/> and
        ///     <see cref="AssetDatabase.DisallowAutoRefresh"/> calls that were made through this helper.
        ///     </para>
        ///     <para>
        ///     It is NOT safe to call Unity's <see cref="AssetDatabase.StopAssetEditing"/> or
        ///     <see cref="AssetDatabase.AllowAutoRefresh"/> more times than the corresponding start/disallow
        ///     methods were called. Doing so causes Unity assertion failures and can leave the Editor
        ///     in a broken state (hanging on "Hold on... Importing Assets").
        ///     </para>
        ///     <para>
        ///     If code has manually incremented the batch depth counter without using <see cref="BeginBatch"/>,
        ///     those "phantom" levels are not cleaned up because Unity's actual state doesn't need cleaning.
        ///     </para>
        /// </remarks>
        internal static void ForceResetAssetDatabase()
        {
            // This is now equivalent to ResetBatchDepth - there's no safe way to "force" reset
            // because we can't call Unity cleanup methods more times than we called start methods
            ResetBatchDepth();
        }

        /// <summary>
        ///     Resets only the internal counters without calling any Unity APIs.
        ///     Use this at the start of a test fixture to clear any stale state from previous
        ///     test runs or Editor sessions without risking Unity assertion failures.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     This is useful when Unity's internal AssetDatabase state may have been reset
        ///     (e.g., by domain reload) but our static counters still hold values from
        ///     previous sessions. Calling <see cref="ResetBatchDepth"/> in this situation
        ///     would cause Unity assertion failures because we'd be calling
        ///     <see cref="AssetDatabase.AllowAutoRefresh"/> and <see cref="AssetDatabase.StopAssetEditing"/>
        ///     when Unity's internal counters are already at zero.
        ///     </para>
        ///     <para>
        ///     <strong>Warning:</strong> This method does NOT clean up Unity's AssetDatabase state.
        ///     It only resets the internal tracking counters. If Unity's AssetDatabase is still
        ///     in batch mode (e.g., <see cref="AssetDatabase.StartAssetEditing"/> was called but
        ///     <see cref="AssetDatabase.StopAssetEditing"/> was not), the caller is responsible
        ///     for ensuring Unity's state is properly cleaned up separately. Use this method only
        ///     when you are certain Unity's state has already been reset (such as after a domain reload)
        ///     or when you will handle Unity state cleanup through other means.
        ///     </para>
        ///     <para>
        ///     If you need to clean up Unity state manually after calling this method, capture
        ///     <see cref="ActualUnityBatchDepth"/> before calling <see cref="ResetCountersOnly"/>,
        ///     then call the cleanup APIs for that many levels:
        ///     </para>
        ///     <code>
        ///     int unityDepth = AssetDatabaseBatchHelper.ActualUnityBatchDepth;
        ///     AssetDatabaseBatchHelper.ResetCountersOnly();
        ///     for (int i = 0; i &lt; unityDepth; i++)
        ///     {
        ///         AssetDatabase.AllowAutoRefresh();
        ///         AssetDatabase.StopAssetEditing();
        ///     }
        ///     </code>
        /// </remarks>
        internal static void ResetCountersOnly()
        {
            lock (Lock)
            {
                _batchDepth = 0;
                _actualUnityBatchDepth = 0;
            }
        }

        /// <summary>
        ///     Temporarily pauses the current batch scope to allow asset operations that require
        ///     immediate processing (like <see cref="AssetImporter.SaveAndReimport"/> or <see cref="AssetDatabase.ImportAsset"/>).
        /// </summary>
        /// <returns>
        ///     A disposable scope that resumes batch mode when disposed.
        ///     If no batch was active, the returned scope does nothing on disposal.
        /// </returns>
        /// <remarks>
        ///     <para>
        ///     Use this method when you need to perform asset operations that require immediate processing
        ///     while inside a batch scope. The typical pattern is:
        ///     </para>
        ///     <code>
        ///     using (AssetDatabaseBatchHelper.BeginBatch())
        ///     {
        ///         // ... batch operations ...
        ///         using (AssetDatabaseBatchHelper.PauseBatch())
        ///         {
        ///             // Operations here run outside of batch mode
        ///             importer.SaveAndReimport();
        ///         }
        ///         // ... more batch operations (batch mode resumed) ...
        ///     }
        ///     </code>
        ///     <para>
        ///     This method properly tracks the pause state so the counters remain in sync with Unity's state.
        ///     </para>
        /// </remarks>
        public static AssetDatabasePauseScope PauseBatch()
        {
            bool wasBatching;
            lock (Lock)
            {
                wasBatching = _batchDepth > 0 && _actualUnityBatchDepth > 0;
                if (wasBatching)
                {
                    _actualUnityBatchDepth--;
                }
            }

            if (wasBatching)
            {
                bool allowAutoRefreshFailed = false;

                try
                {
                    AssetDatabase.AllowAutoRefresh();
                }
                catch (Exception allowAutoRefreshException)
                {
                    allowAutoRefreshFailed = true;
                    Debug.LogError(
                        $"[{nameof(AssetDatabaseBatchHelper)}] {nameof(AssetDatabase.AllowAutoRefresh)} threw during {nameof(PauseBatch)}: {allowAutoRefreshException.Message}"
                    );
                }

                try
                {
                    AssetDatabase.StopAssetEditing();
                }
                catch (Exception stopAssetEditingException)
                {
                    Debug.LogError(
                        $"[{nameof(AssetDatabaseBatchHelper)}] {nameof(AssetDatabase.StopAssetEditing)} threw during {nameof(PauseBatch)}: {stopAssetEditingException.Message}"
                    );

                    if (allowAutoRefreshFailed)
                    {
                        Debug.LogWarning(
                            $"[{nameof(AssetDatabaseBatchHelper)}] {nameof(PauseBatch)} completed with multiple errors. Unity AssetDatabase state may be inconsistent."
                        );
                    }
                }
            }

            return new AssetDatabasePauseScope(wasBatching);
        }

        /// <summary>
        ///     Resumes a previously paused batch scope.
        ///     This is called automatically by <see cref="AssetDatabasePauseScope.Dispose"/>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     This method re-enters batch mode by calling <see cref="AssetDatabase.StartAssetEditing"/>
        ///     and <see cref="AssetDatabase.DisallowAutoRefresh"/>, and updates the internal counters accordingly.
        ///     </para>
        ///     <para>
        ///     <strong>Important:</strong> Prefer using the <c>using</c> pattern with <see cref="PauseBatch"/>
        ///     instead of calling this method directly, to ensure proper pairing.
        ///     </para>
        ///     <para>
        ///     This method is exception-safe: if <see cref="AssetDatabase.StartAssetEditing"/> fails,
        ///     <see cref="AssetDatabase.DisallowAutoRefresh"/> is still attempted, and the counter is only
        ///     incremented if at least one call succeeds (to maintain some level of state consistency).
        ///     </para>
        /// </remarks>
        internal static void ResumeBatch()
        {
            bool startAssetEditingSucceeded = false;
            bool disallowAutoRefreshSucceeded = false;

            try
            {
                AssetDatabase.StartAssetEditing();
                startAssetEditingSucceeded = true;
            }
            catch (Exception startAssetEditingException)
            {
                Debug.LogError(
                    $"[{nameof(AssetDatabaseBatchHelper)}] {nameof(AssetDatabase.StartAssetEditing)} threw during {nameof(ResumeBatch)}: {startAssetEditingException.Message}"
                );
            }

            try
            {
                AssetDatabase.DisallowAutoRefresh();
                disallowAutoRefreshSucceeded = true;
            }
            catch (Exception disallowAutoRefreshException)
            {
                Debug.LogError(
                    $"[{nameof(AssetDatabaseBatchHelper)}] {nameof(AssetDatabase.DisallowAutoRefresh)} threw during {nameof(ResumeBatch)}: {disallowAutoRefreshException.Message}"
                );
            }

            if (!startAssetEditingSucceeded && !disallowAutoRefreshSucceeded)
            {
                Debug.LogWarning(
                    $"[{nameof(AssetDatabaseBatchHelper)}] {nameof(ResumeBatch)} failed completely. Unity AssetDatabase state may be inconsistent."
                );
                return;
            }

            lock (Lock)
            {
                _actualUnityBatchDepth++;
            }

            if (!startAssetEditingSucceeded || !disallowAutoRefreshSucceeded)
            {
                Debug.LogWarning(
                    $"[{nameof(AssetDatabaseBatchHelper)}] {nameof(ResumeBatch)} completed with partial success. StartAssetEditing: {startAssetEditingSucceeded}, DisallowAutoRefresh: {disallowAutoRefreshSucceeded}"
                );
            }
        }
    }
#endif
}
