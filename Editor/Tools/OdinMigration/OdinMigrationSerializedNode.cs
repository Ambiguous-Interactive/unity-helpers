// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    /// <summary>Preserves one Odin Nodes record without constructing its recorded type.</summary>
    public readonly struct OdinMigrationSerializedNode
    {
        /// <summary>Gets the recorded member name.</summary>
        public string Name { get; }

        /// <summary>Gets the numeric Odin entry tag.</summary>
        public int Entry { get; }

        /// <summary>Gets the decoded YAML string containing the record data.</summary>
        public string Data { get; }

        /// <summary>Gets the one-based source line of the record.</summary>
        public int Line { get; }

        internal OdinMigrationSerializedNode(string name, int entry, string data, int line)
        {
            Name = name;
            Entry = entry;
            Data = data;
            Line = line;
        }
    }
}
