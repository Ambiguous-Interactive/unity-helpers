// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    public sealed class OdinMigrationDictionaryImportTests : CommonTestBase
    {
        [Test]
        public void ConvertedFieldsSurviveImportAndReserializationWithNullsAndReferences()
        {
            string folder = "Assets/OdinMigrationImport-" + Guid.NewGuid().ToString("N");
            Assert.IsTrue(
                !string.IsNullOrWhiteSpace(
                    AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder))
                )
            );
            SerializationMode previousMode = EditorSettings.serializationMode;
            try
            {
                EditorSettings.serializationMode = SerializationMode.ForceText;
                Texture2D texture = Track(new Texture2D(1, 1));
                string texturePath = folder + "/Reference.asset";
                AssetDatabase.CreateAsset(texture, texturePath);
                OdinMigrationDictionaryAsset asset = Track(
                    ScriptableObject.CreateInstance<OdinMigrationDictionaryAsset>()
                );
                string assetPath = folder + "/Converted.asset";
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();
                Assert.IsTrue(
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        texture,
                        out string guid,
                        out long fileId
                    )
                );
                string reference =
                    "{fileID: "
                    + fileId.ToString(CultureInfo.InvariantCulture)
                    + ", guid: "
                    + guid
                    + ", type: 2}";
                string retained = OdinMigrationDictionaryConverterTests
                    .SourceWithTwoDictionaries(withNullObjects: true)
                    .Replace("UnityEngine.Sprite,", "UnityEngine.Texture2D,")
                    .Replace(
                        "{fileID: 21300000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}",
                        reference
                    );
                Assert.IsTrue(
                    OdinMigrationTool.TryReadSerializedNodes(
                        retained,
                        out IReadOnlyList<OdinMigrationSerializedData> payloads,
                        out string failure
                    ),
                    failure
                );
                Assert.IsTrue(
                    OdinMigrationTool.TryConvertSerializedDictionaries(
                        payloads[0],
                        new[]
                        {
                            OdinMigrationDictionaryField.Create<SpriteAlignment, Texture2D>(
                                nameof(OdinMigrationDictionaryAsset.Variations)
                            ),
                            OdinMigrationDictionaryField.Create<HideFlags, Texture2D>(
                                nameof(OdinMigrationDictionaryAsset.Other)
                            ),
                        },
                        out string fragment,
                        out failure
                    ),
                    failure
                );
                string path = Path.Combine(
                    Application.dataPath,
                    assetPath.Substring("Assets/".Length)
                );
                string original = File.ReadAllText(path);
                int start = original.IndexOf(
                    "  " + nameof(OdinMigrationDictionaryAsset.Variations) + ":",
                    StringComparison.Ordinal
                );
                Assert.GreaterOrEqual(start, 0);
                Resources.UnloadAsset(asset);
                File.WriteAllText(path, original.Substring(0, start) + fragment);
                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
                );
                AssertConverted(assetPath, texture);
                AssetDatabase.ForceReserializeAssets(new[] { assetPath });
                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
                );
                AssertConverted(assetPath, texture);
            }
            finally
            {
                EditorSettings.serializationMode = previousMode;
                Assert.IsTrue(AssetDatabase.DeleteAsset(folder));
            }
        }

        private void AssertConverted(string path, Texture2D texture)
        {
            OdinMigrationDictionaryAsset loaded = Track(
                AssetDatabase.LoadAssetAtPath<OdinMigrationDictionaryAsset>(path)
            );
            Assert.IsTrue(loaded != null);
            Assert.AreEqual(1, loaded.Variations.Count);
            Assert.IsTrue(
                loaded.Variations.TryGetValue(SpriteAlignment.Center, out List<Texture2D> values)
            );
            Assert.AreEqual(3, values.Count);
            Assert.AreEqual(texture, values[0]);
            Assert.IsTrue(values[1] == null);
            Assert.AreEqual(texture, values[2]);
            Assert.AreEqual(1, loaded.Other.Count);
            Assert.IsTrue(loaded.Other.TryGetValue((HideFlags)(-1), out List<Texture2D> other));
            Assert.AreEqual(1, other.Count);
            Assert.AreEqual(texture, other[0]);
            Resources.UnloadAsset(loaded);
        }
    }
}
