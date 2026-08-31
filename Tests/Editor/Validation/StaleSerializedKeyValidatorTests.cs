// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes;

    /// <summary>
    /// Pins which keys count as live, which count as stale, and which are not judged at all.
    /// </summary>
    /// <remarks>
    /// The alias case is the one that makes this dangerous rather than merely noisy. Judging by
    /// <c>SerializedObject</c> alone was measured reporting 565 <c>FormerlySerializedAs</c> aliases
    /// doing their job as orphans, and a reader acting on that report deletes live data.
    /// </remarks>
    [TestFixture]
    public sealed class StaleSerializedKeyValidatorTests
    {
        [SetUp]
        public void ResolveFixtureScript()
        {
            MonoScriptIndex.ClearCaches();
            Assert.IsTrue(
                MonoScriptIndex.TryGetScriptGuid(
                    typeof(AuthoredRequirementTestAsset),
                    out _scriptGuid
                )
            );
            Assert.IsTrue(
                MonoScriptIndex.TryGetScriptPath(
                    typeof(AuthoredRequirementTestAsset),
                    out string scriptPath
                )
            );

            int marker = scriptPath.IndexOf(TestTypesFolder, StringComparison.Ordinal);
            Assert.IsTrue(0 <= marker, scriptPath);
            _committedEmpty =
                $"{scriptPath.Substring(0, marker)}/TestAssets/EmptyRequirements.asset";

            _root = Path.Combine(Path.GetTempPath(), $"stale-serialized-key-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void DeleteScratchAssets()
        {
            MonoScriptIndex.ClearCaches();
            if (!string.IsNullOrEmpty(_root) && Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            _root = null;
            _scriptGuid = null;
            _committedEmpty = null;
        }

        [Test]
        public void ACommittedAssetOfALiveTypeHasNoStaleKeys()
        {
            List<StaleSerializedKeyFinding> findings = new();

            Assert.IsTrue(
                StaleSerializedKeyValidator.TryScan(
                    new[] { _committedEmpty },
                    findings,
                    out int unresolvedScripts
                )
            );

            Assert.AreEqual(0, unresolvedScripts);
            CollectionAssert.IsEmpty(
                findings.Select(finding => finding.ToString()).ToArray(),
                "Unity's own keys and every FormerlySerializedAs alias are live keys."
            );
        }

        [Test]
        public void TheEngineHeaderOfAScriptableObjectAssetIsNotStale()
        {
            string assetPath = WriteAsset("Header", _scriptGuid, new[] { "  weight: 1" });

            List<StaleSerializedKeyFinding> findings = new();
            Assert.IsTrue(
                StaleSerializedKeyValidator.TryScan(new[] { assetPath }, findings, out int _)
            );

            CollectionAssert.IsEmpty(
                findings.Select(finding => finding.Key).ToArray(),
                "A ScriptableObject asset carries the full MonoBehaviour header, and a "
                    + "SerializedObject over a ScriptableObject reports none of it."
            );
            CollectionAssert.IsSubsetOf(
                new[] { "m_GameObject", "m_Enabled", "m_EditorHideFlags" },
                StaleSerializedKeyValidator.UnityOwnedKeys.ToArray()
            );
        }

        [Test]
        public void OnlyTheKeysNoFieldClaimsAreReported()
        {
            string assetPath = WriteAsset(
                "Stale",
                _scriptGuid,
                new[]
                {
                    "  requiredMaterial: {fileID: 0}",
                    "  requiredName: sample",
                    "  requiredMaterials: []",
                    "  legacyIcon: {fileID: 0}",
                    "  optionalMaterial: {fileID: 0}",
                    "  weight: 1",
                    "  serializationData:",
                    "    SerializedFormat: 2",
                    "  relatedLibraryObject: {fileID: 0}",
                }
            );

            List<StaleSerializedKeyFinding> findings = new();
            Assert.IsTrue(
                StaleSerializedKeyValidator.TryScan(new[] { assetPath }, findings, out int _)
            );

            CollectionAssert.AreEquivalent(
                new[] { "serializationData", "relatedLibraryObject" },
                findings.Select(finding => finding.Key).ToArray(),
                string.Join(Environment.NewLine, findings.Select(finding => finding.ToString()))
            );
            Assert.IsTrue(
                findings.All(finding => finding.OwnerType == typeof(AuthoredRequirementTestAsset))
            );
        }

        [Test]
        public void ADocumentWhoseScriptResolvesToNothingIsCountedRatherThanReported()
        {
            string assetPath = WriteAsset(
                "Missing",
                "ffffffffffffffffffffffffffffffff",
                new[] { "  anythingAtAll: 1", "  andAnother: 2" }
            );

            List<StaleSerializedKeyFinding> findings = new();
            Assert.IsTrue(
                StaleSerializedKeyValidator.TryScan(
                    new[] { assetPath },
                    findings,
                    out int unresolvedScripts
                )
            );

            Assert.AreEqual(1, unresolvedScripts);
            CollectionAssert.IsEmpty(
                findings.Select(finding => finding.ToString()).ToArray(),
                "Guessing at a missing script's keys reports all of them."
            );
        }

        [Test]
        public void OneRetiredFieldAcrossManyAssetsIsOneCause()
        {
            List<string> assetPaths = new();
            for (int index = 0; index < 3; ++index)
            {
                assetPaths.Add(
                    WriteAsset(
                        $"Retired{index}",
                        _scriptGuid,
                        new[] { "  weight: 1", "  relatedLibraryObject: {fileID: 0}" }
                    )
                );
            }

            List<StaleSerializedKeyFinding> findings = new();
            Assert.IsTrue(StaleSerializedKeyValidator.TryScan(assetPaths, findings, out int _));

            Assert.AreEqual(3, findings.Count);
            IReadOnlyDictionary<string, int> causes = StaleSerializedKeyValidator.CausesOf(
                findings
            );
            Assert.AreEqual(1, causes.Count);
            Assert.AreEqual(
                3,
                causes.ValueFor(
                    $"{typeof(AuthoredRequirementTestAsset).FullName}::relatedLibraryObject"
                )
            );
        }

        [Test]
        public void OnlyTheTopLevelKeyOfAStaleBlockIsReported()
        {
            string assetPath = WriteAsset(
                "Nested",
                _scriptGuid,
                new[]
                {
                    "  requiredMaterials:",
                    "  - {fileID: 0}",
                    "  weight: 1",
                    "  serializationData:",
                    "    SerializedFormat: 2",
                    "    SerializedBytes: ",
                }
            );

            List<StaleSerializedKeyFinding> findings = new();
            StaleSerializedKeyValidator.TryScan(new[] { assetPath }, findings, out int _);

            CollectionAssert.AreEquivalent(
                new[] { "serializationData" },
                findings.Select(finding => finding.Key).ToArray(),
                string.Join(Environment.NewLine, findings.Select(finding => finding.ToString()))
            );
        }

        [Test]
        public void AScanWithNothingToReadIsRefusedRatherThanReportedClean()
        {
            List<StaleSerializedKeyFinding> findings = new();

            Assert.IsFalse(StaleSerializedKeyValidator.TryScan(null, findings, out int _));
            Assert.IsFalse(
                StaleSerializedKeyValidator.TryScan(Array.Empty<string>(), null, out int _)
            );
            CollectionAssert.IsEmpty(StaleSerializedKeyValidator.CausesOf(null));
        }

        [Test]
        public void AnAssetThatCannotBeReadIsRefusedRatherThanRewritten()
        {
            Assert.AreEqual(
                StaleSerializedKeyRepairOutcome.RefusedUnreadable,
                StaleSerializedKeyRepair.RepairAsset(null)
            );
            Assert.AreEqual(
                StaleSerializedKeyRepairOutcome.RefusedUnreadable,
                StaleSerializedKeyRepair.RepairAsset(Path.Combine(_root, "absent.asset"))
            );

            Dictionary<string, StaleSerializedKeyRepairOutcome> outcomes = new();
            Assert.IsFalse(StaleSerializedKeyRepair.TryRepair(null, outcomes));
            Assert.IsFalse(StaleSerializedKeyRepair.TryRepair(Array.Empty<string>(), null));
        }

        private string WriteAsset(string name, string scriptGuid, IReadOnlyList<string> body)
        {
            List<string> lines = new()
            {
                "%YAML 1.1",
                "%TAG !u! tag:unity3d.com,2011:",
                "--- !u!114 &11400000",
                "MonoBehaviour:",
                "  m_ObjectHideFlags: 0",
                "  m_CorrespondingSourceObject: {fileID: 0}",
                "  m_PrefabInstance: {fileID: 0}",
                "  m_PrefabAsset: {fileID: 0}",
                "  m_GameObject: {fileID: 0}",
                "  m_Enabled: 1",
                "  m_EditorHideFlags: 0",
                $"  m_Script: {{fileID: 11500000, guid: {scriptGuid}, type: 3}}",
                $"  m_Name: {name}",
                "  m_EditorClassIdentifier: ",
            };
            lines.AddRange(body);

            string assetPath = Path.Combine(_root, $"{name}.asset");
            File.WriteAllLines(assetPath, lines);
            return assetPath;
        }

        private const string TestTypesFolder = "/TestTypes/";

        private string _root;
        private string _scriptGuid;
        private string _committedEmpty;
    }
}
