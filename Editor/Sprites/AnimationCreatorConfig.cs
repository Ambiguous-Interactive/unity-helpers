// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Animation;

    /// <summary>
    /// JSON-serializable configuration for AnimationCreator settings.
    /// Stored alongside sprite source folders as {folderPath}/.animation-creator.json.
    /// </summary>
    [Serializable]
    public sealed class AnimationCreatorConfig
    {
        /// <summary>
        /// Configuration file name (without leading path).
        /// </summary>
        public const string FileName = ".animation-creator.json";

        /// <summary>
        /// Current configuration version for migration support.
        /// </summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// Configuration version number for future migration support.
        /// </summary>
        public int version = CurrentVersion;

        /// <summary>
        /// The regex pattern used to filter sprite names.
        /// </summary>
        public string spriteNameRegex = ".*";

        /// <summary>
        /// Whether auto-refresh is enabled for the filter.
        /// </summary>
        public bool autoRefresh = true;

        /// <summary>
        /// Whether grouping should be case-insensitive.
        /// </summary>
        public bool groupingCaseInsensitive = true;

        /// <summary>
        /// Whether to include the folder name in animation names.
        /// </summary>
        public bool includeFolderNameInAnimName;

        /// <summary>
        /// Whether to include the full folder path in animation names.
        /// </summary>
        public bool includeFullFolderPathInAnimName;

        /// <summary>
        /// Prefix to apply to auto-parsed animation names.
        /// </summary>
        public string autoParseNamePrefix = string.Empty;

        /// <summary>
        /// Suffix to apply to auto-parsed animation names.
        /// </summary>
        public string autoParseNameSuffix = string.Empty;

        /// <summary>
        /// Whether custom group regex is enabled.
        /// </summary>
        public bool useCustomGroupRegex;

        /// <summary>
        /// The custom group regex pattern.
        /// </summary>
        public string customGroupRegex = string.Empty;

        /// <summary>
        /// Whether the custom group regex should ignore case.
        /// </summary>
        public bool customGroupRegexIgnoreCase = true;

        /// <summary>
        /// Whether to resolve duplicate animation names.
        /// </summary>
        public bool resolveDuplicateAnimationNames = true;

        /// <summary>
        /// Whether to use strict numeric ordering.
        /// </summary>
        public bool strictNumericOrdering;

        /// <summary>
        /// The animation data entries.
        /// </summary>
        public List<AnimationDataEntry> animationEntries = new();

        /// <summary>
        /// Gets the config file path for a given folder path.
        /// </summary>
        /// <param name="folderPath">The path to the source folder.</param>
        /// <returns>The config file path within the folder.</returns>
        public static string GetConfigPath(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return string.Empty;
            }

            string normalized = folderPath.TrimEnd('/', '\\');
            return normalized + "/" + FileName;
        }

        /// <summary>
        /// Migrates a configuration from an older version to the current version.
        /// Currently a no-op for version 1, but provides the hook for future migrations.
        /// </summary>
        /// <param name="config">The configuration to migrate.</param>
        public static void MigrateConfig(AnimationCreatorConfig config)
        {
            if (config == null || CurrentVersion <= config.version)
            {
                return;
            }

            // Future migrations go here
            config.version = CurrentVersion;
        }

        /// <summary>
        /// Converts an AnimationCurve to a list of serializable keyframes.
        /// </summary>
        /// <param name="curve">The curve to serialize.</param>
        /// <returns>A list of CurveKeyframes.</returns>
        public static List<CurveKeyframe> SerializeCurve(AnimationCurve curve)
        {
            List<CurveKeyframe> keyframes = new();
            if (curve == null)
            {
                return keyframes;
            }

            for (int i = 0; i < curve.length; i++)
            {
                keyframes.Add(CurveKeyframe.FromKeyframe(curve[i]));
            }

            return keyframes;
        }

        /// <summary>
        /// Converts a list of serializable keyframes back to an AnimationCurve.
        /// </summary>
        /// <param name="keyframes">The keyframes to deserialize.</param>
        /// <param name="preWrapMode">The pre-wrap mode for the curve.</param>
        /// <param name="postWrapMode">The post-wrap mode for the curve.</param>
        /// <returns>An AnimationCurve.</returns>
        public static AnimationCurve DeserializeCurve(
            List<CurveKeyframe> keyframes,
            WrapMode preWrapMode,
            WrapMode postWrapMode
        )
        {
            if (keyframes == null || keyframes.Count == 0)
            {
                return AnimationCurve.Constant(0f, 1f, AnimationData.DefaultFramesPerSecond);
            }

            Keyframe[] unityKeyframes = new Keyframe[keyframes.Count];
            for (int i = 0; i < keyframes.Count; i++)
            {
                unityKeyframes[i] = keyframes[i].ToKeyframe();
            }

            AnimationCurve curve = new(unityKeyframes)
            {
                preWrapMode = preWrapMode,
                postWrapMode = postWrapMode,
            };
            return curve;
        }

        /// <summary>
        /// Serializable version of AnimationData for JSON persistence.
        /// Does not include transient/UI-only fields like showPreview.
        /// </summary>
        [Serializable]
        public sealed class AnimationDataEntry
        {
            /// <summary>
            /// Name of the animation clip.
            /// </summary>
            public string animationName = string.Empty;

            /// <summary>
            /// Asset paths of the sprite frames (relative to Assets/).
            /// </summary>
            public List<string> framePaths = new();

            /// <summary>
            /// Constant frames per second.
            /// </summary>
            public float framesPerSecond = AnimationData.DefaultFramesPerSecond;

            /// <summary>
            /// Whether this animation was created from auto-parse.
            /// </summary>
            public bool isCreatedFromAutoParse;

            /// <summary>
            /// Whether the animation should loop.
            /// </summary>
            public bool loop;

            /// <summary>
            /// The framerate mode (Constant or Curve).
            /// </summary>
            public FramerateMode framerateMode = FramerateMode.Constant;

            /// <summary>
            /// Keyframes for the FPS curve (serialized as time/value pairs).
            /// </summary>
            public List<CurveKeyframe> curveKeyframes = new();

            /// <summary>
            /// Wrap mode for the curve at the start.
            /// </summary>
            public WrapMode curvePreWrapMode = WrapMode.Clamp;

            /// <summary>
            /// Wrap mode for the curve at the end.
            /// </summary>
            public WrapMode curvePostWrapMode = WrapMode.Clamp;

            /// <summary>
            /// Starting point in the animation loop (0-1).
            /// </summary>
            public float cycleOffset;
        }

        /// <summary>
        /// Serializable version of AnimationCurve keyframes.
        /// </summary>
        [Serializable]
        public sealed class CurveKeyframe
        {
            /// <summary>
            /// The time of the keyframe.
            /// </summary>
            public float time;

            /// <summary>
            /// The value of the keyframe.
            /// </summary>
            public float value;

            /// <summary>
            /// The incoming tangent.
            /// </summary>
            public float inTangent;

            /// <summary>
            /// The outgoing tangent.
            /// </summary>
            public float outTangent;

            /// <summary>
            /// The incoming weight.
            /// </summary>
            public float inWeight;

            /// <summary>
            /// The outgoing weight.
            /// </summary>
            public float outWeight;

            /// <summary>
            /// The weighted mode of the keyframe.
            /// </summary>
            public WeightedMode weightedMode;

            /// <summary>
            /// Creates a CurveKeyframe from a Unity Keyframe.
            /// </summary>
            /// <param name="keyframe">The Unity Keyframe to convert.</param>
            /// <returns>A serializable CurveKeyframe.</returns>
            public static CurveKeyframe FromKeyframe(Keyframe keyframe)
            {
                return new CurveKeyframe
                {
                    time = keyframe.time,
                    value = keyframe.value,
                    inTangent = keyframe.inTangent,
                    outTangent = keyframe.outTangent,
                    inWeight = keyframe.inWeight,
                    outWeight = keyframe.outWeight,
                    weightedMode = keyframe.weightedMode,
                };
            }

            /// <summary>
            /// Converts this CurveKeyframe back to a Unity Keyframe.
            /// </summary>
            /// <returns>A Unity Keyframe.</returns>
            public Keyframe ToKeyframe()
            {
                return new Keyframe(time, value, inTangent, outTangent, inWeight, outWeight)
                {
                    weightedMode = weightedMode,
                };
            }
        }
    }
#endif
}
