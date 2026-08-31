// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    /// <summary>One authored dictionary whose keys and values no longer describe one mapping.</summary>
    public readonly struct SerializableDictionaryAssetFinding
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
    }
#endif
}
