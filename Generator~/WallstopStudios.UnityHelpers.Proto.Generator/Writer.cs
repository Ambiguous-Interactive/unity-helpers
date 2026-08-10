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
            // The overwhelmingly common case is a single line, and Split allocates a string[] plus a
            // string per segment for every one of them. Only `Writer.Open` and a handful of literals
            // are multi-line, so the split is worth paying for exactly when it is needed.
            if (text.IndexOf('\n') < 0)
            {
                AppendLine(text);
                return;
            }

            foreach (string part in text.Split('\n'))
            {
                AppendLine(part);
            }
        }

        /// <summary>
        /// Writes one line from parts, without concatenating them first.
        /// </summary>
        /// <remarks>
        /// The emitters build most lines from a fixed handful of fragments -- a keyword, a member
        /// name, a tag. Written as `a + b + c` each of those allocates an intermediate string per
        /// operand pair, all of which is garbage the moment it reaches the StringBuilder. These
        /// overloads append the fragments straight into the buffer instead.
        ///
        /// Fixed arities rather than `params string[]`, because a params array is itself an
        /// allocation per call and would give back most of what this saves.
        /// </remarks>
        internal void Line(string a, string b)
        {
            Indentation();
            _builder.Append(a).Append(b).Append('\n');
        }

        /// <inheritdoc cref="Line(string, string)"/>
        internal void Line(string a, string b, string c)
        {
            Indentation();
            _builder.Append(a).Append(b).Append(c).Append('\n');
        }

        /// <inheritdoc cref="Line(string, string)"/>
        internal void Line(string a, string b, string c, string d)
        {
            Indentation();
            _builder.Append(a).Append(b).Append(c).Append(d).Append('\n');
        }

        /// <inheritdoc cref="Line(string, string)"/>
        internal void Line(string a, string b, string c, string d, string e)
        {
            Indentation();
            _builder.Append(a).Append(b).Append(c).Append(d).Append(e).Append('\n');
        }

        /// <inheritdoc cref="Line(string, string)"/>
        internal void Line(string a, string b, string c, string d, string e, string f)
        {
            Indentation();
            _builder.Append(a).Append(b).Append(c).Append(d).Append(e).Append(f).Append('\n');
        }

        /// <inheritdoc cref="Line(string, string)"/>
        internal void Line(string a, string b, string c, string d, string e, string f, string g)
        {
            Indentation();
            _builder.Append(a).Append(b).Append(c).Append(d).Append(e).Append(f).Append(g);
            _builder.Append('\n');
        }

        private void AppendLine(string part)
        {
            if (part.Length == 0)
            {
                _builder.Append('\n');
                return;
            }

            Indentation();
            _builder.Append(part);
            _builder.Append('\n');
        }

        private void Indentation()
        {
            _builder.Append(' ', _depth * 4);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}
