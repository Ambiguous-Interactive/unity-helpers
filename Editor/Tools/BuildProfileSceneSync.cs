// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR && UNITY_6000_0_OR_NEWER
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using UnityEditor;
    using UnityEditor.Build.Profile;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>Keeps inherited build-profile scene lists equal to the global scene list.</summary>
    /// <remarks>
    /// Unity 6 only. Custom scene lists are preserved. Automatic synchronization is opt-in.
    /// Call TrySynchronize after programmatic globalScenes assignments, which may not raise
    /// Unity's sceneListChanged event.
    /// </remarks>
    [InitializeOnLoad]
    public static class BuildProfileSceneSync
    {
        private const string ScenesPropertyName = "m_Scenes";
        private const string UndoName = "Sync Build Profile Scenes";
        private static bool _scheduled;
        private static bool _synchronizing;

        static BuildProfileSceneSync()
        {
            EditorBuildSettings.sceneListChanged += Schedule;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            UnityHelpersSettings.OnSettingsSaved += Schedule;
            Schedule();
        }

        /// <summary>Synchronizes inherited profiles in existing project folders and saves changed profiles.</summary>
        /// <remarks>
        /// Null folders search Assets; an empty collection changes nothing. Invalid folders are refused.
        /// Changes record Undo, but saved files require saving again after Undo. A failed batch may have
        /// updated earlier profiles. Profiles with custom scene lists are never changed.
        /// </remarks>
        public static bool TrySynchronize(
            IEnumerable<string> profileFolders,
            out int updatedProfiles,
            out string error
        )
        {
            if (
                _synchronizing
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode
                || BuildPipeline.isBuildingPlayer
            )
            {
                updatedProfiles = 0;
                error = "Build profile scenes can only be synchronized in an idle Editor.";
                return false;
            }

            int updated = 0;
            _synchronizing = true;
            try
            {
                using PooledResource<List<string>> folderLease = Buffers<string>.List.Get(
                    out List<string> folders
                );
                if (profileFolders == null)
                {
                    folders.Add("Assets");
                }
                else
                {
                    foreach (string folder in profileFolders.AsValueEnumerableRequired())
                    {
                        if (string.IsNullOrWhiteSpace(folder))
                        {
                            updatedProfiles = 0;
                            error =
                                "Every profile search folder must name an existing project folder.";
                            return false;
                        }
                        string normalized = folder.SanitizePath();
                        if (
                            !AssetDatabase.IsValidFolder(normalized)
                            || (
                                normalized != "Assets"
                                && !normalized.StartsWith("Assets/", StringComparison.Ordinal)
                            )
                        )
                        {
                            updatedProfiles = 0;
                            error =
                                "Every profile search folder must be below Assets and already exist.";
                            return false;
                        }
                        folders.Add(normalized);
                    }
                }

                EditorBuildSettingsScene[] globalScenes = EditorBuildSettings.globalScenes;
                using UndoGroupScope undo = UndoGroupScope.Begin(UndoName);
                if (!undo.IsHeld)
                {
                    updatedProfiles = 0;
                    error = "Could not start the build profile Undo group.";
                    return false;
                }
                foreach (BuildProfile profile in Helpers.EnumerateAssets<BuildProfile>(folders))
                {
                    if (profile.overrideGlobalScenes)
                    {
                        continue;
                    }
                    if (
                        !TrySynchronizeProfile(
                            profile,
                            globalScenes,
                            out bool changed,
                            out string profileError
                        )
                    )
                    {
                        updatedProfiles = updated;
                        error = profileError;
                        return false;
                    }
                    if (changed)
                    {
                        ++updated;
                    }
                }

                updatedProfiles = updated;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                updatedProfiles = updated;
                error = exception.Message;
                return false;
            }
            finally
            {
                _synchronizing = false;
            }
        }

        /// <summary>Synchronizes inherited profiles in the configured project folders.</summary>
        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Sync Build Profile Scenes")]
        public static void SynchronizeConfiguredProfiles()
        {
            try
            {
                IReadOnlyList<string> folders = UnityHelpersSettings.GetBuildProfileSceneFolders();
                if (!TrySynchronize(folders, out _, out string error))
                {
                    Debug.LogWarning(error);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(exception.Message);
            }
        }

        private static bool TrySynchronizeProfile(
            BuildProfile profile,
            EditorBuildSettingsScene[] globalScenes,
            out bool changed,
            out string error
        )
        {
            using SerializedObject serialized = new(profile);
            serialized.Update();
            using SerializedProperty scenes = serialized.FindProperty(ScenesPropertyName);
            if (scenes == null)
            {
                changed = false;
                error =
                    "Unity did not expose the build profile scene list: "
                    + AssetDatabase.GetAssetPath(profile);
                return false;
            }
            if (ScenesMatch(scenes, globalScenes))
            {
                changed = false;
                error = null;
                return true;
            }
            EditorBuildSettingsScene[] previous = new EditorBuildSettingsScene[scenes.arraySize];
            for (int index = 0; index < previous.Length; ++index)
            {
                using SerializedProperty entry = scenes.GetArrayElementAtIndex(index);
                previous[index] = entry.boxedValue as EditorBuildSettingsScene;
            }
            try
            {
                string path = AssetDatabase.GetAssetPath(profile);
                string absolutePath = Path.Combine(
                        Path.GetDirectoryName(Application.dataPath),
                        path
                    )
                    .SanitizePath();
                Undo.RecordObject(profile, UndoName);
                WriteScenes(scenes, globalScenes);
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(profile);
                Undo.FlushUndoRecordObjects();
                string nativeSaveError = null;
                Application.LogCallback captureSaveError = (message, _, type) =>
                {
                    if (
                        (
                            type == LogType.Error
                            || type == LogType.Exception
                            || type == LogType.Assert
                        )
                        && message != null
                    )
                    {
                        string normalizedMessage = message.Replace('\\', '/');
                        if (
                            Helpers.HasAssetPathToken(normalizedMessage, path)
                            || Helpers.HasAssetPathToken(normalizedMessage, absolutePath)
                        )
                        {
                            Interlocked.CompareExchange(ref nativeSaveError, message, null);
                        }
                    }
                };
                Application.logMessageReceivedThreaded += captureSaveError;
                try
                {
                    AssetDatabase.SaveAssetIfDirty(profile);
                }
                finally
                {
                    Application.logMessageReceivedThreaded -= captureSaveError;
                }
                string saveFailure = Volatile.Read(ref nativeSaveError);
                if (
                    EditorUtility.IsDirty(profile)
                    || saveFailure != null
                    || !File.Exists(absolutePath)
                )
                {
                    serialized.Update();
                    WriteScenes(scenes, previous);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(profile);
                    changed = false;
                    error =
                        saveFailure ?? "Unity did not persist the changed build profile: " + path;
                    return false;
                }
            }
            catch (Exception exception)
            {
                string failure = exception.Message;
                try
                {
                    serialized.Update();
                    WriteScenes(scenes, previous);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(profile);
                }
                catch (Exception restoreException)
                {
                    failure +=
                        " Could not restore the previous scene list: " + restoreException.Message;
                }
                changed = false;
                error = failure;
                return false;
            }
            changed = true;
            error = null;
            return true;
        }

        private static void WriteScenes(
            SerializedProperty scenes,
            EditorBuildSettingsScene[] values
        )
        {
            scenes.arraySize = values.Length;
            for (int index = 0; index < values.Length; ++index)
            {
                EditorBuildSettingsScene source = values[index];
                using SerializedProperty entry = scenes.GetArrayElementAtIndex(index);
                entry.boxedValue = new EditorBuildSettingsScene
                {
                    guid = source.guid,
                    path = source.path ?? string.Empty,
                    enabled = source.enabled,
                };
            }
        }

        private static bool ScenesMatch(
            SerializedProperty scenes,
            EditorBuildSettingsScene[] expected
        )
        {
            if (scenes.arraySize != expected.Length)
            {
                return false;
            }
            for (int index = 0; index < expected.Length; ++index)
            {
                using SerializedProperty entry = scenes.GetArrayElementAtIndex(index);
                if (
                    entry.boxedValue is not EditorBuildSettingsScene actual
                    || actual.guid != expected[index].guid
                    || !string.Equals(
                        actual.path ?? string.Empty,
                        expected[index].path ?? string.Empty,
                        StringComparison.Ordinal
                    )
                    || actual.enabled != expected[index].enabled
                )
                {
                    return false;
                }
            }
            return true;
        }

        private static void Schedule()
        {
            if (_scheduled)
            {
                return;
            }
            _scheduled = true;
            EditorApplication.update += RunScheduled;
        }

        private static void RunScheduled()
        {
            EditorApplication.update -= RunScheduled;
            _scheduled = false;
            try
            {
                if (_synchronizing || EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    Schedule();
                    return;
                }
                if (!UnityHelpersSettings.GetAutoSynchronizeBuildProfileScenes())
                {
                    return;
                }
                if (
                    !EditorApplication.isPlayingOrWillChangePlaymode
                    && !BuildPipeline.isBuildingPlayer
                )
                {
                    SynchronizeConfiguredProfiles();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(exception.Message);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                Schedule();
            }
        }
    }
#endif
}
