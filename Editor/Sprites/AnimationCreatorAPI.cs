// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Animation;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Creates sprite animation clips and project assets without an editor window.
    /// </summary>
    public static class AnimationCreatorAPI
    {
        private static readonly char[] InvalidNameCharacters =
        {
            '/',
            '\\',
            ':',
            '*',
            '?',
            '"',
            '<',
            '>',
            '|',
        };

        /// <summary>
        /// Builds a clip from the supplied frames in their given order.
        /// </summary>
        public static bool TryCreateClip(
            AnimationData data,
            IReadOnlyList<Sprite> frames,
            out AnimationClip clip,
            out string error
        )
        {
            clip = null;
            try
            {
                if (data == null || frames == null || frames.Count == 0)
                {
                    error = "Animation data and at least one sprite frame are required.";
                    return false;
                }

                for (int index = 0; index < frames.Count; index++)
                {
                    if (frames[index] == null)
                    {
                        error = "Sprite frames cannot contain null entries.";
                        return false;
                    }
                }

                float baseFrameRate =
                    0 < data.framesPerSecond
                    && !float.IsNaN(data.framesPerSecond)
                    && !float.IsInfinity(data.framesPerSecond)
                        ? data.framesPerSecond
                        : AnimationData.DefaultFramesPerSecond;
                clip = new AnimationClip { frameRate = baseFrameRate };
                ObjectReferenceKeyframe[] keyframes = new ObjectReferenceKeyframe[frames.Count];
                float currentTime = 0f;

                for (int index = 0; index < frames.Count; index++)
                {
                    keyframes[index].time = currentTime;
                    keyframes[index].value = frames[index];
                    if (index == frames.Count - 1)
                    {
                        continue;
                    }

                    float fps = baseFrameRate;
                    if (
                        data.framerateMode == FramerateMode.Curve
                        && data.framesPerSecondCurve != null
                    )
                    {
                        float normalizedPosition =
                            1 < frames.Count ? (float)index / (frames.Count - 1) : 0f;
                        fps = data.framesPerSecondCurve.Evaluate(normalizedPosition);
                        if (fps <= 0 || float.IsNaN(fps) || float.IsInfinity(fps))
                        {
                            fps = baseFrameRate;
                        }
                    }
                    currentTime += 1f / fps;
                }

                AnimationUtility.SetObjectReferenceCurve(
                    clip,
                    EditorCurveBinding.PPtrCurve(
                        "",
                        typeof(SpriteRenderer),
                        UnityExtensions.SpriteBindingProperty
                    ),
                    keyframes
                );
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = data.loop;
                settings.cycleOffset = Mathf.Clamp01(data.cycleOffset);
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                if (clip != null)
                {
                    UnityEngine.Object.DestroyImmediate(clip);
                    clip = null;
                }
                error = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Creates a uniquely named clip beside the first naturally sorted valid sprite.
        /// </summary>
        public static bool TryCreateAsset(
            AnimationData data,
            out string assetPath,
            out string error,
            bool saveAssets = true
        )
        {
            try
            {
                return TryCreateAssetCore(data, out assetPath, out error, saveAssets);
            }
            catch (Exception exception)
            {
                assetPath = null;
                error = exception.Message;
                return false;
            }
        }

        private static bool TryCreateAssetCore(
            AnimationData data,
            out string assetPath,
            out string error,
            bool saveAssets
        )
        {
            assetPath = null;
            if (data == null || string.IsNullOrWhiteSpace(data.animationName))
            {
                error = "An animation name is required.";
                return false;
            }
            if (
                0 <= data.animationName.IndexOfAny(InvalidNameCharacters)
                || string.Equals(data.animationName, ".", StringComparison.Ordinal)
                || string.Equals(data.animationName, "..", StringComparison.Ordinal)
            )
            {
                error = "The animation name must be a single valid file name.";
                return false;
            }
            if (data.frames == null || data.frames.Count == 0)
            {
                error = "At least one sprite frame is required.";
                return false;
            }

            using PooledResource<List<Sprite>> framesLease = Buffers<Sprite>.List.Get(
                out List<Sprite> validFrames
            );
            foreach (Sprite frame in data.frames)
            {
                if (frame != null)
                {
                    validFrames.Add(frame);
                }
            }
            if (validFrames.Count == 0)
            {
                error = "At least one non-null sprite frame is required.";
                return false;
            }
            validFrames.Sort((left, right) => EditorUtility.NaturalCompare(left.name, right.name));

            string firstFramePath = AssetDatabase.GetAssetPath(validFrames[0]);
            if (!firstFramePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                error = "The first sprite must be a project asset under Assets.";
                return false;
            }

            string directory = Path.GetDirectoryName(firstFramePath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(directory))
            {
                error = "The first sprite has no project asset directory.";
                return false;
            }

            if (!TryCreateClip(data, validFrames, out AnimationClip clip, out error))
            {
                return false;
            }

            string finalPath = null;
            try
            {
                finalPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{directory}/{data.animationName}.anim"
                );
                AssetDatabaseBatchHelper.EnsureAssetParentFolder(finalPath);
                AssetDatabase.CreateAsset(clip, finalPath);
                if (!EditorUtility.IsPersistent(clip))
                {
                    UnityEngine.Object.DestroyImmediate(clip);
                    error = $"Unity did not create an animation asset at '{finalPath}'.";
                    return false;
                }
                if (saveAssets)
                {
                    AssetDatabase.SaveAssets();
                }
                assetPath = finalPath;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                if (clip != null && !EditorUtility.IsPersistent(clip))
                {
                    UnityEngine.Object.DestroyImmediate(clip);
                }
                error = exception.Message;
                return false;
            }
        }
    }
#endif
}
