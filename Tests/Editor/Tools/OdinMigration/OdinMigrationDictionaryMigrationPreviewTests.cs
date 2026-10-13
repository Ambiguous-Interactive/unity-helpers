// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    [TestFixture]
    [Category("Fast")]
    public sealed class OdinMigrationDictionaryMigrationPreviewTests : CommonTestBase
    {
        private string _folder;
        private string _scriptGuid;
        private string _reference;
        private Texture2D _texture;

        private static OdinMigrationDictionaryField[] Fields()
        {
            return new[]
            {
                OdinMigrationDictionaryField.Create<SpriteAlignment, Texture2D>(
                    nameof(OdinMigrationBindingAsset.Variations)
                ),
                OdinMigrationDictionaryField.Create<HideFlags, Texture2D>(
                    nameof(OdinMigrationBindingAsset.Other)
                ),
            };
        }

        private static string FullPath(string path)
        {
            return Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
        }

        [SetUp]
        public void CreateOwnedTargets()
        {
            _folder = "Assets/OdinPairedPreview-" + Guid.NewGuid().ToString("N");
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(
                    AssetDatabase.CreateFolder("Assets", Path.GetFileName(_folder))
                )
            );
            _texture = Track(new Texture2D(1, 1));
            AssetDatabase.CreateAsset(_texture, _folder + "/Texture.asset");
            OdinMigrationBindingAsset scriptOwner = Track(
                ScriptableObject.CreateInstance<OdinMigrationBindingAsset>()
            );
            MonoScript script = MonoScript.FromScriptableObject(scriptOwner);
            Assert.IsTrue(script != null);
            Assert.IsTrue(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out _scriptGuid, out long _)
            );
            Assert.IsTrue(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    _texture,
                    out string guid,
                    out long fileId
                )
            );
            _reference =
                "{fileID: "
                + fileId.ToString(CultureInfo.InvariantCulture)
                + ", guid: "
                + guid
                + ", type: 2}";
        }

        [TearDown]
        public void DeleteOwnedTargets()
        {
            Assert.IsTrue(AssetDatabase.DeleteAsset(_folder));
        }

        [TestCase("\n")]
        [TestCase("\r\n")]
        [TestCase("\r")]
        public void PairedPreviewPreservesUnrelatedTextAndLineEndingsWithoutWrites(string newline)
        {
            string unrelated =
                "--- !u!114 &456\nMonoBehaviour:\n  serializationData:\n    SerializedFormat: 0\n    SerializedBytes: opaque-unselected-data\n  Unrelated: 42\n";
            string original = (Document(123) + unrelated).Replace("\n", newline);
            string path = CreateAsset(original);
            byte[] before = File.ReadAllBytes(FullPath(path));
            Assert.IsTrue(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { Target(path, 123) },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(1, preview.Assets.Count);
            Assert.AreEqual(1, preview.Sources.Count);
            Assert.AreEqual(1, preview.Bindings.Count);
            Assert.IsTrue(preview.Assets.TryGetValue(path, out OdinMigrationAssetProposal asset));
            Assert.AreEqual(original, asset.OriginalSource);
            Assert.AreEqual(1, asset.ConvertedDocuments);
            StringAssert.Contains(unrelated.Replace("\n", newline), asset.ProposedSource);
            StringAssert.Contains(
                "  " + nameof(OdinMigrationBindingAsset.Variations) + ":" + newline,
                asset.ProposedSource
            );
            StringAssert.Contains(_reference, asset.ProposedSource);
            StringAssert.Contains("{fileID: 0}", asset.ProposedSource);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(FullPath(path)));
            foreach (KeyValuePair<string, OdinMigrationSourceProposal> source in preview.Sources)
            {
                StringAssert.Contains("SerializableDictionary<", source.Value.ProposedSource);
                StringAssert.DoesNotContain("SerializableDictionary<", source.Value.OriginalSource);
            }
        }

        [Test]
        public void MultipleDocumentsAndAssetsShareOneCoherentSourceProposal()
        {
            string path = CreateAsset(Document(123) + Document(789));
            string second = CreateAsset(Document(987));
            Assert.IsTrue(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { Target(path, 789), Target(second, 987), Target(path, 123) },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(2, preview.Assets.Count);
            Assert.IsTrue(
                preview.Assets.TryGetValue(path, out OdinMigrationAssetProposal firstProposal)
            );
            Assert.IsTrue(
                preview.Assets.TryGetValue(second, out OdinMigrationAssetProposal secondProposal)
            );
            Assert.AreEqual(2, firstProposal.ConvertedDocuments);
            Assert.AreEqual(1, secondProposal.ConvertedDocuments);
            Assert.AreEqual(1, preview.Sources.Count);
            StringAssert.DoesNotContain("serializationData:", firstProposal.ProposedSource);
            StringAssert.Contains("&123", firstProposal.ProposedSource);
            StringAssert.Contains("&789", firstProposal.ProposedSource);
            StringAssert.Contains(_scriptGuid, firstProposal.ProposedSource);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void InvalidTargetsReturnNoPartialPreview(int invalid)
        {
            string path = CreateAsset(Document(123));
            OdinMigrationDictionaryTarget target =
                invalid == 0 ? null
                : invalid == 1
                    ? new OdinMigrationDictionaryTarget(
                        null,
                        123,
                        Fields(),
                        new Object[] { _texture }
                    )
                : invalid == 2
                    ? new OdinMigrationDictionaryTarget(
                        "Assets/../Escape.asset",
                        123,
                        Fields(),
                        new Object[] { _texture }
                    )
                : invalid == 3
                    ? new OdinMigrationDictionaryTarget(
                        _folder + "/NotAnAsset.cs",
                        123,
                        Fields(),
                        new Object[] { _texture }
                    )
                : invalid == 4
                    ? new OdinMigrationDictionaryTarget(
                        _folder + "/Missing.asset",
                        123,
                        Fields(),
                        new Object[] { _texture }
                    )
                : invalid == 5 ? Target(path, 999)
                : invalid == 6
                    ? new OdinMigrationDictionaryTarget(path, 123, null, new Object[] { _texture })
                : invalid == 7
                    ? new OdinMigrationDictionaryTarget(path, 123, Fields(), new Object[] { null })
                : Target(path, 123);
            IReadOnlyList<OdinMigrationDictionaryTarget> targets =
                invalid == 8 ? new[] { target, target } : new[] { target };
            Assert.IsFalse(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    targets,
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                )
            );
            Assert.IsTrue(preview == null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
            Assert.AreEqual(Document(123), File.ReadAllText(FullPath(path)));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void MalformedOwnerStateReturnsNoPreview(int invalid)
        {
            string source = Document(123);
            if (invalid == 0)
            {
                source += "--- !u!1 &123\nGameObject:\n  Unrelated: 1\n";
            }
            if (invalid == 1)
            {
                source = source.Replace("SerializedFormat: 2", "SerializedFormat: 0");
            }
            if (invalid == 2)
            {
                source = source.Replace(
                    "  serializationData:",
                    "  "
                        + nameof(OdinMigrationBindingAsset.Variations)
                        + ": {}\n  serializationData:"
                );
            }
            if (invalid == 3)
            {
                source = source.Replace(_scriptGuid, "00000000000000000000000000000001");
            }
            if (invalid == 4)
            {
                source = source.Replace(
                    "  serializationData:",
                    "  Parent:\n    serializationData:"
                );
            }
            string path = CreateAsset(source);
            Assert.IsFalse(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { Target(path, 123) },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                )
            );
            Assert.IsTrue(preview == null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
            Assert.AreEqual(source, File.ReadAllText(FullPath(path)));
        }

        [Test]
        public void AFailureInALaterAssetDoesNotExposeTheEarlierProposal()
        {
            string first = CreateAsset(Document(123));
            string second = CreateAsset(
                Document(456).Replace("SerializedFormat: 2", "SerializedFormat: 0")
            );
            Assert.IsFalse(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { Target(first, 123), Target(second, 456) },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                )
            );
            Assert.IsTrue(preview == null);
            StringAssert.Contains("Nodes format", failure);
            Assert.AreEqual(Document(123), File.ReadAllText(FullPath(first)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CancellationOrCallbackFailureReturnsNoPreview(bool throws)
        {
            string path = CreateAsset(Document(123));
            Assert.IsFalse(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { Target(path, 123) },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure,
                    (asset, index, total) =>
                        throws ? throw new IOException("callback failure") : true
                )
            );
            Assert.IsTrue(preview == null);
            StringAssert.Contains(throws ? "callback failure" : "cancelled", failure);
            Assert.AreEqual(Document(123), File.ReadAllText(FullPath(path)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingTargetCollectionsReturnNoPreview(bool empty)
        {
            Assert.IsFalse(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    empty ? Array.Empty<OdinMigrationDictionaryTarget>() : null,
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                )
            );
            Assert.IsTrue(preview == null);
            StringAssert.Contains("required", failure);
        }

        [Test]
        public void TargetSnapshotsDoNotFollowCallerArrayEdits()
        {
            string path = CreateAsset(Document(123));
            OdinMigrationDictionaryField[] fields = Fields();
            Object[] objects = { _texture };
            OdinMigrationDictionaryTarget target = new OdinMigrationDictionaryTarget(
                path,
                123,
                fields,
                objects
            );
            fields[0] = null;
            objects[0] = null;
            Assert.IsTrue(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { target },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(1, preview.Assets.Count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EncodedOriginalFilesStayByteIdenticalAfterPreview(bool utf16)
        {
            string path = CreateAsset(Document(123));
            Encoding encoding = utf16
                ? new UnicodeEncoding(false, true, true)
                : new UTF8Encoding(true, true);
            File.WriteAllText(FullPath(path), Document(123), encoding);
            byte[] before = File.ReadAllBytes(FullPath(path));
            Assert.IsTrue(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { Target(path, 123) },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            Assert.IsTrue(
                preview.Assets.TryGetValue(path, out OdinMigrationAssetProposal proposal)
            );
            Assert.AreEqual(Document(123), proposal.OriginalSource);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(FullPath(path)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OversizedOrOverlongAssetsReturnNoPreview(bool lines)
        {
            string path = CreateAsset(Document(123));
            File.AppendAllText(
                FullPath(path),
                lines ? new string('\n', 65537) : new string(' ', 4 * 1024 * 1024 + 1)
            );
            Assert.IsFalse(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { Target(path, 123) },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                )
            );
            Assert.IsTrue(preview == null);
            StringAssert.Contains(lines ? "line limit" : "character limit", failure);
        }

        [Test]
        public void SourceEditsMergeAcrossDistinctSelectionsInOneFile()
        {
            Assert.IsTrue(
                OdinMigrationCompiledBindings.TryGet(
                    typeof(OdinMigrationBindingAsset),
                    out OdinMigrationCompiledBinding original
                )
            );
            OdinMigrationCompiledSource[] captured = new OdinMigrationCompiledSource[
                original.Sources.Count
            ];
            for (int index = 0; index < captured.Length; ++index)
            {
                captured[index] = original.Sources[index];
            }
            OdinMigrationCompiledField[] compiledFields = new OdinMigrationCompiledField[
                original.Fields.Count
            ];
            for (int index = 0; index < compiledFields.Length; ++index)
            {
                compiledFields[index] = original.Fields[index];
            }
            OdinMigrationCompiledBinding first = new OdinMigrationCompiledBinding(
                original.OriginalType,
                captured,
                compiledFields
            );
            OdinMigrationCompiledBinding second = new OdinMigrationCompiledBinding(
                original.OriginalType,
                captured,
                compiledFields
            );
            OdinMigrationDictionaryField[] fields = Fields();
            Dictionary<
                OdinMigrationCompiledBinding,
                IReadOnlyList<OdinMigrationDictionaryField>
            > selections = new Dictionary<
                OdinMigrationCompiledBinding,
                IReadOnlyList<OdinMigrationDictionaryField>
            >
            {
                { first, new[] { fields[0] } },
                { second, new[] { fields[1] } },
            };
            Assert.IsTrue(
                OdinMigrationDictionarySourceRewriter.TryMerge(
                    selections,
                    out Dictionary<string, OdinMigrationSourceProposal> proposals,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(1, proposals.Count);
            foreach (OdinMigrationSourceProposal proposal in proposals.Values)
            {
                StringAssert.Contains(
                    "SerializableDictionary<global::UnityEngine.SpriteAlignment",
                    proposal.ProposedSource
                );
                StringAssert.Contains(
                    "SerializableDictionary<global::UnityEngine.HideFlags",
                    proposal.ProposedSource
                );
            }
            captured[0] = new OdinMigrationCompiledSource(
                captured[0].Path,
                "different compilation"
            );
            OdinMigrationCompiledBinding conflicting = new OdinMigrationCompiledBinding(
                original.OriginalType,
                captured,
                compiledFields
            );
            selections.Remove(second);
            selections.Add(conflicting, new[] { fields[1] });
            Assert.IsFalse(
                OdinMigrationDictionarySourceRewriter.TryMerge(
                    selections,
                    out proposals,
                    out failure
                )
            );
            Assert.IsTrue(proposals == null);
            StringAssert.Contains("disagree", failure);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InventoryReportsUnselectedOriginalAndDerivedRetainedDocuments(bool derived)
        {
            string selectedPath = CreateAsset(Document(123));
            string missingSource = Document(789);
            if (derived)
            {
                OdinMigrationDerivedBindingAsset asset = Track(
                    ScriptableObject.CreateInstance<OdinMigrationDerivedBindingAsset>()
                );
                MonoScript script = MonoScript.FromScriptableObject(asset);
                Assert.IsTrue(
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        script,
                        out string guid,
                        out long _
                    )
                );
                missingSource = missingSource.Replace(_scriptGuid, guid);
            }
            string missingPath = CreateAsset(missingSource);
            Assert.IsTrue(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { Target(selectedPath, 123) },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            Assert.IsTrue(
                OdinMigrationDictionaryInventoryScanner.TryScan(
                    preview,
                    new[] { selectedPath, missingPath },
                    out OdinMigrationDictionaryInventory inventory,
                    out failure,
                    null
                ),
                failure
            );
            Assert.IsTrue(inventory.Complete);
            Assert.IsFalse(inventory.AllRetainedDocumentsSelected);
            Assert.AreEqual(1, inventory.MissingRetainedDocuments);
            Assert.AreEqual(2, inventory.Documents.Count);
            Assert.AreEqual(
                derived
                    ? typeof(OdinMigrationDerivedBindingAsset)
                    : typeof(OdinMigrationBindingAsset),
                inventory.Documents[1].OriginalType
            );
            Assert.IsFalse(inventory.Documents[1].Selected);
            Assert.AreEqual(missingSource, File.ReadAllText(FullPath(missingPath)));
        }

        [Test]
        public void InventoryAcceptsAllSelectedDocumentsAndDeduplicatesPaths()
        {
            string path = CreateAsset(Document(123) + Document(789));
            Assert.IsTrue(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { Target(path, 123), Target(path, 789) },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            Assert.IsTrue(
                OdinMigrationDictionaryInventoryScanner.TryScan(
                    preview,
                    new[] { path, path },
                    out OdinMigrationDictionaryInventory inventory,
                    out failure,
                    null
                ),
                failure
            );
            Assert.IsTrue(inventory.AllRetainedDocumentsSelected);
            Assert.AreEqual(1, inventory.ScannedFiles);
            Assert.AreEqual(2, inventory.Documents.Count);
            Assert.AreEqual(0, inventory.MissingRetainedDocuments);
        }

        [Test]
        public void InventorySkipsUnrelatedNativeAssetDataUsingOnlyTypeMetadata()
        {
            string path = CreateAsset(Document(123));
            Assert.IsTrue(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { Target(path, 123) },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            string texturePath = _folder + "/Texture.asset";
            Assert.AreEqual(typeof(Texture2D), AssetDatabase.GetMainAssetTypeAtPath(texturePath));
            Assert.IsTrue(
                OdinMigrationDictionaryInventoryScanner.TryScan(
                    preview,
                    new[] { path, texturePath },
                    out OdinMigrationDictionaryInventory inventory,
                    out failure,
                    null
                ),
                failure
            );
            Assert.IsTrue(inventory.AllRetainedDocumentsSelected);
            Assert.AreEqual(2, inventory.ScannedFiles);
            Assert.AreEqual(1, inventory.Documents.Count);
        }

        [Test]
        public void InventoryDoesNotLoadOrValidateUnloadedConsumerAssetInstances()
        {
            string path = CreateAsset(Document(123));
            Assert.IsTrue(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { Target(path, 123) },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            OdinMigrationDerivedBindingAsset derived = Track(
                ScriptableObject.CreateInstance<OdinMigrationDerivedBindingAsset>()
            );
            string derivedPath = _folder + "/Unloaded.asset";
            AssetDatabase.CreateAsset(derived, derivedPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(derived);
            AssetDatabase.ImportAsset(
                derivedPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
            );
            derived = Track(
                AssetDatabase.LoadAssetAtPath<OdinMigrationDerivedBindingAsset>(derivedPath)
            );
            Assert.IsTrue(derived != null);
            Assert.Greater(OdinMigrationDerivedBindingAsset.ValidationCalls, 0);
            Resources.UnloadAsset(derived);
            int before = OdinMigrationDerivedBindingAsset.ValidationCalls;
            Assert.IsTrue(
                OdinMigrationDictionaryInventoryScanner.TryScan(
                    preview,
                    new[] { path, derivedPath },
                    out OdinMigrationDictionaryInventory inventory,
                    out failure,
                    null
                ),
                failure
            );
            Assert.AreEqual(before, OdinMigrationDerivedBindingAsset.ValidationCalls);
            Assert.IsTrue(inventory.AllRetainedDocumentsSelected);
            Assert.AreEqual(2, inventory.Documents.Count);
            Assert.IsFalse(inventory.Documents[1].HasRetainedData);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void InventoryCannotCompleteWithChangedMissingOrUnreadableState(int invalid)
        {
            string path = CreateAsset(Document(123));
            Assert.IsTrue(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { Target(path, 123) },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            string[] paths = { path };
            if (invalid == 0)
            {
                File.AppendAllText(FullPath(path), "  Changed: 1\n");
            }
            else if (invalid == 1)
            {
                paths = Array.Empty<string>();
            }
            else if (invalid == 2)
            {
                paths = new[] { path, _folder + "/Missing.asset" };
            }
            else if (invalid == 3)
            {
                paths = new[]
                {
                    path,
                    CreateAsset(
                        Document(789).Replace(_scriptGuid, "00000000000000000000000000000001")
                    ),
                };
            }
            else
            {
                string source = Document(789)
                    .Replace(
                        "  m_Script: {fileID: 11500000, guid: " + _scriptGuid + ", type: 3}\n",
                        string.Empty
                    );
                paths = new[] { path, CreateAsset(source) };
            }
            Assert.IsFalse(
                OdinMigrationDictionaryInventoryScanner.TryScan(
                    preview,
                    paths,
                    out OdinMigrationDictionaryInventory inventory,
                    out failure,
                    null
                )
            );
            Assert.IsFalse(inventory.Complete);
            Assert.IsFalse(inventory.AllRetainedDocumentsSelected);
            Assert.Greater(inventory.Failures.Count, 0);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InventoryCancellationAndCallbackFailureAreIncomplete(bool throws)
        {
            string path = CreateAsset(Document(123));
            Assert.IsTrue(
                OdinMigrationTool.TryPreviewDictionaryMigration(
                    new[] { Target(path, 123) },
                    out OdinMigrationDictionaryMigrationPreview preview,
                    out string failure
                ),
                failure
            );
            Assert.IsFalse(
                OdinMigrationDictionaryInventoryScanner.TryScan(
                    preview,
                    new[] { path },
                    out OdinMigrationDictionaryInventory inventory,
                    out failure,
                    (asset, index, total) =>
                        throws ? throw new IOException("callback failure") : true
                )
            );
            Assert.IsFalse(inventory.Complete);
            Assert.IsFalse(inventory.AllRetainedDocumentsSelected);
            Assert.AreEqual(!throws, inventory.Cancelled);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
        }

        private string Document(long fileId)
        {
            return OdinMigrationDictionaryConverterTests
                .SourceWithTwoDictionaries(withNullObjects: true)
                .Replace("&123", "&" + fileId.ToString(CultureInfo.InvariantCulture))
                .Replace(
                    "MonoBehaviour:\n",
                    "MonoBehaviour:\n  m_Script: {fileID: 11500000, guid: "
                        + _scriptGuid
                        + ", type: 3}\n  Preserve: 17\n"
                )
                .Replace("UnityEngine.Sprite,", "UnityEngine.Texture2D,")
                .Replace(
                    "{fileID: 21300000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}",
                    _reference
                );
        }

        private string CreateAsset(string source)
        {
            string path = _folder + "/Retained-" + Guid.NewGuid().ToString("N") + ".asset";
            OdinMigrationBindingAsset asset = Track(
                ScriptableObject.CreateInstance<OdinMigrationBindingAsset>()
            );
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            File.WriteAllText(FullPath(path), source, new UTF8Encoding(false));
            return path;
        }

        private OdinMigrationDictionaryTarget Target(string path, long fileId)
        {
            return new OdinMigrationDictionaryTarget(
                path,
                fileId,
                Fields(),
                new Object[] { _texture }
            );
        }
    }
}
