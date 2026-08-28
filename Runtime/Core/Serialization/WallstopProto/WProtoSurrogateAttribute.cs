// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Declares that values of one type are serialized through another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is how a type nobody owns gets a wire shape. Unity's <c>Vector3</c>, <c>Color</c> and
    /// <c>Bounds</c> cannot carry <c>[WProtoContract]</c> — they are not ours to annotate — so a
    /// surrogate contract carries it instead and the values are converted at the boundary. The
    /// surrogate's <c>[WProtoMember]</c> numbers define the bytes; the real type contributes
    /// nothing but its values.
    /// </para>
    /// <para>
    /// Applied at <b>assembly</b> level rather than to either type, for two reasons. The real type
    /// is usually in an assembly that cannot reference this one, so it could not carry the attribute
    /// even if we wanted it to. And an assembly attribute is the one thing a source generator can
    /// enumerate cheaply across every referenced assembly — a package's surrogates have to be
    /// visible while generating a consumer's code, and walking every namespace of every reference to
    /// find them would cost more than the whole generator.
    /// </para>
    /// <para>
    /// The surrogate must be a <c>[WProtoContract]</c> and the two types must convert to each other,
    /// by implicit or explicit operator, in both directions. A missing conversion is a build error
    /// naming the pair rather than a formatter that cannot round-trip.
    /// </para>
    /// <para>
    /// An unbound generic real type may name an unbound generic surrogate with the same arity. The
    /// generator closes both over each member's type arguments, so one declaration serves consumer
    /// constructions the assembly declaring the surrogate could not name in advance.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code><![CDATA[
    /// [assembly: WProtoSurrogate(typeof(UnityEngine.Vector3), typeof(Vector3Surrogate))]
    ///
    /// [WProtoContract]
    /// public partial struct Vector3Surrogate
    /// {
    ///     [WProtoMember(1)] public float x;
    ///     [WProtoMember(2)] public float y;
    ///     [WProtoMember(3)] public float z;
    ///
    ///     public static implicit operator UnityEngine.Vector3(Vector3Surrogate value) => new(value.x, value.y, value.z);
    ///     public static implicit operator Vector3Surrogate(UnityEngine.Vector3 value) => new() { x = value.x, y = value.y, z = value.z };
    /// }
    /// ]]></code>
    /// </example>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class WProtoSurrogateAttribute : Attribute
    {
        /// <summary>
        /// Initializes the attribute with the pair it maps.
        /// </summary>
        /// <param name="realType">
        /// The type that appears on contracts, or its unbound generic definition.
        /// </param>
        /// <param name="surrogateType">
        /// The contract that defines its wire shape, or its unbound generic definition.
        /// </param>
        public WProtoSurrogateAttribute(Type realType, Type surrogateType)
        {
            RealType = realType;
            SurrogateType = surrogateType;
        }

        /// <summary>
        /// The type that appears on contracts and has no wire shape of its own.
        /// </summary>
        public Type RealType { get; }

        /// <summary>The <c>[WProtoContract]</c> whose members define the bytes.</summary>
        public Type SurrogateType { get; }
    }
}
