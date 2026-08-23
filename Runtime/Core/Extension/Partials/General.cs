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

        /// <summary>
        /// How many scenes can answer from the negative cache before it starts recycling entries.
        /// </summary>
        /// <remarks>
        /// Only consulted before any DontDestroyOnLoad object has been seen; after that the
        /// positive handle answers everything. Four covers an additive main-plus-level-plus-UI
        /// layout without recycling, and a linear scan of four ints is cheaper than a hash.
        /// </remarks>
        private const int KnownSceneHandleCapacity = 4;

        private static int _dontDestroyOnLoadSceneHandle;
        private static readonly int[] SceneHandlesKnownNotDontDestroyOnLoad = new int[
            KnownSceneHandleCapacity
        ];
        private static int _nextKnownSceneHandleSlot;

        /// <summary>
        /// Determines if a GameObject is in the DontDestroyOnLoad scene.
        /// </summary>
        /// <remarks>
        /// Allocation-free once the scene has been seen. Reading <c>Scene.name</c> marshals a fresh
        /// managed string out of native memory on every call, and this predicate reads cheap enough
        /// to end up in <c>Update</c>, so the answer is cached against the scene's handle -- a value
        /// type whose comparison costs nothing.
        /// <para>
        /// The DontDestroyOnLoad scene is unique and its handle is stable for the session, so once
        /// that handle is known it answers for every scene at once and nothing else is consulted.
        /// Until then -- in a game that has no persistent object, or before it is created -- a small
        /// set of handles already known NOT to be it does the same job, so additively loaded scenes
        /// do not take turns evicting each other.
        /// </para>
        /// </remarks>
        public static bool IsDontDestroyOnLoad(this GameObject gameObjectToCheck)
        {
            if (gameObjectToCheck == null)
            {
                return false;
            }

            Scene scene = gameObjectToCheck.scene;
            int handle = scene.handle;

            int dontDestroyOnLoadHandle = _dontDestroyOnLoadSceneHandle;
            if (dontDestroyOnLoadHandle != 0)
            {
                return handle == dontDestroyOnLoadHandle;
            }

            if (handle != 0)
            {
                foreach (int known in SceneHandlesKnownNotDontDestroyOnLoad)
                {
                    if (known == handle)
                    {
                        return false;
                    }
                }
            }

            bool isDontDestroyOnLoad = string.Equals(
                scene.name,
                DontDestroyOnLoadSceneName,
                StringComparison.Ordinal
            );

            if (handle == 0)
            {
                return isDontDestroyOnLoad;
            }

            if (isDontDestroyOnLoad)
            {
                _dontDestroyOnLoadSceneHandle = handle;
            }
            else
            {
                SceneHandlesKnownNotDontDestroyOnLoad[_nextKnownSceneHandleSlot] = handle;
                _nextKnownSceneHandleSlot =
                    (_nextKnownSceneHandleSlot + 1) % KnownSceneHandleCapacity;
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
            _nextKnownSceneHandleSlot = 0;
            Array.Clear(
                SceneHandlesKnownNotDontDestroyOnLoad,
                0,
                SceneHandlesKnownNotDontDestroyOnLoad.Length
            );
        }
    }
}
