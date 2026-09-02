// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// UNH-SUPPRESS UNH003: every object built here becomes an ASSET whose lifetime is the fixture
// folder, deleted whole in OneTimeTearDown. CommonTestBase destroys what it tracks after each test,
// which would delete the subject the next test is about to repair.
namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Runs the repair's rewrite for real, against subjects this fixture authors and deletes.
    /// </summary>
    /// <remarks>
    /// The refusal branches were the only covered ones, and they refuse before touching anything.
    /// What was unproven is the half that matters: <c>ForceReserializeAssets</c> runs, the non-null
    /// object count is compared before and after, and any rewrite that lowers it is undone. A
    /// <c>VolumeProfile</c> was measured going from twenty serialized documents to one while the
    /// rewrite reported success, so a guard against that which has never run is a comment.
    /// Nothing committed is ever a subject here: every asset is built under <c>Assets/</c> in
    /// <see cref="BuildSubjects"/> and the whole folder is deleted in <see cref="DeleteSubjects"/>.
    /// </remarks>
    [TestFixture]
    public sealed class StaleSerializedKeyRepairTests
    {
        [OneTimeSetUp]
        public void BuildSubjects()
        {
            _folderName = $"UnityHelpersRepairFixture{Guid.NewGuid():N}";
            _folder = $"{AssetsRoot}/{_folderName}";
            Assert.IsFalse(
                string.IsNullOrEmpty(AssetDatabase.CreateFolder(AssetsRoot, _folderName)),
                _folder
            );

            _plain = CreateStaleKeyAsset("PlainStaleKey");
            _subObjects = CreateStaleKeySubObjectAsset("SubObjects");
            _undo = CreateStaleKeySubObjectAsset("UndoSecondHalf");
            _lostSubObjects = CreateStaleKeySubObjectAsset("LostSubObjects");
            _undoFailed = CreateStaleKeySubObjectAsset("UndoFailed");
            _rewriteThrew = CreateStaleKeySubObjectAsset("RewriteThrew");
            _prefab = CreateStaleKeyPrefab("StaleKeyPrefab");
            _prefabControl = CreateStaleKeyPrefab("StaleKeyPrefabControl");
        }

        [OneTimeTearDown]
        public void DeleteSubjects()
        {
            if (!string.IsNullOrEmpty(_folder))
            {
                AssetDatabase.DeleteAsset(_folder);
                AssetDatabase.Refresh();
            }

            _folderName = null;
            _folder = null;
            _plain = null;
            _subObjects = null;
            _undo = null;
            _lostSubObjects = null;
            _undoFailed = null;
            _rewriteThrew = null;
            _prefab = null;
            _prefabControl = null;
        }

        [Test]
        public void APlainAssetComesBackWithoutTheKeyNoFieldClaims()
        {
            Assert.IsTrue(
                TextOf(_plain).Contains(StaleKey, StringComparison.Ordinal),
                "The subject carries no stale key, so a clean result would measure nothing."
            );
            int before = NonNullObjectCount(_plain);

            StaleSerializedKeyRepairOutcome outcome = StaleSerializedKeyRepair.RepairAsset(_plain);

            Assert.AreEqual(StaleSerializedKeyRepairOutcome.Repaired, outcome, _plain);
            Assert.IsFalse(TextOf(_plain).Contains(StaleKey, StringComparison.Ordinal));
            Assert.AreEqual(before, NonNullObjectCount(_plain));
        }

        [Test]
        public void AnAssetWhoseContentLivesInSubObjectsNeverComesBackWithFewer()
        {
            int before = NonNullObjectCount(_subObjects);
            Assert.AreEqual(
                1 + SubObjectCount,
                before,
                "The subject has no sub-objects, so the branch this test exists for cannot be reached."
            );

            byte[] original = File.ReadAllBytes(AuthoredAssetPaths.ToFileSystemPath(_subObjects));
            StaleSerializedKeyRepairOutcome outcome = StaleSerializedKeyRepair.RepairAsset(
                _subObjects
            );
            int after = NonNullObjectCount(_subObjects);

            Assert.IsTrue(
                before <= after,
                $"{_subObjects} went in with {before} objects and came back with {after}. That is "
                    + "the loss the guard exists to undo, and it was not undone."
            );

            if (outcome == StaleSerializedKeyRepairOutcome.RefusedLostSubObjects)
            {
                CollectionAssert.AreEqual(
                    original,
                    File.ReadAllBytes(AuthoredAssetPaths.ToFileSystemPath(_subObjects)),
                    "A refusal that leaves the rewritten bytes on disk is the damage, not the undo."
                );
                return;
            }

            Assert.AreEqual(StaleSerializedKeyRepairOutcome.Repaired, outcome, _subObjects);
            Assert.IsFalse(TextOf(_subObjects).Contains(StaleKey, StringComparison.Ordinal));
        }

        /// <summary>
        /// Pins the option the repair passes: with assets only, a prefab is not rewritten at all.
        /// </summary>
        /// <remarks>
        /// Measured on editor 6000.4.6f1, and previously on ten project prefabs that read as "these
        /// had no stale keys". The control runs first so the subject's pass cannot be read as
        /// "any rewrite reaches everything".
        /// </remarks>
        [Test]
        public void APrefabIsRewrittenOnlyBecauseTheRepairAsksForMetadataToo()
        {
            string controlFile = AuthoredAssetPaths.ToFileSystemPath(_prefabControl);
            byte[] beforeControl = File.ReadAllBytes(controlFile);

            AssetDatabase.ForceReserializeAssets(
                new[] { _prefabControl },
                ForceReserializeAssetsOptions.ReserializeAssets
            );

            CollectionAssert.AreEqual(
                beforeControl,
                File.ReadAllBytes(controlFile),
                "Assets-only reserializing rewrote a prefab, so the repair no longer needs the "
                    + "metadata option to reach one."
            );
            Assert.IsTrue(
                TextOf(_prefabControl).Contains(StaleKey, StringComparison.Ordinal),
                _prefabControl
            );

            int before = NonNullObjectCount(_prefab);
            StaleSerializedKeyRepairOutcome outcome = StaleSerializedKeyRepair.RepairAsset(_prefab);

            Assert.AreEqual(StaleSerializedKeyRepairOutcome.Repaired, outcome, _prefab);
            Assert.IsFalse(TextOf(_prefab).Contains(StaleKey, StringComparison.Ordinal));
            Assert.AreEqual(before, NonNullObjectCount(_prefab));
        }

        /// <summary>
        /// Pins the second half of the undo: the editor still holds the object a repair touched.
        /// </summary>
        /// <remarks>
        /// Putting the original bytes back is only half of it. An asset broke a second time minutes
        /// after a <c>git checkout</c>, because the editor kept the damaged object and the next save
        /// wrote it straight back out. The other half is the forced synchronous re-import.
        /// </remarks>
        [Test]
        public void SavingTheObjectTheEditorStillHoldsDoesNotBreakTheFileAgain()
        {
            int before = NonNullObjectCount(_undo);
            StaleSerializedKeyRepairOutcome outcome = StaleSerializedKeyRepair.RepairAsset(_undo);
            Assert.AreNotEqual(
                StaleSerializedKeyRepairOutcome.RefusedUndoFailed,
                outcome,
                "The rewrite happened and putting the original bytes back did not, so the file on "
                    + "disk holds the rewritten content."
            );

            Object held = AssetDatabase.LoadMainAssetAtPath(_undo);
            Assert.IsTrue(held != null, _undo);

            EditorUtility.SetDirty(held);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                _undo,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
            );

            Assert.AreEqual(
                before,
                NonNullObjectCount(_undo),
                "Saving what the editor still held wrote the file back out with less in it, which "
                    + "is the failure a restored file alone does not prevent."
            );
        }

        /// <summary>
        /// Pins the branch the guard exists for: a rewrite that loses content is undone whole.
        /// </summary>
        /// <remarks>
        /// Nothing a test can author makes <c>ForceReserializeAssets</c> lose anything -- a
        /// <c>VolumeProfile</c> went from twenty serialized documents to one while the rewrite
        /// reported success, and five modelled <c>HideFlags</c> shapes reproduced none of it. So the
        /// loss is supplied through the counter seam and everything after it is production code: the
        /// comparison, the restore, and the forced re-import. The subject is damaged for real, in
        /// both places a real loss damages it, so both halves of the undo have something to reverse.
        /// The weight assertion is not vacuous: measured on 6000.4.6f1, writing the original bytes
        /// back <em>without</em> the re-import leaves the editor holding the damaged value, so
        /// removing the re-import turns this test red.
        /// </remarks>
        [Test]
        public void ARewriteThatLosesObjectsIsUndoneInBothHalves()
        {
            string filePath = AuthoredAssetPaths.ToFileSystemPath(_lostSubObjects);
            byte[] original = File.ReadAllBytes(filePath);
            Assert.AreEqual(
                1 + SubObjectCount,
                NonNullObjectCount(_lostSubObjects),
                "The subject has no sub-objects, so a loss cannot be modelled against it."
            );

            int counts = 0;
            StaleSerializedKeyRepairOutcome outcome = StaleSerializedKeyRepair.RepairAsset(
                _lostSubObjects,
                assetPath =>
                {
                    int held = NonNullObjectCount(assetPath);
                    ++counts;
                    if (counts < 2)
                    {
                        return held;
                    }

                    DamageInPlace(assetPath);
                    return held - 1;
                }
            );

            Assert.AreEqual(2, counts, "The rewrite was never counted on both sides of itself.");
            Assert.AreEqual(
                StaleSerializedKeyRepairOutcome.RefusedLostSubObjects,
                outcome,
                _lostSubObjects
            );
            CollectionAssert.AreEqual(
                original,
                File.ReadAllBytes(filePath),
                "The refusal left the damaged bytes on disk, which is the damage rather than the undo."
            );

            AuthoredRequirementTestAsset restored =
                AssetDatabase.LoadAssetAtPath<AuthoredRequirementTestAsset>(_lostSubObjects);
            Assert.IsTrue(restored != null, _lostSubObjects);
            Assert.AreEqual(
                0,
                restored.weight,
                "The bytes came back and the editor kept the damaged object, so the next save writes "
                    + "the damage straight back out. That is the half a restored file alone misses."
            );
        }

        /// <summary>
        /// Pins the worst outcome: the rewrite happened and putting the original back did not.
        /// </summary>
        /// <remarks>
        /// The write is made to fail by putting a directory where the file was, which fails on every
        /// platform and for every user -- a read-only attribute does not stop a process running as
        /// root, which is how the package's own containers run.
        /// </remarks>
        [Test]
        public void AnUndoThatCannotWriteIsReportedRatherThanSwallowed()
        {
            string filePath = AuthoredAssetPaths.ToFileSystemPath(_undoFailed);
            byte[] original = File.ReadAllBytes(filePath);
            LogAssert.Expect(
                LogType.Error,
                new Regex(Regex.Escape($"Could not undo the rewrite of {_undoFailed}"))
            );

            try
            {
                int counts = 0;
                StaleSerializedKeyRepairOutcome outcome = StaleSerializedKeyRepair.RepairAsset(
                    _undoFailed,
                    assetPath =>
                    {
                        int held = NonNullObjectCount(assetPath);
                        ++counts;
                        if (counts < 2)
                        {
                            return held;
                        }

                        File.Delete(filePath);
                        Directory.CreateDirectory(filePath);
                        return held - 1;
                    }
                );

                Assert.AreEqual(
                    StaleSerializedKeyRepairOutcome.RefusedUndoFailed,
                    outcome,
                    "An undo that could not write must not report the same refusal as one that did."
                );
            }
            finally
            {
                if (Directory.Exists(filePath))
                {
                    Directory.Delete(filePath, true);
                }

                File.WriteAllBytes(filePath, original);
                AssetDatabase.ImportAsset(
                    _undoFailed,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
                );
            }
        }

        /// <summary>
        /// Pins that a rewrite which throws is not reported as the asset being unreadable.
        /// </summary>
        /// <remarks>
        /// Every guard <c>RefusedUnreadable</c> describes has already passed by the time the rewrite
        /// runs: the file existed, its bytes were read, and its objects loaded. Reporting it there
        /// sent a human at permissions and paths while the real failure was Unity's rewrite call.
        /// Nothing a test can author makes <c>ForceReserializeAssets</c> throw, so the throw is
        /// supplied through the rewrite seam and everything after it -- the log, the byte restore
        /// and the forced re-import -- is production code
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/679">#679</see>).
        /// </remarks>
        [Test]
        public void ARewriteThatThrowsIsNotReportedAsUnreadable()
        {
            string filePath = AuthoredAssetPaths.ToFileSystemPath(_rewriteThrew);
            byte[] original = File.ReadAllBytes(filePath);
            LogAssert.Expect(
                LogType.Error,
                new Regex(Regex.Escape($"Rewriting {_rewriteThrew} threw: {RewriteFailureMessage}"))
            );

            int rewrites = 0;
            StaleSerializedKeyRepairOutcome outcome = StaleSerializedKeyRepair.RepairAsset(
                _rewriteThrew,
                null,
                _ =>
                {
                    ++rewrites;
                    throw new InvalidOperationException(RewriteFailureMessage);
                }
            );

            Assert.AreEqual(1, rewrites, "The rewrite seam never ran, so nothing threw.");
            Assert.AreEqual(
                StaleSerializedKeyRepairOutcome.RefusedRewriteThrew,
                outcome,
                "A rewrite that threw must not report the outcome that means the file could not be read."
            );
            CollectionAssert.AreEqual(
                original,
                File.ReadAllBytes(filePath),
                "The refusal did not put the original bytes back."
            );
        }

        /// <summary>
        /// Pins that the three entry guards still mean what <c>RefusedUnreadable</c>'s summary says.
        /// </summary>
        /// <remarks>
        /// The control for the test above: moving the rewrite failure off this outcome is only
        /// correct if the outcome still has callers, and all three of them are guards that run
        /// before anything is read or rewritten.
        /// </remarks>
        [Test]
        [TestCase(null, TestName = "RefusedUnreadableStillMeansUnreadableNoPath")]
        [TestCase("", TestName = "RefusedUnreadableStillMeansUnreadableEmptyPath")]
        public void RefusedUnreadableStillMeansUnreadable(string assetPath)
        {
            Assert.AreEqual(
                StaleSerializedKeyRepairOutcome.RefusedUnreadable,
                StaleSerializedKeyRepair.RepairAsset(assetPath)
            );
        }

        /// <summary>Pins the third entry guard: an asset that loads nothing is refused.</summary>
        [Test]
        public void AnAssetThatLoadsNoObjectsIsRefusedAsUnreadable()
        {
            int counts = 0;
            StaleSerializedKeyRepairOutcome outcome = StaleSerializedKeyRepair.RepairAsset(
                _rewriteThrew,
                _ =>
                {
                    ++counts;
                    return 0;
                },
                _ => Assert.Fail("The rewrite ran for an asset that loaded nothing.")
            );

            Assert.AreEqual(1, counts, "The count seam never ran, so the guard was not reached.");
            Assert.AreEqual(StaleSerializedKeyRepairOutcome.RefusedUnreadable, outcome);
        }

        /// <summary>Damages the asset on disk and in the editor, the way a real loss does.</summary>
        /// <param name="assetPath">The asset to damage.</param>
        private static void DamageInPlace(string assetPath)
        {
            AuthoredRequirementTestAsset subject =
                AssetDatabase.LoadAssetAtPath<AuthoredRequirementTestAsset>(assetPath);
            Assert.IsTrue(subject != null, assetPath);

            subject.weight = DamagedWeight;
            EditorUtility.SetDirty(subject);
            AssetDatabase.SaveAssets();
        }

        private string CreateStaleKeyAsset(string name)
        {
            string assetPath = $"{_folder}/{name}.asset";
            AssetDatabase.CreateAsset(NewSubject(name), assetPath);
            AssetDatabase.SaveAssets();
            InjectStaleKey(assetPath);
            return assetPath;
        }

        private string CreateStaleKeySubObjectAsset(string name)
        {
            string assetPath = $"{_folder}/{name}.asset";
            AuthoredRequirementTestAsset root = NewSubject(name);
            AssetDatabase.CreateAsset(root, assetPath);

            for (int index = 0; index < SubObjectCount; ++index)
            {
                AssetDatabase.AddObjectToAsset(NewSubject($"{name}Child{index}"), root);
            }

            AssetDatabase.SaveAssets();
            InjectStaleKey(assetPath);
            return assetPath;
        }

        /// <summary>Builds one subject, which becomes an asset the fixture folder owns.</summary>
        /// <param name="name">The object name, which is also the asset or sub-asset name.</param>
        /// <returns>The new instance.</returns>
        private static AuthoredRequirementTestAsset NewSubject(string name)
        {
            AuthoredRequirementTestAsset created =
                ScriptableObject.CreateInstance<AuthoredRequirementTestAsset>();
            created.name = name; // UNH-SUPPRESS UNH002: an asset, deleted with the fixture folder.
            return created;
        }

        private string CreateStaleKeyPrefab(string name)
        {
            string assetPath = $"{_folder}/{name}.prefab";
            GameObject host = new(name);
            try
            {
                host.AddComponent<AssignmentComponent>();
                PrefabUtility.SaveAsPrefabAsset(host, assetPath);
            }
            finally
            {
                Object.DestroyImmediate(host); // UNH-SUPPRESS UNH001: a template, never an asset.
            }

            InjectStaleKey(assetPath);
            return assetPath;
        }

        /// <summary>
        /// Leaves a key no field claims in the file's last <c>MonoBehaviour</c> document.
        /// </summary>
        /// <param name="assetPath">The asset to edit, which is then re-imported.</param>
        /// <remarks>
        /// Unity authors the file and the key is injected afterwards, rather than the whole document
        /// being hand-written: the defect is a key left behind in a file Unity wrote, and a
        /// hand-written header would be testing the fixture's YAML instead.
        /// </remarks>
        private static void InjectStaleKey(string assetPath)
        {
            string filePath = AuthoredAssetPaths.ToFileSystemPath(assetPath);
            List<string> lines = new(File.ReadAllLines(filePath));

            int document = -1;
            for (int index = 0; index < lines.Count; ++index)
            {
                if (lines[index].StartsWith(MonoBehaviourDocument, StringComparison.Ordinal))
                {
                    document = index;
                }
            }

            Assert.IsTrue(
                0 <= document,
                $"{assetPath} declares no MonoBehaviour document to leave a stale key in."
            );

            int end = lines.Count;
            for (int index = document + 1; index < lines.Count; ++index)
            {
                if (lines[index].StartsWith(AnyDocument, StringComparison.Ordinal))
                {
                    end = index;
                    break;
                }
            }

            lines.Insert(end, $"  {StaleKey}: 7");
            File.WriteAllLines(filePath, lines);
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport
            );
        }

        private static string TextOf(string assetPath)
        {
            return File.ReadAllText(AuthoredAssetPaths.ToFileSystemPath(assetPath));
        }

        private static int NonNullObjectCount(string assetPath)
        {
            Object[] loaded = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (loaded == null)
            {
                return 0;
            }

            int count = 0;
            foreach (Object candidate in loaded)
            {
                if (candidate != null)
                {
                    ++count;
                }
            }

            return count;
        }

        private const string AssetsRoot = "Assets";
        private const string StaleKey = "staleKeyNoFieldClaims";
        private const string AnyDocument = "--- !u!";
        private const string MonoBehaviourDocument = "--- !u!114 ";
        private const int SubObjectCount = 3;
        private const int DamagedWeight = 41;
        private const string RewriteFailureMessage = "the reserialize refused this asset";

        private string _folderName;
        private string _folder;
        private string _plain;
        private string _subObjects;
        private string _undo;
        private string _lostSubObjects;
        private string _undoFailed;
        private string _rewriteThrew;
        private string _prefab;
        private string _prefabControl;
    }
}
