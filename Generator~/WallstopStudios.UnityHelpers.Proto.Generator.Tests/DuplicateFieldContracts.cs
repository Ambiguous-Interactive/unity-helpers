// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// The sub-message a duplicated field merges into, with two members so each occurrence can
    /// carry a different one.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class DuplicateChild
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int A;

        [ProtoMember(2)]
        [WProtoMember(2)]
        public int B;

        [ProtoMember(3)]
        [WProtoMember(3)]
        public string Text;
    }

    /// <summary>
    /// Carries a non-repeated scalar, a reference sub-message and a struct sub-message, so one
    /// payload can duplicate each of the three shapes a merge has to answer for.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class DuplicateHolder
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Number;

        [ProtoMember(2)]
        [WProtoMember(2)]
        public DuplicateChild Child;

        [ProtoMember(3)]
        [WProtoMember(3)]
        public Outer.Point Where;
    }

    /// <summary>
    /// The same shape with a sub-message its constructor has already filled in, so what the FIRST
    /// occurrence does to a value that is not null is visible.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class SeededHolder
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Number;

        [ProtoMember(2)]
        [WProtoMember(2)]
        public DuplicateChild Child = new DuplicateChild { A = 9 };
    }

    /// <summary>One level further out, so a merge can be proven to recurse.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class DuplicateGrandparent
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public DuplicateHolder Holder;
    }
}
