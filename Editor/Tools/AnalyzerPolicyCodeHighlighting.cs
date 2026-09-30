// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Text;
    using UnityEditor;

    internal static class AnalyzerPolicyCodeHighlighting
    {
        private const int MaximumCachedSnippets = 64;
        private const string NoParseClose = "</noparse>";

        private static readonly Dictionary<string, string> DarkCache = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> LightCache = new(StringComparer.Ordinal);
        private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
        {
            "base",
            "bool",
            "class",
            "else",
            "false",
            "float",
            "for",
            "foreach",
            "if",
            "in",
            "int",
            "internal",
            "new",
            "null",
            "out",
            "override",
            "private",
            "protected",
            "public",
            "readonly",
            "return",
            "sealed",
            "static",
            "string",
            "struct",
            "this",
            "true",
            "using",
            "virtual",
            "void",
            "while",
        };

        internal static string Format(string code)
        {
            return Format(code, EditorGUIUtility.isProSkin);
        }

        internal static string Format(string code, bool darkTheme)
        {
            if (string.IsNullOrEmpty(code))
            {
                return string.Empty;
            }
            Dictionary<string, string> cache = darkTheme ? DarkCache : LightCache;
            if (cache.TryGetValue(code, out string formatted))
            {
                return formatted;
            }
            StringBuilder output = new(code.Length * 2);
            int index = 0;
            while (index < code.Length)
            {
                int start = index;
                char current = code[index];
                string color = null;
                if (current == '"' || current == '\'')
                {
                    index = EndQuotedLiteral(code, index, current);
                    color = darkTheme ? "#CE9178" : "#A31515";
                }
                else if (current == '/' && index + 1 < code.Length && code[index + 1] == '/')
                {
                    while (index < code.Length && code[index] != '\n')
                    {
                        index++;
                    }
                    color = darkTheme ? "#8CBF73" : "#477A32";
                }
                else if (current == '/' && index + 1 < code.Length && code[index + 1] == '*')
                {
                    index += 2;
                    while (index < code.Length)
                    {
                        if (code[index] == '*' && index + 1 < code.Length && code[index + 1] == '/')
                        {
                            index += 2;
                            break;
                        }
                        index++;
                    }
                    color = darkTheme ? "#8CBF73" : "#477A32";
                }
                else if (char.IsDigit(current))
                {
                    do
                    {
                        index++;
                    } while (
                        index < code.Length
                        && (char.IsLetterOrDigit(code[index]) || code[index] == '.')
                    );
                    color = darkTheme ? "#B5CEA8" : "#68501E";
                }
                else if (char.IsLetter(current) || current == '_')
                {
                    do
                    {
                        index++;
                    } while (
                        index < code.Length
                        && (char.IsLetterOrDigit(code[index]) || code[index] == '_')
                    );
                    string token = code.Substring(start, index - start);
                    if (Keywords.Contains(token))
                    {
                        color = darkTheme ? "#80BFFF" : "#174EA6";
                    }
                    else if (char.IsUpper(current))
                    {
                        color = darkTheme ? "#63D5C1" : "#126D62";
                    }
                }
                else
                {
                    do
                    {
                        index++;
                    } while (
                        index < code.Length
                        && !char.IsLetterOrDigit(code[index])
                        && code[index] != '_'
                        && code[index] != '"'
                        && code[index] != '\''
                        && code[index] != '/'
                    );
                }
                if (color != null)
                {
                    output.Append("<color=").Append(color).Append('>');
                }
                AppendLiteral(output, code, start, index - start);
                if (color != null)
                {
                    output.Append("</color>");
                }
            }
            formatted = output.ToString();
            if (MaximumCachedSnippets <= cache.Count)
            {
                cache.Clear();
            }
            cache.Add(code, formatted);
            return formatted;
        }

        private static int EndQuotedLiteral(string code, int start, char quote)
        {
            bool verbatim = quote == '"' && 0 < start && code[start - 1] == '@';
            int index = start + 1;
            while (index < code.Length)
            {
                if (!verbatim && code[index] == '\\')
                {
                    index += Math.Min(2, code.Length - index);
                    continue;
                }
                if (code[index] == quote)
                {
                    if (verbatim && index + 1 < code.Length && code[index + 1] == quote)
                    {
                        index += 2;
                        continue;
                    }
                    return index + 1;
                }
                index++;
            }
            return index;
        }

        private static void AppendLiteral(StringBuilder output, string code, int start, int length)
        {
            output.Append("<noparse>");
            int end = start + length;
            for (int index = start; index < end; index++)
            {
                if (
                    code[index] == '<'
                    && index + NoParseClose.Length <= end
                    && string.Compare(
                        code,
                        index,
                        NoParseClose,
                        0,
                        NoParseClose.Length,
                        StringComparison.OrdinalIgnoreCase
                    ) == 0
                )
                {
                    output.Append("</no\u200Bparse>");
                    index += NoParseClose.Length - 1;
                    continue;
                }
                output.Append(
                    code[index] == ' ' && IsLineIndentation(code, index) ? '\u00A0' : code[index]
                );
            }
            output.Append("</noparse>");
        }

        private static bool IsLineIndentation(string code, int index)
        {
            for (int previous = index - 1; 0 <= previous; previous--)
            {
                char character = code[previous];
                if (character == '\n' || character == '\r')
                {
                    return true;
                }
                if (character != ' ')
                {
                    return false;
                }
            }
            return true;
        }
    }
#endif
}
