// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;

    /// <summary>
    /// Declares that a generic type is serialized to JSON by a generic converter, so the source
    /// generator can construct that converter for every closure a build actually uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because <see cref="System.Text.Json.Serialization.JsonConverterFactory"/> is
    /// unusable under IL2CPP for a closure nothing names. A factory builds its converter with
    /// <c>typeof(SomeConverter&lt;&gt;).MakeGenericType(arguments)</c> followed by
    /// <see cref="Activator.CreateInstance(Type)"/>, and IL2CPP compiles only the closures it can
    /// see statically -- so <c>SomeConverter&lt;TheirStruct&gt;</c> does not exist in the player and
    /// its constructor throws <c>ExecutionEngineException</c> the first time a save is written.
    /// The closures this package's own tests exercise are compiled because this package names them;
    /// a consumer's own closure has nothing naming it, which is why the failure reaches a shipped
    /// game rather than CI.
    /// </para>
    /// <para>
    /// The pairing is declared at assembly level for the same reason
    /// <see cref="WallstopProto.WProtoRootMarshalAttribute"/> is: enumerating assembly attributes is
    /// the one cross-reference lookup that costs nothing, so a consumer's build discovers the
    /// converters this package ships without naming any of them. Both types must be unbound generics
    /// of the same arity. The generator finds each closed construction written in the consumer's
    /// source, closes the converter over the same arguments, and registers the instance with
    /// <see cref="WJsonConverterRegistry"/>; every factory here asks that registry before it reaches
    /// for reflection, so the reflective path remains only as the editor and Mono fallback it was
    /// always safe on.
    /// </para>
    /// <example>
    /// <code>
    /// [assembly: WJsonConverter(
    ///     typeof(Deque&lt;&gt;),
    ///     typeof(DequeConverterFactory.DequeConverter&lt;&gt;))]
    /// </code>
    /// </example>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class WJsonConverterAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WJsonConverterAttribute"/> class.
        /// </summary>
        /// <param name="serializedType">The unbound generic definition being serialized.</param>
        /// <param name="converterType">
        /// The unbound generic definition of a <c>JsonConverter&lt;T&gt;</c> for it, of the same
        /// arity and with a public parameterless constructor.
        /// </param>
        public WJsonConverterAttribute(Type serializedType, Type converterType)
        {
            SerializedType = serializedType;
            ConverterType = converterType;
        }

        /// <summary>The unbound generic definition being serialized.</summary>
        public Type SerializedType { get; }

        /// <summary>The unbound generic definition of the converter that serializes it.</summary>
        public Type ConverterType { get; }
    }
}
