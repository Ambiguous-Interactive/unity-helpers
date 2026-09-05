// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>A chain whose ROOT declares all four hooks: the placement every reader agrees on.</summary>
    [ProtoContract]
    [ProtoInclude(100, typeof(HookLeaf))]
    [WProtoContract]
    [WProtoInclude(100, typeof(HookLeaf))]
    public partial class HookRoot
    {
        /// <summary>A base member, so the root has a wire shape of its own.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Id;

        [ProtoBeforeSerialization]
        [WProtoBeforeSerialization]
        private void RootBeforeSerialization()
        {
            HookDifferentialTests.Record("Root.BeforeSer");
        }

        [ProtoAfterSerialization]
        [WProtoAfterSerialization]
        private void RootAfterSerialization()
        {
            HookDifferentialTests.Record("Root.AfterSer");
        }

        [ProtoBeforeDeserialization]
        [WProtoBeforeDeserialization]
        private void RootBeforeDeserialization()
        {
            HookDifferentialTests.Record("Root.BeforeDes");
        }

        [ProtoAfterDeserialization]
        [WProtoAfterDeserialization]
        private void RootAfterDeserialization()
        {
            HookDifferentialTests.Record("Root.AfterDes");
        }
    }

    /// <summary>The leaf under <see cref="HookRoot"/>, declaring no hook of its own.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial class HookLeaf : HookRoot
    {
        /// <summary>The subtype's own member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int LeafValue;
    }

    /// <summary>A chain whose root declares nothing, so only the subtype has hooks to run.</summary>
    [ProtoContract]
    [ProtoInclude(100, typeof(SubHookLeaf))]
    [WProtoContract]
    [WProtoInclude(100, typeof(SubHookLeaf))]
    public partial class SubHookRoot
    {
        /// <summary>A base member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Id;
    }

    // These fixtures intentionally trigger WPROTO034 to measure the reported hook divergence.
#pragma warning disable WPROTO034

    /// <summary>The subtype protobuf-net 3.2.56 runs no callback on.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial class SubHookLeaf : SubHookRoot
    {
        /// <summary>The subtype's own member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int LeafValue;

        [ProtoBeforeSerialization]
        [WProtoBeforeSerialization]
        private void LeafBeforeSerialization()
        {
            HookDifferentialTests.Record("Leaf.BeforeSer");
        }

        [ProtoAfterSerialization]
        [WProtoAfterSerialization]
        private void LeafAfterSerialization()
        {
            HookDifferentialTests.Record("Leaf.AfterSer");
        }

        [ProtoBeforeDeserialization]
        [WProtoBeforeDeserialization]
        private void LeafBeforeDeserialization()
        {
            HookDifferentialTests.Record("Leaf.BeforeDes");
        }

        [ProtoAfterDeserialization]
        [WProtoAfterDeserialization]
        private void LeafAfterDeserialization()
        {
            HookDifferentialTests.Record("Leaf.AfterDes");
        }
    }

    /// <summary>Three levels, one hook each, so the ORDER is observable and not only the set.</summary>
    [ProtoContract]
    [ProtoInclude(100, typeof(EveryLevelMiddle))]
    [WProtoContract]
    [WProtoInclude(100, typeof(EveryLevelMiddle))]
    public partial class EveryLevelRoot
    {
        /// <summary>The outermost member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Id;

        [ProtoAfterDeserialization]
        [WProtoAfterDeserialization]
        private void RootAfterDeserialization()
        {
            HookDifferentialTests.Record("Root.AfterDes");
        }
    }

    /// <summary>The middle level, which is both a subtype and a base.</summary>
    [ProtoContract]
    [ProtoInclude(101, typeof(EveryLevelLeaf))]
    [WProtoContract]
    [WProtoInclude(101, typeof(EveryLevelLeaf))]
    public partial class EveryLevelMiddle : EveryLevelRoot
    {
        /// <summary>The middle member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int MiddleValue;

        [ProtoAfterDeserialization]
        [WProtoAfterDeserialization]
        private void MiddleAfterDeserialization()
        {
            HookDifferentialTests.Record("Middle.AfterDes");
        }
    }

    /// <summary>The innermost level.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial class EveryLevelLeaf : EveryLevelMiddle
    {
        /// <summary>The innermost member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int LeafValue;

        [ProtoAfterDeserialization]
        [WProtoAfterDeserialization]
        private void LeafAfterDeserialization()
        {
            HookDifferentialTests.Record("Leaf.AfterDes");
        }
    }

#pragma warning restore WPROTO034

    /// <summary>A SkipConstructor contract with no chain, which every reader runs every hook on.</summary>
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public partial class SkippingHookContract
    {
        /// <summary>The only member on the wire.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Value;

        [ProtoBeforeSerialization]
        [WProtoBeforeSerialization]
        private void BeforeSerialization()
        {
            HookDifferentialTests.Record("Skip.BeforeSer");
        }

        [ProtoAfterSerialization]
        [WProtoAfterSerialization]
        private void AfterSerialization()
        {
            HookDifferentialTests.Record("Skip.AfterSer");
        }

        [ProtoBeforeDeserialization]
        [WProtoBeforeDeserialization]
        private void BeforeDeserialization()
        {
            HookDifferentialTests.Record("Skip.BeforeDes");
        }

        [ProtoAfterDeserialization]
        [WProtoAfterDeserialization]
        private void AfterDeserialization()
        {
            HookDifferentialTests.Record("Skip.AfterDes");
        }
    }
}
