// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;

    /// <summary>
    /// Saves sprite frames to an existing animation clip without an editor window.
    /// </summary>
    public static class AnimationClipFrameSaveAPI
    {
        /// <summary>
        /// Replaces one sprite curve and saves the clip asset.
        /// </summary>
        /// <remarks>
        /// The edit records Undo before changing the clip. Asset saving is a file side effect and
        /// cannot be fully reversed by Unity Undo alone.
        /// </remarks>
        public static bool TrySaveFrames(
            AnimationClip clip,
            IReadOnlyList<Sprite> frames,
            float framesPerSecond,
            string preferredBindingPath,
            out bool usedFallbackBinding,
            out string error
        )
        {
            usedFallbackBinding = false;
            if (
                clip == null
                || !EditorUtility.IsPersistent(clip)
                || !AssetDatabase.IsMainAsset(clip)
                || !AssetDatabase
                    .GetAssetPath(clip)
                    .EndsWith(".anim", StringComparison.OrdinalIgnoreCase)
            )
            {
                error = "An editable standalone .anim clip asset is required.";
                return false;
            }
            if (frames == null || frames.Count == 0)
            {
                error = "At least one sprite frame is required.";
                return false;
            }
            if (
                framesPerSecond <= 0f
                || float.IsNaN(framesPerSecond)
                || float.IsInfinity(framesPerSecond)
                || float.IsInfinity((frames.Count - 1) / framesPerSecond)
            )
            {
                error = "A finite, positive frame rate with finite key times is required.";
                return false;
            }

            try
            {
                EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(
                    clip
                );
                EditorCurveBinding selectedBinding = default;
                bool foundBinding = false;
                bool preferredBindingFound = false;
                foreach (EditorCurveBinding candidate in bindings)
                {
                    if (
                        candidate.type != typeof(SpriteRenderer)
                        || !string.Equals(
                            candidate.propertyName,
                            UnityExtensions.SpriteBindingProperty,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        continue;
                    }
                    if (!foundBinding)
                    {
                        selectedBinding = candidate;
                        foundBinding = true;
                    }
                    if (
                        preferredBindingPath != null
                        && string.Equals(
                            candidate.path,
                            preferredBindingPath,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        selectedBinding = candidate;
                        preferredBindingFound = true;
                        break;
                    }
                }
                if (!foundBinding)
                {
                    error = "The clip has no SpriteRenderer sprite curve.";
                    return false;
                }

                ObjectReferenceKeyframe[] replacement = new ObjectReferenceKeyframe[frames.Count];
                for (int index = 0; index < frames.Count; index++)
                {
                    Sprite frame = frames[index];
                    if (frame == null)
                    {
                        error = "Sprite frames cannot contain null entries.";
                        return false;
                    }
                    replacement[index] = new ObjectReferenceKeyframe
                    {
                        time = index / framesPerSecond,
                        value = frame,
                    };
                }

                ObjectReferenceKeyframe[] original = AnimationUtility.GetObjectReferenceCurve(
                    clip,
                    selectedBinding
                );
                float originalFrameRate = clip.frameRate;
                Undo.RecordObject(clip, "Modify Animation Clip Frames");
                try
                {
                    AnimationUtility.SetObjectReferenceCurve(clip, selectedBinding, replacement);
                    clip.frameRate = framesPerSecond;
                    EditorUtility.SetDirty(clip);
                    AssetDatabase.SaveAssets();
                }
                catch (Exception exception)
                {
                    try
                    {
                        AnimationUtility.SetObjectReferenceCurve(clip, selectedBinding, original);
                        clip.frameRate = originalFrameRate;
                        EditorUtility.SetDirty(clip);
                        AssetDatabase.SaveAssets();
                        error = exception.Message;
                    }
                    catch (Exception rollbackException)
                    {
                        error =
                            $"{exception.Message} Restoration also failed: {rollbackException.Message}";
                    }
                    return false;
                }

                usedFallbackBinding = preferredBindingPath != null && !preferredBindingFound;
                error = null;
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
