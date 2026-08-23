// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Core.Helper;

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
        /// DontDestroyOnLoad scene answers everything on its own. Four covers an additive
        /// main-plus-level-plus-UI layout without recycling, and a linear scan of four is cheaper
        /// than a hash.
        /// </remarks>
        private const int KnownSceneCapacity = 4;

        /// <remarks>
        /// Scenes rather than handles, deliberately. <c>Scene.handle</c> is an <c>int</c> up to
        /// Unity 6000.4 and a <c>SceneHandle</c> from 6000.5, where the conversion to <c>int</c> is
        /// obsolete-as-an-error -- so caching the handle compiles here and fails there. The
        /// <see cref="Scene"/> struct compares by that same handle on every version, and
        /// <see cref="Scene.IsValid"/> replaces the "is it zero" test.
        /// </remarks>
        private static Scene _dontDestroyOnLoadScene;
        private static readonly Scene[] ScenesKnownNotDontDestroyOnLoad = new Scene[
            KnownSceneCapacity
        ];
        private static int _nextKnownSceneSlot;

        /// <summary>
        /// Determines if a GameObject is in the DontDestroyOnLoad scene.
        /// </summary>
        /// <remarks>
        /// Allocation-free once the scene has been seen. Reading <c>Scene.name</c> marshals a fresh
        /// managed string out of native memory on every call, and this predicate reads cheap enough
        /// to end up in <c>Update</c>, so the answer is cached against the scene itself -- a value
        /// type whose comparison costs nothing.
        /// <para>
        /// The DontDestroyOnLoad scene is unique and stable for the session, so once it is known it
        /// answers for every scene at once and nothing else is consulted. Until then -- in a game
        /// that has no persistent object, or before it is created -- a small set of scenes already
        /// known NOT to be it does the same job, so additively loaded scenes do not take turns
        /// evicting each other.
        /// </para>
        /// </remarks>
        public static bool IsDontDestroyOnLoad(this GameObject gameObjectToCheck)
        {
            if (gameObjectToCheck == null)
            {
                return false;
            }

            Scene scene = gameObjectToCheck.scene;

            if (_dontDestroyOnLoadScene.IsValid())
            {
                return scene == _dontDestroyOnLoadScene;
            }

            if (scene.IsValid())
            {
                foreach (Scene known in ScenesKnownNotDontDestroyOnLoad)
                {
                    if (known == scene)
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

            if (!scene.IsValid())
            {
                return isDontDestroyOnLoad;
            }

            if (isDontDestroyOnLoad)
            {
                _dontDestroyOnLoadScene = scene;
            }
            else
            {
                ScenesKnownNotDontDestroyOnLoad[_nextKnownSceneSlot] = scene;
                WallMath.WrappedAdd(ref _nextKnownSceneSlot, 1, KnownSceneCapacity);
            }

            return isDontDestroyOnLoad;
        }

        /// <remarks>
        /// A scene is unique for the lifetime of the player, but statics survive entering play mode
        /// when the project disables domain reload, and the next session's scenes start numbering
        /// again. Clearing here -- the earliest point every play session reaches -- keeps a stale
        /// entry from answering for a scene that merely reuses its handle.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSceneResidencyCache()
        {
            _dontDestroyOnLoadScene = default;
            _nextKnownSceneSlot = 0;
            Array.Clear(ScenesKnownNotDontDestroyOnLoad, 0, ScenesKnownNotDontDestroyOnLoad.Length);
        }
    }
}
