// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>Reports animation keyframes whose object reference no longer resolves.</summary>
    /// <remarks>
    /// The instrument is <c>AnimationUtility</c> rather than a guid scan because a keyframe's guid
    /// can resolve while the object does not: a sheet re-imported as <c>Single</c> keeps a
    /// <c>.meta</c> describing slices the importer no longer produces. See
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/docs/features/editor-tools/authored-asset-validation.md">Authored Asset Validation</see>.
    /// </remarks>
    public static class AnimationClipKeyframeValidator
    {
        /// <summary>
        /// Reports every empty object keyframe in the clips under <paramref name="assetPathPrefixes"/>.
        /// </summary>
        /// <param name="assetPathPrefixes">Asset path prefixes to scope the scan to.</param>
        /// <param name="findings">Receives one entry per empty keyframe.</param>
        /// <param name="clipsInspected">Receives how many clips were opened.</param>
        /// <param name="keyframesInspected">Receives how many object keyframes were judged.</param>
        /// <returns><c>false</c> when the scan could not run at all.</returns>
        /// <remarks>Both counts are outputs so a caller can refuse a vacuous pass.</remarks>
        public static bool TryScan(
            IReadOnlyList<string> assetPathPrefixes,
            List<AnimationKeyframeFinding> findings,
            out int clipsInspected,
            out int keyframesInspected
        )
        {
            return TryScan(
                assetPathPrefixes,
                findings,
                new List<string>(),
                out clipsInspected,
                out keyframesInspected
            );
        }

        /// <summary>
        /// Reports every empty object keyframe in the clips under <paramref name="assetPathPrefixes"/>,
        /// and every clip the scan could not load.
        /// </summary>
        /// <param name="assetPathPrefixes">Asset path prefixes to scope the scan to.</param>
        /// <param name="findings">Receives one entry per empty keyframe.</param>
        /// <param name="unreadable">Receives the asset paths the scan could not load, sorted.</param>
        /// <param name="clipsInspected">Receives how many clips were opened.</param>
        /// <param name="keyframesInspected">Receives how many object keyframes were judged.</param>
        /// <returns><c>false</c> when the scan could not run at all.</returns>
        /// <remarks>
        /// A path the asset database names as carrying a clip and then hands back no clip for is
        /// reported rather than skipped, and never as a finding: it is a hole in the measurement,
        /// not a defect in the clip, and the counts are too coarse to show one file going missing.
        /// </remarks>
        public static bool TryScan(
            IReadOnlyList<string> assetPathPrefixes,
            List<AnimationKeyframeFinding> findings,
            List<string> unreadable,
            out int clipsInspected,
            out int keyframesInspected
        )
        {
            if (
                assetPathPrefixes == null
                || assetPathPrefixes.Count <= 0
                || findings == null
                || unreadable == null
            )
            {
                clipsInspected = 0;
                keyframesInspected = 0;
                return false;
            }

            findings.Clear();
            unreadable.Clear();
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip");
            if (guids == null)
            {
                clipsInspected = 0;
                keyframesInspected = 0;
                return false;
            }

            int clips = 0;
            int keyframes = 0;
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (
                    string.IsNullOrEmpty(assetPath)
                    || !AuthoredAssetYaml.IsUnderAnyPrefix(assetPath, assetPathPrefixes)
                )
                {
                    continue;
                }

                int clipsAtPath = InspectClipsIn(
                    assetPath,
                    AssetDatabase.LoadAllAssetsAtPath(assetPath),
                    findings,
                    out int keyframesAtPath
                );

                if (clipsAtPath <= 0)
                {
                    unreadable.Add(assetPath);
                    continue;
                }

                clips += clipsAtPath;
                keyframes += keyframesAtPath;
            }

            UnreadableAssetPaths.SortAndDeduplicate(unreadable);
            clipsInspected = clips;
            keyframesInspected = keyframes;
            return true;
        }

        /// <summary>Judges every clip among what one asset path handed back.</summary>
        /// <param name="assetPath">The path the assets were loaded from.</param>
        /// <param name="assets">What the asset database returned, which may be <c>null</c>.</param>
        /// <param name="findings">Receives one entry per empty keyframe.</param>
        /// <param name="keyframesInspected">Receives how many object keyframes were judged.</param>
        /// <returns>How many clips were read; zero means the path handed back none.</returns>
        internal static int InspectClipsIn(
            string assetPath,
            Object[] assets,
            List<AnimationKeyframeFinding> findings,
            out int keyframesInspected
        )
        {
            int clips = 0;
            int keyframes = 0;
            if (assets != null)
            {
                foreach (Object asset in assets)
                {
                    if (!(asset is AnimationClip clip) || clip == null)
                    {
                        continue;
                    }

                    ++clips;
                    keyframes += Inspect(assetPath, clip, findings);
                }
            }

            keyframesInspected = keyframes;
            return clips;
        }

        /// <summary>Reports every empty object keyframe in one clip.</summary>
        /// <param name="assetPath">The clip's asset path.</param>
        /// <param name="clip">The clip to read.</param>
        /// <param name="findings">Receives one entry per empty keyframe.</param>
        /// <returns>How many object keyframes were judged.</returns>
        internal static int Inspect(
            string assetPath,
            AnimationClip clip,
            List<AnimationKeyframeFinding> findings
        )
        {
            int inspected = 0;
            if (clip == null || findings == null)
            {
                return inspected;
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (bindings == null)
            {
                return inspected;
            }

            foreach (EditorCurveBinding binding in bindings)
            {
                ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                    clip,
                    binding
                );

                if (keyframes == null)
                {
                    continue;
                }

                foreach (ObjectReferenceKeyframe keyframe in keyframes)
                {
                    ++inspected;
                    if (keyframe.value != null)
                    {
                        continue;
                    }

                    findings.Add(
                        new AnimationKeyframeFinding(
                            assetPath,
                            clip.name,
                            binding.path,
                            binding.propertyName,
                            keyframe.time
                        )
                    );
                }
            }

            return inspected;
        }
    }
#endif
}
