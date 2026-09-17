// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;

    [TestFixture]
    [Category("Fast")]
    public sealed class OdinMigrationSerializedDataScannerTests
    {
        [Test]
        public void FindsSerializedDataMappingsAndPrefabOverrides()
        {
            const string Source =
                "%YAML 1.1\r\n"
                + "--- !u!114 &11400000\r\n"
                + "MonoBehaviour:\r\n"
                + "  serializationData:\r\n"
                + "    serializedFormat: 2\r\n"
                + "  _serializationData: {serializedFormat: 2}\r\n"
                + "  m_Modification:\r\n"
                + "    propertyPath: serializationData.serializedBytes\r\n"
                + "    propertyPath: '_serializationData.serializedBytes'\r\n"
                + "    propertyPath: \"m_Component.serializationData.serializedBytes\"\r\n";

            Assert.IsTrue(
                OdinMigrationSerializedDataScanner.TryAnalyze(
                    Source,
                    out IReadOnlyList<OdinMigrationFinding> findings
                )
            );

            Assert.AreEqual(5, findings.Count);
            Assert.AreEqual(4, findings[0].Line);
            Assert.AreEqual(6, findings[1].Line);
            Assert.AreEqual(8, findings[2].Line);
            Assert.AreEqual(9, findings[3].Line);
            Assert.AreEqual(10, findings[4].Line);
        }

        [Test]
        public void FlagsExactKeysButIgnoresCommentsAndSimilarNames()
        {
            const string Source =
                "--- !u!114 &11400000\n"
                + "MonoBehaviour:\n"
                + "# serializationData:\n"
                + "  note: serializationData:\n"
                + "  serializationData: ordinary text\n"
                + "  _serializationData: \"ordinary text\"\n"
                + "  other_serializationData:\n"
                + "  propertyPath: m_Component.serializationDataBackup\n"
                + "  propertyPath: \"m_Component._serializationDataBackup\"\n"
                + "  # propertyPath: serializationData.serializedBytes\n"
                + "  propertyPath: m_Component.notes # serializationData.serializedBytes\n"
                + "  note: |\n"
                + "    serializationData: block scalar text\n";

            Assert.IsTrue(
                OdinMigrationSerializedDataScanner.TryAnalyze(
                    Source,
                    out IReadOnlyList<OdinMigrationFinding> findings
                )
            );

            Assert.AreEqual(2, findings.Count);
            Assert.AreEqual(5, findings[0].Line);
            Assert.AreEqual(6, findings[1].Line);
        }

        [Test]
        public void FindsQuotedMappingKeysWithoutMatchingSimilarNames()
        {
            const string Source =
                "--- !u!114 &11400000\n"
                + "MonoBehaviour:\n"
                + "  'serializationData': {}\n"
                + "  \"_serializationData\": {}\n"
                + "  'propertyPath': 'serializationData.serializedBytes'\n"
                + "  'serializationDataBackup': {}\n"
                + "  'serializationData: {}\n"
                + "  - ";

            Assert.IsTrue(
                OdinMigrationSerializedDataScanner.TryAnalyze(
                    Source,
                    out IReadOnlyList<OdinMigrationFinding> findings
                )
            );

            Assert.AreEqual(3, findings.Count);
            Assert.AreEqual(3, findings[0].Line);
            Assert.AreEqual(4, findings[1].Line);
            Assert.AreEqual(5, findings[2].Line);
        }

        [Test]
        public void HandlesMixedLineEndingsAndNullSource()
        {
            const string Source =
                "--- !u!114 &11400000\r\n"
                + "MonoBehaviour:\r\n"
                + "serializationData:\r_property:\n_serializationData:\r\n";

            Assert.IsTrue(
                OdinMigrationSerializedDataScanner.TryAnalyze(
                    Source,
                    out IReadOnlyList<OdinMigrationFinding> findings
                )
            );

            Assert.AreEqual(2, findings.Count);
            Assert.AreEqual(3, findings[0].Line);
            Assert.AreEqual(5, findings[1].Line);
            Assert.IsFalse(
                OdinMigrationSerializedDataScanner.TryAnalyze(
                    null,
                    out IReadOnlyList<OdinMigrationFinding> empty
                )
            );
            Assert.IsEmpty(empty);
            Assert.IsFalse(
                OdinMigrationSerializedDataScanner.TryAnalyze(
                    "%YAML 1.1\n",
                    out IReadOnlyList<OdinMigrationFinding> noDocuments
                )
            );
            Assert.IsEmpty(noDocuments);
        }

        [Test]
        public void RecognizesUnityYamlHeaders()
        {
            Assert.IsTrue(
                OdinMigrationSerializedDataScanner.LooksLikeUnityYaml(
                    "\ufeff  %YAML 1.1\n--- !u!114"
                )
            );
            Assert.IsTrue(
                OdinMigrationSerializedDataScanner.LooksLikeUnityYaml("\n--- !u!1 &1000\n")
            );
            Assert.IsFalse(OdinMigrationSerializedDataScanner.LooksLikeUnityYaml(null));
            Assert.IsFalse(
                OdinMigrationSerializedDataScanner.LooksLikeUnityYaml(
                    "plain text\nserializationData:\n"
                )
            );
            Assert.IsFalse(
                OdinMigrationSerializedDataScanner.LooksLikeUnityYaml("\0\u0001\u0002--- !u!1")
            );
            Assert.IsFalse(
                OdinMigrationSerializedDataScanner.LooksLikeUnityYaml(
                    "%YAML 1.1\n--- !u!1 &1000\n\0"
                )
            );
        }
    }
}
