// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System;
    using UnityEngine;

    internal static class AnimationEventKeyboardShortcuts
    {
        public static void Handle(
            Event currentEvent,
            AnimationEventEditorViewModel viewModel,
            ref int focusedIndex,
            Action<string> recordUndo,
            Action repaint
        )
        {
            if (currentEvent == null || currentEvent.type != EventType.KeyDown)
            {
                return;
            }

            if (viewModel.Count == 0)
            {
                return;
            }

            if (focusedIndex < 0 || viewModel.Count <= focusedIndex)
            {
                focusedIndex = 0;
            }

            switch (currentEvent.keyCode)
            {
                case KeyCode.Delete:
                    DeleteFocused(viewModel, ref focusedIndex, recordUndo);
                    currentEvent.Use();
                    repaint?.Invoke();
                    break;

                case KeyCode.D:
                    if (currentEvent.control)
                    {
                        DuplicateFocused(viewModel, ref focusedIndex, recordUndo);
                        currentEvent.Use();
                        repaint?.Invoke();
                    }
                    break;

                case KeyCode.UpArrow:
                    focusedIndex = Mathf.Max(0, focusedIndex - 1);
                    currentEvent.Use();
                    repaint?.Invoke();
                    break;

                case KeyCode.DownArrow:
                    focusedIndex = Mathf.Min(viewModel.Count - 1, focusedIndex + 1);
                    currentEvent.Use();
                    repaint?.Invoke();
                    break;
            }
        }

        private static void DeleteFocused(
            AnimationEventEditorViewModel viewModel,
            ref int focusedIndex,
            Action<string> recordUndo
        )
        {
            recordUndo?.Invoke("Delete Animation Event");
            viewModel.RemoveEventAt(focusedIndex);
            focusedIndex = Mathf.Clamp(focusedIndex, 0, viewModel.Count - 1);
        }

        private static void DuplicateFocused(
            AnimationEventEditorViewModel viewModel,
            ref int focusedIndex,
            Action<string> recordUndo
        )
        {
            recordUndo?.Invoke("Duplicate Animation Event");
            if (focusedIndex < 0 || viewModel.Count <= focusedIndex)
            {
                return;
            }

            viewModel.DuplicateEvent(focusedIndex);
            focusedIndex = Mathf.Min(viewModel.Count - 1, focusedIndex + 1);
        }
    }
#endif
}
