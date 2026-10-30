// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;

    [TestFixture]
    [Category("MigrationIsolation")]
    [Category("Fast")]
    public sealed class OdinMigrationApplyRecordTests
    {
        private string _root;

        private static OdinMigrationApplyRecord Create(string root)
        {
            const string Path = "Assets/.OdinRecord.asset";
            string hash = OdinMigrationBackupReceipt.Hash(Array.Empty<byte>());
            return new OdinMigrationApplyRecord
            {
                backupName = System.IO.Path.GetFileName(root),
                projectHash = OdinMigrationBackupReceipt.Hash(
                    Encoding.UTF8.GetBytes(OdinMigrationApplySnapshot.ProjectRoot)
                ),
                importEpoch = Guid.NewGuid().ToString("N"),
                state = OdinMigrationApplyState.AwaitingImport,
                deadlineUtcTicks = DateTime.UtcNow.AddMinutes(15).Ticks,
                affectedPaths = new[] { Path },
                files = new[]
                {
                    new OdinMigrationApplyRecord.FileSnapshot
                    {
                        assetPath = Path,
                        originalHash = hash,
                        proposedHash = hash,
                        metadataHash = hash,
                    },
                },
                assemblies = new[]
                {
                    new OdinMigrationApplyRecord.AssemblySnapshot
                    {
                        name = typeof(OdinMigrationApplyRecordTests).Assembly.GetName().Name,
                        moduleVersion =
                            typeof(OdinMigrationApplyRecordTests).Assembly.ManifestModule.ModuleVersionId.ToString(
                                "D"
                            ),
                    },
                },
                originallyLoadedScenes = Array.Empty<string>(),
                openedScenes = Array.Empty<string>(),
                temporaryScene = string.Empty,
                documents = new[]
                {
                    new OdinMigrationApplyRecord.Document
                    {
                        assetPath = Path,
                        fileId = 123,
                        owningGuid = Guid.NewGuid().ToString("N"),
                        scriptGuid = Guid.NewGuid().ToString("N"),
                        ownerTypeIdentity =
                            typeof(OdinMigrationDictionaryAsset).AssemblyQualifiedName,
                        fields = new[]
                        {
                            new OdinMigrationApplyRecord.Field
                            {
                                name = nameof(OdinMigrationDictionaryAsset.Variations),
                                entries = new[]
                                {
                                    new OdinMigrationApplyRecord.Entry
                                    {
                                        key = -1,
                                        references = new[]
                                        {
                                            new OdinMigrationApplyRecord.Reference
                                            {
                                                address = "{fileID: 0}",
                                                typeIdentity = string.Empty,
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        [SetUp]
        public void CreateRecoveryDirectory()
        {
            _root = System.IO.Path.Combine(
                OdinMigrationApplySnapshot.ProjectRoot,
                "Library",
                "WallstopOdinMigrationBackups",
                "record-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void RemoveRecoveryDirectory()
        {
            Directory.Delete(_root, true);
        }

        [Test]
        public void PersistedPhaseValuesAndReferenceTypesSurviveACompleteRead()
        {
            OdinMigrationApplyRecord original = Create(_root);
            original.failure = "A quoted \"{diagnostic}\" with a \\ slash and\nnewline.";
            original.Write(_root);
            Assert.IsTrue(
                OdinMigrationApplyRecord.TryRead(
                    _root,
                    out OdinMigrationApplyRecord loaded,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(original.backupName, loaded.backupName);
            Assert.AreEqual(original.state, loaded.state);
            Assert.AreEqual(original.importEpoch, loaded.importEpoch);
            Assert.AreEqual(original.failure, loaded.failure);
            Assert.AreEqual(-1, loaded.documents[0].fields[0].entries[0].key);
            Assert.AreEqual(
                "{fileID: 0}",
                loaded.documents[0].fields[0].entries[0].references[0].address
            );
            Assert.AreEqual(
                string.Empty,
                loaded.documents[0].fields[0].entries[0].references[0].typeIdentity
            );
        }

        [TestCase(9007199254740993L)]
        [TestCase(long.MaxValue)]
        [TestCase(long.MinValue)]
        public void FileIdentifiersAndDeadlinesRetainExactInt64Precision(long identifier)
        {
            OdinMigrationApplyRecord record = Create(_root);
            OdinMigrationApplyRecord.Document document = record.documents[0];
            document.fileId = identifier;
            record.documents[0] = document;
            long deadline = record.deadlineUtcTicks;
            record.Write(_root);
            Assert.IsTrue(
                OdinMigrationApplyRecord.TryRead(
                    _root,
                    out OdinMigrationApplyRecord loaded,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(identifier, loaded.documents[0].fileId);
            Assert.AreEqual(deadline, loaded.deadlineUtcTicks);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SoleAffectedSceneRestoresWhileTemporaryEditsRemainProtected(bool editTemporary)
        {
            SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
            for (int index = 0; index < SceneManager.sceneCount; ++index)
            {
                Assert.IsFalse(SceneManager.GetSceneAt(index).isDirty);
            }
            string folder = "Assets/OdinSceneScope-" + Guid.NewGuid().ToString("N");
            string scenePath = folder + "/Saved.unity";
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(
                    AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder))
                )
            );
            try
            {
                Scene saved = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single
                );
                Assert.IsTrue(EditorSceneManager.SaveScene(saved, scenePath));
                OdinMigrationApplyRecord record = Create(_root);
                record.affectedPaths = new[] { scenePath };
                record.originalActiveScene = scenePath;
                OdinMigrationApplySceneScope.CaptureLoadedScenes(record);
                record.Write(_root);
                OdinMigrationApplySceneScope.CloseLoadedScenes(record, _root);
                Scene temporary = SceneManager.GetSceneByName(record.temporaryScene);
                Assert.IsTrue(temporary.IsValid());
                Assert.IsFalse(
                    temporary.isDirty,
                    "The newly created empty migration scene must not block its own transaction."
                );
                if (editTemporary)
                {
                    EditorSceneManager.MarkSceneDirty(temporary);
                    Assert.Throws<IOException>(() =>
                        OdinMigrationApplySceneScope.ValidateEditorState(record.affectedPaths)
                    );
                    Assert.Throws<IOException>(() =>
                        OdinMigrationApplySceneScope.Complete(record, _root)
                    );
                    Assert.IsTrue(temporary.IsValid());
                }
                else
                {
                    Assert.DoesNotThrow(() =>
                        OdinMigrationApplySceneScope.ValidateEditorState(record.affectedPaths)
                    );
                    OdinMigrationApplySceneScope.Complete(record, _root);
                    Assert.IsTrue(SceneManager.GetSceneByPath(scenePath).isLoaded);
                    Assert.AreEqual(scenePath, SceneManager.GetActiveScene().path);
                    Assert.IsFalse(SceneManager.GetSceneByName(record.temporaryScene).IsValid());
                }
            }
            finally
            {
                bool savedSetup = true;
                foreach (SceneSetup setup in previous)
                {
                    savedSetup = savedSetup && !string.IsNullOrWhiteSpace(setup.path);
                }
                if (savedSetup && previous.Length != 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previous);
                }
                else
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void ChangedTruncatedForeignAndExcessiveRecordsAreRejected(int damage)
        {
            Create(_root).Write(_root);
            string path = System.IO.Path.Combine(_root, OdinMigrationApplyRecord.FileName);
            string text = File.ReadAllText(path);
            if (damage == 0)
            {
                File.WriteAllText(path, text + " ");
            }
            else if (damage == 1)
            {
                File.WriteAllText(path, text.Substring(0, text.Length - 1));
            }
            else if (damage == 2)
            {
                File.WriteAllText(
                    path,
                    text.Replace("WallstopOdinDictionaryApply:2", "WallstopOdinDictionaryApply:3")
                );
            }
            else if (damage == 3)
            {
                string json = new string('[', 17) + new string(']', 17);
                File.WriteAllText(
                    path,
                    "WallstopOdinDictionaryApply:1\n"
                        + OdinMigrationBackupReceipt.Hash(Encoding.UTF8.GetBytes(json))
                        + "\n"
                        + json
                );
            }
            else
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Write))
                {
                    stream.SetLength(OdinMigrationApplyRecord.MaximumBytes + 1);
                }
            }
            Assert.IsFalse(
                OdinMigrationApplyRecord.TryRead(
                    _root,
                    out OdinMigrationApplyRecord loaded,
                    out string failure
                )
            );
            Assert.IsTrue(loaded == null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure));
        }

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        public void RecoveryVersionsPreserveLiveCapturePolicy(int version, bool liveCaptured)
        {
            OdinMigrationApplyRecord record = Create(_root);
            record.version = version;
            record.documents[0].liveCaptured = liveCaptured;
            record.Write(_root);

            Assert.IsTrue(
                OdinMigrationApplyRecord.TryRead(
                    _root,
                    out OdinMigrationApplyRecord loaded,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(version, loaded.version);
            Assert.AreEqual(liveCaptured, loaded.documents[0].liveCaptured);
        }

        [Test]
        public void LegacyRecordsCannotClaimLiveDictionaryRecovery()
        {
            OdinMigrationApplyRecord record = Create(_root);
            record.version = 1;
            record.documents[0].liveCaptured = true;

            Assert.Throws<IOException>(() => record.Write(_root));
        }

        [TestCase(1, 2)]
        [TestCase(2, 1)]
        public void RecordHeadersMustMatchTheirSerializedVersions(int original, int changed)
        {
            OdinMigrationApplyRecord record = Create(_root);
            record.version = original;
            record.Write(_root);
            string path = System.IO.Path.Combine(_root, OdinMigrationApplyRecord.FileName);
            string text = File.ReadAllText(path)
                .Replace(
                    "WallstopOdinDictionaryApply:" + original,
                    "WallstopOdinDictionaryApply:" + changed
                );
            File.WriteAllText(path, text);

            Assert.IsFalse(
                OdinMigrationApplyRecord.TryRead(
                    _root,
                    out OdinMigrationApplyRecord _,
                    out string failure
                )
            );
            StringAssert.Contains("version", failure);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void IncompleteIdentitiesAndUninitializedStatesAreRejectedBeforeSaving(int invalid)
        {
            OdinMigrationApplyRecord record = Create(_root);
            if (invalid == 0)
                record.state = (OdinMigrationApplyState)0;
            else if (invalid == 1)
                record.documents = Array.Empty<OdinMigrationApplyRecord.Document>();
            else if (invalid == 2)
                record.projectHash = new string('0', 64);
            else if (invalid == 3)
                record.files = null;
            else if (invalid == 4)
                record.importEpoch = "invalid";
            else
                record.openedScenes = new[] { "Assets/../outside.unity" };
            Assert.Throws<IOException>(() => record.Write(_root));
            Assert.IsFalse(
                File.Exists(System.IO.Path.Combine(_root, OdinMigrationApplyRecord.FileName))
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void AmbiguousDocumentAndFieldIdentitiesAreRejectedBeforeSaving(int invalid)
        {
            OdinMigrationApplyRecord record = Create(_root);
            OdinMigrationApplyRecord.Document document = record.documents[0];
            if (invalid == 0)
            {
                document.fileId = 0;
            }
            else if (invalid == 1)
            {
                document.scriptGuid = "not-a-guid";
            }
            else if (invalid == 2)
            {
                document.owningGuid = "not-a-guid";
            }
            else if (invalid == 3)
            {
                record.files = new[] { record.files[0], record.files[0] };
            }
            else if (invalid == 4)
            {
                record.documents = new[] { document, document };
            }
            else if (invalid == 5)
            {
                document.fields = new[] { document.fields[0], document.fields[0] };
            }
            else
            {
                OdinMigrationApplyRecord.Field field = document.fields[0];
                field.entries = new[] { field.entries[0], field.entries[0] };
                document.fields[0] = field;
            }
            record.documents[0] = document;
            Assert.Throws<IOException>(() => record.Write(_root));
            Assert.IsFalse(
                File.Exists(System.IO.Path.Combine(_root, OdinMigrationApplyRecord.FileName))
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ChangedOrDeletedJournalIsNeverOverwritten(bool delete)
        {
            OdinMigrationApplyRecord record = Create(_root);
            record.Write(_root);
            string path = System.IO.Path.Combine(_root, OdinMigrationApplyRecord.FileName);
            if (delete)
            {
                File.Delete(path);
            }
            else
            {
                File.WriteAllText(path, "External edit", new UTF8Encoding(false));
            }
            record.state = OdinMigrationApplyState.RecoveryRequested;
            Assert.Catch<IOException>(() => record.ValidateCurrent(_root));
            Assert.Catch<IOException>(() => record.Write(_root));
            if (delete)
            {
                Assert.IsFalse(File.Exists(path));
            }
            else
            {
                Assert.AreEqual("External edit", File.ReadAllText(path));
            }
        }
    }
}
