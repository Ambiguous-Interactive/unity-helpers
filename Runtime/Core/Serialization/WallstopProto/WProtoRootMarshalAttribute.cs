// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Declares that a type is encoded as something else when it is the <b>root</b> of a
    /// serialization, and names the formatter that does it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not a surrogate, and the difference is the whole reason it exists.
    /// <see cref="WProtoSurrogateAttribute"/> substitutes a type <b>everywhere</b> -- a member of
    /// the real type is written as the surrogate, and so is the root. A root marshal applies to the
    /// root <b>only</b>, because that is the shape the bytes already have:
    /// <c>Serializer.ProtoSerialize(aSerializableDictionary)</c> has always written a wrapper of
    /// parallel key/value arrays, while the same dictionary held as a member of another contract is
    /// written by protobuf-net's ordinary map handling. Two encodings for one type, decided by
    /// position. Applying a marshal to members would be a silent wire change for every consumer who
    /// has already saved one.
    /// </para>
    /// <para>
    /// <b>The formatter is named rather than generated.</b> A wrapper conversion is not a cast: it
    /// runs the collection's serialization callback, reads the array it stages, and rebuilds the
    /// collection on the other side with a capacity rule of its own. That is hand-written code, and
    /// pointing at it is honest where emitting a guess would not be.
    /// </para>
    /// <para>
    /// <b>Assembly level, and generic-aware.</b> Like <see cref="WProtoSurrogateAttribute"/>, this
    /// is enumerable across every referenced assembly for the cost of one attribute read, which is
    /// what lets a consumer's build discover the marshals this package ships. When the real type is
    /// an unbound generic (<c>typeof(Deque&lt;&gt;)</c>) the formatter type must be an unbound
    /// generic of the same arity (<c>typeof(DequeMarshalFormatter&lt;&gt;)</c>): the generator
    /// closes both with the same type arguments, once per construction it finds in source, because
    /// a registrar cannot register an open generic and <c>MakeGenericType</c> is the one call IL2CPP
    /// cannot compile.
    /// </para>
    /// <example>
    /// <code>
    /// [assembly: WProtoRootMarshal(typeof(Deque&lt;&gt;), typeof(DequeMarshalFormatter&lt;&gt;))]
    /// </code>
    /// </example>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class WProtoRootMarshalAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WProtoRootMarshalAttribute"/> class.
        /// </summary>
        /// <param name="realType">
        /// The type being serialized, or its unbound generic definition.
        /// </param>
        /// <param name="formatterType">
        /// An <see cref="IWProtoFormatter{T}"/> implementation for <paramref name="realType"/>, or
        /// its unbound generic definition when <paramref name="realType"/> is one.
        /// </param>
        public WProtoRootMarshalAttribute(Type realType, Type formatterType)
        {
            RealType = realType;
            FormatterType = formatterType;
        }

        /// <summary>The type being serialized, or its unbound generic definition.</summary>
        public Type RealType { get; }

        /// <summary>The formatter that encodes it, or its unbound generic definition.</summary>
        public Type FormatterType { get; }
    }
}
