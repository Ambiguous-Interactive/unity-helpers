// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.SyntaxPolicy
{
    using System.Collections.Generic;

    internal sealed class StackAllocationSource
    {
        internal string Path { get; }
        internal string Text { get; }
        internal List<string> Symbols { get; }

        internal StackAllocationSource(string path, string text)
        {
            Path = path;
            Text = text;
            Symbols = Program.FindSymbols(text);
        }
    }
}
