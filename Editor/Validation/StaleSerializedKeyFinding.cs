// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;

    /// <summary>One serialized key an asset records that no field of its type claims.</summary>
    public readonly struct StaleSerializedKeyFinding
    {
        /// <summary>The asset carrying the key.</summary>
        public string AssetPath { get; }

        /// <summary>The one-based line the key is written on.</summary>
        public int LineNumber { get; }

        /// <summary>The type the document's script resolves to.</summary>
        public Type OwnerType { get; }

        /// <summary>The key nothing claims.</summary>
        public string Key { get; }

        /// <summary>The cause, one entry per type and key.</summary>
        public string Cause => $"{OwnerType?.FullName}::{Key}";

        /// <summary>Initializes a new instance of the <see cref="StaleSerializedKeyFinding"/> struct.</summary>
        /// <param name="assetPath">The asset carrying the key.</param>
        /// <param name="lineNumber">The one-based line the key is written on.</param>
        /// <param name="ownerType">The type the document's script resolves to.</param>
        /// <param name="key">The key nothing claims.</param>
        public StaleSerializedKeyFinding(
            string assetPath,
            int lineNumber,
            Type ownerType,
            string key
        )
        {
            AssetPath = assetPath;
            LineNumber = lineNumber;
            OwnerType = ownerType;
            Key = key;
        }

        /// <summary>Renders the finding as a location a reader can open.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            return $"{AssetPath}:{LineNumber}: {Cause}";
        }
    }
#endif
}
