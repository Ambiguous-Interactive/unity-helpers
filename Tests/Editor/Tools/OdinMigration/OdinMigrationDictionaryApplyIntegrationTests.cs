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
    using WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration;
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
        private const string RetirementKey =
            nameof(OdinMigrationDictionaryApplyIntegrationTests) + ".Retired";
        private const string ConsumerName = "OdinMigrationApplyConsumer";
        private const string BaseConsumerName = "OdinMigrationApplyConsumerBase";
        private const string Source =
            "// MIT License - Copyright (c) 2026 wallstop\n"
            + "// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE\n"
            + "namespace MigrationConsumer { using System.Collections.Generic; using UnityEngine; "
            + "public partial class OdinMigrationApplyConsumerBase : ScriptableObject { "
            + "public Dictionary<SpriteAlignment, List<Texture2D>> Variations = new Dictionary<SpriteAlignment, List<Texture2D>>(); "
            + "private readonly Dictionary<HideFlags, List<Texture2D>> Other = new Dictionary<HideFlags, List<Texture2D>>(); "
            + "public bool Corrupt; private void OnValidate() { if (Corrupt && Variations.Count != 0) { Variations.Clear(); } } } }\n";
        private static readonly int[] ApplyCases = { 0, 1, 2, 3, 4, 5, 6 };

        private static string FullPath(string path)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
        }

        private static string SourcePath(string folder)
        {
            return folder + "/Base/" + BaseConsumerName + ".cs";
        }

        private static void WriteImporterMeta(string path, bool assemblyDefinition)
        {
            File.WriteAllText(
                FullPath(path) + ".meta",
                "fileFormatVersion: 2\nguid: "
                    + Guid.NewGuid().ToString("N")
                    + (
                        assemblyDefinition
                            ? "\nAssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
                            : "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
                    ),
                new UTF8Encoding(false)
            );
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
                string assemblyName = Path.GetFileName(folder);
                Assert.IsFalse(
                    string.IsNullOrWhiteSpace(AssetDatabase.CreateFolder(folder, "Base"))
                );
                string baseDefinition = folder + "/Base/" + assemblyName + ".Base.asmdef";
                File.WriteAllText(
                    FullPath(baseDefinition),
                    "{\"name\":\""
                        + assemblyName
                        + ".Base\",\"references\":[\"WallstopStudios.UnityHelpers\"],\"includePlatforms\":[\"Editor\"]}",
                    new UTF8Encoding(false)
                );
                File.WriteAllText(FullPath(SourcePath(folder)), Source, new UTF8Encoding(false));
                WriteImporterMeta(baseDefinition, true);
                WriteImporterMeta(SourcePath(folder), false);
                string definition = folder + "/" + assemblyName + ".asmdef";
                File.WriteAllText(
                    FullPath(definition),
                    "{\"name\":\""
                        + assemblyName
                        + "\",\"references\":[\"WallstopStudios.UnityHelpers\",\""
                        + assemblyName
                        + ".Base\"],\"includePlatforms\":[\"Editor\"]}",
                    new UTF8Encoding(false)
                );
                WriteImporterMeta(definition, true);
                string script = folder + "/" + ConsumerName + ".cs";
                File.WriteAllText(
                    FullPath(script),
                    "namespace MigrationConsumer { public sealed class "
                        + ConsumerName
                        + " : "
                        + BaseConsumerName
                        + " {} }",
                    new UTF8Encoding(false)
                );
                WriteImporterMeta(script, false);
                SessionState.SetInt(PhaseKey, 1);
                yield return new RecompileScripts();
            }
            {
                yield return null;
                double readinessDeadline = EditorApplication.timeSinceStartup + 60;
                while (
                    (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    && EditorApplication.timeSinceStartup < readinessDeadline
                )
                {
                    if (EditorApplication.isCompiling)
                    {
                        yield return new RecompileScripts(false);
                    }
                    else
                    {
                        yield return null;
                    }
                }
                Assert.IsFalse(EditorApplication.isCompiling, "Compilation remained pending.");
                Assert.IsFalse(EditorApplication.isUpdating, "Import remained pending.");
            }
        }

        [UnityTest]
        public IEnumerator PairedSourcesAndRetainedValuesSurviveReloadAndCompleteVerification(
            [ValueSource(nameof(ApplyCases))] int scenario
        )
        {
            {
                yield return null;
                double readinessDeadline = EditorApplication.timeSinceStartup + 60;
                while (
                    (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    && EditorApplication.timeSinceStartup < readinessDeadline
                )
                {
                    if (EditorApplication.isCompiling)
                    {
                        yield return new RecompileScripts(false);
                    }
                    else
                    {
                        yield return null;
                    }
                }
                Assert.IsFalse(EditorApplication.isCompiling, "Compilation remained pending.");
                Assert.IsFalse(EditorApplication.isUpdating, "Import remained pending.");
            }
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
                if (scenario == 3 || scenario == 6)
                {
                    retained = retained.Replace(
                        "MonoBehaviour:\n",
                        "MonoBehaviour:\n  Corrupt: 1\n"
                    );
                }
                bool liveCapture = 4 <= scenario;
                if (!liveCapture)
                {
                    File.WriteAllText(
                        FullPath(ownerPath),
                        "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" + retained,
                        new UTF8Encoding(false)
                    );
                }
                else
                {
                    if (scenario == 6)
                    {
                        string originalAsset = File.ReadAllText(FullPath(ownerPath));
                        Assert.IsTrue(originalAsset.Contains("  Corrupt: 0"));
                        File.WriteAllText(
                            FullPath(ownerPath),
                            originalAsset.Replace("  Corrupt: 0", "  Corrupt: 1"),
                            new UTF8Encoding(false)
                        );
                        AssetDatabase.ImportAsset(
                            ownerPath,
                            ImportAssetOptions.ForceSynchronousImport
                                | ImportAssetOptions.ForceUpdate
                        );
                        {
                            yield return null;
                            double readinessDeadline = EditorApplication.timeSinceStartup + 60;
                            while (
                                (EditorApplication.isCompiling || EditorApplication.isUpdating)
                                && EditorApplication.timeSinceStartup < readinessDeadline
                            )
                            {
                                if (EditorApplication.isCompiling)
                                {
                                    yield return new RecompileScripts(false);
                                }
                                else
                                {
                                    yield return null;
                                }
                            }
                            Assert.IsFalse(
                                EditorApplication.isCompiling,
                                "Compilation remained pending."
                            );
                            Assert.IsFalse(
                                EditorApplication.isUpdating,
                                "Import remained pending."
                            );
                        }
                        owner = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ownerPath);
                    }
                    Assert.IsTrue(
                        OdinMigrationCompiledBindings.TryGet(
                            owner.GetType(),
                            out OdinMigrationCompiledBinding liveBinding
                        )
                    );
                    foreach (OdinMigrationCompiledField field in liveBinding.Fields)
                    {
                        int key = string.Equals(
                            field.Name,
                            nameof(OdinMigrationDictionaryAsset.Variations),
                            StringComparison.Ordinal
                        )
                            ? (int)SpriteAlignment.Center
                            : (int)HideFlags.HideInHierarchy;
                        Assert.IsTrue(
                            field.TryRestore(
                                owner,
                                new[] { key },
                                new IReadOnlyList<UnityEngine.Object>[]
                                {
                                    new UnityEngine.Object[] { texture, null },
                                },
                                out string restoreFailure
                            ),
                            restoreFailure
                        );
                    }
                }
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
                                new UnityEngine.Object[] { texture },
                                liveCapture ? owner : null
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
                        StringAssert.Contains("selected live capture", failure);
                        Assert.AreEqual(Source, File.ReadAllText(FullPath(SourcePath(folder))));
                        StringAssert.Contains(
                            "serializationData:",
                            File.ReadAllText(FullPath(ownerPath))
                        );
                        SessionState.SetInt(PhaseKey, 3);
                        yield break;
                    }
                    Assert.IsTrue(started, failure);
                    if (scenario == 1 || scenario == 5)
                    {
                        Assert.IsTrue(apply.TryCancel(out failure), failure);
                    }
                    SessionState.SetInt(PhaseKey, 2);
                }
                if (scenario != 1 && scenario != 5)
                {
                    yield return new WaitForDomainReload();
                    if (scenario == 3 || scenario == 6)
                    {
                        yield return new WaitForDomainReload();
                    }
                }
            }
            {
                yield return null;
                double readinessDeadline = EditorApplication.timeSinceStartup + 60;
                while (
                    (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    && EditorApplication.timeSinceStartup < readinessDeadline
                )
                {
                    if (EditorApplication.isCompiling)
                    {
                        yield return new RecompileScripts(false);
                    }
                    else
                    {
                        yield return null;
                    }
                }
                Assert.IsFalse(EditorApplication.isCompiling, "Compilation remained pending.");
                Assert.IsFalse(EditorApplication.isUpdating, "Import remained pending.");
            }
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
                scenario == 1 || scenario == 3 || scenario == 5 || scenario == 6
                    ? OdinMigrationApplyState.Recovered
                    : OdinMigrationApplyState.Verified,
                state,
                statusFailure
            );
            folder = SessionState.GetString(FolderKey, string.Empty);
            ownerPath = folder + "/Owner.asset";
            if (scenario == 1 || scenario == 3 || scenario == 5 || scenario == 6)
            {
                if (scenario == 3 || scenario == 6)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(statusFailure));
                    StringAssert.DoesNotContain("cancel", statusFailure.ToLowerInvariant());
                }
                Assert.AreEqual(Source, File.ReadAllText(FullPath(SourcePath(folder))));
                if (4 <= scenario)
                {
                    StringAssert.DoesNotContain(
                        "serializationData:",
                        File.ReadAllText(FullPath(ownerPath))
                    );
                    ScriptableObject recovered = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                        ownerPath
                    );
                    Assert.IsTrue(
                        OdinMigrationCompiledBindings.TryGet(
                            recovered.GetType(),
                            out OdinMigrationCompiledBinding recoveredBinding
                        )
                    );
                    foreach (OdinMigrationCompiledField field in recoveredBinding.Fields)
                    {
                        Assert.IsTrue(
                            field.TryCapture(
                                recovered,
                                out OdinMigrationLiveDictionary values,
                                out string captureFailure
                            ),
                            captureFailure
                        );
                        Assert.AreEqual(1, values.Keys.Count);
                        Assert.AreEqual(2, values.Values[0].Count);
                        Assert.IsTrue(values.Values[0][0] != null);
                        Assert.IsTrue(ReferenceEquals(values.Values[0][1], null));
                    }
                }
                else
                {
                    StringAssert.Contains(
                        "serializationData:",
                        File.ReadAllText(FullPath(ownerPath))
                    );
                }
                string[] recoveryLines = File.ReadAllLines(
                    Path.Combine(completed.BackupRoot, OdinMigrationBackupReceipt.ReceiptName)
                );
                for (int index = 2; index < recoveryLines.Length - 1; ++index)
                {
                    string restoredPath = recoveryLines[index].Split('\t')[0];
                    CollectionAssert.AreEqual(
                        File.ReadAllBytes(
                            OdinMigrationBackupReceipt.GetNumberedBackupPath(
                                completed.BackupRoot,
                                index - 2
                            )
                        ),
                        File.ReadAllBytes(FullPath(restoredPath)),
                        restoredPath
                    );
                }
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
                File.ReadAllText(FullPath(SourcePath(folder)))
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
                {
                    yield return null;
                    double readinessDeadline = EditorApplication.timeSinceStartup + 60;
                    while (
                        (EditorApplication.isCompiling || EditorApplication.isUpdating)
                        && EditorApplication.timeSinceStartup < readinessDeadline
                    )
                    {
                        if (EditorApplication.isCompiling)
                        {
                            yield return new RecompileScripts(false);
                        }
                        else
                        {
                            yield return null;
                        }
                    }
                    Assert.IsFalse(EditorApplication.isCompiling, "Compilation remained pending.");
                    Assert.IsFalse(EditorApplication.isUpdating, "Import remained pending.");
                }
                Assert.IsTrue(
                    OdinMigrationTool.TryRestoreBackup(backup, out string restoreFailure),
                    restoreFailure
                );
                yield return new RecompileScripts();
                {
                    yield return null;
                    double readinessDeadline = EditorApplication.timeSinceStartup + 60;
                    while (
                        (EditorApplication.isCompiling || EditorApplication.isUpdating)
                        && EditorApplication.timeSinceStartup < readinessDeadline
                    )
                    {
                        if (EditorApplication.isCompiling)
                        {
                            yield return new RecompileScripts(false);
                        }
                        else
                        {
                            yield return null;
                        }
                    }
                    Assert.IsFalse(EditorApplication.isCompiling, "Compilation remained pending.");
                    Assert.IsFalse(EditorApplication.isUpdating, "Import remained pending.");
                }
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
                string assemblyName = Path.GetFileName(folder);
                string definition = FullPath(folder + "/" + assemblyName + ".asmdef");
                if (File.Exists(definition) && !SessionState.GetBool(RetirementKey, false))
                {
                    File.WriteAllText(
                        definition,
                        "{\"name\":\""
                            + assemblyName
                            + "\",\"references\":[\"WallstopStudios.UnityHelpers\"],\"includePlatforms\":[\"Editor\"],"
                            + "\"defineConstraints\":[\"WALLSTOP_MIGRATION_RETIRED_CONSUMER\"]}",
                        new UTF8Encoding(false)
                    );
                    File.WriteAllText(
                        FullPath(folder + "/Base/" + assemblyName + ".Base.asmdef"),
                        "{\"name\":\""
                            + assemblyName
                            + ".Base\",\"references\":[\"WallstopStudios.UnityHelpers\"],\"includePlatforms\":[\"Editor\"],\"defineConstraints\":[\"WALLSTOP_MIGRATION_RETIRED_CONSUMER\"]}",
                        new UTF8Encoding(false)
                    );
                    SessionState.SetBool(RetirementKey, true);
                    yield return new RecompileScripts(false);
                }
                {
                    yield return null;
                    double readinessDeadline = EditorApplication.timeSinceStartup + 60;
                    while (
                        (EditorApplication.isCompiling || EditorApplication.isUpdating)
                        && EditorApplication.timeSinceStartup < readinessDeadline
                    )
                    {
                        if (EditorApplication.isCompiling)
                        {
                            yield return new RecompileScripts(false);
                        }
                        else
                        {
                            yield return null;
                        }
                    }
                    Assert.IsFalse(EditorApplication.isCompiling, "Compilation remained pending.");
                    Assert.IsFalse(EditorApplication.isUpdating, "Import remained pending.");
                }
                folder = SessionState.GetString(FolderKey, string.Empty);
                assemblyName = Path.GetFileName(folder);
                bool packageAssemblyPresent = false;
                string packageAssemblyName = typeof(OdinMigrationTool).Assembly.GetName().Name;
                foreach (
                    UnityEditor.Compilation.Assembly assembly in UnityEditor.Compilation.CompilationPipeline.GetAssemblies()
                )
                {
                    bool isPackageAssembly = string.Equals(
                        assembly.name,
                        packageAssemblyName,
                        StringComparison.Ordinal
                    );
                    packageAssemblyPresent = packageAssemblyPresent || isPackageAssembly;
                    Assert.AreNotEqual(
                        assemblyName,
                        assembly.name,
                        "Retire the consumer assembly before deleting its compiler inputs."
                    );
                    Assert.AreNotEqual(
                        assemblyName + ".Base",
                        assembly.name,
                        "Retire the base assembly before deleting its compiler inputs."
                    );
                }
                Assert.IsTrue(
                    packageAssemblyPresent,
                    "The compilation graph must include the package editor assembly."
                );
                Assert.IsTrue(File.Exists(FullPath(folder + "/" + ConsumerName + ".cs")));
                Assert.IsTrue(AssetDatabase.DeleteAsset(folder));
                Assert.IsFalse(Directory.Exists(FullPath(folder)));
                {
                    yield return null;
                    double readinessDeadline = EditorApplication.timeSinceStartup + 60;
                    while (
                        (EditorApplication.isCompiling || EditorApplication.isUpdating)
                        && EditorApplication.timeSinceStartup < readinessDeadline
                    )
                    {
                        if (EditorApplication.isCompiling)
                        {
                            yield return new RecompileScripts(false);
                        }
                        else
                        {
                            yield return null;
                        }
                    }
                    Assert.IsFalse(EditorApplication.isCompiling, "Compilation remained pending.");
                    Assert.IsFalse(EditorApplication.isUpdating, "Import remained pending.");
                }
            }
            backup = SessionState.GetString(BackupKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(backup) && Directory.Exists(backup))
            {
                Directory.Delete(backup, true);
            }
            SessionState.EraseString(FolderKey);
            SessionState.EraseString(BackupKey);
            SessionState.EraseInt(PhaseKey);
            SessionState.EraseBool(RetirementKey);
        }
    }
}
