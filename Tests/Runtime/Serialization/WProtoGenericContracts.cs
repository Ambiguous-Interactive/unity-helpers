// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A generic contract, whose members' encodings only the closure decides.
    /// </summary>
    /// <remarks>
    /// This is the shape the package's own generic collections have —
    /// <c>SerializableDictionary&lt;TKey, TValue&gt;</c>, <c>SerializableHashSet&lt;T&gt;</c>,
    /// <c>CyclicBuffer&lt;T&gt;</c> — and the one IL2CPP has to AOT-compile per closure. The field
    /// key itself changes with <c>T</c>, so the emitted code asks <c>WProtoGeneric&lt;T&gt;</c>.
    /// </remarks>
    [WProtoContract]
    public partial class WProtoBox<T>
    {
        /// <summary>A member typed as the parameter.</summary>
        [WProtoMember(1)]
        public T Value;

        /// <summary>A repeated member of the parameter.</summary>
        [WProtoMember(2)]
        public T[] Many;

        /// <summary>A scalar, to pin ordering against the generic members.</summary>
        [WProtoMember(3)]
        public int Trailer;
    }

    /// <summary>Names the closures this assembly uses, so the generator registers them.</summary>
    /// <remarks>
    /// A registrar cannot register an open generic, and constructing one at runtime needs
    /// <c>MakeGenericType</c> — the exact call IL2CPP cannot compile. The generator registers the
    /// constructions it can see in source, and this is where these become visible.
    /// </remarks>
    public static class WProtoBoxClosures
    {
        /// <summary>An integer closure, whose members are varints.</summary>
        public static WProtoBox<int> Ints;

        /// <summary>A floating-point closure, whose members are fixed64.</summary>
        public static WProtoBox<double> Doubles;

        /// <summary>A length-delimited closure.</summary>
        public static WProtoBox<string> Texts;

        /// <summary>A message closure.</summary>
        public static WProtoBox<WProtoRepeatedPoint> Points;
    }
}
