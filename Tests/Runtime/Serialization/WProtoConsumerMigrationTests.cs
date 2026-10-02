// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Serialization.ConsumerMigration;
#if !ENABLE_IL2CPP
    using ProtoBuf.Meta;
#endif

    /// <summary>Proves static consumer declarations preserve runtime-only models on both oracles.</summary>
    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoConsumerMigrationTests
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

#if !ENABLE_IL2CPP
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
            MetaType surrogate = model.Add(typeof(ForeignVector3Surrogate), false);
            surrogate.Add(1, nameof(ForeignVector3Surrogate.x));
            surrogate.Add(2, nameof(ForeignVector3Surrogate.y));
            surrogate.Add(3, nameof(ForeignVector3Surrogate.z));
            surrogate[1].DefaultValue = 0f;
            surrogate[2].DefaultValue = 0f;
            surrogate[3].DefaultValue = 0f;
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
#endif

        private static void Verify<TIdentifier>(RuntimeConfiguredSave<TIdentifier> value)
        {
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] generated));
#if !ENABLE_IL2CPP
            RuntimeTypeModel model = Model<TIdentifier>();
            byte[] original = OracleBytes(model, value);
            CollectionAssert.AreEqual(
                original,
                generated,
                "The original model defines the saved bytes."
            );
#else
            byte[] original = generated;
#endif
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(
                    original,
                    out RuntimeConfiguredSave<TIdentifier> migrated
                )
            );
            AssertValue(value, migrated);
#if !ENABLE_IL2CPP
            using (MemoryStream stream = new MemoryStream(generated))
            {
                RuntimeConfiguredSave<TIdentifier> restored =
                    (RuntimeConfiguredSave<TIdentifier>)
                        model.Deserialize(stream, null, typeof(RuntimeConfiguredSave<TIdentifier>));
                AssertValue(value, restored);
            }
#endif
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
            for (int index = 0; index < count; ++index)
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

        [TestCase(0, false, TestName = "ClosedTargets.Int.Zero")]
        [TestCase(1, false, TestName = "ClosedTargets.Int.One")]
        [TestCase(-1, false, TestName = "ClosedTargets.Int.NegativeOne")]
        [TestCase(int.MinValue, false, TestName = "ClosedTargets.Int.Minimum")]
        [TestCase(int.MaxValue, false, TestName = "ClosedTargets.Int.Maximum")]
        [TestCase(0, true, TestName = "ClosedTargets.String.Zero")]
        [TestCase(-1, true, TestName = "ClosedTargets.String.NegativeOne")]
        [TestCase(int.MinValue, true, TestName = "ClosedTargets.String.Minimum")]
        [TestCase(int.MaxValue, true, TestName = "ClosedTargets.String.Maximum")]
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
        public void CombinedMigrationReadsRetainedGoldenBytes()
        {
#if UNITY_5_3_OR_NEWER
            Assert.AreNotEqual(
                typeof(RuntimeConfiguredSave<int>).Assembly,
                typeof(WProtoConsumerMigrationTests).Assembly
            );
#endif
            byte[] retained =
            {
                0x28,
                0,
                0x88,
                1,
                0,
                0xBA,
                1,
                6,
                0x3A,
                4,
                0x62,
                0x61,
                0x73,
                0x65,
                0xBA,
                1,
                7,
                0xAA,
                6,
                2,
                0x68,
                0,
                0x3A,
                0,
                0xBA,
                1,
                12,
                0xAA,
                6,
                2,
                0x68,
                0,
                0x3A,
                5,
                0x63,
                0x61,
                0x66,
                0xC3,
                0xA9,
                0xEA,
                1,
                10,
                0x15,
                0,
                0,
                0x80,
                0xBF,
                0x1D,
                0,
                0,
                0,
                0x40,
            };
            RuntimeConfiguredSave<int> expected = new RuntimeConfiguredSave<int>
            {
                Checkpoint = 0,
                Entries = Entries(0),
                Position = new ForeignVector3 { y = -1, z = 2 },
            };
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(retained, out RuntimeConfiguredSave<int> actual)
            );
            AssertValue(expected, actual);
            Assert.IsTrue(WProtoFacade.TrySerialize(actual, out byte[] rewritten));
            CollectionAssert.AreEqual(retained, rewritten);
#if !ENABLE_IL2CPP
            CollectionAssert.AreEqual(OracleBytes(Model<int>(), expected), retained);
#endif
        }

        [Test]
        public void RuntimeDefaultsHaveGoldenBytesAndAChangedDefaultIsDetectable()
        {
            RuntimeConfiguredSave<int> value = new RuntimeConfiguredSave<int>();
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] generated));
            CollectionAssert.AreEqual(new byte[] { 0x28, 0, 0xEA, 1, 0 }, generated);
            Verify(value);
#if !ENABLE_IL2CPP
            RuntimeTypeModel changed = Model<int>();
            changed[typeof(RuntimeConfiguredSave<int>)][5].DefaultValue = 0;
            CollectionAssert.AreNotEqual(
                OracleBytes(changed, value),
                generated,
                "A runtime-model default change must invalidate exact-byte migration acceptance."
            );
#endif
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

#if !ENABLE_IL2CPP
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
#endif
    }
}
