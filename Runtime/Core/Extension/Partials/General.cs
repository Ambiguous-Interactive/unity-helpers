// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    /// <summary>
    /// General-purpose helpers such as JSON formatting, input filtering, and scene membership checks.
    /// </summary>
    public static partial class UnityExtensions
    {
        /// <summary>
        /// Converts a Vector3 to a JSON-formatted string representation.
        /// </summary>
        public static string ToJsonString(this Vector3 vector)
        {
            return FormattableString.Invariant($"{{{vector.x}, {vector.y}, {vector.z}}}");
        }

        /// <summary>
        /// Converts a Vector2 to a JSON-formatted string representation.
        /// </summary>
        public static string ToJsonString(this Vector2 vector)
        {
            return FormattableString.Invariant($"{{{vector.x}, {vector.y}}}");
        }

        /// <summary>
        /// Determines if a Vector2 represents insignificant input (noise) below a threshold.
        /// </summary>
        public static bool IsNoise(this Vector2 inputVector, float threshold = 0.2f)
        {
            float limit = Mathf.Abs(threshold);
            return Mathf.Abs(inputVector.x) <= limit && Mathf.Abs(inputVector.y) <= limit;
        }

        private const string DontDestroyOnLoadSceneName = "DontDestroyOnLoad";

        private static int _dontDestroyOnLoadSceneHandle;
        private static int _sceneHandleKnownNotDontDestroyOnLoad;

        /// <summary>
        /// Determines if a GameObject is in the DontDestroyOnLoad scene.
        /// </summary>
        /// <remarks>
        /// Allocation-free once the scene has been seen. Reading <c>Scene.name</c> marshals a fresh
        /// managed string out of native memory on every call, and this predicate reads cheap enough
        /// to end up in <c>Update</c>, so the answer is cached against the scene's handle -- a value
        /// type whose comparison costs nothing. The name is consulted only for a handle neither
        /// cache has seen.
        /// </remarks>
        public static bool IsDontDestroyOnLoad(this GameObject gameObjectToCheck)
        {
            if (gameObjectToCheck == null)
            {
                return false;
            }

            Scene scene = gameObjectToCheck.scene;
            int handle = scene.handle;
            if (handle != 0)
            {
                if (handle == _dontDestroyOnLoadSceneHandle)
                {
                    return true;
                }

                if (handle == _sceneHandleKnownNotDontDestroyOnLoad)
                {
                    return false;
                }
            }

            bool isDontDestroyOnLoad = string.Equals(
                scene.name,
                DontDestroyOnLoadSceneName,
                StringComparison.Ordinal
            );

            if (handle != 0)
            {
                if (isDontDestroyOnLoad)
                {
                    _dontDestroyOnLoadSceneHandle = handle;
                }
                else
                {
                    _sceneHandleKnownNotDontDestroyOnLoad = handle;
                }
            }

            return isDontDestroyOnLoad;
        }

        /// <remarks>
        /// A scene handle is unique for the lifetime of the player, but statics survive entering
        /// play mode when the project disables domain reload, and the next session's scenes start
        /// numbering again. Clearing here -- the earliest point every play session reaches -- keeps
        /// a stale handle from answering for a scene that merely reuses its number.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSceneResidencyCache()
        {
            _dontDestroyOnLoadSceneHandle = 0;
            _sceneHandleKnownNotDontDestroyOnLoad = 0;
        }
    }
}
