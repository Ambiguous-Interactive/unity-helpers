// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Saves animation events and an optional frame rate from editor code.
    /// </summary>
    public static class AnimationEventSaveAPI
    {
        /// <summary>
        /// Tries to save events and an optional frame rate to a clip.
        /// </summary>
        public static bool TrySave(
            AnimationClip clip,
            AnimationEvent[] events,
            float? frameRate,
            out string error
        )
        {
            error = null;
            if (clip == null)
            {
                error = "Animation clip is missing.";
                return false;
            }

            if (events == null)
            {
                error = "Animation events are missing.";
                return false;
            }

            foreach (AnimationEvent animationEvent in events)
            {
                if (animationEvent == null)
                {
                    error = "Animation events cannot contain null entries.";
                    return false;
                }
            }

            if (
                frameRate.HasValue
                && (
                    float.IsNaN(frameRate.Value)
                    || float.IsInfinity(frameRate.Value)
                    || frameRate.Value <= 0f
                )
            )
            {
                error = "Frame rate must be finite and positive.";
                return false;
            }

            try
            {
                Undo.RecordObject(clip, "Save Animation Events");
                AnimationUtility.SetAnimationEvents(clip, events);
                if (frameRate.HasValue)
                {
                    clip.frameRate = frameRate.Value;
                }

                EditorUtility.SetDirty(clip);
                AssetDatabase.SaveAssetIfDirty(clip);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }
#endif
}
