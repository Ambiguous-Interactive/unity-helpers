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

    /// <summary>
    /// Pins the authored states a <c>SerializableDictionary</c> can be in, healthy ones included.
    /// </summary>
    /// <remarks>
    /// The healthy cases carry the weight. A gate that only proves it reports the broken shapes
    /// would pass just as well if it reported every dictionary in the project, and the boxed shape
    /// -- a dictionary whose value type is a collection, which Unity stores in <c>_boxedValues</c>
    /// because it drops an array of collections -- is exactly the correct asset a naive
    /// "keys without values" rule condemns.
    /// </remarks>
    [TestFixture]
    public sealed class SerializableDictionaryAssetValidatorTests
    {
        [SetUp]
        public void CreateScanRoot()
        {
            _root = Path.Combine(Path.GetTempPath(), $"serializable-dictionary-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void DeleteScanRoot()
        {
            if (!string.IsNullOrEmpty(_root) && Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            _root = null;
        }

        [TestCaseSource(nameof(AuthoredStates))]
        public void AnAuthoredDictionaryIsJudgedByWhatCarriesItsValues(
            string name,
            string[] body,
            int expectedInspected,
            SerializableDictionaryAssetProblem[] expected
        )
        {
            string assetPath = WriteAsset(name, body);
            List<SerializableDictionaryAssetFinding> findings = new();

            Assert.IsTrue(
                SerializableDictionaryAssetValidator.TryScan(
                    new[] { assetPath },
                    findings,
                    out int inspected
                )
            );

            Assert.AreEqual(expectedInspected, inspected);
            CollectionAssert.AreEqual(
                expected,
                findings.Select(finding => finding.Problem).ToArray(),
                string.Join(Environment.NewLine, findings.Select(finding => finding.ToString()))
            );
        }

        [Test]
        public void AFindingNamesTheLineTheEvidenceIsOn()
        {
            string assetPath = WriteAsset(
                "NullValue",
                new[]
                {
                    "  _map:",
                    "    _keys:",
                    "    - Idle",
                    "    - Run",
                    "    _values:",
                    "    - {fileID: 7400000, guid: bbb, type: 3}",
                    "    - {fileID: 0}",
                    "    _boxedValues: []",
                }
            );

            List<SerializableDictionaryAssetFinding> findings = new();
            SerializableDictionaryAssetValidator.TryScan(new[] { assetPath }, findings, out int _);

            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(13, findings[0].LineNumber);
            Assert.AreEqual(assetPath, findings[0].AssetPath);
        }

        [Test]
        public void AScanWithNothingToReadIsRefusedRatherThanReportedClean()
        {
            List<SerializableDictionaryAssetFinding> findings = new();

            Assert.IsFalse(SerializableDictionaryAssetValidator.TryScan(null, findings, out int _));
            Assert.IsFalse(
                SerializableDictionaryAssetValidator.TryScan(Array.Empty<string>(), null, out int _)
            );
        }

        [Test]
        public void AnAssetThatCannotBeReadIsSkippedRatherThanThrowing()
        {
            List<SerializableDictionaryAssetFinding> findings = new();

            Assert.IsTrue(
                SerializableDictionaryAssetValidator.TryScan(
                    new[] { Path.Combine(_root, "absent.asset") },
                    findings,
                    out int inspected
                )
            );
            Assert.AreEqual(0, inspected);
            Assert.AreEqual(0, findings.Count);
        }

        private static IEnumerable<TestCaseData> AuthoredStates()
        {
            yield return new TestCaseData(
                "HealthyEmpty",
                new[] { "  _map:", "    _keys: []", "    _values: []", "    _boxedValues: []" },
                1,
                Array.Empty<SerializableDictionaryAssetProblem>()
            ).SetName("AnEmptyDictionaryIsHealthy");

            yield return new TestCaseData(
                "HealthyFilled",
                new[]
                {
                    "  _map:",
                    "    _keys:",
                    "    - Idle",
                    "    - Run",
                    "    _values:",
                    "    - {fileID: 7400000, guid: bbb, type: 3}",
                    "    - {fileID: 7400000, guid: ccc, type: 3}",
                    "    _boxedValues: []",
                },
                1,
                Array.Empty<SerializableDictionaryAssetProblem>()
            ).SetName("APairedDictionaryIsHealthy");

            yield return new TestCaseData(
                "BoxedShape",
                new[]
                {
                    "  _map:",
                    "    _keys:",
                    "    - Idle",
                    "    - Run",
                    "    _boxedValues:",
                    "    - Data:",
                    "      - {fileID: 7400000, guid: bbb, type: 3}",
                    "    - Data:",
                    "      - {fileID: 7400000, guid: ccc, type: 3}",
                },
                1,
                Array.Empty<SerializableDictionaryAssetProblem>()
            ).SetName("ACollectionValuedDictionaryStoresItsValuesBoxedAndIsHealthy");

            yield return new TestCaseData(
                "PreFixDropped",
                new[] { "  _map:", "    _keys:", "    - Idle", "    - Run" },
                1,
                new[] { SerializableDictionaryAssetProblem.ValuesDropped }
            ).SetName("KeysWithNoCarryingArrayAreReported");

            yield return new TestCaseData(
                "CountMismatch",
                new[]
                {
                    "  _map:",
                    "    _keys:",
                    "    - Idle",
                    "    - Run",
                    "    _values:",
                    "    - {fileID: 7400000, guid: bbb, type: 3}",
                    "    _boxedValues: []",
                },
                1,
                new[] { SerializableDictionaryAssetProblem.ValueCountMismatch }
            ).SetName("AnUnpairableLengthIsReported");

            yield return new TestCaseData(
                "NullValue",
                new[]
                {
                    "  _map:",
                    "    _keys:",
                    "    - Idle",
                    "    - Run",
                    "    _values:",
                    "    - {fileID: 7400000, guid: bbb, type: 3}",
                    "    - {fileID: 0}",
                    "    _boxedValues: []",
                },
                1,
                new[] { SerializableDictionaryAssetProblem.NullValueBesideKey }
            ).SetName("ARealKeyBesideAnEmptySlotIsReported");

            yield return new TestCaseData(
                "TwoSiblings",
                new[]
                {
                    "  _first:",
                    "    _keys:",
                    "    - A",
                    "    _values:",
                    "    - {fileID: 1, guid: b, type: 3}",
                    "    _boxedValues: []",
                    "  _second:",
                    "    _keys:",
                    "    - B",
                    "    - C",
                    "    _values:",
                    "    - {fileID: 0}",
                    "    _boxedValues: []",
                },
                2,
                new[] { SerializableDictionaryAssetProblem.ValueCountMismatch }
            ).SetName("TwoDictionariesUnderOneParentAreJudgedSeparately");

            yield return new TestCaseData(
                "DocumentLevel",
                new[]
                {
                    "  _keys:",
                    "  - Idle",
                    "  _values:",
                    "  - {fileID: 0}",
                    "  _boxedValues: []",
                },
                1,
                new[] { SerializableDictionaryAssetProblem.NullValueBesideKey }
            ).SetName("ADictionaryAtTheDocumentRootIsJudged");

            yield return new TestCaseData(
                "NoDictionary",
                new[] { "  _speed: 5", "  _target: {fileID: 0}" },
                0,
                Array.Empty<SerializableDictionaryAssetProblem>()
            ).SetName("AnAssetWithNoDictionaryIsNotJudged");
        }

        private string WriteAsset(string name, IReadOnlyList<string> body)
        {
            List<string> lines = new()
            {
                "%YAML 1.1",
                "%TAG !u! tag:unity3d.com,2011:",
                "--- !u!114 &11400000",
                "MonoBehaviour:",
                "  m_Script: {fileID: 11500000, guid: aaa, type: 3}",
                $"  m_Name: {name}",
            };
            lines.AddRange(body);

            string assetPath = Path.Combine(_root, $"{name}.asset");
            File.WriteAllLines(assetPath, lines);
            return assetPath;
        }

        private string _root;
    }
}
