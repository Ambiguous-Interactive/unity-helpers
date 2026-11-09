// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Validation;

    [TestFixture]
    public sealed class AuthoredAssetYamlBlockTests
    {
        private static AuthoredAssetDocument Parse(string[] lines)
        {
            IReadOnlyList<AuthoredAssetDocument> documents = AuthoredAssetYaml.ReadDocuments(lines);
            Assert.That(documents.Count, Is.EqualTo(1));
            return documents[0];
        }

        private static AuthoredAssetEntry Field(AuthoredAssetDocument document, string name)
        {
            foreach (AuthoredAssetEntry entry in document.Entries)
            {
                if (string.Equals(entry.Key, name, StringComparison.Ordinal))
                {
                    return entry;
                }
            }
            Assert.Fail("Expected a parsed field: " + name);
            return default;
        }

        [Test]
        public void BlockReadsKeepDirectElementsAndSourceLines()
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  values:",
                "  - first",
                "  # comment",
                "  - name: second",
                "    nested:",
                "    - deeper",
                "  -",
                "    name: third",
                "  tail: done",
            };
            AuthoredAssetDocument document = Parse(lines);
            AuthoredAssetEntry field = Field(document, "values");
            IReadOnlyList<AuthoredSequenceElement> items = AuthoredAssetYaml.ReadBlockItems(
                lines,
                field
            );
            Assert.That(items.Count, Is.EqualTo(3));
            Assert.That(AuthoredAssetYaml.CountBlockItems(lines, field), Is.EqualTo(4));
            Assert.That(
                AuthoredAssetYaml.CountBlockItems(lines, field, includeNested: false),
                Is.EqualTo(3)
            );
            Assert.That(items[0].LineNumber, Is.EqualTo(4));
            Assert.That(items[0].Value, Is.EqualTo("first"));
            Assert.That(items[1].LineNumber, Is.EqualTo(6));
            Assert.That(items[1].Value, Is.EqualTo("name: second"));
            Assert.That(items[2].LineNumber, Is.EqualTo(9));
            Assert.That(items[2].Value, Is.Empty);
        }

        [TestCase("", TestName = "BlockHeader.Empty.ReadsItems")]
        [TestCase(" # note", TestName = "BlockHeader.Comment.ReadsItems")]
        public void HeaderCommentsDoNotHideSequenceItems(string suffix)
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  values:" + suffix,
                "  - first",
            };
            AuthoredAssetDocument document = Parse(lines);
            AuthoredAssetEntry entry = Field(document, "values");
            Assert.That(AuthoredAssetYaml.ReadBlockItems(lines, entry).Count, Is.EqualTo(1));
            Assert.That(AuthoredAssetYaml.CountBlockItems(lines, entry), Is.EqualTo(1));
        }

        [Test]
        public void NestedReadsStayInsideEachDirectMappingElement()
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  groups:",
                "  - children:",
                "    - first",
                "    - second",
                "  - name: middle",
                "    children: []",
                "  -",
                "    children:",
                "    - third",
                "    nested:",
                "      children:",
                "      - hidden",
                "  children:",
                "  - outside",
            };
            AuthoredAssetDocument document = Parse(lines);
            AuthoredAssetEntry groups = Field(document, "groups");
            IReadOnlyList<AuthoredSequenceElement> items = AuthoredAssetYaml.NestedBlockItems(
                lines,
                document,
                groups,
                "children:"
            );
            Assert.That(items.Count, Is.EqualTo(3));
            Assert.That(items[0].Value, Is.EqualTo("first"));
            Assert.That(items[1].Value, Is.EqualTo("second"));
            Assert.That(items[2].Value, Is.EqualTo("third"));
        }

        [Test]
        public void DuplicateNestedFieldRefusesTheWholeRead()
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  groups:",
                "  - children:",
                "    - first",
                "  - children:",
                "    - second",
                "    children:",
                "    - ambiguous",
            };
            AuthoredAssetDocument document = Parse(lines);
            AuthoredAssetEntry groups = Field(document, "groups");
            Assert.That(
                AuthoredAssetYaml.NestedBlockItems(lines, document, groups, "children"),
                Is.Empty
            );
        }

        [TestCase(null, TestName = "NestedBlock.NullField.Empty")]
        [TestCase("", TestName = "NestedBlock.EmptyField.Empty")]
        [TestCase(" ", TestName = "NestedBlock.WhitespaceField.Empty")]
        [TestCase("missing", TestName = "NestedBlock.AbsentField.Empty")]
        public void InvalidNestedFieldReturnsEmpty(string name)
        {
            string[] lines = { "--- !u!114 &7", "MonoBehaviour:", "  values:", "  - name: value" };
            AuthoredAssetDocument document = Parse(lines);
            Assert.That(
                AuthoredAssetYaml.NestedBlockItems(
                    lines,
                    document,
                    Field(document, "values"),
                    name
                ),
                Is.Empty
            );
        }

        [Test]
        public void UnknownAndReconstructedParentsRespectDocumentMembership()
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  values:",
                "  - children:",
                "    - child",
            };
            AuthoredAssetDocument document = Parse(lines);
            AuthoredAssetEntry parent = Field(document, "values");
            AuthoredAssetEntry copy = new(
                parent.Key,
                parent.InlineValue,
                parent.Indent,
                parent.LineNumber,
                parent.EndLineNumber
            );
            Assert.That(
                AuthoredAssetYaml.NestedBlockItems(lines, document, copy, "children").Count,
                Is.EqualTo(1)
            );
            Assert.That(
                AuthoredAssetYaml.NestedBlockItems(lines, document, default, "children"),
                Is.Empty
            );
            Assert.That(
                AuthoredAssetYaml.NestedBlockItems(lines, null, parent, "children"),
                Is.Empty
            );
            Assert.That(
                AuthoredAssetYaml.NestedBlockItems(null, document, parent, "children"),
                Is.Empty
            );
        }

        [TestCase(-1, 6, 2, "", TestName = "BlockSpan.NegativeStart.Empty")]
        [TestCase(0, 6, 2, "", TestName = "BlockSpan.ZeroStart.Empty")]
        [TestCase(7, 9, 2, "", TestName = "BlockSpan.StartPastSource.Empty")]
        [TestCase(3, 2, 2, "", TestName = "BlockSpan.Reversed.Empty")]
        [TestCase(3, 6, -1, "", TestName = "BlockSpan.NegativeIndent.Empty")]
        [TestCase(3, 6, 2, "[]", TestName = "BlockSpan.InlineSequence.Empty")]
        [TestCase(3, 6, 2, "|", TestName = "BlockSpan.LiteralScalar.Empty")]
        [TestCase(3, 6, 2, ">", TestName = "BlockSpan.FoldedScalar.Empty")]
        public void InvalidSpansAndInlineValuesReturnEmpty(
            int start,
            int end,
            int indent,
            string value
        )
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  values:",
                "  - first",
                "  - second",
            };
            AuthoredAssetEntry entry = new("values", value, indent, start, end);
            Assert.That(AuthoredAssetYaml.ReadBlockItems(lines, entry), Is.Empty);
            Assert.That(AuthoredAssetYaml.CountBlockItems(lines, entry), Is.Zero);
            Assert.That(AuthoredAssetYaml.ReadBlockItems(null, entry), Is.Empty);
        }

        [Test]
        public void NullLinesAndNonSequenceDashesAreNotElements()
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  values:",
                null,
                "  -notAnItem",
                "  - item",
            };
            AuthoredAssetEntry entry = new("values", string.Empty, 2, 3, int.MaxValue);
            IReadOnlyList<AuthoredSequenceElement> items = AuthoredAssetYaml.ReadBlockItems(
                lines,
                entry
            );
            Assert.That(items.Count, Is.EqualTo(1));
            Assert.That(items[0].Value, Is.EqualTo("item"));
        }

        [TestCase(null, 0, 0, "", TestName = "Key.Null.Empty")]
        [TestCase("", 0, 0, "", TestName = "Key.Empty.Empty")]
        [TestCase("   ", 3, 3, "", TestName = "Key.Spaces.Empty")]
        [TestCase("  key: value", 2, 2, "key: value", TestName = "Key.Mapping.RetainsValue")]
        [TestCase("  - key: value", 2, 4, "key: value", TestName = "Key.Sequence.RetainsValue")]
        [TestCase(
            "  -   key: value",
            2,
            4,
            "key: value",
            TestName = "Key.SequenceSpacing.TrimsContent"
        )]
        [TestCase("  'key': value", 2, 2, "'key': value", TestName = "Key.SingleQuotes.Retained")]
        [TestCase(
            "  \"key\": value",
            2,
            2,
            "\"key\": value",
            TestName = "Key.DoubleQuotes.Retained"
        )]
        [TestCase("  # key: value", 2, 2, "# key: value", TestName = "Key.Comment.Retained")]
        [TestCase("  - scalar", 2, 4, "scalar", TestName = "Key.Scalar.MarkerRemoved")]
        [TestCase("  -", 2, 4, "", TestName = "Key.BareMarker.Empty")]
        [TestCase("  key:value", 2, 2, "key:value", TestName = "Key.UnparsedText.Retained")]
        public void KeyQueriesPreserveConsumerEntryText(
            string line,
            int indent,
            int column,
            string key
        )
        {
            Assert.That(AuthoredAssetYaml.IndentOf(line), Is.EqualTo(indent));
            Assert.That(AuthoredAssetYaml.KeyColumnOf(line), Is.EqualTo(column));
            Assert.That(AuthoredAssetYaml.KeyOf(line), Is.EqualTo(key));
            Assert.That(AuthoredAssetYaml.ColumnStep, Is.EqualTo(2));
        }

        [Test]
        public void NestedReadsSupportAMappingParent()
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  outer:",
                "    children:",
                "    - first",
                "    - second",
                "  children:",
                "  - outside",
            };
            AuthoredAssetDocument document = Parse(lines);
            IReadOnlyList<AuthoredSequenceElement> items = AuthoredAssetYaml.NestedBlockItems(
                lines,
                document,
                Field(document, "outer"),
                "children"
            );
            Assert.That(items.Count, Is.EqualTo(2));
            Assert.That(items[0].Value, Is.EqualTo("first"));
            Assert.That(items[1].Value, Is.EqualTo("second"));
        }

        [Test]
        public void CountsNestedMarkersWithoutCountingBlockScalarText()
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  outer:",
                "    values:",
                "    - note: |",
                "        - literal",
                "      children:",
                "      - real",
                "    - >",
                "      - folded",
                "    -",
                "      note: |",
                "        - text",
                "      children:",
                "      - final",
            };
            AuthoredAssetDocument document = Parse(lines);
            AuthoredAssetEntry parent = Field(document, "outer");
            Assert.That(AuthoredAssetYaml.CountBlockItems(lines, parent), Is.EqualTo(5));
            Assert.That(
                AuthoredAssetYaml.CountBlockItems(lines, parent, includeNested: false),
                Is.Zero
            );
        }

        [Test]
        public void LargeNestedSequenceKeepsEveryItemInSourceOrder()
        {
            List<string> source = new() { "--- !u!114 &7", "MonoBehaviour:", "  groups:" };
            for (int index = 0; index < 10000; ++index)
            {
                source.Add("  - children:");
                source.Add("    - " + index);
            }
            string[] lines = source.ToArray();
            AuthoredAssetDocument document = Parse(lines);
            IReadOnlyList<AuthoredSequenceElement> items = AuthoredAssetYaml.NestedBlockItems(
                lines,
                document,
                Field(document, "groups"),
                "children"
            );
            Assert.That(items.Count, Is.EqualTo(10000));
            Assert.That(items[0].Value, Is.EqualTo("0"));
            Assert.That(items[9999].Value, Is.EqualTo("9999"));
        }

        [Test]
        public void LargeBlockReadsDoNotTruncateOrIncludeTheFollowingField()
        {
            List<string> source = new() { "--- !u!114 &7", "MonoBehaviour:", "  values:" };
            for (int index = 0; index < 10000; ++index)
            {
                source.Add("  - value");
            }
            source.Add("  following: done");
            string[] lines = source.ToArray();
            AuthoredAssetDocument document = Parse(lines);
            AuthoredAssetEntry entry = Field(document, "values");
            IReadOnlyList<AuthoredSequenceElement> items = AuthoredAssetYaml.ReadBlockItems(
                lines,
                entry
            );
            Assert.That(items.Count, Is.EqualTo(10000));
            Assert.That(items[9999].LineNumber, Is.EqualTo(10003));
            Assert.That(AuthoredAssetYaml.CountBlockItems(lines, entry), Is.EqualTo(10000));
        }
    }
}
