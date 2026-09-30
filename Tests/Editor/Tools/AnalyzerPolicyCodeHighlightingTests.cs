// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Tools;

    [TestFixture]
    public sealed class AnalyzerPolicyCodeHighlightingTests
    {
        [TestCase(null)]
        [TestCase("")]
        public void EmptySourceHasNoMarkup(string code)
        {
            Assert.IsEmpty(AnalyzerPolicyCodeHighlighting.Format(code));
        }

        [Test]
        public void WarmSnippetsReuseTheFormattedStringInEachTheme()
        {
            const string Code = "if (item != null) { Use(item); }";
            string dark = AnalyzerPolicyCodeHighlighting.Format(Code, true);
            string light = AnalyzerPolicyCodeHighlighting.Format(Code, false);
            Assert.AreSame(dark, AnalyzerPolicyCodeHighlighting.Format(Code, true));
            Assert.AreSame(light, AnalyzerPolicyCodeHighlighting.Format(Code, false));
            Assert.AreNotEqual(dark, light);
        }

        [TestCase(true, "#80BFFF", "#CE9178", "#8CBF73")]
        [TestCase(false, "#174EA6", "#A31515", "#477A32")]
        public void KeywordsStringsAndCommentsHaveSeparateThemeColors(
            bool darkTheme,
            string keywordColor,
            string stringColor,
            string commentColor
        )
        {
            string formatted = AnalyzerPolicyCodeHighlighting.Format(
                "string mode = \"if\"; // return",
                darkTheme
            );
            Assert.That(
                formatted,
                Does.Contain("<color=" + keywordColor + "><noparse>string</noparse></color>")
            );
            Assert.That(
                formatted,
                Does.Contain("<color=" + stringColor + "><noparse>\"if\"</noparse></color>")
            );
            Assert.That(
                formatted,
                Does.Contain("<color=" + commentColor + "><noparse>// return</noparse></color>")
            );
            Assert.That(formatted, Does.Not.Contain("<noparse>if</noparse>"));
        }

        [Test]
        public void GenericSyntaxAndCodeMarkupAreDisplayedLiterally()
        {
            string formatted = AnalyzerPolicyCodeHighlighting.Format(
                "List<int> rows; string text = \"<color=red>bad</color>\";"
            );
            Assert.That(formatted, Does.Contain("<noparse>\"<color=red>bad</color>\"</noparse>"));
            Assert.That(formatted, Does.Contain("<noparse><</noparse>"));
        }

        [Test]
        public void LiteralNoParseClosersCannotActivateCodeMarkup()
        {
            string formatted = AnalyzerPolicyCodeHighlighting.Format(
                "string text = \"</noparse><size=90>bad</size>\";"
            );
            Assert.That(formatted, Does.Contain("</no\u200Bparse><size=90>bad</size>"));
            Assert.That(formatted, Does.Not.Contain("</noparse><size=90>"));
        }

        [TestCase("\n")]
        [TestCase("\r\n")]
        [TestCase("\r")]
        public void LeadingSpacesStayVisibleAtTheStartAndAfterLineBreaks(string newline)
        {
            string code = "    string text = \"a b\";" + newline + "    return text;";
            string formatted = AnalyzerPolicyCodeHighlighting.Format(code, true);
            Assert.That(formatted, Does.StartWith("<noparse>\u00A0\u00A0\u00A0\u00A0</noparse>"));
            Assert.That(formatted, Does.Contain(newline + "\u00A0\u00A0\u00A0\u00A0"));
            Assert.That(formatted, Does.Contain("<noparse> = </noparse>"));
            Assert.That(formatted, Does.Contain("<noparse>\"a b\"</noparse>"));
        }

        [Test]
        public void InlineAndStringSpacesKeepTheirOrdinaryCharacters()
        {
            string formatted = AnalyzerPolicyCodeHighlighting.Format(
                "string text = \"  a  b  \"; return text;",
                true
            );
            Assert.That(formatted, Does.Not.Contain("\u00A0"));
            Assert.That(formatted, Does.Contain("<noparse>\"  a  b  \"</noparse>"));
            Assert.That(formatted, Does.Contain("<noparse>; </noparse>"));
        }

        [TestCase("string text = \"escaped \\\" return\";")]
        [TestCase("string text = @\"doubled \"\" return\";")]
        [TestCase("/* if return */ int count = 7;")]
        public void QuotesAndBlockCommentsDoNotHighlightTheirInteriorKeywords(string code)
        {
            string formatted = AnalyzerPolicyCodeHighlighting.Format(code, true);
            Assert.That(formatted, Does.Not.Contain("<color=#80BFFF><noparse>return</noparse>"));
            Assert.That(formatted, Does.Contain("</color>"));
        }
    }
}
