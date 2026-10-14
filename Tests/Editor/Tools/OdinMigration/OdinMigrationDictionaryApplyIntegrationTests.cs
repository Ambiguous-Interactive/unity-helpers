// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("MigrationIsolation")]
    [Category("Slow")]
    [Category("Integration")]
    [Timeout(900000)]
    public sealed class OdinMigrationDictionaryApplyIntegrationTests : CommonTestBase
    {
        private const string FolderKey =
            nameof(OdinMigrationDictionaryApplyIntegrationTests) + ".Folder";
        private const string PhaseKey =
            nameof(OdinMigrationDictionaryApplyIntegrationTests) + ".Phase";
        private const string BackupKey =
            nameof(OdinMigrationDictionaryApplyIntegrationTests) + ".Backup";
        private const string ConsumerName = "OdinMigrationApplyConsumer";
        private const string Source =
            "// MIT License - Copyright (c) 2026 wallstop\n"
            + "// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE\n"
            + "namespace MigrationConsumer { using System.Collections.Generic; using UnityEngine; "
            + "public sealed class OdinMigrationApplyConsumer : ScriptableObject { "
            + "public Dictionary<SpriteAlignment, List<Texture2D>> Variations = new Dictionary<SpriteAlignment, List<Texture2D>>(); "
            + "public Dictionary<HideFlags, List<Texture2D>> Other = new Dictionary<HideFlags, List<Texture2D>>(); } }\n";
        private static readonly int[] ApplyCases = { 0, 1, 2 };

        private static string FullPath(string path)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
        }

        private static IEnumerator AwaitEditor()
        {
            yield return null;
            double deadline = EditorApplication.timeSinceStartup + 60;
            while (
                (EditorApplication.isCompiling || EditorApplication.isUpdating)
                && EditorApplication.timeSinceStartup < deadline
            )
            {
                yield return null;
            }
            Assert.IsFalse(EditorApplication.isCompiling);
            Assert.IsFalse(EditorApplication.isUpdating);
        }

        [UnitySetUp]
        public IEnumerator CreateOriginalConsumerSource()
        {
            string folder = SessionState.GetString(FolderKey, string.Empty);
            if (SessionState.GetInt(PhaseKey, 0) == 0)
            {
                folder = "Assets/OdinApplyIntegration-" + Guid.NewGuid().ToString("N");
                SessionState.SetString(FolderKey, folder);
                Assert.IsFalse(
                    string.IsNullOrWhiteSpace(
                        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder))
                    )
                );
                string script = folder + "/" + ConsumerName + ".cs";
                File.WriteAllText(FullPath(script), Source, new UTF8Encoding(false));
                File.WriteAllText(
                    FullPath(script) + ".meta",
                    "fileFormatVersion: 2\nguid: "
                        + Guid.NewGuid().ToString("N")
                        + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
                );
                SessionState.SetInt(PhaseKey, 1);
                yield return new RecompileScripts();
            }
            yield return AwaitEditor();
        }

        [UnityTest]
        public IEnumerator PairedSourcesAndRetainedValuesSurviveReloadAndCompleteVerification(
            [ValueSource(nameof(ApplyCases))] int scenario
        )
        {
            yield return AwaitEditor();
            string folder = SessionState.GetString(FolderKey, string.Empty);
            string ownerPath = folder + "/Owner.asset";
            if (SessionState.GetInt(PhaseKey, 0) == 1)
            {
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                    folder + "/" + ConsumerName + ".cs"
                );
                Assert.IsTrue(script != null);
                Assert.IsTrue(script.GetClass() != null);
                ScriptableObject owner = Track(ScriptableObject.CreateInstance(script.GetClass()));
                Texture2D texture = Track(new Texture2D(1, 1));
                AssetDatabase.CreateAsset(owner, ownerPath);
                _trackedObjects.Remove(owner);
                AssetDatabase.CreateAsset(texture, folder + "/Reference.asset");
                _trackedObjects.Remove(texture);
                if (scenario == 2)
                {
                    ScriptableObject unretained = Track(
                        ScriptableObject.CreateInstance(script.GetClass())
                    );
                    AssetDatabase.CreateAsset(unretained, folder + "/Unretained.asset");
                    _trackedObjects.Remove(unretained);
                }
                AssetDatabase.SaveAssets();
                Assert.IsTrue(
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        owner,
                        out string _,
                        out long ownerId
                    )
                );
                Assert.IsTrue(
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        script,
                        out string scriptGuid,
                        out long _
                    )
                );
                Assert.IsTrue(
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        texture,
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
                File.WriteAllText(
                    FullPath(ownerPath),
                    "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" + retained,
                    new UTF8Encoding(false)
                );
                OdinMigrationDictionaryField[] fields =
                {
                    OdinMigrationDictionaryField.Create<SpriteAlignment, Texture2D>(
                        nameof(OdinMigrationDictionaryAsset.Variations)
                    ),
                    OdinMigrationDictionaryField.Create<HideFlags, Texture2D>(
                        nameof(OdinMigrationDictionaryAsset.Other)
                    ),
                };
                Assert.IsTrue(
                    OdinMigrationTool.TryPreviewDictionaryMigration(
                        new[]
                        {
                            new OdinMigrationDictionaryTarget(
                                ownerPath,
                                ownerId,
                                fields,
                                new UnityEngine.Object[] { texture }
                            ),
                        },
                        out OdinMigrationDictionaryMigrationPreview preview,
                        out string failure
                    ),
                    failure
                );
                Assert.IsTrue(
                    OdinMigrationTool.TryCompileDictionaryMigration(
                        preview,
                        out OdinMigrationDictionaryCompilation compilation,
                        out failure
                    ),
                    failure
                );
                using (compilation)
                {
                    double deadline = EditorApplication.timeSinceStartup + 600;
                    while (!compilation.Completed && EditorApplication.timeSinceStartup < deadline)
                    {
                        yield return null;
                    }
                    Assert.IsTrue(compilation.Completed);
                    Assert.IsTrue(compilation.Succeeded, compilation.Failure);
                    bool started = OdinMigrationTool.TryApplyDictionaryMigration(
                        preview,
                        compilation,
                        out OdinMigrationDictionaryApply apply,
                        out failure
                    );
                    if (apply != null)
                    {
                        SessionState.SetString(BackupKey, apply.BackupRoot);
                    }
                    if (scenario == 2)
                    {
                        Assert.IsFalse(started);
                        Assert.IsTrue(apply == null);
                        StringAssert.Contains("no retained payload", failure);
                        Assert.AreEqual(
                            Source,
                            File.ReadAllText(FullPath(folder + "/" + ConsumerName + ".cs"))
                        );
                        StringAssert.Contains(
                            "serializationData:",
                            File.ReadAllText(FullPath(ownerPath))
                        );
                        SessionState.SetInt(PhaseKey, 3);
                        yield break;
                    }
                    Assert.IsTrue(started, failure);
                    if (scenario == 1)
                    {
                        Assert.IsTrue(apply.TryCancel(out failure), failure);
                    }
                    SessionState.SetInt(PhaseKey, 2);
                }
                if (scenario != 1)
                {
                    yield return new WaitForDomainReload();
                }
            }
            yield return AwaitEditor();
            Assert.IsTrue(
                OdinMigrationTool.TryReadDictionaryApply(
                    SessionState.GetString(BackupKey, string.Empty),
                    out OdinMigrationDictionaryApply completed,
                    out string statusFailure
                ),
                statusFailure
            );
            double completionDeadline = EditorApplication.timeSinceStartup + 120;
            while (!completed.Completed && EditorApplication.timeSinceStartup < completionDeadline)
            {
                yield return null;
            }
            Assert.IsTrue(
                completed.TryReadStatus(out OdinMigrationApplyState state, out statusFailure),
                statusFailure
            );
            Assert.AreEqual(
                scenario == 1
                    ? OdinMigrationApplyState.Recovered
                    : OdinMigrationApplyState.Verified,
                state,
                statusFailure
            );
            folder = SessionState.GetString(FolderKey, string.Empty);
            ownerPath = folder + "/Owner.asset";
            if (scenario == 1)
            {
                Assert.AreEqual(
                    Source,
                    File.ReadAllText(FullPath(folder + "/" + ConsumerName + ".cs"))
                );
                StringAssert.Contains("serializationData:", File.ReadAllText(FullPath(ownerPath)));
                SessionState.SetInt(PhaseKey, 3);
                yield break;
            }
            ScriptableObject imported = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ownerPath);
            Assert.IsTrue(imported != null);
            using (SerializedObject serialized = new SerializedObject(imported))
            {
                SerializedProperty variations = serialized.FindProperty(
                    nameof(OdinMigrationDictionaryAsset.Variations)
                );
                Assert.IsTrue(variations != null);
                SerializedProperty keys = variations.FindPropertyRelative(
                    WallstopStudios
                        .UnityHelpers
                        .Core
                        .DataStructure
                        .Adapters
                        .SerializableDictionarySerializedPropertyNames
                        .Keys
                );
                Assert.AreEqual(1, keys.arraySize);
                Assert.AreEqual(
                    (int)SpriteAlignment.Center,
                    keys.GetArrayElementAtIndex(0).intValue
                );
            }
            StringAssert.Contains(
                "SerializableDictionary",
                File.ReadAllText(FullPath(folder + "/" + ConsumerName + ".cs"))
            );
            StringAssert.DoesNotContain(
                "serializationData:",
                File.ReadAllText(FullPath(ownerPath))
            );
            SessionState.SetInt(PhaseKey, 3);
        }

        [UnityTearDown]
        public IEnumerator RemoveTemporaryConsumerAndRecoveryFiles()
        {
            string folder = SessionState.GetString(FolderKey, string.Empty);
            string backup = SessionState.GetString(BackupKey, string.Empty);
            if (
                !string.IsNullOrWhiteSpace(backup)
                && OdinMigrationTool.TryReadDictionaryApply(
                    backup,
                    out OdinMigrationDictionaryApply apply,
                    out string _
                )
                && apply.TryReadStatus(out OdinMigrationApplyState currentState, out string _)
                && currentState != OdinMigrationApplyState.Verified
                && currentState != OdinMigrationApplyState.Recovered
            )
            {
                Assert.IsTrue(apply.TryCancel(out string cancelFailure), cancelFailure);
                yield return AwaitEditor();
                Assert.IsTrue(
                    OdinMigrationTool.TryRestoreBackup(backup, out string restoreFailure),
                    restoreFailure
                );
                yield return new RecompileScripts();
                yield return AwaitEditor();
                backup = SessionState.GetString(BackupKey, string.Empty);
                Assert.IsTrue(
                    OdinMigrationTool.TryReadDictionaryApply(
                        backup,
                        out apply,
                        out string readFailure
                    ),
                    readFailure
                );
                double deadline = EditorApplication.timeSinceStartup + 120;
                while (!apply.Completed && EditorApplication.timeSinceStartup < deadline)
                {
                    yield return null;
                }
                Assert.IsTrue(
                    apply.TryReadStatus(
                        out OdinMigrationApplyState state,
                        out string recoveryFailure
                    ),
                    recoveryFailure
                );
                Assert.AreEqual(OdinMigrationApplyState.Recovered, state, recoveryFailure);
            }
            folder = SessionState.GetString(FolderKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(folder) && AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.DeleteAsset(folder);
                yield return new RecompileScripts();
            }
            backup = SessionState.GetString(BackupKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(backup) && Directory.Exists(backup))
            {
                Directory.Delete(backup, true);
            }
            SessionState.EraseString(FolderKey);
            SessionState.EraseString(BackupKey);
            SessionState.EraseInt(PhaseKey);
        }
    }
}
