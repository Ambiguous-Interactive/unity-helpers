// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Exercises nullable halves of generated map entries.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class NullableMapContract
    {
        /// <summary>Nullable scalar values.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public Dictionary<int, int?> Values;

        /// <summary>Nullable enum values.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public Dictionary<int, ZeroMapKeyKind?> Enums;

        /// <summary>Nullable message values.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public Dictionary<int, Outer.Point?> Points;

        /// <summary>Nullable scalar keys.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public Dictionary<int?, int> Keys;

        /// <summary>Nullable enum keys.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        public Dictionary<ZeroMapKeyKind?, int> EnumKeys;

        /// <summary>Nullable value-message keys.</summary>
        [ProtoMember(6)]
        [WProtoMember(6)]
        public Dictionary<Outer.Point?, int> PointKeys;

        /// <summary>Nullable halves through a dictionary interface.</summary>
        [ProtoMember(7)]
        [WProtoMember(7)]
        public IDictionary<int?, int?> Interface;

        /// <summary>Nullable halves through a read-only interface.</summary>
        [ProtoMember(8)]
        [WProtoMember(8)]
        public IReadOnlyDictionary<int?, int?> ReadOnly;

        /// <summary>Nullable values in a sorted dictionary.</summary>
        [ProtoMember(9)]
        [WProtoMember(9)]
        public SortedDictionary<int, int?> Sorted;

        /// <summary>Nullable halves in a read-only dictionary.</summary>
        [ProtoMember(10)]
        [WProtoMember(10)]
        public ReadOnlyDictionary<int?, int?> Frozen;
    }
}
