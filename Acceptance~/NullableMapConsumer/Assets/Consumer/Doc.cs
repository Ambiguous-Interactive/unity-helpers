// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace NestedOnlyConsumer
{
    using System;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
#pragma warning disable WPROTO030 // This consumer deliberately uses the legacy runtime backend.
    [ProtoContract]
    public sealed class Doc
    {
        [ProtoMember(1)]
        public int Revision;

        [ProtoMember(7)]
        public SerializableDictionary<Guid, OwnedMessage> Plain;

        [ProtoMember(11)]
        public SerializableSortedDictionary<Guid, OwnedMessage> Sorted;

        [ProtoMember(17)]
        public SerializableDictionary<int, int?, SerializableDictionary.Cache<int?>> Cached;

        [ProtoMember(23)]
        public SerializableSortedDictionary<
            int,
            int?,
            SerializableDictionary.Cache<int?>
        > SortedCached;
    }
#pragma warning restore WPROTO030
}
