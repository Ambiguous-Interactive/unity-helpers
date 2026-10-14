// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using Object = UnityEngine.Object;

    internal static class OdinMigrationApplySceneScope
    {
        internal static void CaptureLoadedScenes(OdinMigrationApplyRecord record)
        {
            ValidateEditorState(record.affectedPaths);
            HashSet<string> affected = new HashSet<string>(
                record.affectedPaths,
                StringComparer.Ordinal
            );
            List<string> loaded = new List<string>();
            int sceneCount = SceneManager.sceneCount;
            for (int index = 0; index < sceneCount; ++index)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded && affected.Contains(scene.path))
                {
                    loaded.Add(scene.path);
                }
            }
            record.originallyLoadedScenes = loaded.ToArray();
        }

        internal static void CloseLoadedScenes(OdinMigrationApplyRecord record, string backupRoot)
        {
            ValidateEditorState(record.affectedPaths);
            foreach (string path in record.originallyLoadedScenes)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }
                if (SceneManager.sceneCount == 1)
                {
                    record.temporaryScene = "OdinMigration-" + record.backupName;
                    record.Write(backupRoot);
                    Scene temporary = EditorSceneManager.NewScene(
                        NewSceneSetup.EmptyScene,
                        NewSceneMode.Additive
                    );
                    temporary.name = record.temporaryScene;
                }
                if (!EditorSceneManager.CloseScene(scene, removeScene: true))
                {
                    throw new IOException(
                        "Could not close an affected saved scene before source changes: " + path
                    );
                }
            }
        }

        internal static void Complete(OdinMigrationApplyRecord record, string backupRoot)
        {
            foreach (string path in record.originallyLoadedScenes)
            {
                EnsureScene(record, backupRoot, path);
            }
            HashSet<string> originals = new HashSet<string>(
                record.originallyLoadedScenes,
                StringComparer.Ordinal
            );
            foreach (string path in record.openedScenes)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                if (scene.IsValid() && scene.isLoaded && !originals.Contains(path))
                {
                    if (scene.isDirty || !EditorSceneManager.CloseScene(scene, removeScene: true))
                    {
                        throw new IOException(
                            "An opened migration scene has changes or cannot be closed: " + path
                        );
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(record.originalActiveScene))
            {
                Scene active = SceneManager.GetSceneByPath(record.originalActiveScene);
                if (active.IsValid() && active.isLoaded)
                {
                    SceneManager.SetActiveScene(active);
                }
            }
            if (!string.IsNullOrWhiteSpace(record.temporaryScene))
            {
                Scene temporary = SceneManager.GetSceneByName(record.temporaryScene);
                if (temporary.IsValid() && temporary.isLoaded)
                {
                    if (
                        temporary.isDirty
                        || temporary.rootCount != 0
                        || !EditorSceneManager.CloseScene(temporary, removeScene: true)
                    )
                    {
                        throw new IOException(
                            "The temporary migration scene contains changes or cannot be closed."
                        );
                    }
                }
            }
        }

        internal static Object FindOwner(
            OdinMigrationApplyRecord record,
            string backupRoot,
            OdinMigrationApplyRecord.Document document
        )
        {
            OdinMigrationObjectReferenceValidator identity =
                new OdinMigrationObjectReferenceValidator(document.owningGuid);
            foreach (Object candidate in LoadObjects(record, backupRoot, document.assetPath))
            {
                if (
                    candidate != null
                    && identity.MatchesIdentity(candidate, document.owningGuid, document.fileId)
                )
                {
                    return candidate;
                }
            }
            throw new IOException(
                "The migrated saved document has no loaded owner: " + document.assetPath
            );
        }

        internal static IReadOnlyList<Object> LoadObjects(
            OdinMigrationApplyRecord record,
            string backupRoot,
            string assetPath
        )
        {
            OdinMigrationTool.GetValidatedAssetPath(assetPath);
            List<Object> objects = new List<Object>();
            if (assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                Scene scene = EnsureScene(record, backupRoot, assetPath);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    AddHierarchy(root, objects);
                }
            }
            else
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                {
                    if (asset != null)
                    {
                        objects.Add(asset);
                        if (asset is GameObject root)
                        {
                            AddHierarchy(root, objects);
                        }
                    }
                }
            }
            return objects.AsReadOnly();
        }

        internal static void ValidateEditorState(IReadOnlyList<string> affectedPaths)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new IOException(
                    "Stop Play Mode before changing migration sources and assets."
                );
            }
            if (!StageUtility.GetCurrentStageHandle().Equals(StageUtility.GetMainStageHandle()))
            {
                throw new IOException(
                    "Close custom and prefab stages before changing migration sources."
                );
            }
            int sceneCount = SceneManager.sceneCount;
            for (int index = 0; index < sceneCount; ++index)
            {
                if (SceneManager.GetSceneAt(index).isDirty)
                {
                    throw new IOException(
                        "Save or discard scene changes before changing migration sources."
                    );
                }
            }
            HashSet<string> affected = new HashSet<string>(affectedPaths, StringComparer.Ordinal);
            foreach (Object loaded in Resources.FindObjectsOfTypeAll<Object>())
            {
                if (
                    loaded != null
                    && EditorUtility.IsPersistent(loaded)
                    && affected.Contains(AssetDatabase.GetAssetPath(loaded))
                    && EditorUtility.IsDirty(loaded)
                )
                {
                    throw new IOException(
                        "An affected loaded asset has unsaved changes: "
                            + AssetDatabase.GetAssetPath(loaded)
                    );
                }
            }
        }

        private static void AddHierarchy(GameObject root, List<Object> objects)
        {
            foreach (
                Transform transform in root.GetComponentsInChildren<Transform>(
                    includeInactive: true
                )
            )
            {
                objects.Add(transform.gameObject);
                foreach (Component component in transform.GetComponents<Component>())
                {
                    if (component != null)
                    {
                        objects.Add(component);
                    }
                }
            }
        }

        private static Scene EnsureScene(
            OdinMigrationApplyRecord record,
            string backupRoot,
            string assetPath
        )
        {
            Scene existing = SceneManager.GetSceneByPath(assetPath);
            if (existing.IsValid() && existing.isLoaded)
            {
                return existing;
            }
            List<string> paths = new List<string>(record.openedScenes);
            if (!paths.Contains(assetPath))
            {
                paths.Add(assetPath);
                record.openedScenes = paths.ToArray();
                record.Write(backupRoot);
            }
            return EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Additive);
        }
    }
}
