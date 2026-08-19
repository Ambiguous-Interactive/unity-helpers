// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using WallstopStudios.UnityHelpers.Core.DataStructure;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
using WallstopStudios.UnityHelpers.Core.Math;
using WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters;

// OPT-OUT: define WALLSTOP_DISABLE_GENERATED_JSON_CONVERTERS to remove these declarations. They are
// on by default because the alternative is an ExecutionEngineException from the first save in a
// shipped player, but they are not free: the generator emits one converter instance per closed
// construction your build writes, and under IL2CPP each of those is compiled code. Turn them off if
// your build never serializes one of these types to JSON in a player.
//
// Assembly level for the same reason the root marshals are: the converters are generic, nothing can
// register an open generic, and the closures a CONSUMER uses cannot appear in this package's
// sources. Declared here, the generator registers the closed converter for every
// SerializableDictionary<TheirKey, TheirValue> it finds in their build.
#if !WALLSTOP_DISABLE_GENERATED_JSON_CONVERTERS
[assembly: WJsonConverter(typeof(Range<>), typeof(RangeConverterFactory.RangeConverter<>))]
[assembly: WJsonConverter(typeof(Deque<>), typeof(DequeConverterFactory.DequeConverter<>))]
[assembly: WJsonConverter(
    typeof(CyclicBuffer<>),
    typeof(CyclicBufferConverterFactory.CyclicBufferConverter<>)
)]
[assembly: WJsonConverter(
    typeof(SerializableList<>),
    typeof(SerializableListConverterFactory.SerializableListConverter<>)
)]
[assembly: WJsonConverter(
    typeof(SerializableHashSet<>),
    typeof(SerializableSetConverterFactory.SerializableHashSetConverter<>)
)]
[assembly: WJsonConverter(
    typeof(SerializableSortedSet<>),
    typeof(SerializableSetConverterFactory.SerializableSortedSetConverter<>)
)]
[assembly: WJsonConverter(
    typeof(SerializableDictionary<,>),
    typeof(SerializableDictionaryConverterFactory.SerializableDictionaryConverter<,>)
)]
[assembly: WJsonConverter(
    typeof(SerializableDictionary<,,>),
    typeof(SerializableDictionaryConverterFactory.SerializableDictionaryWithCacheConverter<,,>)
)]
[assembly: WJsonConverter(
    typeof(SerializableSortedDictionary<,>),
    typeof(SerializableSortedDictionaryConverterFactory.SerializableSortedDictionaryConverter<,>)
)]
[assembly: WJsonConverter(
    typeof(SerializableSortedDictionary<,,>),
    typeof(SerializableSortedDictionaryConverterFactory.SerializableSortedDictionaryWithCacheConverter<,,>)
)]
[assembly: WJsonConverter(
    typeof(SerializableNullable<>),
    typeof(SerializableNullableJsonConverter<>)
)]
#endif
