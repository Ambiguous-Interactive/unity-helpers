// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
    using System.Collections.Generic;
#if UNITY_EDITOR
    using System.Globalization;

    /// <summary>One animation keyframe naming an object the AssetDatabase cannot produce.</summary>
    public readonly struct AnimationKeyframeFinding
        : global::System.IEquatable<AnimationKeyframeFinding>
    {
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

        /// <summary>Initializes a new instance of the <see cref="AnimationKeyframeFinding"/> struct.</summary>
        /// <param name="clipPath">The clip's asset path.</param>
        /// <param name="clipName">The clip's name.</param>
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

        /// <summary>Renders the finding as a location a reader can open.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            string seconds = Time.ToString("0.###", CultureInfo.InvariantCulture);
            return $"{ClipPath} ({ClipName}): {BindingPath}/{PropertyName} at {seconds}s resolves "
                + "to nothing, so the subject vanishes for that frame's duration and comes back.";
        }

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(AnimationKeyframeFinding other)
        {
            return EqualityComparer<string>.Default.Equals(ClipPath, other.ClipPath)
                && EqualityComparer<string>.Default.Equals(ClipName, other.ClipName)
                && EqualityComparer<string>.Default.Equals(BindingPath, other.BindingPath)
                && EqualityComparer<string>.Default.Equals(PropertyName, other.PropertyName)
                && Time.Equals(other.Time);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is AnimationKeyframeFinding other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                ClipPath,
                ClipName,
                BindingPath,
                PropertyName,
                Time
            );
        }
    }
#endif
}
