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
            try
            {
                if (clip == null)
                {
                    usedFallbackBinding = false;
                    error = "An editable standalone .anim clip asset is required.";
                    return false;
                }
                if (frames == null)
                {
                    usedFallbackBinding = false;
                    error = "At least one sprite frame is required.";
                    return false;
                }
                int frameCount = frames.Count;
                if (frameCount <= 0)
                {
                    usedFallbackBinding = false;
                    error = "At least one sprite frame is required.";
                    return false;
                }
                if (
                    framesPerSecond <= 0f
                    || float.IsNaN(framesPerSecond)
                    || float.IsInfinity(framesPerSecond)
                    || float.IsInfinity((frameCount - 1) / framesPerSecond)
                )
                {
                    usedFallbackBinding = false;
                    error = "A finite, positive frame rate with finite key times is required.";
                    return false;
                }
                for (int index = 0; index < frameCount; index++)
                {
                    if (frames[index] == null)
                    {
                        usedFallbackBinding = false;
                        error = "Sprite frames cannot contain null entries.";
                        return false;
                    }
                }
                if (
                    !EditorUtility.IsPersistent(clip)
                    || !AssetDatabase.IsMainAsset(clip)
                    || !AssetDatabase
                        .GetAssetPath(clip)
                        .EndsWith(".anim", StringComparison.OrdinalIgnoreCase)
                )
                {
                    usedFallbackBinding = false;
                    error = "An editable standalone .anim clip asset is required.";
                    return false;
                }

                EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(
                    clip
                );
                EditorCurveBinding? selectedBinding = null;
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
                    if (!selectedBinding.HasValue)
                    {
                        selectedBinding = candidate;
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
                        break;
                    }
                }
                if (!selectedBinding.HasValue)
                {
                    usedFallbackBinding = false;
                    error = "The clip has no SpriteRenderer sprite curve.";
                    return false;
                }

                ObjectReferenceKeyframe[] replacement = new ObjectReferenceKeyframe[frameCount];
                for (int index = 0; index < frameCount; index++)
                {
                    Sprite frame = frames[index];
                    if (frame == null)
                    {
                        usedFallbackBinding = false;
                        error = "Sprite frames cannot contain null entries.";
                        return false;
                    }
                    replacement[index] = new ObjectReferenceKeyframe
                    {
                        time = index / framesPerSecond,
                        value = frame,
                    };
                }

                EditorCurveBinding binding = selectedBinding.Value;
                ObjectReferenceKeyframe[] original = AnimationUtility.GetObjectReferenceCurve(
                    clip,
                    binding
                );
                float originalFrameRate = clip.frameRate;
                Undo.RecordObject(clip, "Modify Animation Clip Frames");
                try
                {
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, replacement);
                    clip.frameRate = framesPerSecond;
                    EditorUtility.SetDirty(clip);
                    AssetDatabase.SaveAssets();
                }
                catch (Exception exception)
                {
                    string failure = exception.Message;
                    try
                    {
                        AnimationUtility.SetObjectReferenceCurve(clip, binding, original);
                        clip.frameRate = originalFrameRate;
                        EditorUtility.SetDirty(clip);
                        AssetDatabase.SaveAssets();
                    }
                    catch (Exception rollbackException)
                    {
                        failure =
                            $"{exception.Message} Restoration also failed: {rollbackException.Message}";
                    }
                    usedFallbackBinding = false;
                    error = failure;
                    return false;
                }

                usedFallbackBinding =
                    preferredBindingPath != null
                    && !string.Equals(binding.path, preferredBindingPath, StringComparison.Ordinal);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                usedFallbackBinding = false;
                error = exception.Message;
                return false;
            }
        }
    }
#endif
}
