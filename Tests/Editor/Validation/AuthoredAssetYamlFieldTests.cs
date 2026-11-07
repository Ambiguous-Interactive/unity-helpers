// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Validation;

    [TestFixture]
    public sealed class AuthoredAssetYamlFieldTests
    {
        private static AuthoredAssetDocument Document(params string[] fields)
        {
            List<string> lines = new List<string> { "--- !u!114 &7", "MonoBehaviour:" };
            lines.AddRange(fields);
            IReadOnlyList<AuthoredAssetDocument> documents = AuthoredAssetYaml.ReadDocuments(lines);
            Assert.That(documents.Count, Is.EqualTo(1));
            return documents[0];
        }

        [TestCase("value", TestName = "DirectField.ExactName.ReadsValue")]
        [TestCase("value:", TestName = "DirectField.TrailingColon.ReadsValue")]
        public void DirectFieldDoesNotReadNestedNames(string field)
        {
            AuthoredAssetDocument document = Document(
                "  nested:",
                "    value: child",
                "  value: root"
            );
            Assert.That(AuthoredAssetYaml.TryGetField(document, field, out string value), Is.True);
            Assert.That(value, Is.EqualTo("root"));
            Assert.That(AuthoredAssetYaml.Field(document, field), Is.EqualTo("root"));
        }

        [TestCase(null, TestName = "Field.NullName.FailsSoft")]
        [TestCase("", TestName = "Field.EmptyName.FailsSoft")]
        [TestCase(":", TestName = "Field.BareColon.FailsSoft")]
        [TestCase("absent", TestName = "Field.MissingName.FailsSoft")]
        public void MissingFieldReturnsEmpty(string field)
        {
            AuthoredAssetDocument document = Document("  nested:", "    value: child");
            Assert.That(AuthoredAssetYaml.TryGetField(document, field, out string value), Is.False);
            Assert.That(value, Is.EqualTo(string.Empty));
            Assert.That(AuthoredAssetYaml.TryGetOnlyField(document, field, out value), Is.False);
            Assert.That(value, Is.EqualTo(string.Empty));
            Assert.That(AuthoredAssetYaml.Field(document, field), Is.EqualTo(string.Empty));
        }

        [Test]
        public void DirectFieldDoesNotFallBackToNestedOrLegacyName()
        {
            AuthoredAssetDocument document = Document(
                "  nested:",
                "    value: child",
                "  renamed: legacy"
            );
            Assert.That(
                AuthoredAssetYaml.TryGetField(document, "value", out string value),
                Is.False
            );
            Assert.That(value, Is.Empty);
            Assert.That(AuthoredAssetYaml.TryGetField(document, "_renamed", out value), Is.False);
            Assert.That(value, Is.Empty);
        }

        [Test]
        public void DuplicateDirectFieldKeepsFirstButUniqueReadRefusesIt()
        {
            AuthoredAssetDocument document = Document("  value: first", "  value: second");
            Assert.That(AuthoredAssetYaml.Field(document, "value"), Is.EqualTo("first"));
            Assert.That(
                AuthoredAssetYaml.TryGetOnlyField(document, "value:", out string value),
                Is.False
            );
            Assert.That(value, Is.Empty);
        }

        [Test]
        public void UniqueReadSearchesAllDepthsAndRefusesAmbiguity()
        {
            AuthoredAssetDocument unique = Document("  nested:", "    value: child");
            Assert.That(
                AuthoredAssetYaml.TryGetOnlyField(unique, "value", out string value),
                Is.True
            );
            Assert.That(value, Is.EqualTo("child"));
            AuthoredAssetDocument duplicate = Document(
                "  value: root",
                "  nested:",
                "    value: child"
            );
            Assert.That(AuthoredAssetYaml.TryGetOnlyField(duplicate, "value", out value), Is.False);
            Assert.That(value, Is.Empty);
        }

        [Test]
        public void EmptyInlineValueIsPresent()
        {
            AuthoredAssetDocument document = Document("  empty:", "  sequence: []");
            Assert.That(
                AuthoredAssetYaml.TryGetField(document, "empty", out string empty),
                Is.True
            );
            Assert.That(empty, Is.Empty);
            Assert.That(AuthoredAssetYaml.TryGetOnlyField(document, "empty", out empty), Is.True);
            Assert.That(empty, Is.Empty);
            Assert.That(AuthoredAssetYaml.Field(document, "sequence"), Is.EqualTo("[]"));
        }

        [Test]
        public void BlockScalarTextIsNotParsedAsAField()
        {
            AuthoredAssetDocument document = Document(
                "  text: |",
                "    fake: not a field",
                "  actual: yes"
            );
            Assert.That(
                AuthoredAssetYaml.TryGetOnlyField(document, "fake", out string value),
                Is.False
            );
            Assert.That(value, Is.Empty);
            Assert.That(AuthoredAssetYaml.Field(document, "actual"), Is.EqualTo("yes"));
        }

        [Test]
        public void NullDocumentFailsSoft()
        {
            Assert.That(AuthoredAssetYaml.TryGetField(null, "value", out string value), Is.False);
            Assert.That(value, Is.Empty);
            Assert.That(AuthoredAssetYaml.TryGetOnlyField(null, "value", out value), Is.False);
            Assert.That(value, Is.Empty);
            Assert.That(AuthoredAssetYaml.Field(null, "value"), Is.Empty);
            Assert.That(AuthoredAssetYaml.ValueWithDirectChildren(null, "value"), Is.Empty);
            Assert.That(
                AuthoredAssetYaml.TryDescend(null, default, "value", out AuthoredAssetEntry child),
                Is.False
            );
            Assert.That(child, Is.EqualTo(default(AuthoredAssetEntry)));
        }

        [Test]
        public void DescendReadsOnlyTheRequestedParentsDirectChild()
        {
            AuthoredAssetDocument document = Document(
                "  parent:",
                "    value: direct",
                "    nested:",
                "      value: deep",
                "  sibling:",
                "    value: other"
            );
            Assert.That(document.TryGetEntry("parent", out AuthoredAssetEntry parent), Is.True);
            Assert.That(
                AuthoredAssetYaml.TryDescend(
                    document,
                    parent,
                    "value:",
                    out AuthoredAssetEntry child
                ),
                Is.True
            );
            Assert.That(child.InlineValue, Is.EqualTo("direct"));
            Assert.That(
                AuthoredAssetYaml.TryDescend(
                    document,
                    parent,
                    "nested",
                    out AuthoredAssetEntry nested
                ),
                Is.True
            );
            Assert.That(
                AuthoredAssetYaml.TryDescend(document, nested, "value", out child),
                Is.True
            );
            Assert.That(child.InlineValue, Is.EqualTo("deep"));
        }

        [Test]
        public void DescendRefusesDuplicateChildNames()
        {
            AuthoredAssetDocument document = Document(
                "  parent:",
                "    value: first",
                "    value: second"
            );
            Assert.That(document.TryGetEntry("parent", out AuthoredAssetEntry parent), Is.True);
            Assert.That(
                AuthoredAssetYaml.TryDescend(
                    document,
                    parent,
                    "value",
                    out AuthoredAssetEntry child
                ),
                Is.False
            );
            Assert.That(child, Is.EqualTo(default(AuthoredAssetEntry)));
        }

        [Test]
        public void DescendRefusesAnEntryThatTheDocumentDoesNotOwn()
        {
            AuthoredAssetDocument document = Document("  parent:", "    value: direct");
            AuthoredAssetEntry fabricated = new AuthoredAssetEntry("other", "", 2, 3, 5);
            Assert.That(
                AuthoredAssetYaml.TryDescend(
                    document,
                    fabricated,
                    "value",
                    out AuthoredAssetEntry child
                ),
                Is.False
            );
            Assert.That(child, Is.EqualTo(default(AuthoredAssetEntry)));
        }

        [TestCase(null, TestName = "Descend.NullName.FailsSoft")]
        [TestCase("", TestName = "Descend.EmptyName.FailsSoft")]
        [TestCase("missing", TestName = "Descend.MissingChild.FailsSoft")]
        public void DescendMissingChildReturnsDefault(string field)
        {
            AuthoredAssetDocument document = Document("  parent:", "    value: direct");
            Assert.That(document.TryGetEntry("parent", out AuthoredAssetEntry parent), Is.True);
            Assert.That(
                AuthoredAssetYaml.TryDescend(document, parent, field, out AuthoredAssetEntry child),
                Is.False
            );
            Assert.That(child, Is.EqualTo(default(AuthoredAssetEntry)));
        }

        [Test]
        public void DirectChildrenIgnoreVersionAndDeeperValues()
        {
            AuthoredAssetDocument document = Document(
                "  vector:",
                "    serializedVersion: 2",
                "    x: 1",
                "    nested:",
                "      x: 99",
                "    y: 2",
                "  other: 9"
            );
            Assert.That(
                AuthoredAssetYaml.ValueWithDirectChildren(document, "vector:"),
                Is.EqualTo("1 2")
            );
            Assert.That(
                AuthoredAssetYaml.ValueWithDirectChildren(document, "other"),
                Is.EqualTo("9")
            );
            Assert.That(AuthoredAssetYaml.ValueWithDirectChildren(document, "missing"), Is.Empty);
            Assert.That(AuthoredAssetYaml.ValueWithDirectChildren(document, null), Is.Empty);
        }

        [TestCase(false, TestName = "DirectChildren.SequenceMapping.ExcludesElementKeys")]
        [TestCase(true, TestName = "DirectChildren.BareSequence.ExcludesElementKeys")]
        public void SequenceElementKeysAreNotDirectMappingChildren(bool bareMarker)
        {
            AuthoredAssetDocument document = bareMarker
                ? Document(
                    "  parent:",
                    "  -",
                    "    value: element",
                    "# comment does not end the sequence",
                    "    other: secondary",
                    "  sibling: root"
                )
                : Document(
                    "  parent:",
                    "  - value: element",
                    "# comment does not end the sequence",
                    "    other: secondary",
                    "  sibling: root"
                );
            Assert.That(document.TryGetEntry("parent", out AuthoredAssetEntry parent), Is.True);
            foreach (string field in new[] { "value", "other" })
            {
                Assert.That(
                    AuthoredAssetYaml.TryDescend(
                        document,
                        parent,
                        field,
                        out AuthoredAssetEntry child
                    ),
                    Is.False
                );
                Assert.That(child, Is.EqualTo(default(AuthoredAssetEntry)));
            }
            Assert.That(AuthoredAssetYaml.ValueWithDirectChildren(document, "parent"), Is.Empty);
        }

        [Test]
        public void MappingChildrenAfterNestedSequenceRemainDirectChildren()
        {
            AuthoredAssetDocument document = Document(
                "  parent:",
                "    items:",
                "    - value: element",
                "      other: secondary",
                "    direct: kept"
            );
            Assert.That(document.TryGetEntry("parent", out AuthoredAssetEntry parent), Is.True);
            Assert.That(document.TryGetEntry("items", out AuthoredAssetEntry items), Is.True);
            Assert.That(
                AuthoredAssetYaml.TryDescend(
                    document,
                    items,
                    "value",
                    out AuthoredAssetEntry child
                ),
                Is.False
            );
            Assert.That(
                AuthoredAssetYaml.TryDescend(document, parent, "direct", out child),
                Is.True
            );
            Assert.That(child.InlineValue, Is.EqualTo("kept"));
            Assert.That(
                AuthoredAssetYaml.ValueWithDirectChildren(document, "parent"),
                Is.EqualTo("kept")
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MappingInsideSequenceRetainsItsOwnDirectChildren(bool reconstructParent)
        {
            AuthoredAssetDocument document = Document(
                "  items:",
                "  - nested:",
                "      x: 3",
                "    peer: 4"
            );
            Assert.That(document.TryGetEntry("nested", out AuthoredAssetEntry nested), Is.True);
            if (reconstructParent)
            {
                AuthoredAssetEntry equivalent = new AuthoredAssetEntry(
                    nested.Key,
                    nested.InlineValue,
                    nested.Indent,
                    nested.LineNumber,
                    nested.EndLineNumber
                );
                Assert.That(equivalent, Is.EqualTo(nested));
                nested = equivalent;
            }
            Assert.That(
                AuthoredAssetYaml.TryDescend(document, nested, "x", out AuthoredAssetEntry child),
                Is.True
            );
            Assert.That(child.InlineValue, Is.EqualTo("3"));
        }

        [TestCase(0)]
        [TestCase(2)]
        public void DirectMappingChildrenContinueAfterComments(int commentIndent)
        {
            AuthoredAssetDocument document = Document(
                "  parent:",
                "    x: 1",
                new string(' ', commentIndent) + "# comment",
                "    y: 2",
                "  sibling: root"
            );
            Assert.That(document.TryGetEntry("parent", out AuthoredAssetEntry parent), Is.True);
            Assert.That(
                AuthoredAssetYaml.TryDescend(document, parent, "y", out AuthoredAssetEntry child),
                Is.True
            );
            Assert.That(child.InlineValue, Is.EqualTo("2"));
            Assert.That(
                AuthoredAssetYaml.ValueWithDirectChildren(document, "parent"),
                Is.EqualTo("1 2")
            );
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(4)]
        public void SequenceMappingChildrenContinueAfterComments(int commentIndent)
        {
            AuthoredAssetDocument document = Document(
                "  items:",
                "  - nested:",
                "      before: 1",
                new string(' ', commentIndent) + "# comment",
                "      after: 2",
                "    peer: 4"
            );
            Assert.That(document.TryGetEntry("nested", out AuthoredAssetEntry parent), Is.True);
            Assert.That(
                AuthoredAssetYaml.TryDescend(
                    document,
                    parent,
                    "after",
                    out AuthoredAssetEntry child
                ),
                Is.True
            );
            Assert.That(child.InlineValue, Is.EqualTo("2"));
        }

        [Test]
        public void SequenceEnumerationContinuesAfterColumnZeroComment()
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  items:",
                "  - value: first",
                "# comment",
                "  - value: second",
                "  sibling: root",
            };
            IReadOnlyList<AuthoredAssetDocument> documents = AuthoredAssetYaml.ReadDocuments(lines);
            Assert.That(documents[0].TryGetEntry("items", out AuthoredAssetEntry parent), Is.True);
            int count = 0;
            foreach (
                AuthoredSequenceElement element in AuthoredAssetYaml.EnumerateSequenceElements(
                    lines,
                    parent
                )
            )
            {
                ++count;
            }
            Assert.That(count, Is.EqualTo(2));
        }

        [Test]
        public void SerializedNamesPreserveExactSpellingBeforeLegacyForm()
        {
            CollectionAssert.AreEqual(
                new[] { "_value:", "value:" },
                AuthoredAssetYaml.SerializedNames("_value:")
            );
            CollectionAssert.AreEqual(
                new[] { "value" },
                AuthoredAssetYaml.SerializedNames("value")
            );
            CollectionAssert.AreEqual(new[] { "_" }, AuthoredAssetYaml.SerializedNames("_"));
            CollectionAssert.IsEmpty(AuthoredAssetYaml.SerializedNames(null));
            CollectionAssert.IsEmpty(AuthoredAssetYaml.SerializedNames(string.Empty));
        }
    }
}
