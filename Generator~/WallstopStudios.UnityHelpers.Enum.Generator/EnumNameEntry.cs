// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Enum.Generator
{
    /// <summary>Describes one generated name table entry.</summary>
    internal sealed class EnumNameEntry
    {
        public readonly ulong Key;
        public readonly string Name;
        public readonly string DisplayName;

        public EnumNameEntry(ulong key, string name, string displayName)
        {
            Key = key;
            Name = name;
            DisplayName = displayName;
        }
    }
}
