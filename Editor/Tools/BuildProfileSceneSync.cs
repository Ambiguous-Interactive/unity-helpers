// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR && UNITY_6000_0_OR_NEWER
    using System;
    using System.Collections.Generic;
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
                    using SerializedObject serialized = new(profile);
                    serialized.Update();
                    using SerializedProperty scenes = serialized.FindProperty(ScenesPropertyName);
                    if (scenes == null)
                    {
                        updatedProfiles = updated;
                        error =
                            "Unity did not expose the build profile scene list: "
                            + AssetDatabase.GetAssetPath(profile);
                        return false;
                    }
                    if (ScenesMatch(scenes, globalScenes))
                    {
                        continue;
                    }
                    Undo.RecordObject(profile, UndoName);
                    scenes.arraySize = globalScenes.Length;
                    for (int index = 0; index < globalScenes.Length; ++index)
                    {
                        EditorBuildSettingsScene source = globalScenes[index];
                        using SerializedProperty entry = scenes.GetArrayElementAtIndex(index);
                        entry.boxedValue = new EditorBuildSettingsScene
                        {
                            guid = source.guid,
                            path = source.path ?? string.Empty,
                            enabled = source.enabled,
                        };
                    }
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(profile);
                    Undo.FlushUndoRecordObjects();
                    AssetDatabase.SaveAssetIfDirty(profile);
                    if (EditorUtility.IsDirty(profile))
                    {
                        updatedProfiles = updated;
                        error =
                            "Unity left the build profile unsaved: "
                            + AssetDatabase.GetAssetPath(profile);
                        return false;
                    }
                    ++updated;
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
