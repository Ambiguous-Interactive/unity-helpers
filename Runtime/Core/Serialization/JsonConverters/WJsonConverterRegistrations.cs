// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using WallstopStudios.UnityHelpers.Core.DataStructure;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
using WallstopStudios.UnityHelpers.Core.Math;
using WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters;

// Generated converters make consumer generic closures usable on IL2CPP; opt out only when player JSON never needs them.

// Assembly declarations let the generator register closed types found in consumer code.

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
