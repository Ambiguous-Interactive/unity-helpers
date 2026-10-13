// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;

    [TestFixture]
    [Category("Fast")]
    public sealed class OdinMigrationNodeReaderTests
    {
        private const string Header =
            "--- !u!114 &123\nMonoBehaviour:\n  m_Script: {fileID: 11500000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n  serializationData:\n    SerializedFormat: 2\n    SerializedBytes: \n    ReferencedUnityObjects:\n    - {fileID: 21300000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}\n    SerializedBytesString: \n    PrefabModifications: []\n    SerializationNodes:\n";
        private const string Record =
            "    - Name: Value\n      Entry: 7\n      Data: 0|System.Collections.Generic.List`1[[UnityEngine.Sprite,\n        UnityEngine.CoreModule]], mscorlib\n";

        private static void Refused(string source)
        {
            Assert.IsFalse(
                OdinMigrationTool.TryReadSerializedNodes(
                    source,
                    out IReadOnlyList<OdinMigrationSerializedData> payloads,
                    out string failure
                )
            );
            Assert.IsEmpty(payloads);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(failure));
        }

        [TestCase("\n")]
        [TestCase("\r\n")]
        [TestCase("\r")]
        public void ReadsRecordsAndPreservesTheExactPayloadAndReferences(string newline)
        {
            string source = (
                Header
                + Record
                + "    - Name: \n      Entry: 10\n      Data: 0\n    - Name: \n      Entry: 8\n      Data: \n  unrelated: 42\n"
            ).Replace("\n", newline);
            Assert.IsTrue(
                OdinMigrationTool.TryReadSerializedNodes(
                    source,
                    out IReadOnlyList<OdinMigrationSerializedData> payloads,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(1, payloads.Count);
            OdinMigrationSerializedData payload = payloads[0];
            Assert.AreEqual(123, payload.FileId);
            Assert.AreEqual("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", payload.ScriptGuid);
            Assert.AreEqual(4, payload.Line);
            Assert.IsTrue(
                payload.Source.StartsWith(
                    "  serializationData:" + newline,
                    StringComparison.Ordinal
                )
            );
            Assert.IsTrue(
                payload.Source.EndsWith("      Data: " + newline, StringComparison.Ordinal)
            );
            Assert.IsFalse(payload.Source.Contains("unrelated"));
            Assert.AreEqual(3, payload.SerializationNodes.Count);
            Assert.AreEqual("Value", payload.SerializationNodes[0].Name);
            Assert.AreEqual(7, payload.SerializationNodes[0].Entry);
            Assert.AreEqual(
                "0|System.Collections.Generic.List`1[[UnityEngine.Sprite, UnityEngine.CoreModule]], mscorlib",
                payload.SerializationNodes[0].Data
            );
            Assert.AreEqual("0", payload.SerializationNodes[1].Data);
            Assert.AreEqual(string.Empty, payload.SerializationNodes[2].Data);
            CollectionAssert.AreEqual(
                new[] { "{fileID: 21300000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}" },
                payload.ReferencedUnityObjects
            );
            Assert.Throws<NotSupportedException>(() =>
                ((IList<OdinMigrationSerializedData>)payloads).Clear()
            );
            Assert.Throws<NotSupportedException>(() =>
                ((IList<OdinMigrationSerializedNode>)payload.SerializationNodes).Clear()
            );
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)payload.ReferencedUnityObjects).Clear()
            );
        }

        [TestCase("\"line\\nvalue\\u263a\"", "line\nvalue☺")]
        [TestCase("'it''s unchanged'", "it's unchanged")]
        [TestCase("\"\"", "")]
        [TestCase("9223372036854775807", "9223372036854775807")]
        public void PreservesScalarDataWithoutUsingPayloadCountsForAllocation(
            string scalar,
            string expected
        )
        {
            string source =
                Header + "    - Name: Count\n      Entry: 12\n      Data: " + scalar + "\n";
            Assert.IsTrue(
                OdinMigrationTool.TryReadSerializedNodes(
                    source,
                    out IReadOnlyList<OdinMigrationSerializedData> payloads,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(1, payloads[0].SerializationNodes.Count);
            Assert.AreEqual(expected, payloads[0].SerializationNodes[0].Data);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        [TestCase(11)]
        [TestCase(12)]
        [TestCase(13)]
        [TestCase(14)]
        [TestCase(15)]
        [TestCase(16)]
        public void RetainsEveryDefinedEntryTagWithoutInterpretingItsType(int entry)
        {
            string source = Header + $"    - Name: \n      Entry: {entry}\n      Data: opaque\n";
            Assert.IsTrue(
                OdinMigrationTool.TryReadSerializedNodes(
                    source,
                    out IReadOnlyList<OdinMigrationSerializedData> payloads,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(entry, payloads[0].SerializationNodes[0].Entry);
            Assert.AreEqual("opaque", payloads[0].SerializationNodes[0].Data);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(17)]
        [TestCase(int.MaxValue)]
        public void RefusesUnknownEntryTagsWithoutReturningPartialPayloads(int entry)
        {
            Refused(Header + $"    - Name: \n      Entry: {entry}\n      Data: \n");
        }

        [TestCase("    - Name: OnlyName\n")]
        [TestCase("    - Name: A\n      Name: B\n      Entry: 1\n      Data: value\n")]
        [TestCase("    - Name: A\n      Entry: 1\n      Data: value\n      Other: lost\n")]
        [TestCase("    - Name: A\n      Entry: 1\n      Data: \"unterminated\n")]
        [TestCase("    - Name: A\n      Entry: 1\n      Data: value # comment\n")]
        [TestCase("    - Name: A\n      Entry: 1\n      Data: &alias\n")]
        [TestCase("    - Name: A\n      Entry: 1\n      Data: 'unescaped'quote'\n")]
        [TestCase("    - Name: A\n      Entry: 1\n      Data: {lost: value}\n")]
        [TestCase("    - Name: A\n      Entry: 1\n      Data: value: mapping\n")]
        [TestCase("    - Name: A\n      Entry: 1\n      Data: |\n        value\n")]
        [TestCase("      notARecord: value\n")]
        [TestCase("      Name: hidden\n    - Name: visible\n      Entry: 1\n      Data: value\n")]
        public void RefusesAmbiguousOrIncompleteRecords(string record)
        {
            Refused(Header + record);
        }

        [Test]
        public void RefusesUnsupportedFormatsAndHiddenData()
        {
            Refused((Header + Record).Replace("SerializedFormat: 2", "SerializedFormat: 0"));
            Refused((Header + Record).Replace("SerializedBytes: ", "SerializedBytes: 0102"));
            Refused(
                (Header + Record).Replace(
                    "SerializedFormat: 2",
                    "SerializedFormat: 2\n    SerializedFormat: 2"
                )
            );
            Refused(
                (Header + Record).Replace(
                    "ReferencedUnityObjects:\n    - {fileID: 21300000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}",
                    "ReferencedUnityObjects:\n      unexpected: reference"
                )
            );
        }

        [Test]
        public void ReadsEmptyPayloadsAndKeepsSeparateDocumentOwners()
        {
            string empty = Header.Replace("SerializationNodes:\n", "SerializationNodes: []\n");
            string source =
                empty
                + empty
                    .Replace("&123", "&456")
                    .Replace("serializationData:", "_serializationData:");
            Assert.IsTrue(
                OdinMigrationTool.TryReadSerializedNodes(
                    source,
                    out IReadOnlyList<OdinMigrationSerializedData> payloads,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(2, payloads.Count);
            Assert.AreEqual(123, payloads[0].FileId);
            Assert.AreEqual(456, payloads[1].FileId);
            Assert.IsEmpty(payloads[0].SerializationNodes);
            Refused(source + Header.Replace("SerializedFormat: 2", "SerializedFormat: 1"));
        }

        [Test]
        public void RejectsInvalidAndOversizedInput()
        {
            Refused(null);
            Refused(string.Empty);
            Refused("ordinary text");
            Refused("%YAML 1.1\n");
            Refused(Header + "\0");
            Refused(Header + new string('x', 4 * 1024 * 1024));
            Refused(Header + new string('\n', 65536));
        }

        [Test]
        public void AValidUnityDocumentWithoutOdinReturnsNoPayloads()
        {
            Assert.IsTrue(
                OdinMigrationTool.TryReadSerializedNodes(
                    "--- !u!1 &1\nGameObject:\n  m_Name: Example\n",
                    out IReadOnlyList<OdinMigrationSerializedData> payloads,
                    out string failure
                ),
                failure
            );
            Assert.IsEmpty(payloads);
        }

        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        public void PreservesLargeDocumentIdentifiersWithoutFloatingPointConversion(long fileId)
        {
            string source = (Header + Record).Replace(
                "&123",
                "&" + fileId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            Assert.IsTrue(
                OdinMigrationTool.TryReadSerializedNodes(
                    source,
                    out IReadOnlyList<OdinMigrationSerializedData> payloads,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(fileId, payloads[0].FileId);
        }
    }
}
