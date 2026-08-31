// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Validation;

    /// <summary>
    /// Pins the reader every authored-asset check begins with, against the text Unity writes.
    /// </summary>
    /// <remarks>
    /// The reader is the single place four checks would otherwise each parse a scene, so a defect
    /// here reports clean four times rather than once.
    /// </remarks>
    [TestFixture]
    public sealed class AuthoredAssetYamlTests
    {
        [Test]
        public void ADocumentCarriesItsClassAnchorAndScript()
        {
            IReadOnlyList<AuthoredAssetDocument> documents = AuthoredAssetYaml.ReadDocuments(
                MonoBehaviourAsset
            );

            Assert.AreEqual(1, documents.Count);
            AuthoredAssetDocument document = documents[0];
            Assert.AreEqual(AuthoredAssetYaml.MonoBehaviourTypeId, document.UnityTypeId);
            Assert.AreEqual(11400000L, document.FileId);
            Assert.AreEqual("MonoBehaviour", document.RootKey);
            Assert.IsFalse(document.IsStripped);
            Assert.AreEqual("5a04e8d1c9d3a4f2f8c5e9a7b6d4c3e1", document.ScriptGuid);
        }

        [Test]
        public void EveryDocumentInASceneIsReturnedInOrder()
        {
            IReadOnlyList<AuthoredAssetDocument> documents = AuthoredAssetYaml.ReadDocuments(
                SceneAsset
            );

            Assert.AreEqual(3, documents.Count);
            Assert.AreEqual(1, documents[0].UnityTypeId);
            Assert.AreEqual(114, documents[1].UnityTypeId);
            Assert.AreEqual(1001, documents[2].UnityTypeId);
            Assert.IsTrue(documents[2].IsStripped);
        }

        [Test]
        public void ANestedKeyIsReadAtItsOwnDepth()
        {
            AuthoredAssetDocument document = AuthoredAssetYaml.ReadDocuments(MonoBehaviourAsset)[0];

            Assert.IsTrue(document.TryGetEntry("_nested", out AuthoredAssetEntry nested));
            Assert.AreEqual(2, nested.Indent);
            Assert.IsTrue(document.TryGetEntry("_inner", out AuthoredAssetEntry inner));
            Assert.AreEqual(4, inner.Indent);
            Assert.AreEqual("7", inner.InlineValue);
        }

        [Test]
        public void ASequenceItemsKeysAreReadOneLevelIn()
        {
            AuthoredAssetDocument document = AuthoredAssetYaml.ReadDocuments(MonoBehaviourAsset)[0];

            List<AuthoredAssetEntry> names = new();
            foreach (AuthoredAssetEntry entry in document.Entries)
            {
                if (entry.Key == "_name")
                {
                    names.Add(entry);
                }
            }

            Assert.AreEqual(2, names.Count);
            Assert.AreEqual(4, names[0].Indent);
            Assert.AreEqual("first", names[0].InlineValue);
            Assert.AreEqual("second", names[1].InlineValue);
        }

        [Test]
        public void ASequenceKeySpansTheItemsBeneathIt()
        {
            AuthoredAssetDocument document = AuthoredAssetYaml.ReadDocuments(MonoBehaviourAsset)[0];

            Assert.IsTrue(document.TryGetEntry("_keys", out AuthoredAssetEntry keys));
            Assert.IsTrue(keys.HasBlockValue);
            Assert.AreEqual(keys.LineNumber + 3, keys.EndLineNumber);
        }

        [Test]
        public void ABlockScalarsContentIsNotReadAsKeys()
        {
            AuthoredAssetDocument document = AuthoredAssetYaml.ReadDocuments(MonoBehaviourAsset)[0];

            Assert.IsFalse(document.TryGetEntry("notAKey", out AuthoredAssetEntry _));
            Assert.IsTrue(document.TryGetEntry("_afterBlock", out AuthoredAssetEntry after));
            Assert.AreEqual("1", after.InlineValue);
        }

        [Test]
        public void TheShallowestEntryWinsWhenANestedTypeReusesAKey()
        {
            AuthoredAssetDocument document = AuthoredAssetYaml.ReadDocuments(MonoBehaviourAsset)[0];

            Assert.IsTrue(document.TryGetEntry("_shared", out AuthoredAssetEntry shared));
            Assert.AreEqual(2, shared.Indent);
            Assert.AreEqual("outer", shared.InlineValue);
        }

        [TestCase("{fileID: 0}", 0L, null)]
        [TestCase("{fileID: 11500000, guid: abc, type: 3}", 11500000L, "abc")]
        [TestCase("{fileID: -8964, guid: def, type: 2}", -8964L, "def")]
        public void AnInlineReferenceSplitsIntoItsFileIdAndGuid(
            string value,
            long expectedFileId,
            string expectedGuid
        )
        {
            Assert.IsTrue(
                AuthoredAssetYaml.TryParseObjectReference(value, out long fileId, out string guid)
            );
            Assert.AreEqual(expectedFileId, fileId);
            Assert.AreEqual(expectedGuid, guid);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("7")]
        [TestCase("[]")]
        [TestCase("some text")]
        public void AValueThatIsNotAnInlineMappingIsRefused(string value)
        {
            Assert.IsFalse(
                AuthoredAssetYaml.TryParseObjectReference(value, out long _, out string _)
            );
        }

        [TestCase("{fileID: 0}", true)]
        [TestCase("{fileID: 0, guid: 00000000000000000000000000000000, type: 2}", true)]
        [TestCase("{fileID: 11500000, guid: abc, type: 3}", false)]
        [TestCase("{fileID: 0, guid: abc, type: 3}", false)]
        [TestCase("[]", false)]
        [TestCase(null, false)]
        public void AnEmptySlotIsTheReferenceThatNamesNoObject(string value, bool expected)
        {
            Assert.AreEqual(expected, AuthoredAssetYaml.IsNullObjectReference(value));
        }

        [TestCase("[]", true)]
        [TestCase(" [] ", true)]
        [TestCase("[1]", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void AnEmptySequenceIsRecognizedWhereverItIsWritten(string value, bool expected)
        {
            Assert.AreEqual(expected, AuthoredAssetYaml.IsEmptySequence(value));
        }

        [Test]
        public void AFileWithNoUnityDocumentYieldsNothingRatherThanGuessing()
        {
            Assert.AreEqual(
                0,
                AuthoredAssetYaml.ReadDocuments(new[] { "not: yaml", "at: all" }).Count
            );
            Assert.AreEqual(0, AuthoredAssetYaml.ReadDocuments(null).Count);
        }

        [Test]
        public void TheExtensionIsRecheckedAfterTheGlob()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                $"authored-asset-yaml-{System.Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(root);
            try
            {
                string nested = Path.Combine(root, "nested");
                Directory.CreateDirectory(nested);
                File.WriteAllText(Path.Combine(root, "Level.unity"), "%YAML 1.1");
                File.WriteAllText(Path.Combine(root, "Level.unityproj"), "%YAML 1.1");
                File.WriteAllText(Path.Combine(nested, "Player.prefab"), "%YAML 1.1");
                File.WriteAllText(Path.Combine(nested, "Notes.txt"), "hello");

                IReadOnlyList<string> found = AuthoredAssetYaml.EnumerateAuthoredAssets(root);

                Assert.AreEqual(2, found.Count, string.Join(", ", found));
                Assert.IsTrue(found[0].EndsWith("Level.unity"), found[0]);
                Assert.IsTrue(found[1].EndsWith("Player.prefab"), found[1]);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void AMissingRootIsAnEmptyListRatherThanAThrow()
        {
            Assert.AreEqual(
                0,
                AuthoredAssetYaml
                    .EnumerateAuthoredAssets(
                        Path.Combine(Path.GetTempPath(), $"absent-{System.Guid.NewGuid():N}")
                    )
                    .Count
            );
            Assert.AreEqual(0, AuthoredAssetYaml.EnumerateAuthoredAssets(null).Count);
        }

        [Test]
        public void AnUnreadableAssetIsRefusedRatherThanReportedEmpty()
        {
            Assert.IsFalse(
                AuthoredAssetYaml.TryReadDocuments(
                    Path.Combine(Path.GetTempPath(), $"absent-{System.Guid.NewGuid():N}.asset"),
                    out IReadOnlyList<string> lines,
                    out IReadOnlyList<AuthoredAssetDocument> documents
                )
            );
            Assert.AreEqual(0, lines.Count);
            Assert.AreEqual(0, documents.Count);
        }

        private static readonly string[] MonoBehaviourAsset =
        {
            "%YAML 1.1",
            "%TAG !u! tag:unity3d.com,2011:",
            "--- !u!114 &11400000",
            "MonoBehaviour:",
            "  m_ObjectHideFlags: 0",
            "  m_Script: {fileID: 11500000, guid: 5a04e8d1c9d3a4f2f8c5e9a7b6d4c3e1, type: 3}",
            "  m_Name: Sample",
            "  _shared: outer",
            "  _keys:",
            "  - alpha",
            "  - beta",
            "  _values: []",
            "  _rows:",
            "  - _name: first",
            "    _clip: {fileID: 0}",
            "  - _name: second",
            "    _clip: {fileID: 21300000, guid: 1111, type: 3}",
            "  _nested:",
            "    _inner: 7",
            "    _shared: inner",
            "  _text: |",
            "    notAKey: still text",
            "  _afterBlock: 1",
        };

        private static readonly string[] SceneAsset =
        {
            "%YAML 1.1",
            "--- !u!1 &519420028",
            "GameObject:",
            "  m_Name: Root",
            "--- !u!114 &519420031",
            "MonoBehaviour:",
            "  m_Script: {fileID: 11500000, guid: aaaa, type: 3}",
            "--- !u!1001 &1234567890 stripped",
            "PrefabInstance:",
            "  m_SourcePrefab: {fileID: 100100000, guid: bbbb, type: 3}",
        };
    }
}
