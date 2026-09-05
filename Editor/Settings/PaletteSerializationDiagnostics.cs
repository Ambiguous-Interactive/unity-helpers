// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#pragma warning disable CS0162
namespace WallstopStudios.UnityHelpers.Editor.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Centralizes palette serialization logging so drawers and settings UI can emit consistent diagnostics.
    /// </summary>
    internal static class PaletteSerializationDiagnostics
    {
        private const string LogPrefix = "[UnityHelpers][PaletteSerialization]";
        private const string CommitLogPrefix = "[UnityHelpers][CommitEntry]";

#if UNITY_HELPERS_PALETTE_DIAGNOSTICS
        // Define UNITY_HELPERS_PALETTE_DIAGNOSTICS to re-enable verbose palette logging during investigations.
        private const bool DiagnosticsEnabled = true;
#else
        private const bool DiagnosticsEnabled = false;
#endif

#if UNITY_HELPERS_COMMIT_DIAGNOSTICS
        // Define UNITY_HELPERS_COMMIT_DIAGNOSTICS to enable verbose logging for dictionary/set add operations.
        private const bool CommitDiagnosticsEnabled = true;
#else
        private const bool CommitDiagnosticsEnabled = false;
#endif

        /// <summary>
        /// Reports the start of a dictionary CommitEntry operation.
        /// </summary>
        internal static void ReportCommitEntryStart(
            SerializedObject serializedObject,
            string propertyPath,
            int keysArraySizeBefore,
            object keyValue,
            object valueValue,
            int existingIndex
        )
        {
            if (!CommitDiagnosticsEnabled)
            {
                return;
            }

            bool isSettings = IsUnityHelpersSettings(serializedObject);
            string message =
                $"{CommitLogPrefix} START property={propertyPath} isSettings={isSettings} keysBefore={keysArraySizeBefore} existingIndex={existingIndex} key={keyValue ?? "<null>"} value={valueValue ?? "<null>"}";
            Debug.Log(message, serializedObject?.targetObject);
        }

        /// <summary>
        /// Reports array insertion during CommitEntry.
        /// </summary>
        internal static void ReportCommitEntryArrayInsert(
            SerializedObject serializedObject,
            string propertyPath,
            int insertIndex,
            int keysArraySizeAfter,
            int valuesArraySizeAfter
        )
        {
            if (!CommitDiagnosticsEnabled)
            {
                return;
            }

            string message =
                $"{CommitLogPrefix} ARRAY_INSERT property={propertyPath} insertIndex={insertIndex} keysAfter={keysArraySizeAfter} valuesAfter={valuesArraySizeAfter}";
            Debug.Log(message, serializedObject?.targetObject);
        }

        /// <summary>
        /// Reports the ApplyModifiedProperties result during CommitEntry.
        /// </summary>
        internal static void ReportCommitEntryApplyResult(
            SerializedObject serializedObject,
            string propertyPath,
            bool applyResult,
            bool hadModifiedPropertiesBefore,
            bool hasModifiedPropertiesAfter
        )
        {
            if (!CommitDiagnosticsEnabled)
            {
                return;
            }

            string message =
                $"{CommitLogPrefix} APPLY property={propertyPath} result={applyResult} dirtyBefore={hadModifiedPropertiesBefore} dirtyAfter={hasModifiedPropertiesAfter}";
            Debug.Log(message, serializedObject?.targetObject);
        }

        /// <summary>
        /// Reports the SyncRuntimeDictionary operation.
        /// </summary>
        internal static void ReportSyncRuntimeDictionary(
            SerializedObject serializedObject,
            string propertyPath,
            object dictionaryInstance,
            bool isSerializableDictionaryBase,
            bool calledSaveSettings
        )
        {
            if (!CommitDiagnosticsEnabled)
            {
                return;
            }

            string instanceType = dictionaryInstance?.GetType().Name ?? "<null>";
            string message =
                $"{CommitLogPrefix} SYNC_RUNTIME property={propertyPath} instanceType={instanceType} isSerializableDictionaryBase={isSerializableDictionaryBase} calledSaveSettings={calledSaveSettings}";
            Debug.Log(message, serializedObject?.targetObject);
        }

        /// <summary>
        /// Reports the completion of CommitEntry.
        /// </summary>
        internal static void ReportCommitEntryComplete(
            SerializedObject serializedObject,
            string propertyPath,
            bool added,
            int affectedIndex,
            int finalKeysArraySize
        )
        {
            if (!CommitDiagnosticsEnabled)
            {
                return;
            }

            string message =
                $"{CommitLogPrefix} COMPLETE property={propertyPath} added={added} index={affectedIndex} finalKeysSize={finalKeysArraySize}";
            Debug.Log(message, serializedObject?.targetObject);
        }

        /// <summary>
        /// Reports the start of a set TryCommitPendingEntry operation.
        /// </summary>
        internal static void ReportSetCommitStart(
            SerializedObject serializedObject,
            string propertyPath,
            int itemsArraySizeBefore,
            object value
        )
        {
            if (!CommitDiagnosticsEnabled)
            {
                return;
            }

            bool isSettings = IsUnityHelpersSettings(serializedObject);
            string message =
                $"{CommitLogPrefix} SET_START property={propertyPath} isSettings={isSettings} itemsBefore={itemsArraySizeBefore} value={value ?? "<null>"}";
            Debug.Log(message, serializedObject?.targetObject);
        }

        /// <summary>
        /// Reports set add operation result.
        /// </summary>
        internal static void ReportSetAddResult(
            SerializedObject serializedObject,
            string propertyPath,
            bool success,
            string errorMessage
        )
        {
            if (!CommitDiagnosticsEnabled)
            {
                return;
            }

            string message =
                $"{CommitLogPrefix} SET_ADD property={propertyPath} success={success} error={errorMessage ?? "<none>"}";
            Debug.Log(message, serializedObject?.targetObject);
        }

        /// <summary>
        /// Reports the SyncRuntimeSet operation.
        /// </summary>
        internal static void ReportSyncRuntimeSet(
            SerializedObject serializedObject,
            string propertyPath,
            object setInstance,
            bool isSerializableSetInspector,
            bool calledSaveSettings
        )
        {
            if (!CommitDiagnosticsEnabled)
            {
                return;
            }

            string instanceType = setInstance?.GetType().Name ?? "<null>";
            string message =
                $"{CommitLogPrefix} SET_SYNC property={propertyPath} instanceType={instanceType} isInspector={isSerializableSetInspector} calledSaveSettings={calledSaveSettings}";
            Debug.Log(message, serializedObject?.targetObject);
        }

        /// <summary>
        /// Reports set commit completion.
        /// </summary>
        internal static void ReportSetCommitComplete(
            SerializedObject serializedObject,
            string propertyPath,
            int finalItemsArraySize
        )
        {
            if (!CommitDiagnosticsEnabled)
            {
                return;
            }

            string message =
                $"{CommitLogPrefix} SET_COMPLETE property={propertyPath} finalItemsSize={finalItemsArraySize}";
            Debug.Log(message, serializedObject?.targetObject);
        }

        private static bool IsUnityHelpersSettings(SerializedObject serializedObject)
        {
            if (serializedObject?.targetObject is UnityHelpersSettings)
            {
                return true;
            }

            Object[] targets = serializedObject?.targetObjects;
            if (targets == null)
            {
                return false;
            }

            foreach (UnityEngine.Object targetsElement in targets)
            {
                if (targetsElement is UnityHelpersSettings)
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly string[] PalettePropertyRoots =
        {
            UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors,
            UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors,
        };

        internal static bool IsPaletteProperty(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
            {
                return false;
            }

            foreach (string PalettePropertyRootsElement in PalettePropertyRoots)
            {
                if (propertyPath.StartsWith(PalettePropertyRootsElement, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static void ReportDrawerApplyResult(
            SerializedObject serializedObject,
            string propertyPath,
            string operation,
            DrawerApplyResult result,
            bool hadChangesBefore,
            bool hasChangesAfter
        )
        {
            if (
                !DiagnosticsEnabled
                || !ShouldLog(serializedObject, propertyPath, hadChangesBefore, result)
            )
            {
                return;
            }

            StringBuilder builder = new();
            builder
                .Append(LogPrefix)
                .Append(' ')
                .Append(operation ?? "DrawerApply")
                .Append(" property=")
                .Append(propertyPath ?? "<null>")
                .Append(" result=")
                .Append(result)
                .Append(" hadChangesBefore=")
                .Append(hadChangesBefore)
                .Append(" remainingDirty=")
                .Append(hasChangesAfter)
                .Append(" targets=")
                .Append(serializedObject.targetObjects?.Length ?? 0);

            Object context = serializedObject.targetObject;
            if (result == DrawerApplyResult.Failed)
            {
                Debug.LogWarning(builder.ToString(), context);
                return;
            }

            if (result == DrawerApplyResult.FallbackPathSucceeded)
            {
                Debug.Log(builder.ToString(), context);
                return;
            }

            Debug.Log(builder.ToString(), context);
        }

        internal static void ReportInspectorApplyResult(
            SerializedObject serializedObject,
            bool paletteChanged,
            bool dataChanged,
            bool guiChanged,
            bool applyResult
        )
        {
            if (!DiagnosticsEnabled || !paletteChanged || !ShouldLogForTargets(serializedObject))
            {
                return;
            }

            bool stillDirty = serializedObject != null && serializedObject.hasModifiedProperties;
            string message =
                $"{LogPrefix} InspectorApply result={(applyResult ? "Success" : "Failed")} dataChanged={dataChanged} guiChanged={guiChanged} remainingDirty={stillDirty}";

            Object context = serializedObject?.targetObject;
            if (applyResult)
            {
                Debug.Log(message, context);
            }
            else
            {
                Debug.LogWarning(message, context);
            }
        }

        internal static bool ShouldLogPaletteSort(
            SerializedProperty dictionaryProperty,
            Type keyType
        )
        {
            if (
                !DiagnosticsEnabled
                || dictionaryProperty == null
                || dictionaryProperty.serializedObject == null
                || keyType != typeof(string)
            )
            {
                return false;
            }

            if (!IsPaletteProperty(dictionaryProperty.propertyPath))
            {
                return false;
            }

            return ShouldLogForTargets(dictionaryProperty.serializedObject);
        }

        internal static void ReportDictionarySort(
            SerializedProperty dictionaryProperty,
            IReadOnlyList<string> serializedBefore,
            IReadOnlyList<string> snapshotAfterSort,
            IReadOnlyList<string> serializedAfter
        )
        {
            if (
                !DiagnosticsEnabled
                || dictionaryProperty == null
                || !IsPaletteProperty(dictionaryProperty.propertyPath)
            )
            {
                return;
            }

            SerializedObject serializedObject = dictionaryProperty.serializedObject;
            if (!ShouldLogForTargets(serializedObject))
            {
                return;
            }

            string beforeSequence = FormatKeySequence(serializedBefore);
            string snapshotSequence = FormatKeySequence(snapshotAfterSort);
            string afterSequence = FormatKeySequence(serializedAfter);
            string message =
                $"{LogPrefix} Sort property={dictionaryProperty.propertyPath} before=[{beforeSequence}] snapshot=[{snapshotSequence}] after=[{afterSequence}]";

            Debug.Log(message, serializedObject?.targetObject);
        }

        private static bool ShouldLog(
            SerializedObject serializedObject,
            string propertyPath,
            bool hadChangesBefore,
            DrawerApplyResult result
        )
        {
            if (
                !DiagnosticsEnabled
                || !ShouldLogForTargets(serializedObject)
                || !IsPaletteProperty(propertyPath)
            )
            {
                return false;
            }

            if (!hadChangesBefore && result != DrawerApplyResult.Failed)
            {
                return false;
            }

            return true;
        }

        private static bool ShouldLogForTargets(SerializedObject serializedObject)
        {
            if (!DiagnosticsEnabled || serializedObject == null)
            {
                return false;
            }

            if (serializedObject.targetObject is UnityHelpersSettings)
            {
                return true;
            }

            Object[] targets = serializedObject.targetObjects;
            if (targets == null || targets.Length == 0)
            {
                return false;
            }

            foreach (UnityEngine.Object targetsElement in targets)
            {
                if (targetsElement is UnityHelpersSettings)
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatKeySequence(IReadOnlyList<string> keys)
        {
            if (keys == null || keys.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new();
            for (int index = 0; index < keys.Count; index++)
            {
                if (0 < index)
                {
                    builder.Append(", ");
                }

                builder.Append(keys[index] ?? "<null>");
            }

            return builder.ToString();
        }

        internal enum DrawerApplyResult
        {
            UndoPathSucceeded,
            FallbackPathSucceeded,
            Failed,
        }
    }
}
