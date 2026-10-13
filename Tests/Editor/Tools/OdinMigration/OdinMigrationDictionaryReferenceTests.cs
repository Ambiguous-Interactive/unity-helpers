// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    [TestFixture]
    [Category("Fast")]
    public sealed class OdinMigrationDictionaryReferenceTests : CommonTestBase
    {
        private const string RecordedReference =
            "{fileID: 21300000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}";
        private string _folder;
        private string _ownerPath;
        private string _texturePath;
        private string _guid;
        private long _fileId;
        private Texture2D _texture;
        private Texture2D _other;

        private static OdinMigrationDictionaryField[] Fields(bool sprites = false)
        {
            return sprites
                ? new[]
                {
                    OdinMigrationDictionaryField.Create<SpriteAlignment, Sprite>(
                        nameof(OdinMigrationDictionaryAsset.Variations)
                    ),
                    OdinMigrationDictionaryField.Create<HideFlags, Sprite>(
                        nameof(OdinMigrationDictionaryAsset.Other)
                    ),
                }
                : new[]
                {
                    OdinMigrationDictionaryField.Create<SpriteAlignment, Texture2D>(
                        nameof(OdinMigrationDictionaryAsset.Variations)
                    ),
                    OdinMigrationDictionaryField.Create<HideFlags, Texture2D>(
                        nameof(OdinMigrationDictionaryAsset.Other)
                    ),
                };
        }

        private static OdinMigrationSerializedData Payload(string reference, bool sprites = false)
        {
            string source = OdinMigrationDictionaryConverterTests
                .SourceWithTwoDictionaries(withNullObjects: true)
                .Replace(RecordedReference, reference);
            if (!sprites)
            {
                source = source.Replace("UnityEngine.Sprite,", "UnityEngine.Texture2D,");
            }
            Assert.IsTrue(
                OdinMigrationTool.TryReadSerializedNodes(
                    source,
                    out IReadOnlyList<OdinMigrationSerializedData> payloads,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(1, payloads.Count);
            return payloads[0];
        }

        [SetUp]
        public void CreateOwnedReferenceAssets()
        {
            _folder = "Assets/OdinMigrationReferences-" + Guid.NewGuid().ToString("N");
            Assert.IsTrue(
                !string.IsNullOrWhiteSpace(
                    AssetDatabase.CreateFolder("Assets", Path.GetFileName(_folder))
                )
            );
            _texture = Track(new Texture2D(1, 1));
            _other = Track(new Texture2D(1, 1));
            _texturePath = _folder + "/Original.asset";
            AssetDatabase.CreateAsset(_texture, _texturePath);
            AssetDatabase.CreateAsset(_other, _folder + "/Other.asset");
            OdinMigrationDictionaryAsset owner = Track(
                ScriptableObject.CreateInstance<OdinMigrationDictionaryAsset>()
            );
            _ownerPath = _folder + "/Owner.asset";
            AssetDatabase.CreateAsset(owner, _ownerPath);
            AssetDatabase.SaveAssets();
            Assert.IsTrue(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_texture, out _guid, out _fileId)
            );
        }

        [TearDown]
        public void RemoveOwnedReferenceAssets()
        {
            if (!string.IsNullOrWhiteSpace(_folder))
            {
                Assert.IsTrue(AssetDatabase.DeleteAsset(_folder));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ValidExternalAndLocalReferencesPreserveNullsAndRepeatedIdentity(bool local)
        {
            OdinMigrationSerializedData payload = Payload(Reference(local));
            OdinMigrationDictionaryField[] fields = Fields();
            Assert.IsTrue(
                OdinMigrationTool.TryConvertValidatedSerializedDictionaries(
                    payload,
                    fields,
                    local ? _texturePath : _ownerPath,
                    new Object[] { _texture },
                    out string validated,
                    out string failure
                ),
                failure
            );
            Assert.IsTrue(
                OdinMigrationTool.TryConvertSerializedDictionaries(
                    payload,
                    fields,
                    out string original,
                    out failure
                ),
                failure
            );
            Assert.AreEqual(original, validated);
            StringAssert.Contains("{fileID: 0}", validated);
            StringAssert.Contains(Reference(local), validated);
        }

        [Test]
        public void DerivedObjectsFitTheDeclaredBaseElementType()
        {
            string source = OdinMigrationDictionaryConverterTests
                .SourceWithTwoDictionaries()
                .Replace(RecordedReference, Reference())
                .Replace("UnityEngine.Sprite,", "UnityEngine.Texture,");
            Assert.IsTrue(
                OdinMigrationTool.TryReadSerializedNodes(
                    source,
                    out IReadOnlyList<OdinMigrationSerializedData> payloads,
                    out string failure
                ),
                failure
            );
            OdinMigrationDictionaryField[] fields =
            {
                OdinMigrationDictionaryField.Create<SpriteAlignment, Texture>(
                    nameof(OdinMigrationDictionaryAsset.Variations)
                ),
                OdinMigrationDictionaryField.Create<HideFlags, Texture>(
                    nameof(OdinMigrationDictionaryAsset.Other)
                ),
            };
            Assert.IsTrue(
                OdinMigrationTool.TryConvertValidatedSerializedDictionaries(
                    payloads[0],
                    fields,
                    _ownerPath,
                    new Object[] { _texture },
                    out string yaml,
                    out failure
                ),
                failure
            );
            StringAssert.Contains(Reference(), yaml);
        }

        [Test]
        public void SubassetWithTheSameGuidAndADifferentFileIdReturnsNoFragment()
        {
            Texture2D subasset = Track(new Texture2D(1, 1));
            AssetDatabase.AddObjectToAsset(subasset, _texturePath);
            AssetDatabase.SaveAssets();
            Assert.IsTrue(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    subasset,
                    out string guid,
                    out long fileId
                )
            );
            Assert.AreEqual(_guid, guid);
            Assert.AreNotEqual(_fileId, fileId);
            AssertRejected(
                Payload(Reference()),
                Fields(),
                _ownerPath,
                new Object[] { subasset },
                "identifier"
            );
        }

        [Test]
        public void LaterFieldTypeFailureDoesNotReturnTheEarlierFragment()
        {
            string source = OdinMigrationDictionaryConverterTests
                .SourceWithTwoDictionaries()
                .Replace(RecordedReference, Reference())
                .Replace("UnityEngine.Sprite,", "UnityEngine.Texture2D,");
            int second = source.IndexOf(
                "    - Name: " + nameof(OdinMigrationDictionaryAsset.Other),
                StringComparison.Ordinal
            );
            Assert.GreaterOrEqual(second, 0);
            source =
                source.Substring(0, second)
                + source.Substring(second).Replace("UnityEngine.Texture2D,", "UnityEngine.Sprite,");
            Assert.IsTrue(
                OdinMigrationTool.TryReadSerializedNodes(
                    source,
                    out IReadOnlyList<OdinMigrationSerializedData> payloads,
                    out string failure
                ),
                failure
            );
            OdinMigrationDictionaryField[] fields =
            {
                OdinMigrationDictionaryField.Create<SpriteAlignment, Texture2D>(
                    nameof(OdinMigrationDictionaryAsset.Variations)
                ),
                OdinMigrationDictionaryField.Create<HideFlags, Sprite>(
                    nameof(OdinMigrationDictionaryAsset.Other)
                ),
            };
            AssertRejected(
                payloads[0],
                fields,
                _ownerPath,
                new Object[] { _texture },
                "element type"
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void InvalidInputsReturnNoFragment(int invalidInput)
        {
            AssertRejected(
                invalidInput == 0 ? null : Payload(Reference()),
                invalidInput == 1 ? null : Fields(),
                invalidInput == 3 ? "Assets/../Escape.asset" : _ownerPath,
                invalidInput == 2 ? null : new Object[] { _texture },
                invalidInput == 3 ? "traversal" : "required"
            );
        }

        [TestCase(false, false, false, 0)]
        [TestCase(false, true, false, 0)]
        [TestCase(true, false, false, 0)]
        [TestCase(true, true, false, 0)]
        [TestCase(false, true, true, 0)]
        [TestCase(true, true, true, 0)]
        [TestCase(true, true, false, 1)]
        [TestCase(true, true, false, 2)]
        [TestCase(true, true, false, 3)]
        [TestCase(false, true, false, 4)]
        [TestCase(false, true, false, 5)]
        [TestCase(false, true, false, 6)]
        [TestCase(false, true, false, 7)]
        public void SavedSceneObjectReferencesMatchTheirAuthoredFileIds(
            bool local,
            bool prefab,
            bool wrongInstance,
            int invalidScene
        )
        {
            Scene previous = SceneManager.GetActiveScene();
            string scenePath = _folder + "/References.unity";
            string sourcePath = Path.Combine(
                Application.dataPath,
                scenePath.Substring("Assets/".Length)
            );
            string template = File.ReadAllText(
                DirectoryHelper.FindAbsolutePathToDirectory("Tests/Runtime/Scenes/Test1.unity")
            );
            Match firstObject = Regex.Match(template, @"^--- !u!1 &", RegexOptions.Multiline);
            Assert.IsTrue(firstObject.Success);
            File.WriteAllText(sourcePath, template.Substring(0, firstObject.Index));
            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport);
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                GameObject value = Track(
                    new GameObject(nameof(SavedSceneObjectReferencesMatchTheirAuthoredFileIds))
                );
                SceneManager.MoveGameObjectToScene(value, scene);
                Assert.IsTrue(SceneManager.SetActiveScene(scene));
                GameObject supplied = value;
                string sourcePrefabReference = null;
                if (prefab)
                {
                    string prefabPath = _folder + "/Reference.prefab";
                    GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(value, prefabPath);
                    Assert.IsTrue(prefabAsset != null);
                    Assert.IsTrue(EditorUtility.IsPersistent(prefabAsset));
                    Assert.IsTrue(
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                            prefabAsset,
                            out string prefabGuid,
                            out long prefabFileId
                        )
                    );
                    if (0 < prefabFileId)
                    {
                        long negativeFileId = -prefabFileId;
                        string prefabSourcePath = Path.Combine(
                            Application.dataPath,
                            prefabPath.Substring("Assets/".Length)
                        );
                        string positive = prefabFileId.ToString(CultureInfo.InvariantCulture);
                        string negative = negativeFileId.ToString(CultureInfo.InvariantCulture);
                        string prefabSource = File.ReadAllText(prefabSourcePath);
                        prefabSource = Regex.Replace(
                            prefabSource,
                            @"(?m)(^--- !u!1 &)" + positive + @"(?=\r?$)",
                            "$1" + negative
                        );
                        prefabSource = Regex.Replace(
                            prefabSource,
                            @"(fileID: )" + positive + @"(?=[},])",
                            "$1" + negative
                        );
                        File.WriteAllText(prefabSourcePath, prefabSource);
                        AssetDatabase.ImportAsset(
                            prefabPath,
                            ImportAssetOptions.ForceSynchronousImport
                                | ImportAssetOptions.ForceUpdate
                        );
                        prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                        Assert.IsTrue(prefabAsset != null);
                        Assert.IsTrue(
                            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                                prefabAsset,
                                out prefabGuid,
                                out prefabFileId
                            )
                        );
                    }
                    Assert.Less(
                        prefabFileId,
                        0,
                        "The fixture must exercise a negative source prefab file ID."
                    );
                    sourcePrefabReference =
                        "{fileID: "
                        + prefabFileId.ToString(CultureInfo.InvariantCulture)
                        + ", guid: "
                        + prefabGuid
                        + ", type: 3}";
                    value = Track((GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, scene));
                    supplied = wrongInstance
                        ? Track((GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, scene))
                        : value;
                }
                GameObject holder = Track(new GameObject(nameof(OdinMigrationReferenceComponent)));
                SceneManager.MoveGameObjectToScene(holder, scene);
                OdinMigrationReferenceComponent component = Track(
                    holder.AddComponent<OdinMigrationReferenceComponent>()
                );
                component.Reference = value;
                Assert.IsTrue(EditorSceneManager.SaveScene(scene, scenePath));
                if (prefab)
                {
                    string sceneSource = File.ReadAllText(sourcePath);
                    sceneSource = Regex.Replace(
                        sceneSource,
                        @"(?m)^(  m_CorrespondingSourceObject: \{fileID: -?\d+, guid: [a-fA-F0-9]{32}), type: (\d+)\}(\r?)$",
                        "$1,\n    type: $2}$3"
                    );
                    File.WriteAllText(sourcePath, sceneSource);
                    Assert.IsTrue(
                        Regex.IsMatch(
                            sceneSource,
                            @"m_CorrespondingSourceObject: \{fileID: -?\d+, guid: [a-fA-F0-9]{32},\r?\n    type: 3\}"
                        ),
                        "The scene fixture must exercise a wrapped source-object flow mapping."
                    );
                }
                Match header = Regex.Match(
                    File.ReadAllText(sourcePath),
                    @"^  "
                        + nameof(OdinMigrationReferenceComponent.Reference)
                        + @": \{fileID: (-?\d+)\}\r?$",
                    RegexOptions.Multiline
                );
                Assert.IsTrue(header.Success);
                long fileId = long.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
                if (invalidScene == 1)
                {
                    File.AppendAllText(sourcePath, new string(' ', 4 * 1024 * 1024 + 1));
                }
                else if (invalidScene == 2)
                {
                    File.AppendAllText(sourcePath, new string('\n', 65537));
                }
                else if (invalidScene == 3)
                {
                    File.AppendAllText(
                        sourcePath,
                        "\n--- !u!1 &"
                            + fileId.ToString(CultureInfo.InvariantCulture)
                            + " stripped\nGameObject:\n"
                    );
                }
                else if (invalidScene == 5)
                {
                    string altered = Regex.Replace(
                        File.ReadAllText(sourcePath),
                        @"(m_CorrespondingSourceObject: \{fileID: -?\d+, guid: )[a-fA-F0-9]{32}",
                        "${1}00000000000000000000000000000001"
                    );
                    File.WriteAllText(sourcePath, altered);
                }
                else if (invalidScene == 6)
                {
                    string altered = Regex.Replace(
                        File.ReadAllText(sourcePath),
                        @"(m_CorrespondingSourceObject: \{fileID: -?\d+, guid: [a-fA-F0-9]{32},\r?\n    type: 3)\}",
                        "$1"
                    );
                    File.WriteAllText(sourcePath, altered);
                }
                else if (invalidScene == 7)
                {
                    string altered = Regex.Replace(
                        File.ReadAllText(sourcePath),
                        @"(m_CorrespondingSourceObject: \{fileID: -?\d+, guid: [a-fA-F0-9]{32},\r?\n)(    type: 3\})",
                        "$1\n$2"
                    );
                    File.WriteAllText(sourcePath, altered);
                }
                string reference =
                    "{fileID: "
                    + fileId.ToString(CultureInfo.InvariantCulture)
                    + (
                        local
                            ? "}"
                            : ", guid: " + AssetDatabase.AssetPathToGUID(scenePath) + ", type: 3}"
                    );
                Assert.IsFalse(EditorUtility.IsPersistent(supplied));
                if (invalidScene == 4)
                {
                    reference = sourcePrefabReference;
                }
                string source = OdinMigrationDictionaryConverterTests
                    .SourceWithTwoDictionaries()
                    .Replace(RecordedReference, reference)
                    .Replace("UnityEngine.Sprite,", "UnityEngine.GameObject,");
                Assert.IsTrue(
                    OdinMigrationTool.TryReadSerializedNodes(
                        source,
                        out IReadOnlyList<OdinMigrationSerializedData> payloads,
                        out string failure
                    ),
                    failure
                );
                OdinMigrationDictionaryField[] fields =
                {
                    OdinMigrationDictionaryField.Create<SpriteAlignment, GameObject>(
                        nameof(OdinMigrationDictionaryAsset.Variations)
                    ),
                    OdinMigrationDictionaryField.Create<HideFlags, GameObject>(
                        nameof(OdinMigrationDictionaryAsset.Other)
                    ),
                };
                bool converted = OdinMigrationTool.TryConvertValidatedSerializedDictionaries(
                    payloads[0],
                    fields,
                    scenePath,
                    new Object[] { supplied },
                    out string yaml,
                    out failure
                );
                if (wrongInstance || invalidScene != 0)
                {
                    Assert.IsFalse(converted);
                    Assert.IsTrue(yaml == null);
                    string expected =
                        invalidScene == 1 ? "size limit"
                        : invalidScene == 2 ? "line limit"
                        : invalidScene == 3 ? "more than once"
                        : invalidScene == 7 ? "malformed"
                        : "identifier";
                    StringAssert.Contains(expected, failure);
                }
                else
                {
                    Assert.IsTrue(
                        converted,
                        failure
                            + "\nLive ID: "
                            + GlobalObjectId.GetGlobalObjectIdSlow(supplied)
                            + "\nRetained reference: "
                            + reference
                            + "\nScene source:\n"
                            + File.ReadAllText(sourcePath)
                    );
                    StringAssert.Contains(reference, yaml);
                }
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
                Assert.IsTrue(EditorSceneManager.CloseScene(scene, true));
            }
        }

        [Test]
        public void SameTypeWithADifferentGuidReturnsNoFragment()
        {
            AssertRejected(
                Payload(Reference()),
                Fields(),
                _ownerPath,
                new Object[] { _other },
                "GUID"
            );
        }

        [Test]
        public void DifferentLocalFileIdentifierReturnsNoFragment()
        {
            AssertRejected(
                Payload(Reference(fileIdOffset: 1)),
                Fields(),
                _ownerPath,
                new Object[] { _texture },
                "identifier"
            );
        }

        [Test]
        public void WrongOwningAssetForALocalReferenceReturnsNoFragment()
        {
            AssertRejected(
                Payload(Reference(local: true)),
                Fields(),
                _ownerPath,
                new Object[] { _texture },
                "GUID"
            );
        }

        [Test]
        public void WrongLiveElementTypeReturnsNoFragment()
        {
            AssertRejected(
                Payload(Reference(), sprites: true),
                Fields(sprites: true),
                _ownerPath,
                new Object[] { _texture },
                "element type"
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrDestroyedObjectReturnsNoFragment(bool destroyed)
        {
            Texture2D reference = null;
            if (destroyed)
            {
                reference = Track(new Texture2D(1, 1));
                Object.DestroyImmediate(reference); // UNH-SUPPRESS: verifies destroyed-object null handling.
            }
            AssertRejected(
                Payload(Reference()),
                Fields(),
                _ownerPath,
                new Object[] { reference },
                "missing or destroyed"
            );
        }

        [Test]
        public void TransientObjectWithoutPersistentIdentityReturnsNoFragment()
        {
            Texture2D transient = Track(new Texture2D(1, 1));
            AssertRejected(
                Payload(Reference()),
                Fields(),
                _ownerPath,
                new Object[] { transient },
                "identifier"
            );
        }

        [TestCase(0)]
        [TestCase(2)]
        public void MissingOrExtraReferenceTableEntriesReturnNoFragment(int count)
        {
            Object[] objects =
                count == 0 ? Array.Empty<Object>() : new Object[] { _texture, _other };
            AssertRejected(Payload(Reference()), Fields(), _ownerPath, objects, "reference table");
        }

        [Test]
        public void RecordedNullReferenceTableEntriesRemainNull()
        {
            Assert.IsTrue(
                OdinMigrationTool.TryConvertValidatedSerializedDictionaries(
                    Payload("{fileID: 0}"),
                    Fields(),
                    _ownerPath,
                    new Object[] { null },
                    out string yaml,
                    out string failure
                ),
                failure
            );
            StringAssert.Contains("{fileID: 0}", yaml);
        }

        [Test]
        public void RecordedNullCannotNameALiveObject()
        {
            AssertRejected(
                Payload("{fileID: 0}"),
                Fields(),
                _ownerPath,
                new Object[] { _texture },
                "recorded null"
            );
        }

        [Test]
        public void MissingOwningAssetReturnsNoFragment()
        {
            AssertRejected(
                Payload(Reference()),
                Fields(),
                _folder + "/Missing.asset",
                new Object[] { _texture },
                "owning asset"
            );
        }

        private string Reference(bool local = false, long fileIdOffset = 0)
        {
            return "{fileID: "
                + (_fileId + fileIdOffset).ToString(CultureInfo.InvariantCulture)
                + (local ? "}" : ", guid: " + _guid + ", type: 2}");
        }

        private void AssertRejected(
            OdinMigrationSerializedData payload,
            OdinMigrationDictionaryField[] fields,
            string owningPath,
            IReadOnlyList<Object> objects,
            string expectedFailure
        )
        {
            Assert.IsFalse(
                OdinMigrationTool.TryConvertValidatedSerializedDictionaries(
                    payload,
                    fields,
                    owningPath,
                    objects,
                    out string yaml,
                    out string failure
                )
            );
            Assert.IsTrue(yaml == null);
            StringAssert.Contains(expectedFailure, failure);
        }
    }
}
