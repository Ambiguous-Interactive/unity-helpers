// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
#if !WALLSTOP_PROTO_ONLY
    using System.IO;
    using ProtoBuf.Serializers;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    internal sealed class SerializableSortedDictionaryCollectionWrapper<TKey, TValue>
        : CollectionWrapperSerializer<SerializableSortedDictionary<TKey, TValue>, TKey, TValue>
        where TKey : System.IComparable<TKey>
    {
        protected override void Prepare(
            SerializableSortedDictionary<TKey, TValue> collection,
            out TKey[] keys,
            out TValue[] values,
            out byte[] presence
        )
        {
            collection.OnBeforeSerialize();
            keys = collection.SerializedKeys;
            values = collection.SerializedValues;
            presence = null;
            NullableCollectionPresence<TValue>.Prepare(keys?.Length ?? 0, ref values, ref presence);
        }

        protected override SerializableSortedDictionary<TKey, TValue> Restore(
            TKey[] keys,
            TValue[] values,
            byte[] presence
        )
        {
            if (
                !NullableCollectionPresence<TValue>.TryRestore(
                    keys?.Length ?? 0,
                    ref values,
                    presence
                )
            )
            {
                throw new InvalidDataException(
                    "Nullable dictionary presence bitmap does not match its arrays."
                );
            }
            SerializableSortedDictionary<TKey, TValue> result = new()
            {
                _keys = keys,
                _values = values,
                _preserveSerializedEntries = true,
            };
            result.OnAfterDeserialize();
            return result;
        }
    }
#endif
}
