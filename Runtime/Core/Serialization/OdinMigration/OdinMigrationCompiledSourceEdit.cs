// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration
{
#if UNITY_EDITOR
    /// <summary>Describes a source replacement resolved during editor compilation.</summary>
    public sealed class OdinMigrationCompiledSourceEdit
    {
        /// <summary>Gets the original compiler source path.</summary>
        public string SourcePath { get; }

        /// <summary>Gets the first source character to replace.</summary>
        public int Start { get; }

        /// <summary>Gets the original character count.</summary>
        public int Length { get; }

        /// <summary>Gets the replacement source text.</summary>
        public string Replacement { get; }

        /// <summary>Captures an immutable compiler-resolved source replacement.</summary>
        public OdinMigrationCompiledSourceEdit(
            string sourcePath,
            int start,
            int length,
            string replacement
        )
        {
            SourcePath = sourcePath;
            Start = start;
            Length = length;
            Replacement = replacement;
        }
    }
#endif
}
