// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System.Collections.Generic;

    /// <summary>Preserves an Odin Nodes payload and its owning Unity document.</summary>
    public sealed class OdinMigrationSerializedData
    {
        /// <summary>Gets the owning document's local file identifier.</summary>
        public long FileId { get; }

        /// <summary>Gets the owning document's script GUID.</summary>
        public string ScriptGuid { get; }

        /// <summary>Gets the complete original payload text, including prefab metadata.</summary>
        public string Source { get; }

        /// <summary>Gets the one-based first line of the payload.</summary>
        public int Line { get; }

        /// <summary>Gets the one-based line immediately after the payload.</summary>
        public int EndLine { get; }

        /// <summary>Gets the immutable records in their original order.</summary>
        public IReadOnlyList<OdinMigrationSerializedNode> SerializationNodes { get; }

        /// <summary>Gets the original inline Unity references in index order.</summary>
        public IReadOnlyList<string> ReferencedUnityObjects { get; }

        internal OdinMigrationSerializedData(
            long fileId,
            string scriptGuid,
            string source,
            int line,
            int endLine,
            List<OdinMigrationSerializedNode> nodes,
            List<string> references
        )
        {
            FileId = fileId;
            ScriptGuid = scriptGuid;
            Source = source;
            Line = line;
            EndLine = endLine;
            SerializationNodes = nodes.AsReadOnly();
            ReferencedUnityObjects = references.AsReadOnly();
        }
    }
}
