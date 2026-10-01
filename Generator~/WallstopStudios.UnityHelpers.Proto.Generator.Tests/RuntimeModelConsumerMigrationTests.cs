// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf.Meta;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Proves static consumer declarations preserve runtime-only models on both oracles.</summary>
    [TestFixture]
    public sealed class RuntimeModelConsumerMigrationTests
    {
        private static List<RuntimeConfiguredEntry> Entries(int edge)
        {
            return new List<RuntimeConfiguredEntry>
            {
                new RuntimeConfiguredEntry { Name = "base" },
                new RuntimeConfiguredItem { Name = "", Quantity = edge },
                new RuntimeConfiguredItem { Name = "café", Quantity = 0 },
            };
        }

        private static RuntimeTypeModel Model<TIdentifier>()
        {
            RuntimeTypeModel model = RuntimeTypeModel.Create();
#pragma warning disable WPROTO048
            MetaType save = model.Add(typeof(RuntimeConfiguredSave<TIdentifier>), false);
            save.Add(5, nameof(RuntimeConfiguredSave<TIdentifier>.Identifier));
            save.Add(11, nameof(RuntimeConfiguredSave<TIdentifier>.Version));
            save[11].DefaultValue = 0;
            save.Add(17, nameof(RuntimeConfiguredSave<TIdentifier>.Checkpoint));
            save.Add(23, nameof(RuntimeConfiguredSave<TIdentifier>.Entries));
            save.Add(29, nameof(RuntimeConfiguredSave<TIdentifier>.Position));
            model
                .Add(typeof(RuntimeConfiguredEntry), false)
                .Add(7, nameof(RuntimeConfiguredEntry.Name))
                .AddSubType(101, typeof(RuntimeConfiguredItem));
            model
                .Add(typeof(RuntimeConfiguredItem), false)
                .Add(13, nameof(RuntimeConfiguredItem.Quantity));
            model.Add(typeof(ForeignVector3), false).SetSurrogate(typeof(ForeignVector3Surrogate));
#pragma warning restore WPROTO048
            return model;
        }

        private static byte[] OracleBytes<T>(RuntimeTypeModel model, T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                model.Serialize(stream, value);
                return stream.ToArray();
            }
        }

        private static void Verify<TIdentifier>(RuntimeConfiguredSave<TIdentifier> value)
        {
            RuntimeTypeModel model = Model<TIdentifier>();
            byte[] original = OracleBytes(model, value);
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] generated));
            CollectionAssert.AreEqual(
                original,
                generated,
                "The old runtime model defines the saved bytes."
            );
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(
                    original,
                    out RuntimeConfiguredSave<TIdentifier> migrated
                )
            );
            AssertValue(value, migrated);
            using (MemoryStream stream = new MemoryStream(generated))
            {
                RuntimeConfiguredSave<TIdentifier> restored =
                    (RuntimeConfiguredSave<TIdentifier>)
                        model.Deserialize(stream, null, typeof(RuntimeConfiguredSave<TIdentifier>));
                AssertValue(value, restored);
            }
        }

        private static void AssertValue<TIdentifier>(
            RuntimeConfiguredSave<TIdentifier> expected,
            RuntimeConfiguredSave<TIdentifier> actual
        )
        {
            Assert.IsTrue(actual != null);
            Assert.AreEqual(expected.Identifier, actual.Identifier);
            Assert.AreEqual(expected.Version, actual.Version);
            Assert.AreEqual(expected.Checkpoint, actual.Checkpoint);
            Assert.AreEqual(expected.Position.x, actual.Position.x);
            Assert.AreEqual(expected.Position.y, actual.Position.y);
            Assert.AreEqual(expected.Position.z, actual.Position.z);
            int count = expected.Entries == null ? 0 : expected.Entries.Count;
            Assert.AreEqual(count, actual.Entries == null ? 0 : actual.Entries.Count);
            for (int index = 0; index < count; index++)
            {
                Assert.AreEqual(expected.Entries[index].GetType(), actual.Entries[index].GetType());
                Assert.AreEqual(expected.Entries[index].Name, actual.Entries[index].Name);
                if (expected.Entries[index] is RuntimeConfiguredItem item)
                {
                    Assert.AreEqual(
                        item.Quantity,
                        ((RuntimeConfiguredItem)actual.Entries[index]).Quantity
                    );
                }
            }
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(-1, false)]
        [TestCase(int.MinValue, false)]
        [TestCase(int.MaxValue, false)]
        [TestCase(0, true)]
        [TestCase(-1, true)]
        [TestCase(int.MinValue, true)]
        [TestCase(int.MaxValue, true)]
        public void ClosedTargetsPreserveModelBytesAndBothReadDirections(
            int edge,
            bool stringIdentifier
        )
        {
            if (stringIdentifier)
            {
                Verify(
                    new RuntimeConfiguredSave<string>
                    {
                        Identifier = edge == 0 ? "" : "café/" + edge,
                        Version = edge,
                        Checkpoint = edge == 0 ? 0 : null,
                        Entries = Entries(edge),
                        Position = new ForeignVector3
                        {
                            x = edge,
                            y = -1,
                            z = 2,
                        },
                    }
                );
            }
            else
            {
                Verify(
                    new RuntimeConfiguredSave<int>
                    {
                        Identifier = edge,
                        Version = edge,
                        Checkpoint = edge == 0 ? 0 : null,
                        Entries = Entries(edge),
                        Position = new ForeignVector3
                        {
                            x = edge,
                            y = -1,
                            z = 2,
                        },
                    }
                );
            }
        }

        [Test]
        public void RuntimeDefaultsHaveGoldenBytesAndAChangedDefaultIsDetectable()
        {
            RuntimeConfiguredSave<int> value = new RuntimeConfiguredSave<int>();
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] generated));
            CollectionAssert.AreEqual(new byte[] { 0x28, 0, 0xEA, 1, 0 }, generated);
            Verify(value);
            RuntimeTypeModel changed = Model<int>();
            changed[typeof(RuntimeConfiguredSave<int>)][5].DefaultValue = 0;
            CollectionAssert.AreNotEqual(
                OracleBytes(changed, value),
                generated,
                "A runtime-model default change must invalidate exact-byte migration acceptance."
            );
        }

        [Test]
        public void NullAndEmptyReferenceValuesKeepTheirWireDistinction()
        {
            RuntimeConfiguredSave<string> value = new RuntimeConfiguredSave<string>();
            Verify(value);
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] absent));
            value.Identifier = "";
            value.Entries = new List<RuntimeConfiguredEntry>();
            Verify(value);
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] present));
            CollectionAssert.AreNotEqual(absent, present);
            CollectionAssert.AreEqual(new byte[] { 0x2A, 0, 0xEA, 1, 0 }, present);
        }

        [Test]
        public void CharacterizeExplicitScalarDefaults()
        {
            RuntimeTypeModel model = Model<int>();
            model[typeof(RuntimeConfiguredSave<int>)][11].DefaultValue = 7;
            using (MemoryStream stream = new MemoryStream())
            {
                RuntimeConfiguredSave<int> restored =
                    (RuntimeConfiguredSave<int>)
                        model.Deserialize(stream, null, typeof(RuntimeConfiguredSave<int>));
                Assert.AreEqual(
                    0,
                    restored.Version,
                    "Record whether an absent field uses configured defaults or constructor state."
                );
            }
        }
    }
}
