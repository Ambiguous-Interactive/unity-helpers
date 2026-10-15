// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System.Collections.Generic;

    /// <summary>Preserves one Odin Nodes record without constructing its recorded type.</summary>
    public readonly struct OdinMigrationSerializedNode
        : global::System.IEquatable<OdinMigrationSerializedNode>
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

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(OdinMigrationSerializedNode other)
        {
            return EqualityComparer<string>.Default.Equals(Name, other.Name)
                && Entry.Equals(other.Entry)
                && EqualityComparer<string>.Default.Equals(Data, other.Data)
                && Line.Equals(other.Line);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is OdinMigrationSerializedNode other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                Name,
                Entry,
                Data,
                Line
            );
        }
    }
}
