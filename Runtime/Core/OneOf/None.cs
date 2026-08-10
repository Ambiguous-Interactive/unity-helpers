// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.OneOf
{
    using System;
    using System.Runtime.CompilerServices;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [Serializable]
    [ProtoContract]
    [WProtoContract]
    public readonly partial struct None : IEquatable<None>
    {
        public static readonly None Default = default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(None other) => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is None;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => 0;

        public override string ToString() => "None";

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(None left, None right) => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(None left, None right) => false;
    }
}
