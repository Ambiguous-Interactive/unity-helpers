// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;

    [TestFixture]
    [Category("Fast")]
    public sealed class OdinMigrationDictionaryConverterTests
    {
        private const string FieldName = "Variations";
        private const string SpriteReference =
            "{fileID: 21300000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}";
        private const string DictionaryType =
            "System.Collections.Generic.Dictionary`2[[UnityEngine.SpriteAlignment, UnityEngine.CoreModule],[System.Collections.Generic.List`1[[UnityEngine.Sprite, UnityEngine.CoreModule]], mscorlib]], mscorlib";
        private const string ListType =
            "System.Collections.Generic.List`1[[UnityEngine.Sprite, UnityEngine.CoreModule]], mscorlib";

        private static void Node(StringBuilder result, string name, int entry, string data)
        {
            result
                .Append("    - Name: ")
                .Append(name)
                .Append("\n      Entry: ")
                .Append(entry.ToString(CultureInfo.InvariantCulture))
                .Append("\n      Data: ")
                .Append(data)
                .Append('\n');
        }

        private static string Source(int[] keys, bool emptyList = false, bool noComparer = false)
        {
            StringBuilder result = new StringBuilder(
                "--- !u!114 &123\nMonoBehaviour:\n  serializationData:\n    SerializedFormat: 2\n    SerializedBytes: \n    SerializedBytesString: \n    ReferencedUnityObjects:\n    - "
                    + SpriteReference
                    + "\n    Prefab: {fileID: 0}\n    PrefabModifications: []\n    PrefabModificationsReferencedUnityObjects: []\n    SerializationNodes:\n"
            );
            Node(result, FieldName, 7, "0|" + DictionaryType);
            if (!noComparer)
            {
                Node(
                    result,
                    "comparer",
                    7,
                    "1|System.Collections.Generic.EnumEqualityComparer`1[[UnityEngine.SpriteAlignment, UnityEngine.CoreModule]], mscorlib"
                );
                Node(result, string.Empty, 12, "0");
                Node(result, string.Empty, 13, string.Empty);
                Node(result, string.Empty, 8, string.Empty);
            }
            else
            {
                Node(result, "comparer", 6, string.Empty);
            }
            Node(result, string.Empty, 12, keys.Length.ToString(CultureInfo.InvariantCulture));
            int identity = 2;
            foreach (int key in keys)
            {
                Node(result, string.Empty, 7, string.Empty);
                Node(result, "$k", 3, key.ToString(CultureInfo.InvariantCulture));
                Node(
                    result,
                    "$v",
                    7,
                    identity.ToString(CultureInfo.InvariantCulture) + "|" + ListType
                );
                ++identity;
                Node(result, string.Empty, 12, emptyList ? "0" : "1");
                if (!emptyList)
                {
                    Node(result, string.Empty, 10, "0");
                }
                Node(result, string.Empty, 13, string.Empty);
                Node(result, string.Empty, 8, string.Empty);
                Node(result, string.Empty, 8, string.Empty);
            }
            Node(result, string.Empty, 13, string.Empty);
            Node(result, string.Empty, 8, string.Empty);
            return result.ToString();
        }

        private static OdinMigrationSerializedData Read(string source)
        {
            Assert.IsTrue(
                OdinMigrationTool.TryReadSerializedNodes(
                    source,
                    out IReadOnlyList<OdinMigrationSerializedData> payloads,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(1, payloads.Count);
            return payloads[0];
        }

        private static void Refused(string source, string fieldName = FieldName)
        {
            Assert.IsFalse(
                OdinMigrationTool.TryConvertSerializedDictionary<SpriteAlignment, Sprite>(
                    Read(source),
                    fieldName,
                    out string yaml,
                    out string failure
                )
            );
            Assert.IsTrue(yaml == null);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(failure));
        }

        [TestCase(0, "00000000")]
        [TestCase(-1, "ffffffff")]
        [TestCase(int.MinValue, "00000080")]
        [TestCase(int.MaxValue, "ffffff7f")]
        public void EmitsTheKnownUnityLayoutAndPreservesEveryEnumBit(int key, string hex)
        {
            Assert.IsTrue(
                OdinMigrationTool.TryConvertSerializedDictionary<SpriteAlignment, Sprite>(
                    Read(Source(new[] { key })),
                    FieldName,
                    out string yaml,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual(
                "  Variations:\n    _keys: "
                    + hex
                    + "\n    _boxedValues:\n    - Data:\n      - "
                    + SpriteReference
                    + "\n",
                yaml
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreservesEmptyDictionariesAndEmptyLists(bool noComparer)
        {
            Assert.IsTrue(
                OdinMigrationTool.TryConvertSerializedDictionary<SpriteAlignment, Sprite>(
                    Read(Source(Array.Empty<int>(), noComparer: noComparer)),
                    FieldName,
                    out string empty,
                    out string failure
                ),
                failure
            );
            Assert.AreEqual("  Variations:\n    _keys: \n    _boxedValues: []\n", empty);
            Assert.IsTrue(
                OdinMigrationTool.TryConvertSerializedDictionary<SpriteAlignment, Sprite>(
                    Read(Source(new[] { 0, -1 }, emptyList: true, noComparer: noComparer)),
                    FieldName,
                    out string lists,
                    out failure
                ),
                failure
            );
            Assert.AreEqual(
                "  Variations:\n    _keys: 00000000ffffffff\n    _boxedValues:\n    - Data: []\n    - Data: []\n",
                lists
            );
        }

        [TestCase("")]
        [TestCase("Other")]
        [TestCase("bad\nfield")]
        [TestCase("0field")]
        public void RefusesWrongOrUnsafeFieldNames(string field)
        {
            Refused(Source(new[] { 0 }), field);
        }

        [TestCase("Entry: 10\n      Data: 0", "Entry: 10\n      Data: 1")]
        [TestCase("Entry: 10\n      Data: 0", "Entry: 10\n      Data: -1")]
        [TestCase("Entry: 10\n      Data: 0", "Entry: 10\n      Data: 2147483647")]
        [TestCase("Entry: 10\n      Data: 0", "Entry: 9\n      Data: 2")]
        [TestCase("Entry: 10\n      Data: 0", "Entry: 6\n      Data: ")]
        [TestCase("Entry: 12\n      Data: 1", "Entry: 12\n      Data: 9223372036854775807")]
        [TestCase("Entry: 12\n      Data: 1", "Entry: 12\n      Data: 0")]
        [TestCase("Entry: 12\n      Data: 0", "Entry: 12\n      Data: 1")]
        [TestCase("Data: 2|", "Data: 1|")]
        [TestCase("Name: $k", "Name: Unknown")]
        [TestCase("Name: $v", "Name: Unknown")]
        [TestCase("UnityEngine.Sprite,", "UnityEngine.Material,")]
        [TestCase("EnumEqualityComparer`1", "CustomComparer`1")]
        public void RefusesMalformedGraphCountsReferencesAndSchema(string before, string after)
        {
            Refused(Source(new[] { 0 }).Replace(before, after));
        }

        [Test]
        public void RefusesDuplicateKeysTruncationAndUnconvertedRecords()
        {
            Refused(Source(new[] { 0, 0 }));
            string source = Source(new[] { 0 });
            Refused(
                source.Substring(0, source.LastIndexOf("    - Name:", StringComparison.Ordinal))
            );
            Refused(source + "    - Name: Extra\n      Entry: 1\n      Data: lost\n");
            Refused(
                source.Replace(
                    ListType,
                    "System.Collections.Generic.List`1[[" + ListType + "]], mscorlib"
                )
            );
        }

        [Test]
        public void RefusesLinkedPrefabAndRetainedOverrides()
        {
            string source = Source(new[] { 0 });
            Refused(source.Replace("Prefab: {fileID: 0}", "Prefab: {fileID: 100}"));
            Refused(source.Replace("PrefabModifications: []", "PrefabModifications: [lost]"));
            Refused(
                source.Replace(
                    "PrefabModificationsReferencedUnityObjects: []",
                    "PrefabModificationsReferencedUnityObjects: [lost]"
                )
            );
            Refused(
                source.Replace(
                    "PrefabModifications: []",
                    "PrefabModifications: []\n    - hidden: lost"
                )
            );
            Refused(
                source.Replace(
                    "Prefab: {fileID: 0}",
                    "Prefab: {fileID: 0}\n    FutureMetadata: lost"
                )
            );
        }

        [Test]
        public void RefusesMissingPayload()
        {
            Assert.IsFalse(
                OdinMigrationTool.TryConvertSerializedDictionary<SpriteAlignment, Sprite>(
                    null,
                    FieldName,
                    out string yaml,
                    out string failure
                )
            );
            Assert.IsTrue(yaml == null);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(failure));
        }

        [Test]
        public void ConvertsAThousandDeliveredEntriesWithoutUsingClaimedCapacity()
        {
            int[] keys = new int[1000];
            int count = keys.Length;
            for (int index = 0; index < count; ++index)
            {
                keys[index] = index;
            }
            Assert.IsTrue(
                OdinMigrationTool.TryConvertSerializedDictionary<SpriteAlignment, Sprite>(
                    Read(Source(keys)),
                    FieldName,
                    out string yaml,
                    out string failure
                ),
                failure
            );
            Assert.IsTrue(yaml.Contains("_keys: 0000000001000000"));
            Assert.IsTrue(yaml.Contains("e7030000\n"));
            Assert.AreEqual(
                count,
                yaml.Split(new[] { SpriteReference }, StringSplitOptions.None).Length - 1
            );
        }

        [Test]
        public void ReadsTheLegacyMissingComparerWithoutDroppingState()
        {
            string source = Source(new[] { 0 }, noComparer: true)
                .Replace("    - Name: comparer\n      Entry: 6\n      Data: \n", string.Empty);
            Assert.IsTrue(
                OdinMigrationTool.TryConvertSerializedDictionary<SpriteAlignment, Sprite>(
                    Read(source),
                    FieldName,
                    out string yaml,
                    out string failure
                ),
                failure
            );
            Assert.IsTrue(yaml.Contains(SpriteReference));
        }
    }
}
