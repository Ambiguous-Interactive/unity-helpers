// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System.Text;

    /// <summary>
    /// Accumulates generated source with tracked indentation.
    /// </summary>
    /// <remarks>
    /// Generated code is read by humans exactly when something has gone wrong with it, so it is
    /// emitted at the same indentation and brace style as the hand-written formatters it replaces.
    /// </remarks>
    internal sealed class Writer
    {
        /// <summary>The opening brace, on its own line, as this repository writes them.</summary>
        internal const string Open = "\n{";

        private readonly StringBuilder _builder = new StringBuilder();
        private int _depth;

        internal void Indent()
        {
            _depth++;
        }

        internal void Outdent()
        {
            if (0 < _depth)
            {
                _depth--;
            }
        }

        internal void Blank()
        {
            _builder.Append('\n');
        }

        internal void Line(string text)
        {
            foreach (string part in text.Split('\n'))
            {
                if (part.Length == 0)
                {
                    _builder.Append('\n');
                    continue;
                }

                _builder.Append(' ', _depth * 4);
                _builder.Append(part);
                _builder.Append('\n');
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}
