// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    public sealed class WProtoSchemaPolymorphismTests
    {
        private static string Export(params Type[] roots)
        {
            Assert.IsTrue(
                WProtoSchemaText.TryWriteSchema(
                    roots,
                    "interop",
                    null,
                    out string schema,
                    out IReadOnlyList<string> diagnostics
                )
            );
            Assert.IsEmpty(diagnostics);
            return schema;
        }

        private static string Message(string schema, string name)
        {
            string prefix = "message " + name + " {\n";
            int start = schema.IndexOf(prefix, StringComparison.Ordinal);
            Assert.IsTrue(0 <= start, name);
            int end = schema.IndexOf("\n}", start, StringComparison.Ordinal);
            Assert.IsTrue(start < end, name);
            return schema.Substring(start, end - start + 2);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ConcreteRootsDescribeTheFullEnvelope(int rootOrder)
        {
            Type[] roots =
                rootOrder == 0 ? new[] { typeof(SchemaDerived) }
                : rootOrder == 1 ? new[] { typeof(SchemaIncludeBase), typeof(SchemaDerived) }
                : new[] { typeof(SchemaDerived), typeof(SchemaIncludeBase) };
            string schema = Export(roots);
            string root = Message(schema, nameof(SchemaDerived));
            StringAssert.Contains("int32 BaseId = 1;", root);
            StringAssert.Contains("string BaseName = 2;", root);
            StringAssert.Contains("SchemaDerivedPayload schemaDerived = 7;", root);
            StringAssert.DoesNotContain("int32 Extra = 10;", root);
            string payload = Message(schema, "SchemaDerivedPayload");
            StringAssert.Contains("int32 Extra = 10;", payload);
            StringAssert.DoesNotContain("BaseId", payload);
            StringAssert.DoesNotContain("BaseName", payload);
        }

        [Test]
        public void ReorderingTheSameRootsKeepsTheSameSchema()
        {
            Assert.AreEqual(
                Export(typeof(SchemaIncludeBase), typeof(SchemaDerived)),
                Export(typeof(SchemaDerived), typeof(SchemaIncludeBase))
            );
        }

        [TestCase(typeof(SchemaChainBase))]
        [TestCase(typeof(SchemaChainDerived))]
        [TestCase(typeof(SchemaChainSubDerived))]
        public void IncludeChainsKeepMembersAtTheirDeclaringLevel(Type root)
        {
            string schema = Export(root);
            string envelope = Message(schema, root.Name);
            StringAssert.Contains("int32 RootId = 1;", envelope);
            StringAssert.Contains("SchemaChainDerivedPayload schemaChainDerived = 7;", envelope);
            StringAssert.DoesNotContain("int32 Middle", envelope);
            StringAssert.DoesNotContain("int32 Leaf", envelope);
            string middle = Message(schema, "SchemaChainDerivedPayload");
            StringAssert.Contains("int32 Middle = 10;", middle);
            StringAssert.Contains(
                "SchemaChainSubDerivedPayload schemaChainSubDerived = 8;",
                middle
            );
            StringAssert.DoesNotContain("RootId", middle);
            string leaf = Message(schema, "SchemaChainSubDerivedPayload");
            StringAssert.Contains("int32 Leaf = 20;", leaf);
            StringAssert.DoesNotContain("RootId", leaf);
            StringAssert.DoesNotContain("Middle", leaf);
        }

        [Test]
        public void ReorderingEveryLevelKeepsTheSameChainSchema()
        {
            Assert.AreEqual(
                Export(
                    typeof(SchemaChainBase),
                    typeof(SchemaChainDerived),
                    typeof(SchemaChainSubDerived)
                ),
                Export(
                    typeof(SchemaChainSubDerived),
                    typeof(SchemaChainDerived),
                    typeof(SchemaChainBase)
                )
            );
        }

        [Test]
        public void MembersAndCollectionsUseFullConcreteEnvelopes()
        {
            string schema = Export(typeof(WProtoSchemaSubtypeHolder));
            string holder = Message(schema, nameof(WProtoSchemaSubtypeHolder));
            StringAssert.Contains("SchemaDerived Concrete = 2;", holder);
            StringAssert.Contains("SchemaChainDerived Middle = 3;", holder);
            StringAssert.Contains("SchemaChainSubDerived Leaf = 4;", holder);
            StringAssert.Contains("repeated SchemaDerived Repeated = 5;", holder);
            StringAssert.Contains("map<string, SchemaDerived> ByName = 6;", holder);
            StringAssert.Contains("schemaDerived = 7;", Message(schema, nameof(SchemaDerived)));
            StringAssert.Contains(
                "schemaChainDerived = 7;",
                Message(schema, nameof(SchemaChainDerived))
            );
            StringAssert.Contains(
                "schemaChainDerived = 7;",
                Message(schema, nameof(SchemaChainSubDerived))
            );
        }

        [Test]
        public void SelfDeclaredConcreteRootsUseTheSameEnvelope()
        {
            string schema = Export(typeof(SchemaSelfDeclaredDerived));
            string root = Message(schema, nameof(SchemaSelfDeclaredDerived));
            StringAssert.Contains("int32 BaseId = 1;", root);
            StringAssert.Contains(
                "SchemaSelfDeclaredDerivedPayload schemaSelfDeclaredDerived = 7;",
                root
            );
            StringAssert.DoesNotContain("int32 Extra", root);
        }

        [Test]
        public void CustomContractNamesKeepIncludeFieldsDistinctFromMembers()
        {
            Assert.IsTrue(
                WProtoSchemaText.TryWriteSchema(
                    new[] { typeof(WProtoNamedSchemaDerived) },
                    "interop",
                    null,
                    out string schema,
                    out IReadOnlyList<string> diagnostics
                )
            );
            Assert.AreEqual(1, diagnostics.Count);
            StringAssert.Contains(
                "NamedNode: the field name namedNode is already in use; renamed to namedNode3.",
                diagnostics[0]
            );
            string root = Message(schema, "NamedNode");
            StringAssert.Contains("int32 namedNode = 3;", root);
            StringAssert.Contains("NamedNodePayload namedNode3 = 7;", root);
            StringAssert.Contains("int32 Extra = 2;", Message(schema, "NamedNodePayload"));
        }

        [Test]
        public void ConcreteAndBaseEntryPointsKeepTheSameWireGolden()
        {
            SchemaDerived value = new SchemaDerived
            {
                BaseId = 1,
                BaseName = "base",
                Extra = 7,
            };
            byte[] expected =
            {
                0x3A,
                0x02,
                0x50,
                0x07,
                0x08,
                0x01,
                0x12,
                0x04,
                0x62,
                0x61,
                0x73,
                0x65,
            };
            CollectionAssert.AreEqual(expected, WProtoFacade.Serialize(value));
            CollectionAssert.AreEqual(expected, WProtoFacade.Serialize<SchemaIncludeBase>(value));
        }
    }
}
