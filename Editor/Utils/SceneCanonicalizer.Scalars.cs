// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
    public static partial class SceneCanonicalizer
    {
        private static bool[] FindScalarCharacters(string text)
        {
            bool[] protectedText = new bool[text.Length];
            char quote = '\0';
            int blockIndent = -1;
            int flowDepth = 0;
            int lineStart = 0;
            while (lineStart < text.Length)
            {
                int lineEnd = text.IndexOf('\n', lineStart);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }
                int contentStart = lineStart;
                while (contentStart < lineEnd && text[contentStart] == ' ')
                {
                    ++contentStart;
                }
                int indent = contentStart - lineStart;
                int fieldValueStart = FindFieldValueStart(text, contentStart, lineEnd);
                if (
                    0 <= blockIndent
                    && (
                        blockIndent < indent
                        || contentStart == lineEnd
                        || text[contentStart] == '\r'
                    )
                )
                {
                    for (int i = lineStart; i < lineEnd; ++i)
                    {
                        protectedText[i] = true;
                    }
                }
                else
                {
                    blockIndent = -1;
                    for (int i = contentStart; i < lineEnd; ++i)
                    {
                        char character = text[i];
                        if (quote != '\0')
                        {
                            protectedText[i] = true;
                            if (quote == '"' && character == '\\' && i + 1 < lineEnd)
                            {
                                protectedText[++i] = true;
                            }
                            else if (character == quote)
                            {
                                if (quote == '\'' && i + 1 < lineEnd && text[i + 1] == '\'')
                                {
                                    protectedText[++i] = true;
                                }
                                else
                                {
                                    quote = '\0';
                                }
                            }
                            continue;
                        }
                        bool tokenStart =
                            i == contentStart
                            || text[i - 1] == ' '
                            || text[i - 1] == ':'
                            || text[i - 1] == '['
                            || text[i - 1] == ',';
                        bool valueStart = false;
                        if (
                            character == '\''
                            || character == '"'
                            || character == '|'
                            || character == '>'
                        )
                        {
                            int previous = i - 1;
                            while (contentStart <= previous && text[previous] == ' ')
                            {
                                --previous;
                            }
                            valueStart =
                                i == fieldValueStart
                                || (
                                    0 < flowDepth
                                    && contentStart <= previous
                                    && (
                                        text[previous] == ':'
                                        || text[previous] == '['
                                        || text[previous] == '{'
                                        || text[previous] == ','
                                    )
                                );
                        }
                        if (valueStart && (character == '\'' || character == '"'))
                        {
                            quote = character;
                            protectedText[i] = true;
                        }
                        else if (tokenStart && character == '#')
                        {
                            for (int j = i; j < lineEnd; ++j)
                            {
                                protectedText[j] = true;
                            }
                            break;
                        }
                        else if (
                            i == fieldValueStart
                            && flowDepth == 0
                            && (character == '|' || character == '>')
                        )
                        {
                            blockIndent = indent;
                        }
                        else if (
                            (i == fieldValueStart || 0 < flowDepth)
                            && (character == '[' || character == '{')
                        )
                        {
                            ++flowDepth;
                        }
                        else if (0 < flowDepth && (character == ']' || character == '}'))
                        {
                            --flowDepth;
                        }
                    }
                }
                lineStart = lineEnd + 1;
            }
            return protectedText;
        }

        private static int FindFieldValueStart(string text, int contentStart, int lineEnd)
        {
            int start = contentStart;
            if (
                start < lineEnd
                && text[start] == '-'
                && start + 1 < lineEnd
                && text[start + 1] == ' '
            )
            {
                ++start;
                while (start < lineEnd && text[start] == ' ')
                {
                    ++start;
                }
            }
            if (
                start == lineEnd
                || text[start] == '['
                || text[start] == '{'
                || text[start] == '\''
                || text[start] == '"'
                || text[start] == '|'
                || text[start] == '>'
            )
            {
                return start;
            }
            int colon = text.IndexOf(':', start, lineEnd - start);
            if (
                colon < 0
                || (colon + 1 < lineEnd && text[colon + 1] != ' ' && text[colon + 1] != '\r')
            )
            {
                return start;
            }
            start = colon + 1;
            while (start < lineEnd && text[start] == ' ')
            {
                ++start;
            }
            return start;
        }
    }
}
