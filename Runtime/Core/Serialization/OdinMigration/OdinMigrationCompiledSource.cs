// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration
{
#if UNITY_EDITOR
    /// <summary>Identifies the source text captured by an editor compilation.</summary>
    public sealed class OdinMigrationCompiledSource
    {
        /// <summary>Gets the compiler's source path.</summary>
        public string Path { get; }

        /// <summary>Gets the SHA-256 checksum of the UTF-8 source text.</summary>
        public string Checksum { get; }

        /// <summary>Captures a source path and checksum emitted by the compiler.</summary>
        public OdinMigrationCompiledSource(string path, string checksum)
        {
            Path = path;
            Checksum = checksum;
        }
    }
#endif
}
