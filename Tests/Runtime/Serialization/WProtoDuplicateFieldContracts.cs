// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// The sub-message a duplicated field merges into, with three members so each occurrence of the
    /// field can carry a different one.
    /// </summary>
    [WProtoContract]
    public sealed partial class WProtoDuplicateChild
    {
        /// <summary>Carried by the first occurrence only.</summary>
        [WProtoMember(1)]
        public int A;

        /// <summary>Carried by the second occurrence only.</summary>
        [WProtoMember(2)]
        public int B;

        /// <summary>Carried by both occurrences, so the within-message rule is visible too.</summary>
        [WProtoMember(3)]
        public string Text;
    }

    /// <summary>
    /// Holds a non-repeated scalar, a reference sub-message and a struct sub-message, so one payload
    /// can duplicate each of the three shapes.
    /// </summary>
    [WProtoContract]
    public sealed partial class WProtoDuplicateHolder
    {
        /// <summary>A non-repeated scalar, whose duplicate is last-wins rather than merged.</summary>
        [WProtoMember(1)]
        public int Number;

        /// <summary>A reference sub-message.</summary>
        [WProtoMember(2)]
        public WProtoDuplicateChild Child;

        /// <summary>A struct sub-message, which cannot be null and merges anyway.</summary>
        [WProtoMember(3)]
        public WProtoDuplicatePoint Where;
    }

    /// <summary>A struct sub-message.</summary>
    [WProtoContract]
    public partial struct WProtoDuplicatePoint
    {
        /// <summary>The first component.</summary>
        [WProtoMember(1)]
        public int X;

        /// <summary>The second component.</summary>
        [WProtoMember(2)]
        public int Y;
    }

    /// <summary>One level further out, so a merge can be proven to recurse.</summary>
    [WProtoContract]
    public sealed partial class WProtoDuplicateGrandparent
    {
        /// <summary>The nested holder, itself duplicated by the payloads under test.</summary>
        [WProtoMember(1)]
        public WProtoDuplicateHolder Holder;
    }

    /// <summary>
    /// The holder with a sub-message its constructor has already filled in, so what the FIRST
    /// occurrence does to a value that is not null is visible.
    /// </summary>
    [WProtoContract]
    public sealed partial class WProtoSeededHolder
    {
        /// <summary>A member the payloads under test never mention, so only the seed can set it.</summary>
        [WProtoMember(1)]
        public WProtoDuplicateChild Child = new WProtoDuplicateChild { A = 9 };

        /// <summary>A struct sub-message, which is always seeded because it cannot be null.</summary>
        [WProtoMember(2)]
        public WProtoDuplicatePoint Where = new WProtoDuplicatePoint { X = 9 };
    }

    /// <summary>A generic contract, whose member's encoding only its closure decides.</summary>
    [WProtoContract]
    public sealed partial class WProtoDuplicateBox<T>
    {
        /// <summary>A member typed as the contract's own parameter.</summary>
        [WProtoMember(1)]
        public T Value;
    }

    /// <summary>Names the closures this assembly uses, so the generator registers them.</summary>
    /// <remarks>
    /// A registrar cannot register an open generic and constructing one at runtime needs
    /// <c>MakeGenericType</c>, which IL2CPP cannot compile, so the closures have to appear in source.
    /// </remarks>
    public static class WProtoDuplicateBoxClosures
    {
        /// <summary>A reference-message closure, whose occurrences must merge.</summary>
        public static WProtoDuplicateBox<WProtoDuplicateChild> Children;

        /// <summary>A struct-message closure.</summary>
        public static WProtoDuplicateBox<WProtoDuplicatePoint> Points;

        /// <summary>A length-delimited scalar closure, which must stay last-wins.</summary>
        public static WProtoDuplicateBox<string> Texts;
    }
}
