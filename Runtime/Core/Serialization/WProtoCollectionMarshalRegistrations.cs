// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using WallstopStudios.UnityHelpers.Core.DataStructure;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
using WallstopStudios.UnityHelpers.Core.Serialization;
using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

// Assembly registrations preserve wrapper wire shapes and expose open generics to consumer builds.

[assembly: WProtoRootMarshal(
    typeof(SerializableHashSet<>),
    typeof(SerializableHashSetMarshalFormatter<>)
)]
[assembly: WProtoRootMarshal(
    typeof(SerializableSortedSet<>),
    typeof(SerializableSortedSetMarshalFormatter<>)
)]
[assembly: WProtoRootMarshal(
    typeof(SerializableDictionary<,>),
    typeof(SerializableDictionaryMarshalFormatter<,>)
)]
[assembly: WProtoRootMarshal(
    typeof(SerializableSortedDictionary<,>),
    typeof(SerializableSortedDictionaryMarshalFormatter<,>)
)]
[assembly: WProtoRootMarshal(typeof(Deque<>), typeof(DequeMarshalFormatter<>))]
[assembly: WProtoRootMarshal(typeof(CyclicBuffer<>), typeof(CyclicBufferMarshalFormatter<>))]
[assembly: WProtoRootMarshal(typeof(SparseSet), typeof(SparseSetMarshalFormatter))]
