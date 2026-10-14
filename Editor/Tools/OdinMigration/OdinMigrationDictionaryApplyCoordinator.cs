// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using UnityEditor;
    using UnityEditor.Compilation;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using Object = UnityEngine.Object;

    [InitializeOnLoad]
    internal static class OdinMigrationDictionaryApplyCoordinator
    {
        private static readonly string Epoch = Guid.NewGuid().ToString("N");
        private static readonly string PointerPath = Path.Combine(
            OdinMigrationApplySnapshot.ProjectRoot,
            "Library",
            "WallstopOdinMigrationApply",
            "active.txt"
        );
        private static OdinMigrationApplyRecord _active;
        private static OdinMigrationApplyRecord _last;
        private static string _root;
        private static bool _refreshScheduled;

        static OdinMigrationDictionaryApplyCoordinator()
        {
            EditorApplication.update += Update;
            CompilationPipeline.assemblyCompilationFinished += CompilationFinished;
            ReloadActive();
        }

        internal static bool TryStart(
            OdinMigrationDictionaryMigrationPreview preview,
            OdinMigrationDictionaryCompilation compilation,
            out OdinMigrationDictionaryApply apply,
            out string failure
        )
        {
            string root = null;
            try
            {
                UnityMainThreadGuard.EnsureMainThread();
                RequireIdle();
                if (_active != null || File.Exists(PointerPath))
                {
                    throw new IOException(
                        "Complete or recover the active dictionary migration first."
                    );
                }
                if (
                    preview == null
                    || compilation == null
                    || !compilation.TryValidateSources(preview.Sources, out string receiptFailure)
                )
                {
                    throw new IOException(
                        "A matching successful dictionary compilation receipt is required."
                    );
                }
                if (
                    !OdinMigrationTool.TryPreviewDictionaryMigration(
                        preview.Targets,
                        out OdinMigrationDictionaryMigrationPreview current,
                        out string previewFailure
                    )
                )
                {
                    throw new IOException(previewFailure);
                }
                if (current.Assets.Count != preview.Assets.Count)
                {
                    throw new IOException("The paired asset proposals changed after preview.");
                }
                foreach (KeyValuePair<string, OdinMigrationAssetProposal> asset in current.Assets)
                {
                    if (
                        !preview.Assets.TryGetValue(
                            asset.Key,
                            out OdinMigrationAssetProposal original
                        )
                        || !string.Equals(
                            asset.Value.OriginalSource,
                            original.OriginalSource,
                            StringComparison.Ordinal
                        )
                        || !string.Equals(
                            asset.Value.ProposedSource,
                            original.ProposedSource,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        throw new IOException(
                            "An asset proposal changed after its paired preview."
                        );
                    }
                }
                if (!compilation.TryValidateSources(current.Sources, out receiptFailure))
                {
                    throw new IOException(receiptFailure);
                }
                if (
                    !OdinMigrationTool.TryInventoryDictionaryMigration(
                        current,
                        out OdinMigrationDictionaryInventory inventory,
                        out string inventoryFailure
                    ) || !inventory.AllRetainedDocumentsSelected
                )
                {
                    throw new IOException(
                        inventoryFailure
                            ?? "Every affected retained document must be selected in a complete current inventory."
                    );
                }
                root = Path.Combine(
                    OdinMigrationApplySnapshot.ProjectRoot,
                    "Library",
                    "WallstopOdinMigrationBackups",
                    "dictionary-" + Guid.NewGuid().ToString("N")
                );
                OdinMigrationApplyRecord record = OdinMigrationApplySnapshot.Capture(
                    current,
                    root,
                    Epoch,
                    out List<OdinMigrationFilePlan> plans
                );
                HashSet<string> affected = new HashSet<string>(StringComparer.Ordinal);
                foreach (OdinMigrationAffectedDocument document in inventory.Documents)
                {
                    if (!document.Selected)
                    {
                        throw new IOException(
                            "An affected document requires a retained payload or a selected live capture to verify: "
                                + document.AssetPath
                        );
                    }
                    affected.Add(document.AssetPath);
                }
                record.affectedPaths = new List<string>(affected).ToArray();
                OdinMigrationApplySceneScope.CaptureLoadedScenes(record);
                OdinMigrationApplyRecord.Validate(record, Path.GetFileName(root));
                bool applied = OdinMigrationBackupReceipt.TryApply(
                    plans,
                    root,
                    out string applyFailure,
                    () =>
                    {
                        record.Write(root);
                        WritePointer(record.backupName);
                        _active = record;
                        _root = root;
                        OdinMigrationApplySceneScope.CloseLoadedScenes(record, root);
                        RequireIdle();
                        OdinMigrationApplySceneScope.ValidateEditorState(record.affectedPaths);
                        ValidatePointer();
                        if (
                            !compilation.TryValidateSources(
                                current.Sources,
                                out string latestFailure
                            )
                        )
                        {
                            throw new IOException(latestFailure);
                        }
                        OdinMigrationApplySnapshot.ValidateFiles(record, original: true);
                        record.ValidateCurrent(root);
                    }
                );
                if (!applied)
                {
                    if (_active != null)
                    {
                        RequestRecovery(applyFailure);
                    }
                    apply = _active == null ? null : new OdinMigrationDictionaryApply(root);
                    failure = applyFailure;
                    return false;
                }
                record.state = OdinMigrationApplyState.AwaitingImport;
                record.Write(root);
                _refreshScheduled = true;
                apply = new OdinMigrationDictionaryApply(root);
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                if (_active != null)
                {
                    TryRequestRecovery(exception.Message);
                }
                apply = _active == null ? null : new OdinMigrationDictionaryApply(root);
                failure = exception.Message;
                return false;
            }
        }

        internal static bool TryReadStatus(
            string backupRoot,
            out OdinMigrationApplyState state,
            out string failure
        )
        {
            try
            {
                UnityMainThreadGuard.EnsureMainThread();
                string root = OdinMigrationTool.GetValidatedBackupRoot(backupRoot);
                OdinMigrationApplyRecord record =
                    _active != null
                    && string.Equals(
                        _active.backupName,
                        Path.GetFileName(root),
                        StringComparison.Ordinal
                    )
                        ? _active
                    : _last != null
                    && string.Equals(
                        _last.backupName,
                        Path.GetFileName(root),
                        StringComparison.Ordinal
                    )
                        ? _last
                    : null;
                if (record == null)
                {
                    if (!OdinMigrationApplyRecord.TryRead(root, out record, out failure))
                    {
                        state = OdinMigrationApplyState.RecoveryFailed;
                        return false;
                    }
                    _last = record;
                }
                state = record.state;
                failure = record.failure;
                return true;
            }
            catch (Exception exception)
            {
                state = OdinMigrationApplyState.RecoveryFailed;
                failure = exception.Message;
                return false;
            }
        }

        internal static bool TryRecover(string backupRoot, string reason, out string failure)
        {
            try
            {
                UnityMainThreadGuard.EnsureMainThread();
                string root = OdinMigrationTool.GetValidatedBackupRoot(backupRoot);
                if (
                    _active == null
                    || !string.Equals(root, _root, StringComparison.Ordinal)
                    || _active.state == OdinMigrationApplyState.Verified
                    || _active.state == OdinMigrationApplyState.Recovered
                )
                {
                    throw new IOException(
                        "Only the active dictionary migration can request recovery."
                    );
                }
                ValidatePointer();
                RequestRecovery(reason);
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        internal static void ReloadActive()
        {
            try
            {
                if (!File.Exists(PointerPath))
                {
                    return;
                }
                OdinMigrationTool.EnsureNoReparsePoints(
                    OdinMigrationApplySnapshot.ProjectRoot,
                    PointerPath
                );
                string name = new UTF8Encoding(false, true).GetString(
                    OdinMigrationBackupReceipt.ReadBounded(PointerPath, 128)
                );
                foreach (char character in name)
                {
                    if (!char.IsLetterOrDigit(character) && character != '-')
                    {
                        throw new IOException(
                            "The active migration pointer contains an invalid directory name."
                        );
                    }
                }
                _root = OdinMigrationTool.GetValidatedBackupRoot(
                    Path.Combine(
                        OdinMigrationApplySnapshot.ProjectRoot,
                        "Library",
                        "WallstopOdinMigrationBackups",
                        name
                    )
                );
                if (
                    !OdinMigrationApplyRecord.TryRead(
                        _root,
                        out OdinMigrationApplyRecord record,
                        out string failure
                    )
                )
                {
                    throw new IOException(failure);
                }
                _active = record;
                if (
                    record.state == OdinMigrationApplyState.Verified
                    || record.state == OdinMigrationApplyState.Recovered
                )
                {
                    Finish();
                }
                else if (record.state != OdinMigrationApplyState.RecoveryFailed)
                {
                    _refreshScheduled = true;
                }
            }
            catch (Exception exception)
            {
                _active = null;
                UnityEngine.Debug.LogWarning(
                    "Dictionary migration recovery needs manual attention: " + exception.Message
                );
            }
        }

        private static bool AssembliesChanged()
        {
            foreach (OdinMigrationApplyRecord.AssemblySnapshot assembly in _active.assemblies)
            {
                if (
                    string.Equals(
                        OdinMigrationApplySnapshot.ModuleVersion(assembly.name),
                        assembly.moduleVersion,
                        StringComparison.Ordinal
                    )
                )
                {
                    return false;
                }
            }
            return true;
        }

        private static bool AnyAssemblyChanged()
        {
            foreach (OdinMigrationApplyRecord.AssemblySnapshot assembly in _active.assemblies)
            {
                if (
                    !string.Equals(
                        OdinMigrationApplySnapshot.ModuleVersion(assembly.name),
                        assembly.moduleVersion,
                        StringComparison.Ordinal
                    )
                )
                {
                    return true;
                }
            }
            return false;
        }

        private static void CompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (
                _active == null
                || (
                    _active.state != OdinMigrationApplyState.AwaitingImport
                    && _active.state != OdinMigrationApplyState.AwaitingRecoveryImport
                )
            )
            {
                return;
            }
            foreach (CompilerMessage message in messages)
            {
                if (message.type == CompilerMessageType.Error)
                {
                    _active.compilationFailed = true;
                    string text = message.message ?? "Unknown compiler error.";
                    _active.failure =
                        "Native source compilation failed: "
                        + text.Substring(0, Math.Min(text.Length, 4096));
                    try
                    {
                        _active.Write(_root);
                    }
                    catch (Exception exception)
                    {
                        _active.state = OdinMigrationApplyState.RecoveryFailed;
                        _active.failure +=
                            " Recovery record could not be saved: " + exception.Message;
                    }
                    return;
                }
            }
        }

        private static void Finish()
        {
            ValidatePointer();
            File.Delete(PointerPath);
            _last = _active;
            _active = null;
            _root = null;
            _refreshScheduled = false;
        }

        private static void ValidatePointer()
        {
            OdinMigrationTool.EnsureNoReparsePoints(
                OdinMigrationApplySnapshot.ProjectRoot,
                PointerPath
            );
            string name = new UTF8Encoding(false, true).GetString(
                OdinMigrationBackupReceipt.ReadBounded(PointerPath, 128)
            );
            if (!string.Equals(name, _active.backupName, StringComparison.Ordinal))
            {
                throw new IOException("The active migration pointer changed during completion.");
            }
        }

        private static void RequireIdle()
        {
            if (
                EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode
            )
            {
                throw new IOException(
                    "Wait for compilation, asset import, and Play Mode to finish."
                );
            }
        }

        private static void RequestRecovery(string reason)
        {
            _active.deadlineUtcTicks = DateTime.UtcNow.AddMinutes(15).Ticks;
            _active.failure = reason;
            _active.state = OdinMigrationApplyState.RecoveryRequested;
            _active.Write(_root);
            _refreshScheduled = false;
        }

        private static void TryRequestRecovery(string reason)
        {
            try
            {
                RequestRecovery(reason);
            }
            catch (Exception exception)
            {
                _active.state = OdinMigrationApplyState.RecoveryFailed;
                _active.failure =
                    reason + " Recovery record could not be saved: " + exception.Message;
            }
        }

        private static void Update()
        {
            if (
                _active == null
                || _active.state == OdinMigrationApplyState.RecoveryFailed
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode
            )
            {
                return;
            }
            if (
                !_refreshScheduled
                && !_active.compilationFailed
                && _active.requiresReload
                && (
                    _active.state == OdinMigrationApplyState.AwaitingImport
                    || _active.state == OdinMigrationApplyState.AwaitingRecoveryImport
                )
                && string.Equals(_active.importEpoch, Epoch, StringComparison.Ordinal)
                && DateTime.UtcNow.Ticks < _active.deadlineUtcTicks
            )
            {
                return;
            }
            try
            {
                _active.ValidateCurrent(_root);
                ValidatePointer();
                if (_active.state == OdinMigrationApplyState.Prepared)
                {
                    TryRequestRecovery(
                        "Migration was interrupted before its write phase completed."
                    );
                }
                if (_active.state == OdinMigrationApplyState.RecoveryRequested)
                {
                    OdinMigrationApplySceneScope.ValidateEditorState(_active.affectedPaths);
                    if (!OdinMigrationBackupReceipt.TryRestore(_root, out string recoveryFailure))
                    {
                        throw new IOException(recoveryFailure);
                    }
                    _active.compilationFailed = false;
                    _active.requiresReload = AnyAssemblyChanged();
                    _active.importEpoch = Epoch;
                    _active.state = OdinMigrationApplyState.AwaitingRecoveryImport;
                    _active.Write(_root);
                    _refreshScheduled = true;
                }
                if (DateTime.UtcNow.Ticks < _active.deadlineUtcTicks && _refreshScheduled)
                {
                    _refreshScheduled = false;
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    if (
                        _active != null
                        && _active.requiresReload
                        && string.Equals(_active.importEpoch, Epoch, StringComparison.Ordinal)
                        && !EditorApplication.isCompiling
                    )
                    {
                        CompilationPipeline.RequestScriptCompilation();
                    }
                    return;
                }
                if (_active.compilationFailed || _active.deadlineUtcTicks <= DateTime.UtcNow.Ticks)
                {
                    throw new IOException(
                        _active.failure ?? "Native migration import exceeded its deadline."
                    );
                }
                if (
                    _active.requiresReload
                    && string.Equals(_active.importEpoch, Epoch, StringComparison.Ordinal)
                )
                {
                    return;
                }
                bool recovery = _active.state == OdinMigrationApplyState.AwaitingRecoveryImport;
                OdinMigrationApplySnapshot.ValidateFiles(_active, recovery);
                OdinMigrationApplySceneScope.ValidateEditorState(_active.affectedPaths);
                if (!recovery)
                {
                    if (!AssembliesChanged())
                    {
                        throw new IOException(
                            "A rewritten source assembly did not load its replacement compilation."
                        );
                    }
                    foreach (OdinMigrationApplyRecord.Document document in _active.documents)
                    {
                        Object owner = OdinMigrationApplySceneScope.FindOwner(
                            _active,
                            _root,
                            document
                        );
                        if (
                            !OdinMigrationDictionaryImportVerifier.TryVerifyCaptured(
                                document,
                                owner,
                                out string verificationFailure
                            )
                        )
                        {
                            throw new IOException(verificationFailure);
                        }
                    }
                }
                else
                {
                    foreach (OdinMigrationApplyRecord.Document document in _active.documents)
                    {
                        if (document.liveCaptured)
                        {
                            Object owner = OdinMigrationApplySceneScope.FindOwner(
                                _active,
                                _root,
                                document
                            );
                            OdinMigrationLiveDocument.Restore(_active, _root, document, owner);
                        }
                    }
                }
                OdinMigrationApplySceneScope.Complete(_active, _root);
                OdinMigrationApplySceneScope.ValidateEditorState(_active.affectedPaths);
                OdinMigrationApplySnapshot.ValidateFiles(_active, recovery);
                _active.state = recovery
                    ? OdinMigrationApplyState.Recovered
                    : OdinMigrationApplyState.Verified;
                _active.Write(_root);
                Finish();
            }
            catch (Exception exception)
            {
                if (_active != null)
                {
                    if (
                        _active.state == OdinMigrationApplyState.RecoveryRequested
                        || _active.state == OdinMigrationApplyState.AwaitingRecoveryImport
                        || _active.state == OdinMigrationApplyState.Verified
                        || _active.state == OdinMigrationApplyState.Recovered
                    )
                    {
                        _active.state = OdinMigrationApplyState.RecoveryFailed;
                        _active.failure =
                            (_active.failure ?? "") + " Recovery failed: " + exception.Message;
                        try
                        {
                            _active.Write(_root);
                        }
                        catch (Exception saveFailure)
                        {
                            _active.failure +=
                                " Recovery record could not be saved: " + saveFailure.Message;
                        }
                    }
                    else
                    {
                        TryRequestRecovery(exception.Message);
                    }
                }
            }
        }

        private static void WritePointer(string name)
        {
            OdinMigrationTool.EnsureNoReparsePoints(
                OdinMigrationApplySnapshot.ProjectRoot,
                PointerPath
            );
            if (File.Exists(PointerPath))
            {
                throw new IOException("An active dictionary migration pointer already exists.");
            }
            if (!DurableFile.TryWriteAllText(PointerPath, name, out Exception failure))
            {
                throw new IOException(
                    "Could not retain the active dictionary migration pointer.",
                    failure
                );
            }
        }
    }
}
