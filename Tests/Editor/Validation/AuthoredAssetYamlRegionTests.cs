// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Validation;

    [TestFixture]
    public sealed class AuthoredAssetYamlRegionTests
    {
        private static AuthoredAssetRegion Whole(IReadOnlyList<string> lines)
        {
            IReadOnlyList<AuthoredAssetDocument> documents = AuthoredAssetYaml.ReadDocuments(lines);
            Assert.AreEqual(1, documents.Count);
            return AuthoredAssetYaml.Whole(lines, documents[0]);
        }

        [TestCase("_settings")]
        [TestCase("settings")]
        public void RegionsKeepMappingsAndSequenceItemsSeparate(string spelling)
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  " + spelling + ":",
                "    name: root",
                "    values:",
                "    - name: first",
                "      amount: 1",
                "      nested:",
                "        name: inside",
                "    -",
                "      name: second",
                "      amount: 2",
                "  other:",
                "    name: outside",
            };
            AuthoredAssetRegion whole = Whole(lines);
            Assert.IsTrue(whole.IsValid);
            Assert.IsFalse(AuthoredAssetYaml.TryGetField(whole, "name", out string missing));
            Assert.IsEmpty(missing);
            Assert.IsTrue(
                AuthoredAssetYaml.TryDescend(
                    whole,
                    new[] { "_settings" },
                    out AuthoredAssetRegion settings
                )
            );
            Assert.IsTrue(AuthoredAssetYaml.TryGetField(settings, "name:", out string root));
            Assert.AreEqual("root", root);
            Assert.IsFalse(AuthoredAssetYaml.TryGetField(settings, "amount", out _));
            IReadOnlyList<AuthoredAssetRegion> items = AuthoredAssetYaml.Items(
                settings,
                new[] { "_values" }
            );
            Assert.AreEqual(2, items.Count);
            Assert.AreEqual(6, items[0].StartLineNumber);
            Assert.AreEqual(10, items[0].EndLineNumber);
            Assert.AreEqual(10, items[1].StartLineNumber);
            Assert.AreEqual(13, items[1].EndLineNumber);
            Assert.IsTrue(AuthoredAssetYaml.TryGetField(items[0], "name", out string first));
            Assert.AreEqual("first", first);
            Assert.IsTrue(AuthoredAssetYaml.TryGetField(items[1], "name", out string second));
            Assert.AreEqual("second", second);
            Assert.IsTrue(
                AuthoredAssetYaml.TryDescend(
                    items[0],
                    new[] { "nested" },
                    out AuthoredAssetRegion nested
                )
            );
            Assert.IsTrue(AuthoredAssetYaml.TryGetField(nested, "name", out string inner));
            Assert.AreEqual("inside", inner);
            Assert.IsFalse(AuthoredAssetYaml.TryGetField(nested, "amount", out _));
            Assert.IsFalse(AuthoredAssetYaml.TryDescend(settings, new[] { "values" }, out _));
            Assert.AreEqual(2, AuthoredAssetYaml.CountBlockItems(settings, "values"));
            Assert.AreEqual(0, AuthoredAssetYaml.CountBlockItems(items[0], "values"));
        }

        [Test]
        public void RegionReadsRefuseDuplicatesInsteadOfFallingBackToAnotherName()
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  _settings:",
                "    name: one",
                "  _settings:",
                "    name: two",
                "  settings:",
                "    name: legacy",
            };
            AuthoredAssetRegion whole = Whole(lines);
            Assert.IsFalse(AuthoredAssetYaml.TryGetField(whole, "_settings", out string value));
            Assert.IsEmpty(value);
            Assert.IsFalse(
                AuthoredAssetYaml.TryDescend(
                    whole,
                    new[] { "_settings" },
                    out AuthoredAssetRegion nested
                )
            );
            Assert.IsFalse(nested.IsValid);
            Assert.IsEmpty(AuthoredAssetYaml.Items(whole, new[] { "_settings" }));
            Assert.IsEmpty(AuthoredAssetYaml.ValueWithDirectChildren(whole, "_settings"));
            Assert.AreEqual(0, AuthoredAssetYaml.CountBlockItems(whole, "_settings"));
        }

        [TestCase("hello")]
        [TestCase("{}")]
        [TestCase("[]")]
        [TestCase("|")]
        [TestCase(">-")]
        public void ScalarsAndLiteralTextDoNotBecomeNestedMappings(string value)
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  value: " + value,
                "    name: pretend",
            };
            AuthoredAssetRegion whole = Whole(lines);
            Assert.IsFalse(
                AuthoredAssetYaml.TryDescend(
                    whole,
                    new[] { "value" },
                    out AuthoredAssetRegion nested
                )
            );
            Assert.IsFalse(nested.IsValid);
            Assert.IsEmpty(AuthoredAssetYaml.Items(whole, new[] { "value" }));
        }

        [Test]
        public void RegionCountsIncludeNestedSequencesWithoutCrossingSiblings()
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  root:",
                "    values:",
                "    - name: first",
                "      children:",
                "      - one",
                "      - two",
                "    - name: second",
                "      children: []",
                "  outside:",
                "  - ignored",
            };
            AuthoredAssetRegion whole = Whole(lines);
            Assert.IsTrue(
                AuthoredAssetYaml.TryDescend(whole, new[] { "root" }, out AuthoredAssetRegion root)
            );
            Assert.AreEqual(4, AuthoredAssetYaml.CountBlockItems(root, "values"));
            Assert.AreEqual(
                2,
                AuthoredAssetYaml.CountBlockItems(root, "values", includeNested: false)
            );
            IReadOnlyList<AuthoredAssetRegion> items = AuthoredAssetYaml.Items(
                root,
                new[] { "values" }
            );
            Assert.AreEqual(2, items.Count);
            Assert.AreEqual(2, AuthoredAssetYaml.CountBlockItems(items[0], "children"));
            Assert.AreEqual(0, AuthoredAssetYaml.CountBlockItems(items[1], "children"));
        }

        [Test]
        public void FlatteningIncludesOnlyDirectMappingValues()
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  root:",
                "    offset:",
                "      serializedVersion: 3",
                "      x: 1",
                "      y: 2",
                "      nested:",
                "        z: 999",
                "    outside: ignored",
            };
            AuthoredAssetRegion whole = Whole(lines);
            Assert.IsTrue(
                AuthoredAssetYaml.TryDescend(whole, new[] { "root" }, out AuthoredAssetRegion root)
            );
            Assert.AreEqual("1 2", AuthoredAssetYaml.ValueWithDirectChildren(root, "offset"));
            Assert.AreEqual("ignored", AuthoredAssetYaml.ValueWithDirectChildren(root, "outside"));
        }

        [Test]
        public void DefaultAndInvalidRegionsRefuseEveryRead()
        {
            AuthoredAssetRegion empty = default;
            Assert.IsFalse(empty.IsValid);
            Assert.IsFalse(AuthoredAssetYaml.TryGetField(empty, "name", out string value));
            Assert.IsEmpty(value);
            Assert.IsFalse(
                AuthoredAssetYaml.TryDescend(empty, null, out AuthoredAssetRegion nested)
            );
            Assert.IsFalse(nested.IsValid);
            Assert.IsEmpty(AuthoredAssetYaml.Items(empty, null));
            Assert.AreEqual(0, AuthoredAssetYaml.CountBlockItems(empty, "name"));
            Assert.IsEmpty(AuthoredAssetYaml.ValueWithDirectChildren(empty, "name"));
            string[] lines = { "--- !u!114 &7", "MonoBehaviour:", "  name: valid" };
            AuthoredAssetDocument document = AuthoredAssetYaml.ReadDocuments(lines)[0];
            Assert.IsFalse(AuthoredAssetYaml.Whole(null, document).IsValid);
            Assert.IsFalse(AuthoredAssetYaml.Whole(lines, null).IsValid);
            Assert.IsFalse(AuthoredAssetYaml.Whole(Array.Empty<string>(), document).IsValid);
            string[] invalid = (string[])lines.Clone();
            invalid[0] = "not a document";
            Assert.IsFalse(AuthoredAssetYaml.Whole(invalid, document).IsValid);
        }

        [Test]
        public void WholeRegionsRetainAbsoluteDocumentBoundsAndSourceIdentity()
        {
            string[] lines =
            {
                "--- !u!114 &7",
                "MonoBehaviour:",
                "  name: first",
                "--- !u!114 &8",
                "MonoBehaviour:",
                "  name: second",
            };
            IReadOnlyList<AuthoredAssetDocument> documents = AuthoredAssetYaml.ReadDocuments(lines);
            AuthoredAssetRegion first = AuthoredAssetYaml.Whole(lines, documents[0]);
            AuthoredAssetRegion second = AuthoredAssetYaml.Whole(lines, documents[1]);
            Assert.IsTrue(AuthoredAssetYaml.TryGetField(first, "name", out string one));
            Assert.IsTrue(AuthoredAssetYaml.TryGetField(second, "name", out string two));
            Assert.AreEqual("first", one);
            Assert.AreEqual("second", two);
            AuthoredAssetRegion copy = AuthoredAssetYaml.Whole(lines, documents[0]);
            Assert.IsTrue(first.Equals(copy));
            Assert.AreEqual(first.GetHashCode(), copy.GetHashCode());
            Assert.IsFalse(first.Equals(second));
            Assert.IsFalse(first.Equals((object)null));
            Assert.IsTrue(first.Equals((object)copy));
            Assert.IsFalse(
                first.Equals(AuthoredAssetYaml.Whole((string[])lines.Clone(), documents[0]))
            );
            Assert.IsTrue(default(AuthoredAssetRegion).Equals(default(AuthoredAssetRegion)));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(1000)]
        public void LargeAndEmptySequencesKeepItemBounds(int count)
        {
            List<string> lines = new() { "--- !u!114 &7", "MonoBehaviour:", "  values:" };
            for (int index = 0; index < count; ++index)
            {
                lines.Add("  - name: item" + index);
            }
            lines.Add("  outside: done");
            AuthoredAssetRegion whole = Whole(lines);
            IReadOnlyList<AuthoredAssetRegion> items = AuthoredAssetYaml.Items(
                whole,
                new[] { "values" }
            );
            Assert.AreEqual(count, items.Count);
            if (0 < count)
            {
                Assert.IsTrue(AuthoredAssetYaml.TryGetField(items[0], "name", out string first));
                Assert.AreEqual("item0", first);
                Assert.IsTrue(
                    AuthoredAssetYaml.TryGetField(items[count - 1], "name", out string last)
                );
                Assert.AreEqual("item" + (count - 1), last);
            }
            Assert.AreEqual(count, AuthoredAssetYaml.CountBlockItems(whole, "values"));
        }
    }
}
