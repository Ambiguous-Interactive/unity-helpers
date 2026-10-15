// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
    using System.Collections.Generic;
#if UNITY_EDITOR
    /// <summary>One authored dictionary whose keys and values no longer describe one mapping.</summary>
    public readonly struct SerializableDictionaryAssetFinding
        : global::System.IEquatable<SerializableDictionaryAssetFinding>
    {
        /// <summary>The asset carrying the dictionary.</summary>
        public string AssetPath { get; }

        /// <summary>The one-based line the evidence is on.</summary>
        public int LineNumber { get; }

        /// <summary>Which state the dictionary is in.</summary>
        public SerializableDictionaryAssetProblem Problem { get; }

        /// <summary>What the scan measured.</summary>
        public string Detail { get; }

        /// <summary>Initializes a new instance of the <see cref="SerializableDictionaryAssetFinding"/> struct.</summary>
        /// <param name="assetPath">The asset carrying the dictionary.</param>
        /// <param name="lineNumber">The one-based line the evidence is on.</param>
        /// <param name="problem">Which state the dictionary is in.</param>
        /// <param name="detail">What the scan measured.</param>
        public SerializableDictionaryAssetFinding(
            string assetPath,
            int lineNumber,
            SerializableDictionaryAssetProblem problem,
            string detail
        )
        {
            AssetPath = assetPath;
            LineNumber = lineNumber;
            Problem = problem;
            Detail = detail;
        }

        /// <summary>Renders the finding as a location a reader can open.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            return $"{AssetPath}:{LineNumber}: {Problem} -- {Detail}";
        }

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(SerializableDictionaryAssetFinding other)
        {
            return EqualityComparer<string>.Default.Equals(AssetPath, other.AssetPath)
                && LineNumber.Equals(other.LineNumber)
                && EqualityComparer<SerializableDictionaryAssetProblem>.Default.Equals(
                    Problem,
                    other.Problem
                )
                && EqualityComparer<string>.Default.Equals(Detail, other.Detail);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is SerializableDictionaryAssetFinding other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                AssetPath,
                LineNumber,
                Problem,
                Detail
            );
        }
    }
#endif
}
