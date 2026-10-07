// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace NestedOnlyConsumer
{
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

#pragma warning disable WPROTO030
    [ProtoContract]
    public sealed class PresenceDoc
    {
        [ProtoMember(1)]
        public SerializableDictionary<int, int?> PlainInt;

        [ProtoMember(2)]
        public SerializableDictionary<int, PresenceEnum?> PlainEnum;

        [ProtoMember(3)]
        public SerializableDictionary<int, PresenceValue?> PlainStruct;

        [ProtoMember(4)]
        public SerializableSortedDictionary<int, int?> SortedInt;

        [ProtoMember(5)]
        public SerializableSortedDictionary<int, PresenceEnum?> SortedEnum;

        [ProtoMember(6)]
        public SerializableSortedDictionary<int, PresenceValue?> SortedStruct;

        [ProtoMember(7)]
        public SerializableDictionary<int, int?, SerializableDictionary.Cache<int?>> CachedInt;

        [ProtoMember(8)]
        public SerializableDictionary<
            int,
            PresenceEnum?,
            SerializableDictionary.Cache<PresenceEnum?>
        > CachedEnum;

        [ProtoMember(9)]
        public SerializableDictionary<
            int,
            PresenceValue?,
            SerializableDictionary.Cache<PresenceValue?>
        > CachedStruct;

        [ProtoMember(10)]
        public SerializableSortedDictionary<
            int,
            int?,
            SerializableDictionary.Cache<int?>
        > SortedCachedInt;

        [ProtoMember(11)]
        public SerializableSortedDictionary<
            int,
            PresenceEnum?,
            SerializableDictionary.Cache<PresenceEnum?>
        > SortedCachedEnum;

        [ProtoMember(12)]
        public SerializableSortedDictionary<
            int,
            PresenceValue?,
            SerializableDictionary.Cache<PresenceValue?>
        > SortedCachedStruct;

        [ProtoMember(13)]
        public Dictionary<int, int?> ClrInt;

        [ProtoMember(14)]
        public Dictionary<int, PresenceEnum?> ClrEnum;

        [ProtoMember(15)]
        public Dictionary<int, PresenceValue?> ClrStruct;

        [ProtoMember(16)]
        public SerializableDictionary<int?, int> PlainKeyInt;

        [ProtoMember(17)]
        public SerializableDictionary<PresenceEnum?, int> PlainKeyEnum;

        [ProtoMember(18)]
        public SerializableDictionary<PresenceValue?, int> PlainKeyStruct;

        [ProtoMember(19)]
        public SerializableDictionary<int?, int, SerializableDictionary.Cache<int>> CachedKeyInt;

        [ProtoMember(20)]
        public SerializableDictionary<
            PresenceEnum?,
            int,
            SerializableDictionary.Cache<int>
        > CachedKeyEnum;

        [ProtoMember(21)]
        public SerializableDictionary<
            PresenceValue?,
            int,
            SerializableDictionary.Cache<int>
        > CachedKeyStruct;

        [ProtoMember(22)]
        public Dictionary<int?, int> ClrKeyInt;

        [ProtoMember(23)]
        public Dictionary<PresenceEnum?, int> ClrKeyEnum;

        [ProtoMember(24)]
        public Dictionary<PresenceValue?, int> ClrKeyStruct;
    }
#pragma warning restore WPROTO030
}
