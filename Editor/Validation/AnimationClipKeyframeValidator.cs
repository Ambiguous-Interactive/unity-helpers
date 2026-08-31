// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// One animation keyframe that names an object the AssetDatabase cannot produce.
    /// </summary>
    public readonly struct AnimationKeyframeFinding
    {
        /// <summary>Initializes a new instance of the <see cref="AnimationKeyframeFinding"/> struct.</summary>
        /// <param name="clipPath">The clip's asset path.</param>
        /// <param name="clipName">The clip's name, which a sub-asset needs to be identified.</param>
        /// <param name="bindingPath">The transform path the curve animates.</param>
        /// <param name="propertyName">The property the curve drives.</param>
        /// <param name="time">The time of the empty keyframe, in seconds.</param>
        public AnimationKeyframeFinding(
            string clipPath,
            string clipName,
            string bindingPath,
            string propertyName,
            float time
        )
        {
            ClipPath = clipPath;
            ClipName = clipName;
            BindingPath = bindingPath;
            PropertyName = propertyName;
            Time = time;
        }

        /// <summary>The clip's asset path.</summary>
        public string ClipPath { get; }

        /// <summary>The clip's name, which a sub-asset needs to be identified.</summary>
        public string ClipName { get; }

        /// <summary>The transform path the curve animates.</summary>
        public string BindingPath { get; }

        /// <summary>The property the curve drives.</summary>
        public string PropertyName { get; }

        /// <summary>The time of the empty keyframe, in seconds.</summary>
        public float Time { get; }

        /// <summary>Renders the finding as a location a reader can open.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            string seconds = Time.ToString("0.###", CultureInfo.InvariantCulture);
            return $"{ClipPath} ({ClipName}): {BindingPath}/{PropertyName} at {seconds}s resolves "
                + "to nothing, so the subject vanishes for that frame's duration and comes back.";
        }
    }

    /// <summary>
    /// Reports animation keyframes whose object reference no longer resolves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <c>AnimationClip</c> stores each animated object reference as a keyframe. Delete the
    /// object and the keyframe stays, pointing at nothing. Unity reports that nowhere: the clip
    /// imports, the animator plays it, and the renderer is handed <c>null</c> for that frame's
    /// duration -- so the symptom is "the thing flickers", which nobody files as a bug against
    /// animation.
    /// </para>
    /// <para>
    /// The instrument is <c>AnimationUtility</c> rather than a guid scan, and the difference is not
    /// a refinement. A keyframe's guid can resolve perfectly while the object does not: a sprite
    /// sheet re-imported as <c>Single</c> still has a <c>.meta</c> describing every slice, and the
    /// importer produces none of them. Only the AssetDatabase can tell "the guid resolves" from
    /// "the object resolves".
    /// </para>
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
        /// <remarks>
        /// Both counts are outputs so a caller can refuse a vacuous pass: a scan whose path scope
        /// stops matching reports zero findings, and a clip whose every frame is empty would pass
        /// beside it.
        /// </remarks>
        public static bool TryScan(
            IReadOnlyList<string> assetPathPrefixes,
            List<AnimationKeyframeFinding> findings,
            out int clipsInspected,
            out int keyframesInspected
        )
        {
            if (assetPathPrefixes == null || assetPathPrefixes.Count <= 0 || findings == null)
            {
                clipsInspected = 0;
                keyframesInspected = 0;
                return false;
            }

            findings.Clear();
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip");
            if (guids == null)
            {
                clipsInspected = 0;
                keyframesInspected = 0;
                return false;
            }

            int clips = 0;
            int keyframes = 0;
            for (int index = 0; index < guids.Length; ++index)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[index]);
                if (string.IsNullOrEmpty(assetPath) || !IsInScope(assetPath, assetPathPrefixes))
                {
                    continue;
                }

                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                if (assets == null)
                {
                    continue;
                }

                for (int asset = 0; asset < assets.Length; ++asset)
                {
                    if (!(assets[asset] is AnimationClip clip) || clip == null)
                    {
                        continue;
                    }

                    ++clips;
                    keyframes += Inspect(assetPath, clip, findings);
                }
            }

            clipsInspected = clips;
            keyframesInspected = keyframes;
            return true;
        }

        private static int Inspect(
            string assetPath,
            AnimationClip clip,
            List<AnimationKeyframeFinding> findings
        )
        {
            int inspected = 0;
            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (bindings == null)
            {
                return inspected;
            }

            for (int index = 0; index < bindings.Length; ++index)
            {
                EditorCurveBinding binding = bindings[index];
                ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve(
                    clip,
                    binding
                );

                if (keyframes == null)
                {
                    continue;
                }

                for (int keyframe = 0; keyframe < keyframes.Length; ++keyframe)
                {
                    ++inspected;
                    if (keyframes[keyframe].value != null)
                    {
                        continue;
                    }

                    findings.Add(
                        new AnimationKeyframeFinding(
                            assetPath,
                            clip.name,
                            binding.path,
                            binding.propertyName,
                            keyframes[keyframe].time
                        )
                    );
                }
            }

            return inspected;
        }

        private static bool IsInScope(string assetPath, IReadOnlyList<string> assetPathPrefixes)
        {
            string normalized = assetPath.Replace('\\', '/');
            for (int index = 0; index < assetPathPrefixes.Count; ++index)
            {
                string prefix = assetPathPrefixes[index];
                if (
                    !string.IsNullOrEmpty(prefix)
                    && normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }

            return false;
        }
    }
#endif
}
