// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Math;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Every struct <c>ProtobufUnityModel</c> routes through a surrogate survives a root
    /// <c>ProtoSerialize</c>/<c>ProtoDeserialize</c>, and has a WallstopProto path that makes that
    /// possible without protobuf-net.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <b>not</b> marked <c>[SkipUnderIL2CPP]</c>, unlike the proto round-trip fixtures
    /// beside it. Those are skipped because they exercise protobuf-net, which cannot run there; this
    /// fixture exists to prove these roots no longer need protobuf-net, so skipping it on the one
    /// backend that can fail the claim would leave the claim untested. It is the same reasoning
    /// <c>JsonAotConverterTests</c> gives for the converter registry.
    /// </para>
    /// <para>
    /// The failure it pins is issue #696. A <c>[WProtoSurrogate]</c> registration substitutes the
    /// surrogate for a MEMBER only, so the generated formatter is
    /// <c>IWProtoFormatter&lt;XSurrogate&gt;</c> and never <c>IWProtoFormatter&lt;X&gt;</c>. A root
    /// <c>X</c> therefore fell through to protobuf-net, which builds
    /// <c>ProtoBuf.Internal.StructValueChecker&lt;X&gt;</c> -- a closed generic no source names, so
    /// IL2CPP compiles nothing for it. Unity 2021.3.45f1 threw <c>ExecutionEngineException</c> and
    /// Unity 6000.5.2f1 handed back a default value in silence, which is the worse of the two: an
    /// <see cref="ImmutableBitSet"/> came back with <c>Capacity == 0</c> and no error.
    /// </para>
    /// <para>
    /// Every case is closed over a <b>value</b> type and named in this assembly's source, which is
    /// what makes the coverage real: IL2CPP shares compiled code between reference-type
    /// instantiations, so a closure over a class is generated whether or not anything names it,
    /// while a value type gets its own copy or none at all.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoUnitySurrogateMarshalTests
    {
        /// <summary>
        /// The types served at the root by a hand-written formatter rather than by a marshal.
        /// </summary>
        /// <remarks>
        /// Both keep a formatter that recomputes the cached hash instead of trusting it from the
        /// wire, which a marshal delegating to the surrogate would not do.
        /// <see cref="EverySurrogatedStructHasAWallstopProtoRootPath"/> checks the claim rather than
        /// believing this sentence.
        /// </remarks>
        private static readonly Type[] ServedByAHandWrittenFormatter =
        {
            typeof(FastVector2Int),
            typeof(FastVector3Int),
        };

        [TestCaseSource(nameof(SurrogatedRootCases))]
        public void EverySurrogatedStructRoundTripsAtTheRoot(Action roundTrip)
        {
            roundTrip();
        }

        /// <summary>
        /// A future surrogate cannot be added without a root path.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The two lists live in different files and nothing but this connects them. A surrogate
        /// added to <c>ProtobufUnityModel</c> without a root marshal has no visible symptom in the
        /// editor -- protobuf-net answers, exactly as before -- and becomes issue #696 again in a
        /// shipped IL2CPP player, where that path cannot run.
        /// </para>
        /// <para>
        /// Read from <c>ProtobufUnityModel.Surrogated</c>, which <c>Register</c> itself fills in, and
        /// from the <c>[assembly: WProtoRootMarshal]</c> attributes, which ARE the registrations.
        /// Neither is reflection into our own implementation: the first is an internal member this
        /// assembly is granted, and the second is attribute metadata.
        /// </para>
        /// </remarks>
        [Test]
        public void EverySurrogatedStructHasAWallstopProtoRootPath()
        {
            ProtobufUnityModel.EnsureInitialized();

            HashSet<Type> rootServed = new HashSet<Type>();
            foreach (
                Attribute declared in Attribute.GetCustomAttributes(
                    typeof(WProtoFacade).Assembly,
                    typeof(WProtoRootMarshalAttribute)
                )
            )
            {
                rootServed.Add(((WProtoRootMarshalAttribute)declared).RealType);
            }

            Assert.IsNotEmpty(
                rootServed,
                "No root marshal registrations were found at all, so this gate had no subjects."
            );
            Assert.IsNotEmpty(
                ProtobufUnityModel.Surrogated,
                "ProtobufUnityModel recorded no surrogate at all, so this gate had no subjects."
            );

            Assert.IsTrue(
                WProtoFormatterProvider.IsRegistered<FastVector2Int>(),
                "FastVector2Int is excused from needing a marshal because a hand-written formatter "
                    + "serves it, and that formatter is not registered."
            );
            Assert.IsTrue(
                WProtoFormatterProvider.IsRegistered<FastVector3Int>(),
                "FastVector3Int is excused from needing a marshal because a hand-written formatter "
                    + "serves it, and that formatter is not registered."
            );

            foreach (Type handWritten in ServedByAHandWrittenFormatter)
            {
                rootServed.Add(handWritten);
            }

            List<string> unserved = new List<string>();
            foreach (Type surrogated in ProtobufUnityModel.Surrogated)
            {
                if (!rootServed.Contains(surrogated))
                {
                    unserved.Add(surrogated.Name);
                }
            }

            Assert.IsEmpty(
                unserved,
                "ProtobufUnityModel routes these through a protobuf-net surrogate and nothing serves "
                    + "them at the WallstopProto root, so a root serialization falls through to "
                    + "protobuf-net -- which under IL2CPP either throws or silently returns a default "
                    + "value (issue #696): "
                    + string.Join(", ", unserved)
            );
        }

        /// <summary>
        /// A root marshal must stay invisible to the member path.
        /// </summary>
        /// <remarks>
        /// <see cref="WProtoGeneric{T}"/> resolves a member whose type a closure decides through
        /// <see cref="WProtoFormatterProvider"/>. A marshal registered there would rewrite the
        /// encoding of every such member, where the shipped bytes are the surrogate's. The surrogate
        /// itself is what belongs in that provider, and it is still there.
        /// </remarks>
        [Test]
        public void AMarshalledStructIsNotVisibleToTheContractFormatterProvider()
        {
            Assert.IsFalse(WProtoFormatterProvider.IsRegistered<Vector2>());
            Assert.IsFalse(WProtoFormatterProvider.IsRegistered<Bounds>());
            Assert.IsFalse(WProtoFormatterProvider.IsRegistered<Resolution>());
            Assert.IsFalse(WProtoFormatterProvider.IsRegistered<ImmutableBitSet>());

            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<Vector2>());
            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<Bounds>());
            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<Resolution>());
            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<ImmutableBitSet>());
        }

        private static IEnumerable<TestCaseData> SurrogatedRootCases()
        {
            yield return Case(
                nameof(Vector2),
                new Vector2(1.5f, -2.25f),
                (Vector2 expected, Vector2 actual) =>
                    expected.x == actual.x && expected.y == actual.y
            );
            yield return Case(
                nameof(Vector3),
                new Vector3(1.5f, -2.25f, 8f),
                (Vector3 expected, Vector3 actual) =>
                    expected.x == actual.x && expected.y == actual.y && expected.z == actual.z
            );
            yield return Case(
                nameof(Quaternion),
                new Quaternion(0.125f, 0.25f, 0.5f, 0.75f),
                (Quaternion expected, Quaternion actual) =>
                    expected.x == actual.x
                    && expected.y == actual.y
                    && expected.z == actual.z
                    && expected.w == actual.w
            );
            yield return Case(
                nameof(Color),
                new Color(0.25f, 0.5f, 0.75f, 1f),
                (Color expected, Color actual) =>
                    expected.r == actual.r
                    && expected.g == actual.g
                    && expected.b == actual.b
                    && expected.a == actual.a
            );
            yield return Case(
                nameof(Color32),
                new Color32(10, 20, 30, 40),
                (Color32 expected, Color32 actual) =>
                    expected.r == actual.r
                    && expected.g == actual.g
                    && expected.b == actual.b
                    && expected.a == actual.a
            );
            yield return Case(
                nameof(Rect),
                new Rect(1.5f, -2.25f, 3f, 4f),
                (Rect expected, Rect actual) =>
                    expected.x == actual.x
                    && expected.y == actual.y
                    && expected.width == actual.width
                    && expected.height == actual.height
            );
            yield return Case(
                nameof(RectInt),
                new RectInt(-1, 2, 3, 4),
                (RectInt expected, RectInt actual) =>
                    expected.x == actual.x
                    && expected.y == actual.y
                    && expected.width == actual.width
                    && expected.height == actual.height
            );
            yield return Case(
                nameof(Bounds),
                new Bounds(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f)),
                (Bounds expected, Bounds actual) =>
                    expected.center.Equals(actual.center) && expected.size.Equals(actual.size)
            );
            yield return Case(
                nameof(BoundsInt),
                new BoundsInt(-1, 2, -3, 4, 5, 6),
                (BoundsInt expected, BoundsInt actual) =>
                    expected.position.Equals(actual.position) && expected.size.Equals(actual.size)
            );
            yield return Case(
                nameof(Vector2Int),
                new Vector2Int(5, -3),
                (Vector2Int expected, Vector2Int actual) =>
                    expected.x == actual.x && expected.y == actual.y
            );
            yield return Case(
                nameof(Vector3Int),
                new Vector3Int(5, -3, 7),
                (Vector3Int expected, Vector3Int actual) =>
                    expected.x == actual.x && expected.y == actual.y && expected.z == actual.z
            );
            yield return Case(
                nameof(Resolution),
                new Resolution { width = 1920, height = 1080 },
                /*
                    Unity 2022.2 replaced refreshRate; both serializers restore only width and height through
                    the shared surrogate.
                */
                (Resolution expected, Resolution actual) =>
                    expected.width == actual.width && expected.height == actual.height
            );
            yield return Case(
                nameof(Parabola),
                new Parabola(maxHeight: 3f, length: 8f),
                (Parabola expected, Parabola actual) => expected.Equals(actual)
            );
            yield return Case(
                nameof(ImmutableBitSet),
                BitSetOf(0, 63, 64, 200),
                // Assert capacity independently: Unity 6000.5 previously lost it silently despite a populated payload.
                (ImmutableBitSet expected, ImmutableBitSet actual) =>
                    expected.Capacity == actual.Capacity && expected.Equals(actual)
            );
            yield return Case(
                nameof(FastVector2Int),
                new FastVector2Int(5, -3),
                (FastVector2Int expected, FastVector2Int actual) =>
                    expected.x == actual.x && expected.y == actual.y
            );
            yield return Case(
                nameof(FastVector3Int),
                new FastVector3Int(5, -3, 7),
                (FastVector3Int expected, FastVector3Int actual) =>
                    expected.x == actual.x && expected.y == actual.y && expected.z == actual.z
            );
        }

        /// <summary>
        /// Wraps one type's round trip as a case NUnit can name and run on its own.
        /// </summary>
        /// <typeparam name="T">The surrogated struct being round-tripped.</typeparam>
        /// <param name="name">The case name, which is the type's.</param>
        /// <param name="value">A value whose members are all non-default.</param>
        /// <param name="matches">How this type decides two values are the same.</param>
        /// <returns>The case.</returns>
        /// <remarks>
        /// A delegate rather than the value itself, because <c>Serializer.ProtoSerialize</c> is
        /// generic and the whole point is to close it over each struct in source rather than through
        /// a reflective call the AOT compiler cannot see.
        /// </remarks>
        private static TestCaseData Case<T>(string name, T value, Func<T, T, bool> matches)
        {
            return new TestCaseData((Action)(() => AssertRoundTrips(name, value, matches))).SetName(
                "EverySurrogatedStructRoundTripsAtTheRoot." + name
            );
        }

        private static void AssertRoundTrips<T>(string name, T value, Func<T, T, bool> matches)
        {
            byte[] bytes = Serializer.ProtoSerialize(value);

            Assert.IsTrue(bytes != null, name + " serialized to nothing at all.");
            Assert.Less(
                0,
                bytes.Length,
                name
                    + " serialized to an empty payload, so every member was dropped before a byte "
                    + "was written."
            );

            T restored = Serializer.ProtoDeserialize<T>(bytes);

            Assert.IsTrue(
                matches(value, restored),
                name
                    + " did not survive a root round trip: wrote "
                    + value
                    + ", read back "
                    + restored
                    + ". A default-looking value here is issue #696 -- the root fell through to "
                    + "protobuf-net, which has no AOT code for it."
            );
        }

        private static ImmutableBitSet BitSetOf(params int[] setBits)
        {
            BitSet builder = new BitSet(256);
            foreach (int bit in setBits)
            {
                Assert.IsTrue(builder.TrySet(bit));
            }

            return builder.ToImmutable();
        }
    }
}
