// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WGroup
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEditor.AnimatedValues;
    using UnityEditorInternal;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;

    /// <summary>
    /// Centralized animation state management for WGroup foldouts.
    /// Uses Unity's AnimBool for smooth expand/collapse transitions.
    /// </summary>
    internal static class WGroupAnimationState
    {
        private static readonly Dictionary<int, AnimBool> FoldoutAnimations = new();

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

            if (!FoldoutAnimations.TryGetValue(key, out AnimBool anim) || anim == null)
            {
                anim = new AnimBool(expanded) { speed = speed };
                anim.valueChanged.AddListener(RequestRepaint);
                FoldoutAnimations[key] = anim;
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
            foreach (KeyValuePair<int, AnimBool> kvp in FoldoutAnimations)
            {
                AnimBool anim = kvp.Value;
                if (anim != null)
                {
                    anim.valueChanged.RemoveListener(RequestRepaint);
                }
            }
            FoldoutAnimations.Clear();
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
