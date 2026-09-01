// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Validation;

    /// <summary>
    /// Holds every text scan to naming the files it could not read, rather than passing over them.
    /// </summary>
    /// <remarks>
    /// A file deleted between enumeration and the read stands in for the family -- a permissions
    /// error, a lock, an I/O error on a network drive, an asset saved in binary serialization mode
    /// -- because it is the one case reproducible without special privileges. The subject counts
    /// cannot catch any of them: a scan that reads 3,999 of 4,000 files reports a large count and a
    /// clean result, which is exactly the confident silence these checks exist to break.
    /// </remarks>
    [TestFixture]
    public sealed class UnreadableAssetScanTests
    {
        [SetUp]
        public void CreateScanRoot()
        {
            _root = Path.Combine(Path.GetTempPath(), $"unreadable-asset-{Guid.NewGuid():N}");
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

        [TestCaseSource(nameof(TextScans))]
        public void APathDeletedBeforeTheReadIsReportedRatherThanPassedOver(
            string validator,
            Func<IReadOnlyList<string>, List<string>, bool> scan
        )
        {
            string absent = Path.Combine(_root, "DeletedBeforeTheRead.asset");
            List<string> unreadable = new();

            Assert.IsTrue(scan(new[] { absent }, unreadable), validator);
            CollectionAssert.AreEqual(
                new[] { absent },
                unreadable,
                $"{validator} read nothing and reported clean, which is the failure it exists to "
                    + "prevent turned on itself."
            );
        }

        [TestCaseSource(nameof(TextScans))]
        public void AnAssetThatReadsIsNeverReportedAsUnreadable(
            string validator,
            Func<IReadOnlyList<string>, List<string>, bool> scan
        )
        {
            /*
                The control runs first and has to move. Asserting only that a readable asset is
                absent from the set passes just as well when the scan read nothing at all --
                AuthoredRequirementValidator returns early when no annotated field exists, and a
                define change is enough to make that happen.
            */
            string absent = Path.Combine(_root, "NeverWritten.asset");
            List<string> control = new();
            Assert.IsTrue(scan(new[] { absent }, control), validator);
            CollectionAssert.AreEqual(
                new[] { absent },
                control,
                $"{validator} reports nothing even for a path that does not exist, so the "
                    + "assertion below could not have failed either."
            );

            List<string> unreadable = new();
            Assert.IsTrue(scan(new[] { WriteAsset("Readable") }, unreadable), validator);
            CollectionAssert.IsEmpty(
                unreadable,
                $"{validator} reported a hole in a measurement it actually took."
            );
        }

        [TestCaseSource(nameof(TextScans))]
        public void TheUnreadableSetIsSortedAndNamesEachFileOnce(
            string validator,
            Func<IReadOnlyList<string>, List<string>, bool> scan
        )
        {
            string first = Path.Combine(_root, "AbsentFirst.asset");
            string second = Path.Combine(_root, "AbsentSecond.asset");
            List<string> unreadable = new();

            Assert.IsTrue(scan(new[] { second, first, second }, unreadable), validator);
            CollectionAssert.AreEqual(new[] { first, second }, unreadable, validator);
        }

        [TestCaseSource(nameof(TextScans))]
        public void AScanWithNowhereToReportWhatItCouldNotReadIsRefused(
            string validator,
            Func<IReadOnlyList<string>, List<string>, bool> scan
        )
        {
            Assert.IsFalse(scan(Array.Empty<string>(), null), validator);
        }

        [Test]
        public void AnUnreadableFileIsReportedBesideTheFindingsRatherThanAsOne()
        {
            string absent = Path.Combine(_root, "DeletedBeforeTheRead.asset");
            string broken = WriteAsset("Broken");
            List<SerializableDictionaryAssetFinding> findings = new();
            List<string> unreadable = new();

            Assert.IsTrue(
                SerializableDictionaryAssetValidator.TryScan(
                    new[] { absent, broken },
                    findings,
                    unreadable,
                    out int dictionariesInspected
                )
            );

            Assert.AreEqual(1, dictionariesInspected);
            CollectionAssert.AreEqual(new[] { absent }, unreadable);
            CollectionAssert.AreEqual(
                new[] { broken },
                findings.Select(finding => finding.AssetPath).ToArray(),
                "A file nobody could open is a hole in the measurement, not a defect in the asset, "
                    + "so folding it into the findings would make a finding mean two things."
            );
        }

        [Test]
        public void TheOverloadWithoutAnUnreadableListStillReportsWhatItRead()
        {
            string absent = Path.Combine(_root, "DeletedBeforeTheRead.asset");
            string[] paths = { absent, WriteAsset("Broken") };

            List<SerializableDictionaryAssetFinding> dictionaryFindings = new();
            Assert.IsTrue(
                SerializableDictionaryAssetValidator.TryScan(
                    paths,
                    dictionaryFindings,
                    out int dictionariesInspected
                )
            );
            Assert.AreEqual(1, dictionariesInspected);
            Assert.AreEqual(1, dictionaryFindings.Count);

            List<StaleSerializedKeyFinding> staleFindings = new();
            Assert.IsTrue(
                StaleSerializedKeyValidator.TryScan(paths, staleFindings, out int unresolvedScripts)
            );
            Assert.AreEqual(1, unresolvedScripts);

            List<AuthoredRequirementFinding> requirementFindings = new();
            List<AuthoredRequirementExemption> exemptions = new();
            Assert.IsTrue(
                AuthoredRequirementValidator.TryScan(
                    paths,
                    requirementFindings,
                    exemptions,
                    out int _
                )
            );
            CollectionAssert.IsEmpty(requirementFindings);
        }

        [Test]
        public void AReportNamesEveryFileItCouldNotRead()
        {
            StringBuilder message = new();
            message.Append("authored dictionaries: 0 findings");
            UnreadableAssetPaths.Append(
                message,
                new[] { "Assets/Locked.asset", "Assets/Binary.prefab" }
            );

            string report = message.ToString();
            StringAssert.Contains("2 file(s) could not be read", report);
            StringAssert.Contains("Assets/Locked.asset", report);
            StringAssert.Contains("Assets/Binary.prefab", report);
        }

        /*
            The severity is the half that decides whether anybody keeps reading the console. Unity
            writes LightingData.asset as binary whatever the serialization mode says -- measured on
            two of two under ForceText -- so any project with baked lighting names one on every run.
            Warning on the set alone would make that project's console permanently yellow for
            something nobody can fix.
        */
        [TestCase(0, 0, false, TestName = "{m}.NothingFoundAndNothingMissedIsNotAWarning")]
        [TestCase(0, 2, false, TestName = "{m}.AHoleInTheMeasurementAloneIsNotAWarning")]
        [TestCase(1, 0, true, TestName = "{m}.AFindingIsAWarning")]
        [TestCase(2, 3, true, TestName = "{m}.AFindingIsAWarningWhateverElseWasMissed")]
        public void TheSeverityFollowsTheFindingsAndTheUnreadableSetAlwaysPrints(
            int findingCount,
            int unreadableCount,
            bool expectedWarn
        )
        {
            List<string> unreadable = new();
            for (int index = 0; index < unreadableCount; ++index)
            {
                unreadable.Add($"Assets/Unreadable{index}.asset");
            }

            List<string> findings = new();
            for (int index = 0; index < findingCount; ++index)
            {
                findings.Add($"finding {index}");
            }

            (string message, bool warn) = AuthoredAssetValidationMenu.Compose(
                "authored dictionaries",
                "1 dictionary judged",
                unreadable,
                findings
            );

            Assert.AreEqual(expectedWarn, warn, message);
            for (int index = 0; index < unreadableCount; ++index)
            {
                StringAssert.Contains($"Assets/Unreadable{index}.asset", message);
            }

            Assert.AreEqual(
                0 < unreadableCount,
                message.Contains("could not be read", StringComparison.Ordinal),
                "the set has to print whether or not it raises the severity"
            );
        }

        [Test]
        public void AReportWithNothingUnreadableStaysSilent()
        {
            StringBuilder message = new();
            message.Append("authored dictionaries: 0 findings");
            UnreadableAssetPaths.Append(message, Array.Empty<string>());

            Assert.AreEqual("authored dictionaries: 0 findings", message.ToString());
        }

        private static IEnumerable<TestCaseData> TextScans()
        {
            yield return new TestCaseData(
                nameof(AuthoredRequirementValidator),
                (Func<IReadOnlyList<string>, List<string>, bool>)ScanAuthoredRequirements
            ).SetName("{m}.AuthoredRequirements");

            yield return new TestCaseData(
                nameof(SerializableDictionaryAssetValidator),
                (Func<IReadOnlyList<string>, List<string>, bool>)ScanSerializableDictionaries
            ).SetName("{m}.SerializableDictionaries");

            yield return new TestCaseData(
                nameof(StaleSerializedKeyValidator),
                (Func<IReadOnlyList<string>, List<string>, bool>)ScanStaleSerializedKeys
            ).SetName("{m}.StaleSerializedKeys");
        }

        private static bool ScanAuthoredRequirements(
            IReadOnlyList<string> assetPaths,
            List<string> unreadable
        )
        {
            return AuthoredRequirementValidator.TryScan(
                assetPaths,
                new List<AuthoredRequirementFinding>(),
                new List<AuthoredRequirementExemption>(),
                unreadable,
                out int _
            );
        }

        private static bool ScanSerializableDictionaries(
            IReadOnlyList<string> assetPaths,
            List<string> unreadable
        )
        {
            return SerializableDictionaryAssetValidator.TryScan(
                assetPaths,
                new List<SerializableDictionaryAssetFinding>(),
                unreadable,
                out int _
            );
        }

        private static bool ScanStaleSerializedKeys(
            IReadOnlyList<string> assetPaths,
            List<string> unreadable
        )
        {
            return StaleSerializedKeyValidator.TryScan(
                assetPaths,
                new List<StaleSerializedKeyFinding>(),
                unreadable,
                out int _
            );
        }

        /// <summary>
        /// Writes one asset carrying a dictionary with no values, so a scan that read it has
        /// something to report and a scan that did not cannot look the same.
        /// </summary>
        private string WriteAsset(string name)
        {
            string assetPath = Path.Combine(_root, $"{name}.asset");
            File.WriteAllLines(
                assetPath,
                new[]
                {
                    "%YAML 1.1",
                    "%TAG !u! tag:unity3d.com,2011:",
                    "--- !u!114 &11400000",
                    "MonoBehaviour:",
                    $"  m_Script: {{fileID: 11500000, guid: {AbsentScriptGuid}, type: 3}}",
                    $"  m_Name: {name}",
                    "  _map:",
                    "    _keys:",
                    "    - Idle",
                }
            );

            return assetPath;
        }

        private const string AbsentScriptGuid = "ffffffffffffffffffffffffffffffff";

        private string _root;
    }
}
