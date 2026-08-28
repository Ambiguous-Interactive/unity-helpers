// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Reference-only shim for the two prefab-stage types the package reaches under UNITY_EDITOR.
//
// They MOVED namespace in Unity 2021.2: `UnityEditor.Experimental.SceneManagement` became
// `UnityEditor.SceneManagement`. The newest community UnityEditor reference assembly published to
// nuget.org is `Unity3D.SDK` 2021.1.14, which still declares them under Experimental, so the
// package's (correct, 2021.3+) `using UnityEditor.SceneManagement` does not bind against it. This
// is a harness gap, not a package one -- every editor CI runs is 2021.3 or newer.
//
// Only the members the package binds are declared. Declaring one Unity does not have would let a
// genuine error through.
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
