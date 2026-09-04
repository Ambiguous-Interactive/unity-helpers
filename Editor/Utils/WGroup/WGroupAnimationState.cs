// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WGroup
{
#if UNITY_EDITOR
    using UnityEditor.AnimatedValues;
    using UnityEditorInternal;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Centralized animation state management for WGroup foldouts.
    /// Uses Unity's AnimBool for smooth expand/collapse transitions.
    /// </summary>
    internal static class WGroupAnimationState
    {
        /// <remarks>
        /// The key mixes a target instance id, so every inspected object a session touches adds an
        /// entry that nothing removes. Each value holds a <see cref="RequestRepaint"/> listener the
        /// clear path deliberately unsubscribes, so eviction has to do the same -- otherwise a
        /// dropped animation keeps repainting every view forever. Re-creating an evicted animation
        /// is equivalent to keeping it apart from one frame of tween, and the least recently used
        /// entry is by definition not the foldout being drawn.
        /// </remarks>
        private const int MaxFoldoutAnimations = 256;

        private static readonly BoundedLruCache<int, AnimBool> FoldoutAnimations = new(
            static () => MaxFoldoutAnimations,
            onEvicted: static (_, anim) => Unsubscribe(anim)
        );

        /// <summary>
        /// Gets or creates an AnimBool for the given WGroup definition.
        /// The AnimBool is keyed by (Name, AnchorPropertyPath, targetInstanceId) hash.
        /// </summary>
        /// <param name="definition">The WGroup definition to get animation state for.</param>
        /// <param name="expanded">The current expanded state of the foldout.</param>
        /// <param name="targetInstanceId">The instance ID of the target object (0 if unknown).</param>
        /// <returns>The AnimBool instance for this definition, with target set to expanded.</returns>
        internal static AnimBool GetOrCreateAnim(
            WGroupDefinition definition,
            bool expanded,
            long targetInstanceId = 0
        )
        {
            int key = ComputeKey(definition, targetInstanceId);
            float speed = UnityHelpersSettings.GetWGroupFoldoutSpeed();

            if (!FoldoutAnimations.TryGet(key, out AnimBool anim) || anim == null)
            {
                anim = new AnimBool(expanded) { speed = speed };
                anim.valueChanged.AddListener(RequestRepaint);
                FoldoutAnimations.Set(key, anim);
            }

            anim.speed = speed;
            anim.target = expanded;
            return anim;
        }

        /// <summary>
        /// Gets the current fade progress for a WGroup foldout.
        /// </summary>
        /// <param name="definition">The WGroup definition.</param>
        /// <param name="expanded">The current expanded state.</param>
        /// <param name="targetInstanceId">The instance ID of the target object (0 if unknown).</param>
        /// <returns>
        /// A value between 0 and 1 representing the animation progress.
        /// Returns 0 or 1 immediately if tweening is disabled.
        /// </returns>
        internal static float GetFadeProgress(
            WGroupDefinition definition,
            bool expanded,
            long targetInstanceId = 0
        )
        {
            if (!UnityHelpersSettings.ShouldTweenWGroupFoldouts())
            {
                return expanded ? 1f : 0f;
            }

            AnimBool anim = GetOrCreateAnim(definition, expanded, targetInstanceId);
            return anim.faded;
        }

        /// <summary>
        /// Clears all cached animation states.
        /// Useful for testing and when settings change.
        /// </summary>
        internal static void ClearCache()
        {
            FoldoutAnimations.Clear();
        }

        /// <summary>
        /// The number of foldout animations currently retained, for testing.
        /// </summary>
        internal static int CachedAnimationCount => FoldoutAnimations.Count;

        /// <summary>
        /// The bound this cache evicts at, for testing.
        /// </summary>
        internal static int MaxCachedAnimations => MaxFoldoutAnimations;

        private static void Unsubscribe(AnimBool anim)
        {
            if (anim != null)
            {
                anim.valueChanged.RemoveListener(RequestRepaint);
            }
        }

        private static int ComputeKey(WGroupDefinition definition, long targetInstanceId)
        {
            return Objects.HashCode(
                definition.Name,
                definition.AnchorPropertyPath,
                targetInstanceId
            );
        }

        private static void RequestRepaint()
        {
            InternalEditorUtility.RepaintAllViews();
        }
    }
#endif
}
