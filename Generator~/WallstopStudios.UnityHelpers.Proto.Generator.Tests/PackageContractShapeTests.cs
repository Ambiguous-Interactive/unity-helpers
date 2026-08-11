// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Drives the shape of every contract this package annotates for WallstopProto through both
    /// serializers, and fails on any disagreement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The annotation gate next door proves the two attribute sets agree; it says nothing about what
    /// they encode to. This says what they encode to, against protobuf-net 3.2.56, in about a second
    /// -- which is what makes porting a contract a red-green step rather than a change that can only
    /// be judged by a CI run an hour later.
    /// </para>
    /// <para>
    /// <b>Why stand-ins rather than the real types.</b> The real contracts reference
    /// <c>UnityEngine</c>, and Unity's assemblies cannot be loaded outside Unity at all: they declare
    /// internal calls with a method body, which CoreCLR refuses. The link back to the real contracts
    /// is <see cref="Mirrors"/>, which <see cref="ContractMirrorTests"/> requires an entry in for
    /// every contract that carries the annotation -- so a contract cannot be ported without stating
    /// what its bytes are.
    /// </para>
    /// <para>
    /// <b>Byte identity is asserted only where it is the contract.</b> A repeated packable member is
    /// written packed here and unpacked by protobuf-net, deliberately and measured; for those shapes
    /// the claim is that each serializer reads what the other writes, which is strictly stronger than
    /// identical output because it exercises both decoders instead of neither.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class PackageContractShapeTests
    {
        /// <summary>
        /// Maps each annotated package contract to the stand-in that pins its bytes.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, Type> Mirrors = new Dictionary<
            string,
            Type
        >(StringComparer.Ordinal)
        {
            ["None"] = typeof(NoneShape),
            ["Line2D"] = typeof(LineShape),
            ["Line3D"] = typeof(LineShape),
            ["Range"] = typeof(RangeShape<>),
            ["SerializableNullable"] = typeof(NullableShape<>),
            ["SerializableType"] = typeof(TypeNameShape),
            ["SerializableList"] = typeof(ListShape<>),
            ["DisjointSet"] = typeof(DisjointShape),
            ["BitSet"] = typeof(BitShape),
            ["AttributeModification"] = typeof(ModificationShape),
            ["PeriodicEffectDefinition"] = typeof(PeriodicShape),
            ["Cache"] = typeof(CacheHolderShape.CacheShape<>),
            ["AbstractRandom"] = typeof(RandomBaseShape),
            ["PcgRandom"] = typeof(RandomLeafShape),
            ["XorShiftRandom"] = typeof(RandomLeafShape),
            ["XoroShiroRandom"] = typeof(RandomLeafShape),
            ["UnityRandom"] = typeof(RandomLeafShape),
            ["LinearCongruentialGenerator"] = typeof(RandomLeafShape),
            ["SquirrelRandom"] = typeof(RandomLeafShape),
            ["RomuDuo"] = typeof(RandomLeafShape),
            ["SplitMix64"] = typeof(RandomLeafShape),
            ["IllusionFlow"] = typeof(RandomLeafShape),
            ["FlurryBurstRandom"] = typeof(RandomLeafShape),
            ["BlastCircuitRandom"] = typeof(RandomLeafShape),
            ["WaveSplatRandom"] = typeof(RandomLeafShape),
            ["DotNetRandom"] = typeof(RandomSkippingShape),
            ["SystemRandom"] = typeof(RandomSkippingShape),
            ["WyRandom"] = typeof(RandomSkippingShape),
            ["PhotonSpinRandom"] = typeof(RandomSkippingShape),
            ["StormDropRandom"] = typeof(RandomSkippingShape),
        };

        // ForeignVector3's surrogate pair is registered for the whole assembly by OracleModelSetup.

        [Test]
        public void AContractWithNoMembersEncodesToNothing()
        {
            AssertIdentical(default(NoneShape), "empty");
        }

        [Test]
        public void ReadonlySurrogatedMembersEncodeAsTheOracleEncodesThem()
        {
            ForeignVector3 origin = default;
            ForeignVector3 point = new ForeignVector3
            {
                x = 1.5f,
                y = -2f,
                z = 0.25f,
            };

            AssertIdentical(new LineShape(origin, origin), "both default");
            AssertIdentical(new LineShape(point, origin), "from only");
            AssertIdentical(new LineShape(origin, point), "to only");
            AssertIdentical(new LineShape(point, point), "both set");
        }

        // The closure is what decides the field key, so each one is its own wire shape.
        [Test]
        public void EveryRangeClosureEncodesAsTheOracleEncodesIt()
        {
            AssertIdentical(
                new RangeShape<int>
                {
                    min = 0,
                    max = 0,
                    startInclusive = false,
                    endInclusive = false,
                },
                "int, all defaults"
            );
            AssertIdentical(
                new RangeShape<int>
                {
                    min = -5,
                    max = 300,
                    startInclusive = true,
                    endInclusive = false,
                },
                "int"
            );
            AssertIdentical(
                new RangeShape<float>
                {
                    min = 0.5f,
                    max = 2.5f,
                    startInclusive = true,
                    endInclusive = true,
                },
                "float"
            );
            AssertIdentical(
                new RangeShape<double>
                {
                    min = double.MinValue,
                    max = double.MaxValue,
                    startInclusive = false,
                    endInclusive = true,
                },
                "double"
            );
            AssertIdentical(
                new RangeShape<long>
                {
                    min = long.MinValue,
                    max = long.MaxValue,
                    startInclusive = true,
                    endInclusive = true,
                },
                "long"
            );
        }

        [Test]
        public void ANullableShapeEncodesAsTheOracleEncodesIt()
        {
            AssertIdentical(new NullableShape<int> { hasValue = false, value = 0 }, "absent");
            AssertIdentical(new NullableShape<int> { hasValue = true, value = 0 }, "present, zero");
            AssertIdentical(new NullableShape<int> { hasValue = true, value = 7 }, "present");
            AssertIdentical(
                new NullableShape<double> { hasValue = true, value = -0.5 },
                "present, double"
            );
        }

        [Test]
        public void AStringMemberEncodesAsTheOracleEncodesIt()
        {
            AssertIdentical(new TypeNameShape { name = null, cached = 9 }, "null");
            AssertIdentical(new TypeNameShape { name = string.Empty, cached = 9 }, "empty");
            AssertIdentical(
                new TypeNameShape { name = "System.Int32, mscorlib", cached = 9 },
                "populated"
            );
            AssertIdentical(new TypeNameShape { name = "é中", cached = 0 }, "non-ascii");
        }

        // protobuf-net takes the LIST reading of this shape and writes the enclosing type's own
        // elements; the generated formatter takes the MESSAGE reading and writes the backing field.
        // They coincide because the field is at tag 1 and holds exactly those elements -- which is
        // the whole reason this contract is portable, so it is measured in both directions.
        [Test]
        public void AListShapedContractInteroperatesInBothDirections()
        {
            AssertInterops(new ListShape<int>(), "empty");
            AssertInterops(new ListShape<int> { 0 }, "one default element");
            AssertInterops(new ListShape<int> { 1, 2, 300 }, "three");
            AssertInterops(new ListShape<string> { "a", string.Empty }, "strings");
            AssertInterops(new ListShape<double> { 0.5, -1 }, "doubles");
        }

        [Test]
        public void ArraysBehindAPrivateConstructorInteroperateInBothDirections()
        {
            AssertInterops(new DisjointShape(null, null, 0), "all absent");
            AssertInterops(new DisjointShape(Array.Empty<int>(), Array.Empty<int>(), 0), "empty");
            AssertInterops(new DisjointShape(new[] { 0, 1, 1 }, new[] { 0, 0, 1 }, 2), "populated");
        }

        [Test]
        public void ABitSetShapedContractInteroperatesInBothDirections()
        {
            AssertInterops(new BitShape(), "default");
            AssertInterops(new BitShape(Array.Empty<ulong>(), 0), "empty");
            AssertInterops(new BitShape(new ulong[] { 5, 9 }, 128), "populated");
            AssertInterops(new BitShape(new[] { ulong.MaxValue }, 64), "saturated");
        }

        [Test]
        public void AStringEnumFloatStructEncodesAsTheOracleEncodesIt()
        {
            AssertIdentical(default(ModificationShape), "all defaults");
            AssertIdentical(
                new ModificationShape
                {
                    attribute = "Health",
                    action = ModificationActionShape.Addition,
                    value = -5f,
                },
                "populated"
            );
            AssertIdentical(
                new ModificationShape
                {
                    attribute = string.Empty,
                    action = ModificationActionShape.None,
                    value = 0f,
                },
                "empty string, default enum"
            );
        }

        [Test]
        public void ARepeatedContractMemberInteroperatesInBothDirections()
        {
            AssertInterops(new PeriodicShape { interval = 0f }, "all defaults");
            AssertInterops(
                new PeriodicShape
                {
                    name = "burn",
                    initialDelay = 0.5f,
                    interval = 2f,
                    maxTicks = 3,
                    modifications =
                    {
                        new ModificationShape
                        {
                            attribute = "Health",
                            action = ModificationActionShape.Addition,
                            value = -5f,
                        },
                    },
                },
                "one modification"
            );

            // A default element still gets its sub-message, or the count changes on the way back.
            AssertInterops(
                new PeriodicShape { interval = 0f, modifications = { default } },
                "one default modification"
            );
        }

        /// <summary>
        /// A subtype encodes the same under its own declared type as it does under its base's.
        /// </summary>
        /// <remarks>
        /// <para>
        /// protobuf-net always writes from the outermost contract in the chain, so
        /// <c>Serialize&lt;Alpha&gt;</c> and <c>Serialize&lt;Base&gt;</c> produce identical bytes --
        /// measured, not assumed. Registering the formatter that writes only the subtype's own
        /// members produced the include payload on its own, which protobuf-net then read as the
        /// BASE's fields: <c>AlphaOnly</c> arriving as <c>Id</c>, with no error anywhere.
        /// </para>
        /// <para>
        /// This is the shape <c>AbstractRandom</c> and its seventeen generators have, and a saved
        /// generator that reloads with its fields shuffled is a different game.
        /// </para>
        /// </remarks>
        [Test]
        public void ASubtypeEncodesTheSameUnderItsOwnDeclaredTypeAsUnderItsBase()
        {
            IncludeAlpha alpha = new IncludeAlpha
            {
                Id = 1,
                Label = "L",
                AlphaOnly = 2,
                AlphaText = "A",
            };

            Assert.AreEqual(
                Hex(OracleWriteAs<IncludeBase>(alpha)),
                Hex(OracleWrite(alpha)),
                "the oracle's own two spellings disagree, so the premise is wrong"
            );
            AssertIdentical(alpha, "leaf subtype as its own root");

            IncludeGamma gamma = new IncludeGamma
            {
                Id = 3,
                BetaOnly = 0.5,
                GammaOnly = true,
            };

            AssertIdentical(gamma, "three levels deep");
            AssertIdentical<IncludeBeta>(gamma, "middle level holding a deeper runtime type");
        }

        /// <summary>
        /// The generator family's shape, in every position a saved generator can be in.
        /// </summary>
        /// <remarks>
        /// Seventeen contracts share it, and all seventeen are reached through the base's includes.
        /// The declared type is asserted both ways round because a generator is usually held as
        /// <c>IRandom</c> and saved as its concrete self.
        /// </remarks>
        [Test]
        public void TheGeneratorFamilyEncodesAsTheOracleEncodesIt()
        {
            RandomLeafShape leaf = new RandomLeafShape
            {
                cachedGaussian = 0.25,
                bitBuffer = 12,
                bitCount = 3,
                byteBuffer = uint.MaxValue,
                byteCount = -1,
                state = ulong.MaxValue,
                increment = 7,
                elements = new uint[] { 1, 0, uint.MaxValue },
                word = 5,
                index = 2,
                seed = -1,
            };

            AssertInterops(leaf, "leaf, every member populated");
            AssertInterops<RandomBaseShape>(leaf, "leaf through the base");
            AssertIdentical(new RandomLeafShape(), "leaf, all defaults");
            AssertIdentical(
                new RandomLeafShape { cachedGaussian = null, seed = null },
                "both nullables absent"
            );
            AssertIdentical(
                new RandomLeafShape { cachedGaussian = 0, seed = 0 },
                "both nullables present and zero"
            );

            // The base's own five members, with nothing from a subtype, so a renumbering there
            // cannot hide behind the subtype's bytes.
            AssertIdentical(
                new RandomLeafShape
                {
                    cachedGaussian = -0.5,
                    bitBuffer = 1,
                    bitCount = 2,
                    byteBuffer = 3,
                    byteCount = 4,
                },
                "base members only"
            );

            RandomSkippingShape skipping = new RandomSkippingShape
            {
                bitBuffer = 1,
                byteBuffer = 2,
                byteCount = 3,
                generated = 9,
                seed = 7,
                elements = new uint[] { 4, 5 },
                word = 6,
                primed = true,
                pending = new byte[] { 1, 2, 3 },
            };

            AssertInterops(skipping, "skip-constructor leaf");
            AssertInterops<RandomBaseShape>(skipping, "skip-constructor leaf through the base");
            AssertIdentical(
                new RandomSkippingShape { pending = Array.Empty<byte>() },
                "an empty blob, which is written where a null one is not"
            );
        }

        [Test]
        public void AGenericContractNestedInANonGenericTypeEncodesAsTheOracleEncodesIt()
        {
            AssertIdentical(new CacheHolderShape.CacheShape<int> { Data = 7 }, "int");
            AssertIdentical(new CacheHolderShape.CacheShape<int> { Data = 0 }, "int, default");
            AssertIdentical(new CacheHolderShape.CacheShape<string> { Data = "hi" }, "string");
            AssertIdentical(new CacheHolderShape.CacheShape<double> { Data = -0.5 }, "double");
        }

        /// <summary>
        /// Asserts byte identity with protobuf-net, then that each serializer reads the other's
        /// output back to the same value.
        /// </summary>
        /// <typeparam name="T">The contract type.</typeparam>
        /// <param name="value">The value to encode.</param>
        /// <param name="context">What is being encoded, for the failure message.</param>
        private static void AssertIdentical<T>(T value, string context)
        {
            Assert.AreEqual(
                Hex(OracleWrite(value)),
                Hex(MineWrite(value)),
                typeof(T).Name + " / " + context
            );
            AssertInterops(value, context);
        }

        /// <summary>
        /// Asserts that each serializer reads what the other writes, without requiring the two to
        /// produce identical bytes.
        /// </summary>
        /// <typeparam name="T">The contract type.</typeparam>
        /// <param name="value">The value to encode.</param>
        /// <param name="context">What is being encoded, for the failure message.</param>
        /// <remarks>
        /// <para>
        /// Decoded values are compared by re-encoding them with the oracle rather than by an
        /// equality member, because most of these types have none and one written for the test would
        /// be free to ignore the field that broke.
        /// </para>
        /// <para>
        /// The baseline is protobuf-net's OWN round trip, not the original value. A field with a
        /// non-default initializer -- <c>interval = 1f</c> on the periodic definition -- cannot come
        /// back as <c>0</c> through any protobuf encoder, because the proto default is omitted and
        /// the initializer runs again on the way in. That is a property of the wire format both
        /// serializers implement, and comparing against the original would report it as a
        /// disagreement between them.
        /// </para>
        /// </remarks>
        private static void AssertInterops<T>(T value, string context)
        {
            string where = typeof(T).Name + " / " + context;
            byte[] theirs = OracleWrite(value);
            byte[] mine = MineWrite(value);
            string expected = Hex(OracleWrite(OracleRead<T>(theirs)));

            Assert.AreEqual(
                expected,
                Hex(OracleWrite(OracleRead<T>(mine))),
                where + ": protobuf-net reading mine (" + Hex(mine) + ")"
            );
            Assert.AreEqual(
                expected,
                Hex(OracleWrite(MineRead<T>(theirs, where))),
                where + ": mine reading protobuf-net's (" + Hex(theirs) + ")"
            );
            Assert.AreEqual(
                expected,
                Hex(OracleWrite(MineRead<T>(mine, where))),
                where + ": mine reading mine (" + Hex(mine) + ")"
            );
        }

        private static byte[] OracleWrite<T>(T value)
        {
            using MemoryStream stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, value);
            return stream.ToArray();
        }

        private static byte[] OracleWriteAs<TDeclared>(TDeclared value)
        {
            using MemoryStream stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize<TDeclared>(stream, value);
            return stream.ToArray();
        }

        private static T OracleRead<T>(byte[] bytes)
        {
            using MemoryStream stream = new MemoryStream(bytes);
            return ProtoBuf.Serializer.Deserialize<T>(stream);
        }

        private static byte[] MineWrite<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value), typeof(T).Name + " write");

            // Measure has to predict Write exactly: the buffer is sized from it and a sub-message's
            // length prefix is written from it, so a short measure is a truncated payload rather
            // than an error.
            Assert.AreEqual(buffer.Length, writer.Position, typeof(T).Name + " measure vs write");
            return buffer;
        }

        private static T MineRead<T>(byte[] bytes, string where)
        {
            WProtoReader reader = new WProtoReader(bytes);
            Assert.IsTrue(
                WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T value),
                where + ": read refused " + Hex(bytes)
            );
            return value;
        }

        private static string Hex(byte[] bytes)
        {
            return bytes.Length == 0 ? "<empty>" : BitConverter.ToString(bytes);
        }
    }
}
