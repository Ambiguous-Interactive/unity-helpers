// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*

 * Prefab-stage types moved out of Experimental in Unity 2021.2; the 2021.1 references still use the old

 * namespace.

 */
namespace UnityEditor.SceneManagement
{
    using UnityEngine;
    using UnityEngine.SceneManagement;

    public class PrefabStage : ScriptableObject
    {
        public Scene scene => default;
        public string assetPath => null;
        public GameObject prefabContentsRoot => null;
    }

    public static class PrefabStageUtility
    {
        public static PrefabStage GetPrefabStage(GameObject gameObject) => null;

        public static PrefabStage GetCurrentPrefabStage() => null;
    }
}
