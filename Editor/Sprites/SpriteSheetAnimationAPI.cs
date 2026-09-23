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
    using WallstopStudios.UnityHelpers.Editor.Utils;

    /// <summary>
    /// Creates sprite sheet animation assets from ordered frames without an editor window.
    /// </summary>
    public static class SpriteSheetAnimationAPI
    {
        internal static Action SaveAssetsForTesting;

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
        /// Previews or creates a uniquely named animation asset in a project folder.
        /// </summary>
        /// <remarks>
        /// A preview returns the prospective path without creating folders or assets. Creating an
        /// asset writes project files, which Unity Undo cannot fully reverse. When saving several
        /// clips, pass <c>saveAssets: false</c> and save once after the batch. If saving fails after
        /// creation, the created asset path remains in <c>assetPath</c>.
        /// </remarks>
        public static bool TryCreate(
            string outputFolder,
            string name,
            IReadOnlyList<Sprite> frames,
            float defaultFrameRate,
            AnimationCurve frameRateCurve,
            bool loop,
            float cycleOffset,
            bool dryRun,
            out string assetPath,
            out string error,
            bool saveAssets = true
        )
        {
            string plannedAssetPath = null;
            bool createdAsset = false;
            try
            {
                string normalizedFolder = outputFolder?.Replace('\\', '/').TrimEnd('/');
                bool underAssets =
                    string.Equals(normalizedFolder, "Assets", StringComparison.OrdinalIgnoreCase)
                    || (
                        normalizedFolder != null
                        && normalizedFolder.StartsWith(
                            "Assets/",
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                bool underPackages =
                    normalizedFolder != null
                    && normalizedFolder.StartsWith("Packages/", StringComparison.Ordinal)
                    && AssetDatabase.IsValidFolder(normalizedFolder);
                if (
                    string.IsNullOrWhiteSpace(normalizedFolder)
                    || (!underAssets && !underPackages)
                    || 0 <= normalizedFolder.IndexOf("..", StringComparison.Ordinal)
                )
                {
                    error =
                        "An output folder under Assets or an existing Packages folder is required.";
                    assetPath = null;
                    return false;
                }
                if (underAssets)
                {
                    normalizedFolder = "Assets" + normalizedFolder.Substring("Assets".Length);
                }
                if (
                    string.IsNullOrWhiteSpace(name)
                    || 0 <= name.IndexOfAny(InvalidNameCharacters)
                    || ContainsControlCharacter(name)
                    || string.Equals(name, ".", StringComparison.Ordinal)
                    || string.Equals(name, "..", StringComparison.Ordinal)
                )
                {
                    assetPath = null;
                    error = "A valid animation file name is required.";
                    return false;
                }
                if (frames == null || frames.Count == 0)
                {
                    assetPath = null;
                    error = "At least one sprite frame is required.";
                    return false;
                }
                if (
                    defaultFrameRate <= 0f
                    || float.IsNaN(defaultFrameRate)
                    || float.IsInfinity(defaultFrameRate)
                    || float.IsNaN(cycleOffset)
                    || float.IsInfinity(cycleOffset)
                )
                {
                    assetPath = null;
                    error = "A finite, positive frame rate and finite cycle offset are required.";
                    return false;
                }
                for (int index = 0; index < frames.Count; index++)
                {
                    if (frames[index] == null)
                    {
                        assetPath = null;
                        error = "Sprite frames cannot contain null entries.";
                        return false;
                    }
                }

                float[] frameTimes = new float[frames.Count];
                float currentTime = 0f;
                float curveDuration = 1f;
                if (frameRateCurve != null && 0 < frameRateCurve.length)
                {
                    float lastTime = frameRateCurve.keys[frameRateCurve.length - 1].time;
                    if (0f < lastTime)
                    {
                        curveDuration = lastTime;
                    }
                }
                for (int index = 0; index < frames.Count; index++)
                {
                    frameTimes[index] = currentTime;
                    if (index == frames.Count - 1)
                    {
                        continue;
                    }
                    float fps = defaultFrameRate;
                    if (frameRateCurve != null && 0 < frameRateCurve.length)
                    {
                        float position = (float)index / (frames.Count - 1) * curveDuration;
                        float evaluated = frameRateCurve.Evaluate(position);
                        if (
                            0f < evaluated
                            && !float.IsNaN(evaluated)
                            && !float.IsInfinity(evaluated)
                        )
                        {
                            fps = evaluated;
                        }
                    }
                    currentTime += 1f / fps;
                    if (float.IsNaN(currentTime) || float.IsInfinity(currentTime))
                    {
                        assetPath = null;
                        error = "Frame times must remain finite.";
                        return false;
                    }
                }

                string candidatePath = $"{normalizedFolder}/{name}.anim";
                plannedAssetPath = AssetDatabase.IsValidFolder(normalizedFolder)
                    ? AssetDatabase.GenerateUniqueAssetPath(candidatePath)
                    : candidatePath;
                if (dryRun)
                {
                    assetPath = plannedAssetPath;
                    error = null;
                    return true;
                }

                AnimationClip clip = new() { frameRate = 60f };
                try
                {
                    ObjectReferenceKeyframe[] keyframes = new ObjectReferenceKeyframe[frames.Count];
                    for (int index = 0; index < frames.Count; index++)
                    {
                        keyframes[index] = new ObjectReferenceKeyframe
                        {
                            time = frameTimes[index],
                            value = frames[index],
                        };
                    }

                    AnimationUtility.SetObjectReferenceCurve(
                        clip,
                        EditorCurveBinding.PPtrCurve(
                            string.Empty,
                            typeof(SpriteRenderer),
                            UnityExtensions.SpriteBindingProperty
                        ),
                        keyframes
                    );
                    AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(
                        clip
                    );
                    settings.loopTime = loop;
                    settings.cycleOffset = cycleOffset;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    AssetDatabaseBatchHelper.EnsureAssetParentFolder(candidatePath);
                    plannedAssetPath = AssetDatabase.GenerateUniqueAssetPath(candidatePath);
                    AssetDatabase.CreateAsset(clip, plannedAssetPath);
                    if (!EditorUtility.IsPersistent(clip))
                    {
                        assetPath = null;
                        error = $"Unity did not create an animation asset at '{plannedAssetPath}'.";
                        return false;
                    }
                    createdAsset = true;
                    if (saveAssets)
                    {
                        if (SaveAssetsForTesting != null)
                        {
                            SaveAssetsForTesting();
                        }
                        else
                        {
                            AssetDatabase.SaveAssets();
                        }
                    }
                    assetPath = plannedAssetPath;
                    error = null;
                    return true;
                }
                finally
                {
                    if (clip != null && !EditorUtility.IsPersistent(clip))
                    {
                        UnityEngine.Object.DestroyImmediate(clip);
                    }
                }
            }
            catch (Exception exception)
            {
                error = createdAsset
                    ? $"Animation asset was created at '{plannedAssetPath}', but saving failed: {exception.Message}"
                    : exception.Message;
                assetPath = createdAsset ? plannedAssetPath : null;
                return false;
            }
        }

        internal static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "UnnamedAnim";
            }
            foreach (char invalidCharacter in InvalidNameCharacters)
            {
                name = name.Replace(invalidCharacter, '_');
            }
            char[] characters = name.ToCharArray();
            for (int index = 0; index < characters.Length; index++)
            {
                if (char.IsControl(characters[index]))
                {
                    characters[index] = '_';
                }
            }
            return new string(characters);
        }

        private static bool ContainsControlCharacter(string value)
        {
            foreach (char character in value)
            {
                if (char.IsControl(character))
                {
                    return true;
                }
            }
            return false;
        }
    }
#endif
}
