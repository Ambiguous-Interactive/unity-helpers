// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    [TestFixture]
    [Category("MigrationIsolation")]
    [Category("Integration")]
    public sealed class OdinMigrationDictionaryImportVerificationTests : CommonTestBase
    {
        private static readonly int[] CorruptionCases = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

        private string _folder;
        private string _path;
        private Texture2D _texture;
        private Texture2D _other;
        private OdinMigrationDictionaryAsset _asset;
        private OdinMigrationSerializedData _payload;
        private OdinMigrationDictionaryField[] _fields;

        private static IEnumerator WaitForImport()
        {
            yield return null;
            double deadline = EditorApplication.timeSinceStartup + 60;
            while (
                (EditorApplication.isUpdating || EditorApplication.isCompiling)
                && EditorApplication.timeSinceStartup < deadline
            )
            {
                yield return null;
            }
            Assert.IsFalse(EditorApplication.isUpdating, "Asset import did not finish.");
            Assert.IsFalse(EditorApplication.isCompiling, "Editor compilation did not finish.");
        }

        [UnitySetUp]
        public IEnumerator WaitForImportedDictionary()
        {
            yield return WaitForImport();
            CreateImportedDictionary();
            yield return WaitForImport();
            Assert.IsTrue(Verify(out string failure), failure);
        }

        [UnityTest]
        public IEnumerator ImportedAndReserializedValuesMatchRetainedKeysNullsAndRepeatedReferences()
        {
            Assert.IsTrue(Verify(out string failure), failure);
            AssetDatabase.ForceReserializeAssets(new[] { _path });
            SaveAndImport();
            yield return WaitForImport();
            Assert.IsTrue(Verify(out failure), failure);
            Assert.AreEqual(1, _asset.Variations.Count);
            Assert.IsTrue(
                _asset.Variations.TryGetValue(SpriteAlignment.Center, out List<Texture2D> values)
            );
            Assert.AreEqual(3, values.Count);
        }

        [UnityTest]
        public IEnumerator ImportedDataLossAndChangedReferencesAreRejected(
            [ValueSource(nameof(CorruptionCases))] int scenario
        )
        {
            int corruption = scenario % 6;
            bool saveAndImport = 6 <= scenario;
            Assert.IsTrue(
                _asset.Variations.TryGetValue(SpriteAlignment.Center, out List<Texture2D> values)
            );
            if (corruption == 0)
            {
                _asset.Variations.Clear();
                _asset.Variations[SpriteAlignment.TopLeft] = values;
            }
            else if (corruption == 1)
            {
                values.RemoveAt(2);
            }
            else if (corruption == 2)
            {
                values[0] = _other;
            }
            else if (corruption == 3)
            {
                values[1] = _texture;
            }
            else if (corruption == 4)
            {
                values[0] = null;
            }
            else
            {
                _asset.Other.Clear();
            }
            if (saveAndImport)
            {
                SaveAndImport();
                yield return WaitForImport();
            }
            Assert.IsFalse(Verify(out string failure));
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
            StringAssert.DoesNotContain("Wait for compilation", failure);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void InvalidVerificationInputsAreRejected(int invalid)
        {
            Assert.IsFalse(
                OdinMigrationTool.TryVerifyConvertedDictionaries(
                    invalid == 0 ? null : _payload,
                    invalid == 1 ? null
                        : invalid == 2 ? Array.Empty<OdinMigrationDictionaryField>()
                        : _fields,
                    invalid == 3 ? " "
                        : invalid == 4 ? _folder + "/Other.asset"
                        : _path,
                    invalid == 5 ? null : _asset,
                    invalid == 6 ? Array.Empty<Object>()
                        : invalid == 7 ? new Object[] { _other }
                        : new Object[] { _texture },
                    out string failure
                )
            );
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RetainedOwnerAndScriptIdentityDriftAreRejected(bool scriptChanged)
        {
            OdinMigrationSerializedData changed = new OdinMigrationSerializedData(
                scriptChanged ? _payload.FileId : _payload.FileId + 1,
                scriptChanged ? Guid.NewGuid().ToString("N") : _payload.ScriptGuid,
                _payload.Source,
                _payload.Line,
                _payload.EndLine,
                new List<OdinMigrationSerializedNode>(_payload.SerializationNodes),
                new List<string>(_payload.ReferencedUnityObjects)
            );
            Assert.IsFalse(
                OdinMigrationTool.TryVerifyConvertedDictionaries(
                    changed,
                    _fields,
                    _path,
                    _asset,
                    new Object[] { _texture },
                    out string failure
                )
            );
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
        }

        private void CreateImportedDictionary()
        {
            _folder = "Assets/OdinImportVerification-" + Guid.NewGuid().ToString("N");
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(
                    AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(_folder))
                )
            );
            TrackFolder(_folder);
            _texture = Track(new Texture2D(1, 1));
            _other = Track(new Texture2D(1, 1));
            AssetDatabase.CreateAsset(_texture, _folder + "/Reference.asset");
            AssetDatabase.CreateAsset(_other, _folder + "/Other.asset");
            _asset = Track(ScriptableObject.CreateInstance<OdinMigrationDictionaryAsset>());
            _path = _folder + "/Owner.asset";
            AssetDatabase.CreateAsset(_asset, _path);
            _asset.Variations[SpriteAlignment.Center] = new List<Texture2D>
            {
                _texture,
                null,
                _texture,
            };
            _asset.Other[(HideFlags)(-1)] = new List<Texture2D> { _texture };
            SaveAndImport();
            MonoScript script = MonoScript.FromScriptableObject(_asset);
            Assert.IsTrue(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    script,
                    out string scriptGuid,
                    out long _
                )
            );
            Assert.IsTrue(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    _asset,
                    out string _,
                    out long ownerId
                )
            );
            Assert.IsTrue(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    _texture,
                    out string textureGuid,
                    out long textureId
                )
            );
            string retained = OdinMigrationDictionaryConverterTests
                .SourceWithTwoDictionaries(withNullObjects: true)
                .Replace("UnityEngine.Sprite,", "UnityEngine.Texture2D,")
                .Replace("&123\n", "&" + ownerId.ToString(CultureInfo.InvariantCulture) + "\n")
                .Replace(
                    "MonoBehaviour:\n",
                    "MonoBehaviour:\n  m_Script: {fileID: 11500000, guid: "
                        + scriptGuid
                        + ", type: 3}\n"
                )
                .Replace(
                    "{fileID: 21300000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}",
                    "{fileID: "
                        + textureId.ToString(CultureInfo.InvariantCulture)
                        + ", guid: "
                        + textureGuid
                        + ", type: 2}"
                );
            Assert.IsTrue(
                OdinMigrationTool.TryReadSerializedNodes(
                    retained,
                    out IReadOnlyList<OdinMigrationSerializedData> payloads,
                    out string failure
                ),
                failure
            );
            _payload = payloads[0];
            _fields = new[]
            {
                OdinMigrationDictionaryField.Create<SpriteAlignment, Texture2D>(
                    nameof(OdinMigrationDictionaryAsset.Variations)
                ),
                OdinMigrationDictionaryField.Create<HideFlags, Texture2D>(
                    nameof(OdinMigrationDictionaryAsset.Other)
                ),
            };
        }

        private void SaveAndImport()
        {
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                _path,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
            );
            _asset = Track(AssetDatabase.LoadAssetAtPath<OdinMigrationDictionaryAsset>(_path));
        }

        private bool Verify(out string failure)
        {
            return OdinMigrationTool.TryVerifyConvertedDictionaries(
                _payload,
                _fields,
                _path,
                _asset,
                new Object[] { _texture },
                out failure
            );
        }
    }
}
