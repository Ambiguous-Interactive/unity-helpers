// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    internal static class AnimationEventClipSelector
    {
        public static AnimationClip Draw(
            Animator animator,
            AnimationEventEditorViewModel viewModel,
            ref string searchString,
            System.Action clearSelection
        )
        {
            EditorGUI.BeginChangeCheck();
            searchString = EditorGUILayout.TextField("Animation Search", searchString);
            EditorGUI.EndChangeCheck();

            if (animator == null)
            {
                clearSelection?.Invoke();
                return null;
            }

            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (controller == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a RuntimeAnimatorController to the selected Animator to edit clips.",
                    MessageType.Info
                );
                clearSelection?.Invoke();
                return null;
            }

            AnimationClip[] clips =
                controller.animationClips ?? System.Array.Empty<AnimationClip>();
            IReadOnlyList<AnimationClip> filtered = viewModel.FilterClips(
                clips,
                searchString,
                viewModel.CurrentClip
            );

            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No animation clips match the current search.",
                    MessageType.Info
                );
                return null;
            }

            string[] names = new string[filtered.Count];
            int selectedIndex = -1;
            for (int i = 0; i < filtered.Count; i++)
            {
                AnimationClip clip = filtered[i];
                names[i] = clip != null ? clip.name : string.Empty;
                if (clip == viewModel.CurrentClip)
                {
                    selectedIndex = i;
                }
            }

            int popupIndex = Mathf.Max(0, selectedIndex);
            int resultIndex = EditorGUILayout.Popup("Animation", popupIndex, names);
            if (resultIndex < 0 || filtered.Count <= resultIndex)
            {
                clearSelection?.Invoke();
                return null;
            }

            return filtered[resultIndex];
        }
    }
#endif
}
