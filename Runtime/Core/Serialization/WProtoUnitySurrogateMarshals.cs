// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Math;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    // A [WProtoSurrogate] registration teaches the generator to substitute the surrogate for a
    // MEMBER, and says nothing about the root. The generated formatter is
    // IWProtoFormatter<XSurrogate> and never IWProtoFormatter<X>, so WProtoFacade answers "not mine"
    // for a root X and Serializer falls through to protobuf-net -- which instantiates
    // ProtoBuf.Internal.StructValueChecker<X>, a closed generic nothing in this package names, so
    // IL2CPP emits no code for it. Measured on issue #696: Unity 2021.3.45f1 throws
    // ExecutionEngineException and Unity 6000.5.2f1 silently returns a default value, which is a
    // save file that loads back empty. Exactly the failure WProtoValueTupleMarshals removed for
    // ValueTuple, now for the structs ProtobufUnityModel routes through a surrogate.
    //
    // FastVector2Int and FastVector3Int are the two deliberate absences: they already have a root,
    // through the hand-written formatters WProtoBuiltInFormatters registers, which recompute the
    // cached hash instead of trusting it from the wire.
    //
    // The bytes are the surrogate's generated formatter's, by construction -- the same formatter the
    // member path already runs -- so a root and a member cannot drift apart, and
    // WProtoSurrogateParityTests already pins that formatter against protobuf-net's output for every
    // type here.

    /// <summary>
    /// Serves a <typeparamref name="TReal"/> root through the formatter generated for its
    /// <typeparamref name="TSurrogate"/>.
    /// </summary>
    /// <typeparam name="TReal">The type being serialized.</typeparam>
    /// <typeparam name="TSurrogate">The <c>[WProtoContract]</c> stand-in that carries its shape.</typeparam>
    /// <remarks>
    /// <para>
    /// The shared half of every marshal below, written once. Each subclass supplies only what
    /// differs: the surrogate's formatter and the two conversions, which are the surrogate's own
    /// implicit operators rather than a second hand-rolled field copy that could disagree with them.
    /// </para>
    /// <para>
    /// <b>The subclasses are what get registered, and they are non-generic on purpose.</b>
    /// <c>[assembly: WProtoRootMarshal(typeof(Vector2), typeof(SurrogateMarshalFormatter&lt;Vector2,
    /// Vector2Surrogate&gt;))]</c> does not compile: the generator normalizes a marshal's formatter
    /// to its unbound definition, so it sees arity 2 against <c>Vector2</c>'s arity 0 and reports
    /// <c>WPROTO019</c>, and the registrar it would emit names the definition's type parameters
    /// rather than the arguments. A closed generic reached through a subclass's base clause has none
    /// of that problem and is still named in metadata, which is the property IL2CPP needs.
    /// </para>
    /// </remarks>
    internal abstract class SurrogateMarshalFormatter<TReal, TSurrogate> : IWProtoFormatter<TReal>
    {
        private readonly IWProtoFormatter<TSurrogate> _surrogateFormatter;

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="SurrogateMarshalFormatter{TReal, TSurrogate}"/> class.
        /// </summary>
        /// <param name="surrogateFormatter">The surrogate's generated formatter.</param>
        protected SurrogateMarshalFormatter(IWProtoFormatter<TSurrogate> surrogateFormatter)
        {
            _surrogateFormatter = surrogateFormatter;
        }

        /// <inheritdoc />
        public int Measure(in TReal value)
        {
            return _surrogateFormatter.Measure(ToSurrogate(value));
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in TReal value)
        {
            return _surrogateFormatter.Write(ref writer, ToSurrogate(value));
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out TReal value)
        {
            if (!_surrogateFormatter.TryRead(ref reader, out TSurrogate surrogate))
            {
                value = default;
                return false;
            }

            value = FromSurrogate(surrogate);
            return true;
        }

        /// <summary>
        /// Converts <paramref name="value"/> to the shape the surrogate's formatter writes.
        /// </summary>
        /// <param name="value">The value being serialized.</param>
        /// <returns>The surrogate carrying the same state.</returns>
        protected abstract TSurrogate ToSurrogate(in TReal value);

        /// <summary>
        /// Converts a decoded <paramref name="surrogate"/> back to the type the caller asked for.
        /// </summary>
        /// <param name="surrogate">The surrogate the formatter read.</param>
        /// <returns>The restored value.</returns>
        protected abstract TReal FromSurrogate(in TSurrogate surrogate);
    }

    /// <summary>Serializes a <see cref="Vector2"/> root through its surrogate.</summary>
    internal sealed class Vector2MarshalFormatter
        : SurrogateMarshalFormatter<Vector2, Vector2Surrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="Vector2MarshalFormatter"/> class.</summary>
        public Vector2MarshalFormatter()
            : base(Vector2Surrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override Vector2Surrogate ToSurrogate(in Vector2 value) => value;

        /// <inheritdoc />
        protected override Vector2 FromSurrogate(in Vector2Surrogate surrogate) => surrogate;
    }

    /// <summary>Serializes a <see cref="Vector3"/> root through its surrogate.</summary>
    internal sealed class Vector3MarshalFormatter
        : SurrogateMarshalFormatter<Vector3, Vector3Surrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="Vector3MarshalFormatter"/> class.</summary>
        public Vector3MarshalFormatter()
            : base(Vector3Surrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override Vector3Surrogate ToSurrogate(in Vector3 value) => value;

        /// <inheritdoc />
        protected override Vector3 FromSurrogate(in Vector3Surrogate surrogate) => surrogate;
    }

    /// <summary>Serializes a <see cref="Quaternion"/> root through its surrogate.</summary>
    internal sealed class QuaternionMarshalFormatter
        : SurrogateMarshalFormatter<Quaternion, QuaternionSurrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="QuaternionMarshalFormatter"/> class.</summary>
        public QuaternionMarshalFormatter()
            : base(QuaternionSurrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override QuaternionSurrogate ToSurrogate(in Quaternion value) => value;

        /// <inheritdoc />
        protected override Quaternion FromSurrogate(in QuaternionSurrogate surrogate) => surrogate;
    }

    /// <summary>Serializes a <see cref="Color"/> root through its surrogate.</summary>
    internal sealed class ColorMarshalFormatter : SurrogateMarshalFormatter<Color, ColorSurrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="ColorMarshalFormatter"/> class.</summary>
        public ColorMarshalFormatter()
            : base(ColorSurrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override ColorSurrogate ToSurrogate(in Color value) => value;

        /// <inheritdoc />
        protected override Color FromSurrogate(in ColorSurrogate surrogate) => surrogate;
    }

    /// <summary>Serializes a <see cref="Color32"/> root through its surrogate.</summary>
    internal sealed class Color32MarshalFormatter
        : SurrogateMarshalFormatter<Color32, Color32Surrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="Color32MarshalFormatter"/> class.</summary>
        public Color32MarshalFormatter()
            : base(Color32Surrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override Color32Surrogate ToSurrogate(in Color32 value) => value;

        /// <inheritdoc />
        protected override Color32 FromSurrogate(in Color32Surrogate surrogate) => surrogate;
    }

    /// <summary>Serializes a <see cref="Rect"/> root through its surrogate.</summary>
    internal sealed class RectMarshalFormatter : SurrogateMarshalFormatter<Rect, RectSurrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="RectMarshalFormatter"/> class.</summary>
        public RectMarshalFormatter()
            : base(RectSurrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override RectSurrogate ToSurrogate(in Rect value) => value;

        /// <inheritdoc />
        protected override Rect FromSurrogate(in RectSurrogate surrogate) => surrogate;
    }

    /// <summary>Serializes a <see cref="RectInt"/> root through its surrogate.</summary>
    internal sealed class RectIntMarshalFormatter
        : SurrogateMarshalFormatter<RectInt, RectIntSurrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="RectIntMarshalFormatter"/> class.</summary>
        public RectIntMarshalFormatter()
            : base(RectIntSurrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override RectIntSurrogate ToSurrogate(in RectInt value) => value;

        /// <inheritdoc />
        protected override RectInt FromSurrogate(in RectIntSurrogate surrogate) => surrogate;
    }

    /// <summary>Serializes a <see cref="Bounds"/> root through its surrogate.</summary>
    internal sealed class BoundsMarshalFormatter
        : SurrogateMarshalFormatter<Bounds, BoundsSurrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="BoundsMarshalFormatter"/> class.</summary>
        public BoundsMarshalFormatter()
            : base(BoundsSurrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override BoundsSurrogate ToSurrogate(in Bounds value) => value;

        /// <inheritdoc />
        protected override Bounds FromSurrogate(in BoundsSurrogate surrogate) => surrogate;
    }

    /// <summary>Serializes a <see cref="BoundsInt"/> root through its surrogate.</summary>
    internal sealed class BoundsIntMarshalFormatter
        : SurrogateMarshalFormatter<BoundsInt, BoundsIntSurrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="BoundsIntMarshalFormatter"/> class.</summary>
        public BoundsIntMarshalFormatter()
            : base(BoundsIntSurrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override BoundsIntSurrogate ToSurrogate(in BoundsInt value) => value;

        /// <inheritdoc />
        protected override BoundsInt FromSurrogate(in BoundsIntSurrogate surrogate) => surrogate;
    }

    /// <summary>Serializes a <see cref="Vector2Int"/> root through its surrogate.</summary>
    internal sealed class Vector2IntMarshalFormatter
        : SurrogateMarshalFormatter<Vector2Int, Vector2IntSurrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="Vector2IntMarshalFormatter"/> class.</summary>
        public Vector2IntMarshalFormatter()
            : base(Vector2IntSurrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override Vector2IntSurrogate ToSurrogate(in Vector2Int value) => value;

        /// <inheritdoc />
        protected override Vector2Int FromSurrogate(in Vector2IntSurrogate surrogate) => surrogate;
    }

    /// <summary>Serializes a <see cref="Vector3Int"/> root through its surrogate.</summary>
    internal sealed class Vector3IntMarshalFormatter
        : SurrogateMarshalFormatter<Vector3Int, Vector3IntSurrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="Vector3IntMarshalFormatter"/> class.</summary>
        public Vector3IntMarshalFormatter()
            : base(Vector3IntSurrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override Vector3IntSurrogate ToSurrogate(in Vector3Int value) => value;

        /// <inheritdoc />
        protected override Vector3Int FromSurrogate(in Vector3IntSurrogate surrogate) => surrogate;
    }

    /// <summary>Serializes a <see cref="Resolution"/> root through its surrogate.</summary>
    /// <remarks>
    /// The conversion to the surrogate is obsolete rather than wrong: it reads
    /// <c>Resolution.refreshRate</c>, which Unity retired at 2022.2 in favour of
    /// <c>refreshRateRatio</c>. The surrogate keeps writing the integer field so a payload written by
    /// an earlier build still reads, which is the same reason protobuf-net's path calls the same
    /// operator.
    /// </remarks>
    internal sealed class ResolutionMarshalFormatter
        : SurrogateMarshalFormatter<Resolution, ResolutionSurrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="ResolutionMarshalFormatter"/> class.</summary>
        public ResolutionMarshalFormatter()
            : base(ResolutionSurrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
#pragma warning disable CS0618 // The surrogate conversion is deliberately obsolete, not wrong.
        protected override ResolutionSurrogate ToSurrogate(in Resolution value) => value;
#pragma warning restore CS0618

        /// <inheritdoc />
        protected override Resolution FromSurrogate(in ResolutionSurrogate surrogate) => surrogate;
    }

    /// <summary>Serializes a <see cref="Parabola"/> root through its surrogate.</summary>
    internal sealed class ParabolaMarshalFormatter
        : SurrogateMarshalFormatter<Parabola, ParabolaSurrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="ParabolaMarshalFormatter"/> class.</summary>
        public ParabolaMarshalFormatter()
            : base(ParabolaSurrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override ParabolaSurrogate ToSurrogate(in Parabola value) => value;

        /// <inheritdoc />
        protected override Parabola FromSurrogate(in ParabolaSurrogate surrogate) => surrogate;
    }

    /// <summary>Serializes an <see cref="ImmutableBitSet"/> root through its surrogate.</summary>
    /// <remarks>
    /// The one type here whose bytes are not protobuf-net's: its surrogate holds a repeated
    /// <c>ulong</c>, and WallstopProto writes a repeated scalar as one packed run where protobuf-net
    /// writes a field key per element. That difference already ships on the member path, each reader
    /// accepts the other's spelling, and <c>WProtoSurrogateParityTests</c> asserts both directions.
    /// </remarks>
    internal sealed class ImmutableBitSetMarshalFormatter
        : SurrogateMarshalFormatter<ImmutableBitSet, ImmutableBitSetSurrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="ImmutableBitSetMarshalFormatter"/> class.</summary>
        public ImmutableBitSetMarshalFormatter()
            : base(ImmutableBitSetSurrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override ImmutableBitSetSurrogate ToSurrogate(in ImmutableBitSet value) => value;

        /// <inheritdoc />
        protected override ImmutableBitSet FromSurrogate(in ImmutableBitSetSurrogate surrogate) =>
            surrogate;
    }
}
