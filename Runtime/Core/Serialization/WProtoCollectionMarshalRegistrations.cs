// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using WallstopStudios.UnityHelpers.Core.DataStructure;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
using WallstopStudios.UnityHelpers.Core.Serialization;
using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

// The seven collections Serializer marshals through a wrapper POCO. They are declared here rather
// than annotated with [WProtoContract] on purpose: annotating them would give each type a SECOND
// wire shape -- its own members as a message -- where the shipped bytes are the wrapper's, and
// WProtoFacade would answer with it. ContractMirrorTests records all seven with that reason.
//
// Assembly level so a CONSUMER's build finds them: their formatters are generic, a registrar cannot
// register an open generic, and Deque<TheirStruct> cannot appear in this package's own sources.
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
