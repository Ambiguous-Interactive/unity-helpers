// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Math;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Every type served by a <c>[WProtoSurrogate]</c> registration encodes to the bytes protobuf-net
    /// writes for it, at every value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A surrogate and its generated formatter are two independent encoders for one type, and nothing
    /// but a comparison stops them drifting. One already did: <c>FastVector2IntSurrogate</c> mirrored a
    /// cached hash through <c>GetHashCode()</c>, which stopped agreeing with the stored field, and
    /// protobuf-net wrote six bytes for <c>default(FastVector2Int)</c> where the formatter wrote none.
    /// That was caught only because those two types were the two that had a gate.
    /// </para>
    /// <para>
    /// The comparison is made at the root rather than inside a holder contract, because the root is
    /// where the two encoders differ if they differ at all: at a member both sides add the same
    /// length-delimited wrapper around exactly these bytes.
    /// </para>
    /// <para>
    /// <c>WProtoFacade.TrySerialize</c> is deliberately not used. A type served through an assembly
    /// surrogate is not registered as its own <c>IWProtoFormatter&lt;T&gt;</c>, so the facade answers
    /// "not mine" for <see cref="Vector3"/> while serving it perfectly well as a member -- the same
    /// trap as <c>IsRegistered&lt;T&gt;()</c>. The surrogate's own formatter is what the member path
    /// runs, so that is what is compared.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoSurrogateParityTests
    {
        /// <summary>
        /// The real types this fixture compares. Kept in step with the registrations by
        /// <see cref="EveryRegisteredSurrogateIsGated"/>, which fails when the two disagree.
        /// </summary>
        private static readonly HashSet<Type> Gated = new()
        {
            typeof(Vector2),
            typeof(Vector3),
            typeof(Quaternion),
            typeof(Color),
            typeof(Color32),
            typeof(Rect),
            typeof(RectInt),
            typeof(Bounds),
            typeof(BoundsInt),
            typeof(Vector2Int),
            typeof(Vector3Int),
            typeof(Resolution),
            typeof(Parabola),
            typeof(ImmutableBitSet),
            typeof(ValueTuple<,>),
            typeof(ValueTuple<,,>),
        };

        /// <summary>
        /// The surrogates whose bytes are deliberately not protobuf-net's, and the reason.
        /// </summary>
        /// <remarks>
        /// <see cref="ImmutableBitSet"/>'s surrogate is the only one holding a repeated scalar, and
        /// WallstopProto writes a repeated scalar as a single packed run where protobuf-net at
        /// CompatibilityLevel 200 writes one field key per element. That is a deliberate choice --
        /// it roughly halves the payload -- and it is safe because each reader accepts the other's
        /// spelling, which is what <see cref="EachEncoderReadsWhatTheOtherWrote"/> asserts for every
        /// type here, this one included.
        /// </remarks>
        private static readonly HashSet<Type> WritesAPackedRun = new() { typeof(ImmutableBitSet) };

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void EverySurrogatedTypeMatchesProtobufNetByteForByte()
        {
            // Skipped under IL2CPP because the ORACLE cannot run there: protobuf-net's TypeHelper<T>
            // static constructor throws, which is the whole reason WallstopProto exists.
            //
            // The surrogates are registered by a static constructor that nothing in this fixture would
            // otherwise touch, and protobuf-net without them refuses Vector2 outright rather than
            // writing different bytes -- so the oracle has to be woken before it is asked.
            ProtobufUnityModel.EnsureInitialized();
            List<string> mismatches = new();
            RunEveryCase(mismatches, Mode.Bytes);
            Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));
        }

        private static void RunEveryCase(List<string> mismatches, Mode mode)
        {
            AssertParity(
                mismatches,
                mode,
                (Vector2 value) => (Vector2Surrogate)value,
                new Vector2(1.5f, -2.25f),
                default,
                new Vector2(float.MaxValue, float.NaN)
            );
            AssertParity(
                mismatches,
                mode,
                (Vector3 value) => (Vector3Surrogate)value,
                new Vector3(1f, -2f, 3.5f),
                default,
                new Vector3(float.NegativeInfinity, 0f, float.Epsilon)
            );
            AssertParity(
                mismatches,
                mode,
                (Quaternion value) => (QuaternionSurrogate)value,
                new Quaternion(0.1f, 0.2f, 0.3f, 0.9f),
                default,
                Quaternion.identity
            );
            AssertParity(
                mismatches,
                mode,
                (Color value) => (ColorSurrogate)value,
                new Color(0.25f, 0.5f, 0.75f, 1f),
                default,
                new Color(-1f, 2f, 0f, 0f)
            );
            AssertParity(
                mismatches,
                mode,
                (Color32 value) => (Color32Surrogate)value,
                new Color32(1, 2, 3, 4),
                default,
                new Color32(255, 255, 255, 255)
            );
            AssertParity(
                mismatches,
                mode,
                (Rect value) => (RectSurrogate)value,
                new Rect(1f, 2f, 3f, 4f),
                default,
                new Rect(-1.5f, 0f, float.MaxValue, 0f)
            );
            AssertParity(
                mismatches,
                mode,
                (RectInt value) => (RectIntSurrogate)value,
                new RectInt(1, 2, 3, 4),
                default,
                new RectInt(int.MinValue, 0, int.MaxValue, -1)
            );
            AssertParity(
                mismatches,
                mode,
                (Bounds value) => (BoundsSurrogate)value,
                new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f)),
                default,
                new Bounds(Vector3.zero, new Vector3(float.MaxValue, 0f, 0f))
            );
            AssertParity(
                mismatches,
                mode,
                (BoundsInt value) => (BoundsIntSurrogate)value,
                new BoundsInt(1, 2, 3, 4, 5, 6),
                default,
                new BoundsInt(-1, -2, -3, int.MaxValue, 0, 1)
            );
            AssertParity(
                mismatches,
                mode,
                (Vector2Int value) => (Vector2IntSurrogate)value,
                new Vector2Int(5, -3),
                default,
                new Vector2Int(int.MinValue, int.MaxValue)
            );
            AssertParity(
                mismatches,
                mode,
                (Vector3Int value) => (Vector3IntSurrogate)value,
                new Vector3Int(5, -3, 7),
                default,
                new Vector3Int(int.MinValue, 0, int.MaxValue)
            );
            AssertParity(
                mismatches,
                mode,
                (Resolution value) => (ResolutionSurrogate)value,
                new Resolution { width = 1920, height = 1080 },
                default,
                new Resolution { width = int.MaxValue, height = -1 }
            );
            AssertParity(
                mismatches,
                mode,
                (Parabola value) => (ParabolaSurrogate)value,
                new Parabola(maxHeight: 3f, length: 8f),
                default,
                new Parabola(maxHeight: 0.001f, length: float.MaxValue)
            );
            AssertParity(
                mismatches,
                mode,
                (ImmutableBitSet value) => (ImmutableBitSetSurrogate)value,
                BitSet(0, 63, 64, 200),
                default,
                BitSet()
            );
            AssertParity(
                mismatches,
                mode,
                (ValueTuple<int, string> value) => (SerializableValueTuple<int, string>)value,
                new ValueTuple<int, string>(7, "pair"),
                default,
                new ValueTuple<int, string>(0, string.Empty)
            );
            AssertParity(
                mismatches,
                mode,
                (ValueTuple<int, string, double> value) =>
                    (SerializableValueTuple<int, string, double>)value,
                new ValueTuple<int, string, double>(8, "triple", 0.5d),
                default,
                new ValueTuple<int, string, double>(0, string.Empty, 0d)
            );
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void EachEncoderReadsWhatTheOtherWrote()
        {
            // The guarantee that survives the packing difference, and the one a save file actually
            // needs: bytes written by either encoder are understood by the other, exactly. Re-encoding
            // rather than comparing values, because a float member carrying NaN is never equal to
            // itself and a bit pattern is.
            ProtobufUnityModel.EnsureInitialized();
            List<string> mismatches = new();
            RunEveryCase(mismatches, Mode.CrossRead);
            Assert.IsEmpty(mismatches, string.Join(Environment.NewLine, mismatches));
        }

        /// <summary>
        /// Every surrogate this package declares actually reached protobuf-net's model.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>RuntimeTypeModel.Default</c> is process-global and freezes a type the first time it
        /// serializes one, so a registration can be refused rather than applied -- and a refused
        /// surrogate is silent: the type keeps serializing, with different bytes. The registrations
        /// used to share one <c>try</c>, which made a single refusal skip every one after it.
        /// </para>
        /// <para>
        /// Skipped under IL2CPP because there a refusal is expected rather than a defect, and this
        /// assertion cannot tell the two apart. protobuf-net builds its serializers by reflection
        /// and the AOT compiler cannot emit them, so a standalone player refuses <c>Vector2</c>,
        /// <c>Vector3</c>, <c>Rect</c>, <c>RectInt</c>, <c>Bounds</c>, <c>BoundsInt</c>,
        /// <c>Vector2Int</c> and <c>Vector3Int</c> outright -- measured on the standalone legs,
        /// which refuse exactly those eight. Those types are served by WallstopProto there, so
        /// nothing encodes wrongly; the property this test is about only means something on the
        /// backend where protobuf-net can run at all.
        /// </para>
        /// </remarks>
        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void EverySurrogateThisPackageDeclaresWasActuallyRegistered()
        {
            ProtobufUnityModel.EnsureInitialized();
            Assert.IsEmpty(
                ProtobufUnityModel.RegistrationFailures,
                "protobuf-net had already bound these types, so they now encode with bytes this "
                    + "package does not document: "
                    + string.Join(", ", ProtobufUnityModel.RegistrationFailures)
            );
        }

        /// <summary>
        /// The public accessor must answer the same question the internal list does, and must wake
        /// the static constructor itself.
        /// </summary>
        /// <remarks>
        /// The accessor exists so a game can refuse to write a save it will not read back. Its whole
        /// hazard is the ordering trap that caused the defect it reports on: the failures are
        /// recorded by a static constructor, so an accessor that does not trigger it reports "ready"
        /// purely because nothing has run yet.
        /// </remarks>
        [Test]
        public void ProtobufSurrogatesReadyAgreesWithTheRecordedFailures()
        {
            bool ready = Serializer.ProtobufSurrogatesReady(out IReadOnlyList<string> refusedTypes);

            Assert.IsTrue(refusedTypes != null, "The refused list must never be null.");
#if ENABLE_IL2CPP
            Assert.IsTrue(
                ready,
                "Under IL2CPP these types are encoded by WallstopProto, so a refused protobuf-net "
                    + "registration changes nothing a consumer can observe."
            );
            Assert.IsEmpty(refusedTypes);
#else
            Assert.AreEqual(
                ProtobufUnityModel.RegistrationFailures.Count == 0,
                ready,
                "The public answer disagreed with the recorded failures."
            );
            CollectionAssert.AreEqual(ProtobufUnityModel.RegistrationFailures, refusedTypes);
#endif
        }

        /// <summary>
        /// Reading the report must not let a caller edit it.
        /// </summary>
        [Test]
        public void ProtobufSurrogatesReadyHandsOutAReadOnlyList()
        {
            Serializer.ProtobufSurrogatesReady(out IReadOnlyList<string> refusedTypes);

            Assert.IsFalse(
                refusedTypes is List<string>,
                "The accessor handed out the mutable backing list, so a caller could rewrite the "
                    + "package's own record of what failed."
            );
        }

        [Test]
        public void EveryRegisteredSurrogateIsGated()
        {
            // Attribute metadata rather than reflection into implementation: the registrations ARE
            // assembly attributes, and reading them is the only way this list stays honest the day
            // a fifteenth surrogate is added rather than the day somebody remembers it.
            HashSet<Type> registered = new();
            foreach (
                Attribute declared in Attribute.GetCustomAttributes(
                    typeof(WProtoFacade).Assembly,
                    typeof(WProtoSurrogateAttribute)
                )
            )
            {
                registered.Add(((WProtoSurrogateAttribute)declared).RealType);
            }

            Assert.IsNotEmpty(registered, "No surrogate registrations found at all.");
            CollectionAssert.AreEquivalent(
                registered,
                Gated,
                "The gated list and the shipped registrations disagree. Add the missing type to "
                    + nameof(EverySurrogatedTypeMatchesProtobufNetByteForByte)
                    + " so a new surrogate is covered the day it is registered."
            );
        }

        private static ImmutableBitSet BitSet(params int[] setBits)
        {
            BitSet builder = new(256);
            foreach (int bit in setBits)
            {
                Assert.IsTrue(builder.TrySet(bit));
            }

            return builder.ToImmutable();
        }

        private static void AssertParity<TReal, TSurrogate>(
            List<string> mismatches,
            Mode mode,
            Func<TReal, TSurrogate> toSurrogate,
            params TReal[] values
        )
        {
            IWProtoFormatter<TSurrogate> formatter = WProtoFormatterProvider.Get<TSurrogate>();
            foreach (TReal value in values)
            {
                TSurrogate surrogate = toSurrogate(value);
                if (!TryWrite(formatter, surrogate, out byte[] mine, out string failure))
                {
                    mismatches.Add($"{typeof(TReal).Name} {value}: {failure}");
                    continue;
                }

                using MemoryStream stream = new();
                ProtoBuf.Serializer.Serialize(stream, value);
                byte[] theirs = stream.ToArray();

                if (mode == Mode.Bytes)
                {
                    if (WritesAPackedRun.Contains(typeof(TReal)) || AreEqual(theirs, mine))
                    {
                        continue;
                    }

                    mismatches.Add(
                        $"{typeof(TReal).Name} {value}: protobuf-net wrote {Hex(theirs)}, "
                            + $"WallstopProto wrote {Hex(mine)}"
                    );
                    continue;
                }

                // protobuf-net reading what WallstopProto wrote. Compared by re-encoding rather than
                // by value: the surrogate round trip is what has to survive, and a float member
                // holding NaN never equals itself.
                using MemoryStream theirsFromMine = new(mine);
                TReal decodedByThem = ProtoBuf.Serializer.Deserialize<TReal>(theirsFromMine);
                using MemoryStream reEncoded = new();
                ProtoBuf.Serializer.Serialize(reEncoded, decodedByThem);
                if (!AreEqual(theirs, reEncoded.ToArray()))
                {
                    mismatches.Add(
                        $"{typeof(TReal).Name} {value}: protobuf-net read WallstopProto's "
                            + $"{Hex(mine)} back as {Hex(reEncoded.ToArray())}, not {Hex(theirs)}"
                    );
                }

                // WallstopProto reading what protobuf-net wrote.
                WProtoReader reader = new(theirs);
                if (!formatter.TryRead(ref reader, out TSurrogate decodedByMe))
                {
                    mismatches.Add(
                        $"{typeof(TReal).Name} {value}: formatter refused protobuf-net's {Hex(theirs)}"
                    );
                    continue;
                }

                if (!TryWrite(formatter, decodedByMe, out byte[] mineAgain, out failure))
                {
                    mismatches.Add($"{typeof(TReal).Name} {value}: {failure}");
                    continue;
                }

                if (!AreEqual(mine, mineAgain))
                {
                    mismatches.Add(
                        $"{typeof(TReal).Name} {value}: WallstopProto read protobuf-net's "
                            + $"{Hex(theirs)} back as {Hex(mineAgain)}, not {Hex(mine)}"
                    );
                }
            }
        }

        private static bool TryWrite<TSurrogate>(
            IWProtoFormatter<TSurrogate> formatter,
            TSurrogate surrogate,
            out byte[] bytes,
            out string failure
        )
        {
            int measured = formatter.Measure(surrogate);
            bytes = new byte[measured];
            WProtoWriter writer = new(bytes);
            if (!formatter.Write(ref writer, surrogate))
            {
                failure = "formatter refused to write";
                return false;
            }

            if (writer.Position != measured)
            {
                failure = $"measured {measured} but wrote {writer.Position}";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool AreEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; ++i)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string Hex(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return "<empty>";
            }

            System.Text.StringBuilder builder = new(bytes.Length * 2);
            foreach (byte current in bytes)
            {
                builder.Append(current.ToString("X2"));
            }

            return builder.ToString();
        }

        private enum Mode
        {
            Bytes = 0,
            CrossRead = 1,
        }
    }
}
