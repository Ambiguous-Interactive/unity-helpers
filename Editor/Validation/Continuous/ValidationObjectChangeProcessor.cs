// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using Object = UnityEngine.Object;

    [InitializeOnLoad]
    internal static class ValidationObjectChangeProcessor
    {
        private static readonly Dictionary<string, GameObject> LivePrefabRoots =
            new Dictionary<string, GameObject>();

        static ValidationObjectChangeProcessor()
        {
            Undo.postprocessModifications += Modified;
            Undo.undoRedoPerformed += QueueDirtyScenes;
            EditorApplication.hierarchyChanged += QueueDirtyScenes;
        }

        private static UndoPropertyModification[] Modified(UndoPropertyModification[] modifications)
        {
            if (!ValidationAutoRun.Enabled || modifications == null)
                return modifications;
            foreach (UndoPropertyModification modification in modifications)
                QueueObject(modification.currentValue.target);
            return modifications;
        }

        private static void QueueObject(Object subject)
        {
            if (subject == null)
                return;
            GameObject gameObject = subject is Component component
                ? component.gameObject
                : subject as GameObject;
            string path = AssetDatabase.GetAssetPath(subject);
            if (gameObject != null && string.IsNullOrEmpty(path))
            {
                path = gameObject.scene.path;
                if (
                    string.IsNullOrEmpty(path)
                    || !path.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase)
                )
                {
                    PrefabStage stage = PrefabStageUtility.GetPrefabStage(gameObject);
                    if (stage != null)
                    {
                        path = stage.assetPath;
                        LivePrefabRoots[AssetDatabase.AssetPathToGUID(path)] =
                            stage.prefabContentsRoot;
                    }
                }
            }
            if (!string.IsNullOrEmpty(path))
                ValidationAutoRun.Queue(new[] { AssetDatabase.AssetPathToGUID(path) }, false, 0);
        }

        private static void QueueDirtyScenes()
        {
            if (!ValidationAutoRun.Enabled)
                return;
            List<string> guids = new List<string>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded && scene.isDirty && !string.IsNullOrEmpty(scene.path))
                    guids.Add(AssetDatabase.AssetPathToGUID(scene.path));
            }
            ValidationAutoRun.Queue(guids, false, 0);
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.scene.isDirty)
                QueueObject(stage.prefabContentsRoot);
        }

        internal static Object Load(ValidationTarget target)
        {
            if (LivePrefabRoots.TryGetValue(target.AssetGuid, out GameObject root))
            {
                if (root != null)
                    return root;
                LivePrefabRoots.Remove(target.AssetGuid);
            }
            return AssetDatabase.LoadMainAssetAtPath(target.AssetPath);
        }
    }
#endif
}
