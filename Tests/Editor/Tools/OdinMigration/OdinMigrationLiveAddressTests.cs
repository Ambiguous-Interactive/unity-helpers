// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Slow")]
    [Category("Integration")]
    public sealed class OdinMigrationLiveAddressTests : CommonTestBase
    {
        [TestCase(false)]
        [TestCase(true)]
        public void LiveAddressesMatchActualUnitySerialization(bool native)
        {
            string folder = "Assets/OdinLiveAddress-" + Guid.NewGuid().ToString("N");
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(
                    AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder))
                )
            );
            TrackFolder(folder);
            try
            {
                Texture2D texture = Track(new Texture2D(1, 1));
                if (native)
                {
                    AssetDatabase.CreateAsset(texture, folder + "/Native.asset");
                    _trackedObjects.Remove(texture);
                }
                else
                {
                    string path = folder + "/Imported.png";
                    File.WriteAllBytes(
                        Path.Combine(Directory.GetParent(Application.dataPath).FullName, path),
                        texture.EncodeToPNG()
                    );
                    AssetDatabase.ImportAsset(
                        path,
                        ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate
                    );
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    Assert.IsTrue(texture != null);
                }
                OdinMigrationDictionaryAsset owner =
                    CreateScriptableObject<OdinMigrationDictionaryAsset>();
                owner.Variations.Add(SpriteAlignment.Center, new List<Texture2D> { texture });
                string ownerPath = folder + "/Owner.asset";
                AssetDatabase.CreateAsset(owner, ownerPath);
                _trackedObjects.Remove(owner);
                AssetDatabase.SaveAssets();
                string guid = AssetDatabase.AssetPathToGUID(ownerPath);
                string address = OdinMigrationLiveDocument.Address(texture, guid);
                string yaml = File.ReadAllText(
                    Path.Combine(Directory.GetParent(Application.dataPath).FullName, ownerPath)
                );

                StringAssert.Contains(address, yaml);
                new OdinMigrationObjectReferenceValidator(guid).ValidateReferenceIdentity(
                    address,
                    texture
                );
            }
            finally
            {
                Assert.IsTrue(AssetDatabase.DeleteAsset(folder));
                Assert.IsFalse(
                    Directory.Exists(
                        Path.Combine(Directory.GetParent(Application.dataPath).FullName, folder)
                    )
                );
            }
        }
    }
}
